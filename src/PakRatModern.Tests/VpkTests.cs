using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PakRatModern.Core;

namespace PakRatModern.Tests
{
    /// <summary>
    /// Pruebas del lector de VPK y de gameinfo.txt sobre archivos sinteticos, para
    /// no depender de que haya un juego de Source instalado.
    /// </summary>
    internal static class VpkTests
    {
        public static void Run(Action<bool, string> check)
        {
            var temp = Path.Combine(Path.GetTempPath(), "pakrat_vpk_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                TestVpkV2Parsing(check, temp);
                TestVpkTruncatedIsTolerated(check, temp);
                TestGameInfoSearchPaths(check, temp);
                TestSearchPathResolution(check, temp);
                TestBaseIndexFindsVpk(check, temp);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch (IOException) { }
            }
        }

        private static void TestVpkV2Parsing(Action<bool, string> check, string temp)
        {
            var path = Path.Combine(temp, "pak01_dir.vpk");
            File.WriteAllBytes(path, BuildVpk(version: 2, new[]
            {
                ("vmt", "materials/custom", "wall"),
                ("vmt", "materials/custom", "floor"),
                ("mdl", "models/props", "crate"),
                ("txt", " ", "readme"),           // " " = raiz del VPK
            }));

            var entries = VpkIndex.EnumerateEntries(path).ToList();

            check(entries.Contains("materials/custom/wall.vmt"), "VPK v2: falta materials/custom/wall.vmt");
            check(entries.Contains("materials/custom/floor.vmt"), "VPK v2: falta materials/custom/floor.vmt");
            check(entries.Contains("models/props/crate.mdl"), "VPK v2: falta models/props/crate.mdl");
            check(entries.Contains("readme.txt"), "VPK v2: no manejo el directorio raiz (' ')");
            check(entries.Count == 4, $"VPK v2: se esperaban 4 entradas, hay {entries.Count}");
        }

        private static void TestVpkTruncatedIsTolerated(Action<bool, string> check, string temp)
        {
            var full = BuildVpk(version: 2, new[] { ("vmt", "materials", "a"), ("vmt", "materials", "b") });
            var path = Path.Combine(temp, "truncado_dir.vpk");
            File.WriteAllBytes(path, full.Take(full.Length - 20).ToArray());

            var threw = false;
            try { VpkIndex.EnumerateEntries(path).ToList(); }
            catch (Exception) { threw = true; }

            check(!threw, "VPK truncado: deberia devolver lo leido, no lanzar excepcion");
        }

        private static void TestGameInfoSearchPaths(Action<bool, string> check, string temp)
        {
            var gameDir = Path.Combine(temp, "cstrike");
            Directory.CreateDirectory(gameDir);

            File.WriteAllText(Path.Combine(gameDir, "gameinfo.txt"), string.Join(Environment.NewLine, new[]
            {
                "\"GameInfo\"",
                "{",
                "    game \"Counter-Strike: Source\"",
                "    FileSystem",
                "    {",
                "        SteamAppId 240",
                "        SearchPaths",
                "        {",
                "            game+mod            |gameinfo_path|.",
                "            game                |all_source_engine_paths|hl2/hl2_textures.vpk",
                "            game                cstrike/cstrike_pak.vpk   // comentario ignorado",
                "            platform            |all_source_engine_paths|platform",
                "        }",
                "    }",
                "}",
            }));

            var values = GameInfo.ReadSearchPathValues(gameDir);

            check(values.Count == 4, $"gameinfo: se esperaban 4 SearchPaths, hay {values.Count} -> {string.Join(", ", values)}");
            check(values.Contains("|gameinfo_path|."), "gameinfo: falta |gameinfo_path|.");
            check(values.Contains("|all_source_engine_paths|hl2/hl2_textures.vpk"), "gameinfo: falta la ruta de hl2");
            check(values.Any(v => v.Contains("cstrike_pak.vpk")), "gameinfo: el comentario // rompio el parseo");
        }

