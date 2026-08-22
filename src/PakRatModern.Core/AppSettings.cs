using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PakRatModern.Core
{
    /// <summary>
    /// Preferencias persistentes. El archivo es el mismo que escribia la version
    /// en PowerShell, asi que actualizar conserva las rutas ya configuradas.
    /// </summary>
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember(Name = "GameRoot")]
        public string GameRoot { get; set; } = string.Empty;

        [DataMember(Name = "SavedGameRoots")]
        public List<string> SavedGameRoots { get; set; } = new List<string>();

        /// <summary>
        /// Se conserva solo para no perder el campo al reescribir un archivo de
        /// settings viejo. Ninguna version compilada lo consulta.
        /// </summary>
        [DataMember(Name = "PathFixupMode")]
        public string PathFixupMode { get; set; } = "Ask";

        [DataMember(Name = "IncludeExtrasInScan")]
        public bool IncludeExtrasInScan { get; set; } = true;

        [DataMember(Name = "BackupBeforeInPlaceSave")]
        public bool BackupBeforeInPlaceSave { get; set; } = true;

        public static AppSettings Load()
        {
            var path = File.Exists(AppPaths.SettingsPath) ? AppPaths.SettingsPath
                     : File.Exists(AppPaths.LegacySettingsPath) ? AppPaths.LegacySettingsPath
                     : null;

            if (path == null) return new AppSettings();

            var isLegacy = path == AppPaths.LegacySettingsPath;

            try
            {
                // UTF-8 explicito: con la codificacion ANSI local, una ruta con
                // acentos o ene se leia corrupta.
                var json = File.ReadAllText(path, new UTF8Encoding(false));
                if (string.IsNullOrWhiteSpace(json)) return new AppSettings();

                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    var settings = (AppSettings)serializer.ReadObject(ms) ?? new AppSettings();
                    settings.Normalize();

                    // Migracion inmediata: si solo se copiara al guardar, un usuario
                    // que nunca toca las preferencias seguiria dependiendo del
                    // archivo viejo, que es justo el que puede quedar en una carpeta
                    // de solo lectura.
                    if (isLegacy)
                    {
                        try { settings.Save(); }
                        catch (Exception) { /* Se sigue usando lo leido. */ }
                    }

                    return settings;
                }
            }
            catch (Exception)
            {
                // Un archivo corrupto no debe impedir arrancar.
                return new AppSettings();
            }
        }

        public void Save()
        {
            Normalize();

            using (var ms = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                serializer.WriteObject(ms, this);
                File.WriteAllText(AppPaths.SettingsPath, Encoding.UTF8.GetString(ms.ToArray()), new UTF8Encoding(false));
            }
        }

        /// <summary>
        /// Quita rutas duplicadas o vacias y canonicaliza las demas. No comprueba
        /// que existan: una unidad externa desconectada no debe borrar su ruta.
        /// </summary>
        public void Normalize()
        {
            GameRoot = NormalizeFolder(GameRoot) ?? string.Empty;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cleaned = new List<string>();

            foreach (var raw in SavedGameRoots ?? new List<string>())
            {
                var folder = NormalizeFolder(raw);
                if (folder == null || !seen.Add(folder)) continue;
                cleaned.Add(folder);
            }

            if (!string.IsNullOrEmpty(GameRoot) && seen.Add(GameRoot))
                cleaned.Insert(0, GameRoot);

            SavedGameRoots = cleaned;
        }

        public void RememberGameRoot(string folder)
        {
            var normalized = NormalizeFolder(folder);
            if (normalized == null) return;

            GameRoot = normalized;
            if (!SavedGameRoots.Any(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase)))
                SavedGameRoots.Insert(0, normalized);
        }

        public void ForgetGameRoot(string folder)
        {
            SavedGameRoots.RemoveAll(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(GameRoot, folder, StringComparison.OrdinalIgnoreCase))
                GameRoot = SavedGameRoots.FirstOrDefault() ?? string.Empty;
        }

        private static string NormalizeFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                var full = Path.GetFullPath(path.Trim().Trim('"'));
                return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
