using System;
using System.IO;
using System.Linq;
using System.Text;
using PakRatModern.Core;

namespace PakRatModern.Tests
{
    /// <summary>
    /// Ciclo completo de edicion sobre disco: abrir, modificar, guardar y releer.
    /// Es el camino donde un error se lleva puesto el mapa del usuario.
    /// </summary>
    internal static class DocumentTests
    {
        public static void Run(Action<bool, string> check)
        {
            var temp = Path.Combine(Path.GetTempPath(), "pakrat_doc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);

            try
            {
                var bspPath = Path.Combine(temp, "de_test.bsp");

                // BSP con una entrada y un lump DESPUES del PAK, que es el caso
                // donde la alineacion importa.
                var entries = PakArchive.NewEntryMap();
                entries["materials/custom/a.vmt"] = new PakEntry("materials/custom/a.vmt", Encoding.ASCII.GetBytes("original"));

                var seed = SyntheticBsp.Build(
                    entities: Encoding.ASCII.GetBytes("{\n\"classname\" \"worldspawn\"\n}\n"),
                    pak: PakArchive.Write(entries));

                File.WriteAllBytes(bspPath, seed.Raw);

                // --- Abrir, agregar, renombrar, borrar, guardar ---
                var doc = PakDocument.Open(bspPath);
                check(doc.Entries.Count == 1, $"open: se esperaba 1 entrada, hay {doc.Entries.Count}");
                check(!doc.IsDirty, "open: un documento recien abierto no deberia estar modificado");

                doc.AddOrReplace("materials/custom/b.vmt", Encoding.ASCII.GetBytes("nuevo"));
                check(doc.IsDirty, "add: el documento deberia quedar marcado como modificado");

                doc.Rename("materials/custom/a.vmt", "materials/renombrado/a.vmt");
                check(doc.Entries.ContainsKey("materials/renombrado/a.vmt"), "rename: no aparecio la ruta nueva");
                check(!doc.Entries.ContainsKey("materials/custom/a.vmt"), "rename: quedo la ruta vieja");

                doc.Save(bspPath, createBackup: true);
                check(!doc.IsDirty, "save: deberia limpiar la marca de modificado");
                check(File.Exists(bspPath + ".bak"), "save: no se creo el respaldo .bak");

                // --- Releer desde disco ---
                var reopened = PakDocument.Open(bspPath);
                check(reopened.Entries.Count == 2, $"reload: se esperaban 2 entradas, hay {reopened.Entries.Count}");
                check(Encoding.ASCII.GetString(reopened.Entries["materials/renombrado/a.vmt"].Data) == "original",
                    "reload: el contenido de la entrada renombrada cambio");
                check(Encoding.ASCII.GetString(reopened.Entries["materials/custom/b.vmt"].Data) == "nuevo",
                    "reload: el contenido de la entrada nueva cambio");

                var (ok, message) = reopened.Verify();
                check(ok, $"reload: el PAK guardado no valida -> {message}");

                // El respaldo debe conservar el estado anterior
                var backup = PakDocument.Open(bspPath + ".bak");
                check(backup.Entries.Count == 1, $"backup: deberia tener el contenido previo (1), tiene {backup.Entries.Count}");

                // --- Borrar y guardar de nuevo ---
                reopened.Remove("materials/custom/b.vmt");
                reopened.Save(bspPath, createBackup: false);

                var final = PakDocument.Open(bspPath);
                check(final.Entries.Count == 1, $"delete: se esperaba 1 entrada tras borrar, hay {final.Entries.Count}");

                // --- Extraccion a disco ---
                var outDir = Path.Combine(temp, "salida");
                final.ExtractTo(outDir, final.Entries.Keys.ToList());
                var extracted = Path.Combine(outDir, "materials", "renombrado", "a.vmt");
                check(File.Exists(extracted), "extract: no se escribio el archivo esperado");
                check(File.ReadAllText(extracted) == "original", "extract: el contenido extraido no coincide");

                // --- Rechazo de rutas duplicadas al renombrar ---
                final.AddOrReplace("materials/otro/c.vmt", new byte[] { 1 });
                var rejected = false;
                try { final.Rename("materials/otro/c.vmt", "materials/renombrado/a.vmt"); }
                catch (InvalidOperationException) { rejected = true; }
                check(rejected, "rename: deberia rechazar una ruta que ya existe");
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch (IOException) { }
            }
        }
    }
}
