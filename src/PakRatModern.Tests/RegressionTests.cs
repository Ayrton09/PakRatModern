using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using PakRatModern.Core;

namespace PakRatModern.Tests
{
    /// <summary>
    /// Pruebas nacidas de la auditoria de 1.3.0. Cada una cubre un caso que
    /// antes pasaba sin control: ZIP que miente sobre su tamano, lumps que
    /// solapan la cabecera, duplicados con contenido distinto, ramas de
    /// ApplyPak sin ejercitar y SearchPaths de gameinfo.txt en disco.
    /// </summary>
    internal static class RegressionTests
    {
        public static void Run(Action<bool, string> check)
        {
            TestLyingZipIsRefused(check);
            TestDeflatePakIsReadAndRewrittenStored(check);
            TestDuplicateWithDifferentContentIsFlagged(check);
            TestLumpInsideHeaderIsRejected(check);
            TestGameLumpAfterPakBlocksResize(check);
            TestPakInsertedWhenLumpEmpty(check);
            TestHugeCentralDirectoryOffsetIsHandled(check);
            TestScanFindsFilesInCustomSearchPath(check);
            TestExtractWithoutOverwriteKeepsExisting(check);
        }

        /// <summary>
        /// ZipArchive de .NET Framework no corta la descompresion al llegar al
        /// tamano declarado: un ZIP de 1 KB que dice "10 bytes" puede inflar
        /// cualquier cosa. El lector tiene que contar lo que sale de verdad.
        /// </summary>
        private static void TestLyingZipIsRefused(Action<bool, string> check)
        {
            var real = new byte[200_000];
            for (var i = 0; i < real.Length; i++) real[i] = (byte)'A';

            var zip = BuildZip(CompressionLevel.Optimal, ("materials/a.vmt", real));

            // Se declara un tamano menor en el directorio central y en la
            // cabecera local; el stream Deflate sigue conteniendo 200 000 bytes.
            var eocd = zip.Length - 22;
            var centralOffset = (int)BitConverter.ToUInt32(zip, eocd + 16);
            var patched = (byte[])zip.Clone();
            BitConverter.GetBytes(10u).CopyTo(patched, centralOffset + 24);   // central: uncompressed size
            BitConverter.GetBytes(10u).CopyTo(patched, 22);                   // local:   uncompressed size

            var refused = false;
            try { PakArchive.Read(patched); }
            catch (InvalidDataException) { refused = true; }
            check(refused, "zip mentiroso: el lector inflo mas bytes de los declarados sin rechazarlo");

            // El ZIP sin manipular se lee entero
            var entries = PakArchive.Read(zip);
            check(entries.Count == 1 && entries["materials/a.vmt"].Size == real.Length,
                "zip mentiroso: el ZIP honesto no se leyo completo");
        }

        /// <summary>
        /// Casi todos los mapas publicados traen el PAK en Deflate. Tiene que
        /// abrirse y, al reescribirse, salir Stored.
        /// </summary>
        private static void TestDeflatePakIsReadAndRewrittenStored(Action<bool, string> check)
        {
            var zip = BuildZip(CompressionLevel.Optimal,
                ("materials/x.vmt", Encoding.ASCII.GetBytes("\"LightmappedGeneric\"{}")),
                ("models/y.mdl", Enumerable.Range(0, 5000).Select(i => (byte)(i % 7)).ToArray()));

            var methods = ZipInspector.GetCompressionMethods(zip);
            check(methods.ContainsKey(8), "deflate: el fixture deberia estar comprimido con Deflate");

            var entries = PakArchive.Read(zip);
            check(entries.Count == 2, $"deflate: se esperaban 2 entradas, hay {entries.Count}");
            check(entries["models/y.mdl"].Size == 5000, "deflate: contenido descomprimido incompleto");

            var rewritten = PakArchive.Write(entries);
            var after = ZipInspector.GetCompressionMethods(rewritten);
            check(after.Count == 1 && after.ContainsKey(0), "deflate: al reescribir no quedo todo Stored");
        }

        private static void TestDuplicateWithDifferentContentIsFlagged(Action<bool, string> check)
        {
            var zip = BuildZip(CompressionLevel.NoCompression,
                ("materials/dup.vmt", Encoding.ASCII.GetBytes("first")),
                ("materials/same.vmt", Encoding.ASCII.GetBytes("x")),
                ("materials/SAME.vmt", Encoding.ASCII.GetBytes("x")),
                ("materials/DUP.vmt", Encoding.ASCII.GetBytes("second")));

            var entries = PakArchive.Read(zip, out _, out var duplicates);

            check(duplicates.Count == 2, $"duplicados: se esperaban 2, hay {duplicates.Count}");
            check(duplicates.Any(d => d.StartsWith("materials/DUP.vmt") && d.Contains("different content")),
                $"duplicados: no se marco el de contenido distinto -> {string.Join(" | ", duplicates)}");
            check(duplicates.Any(d => d == "materials/SAME.vmt"),
                "duplicados: marco como distinto uno identico");

            // Politica documentada: gana la ultima copia, se conserva el primer nombre.
            check(entries.ContainsKey("materials/dup.vmt") &&
                  Encoding.ASCII.GetString(entries["materials/dup.vmt"].Data) == "second",
                "duplicados: no se aplico la politica 'gana la ultima copia'");
            check(entries.Values.Any(e => e.FullPath == "materials/dup.vmt"),
                "duplicados: no se conservo la capitalizacion de la primera aparicion");
        }

