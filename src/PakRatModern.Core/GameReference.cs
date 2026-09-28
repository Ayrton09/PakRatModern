using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace PakRatModern.Core
{
    /// <summary>
    /// Normalizacion de rutas tal como las escribe el motor y las herramientas de
    /// mapeo: con barras invertidas, sin el prefijo de carpeta, entrecomilladas, o
    /// sin extension. Se convierten a la forma canonica que usa el PAK.
    /// </summary>
    public static class GameReference
    {
        private static readonly Regex NumericOnly = new Regex(@"^[+-]?\d+$", RegexOptions.Compiled);

        /// <summary>
        /// Antepone la carpeta raiz que corresponde a la extension. Devuelve null
        /// si la ruta es inutilizable (vacia, con ".." o con unidad de disco).
        /// </summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var v = value.Trim().Trim('"').Replace('\\', '/');
            while (v.StartsWith("/", StringComparison.Ordinal)) v = v.Substring(1);

            if (string.IsNullOrWhiteSpace(v) || v.Contains("..") || v.Contains(":")) return null;

            var ext = GetExtension(v);
            switch (ext)
            {
                case ".vmt":
                case ".vtf":
                    return EnsurePrefix(v, "materials/");
                case ".mdl":
                case ".vvd":
                case ".vtx":
                case ".phy":
                    return EnsurePrefix(v, "models/");
                case ".wav":
                case ".mp3":
                    return EnsurePrefix(v, "sound/");
                default:
                    return v;
            }
        }

        public static void AddRef(ISet<string> set, string reference)
        {
            var norm = Normalize(reference);
            if (norm != null) set.Add(norm);
        }

        /// <summary>
        /// Agrega el .mdl junto con los archivos que el motor carga siempre con el:
        /// sin ellos el modelo no se ve, y son el olvido mas comun al empaquetar.
        /// </summary>
        public static void AddModelWithCompanions(ISet<string> set, string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath)) return;

            var candidate = modelPath.Trim().Trim('"').Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(candidate)) return;
            if (GetExtension(candidate).Length == 0) candidate += ".mdl";

            var mdl = Normalize(candidate);
            if (mdl == null) return;

            set.Add(mdl);

            var dot = mdl.LastIndexOf('.');
            var basePath = dot > 0 ? mdl.Substring(0, dot) : mdl;
            if (string.IsNullOrWhiteSpace(basePath)) return;

            foreach (var suffix in new[] { ".vvd", ".phy", ".dx80.vtx", ".dx90.vtx", ".sw.vtx" })
                set.Add(basePath + suffix);
        }

        public static void AddModelReference(ISet<string> set, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            var candidate = value.Trim().Trim('"').Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(candidate)) return;

            // "*12" son modelos brush internos del BSP, no archivos.
            if (candidate.StartsWith("*", StringComparison.Ordinal)) return;
            if (NumericOnly.IsMatch(candidate)) return;
            if (candidate.Contains("..") || candidate.Contains(":")) return;

            var ext = GetExtension(candidate);
            if (ext.Length == 0 || ext == ".mdl")
            {
                AddModelWithCompanions(set, candidate);
            }
            else if (ext == ".vmt" || ext == ".spr")
            {
                // env_sprite y env_glow guardan su material en "model":
                // "sprites/glow01.vmt", o "sprites/glow01.spr" al estilo HL1,
                // que el motor resuelve a materials/sprites/glow01.vmt.
                AddMaterialReference(set, candidate);
            }
        }

        /// <summary>
        /// Convierte una referencia a material en su .vmt: el motor carga el .vmt,
        /// y el .vtf solo llega como consecuencia de lo que ese .vmt declare.
        /// </summary>
        public static string ToMaterialVmt(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var candidate = value.Trim().Trim('"').Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Contains("..") || candidate.Contains(":"))
                return null;

            var ext = GetExtension(candidate);
            if (ext == ".vtf" || ext == ".spr") candidate = candidate.Substring(0, candidate.Length - 4) + ".vmt";
            else if (ext != ".vmt") candidate += ".vmt";

            var reference = Normalize(candidate);
            if (reference == null) return null;

            return EnsurePrefix(reference, "materials/");
        }

        public static void AddMaterialReference(ISet<string> set, string value)
        {
            var reference = ToMaterialVmt(value);
            if (reference != null) set.Add(reference);
        }

        /// <summary>
        /// Una textura suelta, no un material: <c>env_projectedtexture</c> carga
        /// su "texturename" directamente como .vtf.
        /// </summary>
        public static void AddTextureReference(ISet<string> set, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            var candidate = value.Trim().Trim('"').Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Contains("..") || candidate.Contains(":")) return;
            if (IsRenderTarget(candidate)) return;

            if (GetExtension(candidate) != ".vtf") candidate += ".vtf";
            set.Add(EnsurePrefix(candidate, "materials/"));
        }

        /// <summary>
        /// Los nombres <c>_rt_*</c> son render targets que crea el motor
        /// (reflejo del agua, camaras, pantalla completa), no archivos.
        /// </summary>
        public static bool IsRenderTarget(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var name = value.Trim().Trim('"').Replace('\\', '/');
            var slash = name.LastIndexOf('/');
            return name.Substring(slash + 1).StartsWith("_rt_", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Archivos del modelo que pueden no existir sin que nada se rompa: el
        /// .phy solo esta si el modelo tiene colision, y .dx80/.sw solo los
        /// generan los compiladores viejos. Si faltan no se reportan.
        /// </summary>
        public static bool IsOptionalModelCompanion(string archivePath)
        {
            return archivePath.EndsWith(".phy", StringComparison.OrdinalIgnoreCase)
                || archivePath.EndsWith(".dx80.vtx", StringComparison.OrdinalIgnoreCase)
                || archivePath.EndsWith(".sw.vtx", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Combina el directorio de texturas declarado por un modelo con el nombre
        /// de textura, como hace el motor: siempre directorio + nombre, aunque el
        /// nombre traiga subcarpetas.
        /// </summary>
        public static string JoinModelMaterialPath(string directory, string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName)) return null;

            var texture = textureName.Trim().Trim('"').Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(texture) || texture.Contains("..") || texture.Contains(":"))
                return null;

            if (texture.StartsWith("materials/", StringComparison.OrdinalIgnoreCase)) return texture;

            var dir = string.Empty;
            if (!string.IsNullOrWhiteSpace(directory))
            {
                dir = directory.Trim().Trim('"').Replace('\\', '/').Trim('/');
                if (dir.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
                    dir = dir.Substring("materials/".Length);
            }

            return string.IsNullOrWhiteSpace(dir) ? texture : dir + "/" + texture;
        }

        private static string EnsurePrefix(string value, string prefix) =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? value : prefix + value;

        private static string GetExtension(string path)
        {
            var slash = path.LastIndexOf('/');
            var dot = path.LastIndexOf('.');
            if (dot < 0 || dot < slash) return string.Empty;
            return path.Substring(dot).ToLowerInvariant();
        }

        /// <summary>
        /// Cadena terminada en cero. Se lee como Latin-1, igual que los nombres
        /// del PAK: Hammer escribe los bytes del sistema del mapper y el motor
        /// los compara crudos, asi que un "é" tiene que seguir siendo el mismo byte.
        /// </summary>
        internal static string ReadNullTerminated(byte[] bytes, int offset)
        {
            if (bytes == null || offset < 0 || offset >= bytes.Length) return string.Empty;

            var end = offset;
            while (end < bytes.Length && bytes[end] != 0) end++;
            if (end <= offset) return string.Empty;

            return ZipInspector.Latin1.GetString(bytes, offset, end - offset);
        }

        internal static int? ReadInt32Safe(byte[] bytes, int offset)
        {
            if (bytes == null || offset < 0 || offset + 4 > bytes.Length) return null;
            return BitConverter.ToInt32(bytes, offset);
        }
    }
}
