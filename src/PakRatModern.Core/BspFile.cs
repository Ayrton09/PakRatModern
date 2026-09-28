using System;
using System.IO;
using System.Linq;

namespace PakRatModern.Core
{
    public sealed class Lump
    {
        public int FileOfs;
        public int FileLen;
        public int Version;
        public byte[] FourCc = new byte[4];

        public Lump Clone() => new Lump
        {
            FileOfs = FileOfs,
            FileLen = FileLen,
            Version = Version,
            FourCc = (byte[])FourCc.Clone(),
        };
    }

    /// <summary>Orden de los campos de cada entrada de la tabla de lumps.</summary>
    public enum LumpLayout
    {
        /// <summary>fileofs, filelen, version, fourCC: casi todos los juegos.</summary>
        Standard,

        /// <summary>version, fileofs, filelen, fourCC: Left 4 Dead 2 (BSP v21).</summary>
        VersionFirst,
    }

    /// <summary>
    /// Lectura y actualizacion de un BSP de Source.
    ///
    /// Solo se toca el lump PAKFILE y los offsets que su cambio de tamano obliga a
    /// mover; el resto del archivo se preserva tal cual. Reconstruir todos los
    /// lumps seria mucho mas facil de romper.
    /// </summary>
    public sealed class BspFile
    {
        public byte[] Raw { get; private set; }
        public int Version { get; private set; }
        public int MapRevision { get; private set; }
        public Lump[] Lumps { get; private set; }

        /// <summary>Se conserva al guardar: el juego solo entiende su propio orden.</summary>
        public LumpLayout Layout { get; private set; }

        public static BspFile Load(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                throw new FileNotFoundException("BSP not found.", path);

            if (info.Length > PakLimits.MaxBspBytes)
                throw new InvalidDataException(
                    $"BSP is too large: {info.Length} bytes. Limit: {PakLimits.MaxBspBytes} bytes.");

            return Parse(File.ReadAllBytes(path));
        }

        public static BspFile Parse(byte[] raw)
        {
            if (raw.Length < PakLimits.HeaderSize)
                throw new InvalidDataException("File is too small for a Source BSP.");

            for (var i = 0; i < 4; i++)
            {
                if (raw[i] != PakLimits.Ident[i])
                    throw new InvalidDataException("Invalid BSP identifier (expected VBSP).");
            }

            // Left 4 Dead 2 pone la version primero en cada entrada de la tabla.
            // Leida en el orden normal, sus offsets caen dentro de la cabecera,
            // asi que se prueba el orden normal y, si no cuadra, el de L4D2.
            var lumps = ReadLumpTable(raw, LumpLayout.Standard, out var error);
            var layout = LumpLayout.Standard;
            if (lumps == null)
            {
                // Una tabla danada leida en el otro orden casi siempre parece
                // "todo vacio" (el campo de version suele ser 0). Una de L4D2 de
                // verdad conserva todos sus lumps: tantos no vacios como offsets
                // no nulos tiene la tabla.
                var alternative = ReadLumpTable(raw, LumpLayout.VersionFirst, out _);
                if (alternative != null)
                {
                    var nonEmpty = alternative.Count(l => l.FileLen > 0);
                    if (nonEmpty > 0 && nonEmpty >= CountPositiveFields(raw, 1))
                    {
                        lumps = alternative;
                        layout = LumpLayout.VersionFirst;
                    }
                }
            }
            if (lumps == null)
                throw new InvalidDataException(error);

            return new BspFile
            {
                Raw = raw,
                Version = BitConverter.ToInt32(raw, 4),
                Lumps = lumps,
                Layout = layout,
                MapRevision = BitConverter.ToInt32(raw, 8 + PakLimits.LumpCount * 16),
            };
        }

        /// <summary>Entradas de la tabla cuyo campo <paramref name="field"/> (0-2) es mayor que cero.</summary>
        private static int CountPositiveFields(byte[] raw, int field)
        {
            var count = 0;
            for (var i = 0; i < PakLimits.LumpCount; i++)
            {
                if (BitConverter.ToInt32(raw, 8 + i * 16 + field * 4) > 0) count++;
            }
            return count;
        }

        /// <summary>Tabla de lumps en ese orden de campos, o null si no es coherente.</summary>
        private static Lump[] ReadLumpTable(byte[] raw, LumpLayout layout, out string error)
        {
            error = null;
            var lumps = new Lump[PakLimits.LumpCount];

            var offset = 8;
            for (var i = 0; i < PakLimits.LumpCount; i++)
            {
                var first = BitConverter.ToInt32(raw, offset);
                var second = BitConverter.ToInt32(raw, offset + 4);
                var third = BitConverter.ToInt32(raw, offset + 8);

                var lump = layout == LumpLayout.Standard
                    ? new Lump { FileOfs = first, FileLen = second, Version = third }
                    : new Lump { Version = first, FileOfs = second, FileLen = third };
                Array.Copy(raw, offset + 12, lump.FourCc, 0, 4);
                offset += 16;

                if (lump.FileLen < 0)
                {
                    error = $"Lump {i} has negative length.";
                    return null;
                }

                if (lump.FileLen > 0)
                {
                    // Un lump que empieza dentro de la cabecera no puede ser
                    // valido: al guardar se reescribe la cabecera encima de el.
                    var end = (long)lump.FileOfs + lump.FileLen;
                    if (lump.FileOfs < PakLimits.HeaderSize || end > raw.Length)
                    {
                        error = $"Lump {i} out of range (ofs={lump.FileOfs} len={lump.FileLen}).";
                        return null;
                    }
                }

                lumps[i] = lump;
            }

            return lumps;
        }

