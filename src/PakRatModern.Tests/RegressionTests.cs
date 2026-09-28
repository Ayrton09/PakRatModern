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
    /// Casos que antes pasaban sin control, cada uno con su prueba: ZIP que
    /// miente sobre su tamano o tiene el CRC danado, lumps que solapan la
    /// cabecera, duplicados con contenido distinto, ramas de ApplyPak sin
    /// ejercitar, SearchPaths de gameinfo.txt en disco, guardar sobre un BSP
    /// recompilado, nombres sin flag UTF-8, la tabla de lumps de L4D2 y los
    /// falsos faltantes del scan.
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
            TestSaveRefusesFileChangedOnDisk(check);
            TestCrcMismatchIsRejected(check);
            TestLegacyNamesKeepTheirBytes(check);
            TestLeft4Dead2Layout(check);
            TestScanSkipsInvalidPathCharacters(check);
            TestScanResolvesModelMaterialsLikeTheEngine(check);
            TestScanFollowsParticlesAndSoundscapes(check);
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

                var dirs = GameInfo.ResolveSearchDirectories(gameRoot).ToList();
                var customIndex = dirs.FindIndex(d => d.EndsWith(Path.Combine("custom", "mimod"), StringComparison.OrdinalIgnoreCase));
                var rootIndex = dirs.FindIndex(d => string.Equals(d, Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
                check(customIndex >= 0, $"searchpaths: no expandio custom/* -> {string.Join(" | ", dirs)}");

                // gameinfo.txt lista custom/* antes que |gameinfo_path|., y el motor
                // respeta ese orden: una copia en custom/ gana sobre la del juego.
                check(customIndex >= 0 && rootIndex > customIndex,
                    $"searchpaths: no respeto el orden de gameinfo.txt -> {string.Join(" | ", dirs)}");

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

        /// <summary>
        /// El documento tiene el BSP entero en memoria. Si Hammer lo recompila
        /// mientras esta abierto, guardar escribia la version vieja encima.
        /// </summary>
        private static void TestSaveRefusesFileChangedOnDisk(Action<bool, string> check)
        {
            var temp = NewTemp("pakrat_stale_");
            try
            {
                var path = Path.Combine(temp, "m.bsp");
                var pak = PakArchive.Write(SampleOne());
                File.WriteAllBytes(path, SyntheticBsp.Build(entities: Encoding.ASCII.GetBytes("{\"note\" \"A\"}\n"), pak: pak).Raw);

                var doc = PakDocument.Open(path);
                check(!doc.HasChangedOnDisk(), "cambio en disco: un documento recien abierto no deberia estar desactualizado");

                var recompiled = SyntheticBsp.Build(entities: Encoding.ASCII.GetBytes("{\"note\" \"B, recompilado\"}\n"), pak: pak).Raw;
                File.WriteAllBytes(path, recompiled);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
                check(doc.HasChangedOnDisk(), "cambio en disco: no detecto la recompilacion");

                doc.AddOrReplace("materials/b.vmt", new byte[] { 1 });
                var refused = false;
                try { doc.Save(path, createBackup: true); }
                catch (FileChangedOnDiskException) { refused = true; }
                check(refused, "cambio en disco: guardo encima de la recompilacion sin avisar");
                check(File.ReadAllBytes(path).SequenceEqual(recompiled), "cambio en disco: la recompilacion quedo modificada");
                check(!File.Exists(path + ".bak"), "cambio en disco: hizo el .bak de un guardado que no ocurrio");

                doc.Save(path, createBackup: false, overwriteChangedFile: true);
                check(!doc.HasChangedOnDisk(), "cambio en disco: tras guardar el documento deberia quedar al dia");

                // Tocado con el mismo contenido (otra fecha, mismos bytes): no es un cambio
                File.WriteAllBytes(path, File.ReadAllBytes(path));
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(10));
                check(!doc.HasChangedOnDisk(), "cambio en disco: un archivo tocado con el mismo contenido no deberia contar");

                // Guardar como otro archivo no se bloquea
                File.WriteAllBytes(path, recompiled);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(15));
                var copy = Path.Combine(temp, "copia.bsp");
                var savedAs = true;
                try { doc.Save(copy, createBackup: false); }
                catch (FileChangedOnDiskException) { savedAs = false; }
                check(savedAs && File.Exists(copy), "cambio en disco: bloqueo un Save As a otro archivo");
            }
            finally
            {
                TryDelete(temp);
            }
        }

        /// <summary>
        /// ZipArchive de .NET Framework no verificaba el CRC: un PAK danado se
        /// abria, Verify decia que era valido y al guardar el dano quedaba con
        /// un CRC nuevo que lo escondia.
        /// </summary>
        private static void TestCrcMismatchIsRejected(Action<bool, string> check)
        {
            var entries = PakArchive.NewEntryMap();
            entries["materials/c.vmt"] = new PakEntry("materials/c.vmt", Encoding.ASCII.GetBytes("payload"));
            var damaged = PakArchive.Write(entries);
            damaged[IndexOf(damaged, Encoding.ASCII.GetBytes("payload"))] ^= 0x20;

            string message = null;
            try { PakArchive.Read(damaged); }
            catch (InvalidDataException ex) { message = ex.Message; }
            check(message != null && message.Contains("CRC"), $"crc: acepto un PAK danado -> {message ?? "sin error"}");
        }

        /// <summary>
        /// vbsp y bspzip guardan los nombres con los bytes del sistema del mapper
        /// y sin flag UTF-8; el motor busca esos bytes. Pasarlos a UTF-8 al
        /// guardar hacia que el motor dejara de encontrar el archivo.
        /// </summary>
        private static void TestLegacyNamesKeepTheirBytes(Action<bool, string> check)
        {
            var legacyName = ZipInspector.Latin1.GetBytes("materials/café.vmt");
            var utf8Name = Encoding.UTF8.GetBytes("materials/señal.vmt");
            var zip = RawZip((legacyName, 0, "latin1"), (utf8Name, 0x0800, "utf8"));

            var entries = PakArchive.Read(zip);
            check(entries.TryGetValue("materials/café.vmt", out var legacy) && legacy.LegacyName,
                "nombres: no leyo el nombre sin flag como Latin-1");
            check(entries.TryGetValue("materials/señal.vmt", out var utf8) && !utf8.LegacyName,
                "nombres: marco como heredado un nombre con flag UTF-8");

            var written = ZipInspector.ReadCentralDirectory(PakArchive.Write(entries)).ToDictionary(e => e.Name);
            check(written["materials/café.vmt"].NameBytes.SequenceEqual(legacyName) && !written["materials/café.vmt"].IsUtf8,
                "nombres: guardar cambio los bytes de un nombre sin flag UTF-8");
            check(written["materials/señal.vmt"].NameBytes.SequenceEqual(utf8Name) && written["materials/señal.vmt"].IsUtf8,
                "nombres: guardar cambio un nombre UTF-8");

            // Renombrar y reemplazar conservan la forma en que vino el nombre
            var temp = NewTemp("pakrat_names_");
            try
            {
                var path = Path.Combine(temp, "m.bsp");
                File.WriteAllBytes(path, SyntheticBsp.Build(pak: zip).Raw);
                var doc = PakDocument.Open(path);
                doc.AddOrReplace("materials/café.vmt", Encoding.ASCII.GetBytes("nuevo"));
                check(doc.Entries["materials/café.vmt"].LegacyName, "nombres: reemplazar el contenido cambio la codificacion del nombre");
                doc.Rename("materials/café.vmt", "materials/otra/café.vmt");
                check(doc.Entries["materials/otra/café.vmt"].LegacyName, "nombres: renombrar cambio la codificacion del nombre");
            }
            finally
            {
                TryDelete(temp);
            }
        }

        /// <summary>
        /// Left 4 Dead 2 pone la version primero en cada entrada de la tabla de
        /// lumps. Antes se rechazaba con "Lump 0 out of range", que parece un
        /// archivo corrupto; ahora se lee y al guardar se conserva ese orden.
        /// </summary>
        private static void TestLeft4Dead2Layout(Action<bool, string> check)
        {
            var standard = SyntheticBsp.Build(entities: Encoding.ASCII.GetBytes("{}\n"), pak: PakArchive.Write(SampleOne()));

            var lumps = standard.Lumps.Select(l => l.Clone()).ToArray();
            lumps[PakLimits.PakLumpIndex].Version = 1;
            var raw = (byte[])standard.Raw.Clone();
            BspFile.SerializeHeader(standard.Version, standard.MapRevision, lumps, LumpLayout.VersionFirst).CopyTo(raw, 0);

            BspFile bsp = null;
            try { bsp = BspFile.Parse(raw); }
            catch (InvalidDataException ex) { check(false, $"l4d2: rechazo la tabla de lumps -> {ex.Message}"); return; }

            check(bsp.Layout == LumpLayout.VersionFirst, "l4d2: no detecto el orden de L4D2");
            check(PakArchive.Read(bsp.ReadPakLump()).Count == 1, "l4d2: no leyo el PAK");

            var entries = PakArchive.Read(bsp.ReadPakLump());
            entries["materials/nuevo.vmt"] = new PakEntry("materials/nuevo.vmt", new byte[40]);
            var saved = bsp.ApplyPak(PakArchive.Write(entries));
            var reparsed = BspFile.Parse(saved);

            var at = 8 + PakLimits.PakLumpIndex * 16;
            check(reparsed.Layout == LumpLayout.VersionFirst &&
                  BitConverter.ToInt32(saved, at) == 1 &&
                  BitConverter.ToInt32(saved, at + 4) == reparsed.Lumps[PakLimits.PakLumpIndex].FileOfs,
                "l4d2: guardar cambio el orden de la tabla de lumps");
            check(PakArchive.Read(reparsed.ReadPakLump()).Count == 2, "l4d2: el PAK guardado no se lee");
        }

        /// <summary>Una sola referencia con '|' abortaba el scan entero.</summary>
        private static void TestScanSkipsInvalidPathCharacters(Action<bool, string> check)
        {
            var temp = NewTemp("pakrat_badchar_");
            try
            {
                var bsp = SyntheticBsp.Build(entities: Encoding.ASCII.GetBytes(
                    "{\n\"classname\" \"prop_dynamic\"\n\"model\" \"models/props/a|b.mdl\"\n}\n"));

                ScanResult result = null;
                try { result = new ScanService(PakArchive.NewEntryMap(), temp).Scan(bsp, "x", includeExtras: false); }
                catch (ArgumentException ex) { check(false, $"caracteres invalidos: el scan se aborto -> {ex.Message}"); return; }

                check(result.Rows.Any(r => r.Path == "models/props/a|b.mdl" && r.Status == ScanStatus.MissingOnDisk),
                    "caracteres invalidos: la referencia deberia listarse como faltante");
            }
            finally
            {
                TryDelete(temp);
            }
        }

        /// <summary>
        /// Texturas de modelo como las resuelve el motor: por cada textura, la
        /// primera ruta de $cdmaterials que existe, y directorio + nombre aunque
        /// el nombre traiga subcarpetas. Sin render targets ni archivos
        /// opcionales del modelo como falsos faltantes.
        /// </summary>
        private static void TestScanResolvesModelMaterialsLikeTheEngine(Action<bool, string> check)
        {
            var temp = NewTemp("pakrat_mdl_");
            var game = Path.Combine(temp, "cstrike");
            try
            {
                Write(game, "models/props/m/crate.mdl", BuildMdl(new[] { "crate" }, new[] { "models/props/m/", "models/props/shared/" }));
                Write(game, "models/props/m/crate.vvd", "vvd");
                Write(game, "models/props/m/crate.dx90.vtx", "vtx");
                Write(game, "materials/models/props/m/crate.vmt", "\"VertexLitGeneric\"\n{\n\"$basetexture\" \"models/props/m/crate\"\n}\n");
                Write(game, "models/props/m/panel.mdl", BuildMdl(new[] { "sub/panel" }, new[] { "models/props/m/" }));
                Write(game, "materials/models/props/m/sub/panel.vmt", "\"VertexLitGeneric\"\n{\n}\n");
                Write(game, "models/props/m/ghost.mdl", BuildMdl(new[] { "ghost" }, new[] { "models/props/a/", "models/props/b/" }));
                Write(game, "models/props/m/stock.mdl", BuildMdl(new[] { "stock" }, new[] { "models/props/a/", "models/props/base/" }));
                ScanServiceTests.WriteVpk(Path.Combine(game, "cstrike_pak_dir.vpk"), ("vmt", "materials/models/props/base", "stock"));
                Write(game, "materials/nature/water.vmt",
                    "\"Water\"\n{\n\"$reflecttexture\" \"_rt_WaterReflection\"\n\"$basetexture\" \"_rt_Camera\"\n}\n");

                var entities = string.Concat(new[] { "crate", "panel", "ghost", "stock" }.Select(m =>
                    $"{{\n\"classname\" \"prop_dynamic\"\n\"model\" \"models/props/m/{m}.mdl\"\n}}\n"));
                var bsp = SyntheticBsp.Build(
                    entities: Encoding.ASCII.GetBytes(entities),
                    texDataStringData: Encoding.ASCII.GetBytes("nature/water\0"),
                    texDataStringTable: BitConverter.GetBytes(0));

                var rows = new ScanService(PakArchive.NewEntryMap(), game).Scan(bsp, "x", includeExtras: false).Rows;
                ScanRow Row(string p) => rows.FirstOrDefault(r => string.Equals(r.Path, p, StringComparison.OrdinalIgnoreCase));

                check(Row("materials/models/props/m/crate.vmt")?.Status == ScanStatus.CanAdd,
                    "mdl: no encontro la textura en el primer $cdmaterials");
                check(Row("materials/models/props/shared/crate.vmt") == null,
                    "mdl: reporto como faltante una ruta que el motor nunca busca");
                check(Row("materials/models/props/m/sub/panel.vmt")?.Status == ScanStatus.CanAdd,
                    "mdl: no compuso $cdmaterials con un nombre de textura con subcarpeta");
                check(Row("materials/sub/panel.vmt") == null, "mdl: busco la textura con subcarpeta fuera de $cdmaterials");
                check(Row("materials/models/props/a/ghost.vmt")?.Status == ScanStatus.MissingOnDisk &&
                      Row("materials/models/props/b/ghost.vmt") == null,
                    "mdl: una textura inexistente deberia reportarse una sola vez");
                check(Row("materials/models/props/base/stock.vmt")?.Status == ScanStatus.BaseGameVpk &&
                      Row("materials/models/props/a/stock.vmt") == null,
                    "mdl: no eligio la ruta que trae el juego");

                check(!rows.Any(r => r.Path.IndexOf("_rt_", StringComparison.OrdinalIgnoreCase) >= 0),
                    "scan: reporto render targets (_rt_) como archivos");
                check(!rows.Any(r => GameReference.IsOptionalModelCompanion(r.Path) && r.Status == ScanStatus.MissingOnDisk),
                    "scan: reporto .phy/.dx80.vtx/.sw.vtx opcionales como faltantes");
                check(Row("models/props/m/crate.vvd")?.Status == ScanStatus.CanAdd,
                    "scan: dejo de pedir el .vvd, que si es obligatorio");
            }
            finally
            {
                TryDelete(temp);
            }
        }

        /// <summary>
        /// El manifiesto de particulas y el soundscape se empaquetaban sin lo que
        /// listan: el mapa salia sin particulas ni sonido ambiente.
        /// </summary>
        private static void TestScanFollowsParticlesAndSoundscapes(Action<bool, string> check)
        {
            var temp = NewTemp("pakrat_fx_");
            var game = Path.Combine(temp, "cstrike");
            try
            {
                Write(game, "maps/de_fx_particles.txt", "particles_manifest\n{\n\"file\" \"!particles/fx.pcf\"\n}\n");
                Write(game, "particles/fx.pcf", "<!-- dmx encoding binary 2 format pcf 1 -->\n\0material\0effects/fx/spark.vmt\0model\0models/fx/debris.mdl\0");
                Write(game, "materials/effects/fx/spark.vmt", "\"SpriteCard\"\n{\n\"$basetexture\" \"effects/fx/spark\"\n}\n");
                Write(game, "materials/effects/fx/spark.vtf", "vtf");
                Write(game, "scripts/soundscapes_de_fx.txt",
                    "\"de_fx.Wind\"\n{\n\"playlooping\"\n{\n\"wave\" \")ambient/fx/wind.wav\"\n}\n" +
                    "\"playrandom\"\n{\n\"rndwave\"\n{\n\"wave\" \"ambient/fx/gust1.wav\"\n}\n}\n}\n");
                Write(game, "sound/ambient/fx/wind.wav", "RIFF");
                Write(game, "sound/ambient/fx/gust1.wav", "RIFF");
                Write(game, "maps/de_fx_level_sounds.txt", "\"de_fx.Theme\"\n{\n\"wave\" \"#music/fx/theme.mp3\"\n\"wave\" \"!SENTENCE_NAME\"\n}\n");
                Write(game, "sound/music/fx/theme.mp3", "ID3");

                var rows = new ScanService(PakArchive.NewEntryMap(), game).Scan(SyntheticBsp.Build(), "de_fx", includeExtras: true).Rows;
                bool CanAdd(string p) => rows.Any(r => string.Equals(r.Path, p, StringComparison.OrdinalIgnoreCase) && r.Status == ScanStatus.CanAdd);

                check(CanAdd("maps/de_fx_particles.txt"), "fx: no encontro el manifiesto de particulas");
                check(CanAdd("particles/fx.pcf"), "fx: no siguio el manifiesto hasta el .pcf");
                check(CanAdd("materials/effects/fx/spark.vmt") && CanAdd("materials/effects/fx/spark.vtf"),
                    "fx: no siguio el .pcf hasta su material y su textura");
                check(rows.Any(r => r.Path == "models/fx/debris.mdl"), "fx: no vio el modelo que usa el .pcf");
                check(CanAdd("sound/ambient/fx/wind.wav") && CanAdd("sound/ambient/fx/gust1.wav"),
                    "fx: no siguio los wave del soundscape (con prefijo y dentro de rndwave)");
                check(CanAdd("sound/music/fx/theme.mp3"), "fx: no siguio los wave de level_sounds");
                check(!rows.Any(r => r.Path.IndexOf("SENTENCE", StringComparison.OrdinalIgnoreCase) >= 0),
                    "fx: trato una sentence (!NOMBRE) como archivo");
            }
            finally
            {
                TryDelete(temp);
            }
        }

        /// <summary>studiohdr_t minimo: solo lo que lee MdlReader.</summary>
        private static byte[] BuildMdl(string[] textures, string[] cdDirs)
        {
            var blob = new List<byte>(new byte[240]);
            var textureIndex = blob.Count;
            blob.AddRange(new byte[64 * textures.Length]);
            var cdIndex = blob.Count;
            blob.AddRange(new byte[4 * cdDirs.Length]);

            var raw = blob.ToArray();
            var tail = new List<byte>();
            for (var i = 0; i < textures.Length; i++)
            {
                var at = raw.Length + tail.Count;
                BitConverter.GetBytes(at - (textureIndex + 64 * i)).CopyTo(raw, textureIndex + 64 * i);
                tail.AddRange(Encoding.ASCII.GetBytes(textures[i]));
                tail.Add(0);
            }
            for (var i = 0; i < cdDirs.Length; i++)
            {
                BitConverter.GetBytes(raw.Length + tail.Count).CopyTo(raw, cdIndex + 4 * i);
                tail.AddRange(Encoding.ASCII.GetBytes(cdDirs[i]));
                tail.Add(0);
            }

            Encoding.ASCII.GetBytes("IDST").CopyTo(raw, 0);
            BitConverter.GetBytes(48).CopyTo(raw, 4);
            BitConverter.GetBytes(textures.Length).CopyTo(raw, 204);
            BitConverter.GetBytes(textureIndex).CopyTo(raw, 208);
            BitConverter.GetBytes(cdDirs.Length).CopyTo(raw, 212);
            BitConverter.GetBytes(cdIndex).CopyTo(raw, 216);
            return raw.Concat(tail).ToArray();
        }

        /// <summary>ZIP Stored armado a mano, con nombres y flags exactos.</summary>
        private static byte[] RawZip(params (byte[] Name, ushort Flags, string Content)[] files)
        {
            var body = new List<byte>();
            var central = new List<byte>();
            foreach (var (name, flags, content) in files)
            {
                var data = Encoding.ASCII.GetBytes(content);
                var crc = Crc32.Compute(data);
                var offset = body.Count;

                body.AddRange(BitConverter.GetBytes(0x04034b50u));
                body.AddRange(BitConverter.GetBytes((ushort)20));
                body.AddRange(BitConverter.GetBytes(flags));
                body.AddRange(new byte[6]);                               // metodo, hora, fecha
                body.AddRange(BitConverter.GetBytes(crc));
                body.AddRange(BitConverter.GetBytes(data.Length));
                body.AddRange(BitConverter.GetBytes(data.Length));
                body.AddRange(BitConverter.GetBytes((ushort)name.Length));
                body.AddRange(new byte[2]);
                body.AddRange(name);
                body.AddRange(data);

                central.AddRange(BitConverter.GetBytes(0x02014b50u));
                central.AddRange(BitConverter.GetBytes((ushort)20));
                central.AddRange(BitConverter.GetBytes((ushort)20));
                central.AddRange(BitConverter.GetBytes(flags));
                central.AddRange(new byte[6]);
                central.AddRange(BitConverter.GetBytes(crc));
                central.AddRange(BitConverter.GetBytes(data.Length));
                central.AddRange(BitConverter.GetBytes(data.Length));
                central.AddRange(BitConverter.GetBytes((ushort)name.Length));
                central.AddRange(new byte[12]);                           // extra, comentario, disco, atributos
                central.AddRange(BitConverter.GetBytes(offset));
                central.AddRange(name);
            }

            var eocd = new List<byte>();
            eocd.AddRange(BitConverter.GetBytes(0x06054b50u));
            eocd.AddRange(new byte[4]);
            eocd.AddRange(BitConverter.GetBytes((ushort)files.Length));
            eocd.AddRange(BitConverter.GetBytes((ushort)files.Length));
            eocd.AddRange(BitConverter.GetBytes(central.Count));
            eocd.AddRange(BitConverter.GetBytes(body.Count));
            eocd.AddRange(new byte[2]);

            return body.Concat(central).Concat(eocd).ToArray();
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length && match; j++) match = haystack[i + j] == needle[j];
                if (match) return i;
            }
            return -1;
        }

        private static void Write(string root, string relative, string content) =>
            Write(root, relative, Encoding.ASCII.GetBytes(content));

        private static void Write(string root, string relative, byte[] content)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, content);
        }

        private static string NewTemp(string prefix)
        {
            var temp = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            return temp;
        }

        private static void TryDelete(string dir)
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
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
