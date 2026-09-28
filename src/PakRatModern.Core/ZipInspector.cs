using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PakRatModern.Core
{
    /// <summary>Una entrada tal como la describe el directorio central del ZIP.</summary>
    public sealed class ZipCentralEntry
    {
        public byte[] NameBytes { get; internal set; }
        public ushort Flags { get; internal set; }
        public ushort Method { get; internal set; }
        public uint Crc { get; internal set; }
        public long CompressedSize { get; internal set; }
        public long UncompressedSize { get; internal set; }

        /// <summary>Offset de la cabecera local, ya corregido si el ZIP trae datos delante.</summary>
        public long LocalHeaderOffset { get; internal set; }

        /// <summary>Bit 11: el nombre esta en UTF-8.</summary>
        public bool IsUtf8 => (Flags & 0x0800) != 0;

        public bool IsEncrypted => (Flags & 0x0001) != 0;

        /// <summary>
        /// Nombre decodificado. Sin el flag UTF-8 se lee como Latin-1, que
        /// conserva cada byte tal cual: vbsp y bspzip guardan los bytes crudos
        /// del sistema del mapper y el motor busca esos mismos bytes.
        /// </summary>
        public string Name => (IsUtf8 ? Encoding.UTF8 : ZipInspector.Latin1).GetString(NameBytes);

        public bool IsDirectory => NameBytes.Length > 0 && NameBytes[NameBytes.Length - 1] == (byte)'/';
    }

    /// <summary>
    /// Lector del directorio central de un ZIP.
    ///
    /// El PAK se lee a mano en lugar de con ZipArchive: el de .NET Framework no
    /// verifica el CRC, no expone el metodo ni los flags de cada entrada,
    /// decodifica los nombres con la pagina de codigos del sistema y no sabe
    /// descomprimir LZMA, que es lo que usan los mapas comprimidos con bspzip.
    /// </summary>
    public static class ZipInspector
    {
        private const uint EndOfCentralDirectorySignature = 0x06054b50;
        private const uint CentralFileHeaderSignature = 0x02014b50;
        private const uint Zip64LocatorSignature = 0x07064b50;

        public const ushort MethodStored = 0;
        public const ushort MethodDeflate = 8;
        public const ushort MethodBZip2 = 12;
        public const ushort MethodLzma = 14;

        /// <summary>Codificacion que conserva los bytes 0-255 uno a uno.</summary>
        public static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        private static readonly Dictionary<ushort, string> MethodNames = new Dictionary<ushort, string>
        {
            [0] = "Stored",
            [8] = "Deflate",
            [9] = "Deflate64",
            [12] = "BZip2",
            [14] = "LZMA",
            [95] = "XZ",
            [98] = "PPMd",
        };

        /// <summary>Metodos que este lector descomprime.</summary>
        public static bool IsReadable(ushort method) =>
            method == MethodStored || method == MethodDeflate || method == MethodLzma;

        /// <summary>
        /// Lee el directorio central. Lanza <see cref="InvalidDataException"/> si la
        /// estructura no es coherente y <see cref="NotSupportedException"/> para
        /// Zip64 o ZIP multivolumen, que ninguna herramienta de Source escribe.
        /// </summary>
        public static IReadOnlyList<ZipCentralEntry> ReadCentralDirectory(byte[] zip)
        {
            if (zip == null || zip.Length < 22)
                throw new InvalidDataException("PAK is too small to be a ZIP archive.");

            var eocd = FindEndOfCentralDirectory(zip);
            if (eocd < 0)
                throw new InvalidDataException("PAK has no ZIP end of central directory record.");

            var diskNumber = BitConverter.ToUInt16(zip, eocd + 4);
            var centralDisk = BitConverter.ToUInt16(zip, eocd + 6);
            var entryCount = BitConverter.ToUInt16(zip, eocd + 10);
            long centralSize = BitConverter.ToUInt32(zip, eocd + 12);
            long centralOffset = BitConverter.ToUInt32(zip, eocd + 16);

            if (eocd >= 20 && BitConverter.ToUInt32(zip, eocd - 20) == Zip64LocatorSignature)
                throw new NotSupportedException("This map's PAK is a Zip64 archive, which Source tools never write; it is refused.");

            if (diskNumber != 0 || centralDisk != 0)
                throw new NotSupportedException("This map's PAK is a multi-volume ZIP archive; it is refused.");

            // Igual que zipfile de Python: si el ZIP trae datos delante, todos los
            // offsets quedan corridos en la misma cantidad.
            var concat = eocd - centralSize - centralOffset;
            if (concat < 0)
                throw new InvalidDataException("PAK central directory is out of range.");

            var entries = new List<ZipCentralEntry>(entryCount);
            var pos = centralOffset + concat;
            var end = eocd;

            for (var i = 0; i < entryCount; i++)
            {
                if (pos + 46 > end || BitConverter.ToUInt32(zip, (int)pos) != CentralFileHeaderSignature)
                    throw new InvalidDataException($"PAK central directory entry {i} is damaged.");

                var at = (int)pos;
                var nameLength = BitConverter.ToUInt16(zip, at + 28);
                var extraLength = BitConverter.ToUInt16(zip, at + 30);
                var commentLength = BitConverter.ToUInt16(zip, at + 32);
                var next = pos + 46 + nameLength + extraLength + commentLength;
                if (next > end)
                    throw new InvalidDataException($"PAK central directory entry {i} is damaged.");

                var compressed = BitConverter.ToUInt32(zip, at + 20);
                var uncompressed = BitConverter.ToUInt32(zip, at + 24);
                var localOffset = BitConverter.ToUInt32(zip, at + 42);
                if (compressed == uint.MaxValue || uncompressed == uint.MaxValue || localOffset == uint.MaxValue)
                    throw new NotSupportedException("This map's PAK uses Zip64 entries, which Source tools never write; it is refused.");

                var name = new byte[nameLength];
                Array.Copy(zip, at + 46, name, 0, nameLength);

                entries.Add(new ZipCentralEntry
                {
                    NameBytes = name,
                    Flags = BitConverter.ToUInt16(zip, at + 8),
                    Method = BitConverter.ToUInt16(zip, at + 10),
                    Crc = BitConverter.ToUInt32(zip, at + 16),
                    CompressedSize = compressed,
                    UncompressedSize = uncompressed,
                    LocalHeaderOffset = localOffset + concat,
                });

                pos = next;
            }

            return entries;
        }

        /// <summary>Cantidad de entradas por metodo de compresion. Vacio si el ZIP no se puede leer.</summary>
        public static Dictionary<ushort, int> GetCompressionMethods(byte[] zipBytes)
        {
            var counts = new Dictionary<ushort, int>();
            IReadOnlyList<ZipCentralEntry> entries;
            try { entries = ReadCentralDirectory(zipBytes); }
            catch (Exception ex) when (ex is InvalidDataException || ex is NotSupportedException) { return counts; }

            foreach (var entry in entries)
                counts[entry.Method] = counts.TryGetValue(entry.Method, out var n) ? n + 1 : 1;
            return counts;
        }

        /// <summary>
        /// Devuelve un mensaje explicando el problema, o null si todo el
        /// contenido usa metodos que se pueden leer.
        /// </summary>
        public static string DescribeUnsupported(byte[] zipBytes)
        {
            IReadOnlyList<ZipCentralEntry> entries;
            try { entries = ReadCentralDirectory(zipBytes); }
            catch (Exception ex) when (ex is InvalidDataException || ex is NotSupportedException) { return null; }
            return DescribeUnsupported(entries);
        }

        public static string DescribeUnsupported(IReadOnlyList<ZipCentralEntry> entries)
        {
            var files = entries.Where(e => !e.IsDirectory).ToList();

            var encrypted = files.Count(e => e.IsEncrypted);
            if (encrypted > 0)
                return $"This map's PAK has {encrypted} encrypted entr{(encrypted == 1 ? "y" : "ies")} (out of {files.Count}). " +
                       "Source cannot load encrypted files and they cannot be read here, so the map is refused.";

            var unsupported = files
                .Where(e => !IsReadable(e.Method))
                .GroupBy(e => e.Method)
                .OrderByDescending(g => g.Count())
                .ToList();

            if (unsupported.Count == 0) return null;

            var detail = string.Join(", ", unsupported.Select(g =>
                $"{g.Count()} x {(MethodNames.TryGetValue(g.Key, out var name) ? name : $"method {g.Key}")}"));

            var message = $"This map's PAK uses a compression method that cannot be read here ({detail}, out of {files.Count} entries).\n\n" +
                          "Opening it partially would silently drop those files when saving, so it is refused.";

            // BZip2 lo lee el zipfile de Python; los demas no los lee ninguna de las dos herramientas.
            if (unsupported.All(g => g.Key == MethodBZip2))
            {
                return message + "\n\n" +
                       "The command line tool can read BZip2 (it needs Python 3.8 or later). To convert the map so it opens here, run:\n" +
                       "    pakrat_modern.ps1 repack <map.bsp> --inplace\n" +
                       "which rewrites every entry uncompressed without adding or removing anything.";
            }

            return message + "\n\n" +
                   "The command line tool cannot read them either. Repack the map with the tool that created it " +
                   "(for example bspzip -repack) and open it again.";
        }

        /// <summary>Busca el EOCD hacia atras, tolerando comentario final.</summary>
        private static int FindEndOfCentralDirectory(byte[] bytes)
        {
            var maxComment = Math.Min(bytes.Length, ushort.MaxValue + 22);

            for (var i = 22; i <= maxComment; i++)
            {
                var pos = bytes.Length - i;
                if (pos < 0) break;
                if (BitConverter.ToUInt32(bytes, pos) == EndOfCentralDirectorySignature) return pos;
            }

            return -1;
        }
    }
}
