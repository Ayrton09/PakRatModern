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
        /// Hay mapas publicados con el PAK entero en LZMA, que ZipArchive no
        /// descomprime. Abrirlos a medias haria que guardar borrara esas entradas,
        /// asi que debe rechazarse con un mensaje que diga que pasa.
        /// </summary>
        private static void TestUnsupportedCompressionIsDetected()
        {
            var pak = PakArchive.Write(SampleEntries());

            var methods = ZipInspector.GetCompressionMethods(pak);
            Check(methods.Count == 1 && methods.ContainsKey(0),
                "inspector: un PAK propio deberia ser todo Stored");
            Check(ZipInspector.DescribeUnsupported(pak) == null,
                "inspector: marco como no soportado un PAK valido");

            // Se simula LZMA cambiando el metodo en el directorio central
            var eocd = pak.Length - 22;
            var centralOffset = (int)BitConverter.ToUInt32(pak, eocd + 16);
            var patched = (byte[])pak.Clone();
            patched[centralOffset + 10] = 14;   // metodo LZMA
            patched[centralOffset + 11] = 0;

            var described = ZipInspector.DescribeUnsupported(patched);
            Check(described != null, "inspector: no detecto el metodo no soportado");
            Check(described != null && described.Contains("LZMA"),
                $"inspector: el mensaje no nombra el metodo -> {described}");

            var refused = false;
            try { PakArchive.Read(patched); }
            catch (NotSupportedException) { refused = true; }
            Check(refused, "inspector: Read deberia rechazar el PAK, no abrirlo a medias");
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

            // El Game Path gana sobre la carpeta raiz conocida
            Check(ArchivePath.FromDiskPath(P(gameRoot, "custom", "mimod", "materials", "z.vmt"), gameRoot)
                    == "custom/mimod/materials/z.vmt",
                "ruta desde disco: deberia preferir la ruta relativa al Game Path");

            // Windows no distingue mayusculas en rutas; Linux si.
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                Check(ArchivePath.FromDiskPath(P(gameRoot.ToUpperInvariant(), "sound", "x.wav"), gameRoot)
                        == "sound/x.wav",
                    "ruta desde disco: fallo con el Game Path en otra capitalizacion");
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
            // (tools/Make-PakReference.ps1). Si deja de coincidir, las dos
            // herramientas divergieron y los mapas dejan de ser reproducibles.
            var referencePath = FindReferenceFile();
            if (referencePath == null)
            {
                Failures.Add("no se encontro reference-pak.sha256; la paridad con la CLI no se verifico");
                return;
            }

            var expected = File.ReadAllText(referencePath).Trim().ToLowerInvariant();
            Console.WriteLine($"CLI  sha256: {expected}  (referencia)");
            Check(hash == expected, $"paridad con la CLI rota: C#={hash} CLI={expected}");
        }

        /// <summary>Sube desde el directorio de salida hasta encontrar testdata/.</summary>
        private static string FindReferenceFile()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "testdata", "reference-pak.sha256");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        private static Dictionary<string, PakEntry> SampleEntries()
        {
            var entries = PakArchive.NewEntryMap();
            void Add(string name, string content) =>
                entries[name] = new PakEntry(name, Encoding.ASCII.GetBytes(content));

            Add("materials/_pre.vmt", "pre");
            Add("materials/A.vmt", "AAA");
            Add("materials/a_b.vmt", "ab-");
            Add("materials/ab.vmt", "ab");
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
