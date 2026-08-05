using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PakRatModern.App
{
    /// <summary>
    /// Selector de carpetas con el dialogo moderno de Windows (IFileOpenDialog en
    /// modo carpeta), el mismo que usa el Explorador.
    ///
    /// FolderBrowserDialog de WinForms muestra el arbolito viejo, que ademas no
    /// deja escribir ni pegar una ruta. Con esto elegir el Game Path se ve y se
    /// usa igual que elegir el BSP.
    /// </summary>
    internal static class FolderPicker
    {
        private const uint FosPickFolders = 0x00000020;
        private const uint FosForceFileSystem = 0x00000040;
        private const uint FosNoChangeDir = 0x00000008;
        private const uint FosPathMustExist = 0x00000800;
        private const uint SigdnFileSysPath = 0x80058000;

        private static readonly Guid ShellItemGuid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");

        /// <summary>Devuelve la carpeta elegida, o null si se cancela.</summary>
        public static string Pick(IWin32Window owner, string title, string initialDirectory)
        {
            try
            {
                var selected = PickModern(owner?.Handle ?? IntPtr.Zero, title, initialDirectory);
                return Directory.Exists(selected) ? selected : null;
            }
            catch (Exception)
            {
                // Windows sin el dialogo moderno: se cae al clasico.
                return PickLegacy(owner, title, initialDirectory);
            }
        }

        private static string PickModern(IntPtr owner, string title, string initialDirectory)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogRcw();
            try
            {
                dialog.GetOptions(out var options);
                dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosPathMustExist | FosNoChangeDir);

                if (!string.IsNullOrWhiteSpace(title)) dialog.SetTitle(title);
                dialog.SetOkButtonLabel("Select Folder");

                if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                {
                    var iid = ShellItemGuid;
                    if (SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref iid, out var folder) == 0 && folder != null)
                        dialog.SetFolder(folder);
                }

                if (dialog.Show(owner) != 0) return null;   // cancelado

                dialog.GetResult(out var item);
                if (item == null) return null;

                item.GetDisplayName(SigdnFileSysPath, out var pathPtr);
                if (pathPtr == IntPtr.Zero) return null;

                try { return Marshal.PtrToStringUni(pathPtr); }
                finally { Marshal.FreeCoTaskMem(pathPtr); }
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        private static string PickLegacy(IWin32Window owner, string title, string initialDirectory)
        {
            using (var dialog = new FolderBrowserDialog { Description = title, ShowNewFolderButton = false })
            {
                if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                    dialog.SelectedPath = initialDirectory;

                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                return Directory.Exists(dialog.SelectedPath) ? dialog.SelectedPath : null;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr bindContext, ref Guid riid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRcw { }

        [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr filterSpec);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int place);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int result);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
        }

        [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog : IFileDialog
        {
            void GetResults(out IntPtr items);
            void GetSelectedItems(out IntPtr items);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint sigdnName, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
