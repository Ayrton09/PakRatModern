using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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

        private readonly IReadOnlyDictionary<string, PakEntry> _pakEntries;
        private readonly string _gameRoot;

        public ScanService(IReadOnlyDictionary<string, PakEntry> pakEntries, string gameRoot)
        {
            _pakEntries = pakEntries;
            _gameRoot = gameRoot;
        }

        public ScanResult Scan(BspFile bsp, string mapName, bool includeExtras, Action<string> onProgress = null)
        {
            void Report(string message) => onProgress?.Invoke(message);

            Report("Scan: reading BSP references...");
            var refs = BspReferenceScanner.Collect(bsp, mapName, includeExtras);

            Report("Scan: expanding model materials...");
            ExpandModelMaterials(refs);

            Report("Scan: expanding material dependencies...");
            ExpandVmtDependencies(refs);

            Report("Scan: checking base game VPKs...");
            var baseIndex = BaseGameIndex.Build(_gameRoot, refs);

            Report("Scan: checking disk files...");
            var optionalExtras = includeExtras && !string.IsNullOrWhiteSpace(mapName)
                ? BuildOptionalExtras(mapName)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var rows = new List<ScanRow>();
            var existsCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var reference in refs.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
            {
                var inPak = _pakEntries.ContainsKey(reference);
                var fullPath = ToDiskPath(reference);

                if (!existsCache.TryGetValue(fullPath, out var exists))
                {
                    exists = File.Exists(fullPath);
                    existsCache[fullPath] = exists;
                }

                // Los extras son archivos opcionales por convencion de nombre; si
                // no existen ni estan en el PAK, no son un problema que reportar.
                if (!exists && !inPak && optionalExtras.Contains(reference)) continue;

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

        /// <summary>Cada .mdl aporta los materiales que declara internamente.</summary>
        private void ExpandModelMaterials(ISet<string> refs)
        {
            var models = refs.Where(r => r.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var model in models)
            {
                var bytes = ReadBinary(model);
                if (bytes == null) continue;

                foreach (var material in MdlReader.GetMaterialRefs(bytes))
                    refs.Add(material);
            }
        }

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
                if (_pakEntries.ContainsKey(sameBaseVtf) || File.Exists(ToDiskPath(sameBaseVtf)))
                    refs.Add(sameBaseVtf);
            }
        }

        private static HashSet<string> BuildOptionalExtras(string mapName)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                $"maps/{mapName}.nav",
                $"maps/{mapName}.txt",
                $"resource/overviews/{mapName}.txt",
                $"resource/overviews/{mapName}.dds",
                $"resource/overviews/{mapName}_radar.dds",
                $"materials/overviews/{mapName}.vmt",
                $"materials/overviews/{mapName}.vtf",
                $"materials/overviews/{mapName}_radar.vmt",
                $"materials/overviews/{mapName}_radar.vtf",
            };
        }

        private string ToDiskPath(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(_gameRoot)) return string.Empty;
            return Path.Combine(_gameRoot, archivePath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>Lo empaquetado gana sobre el disco: es lo que el mapa lleva.</summary>
        private byte[] ReadBinary(string archivePath)
        {
            if (_pakEntries.TryGetValue(archivePath, out var entry)) return entry.Data;

            var diskPath = ToDiskPath(archivePath);
            if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath)) return null;

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
