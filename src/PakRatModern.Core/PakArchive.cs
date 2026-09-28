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
        public PakEntry(string fullPath, byte[] data) : this(fullPath, data, false)
        {
        }

        /// <param name="legacyName">
        /// El nombre llego sin el flag UTF-8 del ZIP (asi escriben vbsp y bspzip).
        /// Se reescribe con los mismos bytes y sin flag: el motor busca los bytes
        /// crudos, y pasarlos a UTF-8 cambia el archivo que encuentra.
        /// </param>
        public PakEntry(string fullPath, byte[] data, bool legacyName)
        {
            FullPath = fullPath;
            Data = data ?? new byte[0];
            LegacyName = legacyName && fullPath.Any(c => c > 127);
        }

        /// <summary>Ruta interna normalizada, con '/' como separador.</summary>
        public string FullPath { get; }

        public byte[] Data { get; }

        /// <summary>El nombre se escribe en Latin-1 y sin flag UTF-8, como vino.</summary>
        public bool LegacyName { get; }

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
        private const uint LocalFileHeaderSignature = 0x04034b50;
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
            byte[] pakBytes, out IReadOnlyList<string> skipped, out IReadOnlyList<string> duplicates)
        {
            var unsafeNames = new List<string>();
            var duplicateNames = new List<string>();
            skipped = unsafeNames;
            duplicates = duplicateNames;

            var entries = NewEntryMap();
            if (pakBytes == null || pakBytes.Length == 0)
                return entries;

            IReadOnlyList<ZipCentralEntry> central;
            try
            {
                central = ZipInspector.ReadCentralDirectory(pakBytes);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"PAK lump central directory could not be read: {ex.Message}", ex);
            }

            // Se comprueba antes de leer nada: abrir a medias un PAK con
            // entradas que no se pueden descomprimir haria que guardar las
            // borrara del mapa sin aviso.
            var unsupported = ZipInspector.DescribeUnsupported(central);
            if (unsupported != null) throw new NotSupportedException(unsupported);

            if (central.Count > PakLimits.MaxPakEntries)
                throw new InvalidDataException(
                    $"PAK has too many entries: {central.Count}. Limit: {PakLimits.MaxPakEntries}.");

            try
            {
                long totalUncompressed = 0;

                foreach (var record in central)
                {
                    if (record.IsDirectory) continue;

                    var rawName = record.Name;
                    string name;
                    try
                    {
                        name = ArchivePath.Normalize(rawName);
                    }
                    catch (ArgumentException)
                    {
                        unsafeNames.Add(rawName);
                        continue;
                    }

                    if (record.UncompressedSize > PakLimits.MaxPakEntryBytes)
                        throw new InvalidDataException(
                            $"PAK entry is too large: {name} ({record.UncompressedSize} bytes). Limit: {PakLimits.MaxPakEntryBytes} bytes.");

                    totalUncompressed += record.UncompressedSize;
                    if (totalUncompressed > PakLimits.MaxPakTotalBytes)
                        throw new InvalidDataException(
                            $"PAK uncompressed total is too large. Limit: {PakLimits.MaxPakTotalBytes} bytes.");

                    var data = ReadEntry(pakBytes, record, name);
                    var legacyName = !record.IsUtf8;

                    // Duplicados: comunes en mapas publicados. Se unifican
                    // porque el motor solo puede cargar uno, pero se informa.
                    // Politica: gana la ultima copia (igual que la CLI). Se
                    // conserva el nombre de la primera aparicion para que ambas
                    // herramientas escriban el mismo. Si el contenido difiere se
                    // marca: el usuario pierde una version y tiene que saberlo.
                    if (entries.TryGetValue(name, out var existing))
                    {
                        var sameContent = existing.Data.Length == data.Length && existing.Data.SequenceEqual(data);
                        duplicateNames.Add(sameContent
                            ? rawName
                            : rawName + " (different content; last copy kept)");
                        name = existing.FullPath;
                        legacyName = existing.LegacyName;
                    }

                    entries[name] = new PakEntry(name, data, legacyName);
                }
            }
            catch (Exception ex) when (!(ex is InvalidDataException) && !(ex is NotSupportedException) && !(ex is OutOfMemoryException))
            {
                throw new InvalidDataException($"PAK lump could not be read safely: {ex.Message}", ex);
            }

            return entries;
        }

        /// <summary>
        /// Extrae y verifica una entrada. Nunca produce mas bytes de los
        /// declarados (un ZIP de 1 KB que declara 10 bytes podria inflar
        /// gigabytes) y exige que el CRC-32 coincida: un PAK danado no se abre
        /// ni se reescribe con un CRC nuevo que esconda el dano.
        /// </summary>
        private static byte[] ReadEntry(byte[] zip, ZipCentralEntry record, string name)
        {
            var local = record.LocalHeaderOffset;
            if (local < 0 || local + 30 > zip.Length || BitConverter.ToUInt32(zip, (int)local) != LocalFileHeaderSignature)
                throw new InvalidDataException($"PAK entry {name} has no valid local header.");

            var at = (int)local;
            var start = local + 30 + BitConverter.ToUInt16(zip, at + 26) + BitConverter.ToUInt16(zip, at + 28);
            if (start + record.CompressedSize > zip.Length)
                throw new InvalidDataException($"PAK entry {name} is truncated.");

            var declared = record.UncompressedSize;
            byte[] data;

            switch (record.Method)
            {
                case ZipInspector.MethodStored:
                    if (record.CompressedSize != declared)
                        throw new InvalidDataException(
                            $"PAK entry {name} does not match its declared size ({declared} bytes); refusing to read it.");
                    data = new byte[declared];
                    Array.Copy(zip, start, data, 0, declared);
                    break;

                case ZipInspector.MethodDeflate:
                    data = Inflate(zip, (int)start, (int)record.CompressedSize, declared, name);
                    break;

                case ZipInspector.MethodLzma:
                    data = InflateLzma(zip, (int)start, (int)record.CompressedSize, declared, name);
                    break;

                default:
                    throw new NotSupportedException($"PAK entry {name} uses compression method {record.Method}.");
            }

            if (Crc32.Compute(data) != record.Crc)
                throw new InvalidDataException($"PAK entry {name} is damaged: its CRC-32 does not match its contents.");

            return data;
        }

        private static byte[] Inflate(byte[] zip, int start, int length, long declared, string name)
        {
            var data = new byte[declared];
            var total = 0;

            using (var input = new MemoryStream(zip, start, length, false))
            using (var inflater = new DeflateStream(input, CompressionMode.Decompress))
            {
                while (total < data.Length)
                {
                    var read = inflater.Read(data, total, data.Length - total);
                    if (read <= 0) break;
                    total += read;
                }

                if (total != data.Length)
                    throw new InvalidDataException(
                        $"PAK entry {name} is truncated: declared {declared} bytes, got {total}.");

                // Un byte mas de lo declarado es un ZIP que miente sobre su tamano.
                var probe = new byte[1];
                if (inflater.Read(probe, 0, 1) > 0)
                    throw new InvalidDataException(
                        $"PAK entry {name} is larger than declared ({declared} bytes); refusing to inflate it.");
            }

            return data;
        }

        /// <summary>
        /// Metodo 14 del ZIP: version (2 bytes), largo de propiedades (2),
        /// propiedades (5) y el stream. Es como guarda las entradas
        /// <c>bspzip -repack -compress</c>.
        /// </summary>
        private static byte[] InflateLzma(byte[] zip, int start, int length, long declared, string name)
        {
            if (length < 9 || BitConverter.ToUInt16(zip, start + 2) != 5)
                throw new InvalidDataException($"PAK entry {name} has an invalid LZMA header.");

            var properties = new byte[5];
            Array.Copy(zip, start + 4, properties, 0, 5);

            try
            {
                return Lzma.Decode(zip, start + 9, length - 9, properties, declared);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"PAK entry {name}: {ex.Message}", ex);
            }
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

                    var nameInfo = EncodeName(entry);
                    if (nameInfo.Bytes.Length > ushort.MaxValue)
                        throw new InvalidDataException($"PAK entry path is too long for ZIP: {key}");

                    var localOffset = (uint)ms.Position;
                    var crc = Crc32.Compute(data);
                    var size = (uint)data.Length;

                    WriteUInt32(ms, LocalFileHeaderSignature);
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

        private static NameInfo EncodeName(PakEntry entry)
        {
            var archivePath = entry.FullPath;

            // Un nombre que vino sin flag UTF-8 vuelve a salir con sus bytes originales.
            if (entry.LegacyName)
                return new NameInfo { Bytes = ZipInspector.Latin1.GetBytes(archivePath), Flags = 0 };

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
