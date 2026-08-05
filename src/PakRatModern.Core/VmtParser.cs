using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PakRatModern.Core
{
    /// <summary>
    /// Extrae de un material <c>.vmt</c> los archivos de los que depende.
    ///
    /// Empaquetar el .vmt sin sus .vtf produce el error mas visible de todos: la
    /// textura rosa y negra. Tambien se siguen los <c>include</c>, que apuntan a
    /// otros materiales.
    /// </summary>
    public static class VmtParser
    {
        /// <summary>Parametros cuyo valor es una textura (.vtf).</summary>
        private static readonly HashSet<string> TextureKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "basetexture", "basetexture2", "texture2", "bumpmap", "bumpmap2", "normalmap",
            "envmapmask", "detail", "envmap", "selfillummask", "selfillumtexture", "flowmap",
            "dudvmap", "lightwarptexture", "phongexponenttexture", "iris", "blendmodulatetexture",
            "ambientocclusiontexture", "flashlighttexture", "refracttexture", "reflecttexture",
            "blurtexture", "normalmapalphaenvmapmask", "basetexturenoenvmap",
        };

        /// <summary>Parametros cuyo valor es otro material (.vmt).</summary>
        private static readonly HashSet<string> MaterialKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bottommaterial", "crackmaterial",
        };

        private static readonly Regex IncludePattern =
            new Regex(@"^\s*""?include""?\s+(?:""([^""]+)""|([^\s{}]+))",
                      RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        private static readonly Regex ParameterPattern =
            new Regex(@"^\s*""?\$([A-Za-z0-9_]+)""?\s+(?:""([^""]+)""|([^\s{}]+))",
                      RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        private static readonly Regex NumberPattern =
            new Regex(@"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$", RegexOptions.Compiled);

        private static readonly Regex VectorPattern =
            new Regex(@"^[\{\[]?[+-]?\d+(?:\.\d+)?(?:\s+[+-]?\d+(?:\.\d+)?)+[\}\]]?$", RegexOptions.Compiled);

        private static readonly Regex KeywordPattern =
            new Regex(@"^(env_cubemap|none|null|black|white)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex BareIdentifierPattern =
            new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        public static IReadOnlyList<string> GetDependencies(string content)
        {
            var refs = new List<string>();
            if (string.IsNullOrWhiteSpace(content)) return refs;

            foreach (Match m in IncludePattern.Matches(content))
                AddDependency(refs, FirstGroup(m, 1, 2), ".vmt");

            foreach (Match m in ParameterPattern.Matches(content))
            {
                var key = m.Groups[1].Value;
                var value = FirstGroup(m, 2, 3);

                if (TextureKeys.Contains(key))
                {
                    AddDependency(refs, value, ".vtf");
                }
                else if (MaterialKeys.Contains(key))
                {
                    AddDependency(refs, value, ".vmt");
                }
                else if (LooksLikePath(value))
                {
                    // Parametro desconocido cuyo valor parece una ruta: se asume
                    // textura salvo que declare explicitamente ser un material.
                    var isVmt = value.Trim().Trim('"').EndsWith(".vmt", StringComparison.OrdinalIgnoreCase);
                    AddDependency(refs, value, isVmt ? ".vmt" : ".vtf");
                }
            }

            return refs;
        }

        private static void AddDependency(ICollection<string> refs, string value, string extension)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            var candidate = value.Trim();
            if (NumberPattern.IsMatch(candidate)) return;
            if (KeywordPattern.IsMatch(candidate)) return;

            if (!candidate.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                candidate += extension;

            var reference = GameReference.Normalize(candidate);
            if (reference == null) return;

            if (!reference.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
                reference = "materials/" + reference;

            refs.Add(reference);
        }

        /// <summary>
        /// Distingue una ruta de un valor escalar. Los .vmt mezclan ambos en la
        /// misma sintaxis, asi que hay que descartar numeros, vectores tipo
        /// "[1 1 1]", palabras clave y nombres sueltos sin carpeta ni extension.
        /// </summary>
        private static bool LooksLikePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            var candidate = value.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            if (NumberPattern.IsMatch(candidate)) return false;
            if (VectorPattern.IsMatch(candidate)) return false;
            if (KeywordPattern.IsMatch(candidate)) return false;
            if (BareIdentifierPattern.IsMatch(candidate)) return false;

            return candidate.IndexOfAny(new[] { '/', '\\' }) >= 0
                || candidate.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase)
                || candidate.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstGroup(Match match, params int[] groupIndexes)
        {
            foreach (var index in groupIndexes)
            {
                if (match.Groups[index].Success) return match.Groups[index].Value;
            }
            return string.Empty;
        }
    }
}
