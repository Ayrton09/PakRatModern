using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PakRatModern.Core
{
    public enum ScanStatus
    {
        AlreadyInPak,
        BaseGameVpk,
        CanAdd,
        MissingOnDisk,
    }

    public sealed class ScanRow
    {
        public string Path { get; set; }
        public bool ExistsOnDisk { get; set; }
        public bool InPak { get; set; }
        public bool BaseGame { get; set; }
        public string FullDiskPath { get; set; }
        public ScanStatus Status { get; set; }

        /// <summary>Se puede empaquetar: falta en el PAK, no lo trae el juego y esta en disco.</summary>
        public bool Addable => !InPak && !BaseGame && ExistsOnDisk;

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case ScanStatus.AlreadyInPak: return "Already in PAK";
                    case ScanStatus.BaseGameVpk: return "Base game VPK";
                    case ScanStatus.CanAdd: return "Can add";
                    default: return "Missing on disk";
                }
            }
        }
    }

    public sealed class ScanSummary
    {
        public int MissingTotal { get; set; }
        public int CanAdd { get; set; }
        public int NotFound { get; set; }
        public int AlreadyInPak { get; set; }
    }

    public sealed class ScanResult
    {
        public IReadOnlyList<ScanRow> Rows { get; set; } = new List<ScanRow>();
        public ScanSummary Summary { get; set; } = new ScanSummary();
    }

    /// <summary>
    /// Determina que archivos le faltan a un mapa para funcionar en otra maquina.
    ///
    /// El proceso es: juntar lo que el BSP referencia, seguir las dependencias
    /// hasta agotarlas (un modelo pide materiales, un material pide texturas y
    /// puede incluir otros materiales), y recien ahi clasificar cada archivo.
    /// </summary>
    public sealed class ScanService
    {
        // Tope de expansion: los include de VMT pueden formar ciclos y una cadena
        // patologica no deberia colgar la interfaz.
        private const int MaxExpansionIterations = 20000;

        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

        private static readonly Regex ManifestFilePattern =
            new Regex(@"""?file""?\s+""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex WavePattern =
            new Regex(@"""?wave""?\s+""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SoundFilePattern =
            new Regex(@"\.(wav|mp3|ogg)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ParticleAssetPattern =
            new Regex(@"[A-Za-z0-9_\-/\\\.]+\.(vmt|mdl)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly char[] SoundPrefixChars = "*#@^)(<>!?$}&~`+%".ToCharArray();

        private readonly IReadOnlyDictionary<string, PakEntry> _pakEntries;
        private readonly string _gameRoot;

        // Donde el juego busca archivos sueltos: el Game Path y los SearchPaths
        // de gameinfo.txt (incluidas las subcarpetas de custom/). Mirar solo el
        // Game Path reportaba como faltante contenido que el juego si carga.
        private readonly IReadOnlyList<string> _searchDirs;

        public ScanService(IReadOnlyDictionary<string, PakEntry> pakEntries, string gameRoot)
        {
            _pakEntries = pakEntries;
            _gameRoot = gameRoot;
            _searchDirs = string.IsNullOrWhiteSpace(gameRoot)
                ? new List<string>()
                : GameInfo.ResolveSearchDirectories(gameRoot);
        }

        public ScanResult Scan(BspFile bsp, string mapName, bool includeExtras, Action<string> onProgress = null)
        {
            void Report(string message) => onProgress?.Invoke(message);

            Report("Scan: reading BSP references...");
            var refs = BspReferenceScanner.Collect(bsp, mapName, includeExtras);

            Report("Scan: following particle manifests and soundscapes...");
            ExpandScripts(refs);

            Report("Scan: expanding model materials...");
            var unresolvedMaterials = ExpandModelMaterials(refs);

            Report("Scan: expanding material dependencies...");
            ExpandVmtDependencies(refs);

            Report("Scan: checking base game VPKs...");
            var needed = new HashSet<string>(refs, StringComparer.OrdinalIgnoreCase);
            foreach (var candidates in unresolvedMaterials) needed.UnionWith(candidates);
            var baseIndex = BaseGameIndex.Build(_gameRoot, needed);

            // Texturas de modelo que no estan ni en el PAK ni en disco: si el juego
            // trae alguna de sus rutas, esa es la que carga el motor; si no, se
            // reporta solo la primera, que es la que el motor busca primero.
            foreach (var candidates in unresolvedMaterials)
                refs.Add(candidates.FirstOrDefault(baseIndex.Contains) ?? candidates[0]);

            Report("Scan: checking disk files...");
            var optionalExtras = includeExtras && !string.IsNullOrWhiteSpace(mapName)
                ? BuildOptionalExtras(mapName)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var rows = new List<ScanRow>();

            foreach (var reference in refs.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
            {
                var inPak = _pakEntries.ContainsKey(reference);
                var found = FindOnDisk(reference);
                var exists = found != null;
                var fullPath = found ?? ToDiskPath(reference);

                // Los extras y los archivos opcionales del modelo (.phy, .dx80.vtx,
                // .sw.vtx) pueden no existir sin que nada se rompa; si no estan ni
                // en disco ni en el PAK, no son un problema que reportar.
                if (!exists && !inPak &&
                    (optionalExtras.Contains(reference) || GameReference.IsOptionalModelCompanion(reference)))
                    continue;

                var baseGame = !inPak && baseIndex.Contains(reference);

                rows.Add(new ScanRow
                {
                    Path = reference,
                    ExistsOnDisk = exists,
                    InPak = inPak,
                    BaseGame = baseGame,
                    FullDiskPath = fullPath,
                    Status = inPak ? ScanStatus.AlreadyInPak
                           : baseGame ? ScanStatus.BaseGameVpk
                           : exists ? ScanStatus.CanAdd
                           : ScanStatus.MissingOnDisk,
                });
            }

            return new ScanResult { Rows = rows, Summary = Summarize(rows) };
        }

        public static ScanSummary Summarize(IEnumerable<ScanRow> rows)
        {
            var summary = new ScanSummary();
            foreach (var row in rows)
            {
                if (row.InPak) { summary.AlreadyInPak++; continue; }
                if (row.BaseGame) continue;           // lo provee el juego: no falta

                summary.MissingTotal++;
                if (row.Addable) summary.CanAdd++;
                else summary.NotFound++;
            }
            return summary;
        }

        /// <summary>
        /// Cada .mdl aporta los materiales que declara. Con varios
        /// <c>$cdmaterials</c> el motor usa la primera ruta que existe, asi que se
        /// agrega esa; las texturas que no aparecen en ninguna se devuelven para
        /// decidir despues, con el indice de los VPK del juego.
        /// </summary>
        private List<IReadOnlyList<string>> ExpandModelMaterials(ISet<string> refs)
        {
            var unresolved = new List<IReadOnlyList<string>>();
            var models = refs.Where(r => r.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var model in models)
            {
                var bytes = ReadBinary(model);
                if (bytes == null) continue;

                foreach (var candidates in MdlReader.GetMaterialCandidates(bytes))
                {
                    var present = candidates.FirstOrDefault(c =>
                        refs.Contains(c) || _pakEntries.ContainsKey(c) || FindOnDisk(c) != null);

                    if (present != null) refs.Add(present);
                    else if (candidates.Count == 1) refs.Add(candidates[0]);
                    else unresolved.Add(candidates);
                }
            }

            return unresolved;
        }

        /// <summary>
        /// Archivos de texto que nombran otros archivos: el manifiesto de
        /// particulas lista los .pcf, cada .pcf nombra los materiales de sus
        /// sistemas, y los soundscapes y sonidos del nivel nombran .wav. Empaquetar
        /// el manifiesto sin lo que lista deja el mapa sin particulas ni sonido.
        /// </summary>
        private void ExpandScripts(ISet<string> refs)
        {
            foreach (var manifest in refs.Where(IsParticleManifest).ToList())
            {
                var text = ReadText(manifest);
                if (text == null) continue;

                // "!" al principio solo pide precarga; el archivo es el mismo.
                foreach (Match m in ManifestFilePattern.Matches(text))
                    GameReference.AddRef(refs, m.Groups[1].Value.TrimStart('!'));
            }

            foreach (var pcf in refs.Where(r => r.EndsWith(".pcf", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var bytes = ReadBinary(pcf);
                if (bytes == null) continue;

                // Los .pcf suelen ser DMX binario: se buscan las cadenas con extension.
                foreach (Match m in ParticleAssetPattern.Matches(ZipInspector.Latin1.GetString(bytes)))
                {
                    if (m.Value.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
                        GameReference.AddModelWithCompanions(refs, m.Value);
                    else
                        GameReference.AddMaterialReference(refs, m.Value);
                }
            }

            foreach (var script in refs.Where(IsSoundScript).ToList())
            {
                var text = ReadText(script);
                if (text == null) continue;

                foreach (Match m in WavePattern.Matches(text))
                {
                    // Los prefijos (*, #, ^, ) ...) son modificadores del motor, no parte del nombre.
                    var wave = m.Groups[1].Value.Trim().TrimStart(SoundPrefixChars);
                    if (SoundFilePattern.IsMatch(wave)) GameReference.AddRef(refs, wave);
                }
            }
        }

        private static bool IsParticleManifest(string path) =>
            path.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith("_particles.txt", StringComparison.OrdinalIgnoreCase);

        private static bool IsSoundScript(string path) =>
            (path.StartsWith("scripts/soundscapes", StringComparison.OrdinalIgnoreCase) &&
             path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) ||
            (path.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) &&
             path.EndsWith("_level_sounds.txt", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Recorre los .vmt en anchura: cada material puede incluir otros y pedir
        /// texturas, y esos incluidos pueden a su vez pedir mas.
        /// </summary>
        private void ExpandVmtDependencies(ISet<string> refs)
        {
            var queue = new Queue<string>(refs.Where(r => r.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase)));
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var iterations = 0;

            while (queue.Count > 0 && iterations++ < MaxExpansionIterations)
            {
                var vmtRef = queue.Dequeue();
                if (!processed.Add(vmtRef)) continue;

                var content = ReadText(vmtRef);
                if (content != null)
                {
                    foreach (var dependency in VmtParser.GetDependencies(content))
                    {
                        if (refs.Add(dependency) &&
                            dependency.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
                        {
                            queue.Enqueue(dependency);
                        }
                    }
                }

                // Convencion casi universal: el .vtf que acompania al .vmt con el
                // mismo nombre, aunque el material no lo declare explicitamente.
                var sameBaseVtf = vmtRef.Substring(0, vmtRef.Length - 4) + ".vtf";
                if (_pakEntries.ContainsKey(sameBaseVtf) || FindOnDisk(sameBaseVtf) != null)
                    refs.Add(sameBaseVtf);
            }
        }

        private static HashSet<string> BuildOptionalExtras(string mapName)
        {
            return new HashSet<string>(BspReferenceScanner.MapExtras(mapName), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Ruta donde iria el archivo en el Game Path, exista o no.</summary>
        private string ToDiskPath(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(_gameRoot) || !IsValidPath(archivePath)) return string.Empty;
            return Path.Combine(_gameRoot, archivePath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// Una referencia con | &lt; &gt; o caracteres de control no puede existir en
        /// disco, y Path.Combine lanza con ella: sin este filtro, una sola
        /// referencia rara en el BSP abortaba el scan entero.
        /// </summary>
        private static bool IsValidPath(string archivePath) => archivePath.IndexOfAny(InvalidPathChars) < 0;

        /// <summary>
        /// Primera ruta existente en disco para la referencia, recorriendo los
        /// SearchPaths en el orden en que lo hace el motor. Null si no esta.
        /// </summary>
        private string FindOnDisk(string archivePath)
        {
            if (!IsValidPath(archivePath)) return null;
            var relative = archivePath.Replace('/', Path.DirectorySeparatorChar);
            foreach (var dir in _searchDirs)
            {
                var candidate = Path.Combine(dir, relative);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>Lo empaquetado gana sobre el disco: es lo que el mapa lleva.</summary>
        private byte[] ReadBinary(string archivePath)
        {
            if (_pakEntries.TryGetValue(archivePath, out var entry)) return entry.Data;

            var diskPath = FindOnDisk(archivePath);
            if (diskPath == null) return null;

            try { return File.ReadAllBytes(diskPath); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private string ReadText(string archivePath)
        {
            var bytes = ReadBinary(archivePath);
            if (bytes == null) return null;

            try { return Encoding.UTF8.GetString(bytes); }
            catch (ArgumentException) { return null; }
        }
    }
}
