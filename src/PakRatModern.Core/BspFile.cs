using System;
using System.IO;

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

            var bsp = new BspFile
            {
                Raw = raw,
                Version = BitConverter.ToInt32(raw, 4),
                Lumps = new Lump[PakLimits.LumpCount],
            };

            var offset = 8;
            for (var i = 0; i < PakLimits.LumpCount; i++)
            {
                var lump = new Lump
                {
                    FileOfs = BitConverter.ToInt32(raw, offset),
                    FileLen = BitConverter.ToInt32(raw, offset + 4),
                    Version = BitConverter.ToInt32(raw, offset + 8),
                };
                Array.Copy(raw, offset + 12, lump.FourCc, 0, 4);
                offset += 16;

                if (lump.FileLen < 0)
                    throw new InvalidDataException($"Lump {i} has negative length.");

                if (lump.FileLen > 0)
                {
                    var end = (long)lump.FileOfs + lump.FileLen;
                    if (lump.FileOfs < 0 || end > raw.Length)
                        throw new InvalidDataException(
                            $"Lump {i} out of range (ofs={lump.FileOfs} len={lump.FileLen}).");
                }

                bsp.Lumps[i] = lump;
            }

            bsp.MapRevision = BitConverter.ToInt32(raw, offset);
            return bsp;
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

            var header = SerializeHeader(Version, MapRevision, lumps);
            Array.Copy(header, 0, updated, 0, header.Length);

            Raw = updated;
            Lumps = lumps;
            return updated;
        }

        public static byte[] SerializeHeader(int version, int mapRevision, Lump[] lumps)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(PakLimits.Ident, 0, 4);
                ms.Write(BitConverter.GetBytes(version), 0, 4);

                for (var i = 0; i < PakLimits.LumpCount; i++)
                {
                    var l = lumps[i];
                    ms.Write(BitConverter.GetBytes(l.FileOfs), 0, 4);
                    ms.Write(BitConverter.GetBytes(l.FileLen), 0, 4);
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