        public byte[] GetLumpBytes(int index)
        {
            var lump = Lumps[index];
            if (lump.FileLen == 0)
                return new byte[0];

            var buffer = new byte[lump.FileLen];
            Array.Copy(Raw, lump.FileOfs, buffer, 0, lump.FileLen);
            return buffer;
        }

        public byte[] ReadPakLump() => GetLumpBytes(PakLimits.PakLumpIndex);

        /// <summary>
        /// Contenido del lump, descomprimido si viene en LZMA. Con
        /// <c>bspzip -repack -compress</c> el fourCC guarda el tamano original y
        /// los datos empiezan con la cabecera 'LZMA'; leido crudo, un scan no
        /// encuentra nada en el lump de entidades.
        /// </summary>
        public byte[] GetLumpData(int index)
        {
            var raw = GetLumpBytes(index);
            var uncompressedSize = BitConverter.ToInt32(Lumps[index].FourCc, 0);

            if (uncompressedSize > 0 && Lzma.IsSourceCompressed(raw, 0, raw.Length))
                return Lzma.DecodeSourceBlock(raw, 0, raw.Length, PakLimits.MaxPakEntryBytes);

            return raw;
        }

        /// <summary>
        /// Sustituye el lump PAKFILE y devuelve el BSP completo resultante.
        /// </summary>
        public byte[] ApplyPak(byte[] newPak)
        {
            var lumps = new Lump[PakLimits.LumpCount];
            for (var i = 0; i < PakLimits.LumpCount; i++)
                lumps[i] = Lumps[i].Clone();

            var pak = lumps[PakLimits.PakLumpIndex];
            var game = lumps[PakLimits.GameLumpIndex];

            byte[] updated;

            if (pak.FileLen == 0)
            {
                var insertAt = Align4(Raw.Length);
                updated = new byte[insertAt + newPak.Length];
                Array.Copy(Raw, 0, updated, 0, Raw.Length);
                Array.Copy(newPak, 0, updated, insertAt, newPak.Length);
                pak.FileOfs = insertAt;
                pak.FileLen = newPak.Length;
            }
            else
            {
                var oldStart = pak.FileOfs;
                var oldEnd = pak.FileOfs + pak.FileLen;

                // El PAK se rellena con ceros hasta que el desplazamiento de todo lo
                // que viene despues sea multiplo de 4. Sin esto, un PAK que no es el
                // ultimo lump mueve a los siguientes por un delta arbitrario y les
                // rompe la alineacion a 4 bytes que el motor Source da por sentada.
                var padding = ((pak.FileLen - newPak.Length) % 4 + 4) % 4;
                var delta = (newPak.Length + padding) - pak.FileLen;

                if (delta != 0 && game.FileLen > 0 && game.FileOfs > oldStart)
                    throw new InvalidOperationException(
                        "Cannot resize PAKFILE because LUMP_GAME_LUMP is after it. " +
                        "This BSP is not safe to grow/shrink.");

                updated = new byte[Raw.Length + delta];
                Array.Copy(Raw, 0, updated, 0, oldStart);
                Array.Copy(newPak, 0, updated, oldStart, newPak.Length);

                // Los bytes de relleno quedan en cero por la inicializacion del array.
                var tailLen = Raw.Length - oldEnd;
                if (tailLen > 0)
                    Array.Copy(Raw, oldEnd, updated, oldStart + newPak.Length + padding, tailLen);

                if (delta != 0)
                {
                    for (var i = 0; i < lumps.Length; i++)
                    {
                        if (i == PakLimits.PakLumpIndex) continue;
                        var l = lumps[i];
                        if (l.FileLen > 0 && l.FileOfs > oldStart)
                            l.FileOfs += delta;
                    }
                }

                // filelen queda con el tamano real del ZIP; el relleno es espacio
                // muerto entre lumps, que es como lo emite vbsp.
                pak.FileLen = newPak.Length;
            }

            if (updated.LongLength > PakLimits.MaxBspBytes)
                throw new InvalidDataException(
                    $"Resulting BSP is too large: {updated.LongLength} bytes. Limit: {PakLimits.MaxBspBytes} bytes.");

            var header = SerializeHeader(Version, MapRevision, lumps, Layout);
            Array.Copy(header, 0, updated, 0, header.Length);

            Raw = updated;
            Lumps = lumps;
            return updated;
        }

        public static byte[] SerializeHeader(int version, int mapRevision, Lump[] lumps,
            LumpLayout layout = LumpLayout.Standard)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(PakLimits.Ident, 0, 4);
                ms.Write(BitConverter.GetBytes(version), 0, 4);

                for (var i = 0; i < PakLimits.LumpCount; i++)
                {
                    var l = lumps[i];
                    if (layout == LumpLayout.VersionFirst)
                        ms.Write(BitConverter.GetBytes(l.Version), 0, 4);
                    ms.Write(BitConverter.GetBytes(l.FileOfs), 0, 4);
                    ms.Write(BitConverter.GetBytes(l.FileLen), 0, 4);
                    if (layout == LumpLayout.Standard)
                        ms.Write(BitConverter.GetBytes(l.Version), 0, 4);
                    if (l.FourCc.Length != 4)
                        throw new InvalidDataException($"Lump {i} has invalid fourcc.");
                    ms.Write(l.FourCc, 0, 4);
                }

                ms.Write(BitConverter.GetBytes(mapRevision), 0, 4);
                return ms.ToArray();
            }
        }

        private static int Align4(int value) => (value + 3) & ~3;
    }
}
