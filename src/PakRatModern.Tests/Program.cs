using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PakRatModern.Core;

namespace PakRatModern.Tests
{
    /// <summary>
    /// Runner sin dependencias externas. Verifica el core y, sobre todo, que su
    /// salida coincida byte por byte con la de la CLI de Python.
    /// </summary>
    internal static class Program
    {
        private static readonly List<string> Failures = new List<string>();
        private static int _checks;

        private static void Check(bool condition, string message)
        {
            _checks++;
            if (!condition) Failures.Add(message);
        }

        private static int Main()
        {
            TestArchivePathRejectsUnsafeInput();
            TestCaseInsensitiveEntries();
            TestRealWorldMalformedPak();
            TestUnsupportedCompressionIsDetected();
            TestArchivePathFromDisk();
            TestPakRoundTrip();
            TestAlignmentPreserved();
            TestDeterminism();
            TestByteParityWithPythonCli();
            VpkTests.Run(Check);
            ScannerTests.Run(Check);
            VmtTests.Run(Check);
            ScanServiceTests.Run(Check);
            DocumentTests.Run(Check);
            RegressionTests.Run(Check);
            LzmaTests.Run(Check);

            if (Failures.Count == 0)
            {
                Console.WriteLine($"TODAS LAS PRUEBAS DEL CORE PASARON ({_checks} comprobaciones)");
                return 0;
            }

            foreach (var f in Failures) Console.WriteLine("FALLO: " + f);
            Console.WriteLine($"{Failures.Count} fallo(s) de {_checks} comprobaciones");
            return 1;
        }

        private static void TestArchivePathRejectsUnsafeInput()
        {
            var unsafeInputs = new[]
            {
                "../escape.vmt",
                "materials/../../etc/passwd",
                "/absolute/path.vmt",
                "materials/CON.vmt",
                "materials/nul",
                "materials/trailing./x.vmt",
                "materials/bad<name>.vmt",
                "materials/pipe|name.vmt",
            };

            foreach (var input in unsafeInputs)
            {
                var rejected = false;
                try { ArchivePath.Normalize(input); }
                catch (ArgumentException) { rejected = true; }
                Check(rejected, $"ArchivePath acepto una ruta insegura: {input}");
            }

            Check(ArchivePath.Normalize(@"materials\custom\a.vmt") == "materials/custom/a.vmt",
                "ArchivePath no normalizo las barras invertidas");

            // Zip-slip a traves de la resolucion de destino
            var blocked = false;
            try { ArchivePath.ResolveExtractionTarget(@"C:\out", "../../evil.txt"); }
            catch (Exception) { blocked = true; }
            Check(blocked, "ResolveExtractionTarget no bloqueo un escape de la raiz");
        }

        /// <summary>
        /// Casos vistos en mapas publicados: rutas absolutas dentro del PAK y
        /// entradas repetidas. Ninguno debe impedir abrir el mapa.
        /// </summary>
        private static void TestRealWorldMalformedPak()
        {
            // ZIP con dos entradas inseguras y una repetida entre 3 validas
            var raw = BuildRawZip(new[]
            {
                ("materials/ok/a.vmt", "aaa"),
                ("C:/Program Files/Steam/materials/bad.vmt", "bad1"),
                ("materials/ok/b.vmt", "bbb"),
                ("/sound/bad.mp3", "bad2"),
                ("materials/OK/A.vmt", "aaa"),          // duplicado por mayusculas
                ("materials/ok/c.vmt", "ccc"),
            });

            var entries = PakArchive.Read(raw, out var skipped, out var duplicates);

            Check(entries.Count == 3, $"pak malformado: se esperaban 3 entradas validas, hay {entries.Count}");
            Check(skipped.Count == 2, $"pak malformado: se esperaban 2 entradas inseguras, hay {skipped.Count}");
            Check(duplicates.Count == 1, $"pak malformado: se esperaba 1 duplicado, hay {duplicates.Count}");

            Check(entries.ContainsKey("materials/ok/a.vmt"), "pak malformado: se perdio una entrada valida");
            Check(entries.ContainsKey("materials/ok/c.vmt"), "pak malformado: se perdio la entrada posterior a una insegura");
            Check(!entries.Keys.Any(k => k.Contains(":")), "pak malformado: se colo una ruta con unidad de disco");
            Check(!entries.Keys.Any(k => k.StartsWith("/")), "pak malformado: se colo una ruta absoluta");

            // El PAK reescrito ya no contiene las entradas inseguras
            var back = PakArchive.Read(PakArchive.Write(entries));
            Check(back.Count == 3, $"pak malformado: tras reescribir hay {back.Count} entradas");
        }

