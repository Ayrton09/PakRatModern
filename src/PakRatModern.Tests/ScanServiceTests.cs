using System;
using System.IO;
using System.Linq;
using System.Text;
using PakRatModern.Core;

namespace PakRatModern.Tests
{
    /// <summary>
    /// Prueba de integracion: arma un juego falso en disco y verifica que cada
    /// archivo termine en la categoria correcta.
    /// </summary>
    internal static class ScanServiceTests
    {
        public static void Run(Action<bool, string> check)
        {
            var temp = Path.Combine(Path.GetTempPath(), "pakrat_scan_" + Guid.NewGuid().ToString("N"));
            var gameRoot = Path.Combine(temp, "cstrike");
            Directory.CreateDirectory(gameRoot);

            try
            {
                // --- Contenido en disco (el mapper lo tiene, el jugador no) ---
                WriteFile(gameRoot, "materials/custom/wall.vmt",
                    "\"LightmappedGeneric\"\n{\n\"$basetexture\" \"custom/wall\"\n\"$bumpmap\" \"custom/wall_n\"\n}");
                WriteFile(gameRoot, "materials/custom/wall.vtf", "vtf-data");
                WriteFile(gameRoot, "materials/custom/wall_n.vtf", "vtf-normal");

                // --- VPK del juego base: esto NO hay que empaquetar ---
                WriteVpk(Path.Combine(gameRoot, "cstrike_pak_dir.vpk"),
                    ("vmt", "materials/base", "concrete"));

                // --- BSP que referencia las tres cosas ---
                var entities = "{\n\"classname\" \"worldspawn\"\n}\n" +
                               "{\n\"classname\" \"infodecal\"\n\"texture\" \"custom/wall\"\n}\n";

                var texNames = new[] { "base/concrete", "custom/faltante" };
                BuildTexData(texNames, out var strData, out var strTable);

                var bsp = SyntheticBsp.Build(
                    entities: Encoding.ASCII.GetBytes(entities),
                    texDataStringData: strData,
                    texDataStringTable: strTable);

                var pak = PakArchive.NewEntryMap();
                var service = new ScanService(pak, gameRoot);
                var result = service.Scan(bsp, "de_test", includeExtras: false);

                ScanRow Row(string path) => result.Rows.FirstOrDefault(r =>
                    string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));

                // El material del decal esta en disco -> se puede agregar
                var wall = Row("materials/custom/wall.vmt");
                check(wall != null && wall.Status == ScanStatus.CanAdd,
                    $"scan: wall.vmt deberia ser 'Can add', es '{wall?.StatusText ?? "ausente"}'");

                // Sus texturas se descubren siguiendo el .vmt
                var wallVtf = Row("materials/custom/wall.vtf");
                check(wallVtf != null && wallVtf.Status == ScanStatus.CanAdd,
                    $"scan: no siguio $basetexture -> {wallVtf?.StatusText ?? "ausente"}");

                var normal = Row("materials/custom/wall_n.vtf");
                check(normal != null && normal.Status == ScanStatus.CanAdd,
                    $"scan: no siguio $bumpmap -> {normal?.StatusText ?? "ausente"}");

                // Lo que trae el juego base no debe contarse como faltante
                var concrete = Row("materials/base/concrete.vmt");
                check(concrete != null && concrete.Status == ScanStatus.BaseGameVpk,
                    $"scan: concrete.vmt deberia venir del VPK base, es '{concrete?.StatusText ?? "ausente"}'");

                // Referenciado pero inexistente: el mapa se va a romper
                var missing = Row("materials/custom/faltante.vmt");
                check(missing != null && missing.Status == ScanStatus.MissingOnDisk,
                    $"scan: faltante.vmt deberia ser 'Missing on disk', es '{missing?.StatusText ?? "ausente"}'");

                check(result.Summary.CanAdd == 3, $"resumen: se esperaban 3 agregables, hay {result.Summary.CanAdd}");
                check(result.Summary.NotFound == 1, $"resumen: se esperaba 1 no encontrado, hay {result.Summary.NotFound}");
                check(result.Summary.MissingTotal == 4, $"resumen: se esperaban 4 faltantes, hay {result.Summary.MissingTotal}");
                check(result.Summary.AlreadyInPak == 0, $"resumen: se esperaban 0 ya empaquetados, hay {result.Summary.AlreadyInPak}");

                // --- Con el material ya dentro del PAK cambia de categoria ---
                pak["materials/custom/wall.vmt"] = new PakEntry("materials/custom/wall.vmt",
                    Encoding.ASCII.GetBytes("\"UnlitGeneric\"\n{\n}"));

                var second = new ScanService(pak, gameRoot).Scan(bsp, "de_test", includeExtras: false);
                var packed = second.Rows.First(r => r.Path == "materials/custom/wall.vmt");

                check(packed.Status == ScanStatus.AlreadyInPak,
                    $"scan: con el .vmt empaquetado deberia ser 'Already in PAK', es '{packed.StatusText}'");
                check(second.Summary.AlreadyInPak == 1,
                    $"resumen: se esperaba 1 ya empaquetado, hay {second.Summary.AlreadyInPak}");
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch (IOException) { }
            }
        }

        private static void BuildTexData(string[] names, out byte[] strData, out byte[] strTable)
        {
            var data = new System.Collections.Generic.List<byte>();
            var table = new System.Collections.Generic.List<byte>();

            foreach (var name in names)
            {
                table.AddRange(BitConverter.GetBytes(data.Count));
                data.AddRange(Encoding.ASCII.GetBytes(name));
                data.Add(0);
            }

            strData = data.ToArray();
            strTable = table.ToArray();
        }

        private static void WriteFile(string root, string relative, string content)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        private static void WriteVpk(string path, params (string Ext, string Dir, string Name)[] files)
        {
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
                        WriteUInt32(tree, 0);
                        WriteUInt16(tree, 0);
                        WriteUInt16(tree, 0);
                        WriteUInt32(tree, 0);
                        WriteUInt32(tree, 0);
                        WriteUInt16(tree, 0xffff);
                    }
                    tree.WriteByte(0);
                }
                tree.WriteByte(0);
            }
            tree.WriteByte(0);

            var treeBytes = tree.ToArray();
            var output = new MemoryStream();
            WriteUInt32(output, 0x55aa1234);
            WriteUInt32(output, 2);
            WriteUInt32(output, (uint)treeBytes.Length);
            for (var i = 0; i < 4; i++) WriteUInt32(output, 0);
            output.Write(treeBytes, 0, treeBytes.Length);

            File.WriteAllBytes(path, output.ToArray());
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
