using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PakRatModern.Core
{
    /// <summary>
    /// Lector del arbol de directorios de un VPK (<c>*_dir.vpk</c>).
    ///
    /// Solo interesa que rutas existen, no su contenido: sirve para no empaquetar
    /// dentro del BSP archivos que el juego ya trae de fabrica, que es lo que hace
    /// que un mapa pese de mas sin ninguna necesidad.
    /// </summary>
    public static class VpkIndex
    {
        private const uint Signature = 0x55aa1234;
        private const ushort EntryTerminator = 0xffff;

        /// <summary>
        /// Devuelve las rutas internas contenidas en el VPK. Si el archivo esta
        /// truncado o corrupto, devuelve lo leido hasta ese punto en lugar de
        /// fallar: un VPK ilegible no deberia impedir escanear el resto.
        /// </summary>
        public static IEnumerable<string> EnumerateEntries(string dirVpkPath)
        {
            var results = new List<string>();
            if (!File.Exists(dirVpkPath))
                return results;

            try
            {
                using (var fs = File.OpenRead(dirVpkPath))
                using (var br = new BinaryReader(fs))
                {
                    var limit = fs.Length;

                    if (fs.Length >= 12)
                    {
                        var signature = br.ReadUInt32();
                        if (signature == Signature)
                        {
                            var version = br.ReadUInt32();
                            var treeSize = br.ReadUInt32();

                            if (version == 2)
                            {
                                if (fs.Length < 28) return results;
                                for (var i = 0; i < 4; i++) br.ReadUInt32();
                            }
                            else if (version != 1)
                            {
                                return results;
                            }

                            limit = Math.Min(fs.Length, fs.Position + treeSize);
                        }
                        else
                        {
                            // Sin cabecera: el arbol arranca en el byte 0.
                            fs.Position = 0;
                        }
                    }

                    ReadTree(fs, br, limit, results);
                }
            }
            catch (Exception)
            {
                // Devolvemos lo que se haya podido leer.
            }

            return results;
        }

        private static void ReadTree(FileStream fs, BinaryReader br, long limit, List<string> results)
        {
            while (fs.Position < limit)
            {
                var extension = ReadCString(br, limit);
                if (string.IsNullOrEmpty(extension)) break;

                while (fs.Position < limit)
                {
                    var directory = ReadCString(br, limit);
                    if (string.IsNullOrEmpty(directory)) break;

                    while (fs.Position < limit)
                    {
                        var fileName = ReadCString(br, limit);
                        if (string.IsNullOrEmpty(fileName)) break;

                        // crc(4) + preload(2) + archiveIndex(2) + offset(4) + length(4) + terminator(2)
                        if (limit - fs.Position < 18) return;

                        br.ReadUInt32();                    // crc
                        var preloadBytes = br.ReadUInt16();
                        br.ReadUInt16();                    // archive index
                        br.ReadUInt32();                    // entry offset
                        br.ReadUInt32();                    // entry length

                        if (br.ReadUInt16() != EntryTerminator) return;

                        var archivePath = BuildArchivePath(extension, directory, fileName);
                        if (archivePath != null) results.Add(archivePath);

                        if (preloadBytes > 0)
                        {
                            var next = fs.Position + preloadBytes;
                            if (next > limit) return;
                            fs.Position = next;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// El formato usa un unico espacio para representar "sin extension" o
        /// "raiz", no una cadena vacia.
        /// </summary>
        internal static string BuildArchivePath(string extension, string directory, string fileName)
        {
            if (extension == " ") extension = string.Empty;
            if (directory == " ") directory = string.Empty;
            if (fileName == " ") fileName = string.Empty;

            if (string.IsNullOrWhiteSpace(fileName)) return null;

            var leaf = string.IsNullOrWhiteSpace(extension) ? fileName : fileName + "." + extension;
            var path = string.IsNullOrWhiteSpace(directory) ? leaf : directory + "/" + leaf;

            try { return ArchivePath.Normalize(path); }
            catch (ArgumentException) { return null; }
        }

        private static string ReadCString(BinaryReader reader, long limit)
        {
            var bytes = new List<byte>(64);
            while (reader.BaseStream.Position < limit)
            {
                var b = reader.ReadByte();
                if (b == 0) break;
                bytes.Add(b);
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }
}
