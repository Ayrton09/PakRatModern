using System;
using System.IO;
using System.Text.RegularExpressions;

namespace PakRatModern.Core
{
    /// <summary>
    /// Normalizacion y validacion de rutas internas del PAK.
    ///
    /// Las reglas son identicas a las de la CLI (_norm_archive_path) y del GUI
    /// (Normalize-ArchivePath): bloquean escapes tipo "../", rutas absolutas,
    /// caracteres que NTFS no admite y nombres reservados de Windows, de modo que
    /// extraer un BSP de origen desconocido no pueda escribir fuera del destino.
    /// </summary>
    public static class ArchivePath
    {
        private static readonly bool IsWindows =
            Environment.OSVersion.Platform == PlatformID.Win32NT;

        // Verbatim: los escapes \x00 los interpreta el motor de regex, no el compilador.
        private static readonly Regex UnsafeChars =
            new Regex(@"[\x00-\x1f<>:""|?*]", RegexOptions.Compiled);

        private static readonly Regex ReservedNames =
            new Regex(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$",
                      RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Invalid internal path.", nameof(path));

            var p = path.Replace('\\', '/').Trim();

            if (p.StartsWith("/", StringComparison.Ordinal))
                throw new ArgumentException($"Unsafe absolute internal path: {path}", nameof(path));

            if (string.IsNullOrWhiteSpace(p) || p == ".")
                throw new ArgumentException("Invalid internal path.", nameof(path));

            if (UnsafeChars.IsMatch(p))
                throw new ArgumentException($"Unsafe internal path characters: {path}", nameof(path));

            foreach (var part in p.Split('/'))
            {
                if (string.IsNullOrWhiteSpace(part) || part == "." || part == "..")
                    throw new ArgumentException($"Unsafe internal path: {path}", nameof(path));

                if (part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal))
                    throw new ArgumentException($"Unsafe internal path segment: {path}", nameof(path));

                if (ReservedNames.IsMatch(part))
                    throw new ArgumentException($"Unsafe reserved internal path segment: {path}", nameof(path));
            }

            return p;
        }

        /// <summary>
        /// Carpetas raiz que el motor reconoce dentro del contenido de un juego.
        /// </summary>
        private static readonly string[] KnownRoots =
        {
            "materials", "models", "sound", "maps", "resource",
            "particles", "scripts", "scenes", "media", "cfg",
        };

        /// <summary>
        /// Deduce la ruta interna del PAK a partir de un archivo del disco.
        ///
        /// Primero se prueba relativo al Game Path, que es el caso normal. Dentro
        /// de el, <c>custom/&lt;mod&gt;/</c> y <c>download/</c> son puntos de
        /// montaje que el motor agrega como SearchPaths propios: un archivo en
        /// <c>cstrike/custom/mimod/materials/x.vmt</c> se carga como
        /// <c>materials/x.vmt</c>, asi que esa es la ruta que hay que empaquetar.
        /// Empaquetarlo como <c>custom/mimod/materials/x.vmt</c> produce un mapa
        /// con texturas rosas y ningun error.
        ///
        /// Si el archivo esta fuera del Game Path, se busca una carpeta raiz
        /// conocida dentro de la ruta: sin eso, arrastrar una carpeta suelta al
        /// programa no podria deducir donde va el archivo dentro del mapa.
        ///
        /// Devuelve null si no se puede deducir, en vez de inventar una ruta.
        /// </summary>
        public static string FromDiskPath(string filePath, string gameRoot)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;

            var full = Path.GetFullPath(filePath);

            if (!string.IsNullOrWhiteSpace(gameRoot))
            {
                var root = Path.GetFullPath(gameRoot);
                if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                    root += Path.DirectorySeparatorChar;

                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return TryNormalize(StripMountPrefix(full.Substring(root.Length).Replace('\\', '/')));
            }

            var fromKnownRoot = FromKnownRoot(full.Replace('\\', '/').Split('/'));
            return fromKnownRoot == null ? null : TryNormalize(fromKnownRoot);
        }

        /// <summary>
        /// Quita el punto de montaje (<c>custom/&lt;mod&gt;/</c> o
        /// <c>download/</c>) de una ruta relativa al Game Path. Si tras eso el
        /// primer segmento no es una raiz conocida, se busca una mas adentro; si
        /// no hay ninguna, se devuelve la ruta relativa tal cual.
        /// </summary>
        internal static string StripMountPrefix(string relative)
        {
            var parts = relative.Split('/');
            var skip = 0;

            if (parts.Length > 2 && string.Equals(parts[0], "custom", StringComparison.OrdinalIgnoreCase))
                skip = 2;
            else if (parts.Length > 1 && string.Equals(parts[0], "download", StringComparison.OrdinalIgnoreCase))
                skip = 1;

            var remainder = skip == 0 ? relative : string.Join("/", parts, skip, parts.Length - skip);
            var remainderParts = remainder.Split('/');

            if (remainderParts.Length > 0 && IsKnownRoot(remainderParts[0]))
                return remainder;

            return FromKnownRoot(remainderParts) ?? remainder;
        }

        private static string FromKnownRoot(string[] parts)
        {
            for (var i = 0; i < parts.Length; i++)
            {
                if (IsKnownRoot(parts[i]))
                    return string.Join("/", parts, i, parts.Length - i);
            }
            return null;
        }

        private static bool IsKnownRoot(string segment)
        {
            foreach (var known in KnownRoots)
            {
                if (string.Equals(segment, known, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string TryNormalize(string relative)
        {
            try { return Normalize(relative); }
            catch (ArgumentException) { return null; }
        }

        /// <summary>
        /// Resuelve la ruta de extraccion y verifica que caiga dentro de la raiz.
        /// Segunda barrera despues de <see cref="Normalize"/>: cubre el caso de un
        /// nombre que el sistema de archivos reinterprete de forma inesperada.
        /// </summary>
        public static string ResolveExtractionTarget(string targetRoot, string entryName)
        {
            var root = Path.GetFullPath(targetRoot);
            var relative = Normalize(entryName).Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(root, relative));

            var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;

            // En Windows la comparacion ignora mayusculas porque el sistema de
            // archivos tampoco las distingue. En Linux si las distingue, y
            // ignorarlas dejaria pasar "/salida/../SALIDA/x" como si estuviera
            // dentro de "/salida".
            var comparison = IsWindows
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!target.StartsWith(rootPrefix, comparison))
                throw new InvalidOperationException($"Unsafe extraction path: {entryName}");

            return target;
        }
    }
}