        /// <summary>
        /// Un PAK con metodos que no se pueden descomprimir se rechaza entero:
        /// abrirlo a medias haria que guardar borrara esas entradas. El mensaje
        /// tiene que nombrar el metodo y decir que hacer. LZMA ya no esta en
        /// esta lista: lo lee el core (ver LzmaTests).
        /// </summary>
        private static void TestUnsupportedCompressionIsDetected()
        {
            var pak = PakArchive.Write(SampleEntries());

            var methods = ZipInspector.GetCompressionMethods(pak);
            Check(methods.Count == 1 && methods.ContainsKey(0),
                "inspector: un PAK propio deberia ser todo Stored");
            Check(ZipInspector.DescribeUnsupported(pak) == null,
                "inspector: marco como no soportado un PAK valido");

            // BZip2 lo lee la CLI: el mensaje propone repack.
            var bzip2 = PatchFirstMethod(pak, 12);
            var described = ZipInspector.DescribeUnsupported(bzip2);
            Check(described != null && described.Contains("BZip2"),
                $"inspector: el mensaje no nombra BZip2 -> {described}");
            Check(described != null && described.Contains("repack") && !described.Contains("any file"),
                $"inspector: el mensaje deberia proponer repack, no agregar un archivo -> {described}");

            var refused = false;
            try { PakArchive.Read(bzip2); }
            catch (NotSupportedException) { refused = true; }
            Check(refused, "inspector: Read deberia rechazar el PAK, no abrirlo a medias");

            // Deflate64 no lo lee ninguna de las dos herramientas: no se promete la CLI.
            var deflate64 = ZipInspector.DescribeUnsupported(PatchFirstMethod(pak, 9));
            Check(deflate64 != null && deflate64.Contains("Deflate64") && !deflate64.Contains("pakrat_modern.ps1"),
                $"inspector: para Deflate64 no deberia sugerir la CLI -> {deflate64}");
        }

        /// <summary>Cambia el metodo de la primera entrada en el directorio central.</summary>
        private static byte[] PatchFirstMethod(byte[] zip, ushort method)
        {
            var eocd = zip.Length - 22;
            var centralOffset = (int)BitConverter.ToUInt32(zip, eocd + 16);
            var patched = (byte[])zip.Clone();
            BitConverter.GetBytes(method).CopyTo(patched, centralOffset + 10);
            return patched;
        }

