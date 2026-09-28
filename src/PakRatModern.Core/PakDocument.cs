using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PakRatModern.Core
{
    /// <summary>Tamano y fecha de modificacion de un archivo, para notar cambios hechos por fuera.</summary>
    public struct FileStamp : IEquatable<FileStamp>
    {
        public FileStamp(long length, DateTime lastWriteUtc)
        {
            Length = length;
            LastWriteUtc = lastWriteUtc;
        }

        public long Length { get; }
        public DateTime LastWriteUtc { get; }

        /// <summary>Sello actual del archivo, o null si no existe.</summary>
        public static FileStamp? Read(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc) : (FileStamp?)null;
        }

        public bool Equals(FileStamp other) => Length == other.Length && LastWriteUtc == other.LastWriteUtc;
        public override bool Equals(object obj) => obj is FileStamp other && Equals(other);
        public override int GetHashCode() => Length.GetHashCode() ^ LastWriteUtc.GetHashCode();
    }

    /// <summary>
    /// Se intento guardar sobre el BSP abierto despues de que otro programa lo
    /// modificara (tipicamente, una recompilacion desde Hammer).
    /// </summary>
    public sealed class FileChangedOnDiskException : IOException
    {
        public FileChangedOnDiskException(string path)
            : base($"{System.IO.Path.GetFileName(path)} changed on disk after it was opened here.")
        {
            FilePath = path;
        }

        public string FilePath { get; }
    }

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

        /// <summary>
        /// El archivo tal como se abrio o se guardo por ultima vez. El documento
        /// guarda el BSP entero en memoria; si otro programa lo reemplaza,
        /// guardar escribiria la version vieja encima de la nueva.
        /// </summary>
        private FileStamp? _stamp;

        public static PakDocument Open(string path)
        {
            var full = System.IO.Path.GetFullPath(path);

            // El sello se toma antes de leer: si el archivo cambia durante la
            // lectura, el documento queda marcado como desactualizado, no al reves.
            var stamp = FileStamp.Read(full);
            var bsp = BspFile.Load(full);
            var entries = PakArchive.Read(bsp.ReadPakLump(), out var skipped, out var duplicates);
            return new PakDocument(full, bsp, entries, skipped, duplicates) { _stamp = stamp };
        }

        /// <summary>
        /// Si el archivo en disco ya no es el que tiene este documento. Un archivo
        /// borrado no cuenta (guardar lo vuelve a crear sin pisar nada), ni uno
        /// tocado pero con el mismo contenido.
        /// </summary>
        public bool HasChangedOnDisk() => HasChangedOnDisk(out _);

        /// <param name="current">Sello actual, para no volver a preguntar por el mismo cambio.</param>
        public bool HasChangedOnDisk(out FileStamp? current)
        {
            current = FileStamp.Read(Path);
            if (current == null) return false;
            if (_stamp.HasValue && current.Value.Equals(_stamp.Value)) return false;

            if (!SameContent(Path, Bsp.Raw)) return true;

            _stamp = current;
            return false;
        }

        private static bool SameContent(string path, byte[] expected)
        {
            try
            {
                if (new FileInfo(path).Length != expected.LongLength) return false;

                var actual = File.ReadAllBytes(path);
                if (actual.Length != expected.Length) return false;
                for (var i = 0; i < actual.Length; i++)
                {
                    if (actual[i] != expected[i]) return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Si no se puede leer (p. ej. vbsp lo esta escribiendo), se trata como cambiado.
                return false;
            }
        }

        public IEnumerable<PakEntry> SortedEntries =>
            Entries.Values.OrderBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase);

        public long TotalSize => Entries.Values.Sum(e => (long)e.Size);

        /// <summary>Agrega o reemplaza una entrada. Devuelve true si reemplazo.</summary>
        public bool AddOrReplace(string archivePath, byte[] data)
        {
            var key = ArchivePath.Normalize(archivePath);
            var replaced = Entries.TryGetValue(key, out var existing);

            // Al reemplazar el contenido, el nombre conserva los bytes con que
            // venia: son los que busca el motor.
            Entries[key] = new PakEntry(key, data, replaced && existing.LegacyName && FitsLatin1(key));
            IsDirty = true;
            return replaced;
        }

        private static bool FitsLatin1(string name) => name.All(c => c <= 0xFF);

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
            Entries[key] = new PakEntry(key, entry.Data, entry.LegacyName && FitsLatin1(key));
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
        /// <param name="overwriteChangedFile">
        /// Sin esto, guardar sobre el BSP abierto despues de que otro programa lo
        /// cambiara lanza <see cref="FileChangedOnDiskException"/>: el documento
        /// tiene la version vieja y la escribiria encima de la nueva.
        /// </param>
        public void Save(string outputPath, bool createBackup, bool overwriteChangedFile = false)
        {
            var target = System.IO.Path.GetFullPath(outputPath);

            if (!overwriteChangedFile &&
                string.Equals(target, Path, StringComparison.OrdinalIgnoreCase) &&
                HasChangedOnDisk())
            {
                throw new FileChangedOnDiskException(target);
            }

            var pakBytes = PakArchive.Write(Entries);
            var updated = Bsp.ApplyPak(pakBytes);

            if (createBackup) AtomicFile.CreateBackup(target);
            AtomicFile.WriteAllBytes(target, updated);

            Path = target;
            _stamp = FileStamp.Read(target);
            IsDirty = false;
        }
    }
}
