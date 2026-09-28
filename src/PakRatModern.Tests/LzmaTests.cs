using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using PakRatModern.Core;

namespace PakRatModern.Tests
{
    /// <summary>
    /// Decodificador LZMA contra datos comprimidos por liblzma
    /// (tools/make_lzma_fixtures.py): lumps con el formato de Source, un PAK
    /// con entradas LZMA como el de bspzip -repack -compress, y BSPs con
    /// entidades y static props comprimidos.
    /// </summary>
    internal static class LzmaTests
    {
        public static void Run(Action<bool, string> check)
        {
            var manifestPath = Program.FindTestData(Path.Combine("lzma", "manifest.txt"));
            if (manifestPath == null)
            {
                check(false, "lzma: no se encontraron los fixtures de testdata/lzma");
                return;
            }

            var dir = Path.GetDirectoryName(manifestPath);
            var manifest = File.ReadAllLines(manifestPath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(' '))
                .ToDictionary(p => p[0], p => (Size: long.Parse(p[1]), Sha: p[2]));

            TestSourceBlocks(check, dir, manifest);
            TestTruncatedStreamIsRejected(check, dir);
            TestLzmaPak(check, dir, manifest);
            TestCompressedBsp(check, dir, manifest);
        }

        private static void TestSourceBlocks(Action<bool, string> check, string dir,
            IDictionary<string, (long Size, string Sha)> manifest)
        {
            foreach (var name in new[] { "mixed.lzma", "literals.lzma", "entities.lzma", "sprp.lzma" })
            {
                var data = File.ReadAllBytes(Path.Combine(dir, name));
                check(Lzma.IsSourceCompressed(data, 0, data.Length), $"lzma {name}: no reconocio la cabecera de Source");

                byte[] plain = null;
                try { plain = Lzma.DecodeSourceBlock(data, 0, data.Length, PakLimits.MaxPakEntryBytes); }
                catch (InvalidDataException ex) { check(false, $"lzma {name}: fallo al descomprimir -> {ex.Message}"); continue; }

                var expected = manifest[name];
                check(plain.Length == expected.Size, $"lzma {name}: {plain.Length} bytes, se esperaban {expected.Size}");
                check(Sha256(plain) == expected.Sha, $"lzma {name}: el contenido descomprimido no coincide");
            }
        }

        private static void TestTruncatedStreamIsRejected(Action<bool, string> check, string dir)
        {
            var data = File.ReadAllBytes(Path.Combine(dir, "mixed.lzma"));

            // Cabecera que promete mas datos de los que hay
            var cut = data.Take(data.Length / 2).ToArray();
            check(Throws(() => Lzma.DecodeSourceBlock(cut, 0, cut.Length, PakLimits.MaxPakEntryBytes)),
                "lzma: acepto un bloque mas corto que su cabecera");

            // Cabecera coherente pero stream cortado: se queda sin datos a mitad
            BitConverter.GetBytes((uint)(cut.Length - Lzma.SourceHeaderSize)).CopyTo(cut, 8);
            check(Throws(() => Lzma.DecodeSourceBlock(cut, 0, cut.Length, PakLimits.MaxPakEntryBytes)),
                "lzma: no detecto un stream truncado");

            // Tamano declarado por encima del limite: se rechaza antes de reservar memoria
            check(Throws(() => Lzma.DecodeSourceBlock(data, 0, data.Length, 1000)),
                "lzma: no aplico el limite de tamano descomprimido");
        }

        private static void TestLzmaPak(Action<bool, string> check, string dir,
            IDictionary<string, (long Size, string Sha)> manifest)
        {
            var pak = File.ReadAllBytes(Path.Combine(dir, "pak-lzma.zip"));

            var methods = ZipInspector.GetCompressionMethods(pak);
            check(methods.Count == 1 && methods.ContainsKey(ZipInspector.MethodLzma),
                "pak lzma: el fixture deberia estar todo en LZMA");
            check(ZipInspector.DescribeUnsupported(pak) == null, "pak lzma: marco LZMA como no soportado");

            Dictionary<string, PakEntry> entries;
            try { entries = PakArchive.Read(pak); }
            catch (Exception ex) { check(false, $"pak lzma: no se pudo leer -> {ex.Message}"); return; }

            foreach (var kv in manifest.Where(m => m.Key.StartsWith("pak-lzma.zip:", StringComparison.Ordinal)))
            {
                var name = kv.Key.Substring("pak-lzma.zip:".Length);
                if (!entries.TryGetValue(name, out var entry))
                {
                    check(false, $"pak lzma: falta {name}");
                    continue;
                }
                check(entry.Size == kv.Value.Size && Sha256(entry.Data) == kv.Value.Sha,
                    $"pak lzma: contenido distinto en {name}");
            }

            // Al guardar sale todo Stored, que es lo unico que lee el motor en el PAK
            var rewritten = PakArchive.Write(entries);
            var after = ZipInspector.GetCompressionMethods(rewritten);
            check(after.Count == 1 && after.ContainsKey(ZipInspector.MethodStored),
                "pak lzma: al reescribir no quedo todo Stored");
            check(PakArchive.Read(rewritten).Count == entries.Count, "pak lzma: el PAK reescrito perdio entradas");
        }

        private static void TestCompressedBsp(Action<bool, string> check, string dir,
            IDictionary<string, (long Size, string Sha)> manifest)
        {
            var entities = File.ReadAllBytes(Path.Combine(dir, "entities.lzma"));
            var bsp = SyntheticBsp.Build(entities: entities, entitiesUncompressedSize: (int)manifest["entities.lzma"].Size);

            check(Sha256(bsp.GetLumpData(PakLimits.EntitiesLumpIndex)) == manifest["entities.lzma"].Sha,
                "bsp lzma: GetLumpData no descomprimio el lump de entidades");

            var refs = BspReferenceScanner.Collect(bsp, "de_lz", includeExtras: false);
            check(refs.Contains("models/props/lz/crate.mdl"), "bsp lzma: el scan no vio el prop del lump comprimido");
            check(refs.Contains("materials/sprites/lz/glow.vmt"), "bsp lzma: el scan no vio el sprite del lump comprimido");

            // Guardar no toca los lumps comprimidos: siguen leyendose igual
            var entries = PakArchive.NewEntryMap();
            entries["materials/x.vmt"] = new PakEntry("materials/x.vmt", new byte[] { 1, 2, 3 });
            var saved = BspFile.Parse(bsp.ApplyPak(PakArchive.Write(entries)));
            check(Sha256(saved.GetLumpData(PakLimits.EntitiesLumpIndex)) == manifest["entities.lzma"].Sha,
                "bsp lzma: guardar rompio el lump de entidades comprimido");

            // Game lump con el sub-lump de static props en LZMA (flag 1)
            var sprp = File.ReadAllBytes(Path.Combine(dir, "sprp.lzma"));
            var props = SyntheticBsp.Build(staticPropData: sprp, staticPropsCompressed: true);
            var propRefs = BspReferenceScanner.Collect(props, "de_lz", includeExtras: false);
            check(propRefs.Contains("models/props/lz/tree.mdl") && propRefs.Contains("models/props/lz/rock.mdl"),
                "bsp lzma: el scan no leyo los static props comprimidos");
        }

        private static bool Throws(Action action)
        {
            try { action(); return false; }
            catch (InvalidDataException) { return true; }
        }

        private static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
