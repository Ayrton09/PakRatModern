using System;
using System.Collections.Generic;
using System.Linq;

namespace PakRatModern.Core
{
    /// <summary>
    /// Lector minimo del directorio central de un ZIP, solo para conocer con que
    /// metodo esta comprimida cada entrada.
    ///
    /// Hace falta porque ZipArchive de .NET no expone el metodo y falla con un
    /// mensaje generico cuando no lo soporta: sin esto no se puede decirle al
    /// usuario que le pasa a su mapa.
    /// </summary>
    public static class ZipInspector
    {
        private const uint EndOfCentralDirectorySignature = 0x06054b50;
        private const uint CentralFileHeaderSignature = 0x02014b50;

        // Lo unico que ZipArchive sabe descomprimir.
        private const ushort MethodStored = 0;
        private const ushort MethodDeflate = 8;

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

        /// <summary>Cantidad de entradas por metodo de compresion.</summary>
        public static Dictionary<ushort, int> GetCompressionMethods(byte[] zipBytes)
        {
            var counts = new Dictionary<ushort, int>();
            if (zipBytes == null || zipBytes.Length < 22) return counts;

            var eocd = FindEndOfCentralDirectory(zipBytes);
            if (eocd < 0) return counts;

            var entryCount = BitConverter.ToUInt16(zipBytes, eocd + 10);

            // Aritmetica en long: un offset cercano a uint.MaxValue desbordaba
            // el int y pasaba el chequeo de rango con un valor negativo.
            long offset = BitConverter.ToUInt32(zipBytes, eocd + 16);

            for (var i = 0; i < entryCount; i++)
            {
                if (offset < 0 || offset + 46 > zipBytes.Length) break;
                var at = (int)offset;
                if (BitConverter.ToUInt32(zipBytes, at) != CentralFileHeaderSignature) break;

                var method = BitConverter.ToUInt16(zipBytes, at + 10);
                counts[method] = counts.TryGetValue(method, out var n) ? n + 1 : 1;

                var nameLength = BitConverter.ToUInt16(zipBytes, at + 28);
                var extraLength = BitConverter.ToUInt16(zipBytes, at + 30);
                var commentLength = BitConverter.ToUInt16(zipBytes, at + 32);
                offset += 46 + nameLength + extraLength + commentLength;
            }

            return counts;
        }

        /// <summary>
        /// Devuelve un mensaje explicando el problema, o null si todo el
        /// contenido usa metodos que se pueden leer.
        /// </summary>
        public static string DescribeUnsupported(byte[] zipBytes)
        {
            var counts = GetCompressionMethods(zipBytes);

            var unsupported = counts
                .Where(kv => kv.Key != MethodStored && kv.Key != MethodDeflate)
                .OrderByDescending(kv => kv.Value)
                .ToList();

            if (unsupported.Count == 0) return null;

            var detail = string.Join(", ", unsupported.Select(kv =>
                $"{kv.Value} x {(MethodNames.TryGetValue(kv.Key, out var name) ? name : $"method {kv.Key}")}"));

            var total = counts.Values.Sum();

            return $"This map's PAK uses a compression method that cannot be read here ({detail}, out of {total} entries).\n\n" +
                   "Opening it partially would silently drop those files when saving, so it is refused.\n\n" +
                   "The command line tool can read them. To convert the map so it opens here, run:\n" +
                   "    pakrat_modern.ps1 add <map.bsp> <any file> --base <its folder> --inplace\n" +
                   "which rewrites every entry uncompressed.";
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
