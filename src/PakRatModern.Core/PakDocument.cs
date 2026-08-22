using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PakRatModern.Core
{
    /// <summary>
    /// El BSP abierto y sus entradas embebidas, con el estado de edicion.
    ///
    /// Concentra las operaciones que la interfaz necesita para que la UI no
    /// manipule directamente ni el ZIP ni los lumps.
    /// </summary>
    public sealed class PakDocument
    {
        private PakDocument(
            string path,
            BspFile bsp,
            Dictionary<string, PakEntry> entries,
            IReadOnlyList<string> skipped,
            IReadOnlyList<string> duplicates)
        {
            Path = path;
            Bsp = bsp;
            Entries = entries;
            SkippedEntries = skipped;
            DuplicateEntries = duplicates;
        }

        public string Path { get; private set; }
        public BspFile Bsp { get; }
        public Dictionary<string, PakEntry> Entries { get; }
        public bool IsDirty { get; private set; }

        /// <summary>
        /// Entradas que el PAK traia con rutas inseguras y no se cargaron.
        /// Guardar el BSP las quita del mapa, asi que conviene avisarlo.
        /// </summary>
        public IReadOnlyList<string> SkippedEntries { get; }

        /// <summary>Entradas repetidas que se unificaron al abrir.</summary>
        public IReadOnlyList<string> DuplicateEntries { get; }

        public string MapName => string.IsNullOrEmpty(Path)
            ? string.Empty
            : System.IO.Path.GetFileNameWithoutExtension(Path);

        public static PakDocument Open(string path)
        {
            var full = System.IO.Path.GetFullPath(path);
            var bsp = BspFile.Load(full);
            var entries = PakArchive.Read(bsp.ReadPakLump(), out var skipped, out var duplicates);
            return new PakDocument(full, bsp, entries, skipped, duplicates);
        }

        public IEnumerable<PakEntry> SortedEntries =>
            Entries.Values.OrderBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase);

        public long TotalSize => Entries.Values.Sum(e => (long)e.Size);

        /// <summary>Agrega o reemplaza una entrada. Devuelve true si reemplazo.</summary>
        public bool AddOrReplace(string archivePath, byte[] data)
        {
            var key = ArchivePath.Normalize(archivePath);
            var replaced = Entries.ContainsKey(key);
            Entries[key] = new PakEntry(key, data);
            IsDirty = true;
            return replaced;
        }

        public bool Remove(string archivePath)
        {
            if (!Entries.Remove(archivePath)) return false;
            IsDirty = true;
            return true;
        }

        /// <summary>Cambia la ruta interna de una entrada conservando su contenido.</summary>
        public void Rename(string oldPath, string newPath)
        {
            if (!Entries.TryGetValue(oldPath, out var entry))
                throw new InvalidOperationException($"Entry not found: {oldPath}");

            var key = ArchivePath.Normalize(newPath);

            if (!string.Equals(key, oldPath, StringComparison.OrdinalIgnoreCase) &&
                Entries.ContainsKey(key))
            {
                throw new InvalidOperationException($"An entry with that path already exists: {key}");
            }

            Entries.Remove(oldPath);
            Entries[key] = new PakEntry(key, entry.Data);
            IsDirty = true;
        }

        /// <summary>
        /// Archivos de destino que ya existen en disco para estas entradas. La
        /// interfaz lo usa para preguntar antes de pisar algo.
        /// </summary>
        public IReadOnlyList<string> FindExistingExtractionTargets(string targetRoot, IEnumerable<string> archivePaths)
        {
            var existing = new List<string>();
            foreach (var archivePath in archivePaths)
            {
                if (!Entries.TryGetValue(archivePath, out var entry)) continue;
                var target = ArchivePath.ResolveExtractionTarget(targetRoot, entry.FullPath);
                if (File.Exists(target)) existing.Add(target);
            }
            return existing;
        }

        /// <summary>
        /// Extrae entradas. Devuelve cuantas se escribieron; con
        /// <paramref name="overwrite"/> en false, las que ya existian en disco
        /// se saltan en lugar de pisarse.
        /// </summary>
        public int ExtractTo(string targetRoot, IEnumerable<string> archivePaths, bool overwrite = true)
        {
            var written = 0;
            foreach (var archivePath in archivePaths)
            {
                if (!Entries.TryGetValue(archivePath, out var entry)) continue;

                var target = ArchivePath.ResolveExtractionTarget(targetRoot, entry.FullPath);
                if (!overwrite && File.Exists(target)) continue;

                var dir = System.IO.Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                File.WriteAllBytes(target, entry.Data);
                written++;
            }
            return written;
        }

        public (bool Ok, string Message) Verify() => PakArchive.Verify(Entries);

        /// <summary>
        /// Escribe el BSP. El respaldo se hace siempre que se sobreescriba un
        /// archivo existente, no solo al guardar sobre el original.
        /// </summary>
        public void Save(string outputPath, bool createBackup)
        {
            var target = System.IO.Path.GetFullPath(outputPath);

            var pakBytes = PakArchive.Write(Entries);
            var updated = Bsp.ApplyPak(pakBytes);

            if (createBackup) AtomicFile.CreateBackup(target);
            AtomicFile.WriteAllBytes(target, updated);

            Path = target;
            IsDirty = false;
        }
    }
}