        /// <summary>ZIP crudo, sin pasar por la validacion de rutas.</summary>
        private static byte[] BuildRawZip((string Name, string Content)[] files)
        {
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (var (name, content) in files)
                    {
                        var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                        using (var writer = new StreamWriter(entry.Open()))
                            writer.Write(content);
                    }
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Deduccion de la ruta interna al agregar archivos del disco: es lo que
        /// decide donde termina cada archivo dentro del mapa.
        /// </summary>
        private static void TestArchivePathFromDisk()
        {
            // Las rutas se arman con el separador de la plataforma: en Linux una
            // cadena con barras invertidas no es una ruta, es un nombre de archivo.
            var sep = Path.DirectorySeparatorChar;
            string P(params string[] parts) => string.Join(sep.ToString(), parts);

            var gameRoot = Path.GetFullPath(P("juegos", "cstrike"));
            var afuera = Path.GetFullPath(P("descargas", "pack"));

            Check(ArchivePath.FromDiskPath(P(gameRoot, "materials", "custom", "wall.vmt"), gameRoot)
                    == "materials/custom/wall.vmt",
                "ruta desde disco: no resolvio relativo al Game Path");

            // El Game Path con separador final debe dar lo mismo
            Check(ArchivePath.FromDiskPath(P(gameRoot, "models", "props", "a.mdl"), gameRoot + sep)
                    == "models/props/a.mdl",
                "ruta desde disco: el separador final del Game Path rompio la resolucion");

            // Fuera del Game Path se busca una carpeta raiz conocida
            Check(ArchivePath.FromDiskPath(P(afuera, "materials", "custom", "b.vtf"), gameRoot)
                    == "materials/custom/b.vtf",
                "ruta desde disco: no encontro la carpeta raiz conocida");

            Check(ArchivePath.FromDiskPath(P(afuera, "materials", "custom", "b.vtf"), null)
                    == "materials/custom/b.vtf",
                "ruta desde disco: fallo sin Game Path configurado");

            // Sin carpeta raiz reconocible no se inventa una ruta
            Check(ArchivePath.FromDiskPath(P(afuera, "suelto.vmt"), gameRoot) == null,
                "ruta desde disco: invento una ruta para un archivo sin carpeta reconocible");

            // Una carpeta que solo contiene la palabra no cuenta como raiz
            Check(ArchivePath.FromDiskPath(P(afuera, "mis materials viejos", "x.vmt"), gameRoot) == null,
                "ruta desde disco: confundio 'mis materials viejos' con la raiz materials");

            // custom/<mod>/ y download/ son puntos de montaje del motor: lo que
            // hay dentro se carga como si estuviera en la raiz del juego.
            Check(ArchivePath.FromDiskPath(P(gameRoot, "custom", "mimod", "materials", "z.vmt"), gameRoot)
                    == "materials/z.vmt",
                "ruta desde disco: no quito el punto de montaje custom/<mod>/");

            Check(ArchivePath.FromDiskPath(P(gameRoot, "download", "models", "props", "q.mdl"), gameRoot)
                    == "models/props/q.mdl",
                "ruta desde disco: no quito el punto de montaje download/");

            // Un archivo directamente en custom/ (sin subcarpeta de mod) no es
            // contenido montado; se deja como esta.
            Check(ArchivePath.FromDiskPath(P(gameRoot, "custom", "readme.txt"), gameRoot)
                    == "custom/readme.txt",
                "ruta desde disco: trato custom/<archivo> como punto de montaje");

            // Dentro del Game Path, una raiz conocida mas adentro tambien se
            // reconoce (p. ej. una carpeta de trabajo del mapper).
            Check(ArchivePath.FromDiskPath(P(gameRoot, "work", "v2", "materials", "w.vmt"), gameRoot)
                    == "materials/w.vmt",
                "ruta desde disco: no encontro la raiz conocida dentro del Game Path");

            // Sin raiz conocida, la ruta relativa al Game Path se conserva
            Check(ArchivePath.FromDiskPath(P(gameRoot, "gameinfo.txt"), gameRoot) == "gameinfo.txt",
                "ruta desde disco: perdio una ruta relativa sin raiz conocida");

            // Un Game Path que contiene una raiz conocida en su propia ruta no
            // confunde la deduccion: se resuelve relativo al Game Path primero.
            var rootConMaterials = Path.GetFullPath(P("juegos", "materials", "cstrike"));
            Check(ArchivePath.FromDiskPath(P(rootConMaterials, "models", "a.mdl"), rootConMaterials)
                    == "models/a.mdl",
                "ruta desde disco: la palabra materials en el Game Path contamino la ruta");

            // Windows no distingue mayusculas en rutas; Linux si.
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                Check(ArchivePath.FromDiskPath(P(gameRoot.ToUpperInvariant(), "sound", "x.wav"), gameRoot)
                        == "sound/x.wav",
                    "ruta desde disco: fallo con el Game Path en otra capitalizacion");
            }

            // Fuera del Game Path, carpetas del usuario que se llaman como una
            // raiz ("maps", "Media", "Scripts") no pueden ganarle al contenido.
            var docs = Path.GetFullPath(P("Users", "me", "Documents"));
            Check(ArchivePath.FromDiskPath(P(docs, "maps", "mymap", "materials", "custom", "wall.vmt"), gameRoot)
                    == "materials/custom/wall.vmt",
                "ruta desde disco: una carpeta 'maps' del usuario decidio la ruta interna");
            Check(ArchivePath.FromDiskPath(P(Path.GetFullPath("Media"), "Mapping", "materials", "x.vmt"), null)
                    == "materials/x.vmt",
                "ruta desde disco: una carpeta 'Media' del usuario decidio la ruta interna");
            Check(ArchivePath.FromDiskPath(P(docs, "Scripts", "pack", "sound", "mymap", "alarm.wav"), gameRoot)
                    == "sound/mymap/alarm.wav",
                "ruta desde disco: una carpeta 'Scripts' del usuario decidio la ruta interna");

            // Las raices que si se anidan de verdad se respetan
            Check(ArchivePath.FromDiskPath(P(afuera, "materials", "models", "props", "x.vmt"), null)
                    == "materials/models/props/x.vmt",
                "ruta desde disco: materials/models se partio en models/");
            Check(ArchivePath.FromDiskPath(P(afuera, "materials", "maps", "de_x", "c0_0_0.vtf"), null)
                    == "materials/maps/de_x/c0_0_0.vtf",
                "ruta desde disco: materials/maps se partio en maps/");

            // Carpeta agregada entera: la ruta se ancla en ella
            var mapFolder = P(docs, "maps", "mymap");
            Check(ArchivePath.FromDiskPath(P(mapFolder, "materials", "a.vmt"), gameRoot, mapFolder) == "materials/a.vmt",
                "ruta desde disco: no anclo en la carpeta de contenido agregada");
            Check(ArchivePath.FromDiskPath(P(mapFolder, "notas.txt"), gameRoot, mapFolder) == null,
                "ruta desde disco: un archivo suelto de la carpeta de contenido no deberia empaquetarse");
            Check(ArchivePath.IsInside(P(gameRoot, "materials", "a.vmt"), gameRoot) &&
                  !ArchivePath.IsInside(P(afuera, "materials", "a.vmt"), gameRoot),
                "ruta desde disco: IsInside no distingue dentro y fuera del Game Path");

            TestFindContentRoot();
        }