        private static void TestLumpInsideHeaderIsRejected(Action<bool, string> check)
        {
            var bsp = SyntheticBsp.Build(entities: Encoding.ASCII.GetBytes("{}"));
            var raw = (byte[])bsp.Raw.Clone();

            // Se apunta el lump de entidades al byte 100, dentro de la cabecera.
            var offset = 8 + PakLimits.EntitiesLumpIndex * 16;
            BitConverter.GetBytes(100).CopyTo(raw, offset);

            var rejected = false;
            try { BspFile.Parse(raw); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "cabecera: acepto un lump que empieza dentro de la cabecera");
        }

        /// <summary>El guard existe desde 1.0 pero nunca se habia probado que salte.</summary>
        private static void TestGameLumpAfterPakBlocksResize(Action<bool, string> check)
        {
            var entries = PakArchive.NewEntryMap();
            entries["a.txt"] = new PakEntry("a.txt", new byte[] { 1, 2, 3 });
            var pak = PakArchive.Write(entries);

            var lumps = new Lump[PakLimits.LumpCount];
            for (var i = 0; i < lumps.Length; i++) lumps[i] = new Lump();

            var body = new List<byte>();
            void Place(int idx, byte[] data)
            {
                while (body.Count % 4 != 0) body.Add(0);
                lumps[idx].FileOfs = PakLimits.HeaderSize + body.Count;
                lumps[idx].FileLen = data.Length;
                body.AddRange(data);
            }

            Place(PakLimits.PakLumpIndex, pak);
            Place(PakLimits.GameLumpIndex, new byte[32]);       // DESPUES del PAK

            var raw = new byte[PakLimits.HeaderSize + body.Count];
            BspFile.SerializeHeader(21, 1, lumps).CopyTo(raw, 0);
            body.CopyTo(0, raw, PakLimits.HeaderSize, body.Count);

            var bsp = BspFile.Parse(raw);

            // Mismo tamano: no hay delta, se permite
            var same = false;
            try { bsp.ApplyPak(pak); same = true; } catch (InvalidOperationException) { }
            check(same, "game lump: bloqueo una reescritura sin cambio de tamano");

            // Distinto tamano: debe bloquear
            entries["b.txt"] = new PakEntry("b.txt", new byte[64]);
            var blocked = false;
            try { bsp.ApplyPak(PakArchive.Write(entries)); } catch (InvalidOperationException) { blocked = true; }
            check(blocked, "game lump: permitio redimensionar el PAK con LUMP_GAME_LUMP detras");
        }

        private static void TestPakInsertedWhenLumpEmpty(Action<bool, string> check)
        {
            // BSP sin PAK y con un tamano que no es multiplo de 4
            var bsp = SyntheticBsp.Build(entities: Encoding.ASCII.GetBytes("{\"classname\" \"worldspawn\"}\n"));
            check(bsp.Lumps[PakLimits.PakLumpIndex].FileLen == 0, "insercion: el fixture deberia no tener PAK");

            var entries = PakArchive.NewEntryMap();
            entries["materials/n.vmt"] = new PakEntry("materials/n.vmt", new byte[] { 9 });
            var pak = PakArchive.Write(entries);

            var updated = BspFile.Parse(bsp.ApplyPak(pak));
            var lump = updated.Lumps[PakLimits.PakLumpIndex];

            check(lump.FileLen == pak.Length, "insercion: filelen incorrecto");
            check(lump.FileOfs % 4 == 0, "insercion: el PAK quedo desalineado");
            check(updated.ReadPakLump().SequenceEqual(pak), "insercion: el PAK leido no coincide con el escrito");
            check(PakArchive.Read(updated.ReadPakLump()).Count == 1, "insercion: el PAK insertado no se lee");
        }

        private static void TestHugeCentralDirectoryOffsetIsHandled(Action<bool, string> check)
        {
            var zip = PakArchive.Write(SampleOne());
            var patched = (byte[])zip.Clone();
            var eocd = patched.Length - 22;
            BitConverter.GetBytes(0x7FFFFFF0u).CopyTo(patched, eocd + 16);   // offset cerca de int.MaxValue

            var outcome = "ok";
            try { PakArchive.Read(patched); }
            catch (InvalidDataException) { outcome = "invalid"; }
            catch (Exception ex) { outcome = ex.GetType().Name; }

            check(outcome == "invalid", $"eocd: un offset enorme deberia dar InvalidDataException, dio {outcome}");
        }