        private static void TestSearchPathResolution(Action<bool, string> check, string temp)
        {
            var gameDir = Path.Combine(temp, "cstrike");

            check(GameInfo.ResolveSearchPath(gameDir, "|gameinfo_path|.") == gameDir,
                "resolucion: |gameinfo_path|. deberia dar la raiz del juego");

            check(GameInfo.ResolveSearchPath(gameDir, "GAME") == gameDir,
                "resolucion: GAME deberia dar la raiz del juego");

            var expected = Path.GetFullPath(Path.Combine(temp, "hl2"));
            check(GameInfo.ResolveSearchPath(gameDir, "|all_source_engine_paths|hl2") == expected,
                "resolucion: |all_source_engine_paths| deberia resolver contra el directorio padre");

            check(GameInfo.ResolveSearchPath(gameDir, "|all_source_engine_paths|*") == null,
                "resolucion: un comodin deberia descartarse");
        }

        private static void TestBaseIndexFindsVpk(Action<bool, string> check, string temp)
        {
            var gameDir = Path.Combine(temp, "cstrike");
            File.WriteAllBytes(Path.Combine(gameDir, "cstrike_pak_dir.vpk"), BuildVpk(2, new[]
            {
                ("vmt", "materials/base", "concrete"),
                ("vtf", "materials/base", "concrete"),
            }));

            var index = BaseGameIndex.Build(gameDir);
            check(index.Contains("materials/base/concrete.vmt"), "BaseGameIndex: no encontro el VPK de la carpeta del juego");
            check(!index.Contains("materials/custom/mio.vmt"), "BaseGameIndex: reporto una ruta que no existe");

            // Con filtro solo debe retener lo pedido
            var needed = new HashSet<string>(new[] { "materials/base/concrete.vtf" }, StringComparer.OrdinalIgnoreCase);
            var filtered = BaseGameIndex.Build(gameDir, needed);
            check(filtered.Contains("materials/base/concrete.vtf"), "BaseGameIndex filtrado: falta la ruta pedida");
            check(filtered.Count == 1, $"BaseGameIndex filtrado: retuvo {filtered.Count} entradas, se esperaba 1");
        }

        /// <summary>Construye un VPK valido en memoria (solo el arbol de directorios).</summary>
        private static byte[] BuildVpk(int version, (string Ext, string Dir, string Name)[] files)
        {
            // Agrupado por extension y luego por directorio, como exige el formato.
            var tree = new MemoryStream();
            foreach (var byExt in files.GroupBy(f => f.Ext))
            {
                WriteCString(tree, byExt.Key);
                foreach (var byDir in byExt.GroupBy(f => f.Dir))
                {
                    WriteCString(tree, byDir.Key);
                    foreach (var file in byDir)
                    {
                        WriteCString(tree, file.Name);
                        WriteUInt32(tree, 0);        // crc
                        WriteUInt16(tree, 0);        // preload bytes
                        WriteUInt16(tree, 0);        // archive index
                        WriteUInt32(tree, 0);        // entry offset
                        WriteUInt32(tree, 0);        // entry length
                        WriteUInt16(tree, 0xffff);   // terminator
                    }
                    tree.WriteByte(0);               // fin de archivos del directorio
                }
                tree.WriteByte(0);                   // fin de directorios de la extension
            }
            tree.WriteByte(0);                       // fin del arbol

            var treeBytes = tree.ToArray();

            var output = new MemoryStream();
            WriteUInt32(output, 0x55aa1234);
            WriteUInt32(output, (uint)version);
            WriteUInt32(output, (uint)treeBytes.Length);
            if (version == 2)
            {
                for (var i = 0; i < 4; i++) WriteUInt32(output, 0);
            }
            output.Write(treeBytes, 0, treeBytes.Length);
            return output.ToArray();
        }

        private static void WriteCString(Stream s, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            s.Write(bytes, 0, bytes.Length);
            s.WriteByte(0);
        }

        private static void WriteUInt16(Stream s, ushort v)
        {
            s.WriteByte((byte)(v & 0xFF));
            s.WriteByte((byte)((v >> 8) & 0xFF));
        }

        private static void WriteUInt32(Stream s, uint v)
        {
            s.WriteByte((byte)(v & 0xFF));
            s.WriteByte((byte)((v >> 8) & 0xFF));
            s.WriteByte((byte)((v >> 16) & 0xFF));
            s.WriteByte((byte)((v >> 24) & 0xFF));
        }
    }
}
