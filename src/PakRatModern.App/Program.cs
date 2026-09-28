using System;
using System.IO;
using System.Windows.Forms;
using PakRatModern.Core;

namespace PakRatModern.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            DarkTheme.EnableAppDarkMode();

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log("Unhandled exception", e.ExceptionObject as Exception);
                MessageBox.Show(
                    "PakRat Modern hit an unexpected error and must close.\n\n" +
                    $"Details were written to:\n{AppPaths.LogPath}",
                    "PakRat Modern", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.ThreadException += (s, e) =>
            {
                Log("Thread exception", e.Exception);
                MessageBox.Show(e.Exception.Message, "PakRat Modern",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            try
            {
                var settings = AppSettings.Load();
                var initialBsp = args.Length > 0 ? args[0] : null;

                Log("Startup");
                Application.Run(new MainForm(settings, initialBsp));
                Log("Shutdown");
            }
            catch (Exception ex)
            {
                Log("Startup failed", ex);
                MessageBox.Show(
                    $"PakRat Modern could not start.\n\n{ex.Message}\n\nLog: {AppPaths.LogPath}",
                    "PakRat Modern", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Cada arranque agrega dos lineas: sin tope, el log crecia para siempre.
        private const long MaxLogBytes = 512 * 1024;

        private static void Log(string message, Exception ex = null)
        {
            try
            {
                var lines = new System.Collections.Generic.List<string>
                {
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}",
                };

                while (ex != null)
                {
                    lines.Add($"  {ex.GetType().Name}: {ex.Message}");
                    if (!string.IsNullOrEmpty(ex.StackTrace)) lines.Add(ex.StackTrace);
                    ex = ex.InnerException;
                }

                var path = AppPaths.LogPath;
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxLogBytes)
                {
                    // Se conserva una generacion anterior, como el .bak del BSP.
                    File.Copy(path, path + ".old", true);
                    File.Delete(path);
                }

                File.AppendAllLines(path, lines);
            }
            catch (Exception)
            {
                // Registrar no debe poder tumbar la aplicacion.
            }
        }
    }
}