        private static void TestFindContentRoot()
        {
            var temp = Path.Combine(Path.GetTempPath(), "pakrat_content_" + Guid.NewGuid().ToString("N"));
            try
            {
                var mapFolder = Path.Combine(temp, "maps", "mymap");
                Directory.CreateDirectory(Path.Combine(mapFolder, "materials", "custom"));
                Directory.CreateDirectory(Path.Combine(mapFolder, "models"));

                Check(string.Equals(ArchivePath.FindContentRoot(mapFolder), mapFolder, StringComparison.OrdinalIgnoreCase),
                    "raiz de contenido: una carpeta con materials/ y models/ deberia ser la raiz");
                Check(string.Equals(ArchivePath.FindContentRoot(Path.Combine(mapFolder, "materials")), mapFolder, StringComparison.OrdinalIgnoreCase),
                    "raiz de contenido: agregar materials/ deberia anclar en su carpeta padre");
                Check(ArchivePath.FindContentRoot(Path.Combine(mapFolder, "materials", "custom")) == null,
                    "raiz de contenido: una subcarpeta de materials/ no deberia ser raiz");
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch (IOException) { }
            }
        }

        private static void TestCaseInsensitiveEntries()
        {
            var entries = PakArchive.NewEntryMap();
            entries["materials/Custom/A.vmt"] = new PakEntry("materials/Custom/A.vmt", new byte[] { 1 });
            entries["materials/custom/a.vmt"] = new PakEntry("materials/custom/a.vmt", new byte[] { 2 });

            Check(entries.Count == 1, $"claves case-insensitive: quedaron {entries.Count} entradas, se esperaba 1");
            Check(entries.ContainsKey("MATERIALS/CUSTOM/A.VMT"),
                "claves case-insensitive: la busqueda con otra capitalizacion fallo");
        }

        private static void TestPakRoundTrip()
        {
            var entries = SampleEntries();
            var pak = PakArchive.Write(entries);
            var back = PakArchive.Read(pak);

            Check(back.Count == entries.Count, $"round-trip: {back.Count} entradas de {entries.Count}");

            foreach (var kv in entries)
            {
                if (!back.TryGetValue(kv.Key, out var got))
                {
                    Failures.Add($"round-trip: falta la entrada {kv.Key}");
                    continue;
                }
                Check(got.Data.SequenceEqual(kv.Value.Data), $"round-trip: contenido distinto en {kv.Key}");
            }

            // El resultado debe ser legible por el ZipArchive del framework
            using (var ms = new MemoryStream(pak))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                Check(zip.Entries.Count == entries.Count, "el ZIP producido no es legible por ZipArchive");
            }
        }

