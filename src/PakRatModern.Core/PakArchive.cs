using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PakRatModern.Core
{
    /// <summary>Una entrada embebida en el lump PAKFILE.</summary>
    public sealed class PakEntry
    {
        public PakEntry(string fullPath, byte[] data)
        {
            FullPath = fullPath;
            Data = data ?? new byte[0];
        }

        /// <summary>Ruta interna normalizada, con '/' como separador.</summary>
        public string FullPath { get; }

        public byte[] Data { get; }

        public int Size => Data.Length;

        public string Name => FullPath.Substring(FullPath.LastIndexOf('/') + 1);

        public string Directory
        {
            get
            {
                var i = FullPath.LastIndexOf('/');
                return i < 0 ? string.Empty : FullPath.Substring(0, i);
            }
        }
    }

    /// <summary>
    /// Lectura y escritura del ZIP que vive dentro del lump PAKFILE.
    ///
    /// El escritor produce byte por byte el mismo ZIP que la CLI de Python; hay un
    /// test que compara los hashes de ambas salidas. Eso permite mezclar las dos
    /// herramientas sobre el mismo mapa sin que el archivo cambie de forma espuria.
    /// </summary>
    public static class PakArchive
    {
        // El motor Source solo lee entradas STORE del lump PAKFILE.
        private const ushort MethodStored = 0;
        private const ushort VersionNeeded = 20;
        private const ushort VersionMadeBy = 20;

        // zipfile de Python fuerza 0o600<<16 en _open_to_write cuando el campo es 0,
        // asi que este es el valor canonico si se quiere paridad exacta con la CLI.
        private const uint ExternalAttributes = 0x01800000;

        // Fecha fija 1980-01-01: con la hora actual, dos guardados sin cambios
        // producian archivos distintos. El motor ignora el campo.
        private const ushort DosDate = (1 << 5) | 1;
        private const ushort DosTime = 0;

        /// <summary>
        /// Diccionario de entradas con claves insensibles a mayusculas y orden de
        /// insercion estable. Source resuelve rutas sin distinguir mayusculas, asi
        /// que dos entradas que solo difieren en capitalizacion son la misma.
        /// </summary>
        public static Dictionary<string, PakEntry> NewEntryMap()
        {
            return new Dictionary<string, PakEntry>(StringComparer.OrdinalIgnoreCase);
        }

        public static Dictionary<string, PakEntry> Read(byte[] pakBytes) => Read(pakBytes, out _, out _);

        public static Dictionary<string, PakEntry> Read(
            byte[] pakBytes, out IReadOnlyList<string> skipped, out IReadOnlyList<string> duplicates)
        {
            if (pakBytes == null || pakBytes.Length == 0)
            {
                skipped = new List<string>();
                duplicates = new List<string>();
                return NewEntryMap();
            }

            // Se comprueba antes de leer nada: abrir a medias un PAK con
            // entradas que no se pueden descomprimir haria que guardar las
            // borrara del mapa sin aviso.
            string unsupported;
            try
            {
                unsupported = ZipInspector.DescribeUnsupported(pakBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"PAK lump central directory could not be read: {ex.Message}", ex);
            }
            if (unsupported != null) throw new NotSupportedException(unsupported);

            using (var ms = new MemoryStream(pakBytes, false))
                return Read(ms, out skipped, out duplicates);
        }

        public static Dictionary<string, PakEntry> Read(Stream stream) => Read(stream, out _, out _);

        /// <param name="skipped">
        /// Entradas descartadas por tener rutas inseguras. Hay mapas publicados
        /// con rutas absolutas dentro del PAK; se descarta esa entrada y no el
        /// mapa entero, porque rechazar todo dejaba inaccesibles cientos de
        /// archivos validos por una sola entrada mal empaquetada.
        /// </param>
        /// <param name="duplicates">
        /// Entradas repetidas que quedaron unificadas. El motor no distingue
        /// mayusculas y solo puede cargar una, pero cambia el conteo y el tamano
        /// del archivo, asi que conviene informarlo.
        /// </param>
        public static Dictionary<string, PakEntry> Read(
            Stream stream, out IReadOnlyList<string> skipped, out IReadOnlyList<string> duplicates)
        {
            var unsafeNames = new List<string>();
            var duplicateNames = new List<string>();
            skipped = unsafeNames;
            duplicates = duplicateNames;

            var entries = NewEntryMap();
            if (stream == null || stream.Length == 0)
                return entries;

            try
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
                {
                    if (zip.Entries.Count > PakLimits.MaxPakEntries)
                        throw new InvalidDataException(
                            $"PAK has too many entries: {zip.Entries.Count}. Limit: {PakLimits.MaxPakEntries}.");

                    long totalUncompressed = 0;

                    foreach (var entry in zip.Entries)
                    {
                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                            continue;

                        string name;
                        try
                        {
                            name = ArchivePath.Normalize(entry.FullName);
                        }
                        catch (ArgumentException)
                        {
                            unsafeNames.Add(entry.FullName);
                            continue;
                        }

                        if (entry.Length > PakLimits.MaxPakEntryBytes)
                            throw new InvalidDataException(
                                $"PAK entry is too large: {name} ({entry.Length} bytes). Limit: {PakLimits.MaxPakEntryBytes} bytes.");

                        totalUncompressed += entry.Length;
                        if (totalUncompressed > PakLimits.MaxPakTotalBytes)
                            throw new InvalidDataException(
                                $"PAK uncompressed total is too large. Limit: {PakLimits.MaxPakTotalBytes} bytes.");

                        var data = ReadEntryBounded(entry, name);

                        // Duplicados: comunes en mapas publicados. Se unifican
                        // porque el motor solo puede cargar uno, pero se informa.
                        // Politica: gana la ultima copia (igual que la CLI). Se
                        // conserva la capitalizacion de la primera aparicion para
                        // que ambas herramientas escriban el mismo nombre. Si el
                        // contenido difiere se marca: el usuario pierde una version
                        // y tiene que saberlo.
                        if (entries.TryGetValue(name, out var existing))
                        {
                            var sameContent = existing.Data.Length == data.Length && existing.Data.SequenceEqual(data);
                            duplicateNames.Add(sameContent
                                ? entry.FullName
                                : entry.FullName + " (different content; last copy kept)");
                            name = existing.FullPath;
                        }

                        entries[name] = new PakEntry(name, data);
                    }
                }
            }
            catch (Exception ex) when (!(ex is InvalidDataException) && !(ex is NotSupportedException))
            {
                throw new InvalidDataException($"PAK lump could not be read safely: {ex.Message}", ex);
            }

            return entries;
        }

        /// <summary>
        /// Descomprime una entrada sin confiar en el tamano declarado.
        ///
        /// ZipArchive de .NET Framework no corta la descompresion al llegar al
        /// tamano que anuncia el directorio central: un ZIP de 1 KB que declara
        /// 10 bytes puede inflar gigabytes. Los limites de arriba se comparan
        /// contra lo declarado, asi que aca se cuenta lo que realmente sale.
        /// </summary>
        private static byte[] ReadEntryBounded(ZipArchiveEntry entry, string name)
        {
            var declared = entry.Length;
            if (declared < 0 || declared > PakLimits.MaxPakEntryBytes)
                throw new InvalidDataException($"PAK entry is too large: {name} ({declared} bytes).");

            var data = new byte[declared];
            var total = 0;

            using (var source = entry.Open())
            {
                while (total < data.Length)
                {
                    var read = source.Read(data, total, data.Length - total);
                    if (read <= 0) break;
                    total += read;
                }

                if (total != data.Length)
                    throw new InvalidDataException(
                        $"PAK entry {name} is truncated: declared {declared} bytes, got {total}.");

                // Un byte mas de lo declarado es un ZIP que miente sobre su tamano.
                var probe = new byte[1];
                if (source.Read(probe, 0, 1) > 0)
                    throw new InvalidDataException(
                        $"PAK entry {name} is larger than declared ({declared} bytes); refusing to inflate it.");
            }

            return data;
        }

        public static byte[] Write(IReadOnlyDictionary<string, PakEntry> entries)
        {
            if (entries.Count > PakLimits.MaxPakEntries)
                throw new InvalidDataException(
                    $"PAK has too many entries: {entries.Count}. Limit: {PakLimits.MaxPakEntries}.");

            // El ZIP clasico no representa mas de 65535 entradas en el EOCD; se
            // valida antes de escribir nada.
            if (entries.Count > ushort.MaxValue)
                throw new InvalidDataException("PAK has too many entries for classic ZIP.");

            // Orden ordinal explicito: un orden dependiente de la cultura ubica
            // '_pre.vmt' y 'A.vmt' al reves y haria que la salida dependiera del
            // idioma del sistema.
            var keys = entries.Values.Select(e => e.FullPath).ToArray();
            Array.Sort(keys, StringComparer.Ordinal);

            var central = new List<CentralRecord>(keys.Length);
            long totalUncompressed = 0;

            using (var ms = new MemoryStream())
            {
                foreach (var key in keys)
                {
                    var entry = entries[key];
                    var data = entry.Data;

                    if (data.LongLength > PakLimits.MaxPakEntryBytes)
                        throw new InvalidDataException(
                            $"PAK entry is too large: {key} ({data.LongLength} bytes). Limit: {PakLimits.MaxPakEntryBytes} bytes.");

                    totalUncompressed += data.LongLength;
                    if (totalUncompressed > PakLimits.MaxPakTotalBytes)
                        throw new InvalidDataException(
                            $"PAK uncompressed total is too large. Limit: {PakLimits.MaxPakTotalBytes} bytes.");

                    var nameInfo = EncodeName(key);
                    if (nameInfo.Bytes.Length > ushort.MaxValue)
                        throw new InvalidDataException($"PAK entry path is too long for ZIP: {key}");

                    var localOffset = (uint)ms.Position;
                    var crc = Crc32.Compute(data);
                    var size = (uint)data.Length;

                    WriteUInt32(ms, 0x04034b50);
                    WriteUInt16(ms, VersionNeeded);
                    WriteUInt16(ms, nameInfo.Flags);
                    WriteUInt16(ms, MethodStored);
                    WriteUInt16(ms, DosTime);
                    WriteUInt16(ms, DosDate);
                    WriteUInt32(ms, crc);
                    WriteUInt32(ms, size);
                    WriteUInt32(ms, size);
                    WriteUInt16(ms, (ushort)nameInfo.Bytes.Length);
                    WriteUInt16(ms, 0);
                    ms.Write(nameInfo.Bytes, 0, nameInfo.Bytes.Length);
                    ms.Write(data, 0, data.Length);

                    central.Add(new CentralRecord
                    {
                        NameBytes = nameInfo.Bytes,
                        Flags = nameInfo.Flags,
                        Crc = crc,
                        Size = size,
                        LocalOffset = localOffset,
                    });
                }

                var centralOffset = (uint)ms.Position;

                foreach (var record in central)
                {
                    WriteUInt32(ms, 0x02014b50);
                    WriteUInt16(ms, VersionMadeBy);
                    WriteUInt16(ms, VersionNeeded);
                    WriteUInt16(ms, record.Flags);
                    WriteUInt16(ms, MethodStored);
                    WriteUInt16(ms, DosTime);
                    WriteUInt16(ms, DosDate);
                    WriteUInt32(ms, record.Crc);
                    WriteUInt32(ms, record.Size);
                    WriteUInt32(ms, record.Size);
                    WriteUInt16(ms, (ushort)record.NameBytes.Length);
                    WriteUInt16(ms, 0);                     // extra length
                    WriteUInt16(ms, 0);                     // comment length
                    WriteUInt16(ms, 0);                     // disk number start
                    WriteUInt16(ms, 0);                     // internal attributes
                    WriteUInt32(ms, ExternalAttributes);
                    WriteUInt32(ms, record.LocalOffset);
                    ms.Write(record.NameBytes, 0, record.NameBytes.Length);
                }

                var centralSize = (uint)(ms.Position - centralOffset);

                WriteUInt32(ms, 0x06054b50);
                WriteUInt16(ms, 0);
                WriteUInt16(ms, 0);
                WriteUInt16(ms, (ushort)central.Count);
                WriteUInt16(ms, (ushort)central.Count);
                WriteUInt32(ms, centralSize);
                WriteUInt32(ms, centralOffset);
                WriteUInt16(ms, 0);

                return ms.ToArray();
            }
        }

        /// <summary>Round-trip de validacion: escribe y vuelve a leer.</summary>
        public static (bool Ok, string Message) Verify(IReadOnlyDictionary<string, PakEntry> entries)
        {
            try
            {
                var pak = Write(entries);
                var back = Read(pak);
                return (true, $"ZIP valid with {back.Count} entries.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private struct NameInfo
        {
            public byte[] Bytes;
            public ushort Flags;
        }

        private sealed class CentralRecord
        {
            public byte[] NameBytes;
            public ushort Flags;
            public uint Crc;
            public uint Size;
            public uint LocalOffset;
        }

        private static NameInfo EncodeName(string archivePath)
        {
            var needsUtf8 = archivePath.Any(c => c > 127);
            return needsUtf8
                ? new NameInfo { Bytes = Encoding.UTF8.GetBytes(archivePath), Flags = 0x0800 }
                : new NameInfo { Bytes = Encoding.ASCII.GetBytes(archivePath), Flags = 0 };
        }

        private static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 24) & 0xFF));
        }
    }
}
