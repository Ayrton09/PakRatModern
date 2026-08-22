using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PakRatModern.Core
{
    /// <summary>
    /// Lectura del bloque <c>SearchPaths</c> de <c>gameinfo.txt</c>.
    ///
    /// Ese bloque define donde busca contenido el juego, asi que es lo que
    /// determina si un archivo referenciado por el mapa ya viene incluido o hay
    /// que empaquetarlo.
    /// </summary>
    public static class GameInfo
    {
        private static readonly Regex CommentPattern = new Regex(@"//.*$", RegexOptions.Compiled);
        private static readonly Regex SearchPathsHeader = new Regex(@"^""?SearchPaths""?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SearchPathsOnly = new Regex(@"^""?SearchPaths""?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TokenPattern = new Regex(@"""([^""]*)""|([^\s{}]+)", RegexOptions.Compiled);
        private static readonly Regex GameInfoPathToken = new Regex(@"^\|gameinfo_path\|", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AllSourcePathsToken = new Regex(@"^\|all_source_engine_paths\|", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Valores crudos declarados en el bloque SearchPaths.</summary>
        public static IReadOnlyList<string> ReadSearchPathValues(string gameRoot)
        {
            var values = new List<string>();
            var gameInfoPath = Path.Combine(gameRoot, "gameinfo.txt");
            if (!File.Exists(gameInfoPath)) return values;

            var inSearchPaths = false;
            var braceDepth = 0;
            var seenOpenBrace = false;

            foreach (var rawLine in File.ReadAllLines(gameInfoPath))
            {
                var line = CommentPattern.Replace(rawLine, string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (!inSearchPaths)
                {
                    if (SearchPathsHeader.IsMatch(line)) inSearchPaths = true;
                    else continue;
                }

                var openCount = CountChar(line, '{');
                var closeCount = CountChar(line, '}');
                if (openCount > 0) seenOpenBrace = true;
                braceDepth += openCount;

                var body = line.Replace("{", " ").Replace("}", " ").Trim();

                if (seenOpenBrace && !string.IsNullOrWhiteSpace(body) && !SearchPathsOnly.IsMatch(body))
                {
                    var tokens = new List<string>();
                    foreach (Match m in TokenPattern.Matches(body))
                    {
                        var token = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                        if (!string.IsNullOrWhiteSpace(token)) tokens.Add(token);
                    }

                    // Formato "<clave> <ruta>": la ruta es el ultimo token.
                    if (tokens.Count >= 2) values.Add(tokens[tokens.Count - 1]);
                }

                braceDepth -= closeCount;
                if (seenOpenBrace && braceDepth <= 0) break;
            }

            return values;
        }

        /// <summary>
        /// Expande los tokens del motor (<c>|gameinfo_path|</c>,
        /// <c>|all_source_engine_paths|</c>) a una ruta real del disco.
        /// Devuelve null si el valor tiene comodines o no se puede resolver.
        /// </summary>
        public static string ResolveSearchPath(string gameRoot, string searchPath)
        {
            if (string.IsNullOrWhiteSpace(searchPath)) return null;

            var value = searchPath.Trim().Trim('"').Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(value) || value.Contains("*")) return null;
            if (value == "GAME") return gameRoot;

            var sourceRoot = Path.GetDirectoryName(gameRoot);
            if (string.IsNullOrWhiteSpace(sourceRoot)) sourceRoot = gameRoot;

            if (GameInfoPathToken.IsMatch(value))
                return CombineToken(gameRoot, value, "|gameinfo_path|".Length);

            if (AllSourcePathsToken.IsMatch(value))
                return CombineToken(sourceRoot, value, "|all_source_engine_paths|".Length);

            return Path.IsPathRooted(value)
                ? Path.GetFullPath(value)
                : Path.GetFullPath(Path.Combine(sourceRoot, value));
        }

        /// <summary>
        /// Directorios donde el juego busca archivos sueltos, en orden de
        /// prioridad: el Game Path primero y despues cada SearchPath de
        /// gameinfo.txt que sea una carpeta existente. Un valor con comodin
        /// final (<c>custom/*</c>) se expande a sus subcarpetas, que es como el
        /// motor monta el contenido de terceros.
        /// </summary>
        public static IReadOnlyList<string> ResolveSearchDirectories(string gameRoot)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string dir)
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
                var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (seen.Add(full)) result.Add(full);
            }

            if (string.IsNullOrWhiteSpace(gameRoot)) return result;
            Add(gameRoot);

            foreach (var raw in ReadSearchPathValues(gameRoot))
            {
                var value = raw.Trim().Trim('"').Replace('\\', '/');

                if (value.EndsWith("/*", StringComparison.Ordinal))
                {
                    var parent = ResolveSearchPath(gameRoot, value.Substring(0, value.Length - 2));
                    if (parent == null || !Directory.Exists(parent)) continue;

                    try
                    {
                        foreach (var sub in Directory.GetDirectories(parent).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                            Add(sub);
                    }
                    catch (Exception)
                    {
                        // Carpeta inaccesible: se ignora este SearchPath.
                    }
                    continue;
                }

                var resolved = ResolveSearchPath(gameRoot, value);
                if (resolved != null && !resolved.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
                    Add(resolved);
            }

            return result;
        }

        private static string CombineToken(string root, string value, int prefixLength)
        {
            var suffix = value.Substring(prefixLength).TrimStart('/', '\\');
            if (string.IsNullOrWhiteSpace(suffix) || suffix == ".") return root;
            return Path.GetFullPath(Path.Combine(root, suffix));
        }

        private static int CountChar(string text, char c)
        {
            var n = 0;
            foreach (var ch in text) if (ch == c) n++;
            return n;
        }
    }
}