        private static void TestAlignmentPreserved()
        {
            foreach (var extra in new[] { 1, 2, 3, 5, 17, 64 })
            {
                var entries = PakArchive.NewEntryMap();
                entries["materials/a.vmt"] = new PakEntry("materials/a.vmt", Fill(0x41, 40));
                var originalPak = PakArchive.Write(entries);

                var (raw, lumps) = BuildSyntheticBsp(originalPak);
                var oldOfs = new[] { 1, 2, 3 }.ToDictionary(i => i, i => lumps[i].FileOfs);

                var bsp = BspFile.Parse(raw);
                var reread = PakArchive.Read(bsp.ReadPakLump());
                reread["materials/b.vmt"] = new PakEntry("materials/b.vmt", Fill(0x62, extra));

                var updated = bsp.ApplyPak(PakArchive.Write(reread));
                var after = BspFile.Parse(updated);

                foreach (var i in new[] { 1, 2, 3 })
                {
                    var shift = after.Lumps[i].FileOfs - oldOfs[i];
                    Check(shift % 4 == 0, $"extra={extra} lump {i}: desplazamiento {shift} no es multiplo de 4");
                    Check(after.Lumps[i].FileOfs % 4 == 0, $"extra={extra} lump {i}: offset desalineado");

                    var before = new byte[lumps[i].FileLen];
                    Array.Copy(raw, oldOfs[i], before, 0, lumps[i].FileLen);
                    Check(after.GetLumpBytes(i).SequenceEqual(before), $"extra={extra} lump {i}: datos corrompidos");
                }

                var finalEntries = PakArchive.Read(after.ReadPakLump());
                Check(finalEntries.Count == 2, $"extra={extra}: el PAK quedo con {finalEntries.Count} entradas");
            }
        }

        private static void TestDeterminism()
        {
            var a = PakArchive.Write(SampleEntries());
            var b = PakArchive.Write(SampleEntries());
            Check(a.SequenceEqual(b), "el escritor no es determinista: dos corridas dieron bytes distintos");
        }

        private static void TestByteParityWithPythonCli()
        {
            var pak = PakArchive.Write(SampleEntries());
            var hash = Sha256(pak);
            Console.WriteLine($"C#   sha256: {hash}  ({pak.Length} bytes)");

            // Referencia generada por la CLI de Python sobre el mismo contenido
            // (tools/make_pak_reference.py). Si deja de coincidir, las dos
            // herramientas divergieron y los mapas dejan de ser reproducibles.
            var referencePath = FindTestData("reference-pak.sha256");
            if (referencePath == null)
            {
                Failures.Add("no se encontro reference-pak.sha256; la paridad con la CLI no se verifico");
                return;
            }

            var expected = File.ReadAllText(referencePath).Trim().ToLowerInvariant();
            Console.WriteLine($"CLI  sha256: {expected}  (referencia)");
            Check(hash == expected, $"paridad con la CLI rota: C#={hash} CLI={expected}");
        }

        /// <summary>Sube desde el directorio de salida hasta encontrar testdata/&lt;ruta&gt;.</summary>
        internal static string FindTestData(string relativePath)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "testdata", relativePath);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        private static Dictionary<string, PakEntry> SampleEntries()
        {
            var entries = PakArchive.NewEntryMap();
            void Add(string name, string content, bool legacyName = false) =>
                entries[name] = new PakEntry(name, Encoding.ASCII.GetBytes(content), legacyName);

            Add("materials/_pre.vmt", "pre");
            Add("materials/A.vmt", "AAA");
            Add("materials/a_b.vmt", "ab-");
            Add("materials/ab.vmt", "ab");
            Add("materials/custom/señal.vmt", "utf8");            // cubre el flag UTF-8 del ZIP
            Add("materials/legacy/café.vmt", "latin1", true);     // nombre crudo, sin flag, como vbsp
            Add("models/de_dust2/x.mdl", "mdl-data");
            Add("maps/de_dust2.nav", "nav");
            return entries;
        }

        private static (byte[] Raw, Lump[] Lumps) BuildSyntheticBsp(byte[] pakBytes)
        {
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

            Place(0, Fill(0x45, 100));
            Place(PakLimits.GameLumpIndex, Fill(0x47, 64));   // antes del PAK: el guard no salta
            Place(PakLimits.PakLumpIndex, pakBytes);
            Place(1, Fill(0x50, 333));                        // lumps DESPUES del PAK
            Place(2, Fill(0x54, 71));
            Place(3, Fill(0x58, 12));

            var raw = new byte[PakLimits.HeaderSize + body.Count];
            var header = BspFile.SerializeHeader(21, 7, lumps);
            Array.Copy(header, 0, raw, 0, header.Length);
            body.CopyTo(0, raw, PakLimits.HeaderSize, body.Count);

            return (raw, lumps);
        }

        private static byte[] Fill(byte value, int count)
        {
            var b = new byte[count];
            for (var i = 0; i < count; i++) b[i] = value;
            return b;
        }

        private static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