        /// <summary>
        /// gameinfo.txt con <c>custom/*</c>: el archivo vive en
        /// cstrike/custom/mimod/materials/... y el scan tiene que encontrarlo
        /// y empaquetarlo como materials/..., que es como lo carga el motor.
        /// </summary>
        private static void TestScanFindsFilesInCustomSearchPath(Action<bool, string> check)
        {
            var temp = Path.Combine(Path.GetTempPath(), "pakrat_custom_" + Guid.NewGuid().ToString("N"));
            var gameRoot = Path.Combine(temp, "cstrike");
            Directory.CreateDirectory(gameRoot);

            try
            {
                File.WriteAllText(Path.Combine(gameRoot, "gameinfo.txt"), string.Join("\n", new[]
                {
                    "\"GameInfo\"", "{", "  FileSystem", "  {", "    SearchPaths", "    {",
                    "      game+mod  |gameinfo_path|custom/*",
                    "      game+mod  |gameinfo_path|.",
                    "    }", "  }", "}",
                }));

                var modMaterials = Path.Combine(gameRoot, "custom", "mimod", "materials", "custom");
                Directory.CreateDirectory(modMaterials);
                File.WriteAllText(Path.Combine(modMaterials, "wall.vmt"), "\"LightmappedGeneric\"\n{\n\"$basetexture\" \"custom/wall\"\n}");
                File.WriteAllText(Path.Combine(modMaterials, "wall.vtf"), "vtf");

                var dirs = GameInfo.ResolveSearchDirectories(gameRoot);
                check(dirs.Any(d => d.EndsWith(Path.Combine("custom", "mimod"), StringComparison.OrdinalIgnoreCase)),
                    $"searchpaths: no expandio custom/* -> {string.Join(" | ", dirs)}");
                check(string.Equals(dirs[0].TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase),
                    "searchpaths: el Game Path deberia ir primero");

                var entities = "{\n\"classname\" \"infodecal\"\n\"texture\" \"custom/wall\"\n}\n";
                var bsp = SyntheticBsp.Build(entities: Encoding.ASCII.GetBytes(entities));

                var result = new ScanService(PakArchive.NewEntryMap(), gameRoot).Scan(bsp, "de_test", includeExtras: false);
                var vmt = result.Rows.FirstOrDefault(r => r.Path == "materials/custom/wall.vmt");
                var vtf = result.Rows.FirstOrDefault(r => r.Path == "materials/custom/wall.vtf");

                check(vmt != null && vmt.Status == ScanStatus.CanAdd,
                    $"scan custom/: wall.vmt deberia ser 'Can add', es '{vmt?.StatusText ?? "ausente"}'");
                check(vtf != null && vtf.Status == ScanStatus.CanAdd,
                    $"scan custom/: no siguio el $basetexture de un .vmt que esta en custom/ -> {vtf?.StatusText ?? "ausente"}");
                check(vmt != null && vmt.FullDiskPath.IndexOf("mimod", StringComparison.OrdinalIgnoreCase) >= 0,
                    "scan custom/: FullDiskPath no apunta al archivo real dentro de custom/");
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch (IOException) { }
            }
        }

        private static void TestExtractWithoutOverwriteKeepsExisting(Action<bool, string> check)
        {
            var temp = Path.Combine(Path.GetTempPath(), "pakrat_extract_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);

            try
            {
                var bspPath = Path.Combine(temp, "m.bsp");
                var entries = PakArchive.NewEntryMap();
                entries["materials/k.vmt"] = new PakEntry("materials/k.vmt", Encoding.ASCII.GetBytes("packed"));
                File.WriteAllBytes(bspPath, SyntheticBsp.Build(pak: PakArchive.Write(entries)).Raw);

                var doc = PakDocument.Open(bspPath);
                var outDir = Path.Combine(temp, "out");
                var target = Path.Combine(outDir, "materials", "k.vmt");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllText(target, "mine");

                var existing = doc.FindExistingExtractionTargets(outDir, doc.Entries.Keys.ToList());
                check(existing.Count == 1, $"extract: deberia detectar 1 archivo existente, detecto {existing.Count}");

                var written = doc.ExtractTo(outDir, doc.Entries.Keys.ToList(), overwrite: false);
                check(written == 0 && File.ReadAllText(target) == "mine", "extract: piso un archivo existente con overwrite=false");

                written = doc.ExtractTo(outDir, doc.Entries.Keys.ToList(), overwrite: true);
                check(written == 1 && File.ReadAllText(target) == "packed", "extract: no sobrescribio con overwrite=true");
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch (IOException) { }
            }
        }

        private static Dictionary<string, PakEntry> SampleOne()
        {
            var entries = PakArchive.NewEntryMap();
            entries["a.txt"] = new PakEntry("a.txt", new byte[] { 1 });
            return entries;
        }

        private static byte[] BuildZip(CompressionLevel level, params (string Name, byte[] Data)[] files)
        {
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (var (name, data) in files)
                    {
                        var entry = zip.CreateEntry(name, level);
                        using (var s = entry.Open()) s.Write(data, 0, data.Length);
                    }
                }
                return ms.ToArray();
            }
        }
    }
}
