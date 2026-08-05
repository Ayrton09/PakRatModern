using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PakRatModern.Core
{
    /// <summary>
    /// Conjunto de rutas que el juego ya provee en sus VPK base.
    ///
    /// Se usa para no empaquetar en el BSP archivos que el juego trae de fabrica:
    /// meterlos infla el mapa sin aportar nada y en algunos casos pisa assets
    /// oficiales con copias identicas.
    /// </summary>
    public sealed class BaseGameIndex
    {
        private readonly HashSet<string> _entries;

        private BaseGameIndex(HashSet<string> entries)
        {
            _entries = entries;
        }

        public int Count => _entries.Count;

        public static BaseGameIndex Empty => new BaseGameIndex(NewSet());

        public bool Contains(string archivePath) =>
            !string.IsNullOrWhiteSpace(archivePath) && _entries.Contains(archivePath);

        private static HashSet<string> NewSet() => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Construye el indice a partir de la carpeta del juego: los VPK que hay
        /// ahi mas los que aparezcan en los SearchPaths de gameinfo.txt.
        /// </summary>
        /// <param name="neededRefs">
        /// Si se pasa, solo se retienen las rutas presentes en ese conjunto y el
        /// escaneo corta apenas las encuentra a todas. Sin este filtro habria que
        /// indexar cientos de miles de entradas para responder unas pocas.
        /// </param>
        public static BaseGameIndex Build(string gameRoot, ISet<string> neededRefs = null)
        {
            var entries = NewSet();
            if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
                return new BaseGameIndex(entries);

            var root = Path.GetFullPath(gameRoot);
            var vpkFiles = NewSet();

            AddVpkFilesFromFolder(vpkFiles, root);

            foreach (var searchPath in GameInfo.ReadSearchPathValues(root))
            {
                var resolved = GameInfo.ResolveSearchPath(root, searchPath);
                if (resolved == null) continue;

                if (Directory.Exists(resolved)) AddVpkFilesFromFolder(vpkFiles, resolved);
                else AddVpkCandidateFile(vpkFiles, resolved);
            }

            foreach (var vpk in vpkFiles.OrderBy(p => p, StringComparer.Ordinal))
            {
                foreach (var entry in VpkIndex.EnumerateEntries(vpk))
                {
                    if (neededRefs != null && !neededRefs.Contains(entry)) continue;
                    entries.Add(entry);
                }

                if (neededRefs != null && entries.Count >= neededRefs.Count) break;
            }

            return new BaseGameIndex(entries);
        }

        private static void AddVpkFilesFromFolder(HashSet<string> files, string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

            try
            {
                foreach (var vpk in Directory.GetFiles(folder, "*_dir.vpk", SearchOption.TopDirectoryOnly))
                    files.Add(Path.GetFullPath(vpk));
            }
            catch (Exception)
            {
                // Carpeta inaccesible: no es motivo para abortar el escaneo entero.
            }
        }

        /// <summary>
        /// Un SearchPath puede apuntar a <c>pak01.vpk</c>; el arbol de directorios
        /// vive en el <c>_dir.vpk</c> correspondiente.
        /// </summary>
        private static void AddVpkCandidateFile(HashSet<string> files, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var candidate = path;
            if (!candidate.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase) &&
                candidate.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(candidate) ?? string.Empty;
                candidate = Path.Combine(dir, Path.GetFileNameWithoutExtension(candidate) + "_dir.vpk");
            }

            if (File.Exists(candidate)) files.Add(Path.GetFullPath(candidate));
        }
    }
}
