using System;
using System.IO;

namespace PakRatModern.Core
{
    /// <summary>Ubicaciones de los archivos de la aplicacion.</summary>
    public static class AppPaths
    {
        private const string FolderName = "PakRatModern";
        private const string SettingsFileName = "pakrat_modern_gui.settings.json";
        private const string LogFileName = "PakRatModern-startup.log";

        /// <summary>
        /// Directorio del ejecutable: recursos que vienen con la app.
        /// Se usa AppContext.BaseDirectory y no Assembly.Location porque este
        /// ultimo devuelve una cadena vacia cuando la app se publica como un
        /// unico archivo.
        /// </summary>
        public static string AppDirectory =>
            AppContext.BaseDirectory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            ?? Environment.CurrentDirectory;

        /// <summary>
        /// Directorio de datos mutables. No se usa el del ejecutable porque si la
        /// app se instala en Program Files ese directorio es de solo lectura y
        /// cada guardado falla.
        /// </summary>
        public static string DataDirectory
        {
            get
            {
                try
                {
                    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    if (!string.IsNullOrWhiteSpace(local))
                    {
                        var dir = Path.Combine(local, FolderName);
                        Directory.CreateDirectory(dir);
                        return dir;
                    }
                }
                catch (Exception)
                {
                    // Sin perfil accesible se cae al directorio del ejecutable.
                }

                return AppDirectory;
            }
        }

        public static string SettingsPath => Path.Combine(DataDirectory, SettingsFileName);

        public static string LogPath => Path.Combine(DataDirectory, LogFileName);

        /// <summary>Ubicacion previa a 1.3.0, junto al ejecutable.</summary>
        public static string LegacySettingsPath => Path.Combine(AppDirectory, SettingsFileName);
    }
}
