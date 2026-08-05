using System;
using System.IO;

namespace PakRatModern.Core
{
    /// <summary>Escritura que no puede dejar el destino a medio escribir.</summary>
    public static class AtomicFile
    {
        /// <summary>
        /// Escribe primero a un temporal del mismo directorio (mismo volumen) y
        /// despues reemplaza el destino. Un corte a mitad de escritura deja el
        /// archivo original intacto en lugar de un BSP truncado.
        /// </summary>
        public static void WriteAllBytes(string path, byte[] bytes)
        {
            var fullPath = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(dir))
                throw new ArgumentException("Cannot resolve target directory.", nameof(path));

            var tmpPath = Path.Combine(dir, Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }

                if (File.Exists(fullPath))
                    File.Replace(tmpPath, fullPath, null);
                else
                    File.Move(tmpPath, fullPath);
            }
            catch
            {
                try
                {
                    if (File.Exists(tmpPath))
                        File.Delete(tmpPath);
                }
                catch
                {
                    // El temporal quedo huerfano; no tiene sentido enmascarar el
                    // error original por esto.
                }
                throw;
            }
        }

        /// <summary>Copia de respaldo antes de sobrescribir un archivo existente.</summary>
        public static void CreateBackup(string path)
        {
            if (File.Exists(path))
                File.Copy(path, path + ".bak", true);
        }
    }
}
