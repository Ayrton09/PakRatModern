param(
    [string]$InitialBsp = ''
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-AppBasePath {
    try {
        $exePath = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
        if (-not [string]::IsNullOrWhiteSpace($exePath)) {
            $exeName = [System.IO.Path]::GetFileName($exePath)
            if ($exeName -notin @('powershell.exe', 'pwsh.exe')) {
                $exeDir = [System.IO.Path]::GetDirectoryName($exePath)
                if (-not [string]::IsNullOrWhiteSpace($exeDir) -and (Test-Path -LiteralPath $exeDir -PathType Container)) {
                    return $exeDir
                }
            }
        }
    } catch { }

    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot) -and (Test-Path -LiteralPath $PSScriptRoot -PathType Container)) {
        return $PSScriptRoot
    }

    return [Environment]::CurrentDirectory
}

$script:AppBasePath = Get-AppBasePath
$script:StartupLogPath = Join-Path $script:AppBasePath 'PakRatModern-startup.log'
$script:AppVersion = '1.2.1'

function Write-StartupLog {
    param(
        [string]$Message,
        [System.Exception]$Exception
    )

    try {
        $dir = [System.IO.Path]::GetDirectoryName($script:StartupLogPath)
        if (-not [string]::IsNullOrWhiteSpace($dir) -and -not (Test-Path -LiteralPath $dir -PathType Container)) {
            [System.IO.Directory]::CreateDirectory($dir) | Out-Null
        }

        $lines = [System.Collections.Generic.List[string]]::new()
        $lines.Add(('[{0}] {1}' -f ([DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss.fff')), $Message))
        if ($Exception) {
            $lines.Add(('Exception: {0}' -f $Exception.Message))
            if ($Exception.StackTrace) { $lines.Add($Exception.StackTrace) }
            if ($Exception.InnerException) {
                $lines.Add(('Inner: {0}' -f $Exception.InnerException.Message))
                if ($Exception.InnerException.StackTrace) { $lines.Add($Exception.InnerException.StackTrace) }
            }
        }
        [System.IO.File]::AppendAllLines($script:StartupLogPath, $lines)
    } catch { }
}

if (-not ('PakRatDarkColorTable' -as [type])) {
    Add-Type -ReferencedAssemblies @('System.Drawing.dll', 'System.Windows.Forms.dll') -TypeDefinition @"
using System.Drawing;
using System.Windows.Forms;

public class PakRatDarkColorTable : ProfessionalColorTable
{
    private static readonly Color Back = Color.FromArgb(40, 43, 48);
    private static readonly Color Panel = Color.FromArgb(45, 48, 54);
    private static readonly Color Border = Color.FromArgb(52, 56, 62);
    private static readonly Color Select = Color.FromArgb(64, 96, 150);
    public override Color MenuStripGradientBegin { get { return Panel; } }
    public override Color MenuStripGradientEnd { get { return Panel; } }
    public override Color ToolStripDropDownBackground { get { return Back; } }
    public override Color ToolStripBorder { get { return Border; } }
    public override Color MenuBorder { get { return Border; } }
    public override Color ImageMarginGradientBegin { get { return Back; } }
    public override Color ImageMarginGradientMiddle { get { return Back; } }
    public override Color ImageMarginGradientEnd { get { return Back; } }
    public override Color ImageMarginRevealedGradientBegin { get { return Back; } }
    public override Color ImageMarginRevealedGradientMiddle { get { return Back; } }
    public override Color ImageMarginRevealedGradientEnd { get { return Back; } }
    public override Color MenuItemSelected { get { return Select; } }
    public override Color MenuItemBorder { get { return Border; } }
    public override Color MenuItemSelectedGradientBegin { get { return Select; } }
    public override Color MenuItemSelectedGradientEnd { get { return Select; } }
    public override Color MenuItemPressedGradientBegin { get { return Back; } }
    public override Color MenuItemPressedGradientMiddle { get { return Back; } }
    public override Color MenuItemPressedGradientEnd { get { return Back; } }
    public override Color ButtonSelectedHighlight { get { return Select; } }
    public override Color ButtonSelectedHighlightBorder { get { return Border; } }
    public override Color ButtonPressedHighlight { get { return Select; } }
    public override Color ButtonPressedHighlightBorder { get { return Border; } }
    public override Color CheckBackground { get { return Select; } }
    public override Color CheckSelectedBackground { get { return Select; } }
    public override Color CheckPressedBackground { get { return Select; } }
    public override Color GripDark { get { return Border; } }
    public override Color GripLight { get { return Border; } }
    public override Color SeparatorDark { get { return Border; } }
    public override Color SeparatorLight { get { return Border; } }
}

public class PakRatDarkRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Panel = Color.FromArgb(45, 48, 54);
    private static readonly Color Select = Color.FromArgb(64, 96, 150);
    private static readonly Color Border = Color.FromArgb(52, 56, 62);

    public PakRatDarkRenderer() : base(new PakRatDarkColorTable())
    {
        this.RoundedEdges = false;
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is MenuStrip)
        {
            return;
        }

        Rectangle r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        using (Pen p = new Pen(Border))
        {
            e.Graphics.DrawRectangle(p, r);
        }
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        Rectangle r = new Rectangle(Point.Empty, e.Item.Size);
        ToolStripMenuItem mi = e.Item as ToolStripMenuItem;
        bool active = e.Item.Selected || (mi != null && mi.DropDown.Visible);
        Color fill = active ? Select : Panel;

        using (SolidBrush b = new SolidBrush(fill))
        {
            e.Graphics.FillRectangle(b, r);
        }

        if (active)
        {
            using (Pen p = new Pen(Border))
            {
                e.Graphics.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
            }
        }
    }
}
"@
}

if (-not ('PakRatDwm' -as [type])) {
    Add-Type -ReferencedAssemblies @('System.dll') -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class PakRatDwm
{
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
"@
}

if (-not ('PakRatNativeTheme' -as [type])) {
    Add-Type -ReferencedAssemblies @('System.dll') -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class PakRatNativeTheme
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    public static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    public static extern int SetPreferredAppMode(int appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    public static extern void FlushMenuThemes();
}
"@
}

if (-not ('PakRatListViewNative' -as [type])) {
    Add-Type -ReferencedAssemblies @('System.dll') -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class PakRatListViewNative
{
    private const int SB_VERT = 1;

    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

    public static void HideVerticalScrollBar(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            ShowScrollBar(handle, SB_VERT, false);
        }
    }
}
"@
}

if (-not ('PakRatCrc32' -as [type])) {
    Add-Type -ReferencedAssemblies @('System.dll') -TypeDefinition @"
using System;

public static class PakRatCrc32
{
    private static readonly uint[] Table = CreateTable();

    private static uint[] CreateTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                crc = ((crc & 1) != 0) ? ((crc >> 1) ^ 0xEDB88320u) : (crc >> 1);
            }
            table[i] = crc;
        }
        return table;
    }

    public static uint Compute(byte[] bytes)
    {
        uint crc = 0xFFFFFFFFu;
        if (bytes != null)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                crc = (crc >> 8) ^ Table[(crc ^ bytes[i]) & 0xFF];
            }
        }
        return crc ^ 0xFFFFFFFFu;
    }
}
"@
}

if (-not ('PakRatWheelPanel' -as [type])) {
    Add-Type -ReferencedAssemblies @('System.dll', 'System.Windows.Forms.dll') -TypeDefinition @"
using System;
using System.Windows.Forms;

public class PakRatWheelPanel : Panel
{
    public PakRatWheelPanel()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    public void ActivateForWheel()
    {
        Control parent = Parent;
        while (parent != null)
        {
            ContainerControl container = parent as ContainerControl;
            if (container != null)
            {
                container.ActiveControl = this;
                break;
            }
            parent = parent.Parent;
        }

        Select();
        Focus();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        ActivateForWheel();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Focused)
        {
            ActivateForWheel();
        }
    }
}
"@
}

if (-not ('PakRatMouseWheelForwarder' -as [type])) {
    Add-Type -ReferencedAssemblies @('System.dll', 'System.Windows.Forms.dll') -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public sealed class PakRatMouseWheelForwarder : IMessageFilter
{
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WH_MOUSE_LL = 14;
    private static readonly Dictionary<IntPtr, IntPtr> TargetByHandle = new Dictionary<IntPtr, IntPtr>();
    private static readonly object Sync = new object();
    private static bool installed;
    private static IntPtr hookHandle = IntPtr.Zero;
    private static readonly LowLevelMouseProc mouseProc = HookCallback;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    public static void Register(Control control)
    {
        Register(control, control);
    }

    public static void Register(Control control, Control wheelTarget)
    {
        if (control == null)
        {
            return;
        }

        if (wheelTarget == null)
        {
            wheelTarget = control;
        }

        EnsureInstalled();
        if (control.IsHandleCreated)
        {
            AddHandle(control.Handle, wheelTarget.IsHandleCreated ? wheelTarget.Handle : control.Handle);
        }

        control.HandleCreated += delegate { AddHandle(control.Handle, wheelTarget.IsHandleCreated ? wheelTarget.Handle : control.Handle); };
        control.HandleDestroyed += delegate { RemoveHandle(control.Handle); };
        wheelTarget.HandleCreated += delegate
        {
            if (control.IsHandleCreated)
            {
                AddHandle(control.Handle, wheelTarget.Handle);
            }
        };
    }

    private static void EnsureInstalled()
    {
        if (installed)
        {
            return;
        }

        Application.AddMessageFilter(new PakRatMouseWheelForwarder());
        InstallHook();
        installed = true;
    }

    private static void InstallHook()
    {
        if (hookHandle != IntPtr.Zero)
        {
            return;
        }

        hookHandle = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, GetModuleHandle(null), 0);
        Application.ApplicationExit += delegate
        {
            if (hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookHandle);
                hookHandle = IntPtr.Zero;
            }
        };
    }

    private static void AddHandle(IntPtr handle, IntPtr targetHandle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        lock (Sync)
        {
            TargetByHandle[handle] = (targetHandle == IntPtr.Zero) ? handle : targetHandle;
        }
    }

    private static void RemoveHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        lock (Sync)
        {
            TargetByHandle.Remove(handle);
        }
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEWHEEL)
        {
            return false;
        }

        POINT point;
        if (!GetCursorPos(out point))
        {
            return false;
        }

        IntPtr target = WindowFromPoint(point);
        if (target == IntPtr.Zero)
        {
            return false;
        }

        lock (Sync)
        {
            foreach (KeyValuePair<IntPtr, IntPtr> pair in TargetByHandle)
            {
                IntPtr handle = pair.Key;
                if (target == handle || IsChild(handle, target))
                {
                    SendMessage(pair.Value, m.Msg, m.WParam, m.LParam);
                    return true;
                }
            }
        }

        return false;
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WM_MOUSEWHEEL)
        {
            MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            IntPtr target = WindowFromPoint(data.pt);
            if (target != IntPtr.Zero)
            {
                lock (Sync)
                {
                    foreach (KeyValuePair<IntPtr, IntPtr> pair in TargetByHandle)
                    {
                        IntPtr handle = pair.Key;
                        if (target == handle || IsChild(handle, target))
                        {
                            int lParamValue = ((data.pt.Y & 0xffff) << 16) | (data.pt.X & 0xffff);
                            IntPtr wheelWParam = new IntPtr(unchecked((int)(data.mouseData & 0xffff0000)));
                            PostMessage(pair.Value, WM_MOUSEWHEEL, wheelWParam, new IntPtr(lParamValue));
                            return new IntPtr(1);
                        }
                    }
                }
            }
        }

        return CallNextHookEx(hookHandle, nCode, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
"@
}

if (-not ('PakRatFolderPicker' -as [type])) {
    Add-Type -ReferencedAssemblies @('System.dll') -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class PakRatFolderPicker
{
    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_NOCHANGEDIR = 0x00000008;
    private const uint FOS_PATHMUSTEXIST = 0x00000800;
    private const uint SIGDN_FILESYSPATH = 0x80058000;
    private static readonly Guid ShellItemGuid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    public static string PickFolder(string title, string initialDirectory, IntPtr owner)
    {
        IFileOpenDialog dialog = (IFileOpenDialog)new FileOpenDialogRCW();
        try
        {
            uint options;
            dialog.GetOptions(out options);
            dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR);

            if (!String.IsNullOrWhiteSpace(title))
            {
                dialog.SetTitle(title);
            }
            dialog.SetOkButtonLabel("Select Folder");

            if (!String.IsNullOrWhiteSpace(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
            {
                IShellItem folder;
                Guid iid = ShellItemGuid;
                int hr = SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref iid, out folder);
                if (hr == 0 && folder != null)
                {
                    dialog.SetFolder(folder);
                }
            }

            int result = dialog.Show(owner);
            if (result != 0)
            {
                return null;
            }

            IShellItem item;
            dialog.GetResult(out item);
            if (item == null)
            {
                return null;
            }

            IntPtr pathPtr;
            item.GetDisplayName(SIGDN_FILESYSPATH, out pathPtr);
            if (pathPtr == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(pathPtr);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pathPtr);
            }
        }
        finally
        {
            if (dialog != null)
            {
                Marshal.ReleaseComObject(dialog);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        out IShellItem ppv);

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRCW
    {
    }

    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    [ComImport]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog : IFileDialog
    {
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }
}
"@
}

if (-not ('PakRatVpk' -as [type])) {
    Add-Type -ReferencedAssemblies @('System.dll') -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class PakRatVpk
{
    private const uint Signature = 0x55aa1234;

    private sealed class NeedIndex
    {
        public readonly HashSet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, HashSet<string>> DirectoriesByExtension = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    }

    public static string[] FindNeededEntries(string dirVpkPath, string[] neededRefs)
    {
        var matches = new List<string>();
        if (String.IsNullOrWhiteSpace(dirVpkPath) || neededRefs == null || neededRefs.Length == 0 || !File.Exists(dirVpkPath))
        {
            return matches.ToArray();
        }

        NeedIndex index = BuildNeedIndex(neededRefs);
        if (index.All.Count == 0)
        {
            return matches.ToArray();
        }

        using (FileStream fs = File.OpenRead(dirVpkPath))
        using (BinaryReader br = new BinaryReader(fs, Encoding.ASCII))
        {
            long limit = fs.Length;
            if (fs.Length >= 12)
            {
                uint signature = br.ReadUInt32();
                if (signature == Signature)
                {
                    uint version = br.ReadUInt32();
                    uint treeSize = br.ReadUInt32();
                    if (version == 2)
                    {
                        if (fs.Length < 28)
                        {
                            return matches.ToArray();
                        }
                        br.ReadUInt32();
                        br.ReadUInt32();
                        br.ReadUInt32();
                        br.ReadUInt32();
                    }
                    else if (version != 1)
                    {
                        return matches.ToArray();
                    }

                    limit = Math.Min(fs.Length, fs.Position + (long)treeSize);
                }
                else
                {
                    fs.Position = 0;
                }
            }

            while (fs.Position < limit)
            {
                string rawExtension = ReadCString(br, limit);
                if (rawExtension.Length == 0)
                {
                    break;
                }
                string extension = NormalizeVpkPart(rawExtension);

                bool extensionNeeded = index.DirectoriesByExtension.ContainsKey(extension);

                while (fs.Position < limit)
                {
                    string rawDirectory = ReadCString(br, limit);
                    if (rawDirectory.Length == 0)
                    {
                        break;
                    }
                    string directory = NormalizeVpkPart(rawDirectory);

                    HashSet<string> neededDirectories = null;
                    bool directoryNeeded = extensionNeeded &&
                        index.DirectoriesByExtension.TryGetValue(extension, out neededDirectories) &&
                        neededDirectories.Contains(directory);

                    while (fs.Position < limit)
                    {
                        string rawFileName = ReadCString(br, limit);
                        if (rawFileName.Length == 0)
                        {
                            break;
                        }
                        string fileName = NormalizeVpkPart(rawFileName);
                        if ((limit - fs.Position) < 18)
                        {
                            return matches.ToArray();
                        }

                        br.ReadUInt32();
                        ushort preloadBytes = br.ReadUInt16();
                        br.ReadUInt16();
                        br.ReadUInt32();
                        br.ReadUInt32();
                        ushort terminator = br.ReadUInt16();
                        if (terminator != 0xffff)
                        {
                            return matches.ToArray();
                        }

                        if (directoryNeeded)
                        {
                            string path = ComposePath(extension, directory, fileName);
                            if (path.Length > 0 && index.All.Contains(path))
                            {
                                matches.Add(path);
                                if (matches.Count >= index.All.Count)
                                {
                                    return matches.ToArray();
                                }
                            }
                        }

                        if (preloadBytes > 0)
                        {
                            long next = fs.Position + preloadBytes;
                            if (next > limit)
                            {
                                return matches.ToArray();
                            }
                            fs.Position = next;
                        }
                    }
                }
            }
        }

        return matches.ToArray();
    }

    private static NeedIndex BuildNeedIndex(string[] neededRefs)
    {
        var index = new NeedIndex();
        foreach (string raw in neededRefs)
        {
            string normalized = NormalizeArchivePath(raw);
            if (normalized.Length == 0 || !index.All.Add(normalized))
            {
                continue;
            }

            string extension;
            string directory;
            SplitPath(normalized, out extension, out directory);

            HashSet<string> dirs;
            if (!index.DirectoriesByExtension.TryGetValue(extension, out dirs))
            {
                dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                index.DirectoriesByExtension.Add(extension, dirs);
            }
            dirs.Add(directory);
        }
        return index;
    }

    private static string ReadCString(BinaryReader br, long limit)
    {
        var bytes = new List<byte>(64);
        while (br.BaseStream.Position < limit)
        {
            byte b = br.ReadByte();
            if (b == 0)
            {
                break;
            }
            bytes.Add(b);
        }
        if (bytes.Count == 0)
        {
            return String.Empty;
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static string NormalizeVpkPart(string value)
    {
        if (String.IsNullOrWhiteSpace(value) || value == " ")
        {
            return String.Empty;
        }
        return value.Replace('\\', '/').Trim('/');
    }

    private static string ComposePath(string extension, string directory, string fileName)
    {
        if (String.IsNullOrWhiteSpace(fileName))
        {
            return String.Empty;
        }

        string leaf = String.IsNullOrWhiteSpace(extension) ? fileName : fileName + "." + extension;
        string path = String.IsNullOrWhiteSpace(directory) ? leaf : directory + "/" + leaf;
        return NormalizeArchivePath(path);
    }

    private static void SplitPath(string path, out string extension, out string directory)
    {
        int slash = path.LastIndexOf('/');
        int dot = path.LastIndexOf('.');
        extension = (dot > slash) ? path.Substring(dot + 1) : String.Empty;
        directory = (slash >= 0) ? path.Substring(0, slash) : String.Empty;
    }

    private static string NormalizeArchivePath(string path)
    {
        if (String.IsNullOrWhiteSpace(path))
        {
            return String.Empty;
        }

        string p = path.Replace('\\', '/').Trim().Trim('/');
        if (p.Length == 0 || p.IndexOf(':') >= 0)
        {
            return String.Empty;
        }

        string[] parts = p.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (String.IsNullOrWhiteSpace(part) || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" "))
            {
                return String.Empty;
            }
            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return String.Empty;
            }
        }

        return p;
    }
}
"@
}

$LumpCount = 64
$PakLumpIndex = 40
$GameLumpIndex = 35
$EntitiesLumpIndex = 0
$TexDataStringDataLumpIndex = 43
$TexDataStringTableLumpIndex = 44
$StaticPropGameLumpId = 1936749168
$HeaderSize = 4 + 4 + ($LumpCount * 16) + 4
$MaxBspBytes = [int64](1024MB)
$MaxPakEntryBytes = [int64](512MB)
$MaxPakTotalBytes = [int64](1536MB)
$MaxPakEntries = 20000

$script:SettingsPath = Join-Path $script:AppBasePath 'pakrat_modern_gui.settings.json'
$script:MainForm = $null
$script:StatusLabel = $null
$script:PathBox = $null
$script:GameRootBox = $null
$script:AppIconPath = Join-Path $script:AppBasePath 'pakrat_modern.ico'
$script:ScanSummaryMissingLabel = $null
$script:ScanSummaryCanAddLabel = $null
$script:ScanSummaryNotFoundLabel = $null
$script:ScanSummaryInPakLabel = $null
$script:ListView = $null
$script:ListBodyPanel = $null
$script:ListRowsPanel = $null
$script:ListHeaderPanel = $null
$script:ListHeaderButtons = @()
$script:ListScrollBar = $null
$script:ListScrollTrack = $null
$script:ListScrollThumb = $null
$script:ListScrollDragging = $false
$script:ListScrollDragOffsetY = 0
$script:TreeView = $null
$script:DarkMenuRenderer = $null
$script:Theme = [ordered]@{
    Back = [System.Drawing.Color]::FromArgb(32, 34, 37)
    Panel = [System.Drawing.Color]::FromArgb(40, 43, 48)
    Input = [System.Drawing.Color]::FromArgb(28, 30, 33)
    Border = [System.Drawing.Color]::FromArgb(52, 56, 62)
    Text = [System.Drawing.Color]::FromArgb(230, 233, 239)
    MutedText = [System.Drawing.Color]::FromArgb(180, 186, 198)
    Accent = [System.Drawing.Color]::FromArgb(72, 133, 237)
    ModifiedText = [System.Drawing.Color]::FromArgb(138, 198, 255)
    AccentText = [System.Drawing.Color]::FromArgb(244, 247, 255)
    Success = [System.Drawing.Color]::FromArgb(76, 175, 80)
    Error = [System.Drawing.Color]::FromArgb(244, 67, 54)
    RowAlt = [System.Drawing.Color]::FromArgb(36, 38, 43)
    Selection = [System.Drawing.Color]::FromArgb(64, 96, 150)
    Header = [System.Drawing.Color]::FromArgb(45, 48, 54)
    ScrollTrack = [System.Drawing.Color]::FromArgb(36, 39, 44)
    ScrollThumb = [System.Drawing.Color]::FromArgb(96, 104, 116)
}

$script:State = [ordered]@{
    CurrentBspPath = $null
    BspRaw = $null
    BspVersion = 0
    MapRevision = 0
    Lumps = @()
    Entries = [ordered]@{}
    IsDirty = $false
    GameRoot = ''
    SavedGameRoots = @()
    PathFixupMode = 'Ask'
    IncludeExtrasInScan = $true
    BackupBeforeInPlaceSave = $true
    ViewAsTree = $false
    SortColumn = 1
    SortDescending = $false
    ScanMissingTotal = 0
    ScanCanAdd = 0
    ScanNotFound = 0
    ScanAlreadyInPak = 0
}
$script:BaseVpkCache = [ordered]@{
    GameRoot = ''
    NeededKey = ''
    Matches = $null
}
$script:VmtDependencyCache = @{}
$script:ScanInProgress = $false
$script:LastUiPump = [DateTime]::MinValue

function Show-DarkDialog {
    param(
        [System.Windows.Forms.Form]$Dialog,
        [System.Windows.Forms.IWin32Window]$Owner = $script:MainForm
    )

    if ($null -eq $Dialog) { return [System.Windows.Forms.DialogResult]::None }
    $Dialog.ShowInTaskbar = $false
    $Dialog.Add_HandleCreated({ Enable-DarkTitleBar -Form $Dialog })
    Apply-DarkThemeRecursive -Control $Dialog
    Enable-DarkTitleBar -Form $Dialog
    if ($Owner) { return $Dialog.ShowDialog($Owner) }
    return $Dialog.ShowDialog()
}

function Show-DarkMessageDialog {
    param(
        [string]$Message,
        [string]$Title = 'PakRat Modern',
        [string[]]$Buttons = @('OK'),
        [string]$DefaultButton = 'OK'
    )

    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = $Title
    $dlg.StartPosition = 'CenterParent'
    $dlg.Width = 520
    $dlg.Height = 190
    $dlg.MinimumSize = New-Object System.Drawing.Size(420, 170)
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.MaximizeBox = $false
    $dlg.MinimizeBox = $false

    $text = New-Object System.Windows.Forms.Label
    $text.Left = 18
    $text.Top = 18
    $text.Width = 470
    $text.Height = 78
    $text.AutoEllipsis = $true
    $text.Text = $Message
    $dlg.Controls.Add($text)

    $panel = New-Object System.Windows.Forms.Panel
    $panel.Dock = 'Bottom'
    $panel.Height = 56
    $dlg.Controls.Add($panel)

    $buttonWidth = 92
    $gap = 10
    $totalWidth = ($Buttons.Count * $buttonWidth) + ([Math]::Max(0, $Buttons.Count - 1) * $gap)
    $left = [Math]::Max(12, $dlg.ClientSize.Width - $totalWidth - 18)

    foreach ($buttonText in $Buttons) {
        $btn = New-Object System.Windows.Forms.Button
        $btn.Text = $buttonText
        $btn.Width = $buttonWidth
        $btn.Height = 28
        $btn.Left = $left
        $btn.Top = 14
        $btn.Tag = $buttonText
        $btn.Add_Click({
            $dlg.Tag = [string]$this.Tag
            $dlg.Close()
        })
        $panel.Controls.Add($btn)
        if ($buttonText -eq $DefaultButton) { $dlg.AcceptButton = $btn }
        if ($buttonText -eq 'Cancel' -or $buttonText -eq 'No') { $dlg.CancelButton = $btn }
        $left += $buttonWidth + $gap
    }

    [void](Show-DarkDialog -Dialog $dlg)
    if ([string]::IsNullOrWhiteSpace([string]$dlg.Tag)) { return 'Cancel' }
    return [string]$dlg.Tag
}

function Show-ErrorDialog {
    param([string]$Message)
    [void](Show-DarkMessageDialog -Message $Message -Buttons @('OK') -DefaultButton 'OK')
}

function Show-InfoDialog {
    param([string]$Message)
    [void](Show-DarkMessageDialog -Message $Message -Buttons @('OK') -DefaultButton 'OK')
}

function Confirm-Dialog {
    param([string]$Message)
    $result = Show-DarkMessageDialog -Message $Message -Buttons @('Yes', 'No') -DefaultButton 'No'
    return $result -eq 'Yes'
}

function Update-Status {
    param([string]$Message)
    if ($script:StatusLabel) {
        $script:StatusLabel.Text = $Message
    }
}

function Pump-UiMessages {
    param([int]$MinMilliseconds = 200)
    if (-not $script:MainForm) { return }

    $now = [DateTime]::UtcNow
    if (($now - $script:LastUiPump).TotalMilliseconds -lt $MinMilliseconds) { return }
    $script:LastUiPump = $now
    try { [System.Windows.Forms.Application]::DoEvents() } catch { }
}

function Set-ScanBusy {
    param([bool]$Busy, [string]$Message = '')
    $script:ScanInProgress = $Busy
    if (-not [string]::IsNullOrWhiteSpace($Message)) { Update-Status $Message }
    if ($script:MainForm) {
        $script:MainForm.UseWaitCursor = $Busy
        [System.Windows.Forms.Cursor]::Current = if ($Busy) { [System.Windows.Forms.Cursors]::WaitCursor } else { [System.Windows.Forms.Cursors]::Default }
        $script:MainForm.Refresh()
    }
    Pump-UiMessages -MinMilliseconds 0
}

function Set-ScanSummary {
    param(
        [int]$MissingTotal,
        [int]$CanAdd,
        [int]$NotFound,
        [int]$AlreadyInPak
    )
    $script:State.ScanMissingTotal = [Math]::Max(0, $MissingTotal)
    $script:State.ScanCanAdd = [Math]::Max(0, $CanAdd)
    $script:State.ScanNotFound = [Math]::Max(0, $NotFound)
    $script:State.ScanAlreadyInPak = [Math]::Max(0, $AlreadyInPak)
}

function Refresh-ScanSummaryUI {
    if ($script:ScanSummaryMissingLabel) {
        $script:ScanSummaryMissingLabel.Text = "Missing in BSP: $($script:State.ScanMissingTotal)"
    }
    if ($script:ScanSummaryCanAddLabel) {
        $script:ScanSummaryCanAddLabel.Text = "Can add: $($script:State.ScanCanAdd)"
    }
    if ($script:ScanSummaryNotFoundLabel) {
        $script:ScanSummaryNotFoundLabel.Text = "Not found: $($script:State.ScanNotFound)"
    }
    if ($script:ScanSummaryInPakLabel) {
        $script:ScanSummaryInPakLabel.Text = "Already in PAK: $($script:State.ScanAlreadyInPak)"
    }
}

function Set-ControlDoubleBuffered {
    param([System.Windows.Forms.Control]$Control)
    if ($null -eq $Control) { return }
    try {
        $doubleBufferedProp = [System.Windows.Forms.Control].GetProperty(
            'DoubleBuffered',
            [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic
        )
        if ($doubleBufferedProp) {
            $doubleBufferedProp.SetValue($Control, $true, $null)
        }
    } catch { }
}

function Enable-DarkListBoxRendering {
    param([System.Windows.Forms.ListBox]$ListBox)
    if ($null -eq $ListBox) { return }
    if ($ListBox.DrawMode -eq [System.Windows.Forms.DrawMode]::OwnerDrawFixed) { return }
    $ListBox.DrawMode = [System.Windows.Forms.DrawMode]::OwnerDrawFixed
    if ($ListBox.ItemHeight -lt 18) { $ListBox.ItemHeight = 18 }
    $ListBox.Add_DrawItem({
        param($sender, $e)
        if ($e.Index -lt 0) { return }
        $selected = (($e.State -band [System.Windows.Forms.DrawItemState]::Selected) -ne 0)
        $back = if ($selected) { $script:Theme.Selection } else { $script:Theme.Input }
        $fore = if ($selected) { $script:Theme.AccentText } else { $script:Theme.Text }
        $backBrush = New-Object System.Drawing.SolidBrush($back)
        $foreBrush = New-Object System.Drawing.SolidBrush($fore)
        try {
            $e.Graphics.FillRectangle($backBrush, $e.Bounds)
            $text = [string]$sender.Items[$e.Index]
            $rect = New-Object System.Drawing.RectangleF(($e.Bounds.X + 4), ($e.Bounds.Y + 1), ($e.Bounds.Width - 8), ($e.Bounds.Height - 2))
            $e.Graphics.DrawString($text, $sender.Font, $foreBrush, $rect)
        } finally {
            $backBrush.Dispose()
            $foreBrush.Dispose()
        }
    })
}

function Enable-DarkComboBoxRendering {
    param([System.Windows.Forms.ComboBox]$ComboBox)
    if ($null -eq $ComboBox) { return }
    if ($ComboBox.DrawMode -eq [System.Windows.Forms.DrawMode]::OwnerDrawFixed) { return }
    $ComboBox.DrawMode = [System.Windows.Forms.DrawMode]::OwnerDrawFixed
    $ComboBox.Add_DrawItem({
        param($sender, $e)
        if ($e.Index -lt 0) { return }
        $selected = (($e.State -band [System.Windows.Forms.DrawItemState]::Selected) -ne 0)
        $back = if ($selected) { $script:Theme.Selection } else { $script:Theme.Input }
        $fore = if ($selected) { $script:Theme.AccentText } else { $script:Theme.Text }
        $backBrush = New-Object System.Drawing.SolidBrush($back)
        $foreBrush = New-Object System.Drawing.SolidBrush($fore)
        try {
            $e.Graphics.FillRectangle($backBrush, $e.Bounds)
            $text = [string]$sender.Items[$e.Index]
            $rect = New-Object System.Drawing.RectangleF(($e.Bounds.X + 4), ($e.Bounds.Y + 1), ($e.Bounds.Width - 8), ($e.Bounds.Height - 2))
            $e.Graphics.DrawString($text, $sender.Font, $foreBrush, $rect)
        } finally {
            $backBrush.Dispose()
            $foreBrush.Dispose()
        }
    })
}

function Apply-DarkThemeRecursive {
    param([System.Windows.Forms.Control]$Control)

    if ($null -eq $Control) { return }

    if ($Control -is [System.Windows.Forms.Form]) {
        Set-ControlDoubleBuffered -Control $Control
        $Control.BackColor = $script:Theme.Back
        $Control.ForeColor = $script:Theme.Text
    }
    elseif ($Control -is [System.Windows.Forms.Panel]) {
        Set-ControlDoubleBuffered -Control $Control
        $Control.BackColor = $script:Theme.Panel
        $Control.ForeColor = $script:Theme.Text
    }
    elseif ($Control -is [System.Windows.Forms.TextBox]) {
        $Control.BackColor = $script:Theme.Input
        $Control.ForeColor = $script:Theme.Text
        $Control.BorderStyle = 'None'
        Enable-NativeDarkControlTheme -Control $Control
    }
    elseif ($Control -is [System.Windows.Forms.ComboBox]) {
        $Control.BackColor = $script:Theme.Input
        $Control.ForeColor = $script:Theme.Text
        $Control.FlatStyle = 'Flat'
        Enable-DarkComboBoxRendering -ComboBox $Control
    }
    elseif ($Control -is [System.Windows.Forms.ListBox]) {
        $Control.BackColor = $script:Theme.Input
        $Control.ForeColor = $script:Theme.Text
        $Control.BorderStyle = 'FixedSingle'
        Enable-DarkListBoxRendering -ListBox $Control
        Enable-NativeDarkControlTheme -Control $Control
    }
    elseif ($Control -is [System.Windows.Forms.ListView]) {
        Set-ControlDoubleBuffered -Control $Control
        $Control.BackColor = $script:Theme.Input
        $Control.ForeColor = $script:Theme.Text
        $Control.BorderStyle = 'None'
        Enable-NativeDarkControlTheme -Control $Control
    }
    elseif ($Control -is [System.Windows.Forms.TreeView]) {
        Set-ControlDoubleBuffered -Control $Control
        $Control.BackColor = $script:Theme.Input
        $Control.ForeColor = $script:Theme.Text
        $Control.BorderStyle = 'None'
        Enable-NativeDarkControlTheme -Control $Control
    }
    elseif ($Control -is [System.Windows.Forms.Button]) {
        $Control.UseVisualStyleBackColor = $false
        $Control.BackColor = $script:Theme.Panel
        $Control.ForeColor = $script:Theme.Text
        $Control.FlatStyle = 'Flat'
        $Control.FlatAppearance.BorderColor = $script:Theme.Border
        $Control.FlatAppearance.MouseDownBackColor = $script:Theme.Selection
        $Control.FlatAppearance.MouseOverBackColor = $script:Theme.Header
    }
    elseif ($Control -is [System.Windows.Forms.CheckBox]) {
        $Control.UseVisualStyleBackColor = $false
        $Control.BackColor = [System.Drawing.Color]::Transparent
        $Control.ForeColor = $script:Theme.Text
        $Control.FlatStyle = 'Flat'
    }
    elseif ($Control -is [System.Windows.Forms.Label]) {
        $Control.BackColor = [System.Drawing.Color]::Transparent
        $Control.ForeColor = $script:Theme.Text
    }

    foreach ($child in $Control.Controls) {
        Apply-DarkThemeRecursive -Control $child
    }
}

function Apply-DarkThemeToToolStrips {
    param(
        [System.Windows.Forms.MenuStrip]$MenuStrip,
        [System.Windows.Forms.StatusStrip]$StatusStrip
    )

    if ($MenuStrip) {
        if (-not $script:DarkMenuRenderer) {
            $script:DarkMenuRenderer = New-Object PakRatDarkRenderer
        }
        $renderer = $script:DarkMenuRenderer
        $MenuStrip.RenderMode = [System.Windows.Forms.ToolStripRenderMode]::Professional
        $MenuStrip.Renderer = $renderer
        $MenuStrip.BackColor = $script:Theme.Header
        $MenuStrip.ForeColor = $script:Theme.Text
        foreach ($rootItem in $MenuStrip.Items) {
            if ($rootItem -is [System.Windows.Forms.ToolStripMenuItem]) {
                $queue = New-Object System.Collections.Generic.Queue[System.Windows.Forms.ToolStripMenuItem]
                $queue.Enqueue($rootItem)
                while ($queue.Count -gt 0) {
                    $mi = $queue.Dequeue()
                    if ($mi.DropDown) {
                        $mi.DropDown.RenderMode = [System.Windows.Forms.ToolStripRenderMode]::Professional
                        $mi.DropDown.Renderer = $renderer
                        $mi.DropDown.ShowImageMargin = $false
                        $mi.DropDown.ShowCheckMargin = $false
                        $mi.DropDown.DropShadowEnabled = $false
                        $mi.DropDown.BackColor = $script:Theme.Panel
                        $mi.DropDown.ForeColor = $script:Theme.Text
                    }
                    foreach ($child in $mi.DropDownItems) {
                        if ($child -is [System.Windows.Forms.ToolStripMenuItem]) {
                            $child.BackColor = $script:Theme.Panel
                            $child.ForeColor = $script:Theme.Text
                            $queue.Enqueue($child)
                        }
                    }
                }
            }
        }
    }
    if ($StatusStrip) {
        $StatusStrip.BackColor = $script:Theme.Header
        $StatusStrip.ForeColor = $script:Theme.Text
    }
}

function Apply-DarkThemeToContextMenu {
    param([System.Windows.Forms.ContextMenuStrip]$Menu)
    if (-not $Menu) { return }

    if (-not $script:DarkMenuRenderer) {
        $script:DarkMenuRenderer = New-Object PakRatDarkRenderer
    }
    $renderer = $script:DarkMenuRenderer
    $Menu.RenderMode = [System.Windows.Forms.ToolStripRenderMode]::Professional
    $Menu.Renderer = $renderer
    $Menu.ShowImageMargin = $false
    $Menu.ShowCheckMargin = $false
    $Menu.DropShadowEnabled = $false
    $Menu.BackColor = $script:Theme.Panel
    $Menu.ForeColor = $script:Theme.Text

    foreach ($item in $Menu.Items) {
        if ($item -is [System.Windows.Forms.ToolStripMenuItem]) {
            $item.BackColor = $script:Theme.Panel
            $item.ForeColor = $script:Theme.Text
        }
    }
}

function Enable-NativeDarkAppMode {
    try {
        [void][PakRatNativeTheme]::SetPreferredAppMode(2)
        [PakRatNativeTheme]::FlushMenuThemes()
    } catch { }
}

function Enable-DarkTitleBar {
    param([System.Windows.Forms.Form]$Form)
    if ($null -eq $Form) { return }

    try {
        $enabled = 1
        foreach ($attribute in @(20, 19)) {
            [void][PakRatDwm]::DwmSetWindowAttribute($Form.Handle, $attribute, [ref]$enabled, 4)
        }
    } catch { }
}

function Enable-NativeDarkControlTheme {
    param([System.Windows.Forms.Control]$Control)
    if ($null -eq $Control) { return }

    $apply = {
        param($target)
        try {
            if ($target -and $target.Handle -ne [IntPtr]::Zero) {
                [void][PakRatNativeTheme]::SetWindowTheme($target.Handle, 'DarkMode_Explorer', $null)
            }
        } catch { }
    }

    & $apply $Control
    $Control.Add_HandleCreated({
        try {
            [void][PakRatNativeTheme]::SetWindowTheme($this.Handle, 'DarkMode_Explorer', $null)
        } catch { }
    })
}

function Hide-NativeListViewScrollBars {
    param([System.Windows.Forms.ListView]$ListView)
    if ($null -eq $ListView -or -not $ListView.IsHandleCreated) { return }
    try {
        [PakRatListViewNative]::HideVerticalScrollBar($ListView.Handle)
    } catch { }
}

function Enable-DarkListViewRendering {
    param(
        [System.Windows.Forms.ListView]$ListView,
        [bool]$UseOwnerDraw = $true
    )
    if ($null -eq $ListView) { return }

    try {
        $doubleBufferedProp = [System.Windows.Forms.Control].GetProperty(
            'DoubleBuffered',
            [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic
        )
        if ($doubleBufferedProp) {
            $doubleBufferedProp.SetValue($ListView, $true, $null)
        }
    } catch { }

    $ListView.HoverSelection = $false
    $ListView.HideSelection = $false
    $ListView.BackColor = $script:Theme.Input
    $ListView.ForeColor = $script:Theme.Text
    Enable-NativeDarkControlTheme -Control $ListView

    if (-not $UseOwnerDraw) {
        $ListView.OwnerDraw = $false
        return
    }

    $ListView.OwnerDraw = $true
    $ListView.Add_DrawColumnHeader({
        $bg = New-Object System.Drawing.SolidBrush($script:Theme.Header)
        $fg = New-Object System.Drawing.SolidBrush($script:Theme.Text)
        $pen = New-Object System.Drawing.Pen($script:Theme.Border)
        try {
            $_.Graphics.FillRectangle($bg, $_.Bounds)
            [System.Windows.Forms.TextRenderer]::DrawText(
                $_.Graphics,
                $_.Header.Text,
                $_.Font,
                $_.Bounds,
                $script:Theme.Text,
                [System.Windows.Forms.TextFormatFlags]::Left -bor [System.Windows.Forms.TextFormatFlags]::VerticalCenter -bor [System.Windows.Forms.TextFormatFlags]::EndEllipsis
            )
            $_.Graphics.DrawLine($pen, $_.Bounds.Left, $_.Bounds.Bottom - 1, $_.Bounds.Right, $_.Bounds.Bottom - 1)
            $_.Graphics.DrawLine($pen, $_.Bounds.Right - 1, $_.Bounds.Top, $_.Bounds.Right - 1, $_.Bounds.Bottom)
        } finally {
            $bg.Dispose()
            $fg.Dispose()
            $pen.Dispose()
        }
    })

    $ListView.Add_DrawItem({
        if ($ListView.View -ne [System.Windows.Forms.View]::Details) {
            $_.DrawDefault = $true
            return
        }

        $isSelected = $_.Item.Selected
        $rowColor = if ($isSelected) { $script:Theme.Selection } elseif (($_.ItemIndex % 2) -eq 0) { $script:Theme.Input } else { $script:Theme.RowAlt }
        $fgColor = if ($isSelected) { $script:Theme.AccentText } else { $_.Item.ForeColor }
        if ($fgColor -eq [System.Drawing.Color]::Empty) { $fgColor = $script:Theme.Text }

        $firstColumnWidth = if ($ListView.Columns.Count -gt 0) { $ListView.Columns[0].Width } else { $_.Bounds.Width }
        $cellBounds = New-Object System.Drawing.Rectangle($_.Bounds.Left, $_.Bounds.Top, [Math]::Min($_.Bounds.Width, $firstColumnWidth), $_.Bounds.Height)
        $bg = New-Object System.Drawing.SolidBrush($rowColor)
        $pen = New-Object System.Drawing.Pen($script:Theme.Border)
        try {
            $_.Graphics.FillRectangle($bg, $cellBounds)
            $textRect = New-Object System.Drawing.Rectangle($cellBounds.Left + 4, $cellBounds.Top, [Math]::Max(0, $cellBounds.Width - 6), $cellBounds.Height)
            [System.Windows.Forms.TextRenderer]::DrawText(
                $_.Graphics,
                $_.Item.Text,
                $_.Item.Font,
                $textRect,
                $fgColor,
                [System.Windows.Forms.TextFormatFlags]::Left -bor [System.Windows.Forms.TextFormatFlags]::VerticalCenter -bor [System.Windows.Forms.TextFormatFlags]::EndEllipsis
            )
            $_.Graphics.DrawLine($pen, $cellBounds.Left, $cellBounds.Bottom - 1, $cellBounds.Right, $cellBounds.Bottom - 1)
            $_.Graphics.DrawLine($pen, $cellBounds.Right - 1, $cellBounds.Top, $cellBounds.Right - 1, $cellBounds.Bottom)
        } finally {
            $bg.Dispose()
            $pen.Dispose()
        }
    })

    $ListView.Add_DrawSubItem({
        $isSelected = $_.Item.Selected
        $rowColor = if ($isSelected) { $script:Theme.Selection } elseif (($_.ItemIndex % 2) -eq 0) { $script:Theme.Input } else { $script:Theme.RowAlt }
        $fgColor = if ($isSelected) { $script:Theme.AccentText } else { $_.Item.ForeColor }
        if ($fgColor -eq [System.Drawing.Color]::Empty) { $fgColor = $script:Theme.Text }

        $bg = New-Object System.Drawing.SolidBrush($rowColor)
        $pen = New-Object System.Drawing.Pen($script:Theme.Border)
        try {
            $_.Graphics.FillRectangle($bg, $_.Bounds)
            $textRect = New-Object System.Drawing.Rectangle($_.Bounds.Left + 4, $_.Bounds.Top, [Math]::Max(0, $_.Bounds.Width - 6), $_.Bounds.Height)
            [System.Windows.Forms.TextRenderer]::DrawText(
                $_.Graphics,
                $_.SubItem.Text,
                $_.SubItem.Font,
                $textRect,
                $fgColor,
                [System.Windows.Forms.TextFormatFlags]::Left -bor [System.Windows.Forms.TextFormatFlags]::VerticalCenter -bor [System.Windows.Forms.TextFormatFlags]::EndEllipsis
            )
            $_.Graphics.DrawLine($pen, $_.Bounds.Left, $_.Bounds.Bottom - 1, $_.Bounds.Right, $_.Bounds.Bottom - 1)
            $_.Graphics.DrawLine($pen, $_.Bounds.Right - 1, $_.Bounds.Top, $_.Bounds.Right - 1, $_.Bounds.Bottom)

            if ($isSelected -and $_.ColumnIndex -eq 0) {
                $_.DrawFocusRectangle($_.Bounds)
            }
        } finally {
            $bg.Dispose()
            $pen.Dispose()
        }
    })
}

function Get-ListViewRowHeight {
    if ($null -eq $script:ListView -or $script:ListView.Items.Count -eq 0) { return 18 }
    try {
        $rect = $script:ListView.GetItemRect(0)
        if ($rect.Height -gt 0) { return $rect.Height }
    } catch { }
    return 18
}

function Get-ListViewVisibleRowCount {
    if ($null -eq $script:ListView) { return 1 }
    $rowHeight = Get-ListViewRowHeight
    return [Math]::Max(1, [int][Math]::Floor($script:ListView.ClientSize.Height / [double]$rowHeight))
}

function Get-ListViewTopIndex {
    if ($null -eq $script:ListView -or $script:ListView.Items.Count -eq 0) { return 0 }
    try {
        if ($script:ListView.TopItem) { return [int]$script:ListView.TopItem.Index }
    } catch { }
    return 0
}

function Scroll-ListViewToTopIndex {
    param([int]$Index)
    if ($null -eq $script:ListView -or $script:ListView.Items.Count -eq 0) { return }

    $visibleRows = Get-ListViewVisibleRowCount
    $maxTop = [Math]::Max(0, $script:ListView.Items.Count - $visibleRows)
    $target = [Math]::Max(0, [Math]::Min($Index, $maxTop))
    try {
        $script:ListView.TopItem = $script:ListView.Items[$target]
    } catch {
        try { $script:ListView.EnsureVisible($target) } catch { }
    }
    Hide-NativeListViewScrollBars -ListView $script:ListView
    Update-ListScrollBar
}

function Update-ListScrollBar {
    if ($null -eq $script:ListView -or $null -eq $script:ListScrollBar -or $null -eq $script:ListScrollTrack -or $null -eq $script:ListScrollThumb) { return }

    $script:ListScrollBar.BackColor = $script:Theme.Panel
    $script:ListScrollTrack.BackColor = $script:Theme.ScrollTrack
    $script:ListScrollThumb.BackColor = $script:Theme.ScrollThumb

    $total = [int]$script:ListView.Items.Count
    $visibleRows = Get-ListViewVisibleRowCount
    $needsScroll = ($total -gt $visibleRows)
    $script:ListScrollBar.Visible = $needsScroll
    if (-not $needsScroll) { return }

    $trackHeight = [Math]::Max(1, $script:ListScrollTrack.ClientSize.Height)
    $thumbHeight = [Math]::Max(44, [int][Math]::Floor($trackHeight * ($visibleRows / [double]$total)))
    $thumbHeight = [Math]::Min($trackHeight, $thumbHeight)
    $range = [Math]::Max(0, $trackHeight - $thumbHeight)
    $maxTop = [Math]::Max(1, $total - $visibleRows)
    $topIndex = [Math]::Max(0, [Math]::Min((Get-ListViewTopIndex), $maxTop))
    $thumbTop = if ($range -gt 0) { [int][Math]::Round($range * ($topIndex / [double]$maxTop)) } else { 0 }

    $script:ListScrollThumb.Left = 3
    $script:ListScrollThumb.Width = [Math]::Max(10, $script:ListScrollTrack.ClientSize.Width - 6)
    $script:ListScrollThumb.Height = $thumbHeight
    $script:ListScrollThumb.Top = $thumbTop
}

function Scroll-ListViewFromThumbY {
    param([int]$ThumbY)
    if ($null -eq $script:ListView -or $null -eq $script:ListScrollTrack -or $null -eq $script:ListScrollThumb) { return }
    if ($script:ListView.Items.Count -eq 0) { return }

    $visibleRows = Get-ListViewVisibleRowCount
    $maxTop = [Math]::Max(0, $script:ListView.Items.Count - $visibleRows)
    $range = [Math]::Max(1, $script:ListScrollTrack.ClientSize.Height - $script:ListScrollThumb.Height)
    $clampedY = [Math]::Max(0, [Math]::Min($ThumbY, $range))
    $target = [int][Math]::Round($maxTop * ($clampedY / [double]$range))
    Scroll-ListViewToTopIndex -Index $target
}

function Scroll-ListViewByRows {
    param([int]$Rows)
    Scroll-ListViewToTopIndex -Index ((Get-ListViewTopIndex) + $Rows)
}

function Get-MouseWheelScrollRows {
    param(
        [int]$Delta,
        [int]$VisibleRows
    )

    if ($Delta -eq 0) { return 0 }

    $lines = [System.Windows.Forms.SystemInformation]::MouseWheelScrollLines
    if ($lines -le 0) {
        $lines = 3
    } elseif ($lines -gt 100) {
        $lines = [Math]::Max(1, $VisibleRows)
    }

    $notches = [Math]::Max(1, [int][Math]::Ceiling([Math]::Abs($Delta) / 120.0))
    $rows = [int]($lines * $notches)
    if ($Delta -gt 0) { return -$rows }
    return $rows
}

function Scroll-ListViewByWheelDelta {
    param([int]$Delta)

    $rows = Get-MouseWheelScrollRows -Delta $Delta -VisibleRows (Get-ListViewVisibleRowCount)
    if ($rows -eq 0) {
        Update-ListScrollBar
        return
    }

    Scroll-ListViewByRows -Rows $rows
}

function Set-WheelFocus {
    param([System.Windows.Forms.Control]$Control)

    if ($null -eq $Control) { return }
    try {
        $method = $Control.GetType().GetMethod('ActivateForWheel')
        if ($method) {
            [void]$method.Invoke($Control, @())
            return
        }
    } catch { }

    try { [void]$Control.Focus() } catch { }
}

function Set-MouseWheelHandled {
    param($EventArgs)

    try {
        if ($EventArgs -is [System.Windows.Forms.HandledMouseEventArgs]) {
            $EventArgs.Handled = $true
        }
    } catch { }
}

function Normalize-FolderPath {
    param([string]$PathValue)
    if ([string]::IsNullOrWhiteSpace($PathValue)) { return $null }
    try {
        $full = [System.IO.Path]::GetFullPath($PathValue.Trim())
        if (-not (Test-Path -LiteralPath $full -PathType Container)) { return $null }
        return $full.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    } catch {
        return $null
    }
}

function Add-SavedGameRoot {
    param([string]$PathValue)
    $norm = Normalize-FolderPath -PathValue $PathValue
    if ($null -eq $norm) { return $false }

    $exists = $false
    foreach ($p in $script:State.SavedGameRoots) {
        if ($p.Equals($norm, [System.StringComparison]::OrdinalIgnoreCase)) {
            $exists = $true
            break
        }
    }
    if (-not $exists) {
        $script:State.SavedGameRoots = @($script:State.SavedGameRoots + $norm)
    }
    return $true
}

function Remove-SavedGameRoot {
    param([string]$PathValue)
    if ([string]::IsNullOrWhiteSpace($PathValue)) { return $false }
    try {
        $norm = [System.IO.Path]::GetFullPath($PathValue.Trim()).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    } catch {
        return $false
    }
    if ($null -eq $norm) { return $false }

    $kept = New-Object System.Collections.Generic.List[string]
    $removed = $false
    foreach ($p in $script:State.SavedGameRoots) {
        if ($p.Equals($norm, [System.StringComparison]::OrdinalIgnoreCase)) {
            $removed = $true
            continue
        }
        $kept.Add($p)
    }
    $script:State.SavedGameRoots = @($kept.ToArray())

    if ($script:State.GameRoot -and $script:State.GameRoot.Equals($norm, [System.StringComparison]::OrdinalIgnoreCase)) {
        if ($script:State.SavedGameRoots.Count -gt 0) {
            $script:State.GameRoot = $script:State.SavedGameRoots[0]
        } else {
            $script:State.GameRoot = ''
        }
    }
    return $removed
}

function Remove-MissingSavedGameRoots {
    $kept = New-Object System.Collections.Generic.List[string]
    $removedCount = 0
    foreach ($p in $script:State.SavedGameRoots) {
        if (Test-Path -LiteralPath $p -PathType Container) {
            $kept.Add($p)
        } else {
            $removedCount++
        }
    }
    $script:State.SavedGameRoots = @($kept.ToArray())
    if ($script:State.GameRoot -and -not (Test-Path -LiteralPath $script:State.GameRoot -PathType Container)) {
        $script:State.GameRoot = if ($script:State.SavedGameRoots.Count -gt 0) { $script:State.SavedGameRoots[0] } else { '' }
    }
    return $removedCount
}

function Refresh-GameRootSelector {
    if (-not $script:GameRootBox) { return }
    if ($script:GameRootBox -is [System.Windows.Forms.TextBox]) {
        if (-not [string]::IsNullOrWhiteSpace($script:State.GameRoot)) {
            $script:GameRootBox.Text = $script:State.GameRoot
        }
        return
    }
    if ($script:GameRootBox -isnot [System.Windows.Forms.ComboBox]) { return }

    $current = $script:GameRootBox.Text
    $script:GameRootBox.BeginUpdate()
    try {
        $script:GameRootBox.Items.Clear()
        foreach ($p in ($script:State.SavedGameRoots | Sort-Object)) {
            [void]$script:GameRootBox.Items.Add($p)
        }
        $script:GameRootBox.Text = $current
    } finally {
        $script:GameRootBox.EndUpdate()
    }
}

function Set-CurrentGameRoot {
    param(
        [string]$PathValue,
        [bool]$Persist = $true
    )
    $norm = Normalize-FolderPath -PathValue $PathValue
    if ($null -eq $norm) { return $false }

    $script:State.GameRoot = $norm
    [void](Add-SavedGameRoot -PathValue $norm)
    Refresh-GameRootSelector
    if ($script:GameRootBox) { $script:GameRootBox.Text = $norm }
    if ($Persist) { Save-Settings }
    return $true
}

function Align-4 {
    param([int]$Value)
    return (($Value + 3) -band -4)
}

function Copy-ByteRange {
    param([byte[]]$Source, [int]$Start, [int]$Length)
    if ($Length -le 0) { return ,[byte[]]@() }
    $dest = New-Object byte[] $Length
    [Array]::Copy($Source, $Start, $dest, 0, $Length)
    return ,$dest
}

function Assert-FileSizeLimit {
    param(
        [string]$Path,
        [int64]$MaxBytes,
        [string]$Label
    )

    $info = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($info.Length -gt $MaxBytes) {
        throw "$Label is too large: $($info.Length) bytes. Limit: $MaxBytes bytes."
    }
    return [int64]$info.Length
}

function Read-FileBytesResponsive {
    param(
        [string]$Path,
        [int64]$MaxBytes,
        [string]$Label
    )

    $length = Assert-FileSizeLimit -Path $Path -MaxBytes $MaxBytes -Label $Label
    $buffer = New-Object byte[] $length
    if ($length -eq 0) { return ,$buffer }

    $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
        $offset = 0
        $chunkSize = 1048576
        while ($offset -lt $length) {
            $toRead = [Math]::Min($chunkSize, $length - $offset)
            $read = $fs.Read($buffer, $offset, $toRead)
            if ($read -le 0) { throw "Could not read full file: $Path" }
            $offset += $read
            Pump-UiMessages
        }
    } finally {
        $fs.Dispose()
    }

    return ,$buffer
}

function Copy-StreamToMemoryResponsive {
    param(
        [System.IO.Stream]$InputStream,
        [System.IO.MemoryStream]$OutputStream
    )

    $buffer = New-Object byte[] 1048576
    while ($true) {
        $read = $InputStream.Read($buffer, 0, $buffer.Length)
        if ($read -le 0) { break }
        $OutputStream.Write($buffer, 0, $read)
        Pump-UiMessages
    }
}

function Write-BytesToStreamResponsive {
    param(
        [System.IO.Stream]$OutputStream,
        [byte[]]$Bytes
    )

    if ($null -eq $Bytes -or $Bytes.Length -eq 0) { return }
    $offset = 0
    $chunkSize = 1048576
    while ($offset -lt $Bytes.Length) {
        $count = [Math]::Min($chunkSize, $Bytes.Length - $offset)
        $OutputStream.Write($Bytes, $offset, $count)
        $offset += $count
        Pump-UiMessages
    }
}

function Write-FileBytesResponsive {
    param(
        [string]$Path,
        [byte[]]$Bytes
    )

    # Escritura atomica: se escribe primero a un temporal en el mismo directorio
    # (mismo volumen) y luego se reemplaza el destino. Asi un fallo a mitad de
    # escritura nunca deja el BSP original corrupto.
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $dir = [System.IO.Path]::GetDirectoryName($fullPath)
    $tmpPath = [System.IO.Path]::Combine($dir, ([System.IO.Path]::GetFileName($fullPath) + '.' + [System.Guid]::NewGuid().ToString('N') + '.tmp'))

    try {
        $fs = [System.IO.File]::Open($tmpPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try {
            Write-BytesToStreamResponsive -OutputStream $fs -Bytes $Bytes
            $fs.Flush($true)
        } finally {
            $fs.Dispose()
        }

        if ([System.IO.File]::Exists($fullPath)) {
            [System.IO.File]::Replace($tmpPath, $fullPath, $null)
        } else {
            [System.IO.File]::Move($tmpPath, $fullPath)
        }
    } catch {
        if ([System.IO.File]::Exists($tmpPath)) {
            try { [System.IO.File]::Delete($tmpPath) } catch { }
        }
        throw
    }
}

function Normalize-ArchivePath {
    param([string]$PathValue)
    if ([string]::IsNullOrWhiteSpace($PathValue)) { throw 'Invalid internal path.' }
    $p = $PathValue.Replace('\', '/').Trim()
    if ($p.StartsWith('/')) { throw "Unsafe absolute internal path: $PathValue" }
    if ([string]::IsNullOrWhiteSpace($p) -or $p -eq '.') { throw 'Invalid internal path.' }
    if ($p -match '[\x00-\x1F<>:"|?*]') { throw "Unsafe internal path characters: $PathValue" }
    foreach ($part in $p.Split('/')) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part -eq '.' -or $part -eq '..') { throw "Unsafe internal path: $PathValue" }
        if ($part.EndsWith('.') -or $part.EndsWith(' ')) { throw "Unsafe internal path segment: $PathValue" }
        if ($part -match '^(?i)(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$') { throw "Unsafe reserved internal path segment: $PathValue" }
    }
    return $p
}

function Read-VpkCString {
    param([System.IO.BinaryReader]$Reader, [long]$Limit)

    $bytes = New-Object System.Collections.Generic.List[byte]
    while ($Reader.BaseStream.Position -lt $Limit) {
        $b = $Reader.ReadByte()
        if ($b -eq 0) { break }
        $bytes.Add([byte]$b)
    }

    if ($bytes.Count -eq 0) { return '' }
    return [System.Text.Encoding]::ASCII.GetString($bytes.ToArray())
}

function ConvertTo-VpkArchivePath {
    param([string]$Extension, [string]$Directory, [string]$FileName)

    if ($Extension -eq ' ') { $Extension = '' }
    if ($Directory -eq ' ') { $Directory = '' }
    if ($FileName -eq ' ') { $FileName = '' }
    if ([string]::IsNullOrWhiteSpace($FileName)) { return $null }

    $leaf = if ([string]::IsNullOrWhiteSpace($Extension)) { $FileName } else { "${FileName}.${Extension}" }
    $path = if ([string]::IsNullOrWhiteSpace($Directory)) { $leaf } else { "$Directory/$leaf" }

    try {
        return Normalize-ArchivePath -PathValue $path
    } catch {
        return $null
    }
}

function Add-VpkDirectoryEntries {
    param(
        [string]$DirVpkPath,
        [System.Collections.Generic.HashSet[string]]$Entries,
        [System.Collections.Generic.HashSet[string]]$NeededRefs = $null,
        [string[]]$NeededRefArray = $null
    )

    if (-not [System.IO.File]::Exists($DirVpkPath)) { return }

    if ($null -ne $NeededRefs -and ('PakRatVpk' -as [type])) {
        try {
            if ($null -eq $NeededRefArray) {
                $NeededRefArray = [string[]]@($NeededRefs | ForEach-Object { [string]$_ })
            }
            foreach ($match in [PakRatVpk]::FindNeededEntries($DirVpkPath, $NeededRefArray)) {
                if (-not [string]::IsNullOrWhiteSpace($match)) { [void]$Entries.Add($match) }
            }
            return
        } catch {
            # Fall back to the PowerShell parser below if the compiled helper is unavailable.
        }
    }

    $fs = $null
    $br = $null
    try {
        $fs = [System.IO.File]::OpenRead($DirVpkPath)
        $br = New-Object System.IO.BinaryReader($fs)
        $limit = $fs.Length

        if ($fs.Length -ge 12) {
            $signature = $br.ReadUInt32()
            if ($signature -eq 0x55aa1234) {
                $version = $br.ReadUInt32()
                $treeSize = [uint32]$br.ReadUInt32()
                if ($version -eq 2) {
                    if ($fs.Length -lt 28) { return }
                    [void]$br.ReadUInt32()
                    [void]$br.ReadUInt32()
                    [void]$br.ReadUInt32()
                    [void]$br.ReadUInt32()
                } elseif ($version -ne 1) {
                    return
                }
                $limit = [Math]::Min($fs.Length, ($fs.Position + [int64]$treeSize))
            } else {
                $fs.Position = 0
            }
        }

        while ($fs.Position -lt $limit) {
            $extension = Read-VpkCString -Reader $br -Limit $limit
            if ([string]::IsNullOrEmpty($extension)) { break }

            while ($fs.Position -lt $limit) {
                $directory = Read-VpkCString -Reader $br -Limit $limit
                if ([string]::IsNullOrEmpty($directory)) { break }

                while ($fs.Position -lt $limit) {
                    Pump-UiMessages
                    $fileName = Read-VpkCString -Reader $br -Limit $limit
                    if ([string]::IsNullOrEmpty($fileName)) { break }
                    if (($limit - $fs.Position) -lt 18) { return }

                    [void]$br.ReadUInt32()
                    $preloadBytes = $br.ReadUInt16()
                    [void]$br.ReadUInt16()
                    [void]$br.ReadUInt32()
                    [void]$br.ReadUInt32()
                    $terminator = $br.ReadUInt16()
                    if ($terminator -ne 0xffff) { return }

                    $archivePath = ConvertTo-VpkArchivePath -Extension $extension -Directory $directory -FileName $fileName
                    if ($null -ne $archivePath -and ($null -eq $NeededRefs -or $NeededRefs.Contains($archivePath))) {
                        [void]$Entries.Add($archivePath)
                        if ($null -ne $NeededRefs -and $Entries.Count -ge $NeededRefs.Count) { return }
                    }

                    if ($preloadBytes -gt 0) {
                        $next = $fs.Position + [int64]$preloadBytes
                        if ($next -gt $limit) { return }
                        $fs.Position = $next
                    }
                }
            }
        }
    } catch {
        return
    } finally {
        if ($br) { $br.Dispose() }
        elseif ($fs) { $fs.Dispose() }
    }
}

function Add-BaseVpkCandidateFile {
    param(
        [System.Collections.Generic.HashSet[string]]$Files,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) { return }

    $candidate = $Path
    if ($candidate -notmatch '(?i)_dir\.vpk$' -and $candidate -match '(?i)\.vpk$') {
        $candidate = [System.IO.Path]::Combine(
            [System.IO.Path]::GetDirectoryName($candidate),
            ([System.IO.Path]::GetFileNameWithoutExtension($candidate) + '_dir.vpk')
        )
    }

    if ([System.IO.File]::Exists($candidate)) {
        [void]$Files.Add([System.IO.Path]::GetFullPath($candidate))
    }
}

function Add-BaseVpkFilesFromFolder {
    param(
        [System.Collections.Generic.HashSet[string]]$Files,
        [string]$Folder
    )

    if ([string]::IsNullOrWhiteSpace($Folder) -or -not [System.IO.Directory]::Exists($Folder)) { return }
    foreach ($vpk in (Get-ChildItem -LiteralPath $Folder -Filter '*_dir.vpk' -File -ErrorAction SilentlyContinue)) {
        [void]$Files.Add($vpk.FullName)
    }
}

function Get-GameInfoSearchPathValues {
    param([string]$GameRoot)

    $values = New-Object System.Collections.Generic.List[string]
    $gameInfo = Join-Path $GameRoot 'gameinfo.txt'
    if (-not (Test-Path -LiteralPath $gameInfo -PathType Leaf)) { return @() }

    $inSearchPaths = $false
    $braceDepth = 0
    $seenOpenBrace = $false

    foreach ($raw in [System.IO.File]::ReadAllLines($gameInfo)) {
        $line = ([regex]::Replace($raw, '//.*$', '')).Trim()
        if ([string]::IsNullOrWhiteSpace($line)) { continue }

        if (-not $inSearchPaths) {
            if ($line -match '^(?i)"?SearchPaths"?\b') { $inSearchPaths = $true }
            else { continue }
        }

        $openCount = [regex]::Matches($line, '\{').Count
        $closeCount = [regex]::Matches($line, '\}').Count
        if ($openCount -gt 0) { $seenOpenBrace = $true }
        $braceDepth += $openCount

        $body = ($line -replace '[{}]', ' ').Trim()
        if ($seenOpenBrace -and -not [string]::IsNullOrWhiteSpace($body) -and $body -notmatch '^(?i)"?SearchPaths"?$') {
            $tokens = New-Object System.Collections.Generic.List[string]
            foreach ($m in [regex]::Matches($body, '"([^"]*)"|([^\s{}]+)')) {
                $token = if ($m.Groups[1].Success) { $m.Groups[1].Value } else { $m.Groups[2].Value }
                if (-not [string]::IsNullOrWhiteSpace($token)) { $tokens.Add($token) }
            }
            if ($tokens.Count -ge 2) { $values.Add($tokens[$tokens.Count - 1]) }
        }

        $braceDepth -= $closeCount
        if ($seenOpenBrace -and $braceDepth -le 0) { break }
    }

    return @($values.ToArray())
}

function Resolve-GameInfoSearchPath {
    param([string]$GameRoot, [string]$SearchPath)

    if ([string]::IsNullOrWhiteSpace($SearchPath)) { return $null }
    $value = $SearchPath.Trim().Trim('"').Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($value) -or $value.Contains('*')) { return $null }
    if ($value -eq 'GAME') { return $GameRoot }

    $sourceRoot = [System.IO.Path]::GetDirectoryName($GameRoot)
    if ([string]::IsNullOrWhiteSpace($sourceRoot)) { $sourceRoot = $GameRoot }

    if ($value -match '^(?i)\|gameinfo_path\|') {
        $suffix = $value.Substring('|gameinfo_path|'.Length).TrimStart('/', '\')
        if ([string]::IsNullOrWhiteSpace($suffix) -or $suffix -eq '.') { return $GameRoot }
        return [System.IO.Path]::GetFullPath((Join-Path $GameRoot $suffix))
    }

    if ($value -match '^(?i)\|all_source_engine_paths\|') {
        $suffix = $value.Substring('|all_source_engine_paths|'.Length).TrimStart('/', '\')
        if ([string]::IsNullOrWhiteSpace($suffix) -or $suffix -eq '.') { return $sourceRoot }
        return [System.IO.Path]::GetFullPath((Join-Path $sourceRoot $suffix))
    }

    if ([System.IO.Path]::IsPathRooted($value)) { return [System.IO.Path]::GetFullPath($value) }
    return [System.IO.Path]::GetFullPath((Join-Path $sourceRoot $value))
}

function Get-ArchivePathSetKey {
    param([System.Collections.Generic.HashSet[string]]$Set)
    if ($null -eq $Set -or $Set.Count -eq 0) { return '' }
    return (($Set | Sort-Object | ForEach-Object { $_.ToLowerInvariant() }) -join "`n")
}

function Get-BaseVpkEntries {
    param(
        [string]$GameRoot,
        [System.Collections.Generic.HashSet[string]]$NeededRefs = $null
    )

    $norm = Normalize-FolderPath -PathValue $GameRoot
    $entries = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    if ($null -eq $norm) { return ,$entries }
    $neededKey = Get-ArchivePathSetKey -Set $NeededRefs
    $neededArray = if ($null -ne $NeededRefs) { [string[]]@($NeededRefs | ForEach-Object { [string]$_ }) } else { $null }

    if (($null -ne $script:BaseVpkCache.Matches) -and
        $script:BaseVpkCache.GameRoot.Equals($norm, [System.StringComparison]::OrdinalIgnoreCase) -and
        $script:BaseVpkCache.NeededKey.Equals($neededKey, [System.StringComparison]::Ordinal)) {
        return ,$script:BaseVpkCache.Matches
    }

    $vpkFiles = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    Add-BaseVpkFilesFromFolder -Files $vpkFiles -Folder $norm

    foreach ($searchPath in (Get-GameInfoSearchPathValues -GameRoot $norm)) {
        $resolved = Resolve-GameInfoSearchPath -GameRoot $norm -SearchPath $searchPath
        if ($null -eq $resolved) { continue }
        if ([System.IO.Directory]::Exists($resolved)) {
            Add-BaseVpkFilesFromFolder -Files $vpkFiles -Folder $resolved
        } else {
            Add-BaseVpkCandidateFile -Files $vpkFiles -Path $resolved
        }
    }

    foreach ($vpk in ($vpkFiles | Sort-Object)) {
        Add-VpkDirectoryEntries -DirVpkPath $vpk -Entries $entries -NeededRefs $NeededRefs -NeededRefArray $neededArray
        if ($null -ne $NeededRefs -and $entries.Count -ge $NeededRefs.Count) { break }
    }

    $script:BaseVpkCache.GameRoot = $norm
    $script:BaseVpkCache.NeededKey = $neededKey
    $script:BaseVpkCache.Matches = $entries
    return ,$entries
}

function Test-BaseGameArchivePath {
    param(
        [System.Collections.Generic.HashSet[string]]$BaseVpkEntries,
        [string]$ArchivePath
    )

    if ($null -eq $BaseVpkEntries -or [string]::IsNullOrWhiteSpace($ArchivePath)) { return $false }
    return $BaseVpkEntries.Contains($ArchivePath)
}

function To-OsPath { param([string]$ArchivePath) return $ArchivePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar) }

function Get-RelativePath {
    param([string]$BasePath, [string]$FullPath)
    $base = [System.IO.Path]::GetFullPath($BasePath)
    $full = [System.IO.Path]::GetFullPath($FullPath)
    if (-not $base.EndsWith([System.IO.Path]::DirectorySeparatorChar)) { $base += [System.IO.Path]::DirectorySeparatorChar }
    if (-not $full.StartsWith($base, [System.StringComparison]::OrdinalIgnoreCase)) { return $null }
    return $full.Substring($base.Length)
}

function Get-EntryType {
    param([string]$Name)
    $ext = [System.IO.Path]::GetExtension($Name).ToLowerInvariant()
    switch ($ext) {
        '.vmt' { return 'Material' }
        '.vtf' { return 'Texture' }
        '.dds' { return 'Radar Texture' }
        '.mdl' { return 'Model' }
        '.vvd' { return 'Model data' }
        '.vtx' { return 'Model mesh' }
        '.phy' { return 'Model physics' }
        '.nav' { return 'Navigation Mesh' }
        '.ain' { return 'Node Graph' }
        '.wav' { return 'Sound' }
        '.mp3' { return 'Sound' }
        '.pcf' { return 'Particles' }
        '.txt' { return 'Text' }
        default { return 'File' }
    }
}

function Get-ArchiveSortBaseName {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) { return '' }
    $leaf = [System.IO.Path]::GetFileName($Name.Replace('\', '/'))
    if ($leaf -match '^(?i)(.+)\.(dx80|dx90|sw)\.vtx$') { return $Matches[1].ToLowerInvariant() }
    return [System.IO.Path]::GetFileNameWithoutExtension($leaf).ToLowerInvariant()
}

function Get-ArchiveSortExtensionRank {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) { return 99 }
    $leaf = [System.IO.Path]::GetFileName($Name.Replace('\', '/'))
    $ext = [System.IO.Path]::GetExtension($leaf).ToLowerInvariant()

    if ($leaf -match '^(?i).+\.dx80\.vtx$') { return 22 }
    if ($leaf -match '^(?i).+\.dx90\.vtx$') { return 23 }
    if ($leaf -match '^(?i).+\.sw\.vtx$') { return 24 }

    switch ($ext) {
        '.vmt' { return 10 }
        '.vtf' { return 11 }
        '.mdl' { return 20 }
        '.vvd' { return 21 }
        '.vtx' { return 25 }
        '.phy' { return 26 }
        default { return 99 }
    }
}

function Get-ArchiveSortInfo {
    param([string]$ArchivePath)

    $normalized = $ArchivePath.Replace('\', '/').Trim('/')
    $dir = [System.IO.Path]::GetDirectoryName((To-OsPath $normalized))
    if ($null -eq $dir) { $dir = '' } else { $dir = $dir.Replace('\', '/') }
    $name = [System.IO.Path]::GetFileName((To-OsPath $normalized))

    return [pscustomobject]@{
        Directory = $dir
        Name = $name
        SortDirectory = $dir.ToLowerInvariant()
        SortBaseName = Get-ArchiveSortBaseName -Name $name
        SortExtensionRank = Get-ArchiveSortExtensionRank -Name $name
        SortName = $name.ToLowerInvariant()
        SortFullPath = $normalized.ToLowerInvariant()
    }
}

function Get-ArchivePathSortKey {
    param([string]$ArchivePath)

    $info = Get-ArchiveSortInfo -ArchivePath $ArchivePath
    return ('{0}|{1}|{2:D3}|{3}' -f $info.SortDirectory, $info.SortBaseName, [int]$info.SortExtensionRank, $info.SortName)
}

function New-EntryRecord {
    param([string]$FullPath, [byte[]]$Data, [bool]$InOriginal, [bool]$Modified)
    $normalized = Normalize-ArchivePath -PathValue $FullPath
    $sortInfo = Get-ArchiveSortInfo -ArchivePath $normalized
    [pscustomobject]@{
        FullPath = $normalized
        Directory = $sortInfo.Directory
        Name = $sortInfo.Name
        Size = $Data.Length
        Type = Get-EntryType -Name $sortInfo.Name
        Data = $Data
        InOriginal = $InOriginal
        Modified = $Modified
        SortDirectory = $sortInfo.SortDirectory
        SortBaseName = $sortInfo.SortBaseName
        SortExtensionRank = $sortInfo.SortExtensionRank
        SortName = $sortInfo.SortName
        SortFullPath = $sortInfo.SortFullPath
    }
}

function Set-WindowTitle {
    if (-not $script:MainForm) { return }
    $name = if ($script:State.CurrentBspPath) { [System.IO.Path]::GetFileName($script:State.CurrentBspPath) } else { 'No BSP loaded' }
    $dirty = if ($script:State.IsDirty) { ' *' } else { '' }
    $script:MainForm.Text = "PakRat Modern - $name$dirty"
}

function Get-ListSortMarker {
    param([int]$ColumnIndex)
    if ($script:State.SortColumn -ne $ColumnIndex) { return '' }
    if ($script:State.SortDescending) { return ' v' }
    return ' ^'
}

function Refresh-ListHeaderUI {
    if (-not $script:ListHeaderPanel) { return }

    $labels = @('Name', 'Path', 'Size', 'Type')
    for ($i = 0; $i -lt $script:ListHeaderButtons.Count; $i++) {
        $btn = $script:ListHeaderButtons[$i]
        if (-not $btn) { continue }
        if ($i -ge $labels.Count) {
            $btn.Visible = $false
            continue
        }
        $btn.Visible = $true

        $width = if ($script:ListView -and $script:ListView.Columns.Count -gt $i) {
            $script:ListView.Columns[$i].Width
        } else {
            $btn.Width
        }

        $btn.Width = $width
        $btn.Text = $labels[$i] + (Get-ListSortMarker -ColumnIndex $i)
    }
}

function Set-ListSort {
    param([int]$ColumnIndex)
    if ($script:State.SortColumn -eq $ColumnIndex) {
        $script:State.SortDescending = -not $script:State.SortDescending
    } else {
        $script:State.SortColumn = $ColumnIndex
        $script:State.SortDescending = $false
    }

    Refresh-AllViews
    Refresh-ListHeaderUI
}

function Ensure-ListViewColumnSchema {
    if (-not $script:ListView) { return }

    $columns = @(
        @{ Text = 'Name'; Width = 300 },
        @{ Text = 'Path'; Width = 520 },
        @{ Text = 'Size'; Width = 120 },
        @{ Text = 'Type'; Width = 150 }
    )

    $needsReset = ($script:ListView.Columns.Count -ne $columns.Count)
    if (-not $needsReset) {
        for ($i = 0; $i -lt $columns.Count; $i++) {
            if ($script:ListView.Columns[$i].Text -ne $columns[$i].Text) {
                $needsReset = $true
                break
            }
        }
    }

    if (-not $needsReset) { return }

    $script:ListView.Columns.Clear()
    foreach ($column in $columns) {
        [void]$script:ListView.Columns.Add([string]$column.Text, [int]$column.Width)
    }
}

function Ensure-BspLoaded {
    if (-not $script:State.BspRaw) { throw 'Load a BSP first.' }
}

function Serialize-Header {
    param([int]$Version, [int]$MapRevision, [object[]]$Lumps)
    $ms = New-Object System.IO.MemoryStream
    try {
        $ident = [System.Text.Encoding]::ASCII.GetBytes('VBSP')
        $ms.Write($ident, 0, $ident.Length)
        $ms.Write([BitConverter]::GetBytes([int]$Version), 0, 4)
        for ($i = 0; $i -lt $LumpCount; $i++) {
            $l = $Lumps[$i]
            $ms.Write([BitConverter]::GetBytes([int]$l.fileofs), 0, 4)
            $ms.Write([BitConverter]::GetBytes([int]$l.filelen), 0, 4)
            $ms.Write([BitConverter]::GetBytes([int]$l.version), 0, 4)
            $fourcc = [byte[]]$l.fourcc
            if ($fourcc.Length -ne 4) { throw "Lump $i has invalid fourcc." }
            $ms.Write($fourcc, 0, 4)
        }
        $ms.Write([BitConverter]::GetBytes([int]$MapRevision), 0, 4)
        return ,$ms.ToArray()
    } finally {
        $ms.Dispose()
    }
}

function Parse-Bsp {
    param([string]$Path)
    $raw = Read-FileBytesResponsive -Path $Path -MaxBytes $MaxBspBytes -Label 'BSP'
    if ($raw.Length -lt $HeaderSize) { throw 'File is too small for a Source BSP.' }
    $ident = [System.Text.Encoding]::ASCII.GetString($raw, 0, 4)
    if ($ident -ne 'VBSP') { throw 'Invalid BSP identifier (expected VBSP).' }

    $version = [BitConverter]::ToInt32($raw, 4)
    $offset = 8
    $lumps = @()

    for ($i = 0; $i -lt $LumpCount; $i++) {
        $fileofs = [BitConverter]::ToInt32($raw, $offset)
        $filelen = [BitConverter]::ToInt32($raw, $offset + 4)
        $lumpver = [BitConverter]::ToInt32($raw, $offset + 8)
        $fourcc = Copy-ByteRange -Source $raw -Start ($offset + 12) -Length 4
        $offset += 16

        if ($filelen -lt 0) { throw "Lump $i has negative length." }
        if ($filelen -gt 0) {
            $end = $fileofs + $filelen
            if ($fileofs -lt 0 -or $end -gt $raw.Length -or $fileofs -gt $end) {
                throw "Lump $i out of range (ofs=$fileofs len=$filelen)."
            }
        }

        $lumps += [pscustomobject]@{ fileofs = $fileofs; filelen = $filelen; version = $lumpver; fourcc = $fourcc }
    }

    $mapRevision = [BitConverter]::ToInt32($raw, $offset)
    [pscustomobject]@{ Raw = $raw; Version = $version; MapRevision = $mapRevision; Lumps = $lumps }
}

function Get-LumpBytes {
    param([byte[]]$Raw, [object[]]$Lumps, [int]$Index)
    $l = $Lumps[$Index]
    if ($l.filelen -eq 0) { return [byte[]]@() }
    return Copy-ByteRange -Source $Raw -Start $l.fileofs -Length $l.filelen
}

function Read-PakEntriesFromStream {
    param([System.IO.Stream]$Stream)

    $entries = [ordered]@{}
    if ($null -eq $Stream -or $Stream.Length -eq 0) { return $entries }

    try {
        $zip = New-Object System.IO.Compression.ZipArchive($Stream, [System.IO.Compression.ZipArchiveMode]::Read, $true)
        try {
            if ($zip.Entries.Count -gt $MaxPakEntries) { throw "PAK has too many entries: $($zip.Entries.Count). Limit: $MaxPakEntries." }
            $totalUncompressed = 0L
            foreach ($entry in $zip.Entries) {
                if ($entry.FullName.EndsWith('/')) { continue }
                $name = Normalize-ArchivePath -PathValue $entry.FullName
                if ($entry.Length -gt $MaxPakEntryBytes) { throw "PAK entry is too large: $name ($($entry.Length) bytes). Limit: $MaxPakEntryBytes bytes." }
                $totalUncompressed += [int64]$entry.Length
                if ($totalUncompressed -gt $MaxPakTotalBytes) { throw "PAK uncompressed total is too large. Limit: $MaxPakTotalBytes bytes." }
                $stream = $entry.Open()
                try {
                    $tmp = New-Object System.IO.MemoryStream
                    try {
                        Copy-StreamToMemoryResponsive -InputStream $stream -OutputStream $tmp
                        $entries[$name] = New-EntryRecord -FullPath $name -Data $tmp.ToArray() -InOriginal:$true -Modified:$false
                    } finally { $tmp.Dispose() }
                } finally { $stream.Dispose() }
            }
        } finally { $zip.Dispose() }
    } catch {
        throw "PAK lump could not be read safely: $($_.Exception.Message)"
    }

    return $entries
}

function Read-PakEntries {
    param([byte[]]$PakBytes)
    if ($PakBytes.Length -eq 0) { return [ordered]@{} }

    $ms = New-Object System.IO.MemoryStream(,$PakBytes)
    try { return Read-PakEntriesFromStream -Stream $ms }
    finally { $ms.Dispose() }
}

function Read-PakEntriesFromBspLump {
    param([byte[]]$Raw, [object[]]$Lumps, [int]$Index)

    $l = $Lumps[$Index]
    if ($l.filelen -eq 0) { return [ordered]@{} }

    $ms = [System.IO.MemoryStream]::new($Raw, [int]$l.fileofs, [int]$l.filelen, $false)
    try { return Read-PakEntriesFromStream -Stream $ms }
    finally { $ms.Dispose() }
}

function Get-Crc32 {
    param([byte[]]$Bytes)

    return [PakRatCrc32]::Compute($Bytes)
}

function Write-ZipUInt16 {
    param([System.IO.Stream]$Stream, [uint16]$Value)
    $bytes = [BitConverter]::GetBytes($Value)
    $Stream.Write($bytes, 0, 2)
}

function Write-ZipUInt32 {
    param([System.IO.Stream]$Stream, [uint32]$Value)
    $bytes = [BitConverter]::GetBytes($Value)
    $Stream.Write($bytes, 0, 4)
}

function Get-ZipDosTimestamp {
    $now = [DateTime]::Now
    $year = [Math]::Max(1980, [Math]::Min(2107, $now.Year))
    $date = (($year - 1980) -shl 9) -bor ($now.Month -shl 5) -bor $now.Day
    $time = ($now.Hour -shl 11) -bor ($now.Minute -shl 5) -bor ([int]($now.Second / 2))
    return [pscustomobject]@{ Date = [uint16]$date; Time = [uint16]$time }
}

function Get-ZipEntryNameInfo {
    param([string]$ArchivePath)

    $usesUtf8 = $false
    foreach ($ch in $ArchivePath.ToCharArray()) {
        if ([int][char]$ch -gt 127) {
            $usesUtf8 = $true
            break
        }
    }

    if ($usesUtf8) {
        return [pscustomobject]@{
            Bytes = [System.Text.Encoding]::UTF8.GetBytes($ArchivePath)
            Flags = [uint16]0x0800
        }
    }

    return [pscustomobject]@{
        Bytes = [System.Text.Encoding]::ASCII.GetBytes($ArchivePath)
        Flags = [uint16]0
    }
}

function Write-PakEntries {
    param([System.Collections.IDictionary]$Entries)
    if ($Entries.Count -gt $MaxPakEntries) { throw "PAK has too many entries: $($Entries.Count). Limit: $MaxPakEntries." }
    $totalUncompressed = 0L

    $ms = New-Object System.IO.MemoryStream
    try {
        $centralRecords = New-Object System.Collections.Generic.List[object]
        $timestamp = Get-ZipDosTimestamp

        foreach ($k in (@($Entries.Keys) | Sort-Object)) {
            Pump-UiMessages
            $rec = $Entries[$k]
            $bytes = [byte[]]$rec.Data
            if ($bytes.Length -gt $MaxPakEntryBytes) { throw "PAK entry is too large: $k ($($bytes.Length) bytes). Limit: $MaxPakEntryBytes bytes." }
            $totalUncompressed += [int64]$bytes.Length
            if ($totalUncompressed -gt $MaxPakTotalBytes) { throw "PAK uncompressed total is too large. Limit: $MaxPakTotalBytes bytes." }
            if ($bytes.Length -gt [uint32]::MaxValue) { throw "PAK entry is too large for classic ZIP: $k" }

            $nameInfo = Get-ZipEntryNameInfo -ArchivePath $k
            if ($nameInfo.Bytes.Length -gt [uint16]::MaxValue) { throw "PAK entry path is too long for ZIP: $k" }

            $localOffset = [uint32]$ms.Position
            $crc = Get-Crc32 -Bytes $bytes
            $size = [uint32]$bytes.Length
            $nameBytes = [byte[]]$nameInfo.Bytes
            $flags = [uint16]$nameInfo.Flags

            Write-ZipUInt32 -Stream $ms -Value ([uint32]0x04034b50)
            Write-ZipUInt16 -Stream $ms -Value ([uint16]10)
            Write-ZipUInt16 -Stream $ms -Value $flags
            Write-ZipUInt16 -Stream $ms -Value ([uint16]0)
            Write-ZipUInt16 -Stream $ms -Value $timestamp.Time
            Write-ZipUInt16 -Stream $ms -Value $timestamp.Date
            Write-ZipUInt32 -Stream $ms -Value $crc
            Write-ZipUInt32 -Stream $ms -Value $size
            Write-ZipUInt32 -Stream $ms -Value $size
            Write-ZipUInt16 -Stream $ms -Value ([uint16]$nameBytes.Length)
            Write-ZipUInt16 -Stream $ms -Value ([uint16]0)
            $ms.Write($nameBytes, 0, $nameBytes.Length)
            Write-BytesToStreamResponsive -OutputStream $ms -Bytes $bytes

            [void]$centralRecords.Add([pscustomobject]@{
                NameBytes = $nameBytes
                Flags = $flags
                Crc = $crc
                Size = $size
                LocalOffset = $localOffset
                Time = $timestamp.Time
                Date = $timestamp.Date
            })
        }

        $centralOffset = [uint32]$ms.Position
        foreach ($record in $centralRecords) {
            Pump-UiMessages
            $nameBytes = [byte[]]$record.NameBytes
            Write-ZipUInt32 -Stream $ms -Value ([uint32]0x02014b50)
            Write-ZipUInt16 -Stream $ms -Value ([uint16]20)
            Write-ZipUInt16 -Stream $ms -Value ([uint16]10)
            Write-ZipUInt16 -Stream $ms -Value ([uint16]$record.Flags)
            Write-ZipUInt16 -Stream $ms -Value ([uint16]0)
            Write-ZipUInt16 -Stream $ms -Value ([uint16]$record.Time)
            Write-ZipUInt16 -Stream $ms -Value ([uint16]$record.Date)
            Write-ZipUInt32 -Stream $ms -Value ([uint32]$record.Crc)
            Write-ZipUInt32 -Stream $ms -Value ([uint32]$record.Size)
            Write-ZipUInt32 -Stream $ms -Value ([uint32]$record.Size)
            Write-ZipUInt16 -Stream $ms -Value ([uint16]$nameBytes.Length)
            Write-ZipUInt16 -Stream $ms -Value ([uint16]0)
            Write-ZipUInt16 -Stream $ms -Value ([uint16]0)
            Write-ZipUInt16 -Stream $ms -Value ([uint16]0)
            Write-ZipUInt16 -Stream $ms -Value ([uint16]0)
            Write-ZipUInt32 -Stream $ms -Value ([uint32]0)
            Write-ZipUInt32 -Stream $ms -Value ([uint32]$record.LocalOffset)
            $ms.Write($nameBytes, 0, $nameBytes.Length)
        }

        $centralSize = [uint32]($ms.Position - $centralOffset)
        if ($centralRecords.Count -gt [uint16]::MaxValue) { throw 'PAK has too many entries for classic ZIP.' }
        Write-ZipUInt32 -Stream $ms -Value ([uint32]0x06054b50)
        Write-ZipUInt16 -Stream $ms -Value ([uint16]0)
        Write-ZipUInt16 -Stream $ms -Value ([uint16]0)
        Write-ZipUInt16 -Stream $ms -Value ([uint16]$centralRecords.Count)
        Write-ZipUInt16 -Stream $ms -Value ([uint16]$centralRecords.Count)
        Write-ZipUInt32 -Stream $ms -Value $centralSize
        Write-ZipUInt32 -Stream $ms -Value $centralOffset
        Write-ZipUInt16 -Stream $ms -Value ([uint16]0)

        return ,$ms.ToArray()
    } finally { $ms.Dispose() }
}

function Apply-PakToBsp {
    param([byte[]]$Raw, [object[]]$Lumps, [int]$Version, [int]$MapRevision, [byte[]]$NewPak)

    $newLumps = @()
    foreach ($l in $Lumps) {
        $newLumps += [pscustomobject]@{ fileofs = [int]$l.fileofs; filelen = [int]$l.filelen; version = [int]$l.version; fourcc = [byte[]]$l.fourcc.Clone() }
    }

    $pak = $newLumps[$PakLumpIndex]
    $game = $newLumps[$GameLumpIndex]

    if ($pak.filelen -eq 0) {
        $insertAt = Align-4 -Value $Raw.Length
        $updated = New-Object byte[] ($insertAt + $NewPak.Length)
        [Array]::Copy($Raw, 0, $updated, 0, $Raw.Length)
        if ($NewPak.Length -gt 0) { [Array]::Copy($NewPak, 0, $updated, $insertAt, $NewPak.Length) }
        $pak.fileofs = $insertAt
        $pak.filelen = $NewPak.Length
    } else {
        $oldStart = $pak.fileofs
        $oldEnd = $pak.fileofs + $pak.filelen
        $delta = $NewPak.Length - $pak.filelen

        if ($delta -ne 0 -and $game.filelen -gt 0 -and $game.fileofs -gt $oldStart) {
            throw 'Cannot resize PAKFILE because LUMP_GAME_LUMP is after it. This BSP is not safe to grow/shrink.'
        }

        $updated = New-Object byte[] ($Raw.Length + $delta)
        if ($oldStart -gt 0) { [Array]::Copy($Raw, 0, $updated, 0, $oldStart) }
        if ($NewPak.Length -gt 0) { [Array]::Copy($NewPak, 0, $updated, $oldStart, $NewPak.Length) }

        $tailLen = $Raw.Length - $oldEnd
        if ($tailLen -gt 0) { [Array]::Copy($Raw, $oldEnd, $updated, ($oldStart + $NewPak.Length), $tailLen) }

        if ($delta -ne 0) {
            for ($i = 0; $i -lt $newLumps.Count; $i++) {
                if ($i -eq $PakLumpIndex) { continue }
                $l = $newLumps[$i]
                if ($l.filelen -gt 0 -and $l.fileofs -gt $oldStart) { $l.fileofs += $delta }
            }
        }

        $pak.filelen = $NewPak.Length
    }

    $header = Serialize-Header -Version $Version -MapRevision $MapRevision -Lumps $newLumps
    [Array]::Copy($header, 0, $updated, 0, $header.Length)
    [pscustomobject]@{ Raw = $updated; Lumps = $newLumps }
}

function Verify-PakEntries {
    param([System.Collections.IDictionary]$Entries)
    try {
        $pak = Write-PakEntries -Entries $Entries
        $round = Read-PakEntries -PakBytes $pak
        [pscustomobject]@{ Ok = $true; Message = "ZIP valid with $(@($round.Keys).Count) entries." }
    } catch {
        [pscustomobject]@{ Ok = $false; Message = $_.Exception.Message }
    }
}
function Load-Settings {
    if (-not (Test-Path $script:SettingsPath)) { return }
    try {
        $raw = Get-Content -Raw -Path $script:SettingsPath
        if ([string]::IsNullOrWhiteSpace($raw)) { return }
        $obj = $raw | ConvertFrom-Json
        $script:State.SavedGameRoots = @()
        if ($obj.SavedGameRoots) {
            foreach ($p in @($obj.SavedGameRoots)) {
                [void](Add-SavedGameRoot -PathValue ([string]$p))
            }
        }
        if ($obj.GameRoot) {
            $script:State.GameRoot = [string]$obj.GameRoot
            [void](Add-SavedGameRoot -PathValue $script:State.GameRoot)
            $normCurrent = Normalize-FolderPath -PathValue $script:State.GameRoot
            if ($null -ne $normCurrent) { $script:State.GameRoot = $normCurrent }
        }
        if ($obj.PathFixupMode) { $script:State.PathFixupMode = [string]$obj.PathFixupMode }
        if ($null -ne $obj.IncludeExtrasInScan) { $script:State.IncludeExtrasInScan = [bool]$obj.IncludeExtrasInScan }
        if ($null -ne $obj.BackupBeforeInPlaceSave) { $script:State.BackupBeforeInPlaceSave = [bool]$obj.BackupBeforeInPlaceSave }
    } catch {
        Update-Status "Could not load settings: $($_.Exception.Message)"
    }
}

function Save-Settings {
    try {
        $obj = [pscustomobject]@{
            GameRoot = $script:State.GameRoot
            SavedGameRoots = @($script:State.SavedGameRoots)
            PathFixupMode = $script:State.PathFixupMode
            IncludeExtrasInScan = $script:State.IncludeExtrasInScan
            BackupBeforeInPlaceSave = $script:State.BackupBeforeInPlaceSave
        }
        $json = $obj | ConvertTo-Json -Depth 3
        Set-Content -Encoding ASCII -Path $script:SettingsPath -Value $json
    } catch {
        Show-ErrorDialog -Message "Could not save settings: $($_.Exception.Message)"
    }
}

function Get-SortedEntries {
    $all = @($script:State.Entries.Values)
    $column = $script:State.SortColumn
    $desc = $script:State.SortDescending

    $sorted = switch ($column) {
        0 { $all | Sort-Object SortBaseName, SortExtensionRank, SortDirectory, SortName }
        1 { $all | Sort-Object SortDirectory, SortBaseName, SortExtensionRank, SortName }
        2 { $all | Sort-Object @{Expression = { $_.Size }}, SortDirectory, SortBaseName, SortExtensionRank, SortName }
        3 { $all | Sort-Object Type, SortDirectory, SortBaseName, SortExtensionRank, SortName }
        default { $all | Sort-Object SortDirectory, SortBaseName, SortExtensionRank, SortName }
    }

    if ($desc) { [array]::Reverse($sorted) }
    return $sorted
}

function Refresh-ListView {
    $script:ListView.BeginUpdate()
    try {
        $script:ListView.View = 'Details'
        Ensure-ListViewColumnSchema
        $script:ListView.Items.Clear()
        $items = New-Object 'System.Collections.Generic.List[System.Windows.Forms.ListViewItem]'
        foreach ($entry in (Get-SortedEntries)) {
            $item = New-Object System.Windows.Forms.ListViewItem($entry.Name)
            [void]$item.SubItems.Add($entry.Directory)
            [void]$item.SubItems.Add($entry.Size.ToString())
            [void]$item.SubItems.Add($entry.Type)
            $item.Tag = $entry.FullPath
            if ($entry.Modified -or -not $entry.InOriginal) {
                $item.ForeColor = $script:Theme.ModifiedText
            } else {
                $item.ForeColor = $script:Theme.Text
            }
            [void]$items.Add($item)
        }
        if ($items.Count -gt 0) {
            $script:ListView.Items.AddRange([System.Windows.Forms.ListViewItem[]]$items.ToArray())
        }
    } finally {
        $script:ListView.EndUpdate()
    }
    Update-ListScrollBar
}

function Ensure-TreeFolderNode {
    param([System.Windows.Forms.TreeNodeCollection]$Nodes, [string]$FolderName)
    foreach ($n in $Nodes) {
        if ($n.Text -eq $FolderName -and $null -eq $n.Tag) { return $n }
    }
    $newNode = New-Object System.Windows.Forms.TreeNode($FolderName)
    $newNode.Tag = $null
    [void]$Nodes.Add($newNode)
    return $newNode
}

function Refresh-TreeView {
    $script:TreeView.BeginUpdate()
    try {
        $script:TreeView.Nodes.Clear()
        foreach ($entry in (Get-SortedEntries)) {
            $parts = $entry.FullPath.Split('/')
            $parent = $null
            for ($i = 0; $i -lt $parts.Length; $i++) {
                $part = $parts[$i]
                $isFile = ($i -eq ($parts.Length - 1))
                if ($isFile) {
                    $node = New-Object System.Windows.Forms.TreeNode($part)
                    $node.Tag = $entry.FullPath
                    if ($entry.Modified -or -not $entry.InOriginal) { $node.ForeColor = $script:Theme.ModifiedText }
                    else { $node.ForeColor = $script:Theme.Text }
                    if ($parent) { [void]$parent.Nodes.Add($node) } else { [void]$script:TreeView.Nodes.Add($node) }
                } else {
                    if ($parent) { $parent = Ensure-TreeFolderNode -Nodes $parent.Nodes -FolderName $part }
                    else { $parent = Ensure-TreeFolderNode -Nodes $script:TreeView.Nodes -FolderName $part }
                }
            }
        }
        if (@($script:State.Entries.Keys).Count -le 1000) { $script:TreeView.ExpandAll() }
    } finally {
        $script:TreeView.EndUpdate()
    }
}

function Refresh-AllViews {
    if (-not $script:ListView -or -not $script:TreeView) { return }
    if ($script:State.ViewAsTree) {
        Refresh-TreeView
    } else {
        Refresh-ListView
        Refresh-ListHeaderUI
    }

    $count = @($script:State.Entries.Keys).Count
    $total = 0L
    foreach ($entry in $script:State.Entries.Values) { $total += $entry.Size }

    Update-Status "Entries: $count | Total: $total bytes"
    Set-WindowTitle
}

function Set-ViewMode {
    param([bool]$AsTree)
    $script:State.ViewAsTree = $AsTree
    $script:TreeView.Visible = $AsTree
    $script:ListView.Visible = -not $AsTree
    if ($script:ListBodyPanel) { $script:ListBodyPanel.Visible = -not $AsTree }
    if ($script:ListHeaderPanel) { $script:ListHeaderPanel.Visible = -not $AsTree }
    if ($AsTree) { $script:TreeView.BringToFront() }
    if ($script:State.Entries.Count -gt 0) {
        if ($AsTree) { Refresh-TreeView }
        else {
            Refresh-ListView
            Refresh-ListHeaderUI
        }
    }
}

function Get-SelectedKeys {
    $keys = New-Object System.Collections.Generic.List[string]
    if ($script:State.ViewAsTree) {
        if ($script:TreeView.SelectedNode -and $script:TreeView.SelectedNode.Tag) { $keys.Add([string]$script:TreeView.SelectedNode.Tag) }
        return ,([string[]]$keys.ToArray())
    }

    foreach ($item in $script:ListView.SelectedItems) {
        if ($item.Tag) { $keys.Add([string]$item.Tag) }
    }
    return ,([string[]]$keys.ToArray())
}

function Mark-EntriesClean {
    $newEntries = [ordered]@{}
    foreach ($key in ($script:State.Entries.Keys | Sort-Object)) {
        $e = $script:State.Entries[$key]
        $newEntries[$key] = New-EntryRecord -FullPath $e.FullPath -Data ([byte[]]$e.Data) -InOriginal:$true -Modified:$false
    }
    $script:State.Entries = $newEntries
}

function Save-CurrentBsp {
    param(
        [string]$OutputPath,
        [bool]$InPlace,
        [bool]$CreateBackup = $true
    )
    Ensure-BspLoaded

    $pakBytes = Write-PakEntries -Entries $script:State.Entries
    $result = Apply-PakToBsp -Raw $script:State.BspRaw -Lumps $script:State.Lumps -Version $script:State.BspVersion -MapRevision $script:State.MapRevision -NewPak $pakBytes

    # Crear .bak siempre que se vaya a sobrescribir un archivo existente (in-place
    # o Save As sobre un .bsp ya presente), no solo en modo in-place.
    if ($CreateBackup -and [System.IO.File]::Exists($OutputPath)) {
        [System.IO.File]::Copy($OutputPath, "$OutputPath.bak", $true)
    }

    Write-FileBytesResponsive -Path $OutputPath -Bytes ([byte[]]$result.Raw)
    $script:State.BspRaw = [byte[]]$result.Raw
    $script:State.Lumps = $result.Lumps
    $script:State.CurrentBspPath = $OutputPath
    $script:State.IsDirty = $false
    Mark-EntriesClean
}

function Open-Bsp {
    param([string]$Path)

    if ($script:State.IsDirty) {
        if (-not (Confirm-Dialog -Message 'There are unsaved changes. Continue and discard them?')) { return }
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    Update-Status "Loading BSP: $fullPath"
    Pump-UiMessages -MinMilliseconds 0
    $parsed = Parse-Bsp -Path $fullPath
    Update-Status 'Loading BSP: reading PAK entries...'
    Pump-UiMessages -MinMilliseconds 0
    $entries = Read-PakEntriesFromBspLump -Raw $parsed.Raw -Lumps $parsed.Lumps -Index $PakLumpIndex

    $script:State.CurrentBspPath = $fullPath
    $script:State.BspRaw = [byte[]]$parsed.Raw
    $script:State.BspVersion = $parsed.Version
    $script:State.MapRevision = $parsed.MapRevision
    $script:State.Lumps = $parsed.Lumps
    $script:State.Entries = $entries
    $script:State.IsDirty = $false
    Set-ScanSummary -MissingTotal 0 -CanAdd 0 -NotFound 0 -AlreadyInPak 0
    Refresh-ScanSummaryUI

    $script:PathBox.Text = $fullPath
    Update-Status 'Loading BSP: rendering entry list...'
    Pump-UiMessages -MinMilliseconds 0
    Refresh-AllViews
    Update-Status "BSP loaded: $fullPath"
}

function Resolve-ArchivePathFromFile {
    param([string]$FilePath, [string]$BasePath, [bool]$UseGameRootFixup)

    $source = if ($UseGameRootFixup -and -not [string]::IsNullOrWhiteSpace($script:State.GameRoot)) { $script:State.GameRoot } else { $BasePath }
    $rel = Get-RelativePath -BasePath $source -FullPath $FilePath
    if ($null -eq $rel) { return $null }
    return Normalize-ArchivePath -PathValue ($rel.Replace('\', '/'))
}

function Add-OrReplaceEntry {
    param([string]$ArchivePath, [byte[]]$Data)
    $key = Normalize-ArchivePath -PathValue $ArchivePath
    if ($script:State.Entries.Contains($key)) {
        $old = $script:State.Entries[$key]
        $script:State.Entries[$key] = New-EntryRecord -FullPath $key -Data $Data -InOriginal:$old.InOriginal -Modified:$true
        return 'Replaced'
    }

    $script:State.Entries[$key] = New-EntryRecord -FullPath $key -Data $Data -InOriginal:$false -Modified:$true
    return 'Added'
}

function Collect-FilesFromPaths {
    param([string[]]$Paths)
    $files = New-Object System.Collections.Generic.List[string]

    foreach ($p in $Paths) {
        if (Test-Path -LiteralPath $p -PathType Leaf) {
            $files.Add([System.IO.Path]::GetFullPath($p))
            continue
        }

        if (Test-Path -LiteralPath $p -PathType Container) {
            foreach ($f in (Get-ChildItem -LiteralPath $p -Recurse -File)) {
                $files.Add($f.FullName)
            }
        }
    }

    return @($files | Sort-Object -Unique)
}

function Choose-BaseFolder {
    param([string]$Suggested)
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Select base folder for internal paths'
    if ($Suggested -and (Test-Path $Suggested -PathType Container)) { $dlg.SelectedPath = $Suggested }
    if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return $null }
    return $dlg.SelectedPath
}

function Maybe-UseGameRootFixup {
    param([string[]]$Files)

    if ([string]::IsNullOrWhiteSpace($script:State.GameRoot) -or -not (Test-Path $script:State.GameRoot -PathType Container)) { return $false }

    $insideAny = $false
    foreach ($f in $Files) {
        if ($null -ne (Get-RelativePath -BasePath $script:State.GameRoot -FullPath $f)) {
            $insideAny = $true
            break
        }
    }

    if (-not $insideAny) { return $false }

    switch ($script:State.PathFixupMode) {
        'Always' { return $true }
        'Never' { return $false }
        default { return (Confirm-Dialog -Message 'Apply path fixup relative to Game Path?') }
    }
}

function Add-FilesWithBase {
    param([string[]]$Files, [string]$BasePath, [bool]$UseGameRootFixup)
    Ensure-BspLoaded

    $added = 0
    $replaced = 0
    $skipped = 0

    foreach ($f in $Files) {
        if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { continue }

        $arc = Resolve-ArchivePathFromFile -FilePath $f -BasePath $BasePath -UseGameRootFixup:$UseGameRootFixup
        if ($null -eq $arc) { $skipped++; continue }

        $bytes = Read-FileBytesResponsive -Path $f -MaxBytes $MaxPakEntryBytes -Label $arc
        $kind = Add-OrReplaceEntry -ArchivePath $arc -Data $bytes
        if ($kind -eq 'Added') { $added++ } else { $replaced++ }
    }

    if ($added -gt 0 -or $replaced -gt 0) {
        $script:State.IsDirty = $true
        Refresh-AllViews
    }

    Update-Status "Added: $added | Replaced: $replaced | Skipped: $skipped"
}

function Add-PathsWorkflow {
    param([string[]]$RawPaths)
    Ensure-BspLoaded

    $files = Collect-FilesFromPaths -Paths $RawPaths
    if ($files.Count -eq 0) { return }

    $suggestBase = [System.IO.Path]::GetDirectoryName($files[0])
    $base = Choose-BaseFolder -Suggested $suggestBase
    if ($null -eq $base) { return }

    $useFixup = Maybe-UseGameRootFixup -Files $files
    Add-FilesWithBase -Files $files -BasePath $base -UseGameRootFixup:$useFixup
}

function Remove-SelectedEntries {
    Ensure-BspLoaded
    $keys = Get-SelectedKeys
    if (@($keys).Count -eq 0) { return }
    if (-not (Confirm-Dialog -Message 'Delete selected files from the PAK?')) { return }

    $removed = 0
    foreach ($k in $keys) {
        if ($script:State.Entries.Contains($k)) {
            [void]$script:State.Entries.Remove($k)
            $removed++
        }
    }

    if ($removed -gt 0) { $script:State.IsDirty = $true }
    Refresh-AllViews
    Update-Status "Removed: $removed"
}

function Format-HexPreview {
    param([byte[]]$Data)
    $max = [Math]::Min($Data.Length, 65536)
    $sb = New-Object System.Text.StringBuilder

    for ($i = 0; $i -lt $max; $i += 16) {
        $lineLen = [Math]::Min(16, $max - $i)
        $hex = New-Object System.Text.StringBuilder
        $ascii = New-Object System.Text.StringBuilder

        for ($j = 0; $j -lt $lineLen; $j++) {
            $b = $Data[$i + $j]
            [void]$hex.AppendFormat('{0:X2} ', $b)
            if ($b -ge 32 -and $b -le 126) { [void]$ascii.Append([char]$b) } else { [void]$ascii.Append('.') }
        }

        [void]$sb.AppendFormat('{0:X8}  {1,-48} {2}`r`n', $i, $hex.ToString(), $ascii.ToString())
    }

    if ($Data.Length -gt $max) { [void]$sb.Append("`r`n[Preview truncated. Total bytes: $($Data.Length)]") }
    return $sb.ToString()
}

function Show-EntryViewer {
    Ensure-BspLoaded
    $keys = Get-SelectedKeys
    if (@($keys).Count -ne 1) { Show-InfoDialog -Message 'Select exactly one file to view.'; return }

    $entry = $script:State.Entries[$keys[0]]
    $bytes = [byte[]]$entry.Data
    $textLike = @('.txt', '.cfg', '.vmt', '.vmf', '.res', '.log', '.ini', '.kv', '.json', '.xml', '.nut')
    $ext = [System.IO.Path]::GetExtension($entry.Name).ToLowerInvariant()

    if ($textLike -contains $ext) {
        try { $content = [System.Text.Encoding]::UTF8.GetString($bytes) }
        catch { $content = [System.Text.Encoding]::Default.GetString($bytes) }
    } else {
        $content = Format-HexPreview -Data $bytes
    }

    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = "View: $($entry.FullPath)"
    $dlg.StartPosition = 'CenterParent'
    $dlg.Width = 900
    $dlg.Height = 640

    $txt = New-Object System.Windows.Forms.TextBox
    $txt.Multiline = $true
    $txt.ScrollBars = 'Both'
    $txt.ReadOnly = $true
    $txt.WordWrap = $false
    $txt.Dock = 'Fill'
    $txt.Font = New-Object System.Drawing.Font('Consolas', 9)
    $txt.Text = $content
    $dlg.Controls.Add($txt)

    [void](Show-DarkDialog -Dialog $dlg)
}
function Edit-SelectedEntry {
    Ensure-BspLoaded
    $keys = Get-SelectedKeys
    if (@($keys).Count -ne 1) { Show-InfoDialog -Message 'Select exactly one file to edit name/path.'; return }

    $oldKey = $keys[0]
    if (-not $script:State.Entries.Contains($oldKey)) { return }
    $entry = $script:State.Entries[$oldKey]

    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = 'Edit file'
    $dlg.StartPosition = 'CenterParent'
    $dlg.Width = 520
    $dlg.Height = 190
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.MaximizeBox = $false
    $dlg.MinimizeBox = $false

    $lblName = New-Object System.Windows.Forms.Label
    $lblName.Text = 'Name:'
    $lblName.Left = 12
    $lblName.Top = 18
    $lblName.Width = 70
    $dlg.Controls.Add($lblName)

    $txtName = New-Object System.Windows.Forms.TextBox
    $txtName.Left = 90
    $txtName.Top = 14
    $txtName.Width = 400
    $txtName.Text = $entry.Name
    $dlg.Controls.Add($txtName)

    $lblPath = New-Object System.Windows.Forms.Label
    $lblPath.Text = 'Path:'
    $lblPath.Left = 12
    $lblPath.Top = 54
    $lblPath.Width = 70
    $dlg.Controls.Add($lblPath)

    $txtPath = New-Object System.Windows.Forms.TextBox
    $txtPath.Left = 90
    $txtPath.Top = 50
    $txtPath.Width = 400
    $txtPath.Text = $entry.Directory
    $dlg.Controls.Add($txtPath)

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = 'OK'
    $ok.Left = 334
    $ok.Top = 98
    $ok.Width = 75
    $ok.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $dlg.Controls.Add($ok)

    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = 'Cancel'
    $cancel.Left = 415
    $cancel.Top = 98
    $cancel.Width = 75
    $cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $dlg.Controls.Add($cancel)

    $dlg.AcceptButton = $ok
    $dlg.CancelButton = $cancel

    if ((Show-DarkDialog -Dialog $dlg) -ne [System.Windows.Forms.DialogResult]::OK) { return }

    $newName = $txtName.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($newName)) { Show-ErrorDialog -Message 'Name cannot be empty.'; return }

    $newPath = $txtPath.Text.Trim().Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($newPath)) { $combined = $newName } else { $combined = "$newPath/$newName" }

    try { $newKey = Normalize-ArchivePath -PathValue $combined }
    catch { Show-ErrorDialog -Message $_.Exception.Message; return }

    if ($newKey -ne $oldKey -and $script:State.Entries.Contains($newKey)) { Show-ErrorDialog -Message 'Another file already exists with that path.'; return }

    [void]$script:State.Entries.Remove($oldKey)
    $script:State.Entries[$newKey] = New-EntryRecord -FullPath $newKey -Data ([byte[]]$entry.Data) -InOriginal:$entry.InOriginal -Modified:$true
    $script:State.IsDirty = $true
    Refresh-AllViews
    Update-Status "Renamed: $oldKey -> $newKey"
}

function Write-EntryToDisk {
    param([string]$TargetRoot, [string]$EntryName, [byte[]]$Data)

    $relative = To-OsPath $EntryName
    $root = [System.IO.Path]::GetFullPath($TargetRoot)
    $targetPath = [System.IO.Path]::GetFullPath((Join-Path $root $relative))
    $rootPrefix = if ($root.EndsWith([System.IO.Path]::DirectorySeparatorChar)) { $root } else { $root + [System.IO.Path]::DirectorySeparatorChar }
    if (-not $targetPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe extraction path: $EntryName" }

    $parent = [System.IO.Path]::GetDirectoryName($targetPath)
    if (-not [string]::IsNullOrEmpty($parent)) { [System.IO.Directory]::CreateDirectory($parent) | Out-Null }
    [System.IO.File]::WriteAllBytes($targetPath, $Data)
}

function Extract-SelectedEntries {
    Ensure-BspLoaded
    $keys = Get-SelectedKeys
    if (@($keys).Count -eq 0) { return }

    if (@($keys).Count -eq 1) {
        $entry = $script:State.Entries[$keys[0]]
        $dlg = New-Object System.Windows.Forms.SaveFileDialog
        $dlg.FileName = $entry.Name
        $dlg.Filter = 'All files (*.*)|*.*'
        if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }

        [System.IO.File]::WriteAllBytes($dlg.FileName, [byte[]]$entry.Data)
        Update-Status "Extracted: $($entry.FullPath)"
        return
    }

    $folderDlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $folderDlg.Description = 'Select destination folder for extraction'
    if ($folderDlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }

    $count = 0
    foreach ($k in $keys) {
        $entry = $script:State.Entries[$k]
        Write-EntryToDisk -TargetRoot $folderDlg.SelectedPath -EntryName $entry.FullPath -Data ([byte[]]$entry.Data)
        $count++
    }

    Update-Status "Extracted: $count"
}

function Normalize-GameRef {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $v = $Value.Trim().Trim('"').Replace('\', '/')
    while ($v.StartsWith('/')) { $v = $v.Substring(1) }
    if ([string]::IsNullOrWhiteSpace($v) -or $v.Contains('..') -or $v.Contains(':')) { return $null }

    $ext = [System.IO.Path]::GetExtension($v).ToLowerInvariant()
    switch ($ext) {
        '.vmt' { if ($v -notmatch '^(?i)materials/') { "materials/$v" } else { $v } }
        '.vtf' { if ($v -notmatch '^(?i)materials/') { "materials/$v" } else { $v } }
        '.mdl' { if ($v -notmatch '^(?i)models/') { "models/$v" } else { $v } }
        '.vvd' { if ($v -notmatch '^(?i)models/') { "models/$v" } else { $v } }
        '.vtx' { if ($v -notmatch '^(?i)models/') { "models/$v" } else { $v } }
        '.phy' { if ($v -notmatch '^(?i)models/') { "models/$v" } else { $v } }
        '.wav' { if ($v -notmatch '^(?i)sound/') { "sound/$v" } else { $v } }
        '.mp3' { if ($v -notmatch '^(?i)sound/') { "sound/$v" } else { $v } }
        default { $v }
    }
}

function Add-RefToSet {
    param([System.Collections.Generic.HashSet[string]]$Set, [string]$Ref)
    $norm = Normalize-GameRef -Value $Ref
    if ($null -ne $norm) { [void]$Set.Add($norm) }
}

function Add-ModelWithCompanions {
    param([System.Collections.Generic.HashSet[string]]$Set, [string]$ModelPath)
    if ([string]::IsNullOrWhiteSpace($ModelPath)) { return }
    $candidate = $ModelPath.Trim().Trim('"').Replace('\', '/').Trim('/')
    if ([string]::IsNullOrWhiteSpace($candidate)) { return }
    if ([System.IO.Path]::GetExtension($candidate) -eq '') { $candidate += '.mdl' }

    $mdl = Normalize-GameRef -Value $candidate
    if ($null -eq $mdl) { return }
    [void]$Set.Add($mdl)
    $base = ([System.IO.Path]::ChangeExtension($mdl, $null)).TrimEnd('.')
    if ([string]::IsNullOrWhiteSpace($base)) { return }
    foreach ($suffix in @('.vvd', '.phy', '.dx80.vtx', '.dx90.vtx', '.sw.vtx')) {
        [void]$Set.Add("${base}${suffix}")
    }
}

function Add-ModelReference {
    param([System.Collections.Generic.HashSet[string]]$Set, [string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return }
    $candidate = $Value.Trim().Trim('"').Replace('\', '/').Trim('/')
    if ([string]::IsNullOrWhiteSpace($candidate) -or $candidate.StartsWith('*')) { return }
    if ($candidate -match '^[+-]?\d+$') { return }
    if ($candidate.Contains('..') -or $candidate.Contains(':')) { return }

    $ext = [System.IO.Path]::GetExtension($candidate).ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($ext)) {
        Add-ModelWithCompanions -Set $Set -ModelPath $candidate
    } elseif ($ext -eq '.mdl') {
        Add-ModelWithCompanions -Set $Set -ModelPath $candidate
    }
}

function Read-NullTerminatedString {
    param([byte[]]$Bytes, [int]$Offset)
    if ($Offset -lt 0 -or $Offset -ge $Bytes.Length) { return '' }
    $end = $Offset
    while ($end -lt $Bytes.Length -and $Bytes[$end] -ne 0) { $end++ }
    if ($end -le $Offset) { return '' }
    return [System.Text.Encoding]::ASCII.GetString($Bytes, $Offset, ($end - $Offset))
}

function Read-NullTerminatedStringFromBytes {
    param([byte[]]$Bytes)
    if ($null -eq $Bytes -or $Bytes.Length -eq 0) { return '' }

    $end = 0
    while ($end -lt $Bytes.Length -and $Bytes[$end] -ne 0) { $end++ }
    if ($end -le 0) { return '' }
    return [System.Text.Encoding]::ASCII.GetString($Bytes, 0, $end)
}

function Read-NullTerminatedStringAt {
    param([byte[]]$Bytes, [int]$Offset)

    if ($null -eq $Bytes -or $Offset -lt 0 -or $Offset -ge $Bytes.Length) { return '' }
    $end = $Offset
    while ($end -lt $Bytes.Length -and $Bytes[$end] -ne 0) { $end++ }
    if ($end -le $Offset) { return '' }
    return [System.Text.Encoding]::ASCII.GetString($Bytes, $Offset, ($end - $Offset))
}

function Read-Int32LeSafe {
    param([byte[]]$Bytes, [int]$Offset)

    if ($null -eq $Bytes -or $Offset -lt 0 -or ($Offset + 4) -gt $Bytes.Length) { return $null }
    return [BitConverter]::ToInt32($Bytes, $Offset)
}

function Add-MaterialRefToList {
    param([System.Collections.Generic.List[string]]$Refs, [string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return }
    $candidate = $Value.Trim().Trim('"').Replace('\', '/').Trim('/')
    if ([string]::IsNullOrWhiteSpace($candidate) -or $candidate.Contains('..') -or $candidate.Contains(':')) { return }

    $ext = [System.IO.Path]::GetExtension($candidate).ToLowerInvariant()
    if ($ext -eq '.vtf') {
        $candidate = [System.IO.Path]::ChangeExtension($candidate, '.vmt').Replace('\', '/')
    } elseif ($ext -ne '.vmt') {
        $candidate += '.vmt'
    }

    $ref = Normalize-GameRef -Value $candidate
    if ($null -eq $ref) { return }
    if ($ref -notmatch '^(?i)materials/') { $ref = "materials/$ref" }
    [void]$Refs.Add($ref)
}

function Join-ModelMaterialPath {
    param([string]$Directory, [string]$TextureName)

    if ([string]::IsNullOrWhiteSpace($TextureName)) { return $null }
    $texture = $TextureName.Trim().Trim('"').Replace('\', '/').Trim('/')
    if ([string]::IsNullOrWhiteSpace($texture) -or $texture.Contains('..') -or $texture.Contains(':')) { return $null }
    if ($texture -match '^(?i)materials/') { return $texture }
    if ($texture -match '/') { return $texture }

    $dir = ''
    if (-not [string]::IsNullOrWhiteSpace($Directory)) {
        $dir = $Directory.Trim().Trim('"').Replace('\', '/').Trim('/')
        if ($dir -match '^(?i)materials/(.+)$') { $dir = $Matches[1] }
    }
    if ([string]::IsNullOrWhiteSpace($dir)) { return $texture }
    return "$dir/$texture"
}

function Get-MdlMaterialRefsFromBytes {
    param([byte[]]$Bytes)

    $refs = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Bytes -or $Bytes.Length -lt 220) { return @() }

    $id = [System.Text.Encoding]::ASCII.GetString($Bytes, 0, 4)
    if ($id -ne 'IDST') { return @() }

    $numTextures = Read-Int32LeSafe -Bytes $Bytes -Offset 204
    $textureIndex = Read-Int32LeSafe -Bytes $Bytes -Offset 208
    $numCdTextures = Read-Int32LeSafe -Bytes $Bytes -Offset 212
    $cdTextureIndex = Read-Int32LeSafe -Bytes $Bytes -Offset 216

    if ($null -eq $numTextures -or $null -eq $textureIndex -or $numTextures -lt 0 -or $numTextures -gt 4096) { return @() }
    if ($null -eq $numCdTextures -or $null -eq $cdTextureIndex -or $numCdTextures -lt 0 -or $numCdTextures -gt 1024) { $numCdTextures = 0 }

    $textureNames = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $numTextures; $i++) {
        $textureStruct = [int]$textureIndex + ($i * 64)
        if ($textureStruct -lt 0 -or ($textureStruct + 64) -gt $Bytes.Length) { break }
        $nameOffset = Read-Int32LeSafe -Bytes $Bytes -Offset $textureStruct
        if ($null -eq $nameOffset) { continue }
        $absoluteNameOffset = $textureStruct + [int]$nameOffset
        $name = Read-NullTerminatedStringAt -Bytes $Bytes -Offset $absoluteNameOffset
        if (-not [string]::IsNullOrWhiteSpace($name)) { [void]$textureNames.Add($name) }
    }

    if ($textureNames.Count -eq 0) { return @() }

    $textureDirs = New-Object System.Collections.Generic.List[string]
    if ($numCdTextures -gt 0 -and $cdTextureIndex -gt 0) {
        for ($i = 0; $i -lt $numCdTextures; $i++) {
            $offsetOffset = [int]$cdTextureIndex + ($i * 4)
            $dirOffset = Read-Int32LeSafe -Bytes $Bytes -Offset $offsetOffset
            if ($null -eq $dirOffset) { continue }
            $dir = Read-NullTerminatedStringAt -Bytes $Bytes -Offset ([int]$dirOffset)
            if (-not [string]::IsNullOrWhiteSpace($dir)) { [void]$textureDirs.Add($dir) }
        }
    }
    if ($textureDirs.Count -eq 0) { [void]$textureDirs.Add('') }

    foreach ($textureName in $textureNames) {
        foreach ($textureDir in $textureDirs) {
            $materialPath = Join-ModelMaterialPath -Directory $textureDir -TextureName $textureName
            Add-MaterialRefToList -Refs $refs -Value $materialPath
        }
    }

    return @([string[]]$refs.ToArray())
}

function Add-StaticPropModelReferences {
    param([byte[]]$Raw, [object[]]$Lumps, [System.Collections.Generic.HashSet[string]]$Set)

    if ($null -eq $Raw -or $null -eq $Lumps -or $Lumps.Count -le $GameLumpIndex) { return }
    $gameLump = $Lumps[$GameLumpIndex]
    if ($gameLump.filelen -lt 4) { return }

    $gameStart = [int]$gameLump.fileofs
    $gameEnd = $gameStart + [int]$gameLump.filelen
    if ($gameStart -lt 0 -or $gameEnd -gt $Raw.Length) { return }

    $ms = [System.IO.MemoryStream]::new($Raw, $gameStart, [int]$gameLump.filelen, $false)
    $br = New-Object System.IO.BinaryReader($ms)
    try {
        $lumpCount = $br.ReadInt32()
        if ($lumpCount -lt 0 -or $lumpCount -gt 1024) { return }

        for ($i = 0; $i -lt $lumpCount; $i++) {
            if (($ms.Length - $ms.Position) -lt 16) { return }
            $id = $br.ReadUInt32()
            [void]$br.ReadUInt16()
            [void]$br.ReadUInt16()
            $fileOfs = $br.ReadInt32()
            $fileLen = $br.ReadInt32()

            if ($id -ne $StaticPropGameLumpId) { continue }
            if ($fileLen -lt 4 -or $fileOfs -lt 0 -or ($fileOfs + $fileLen) -gt $Raw.Length) { continue }

            $sprpStream = [System.IO.MemoryStream]::new($Raw, $fileOfs, $fileLen, $false)
            $sprpReader = New-Object System.IO.BinaryReader($sprpStream)
            try {
                $dictCount = $sprpReader.ReadInt32()
                if ($dictCount -lt 0 -or $dictCount -gt 10000) { continue }

                for ($d = 0; $d -lt $dictCount; $d++) {
                    if (($sprpStream.Length - $sprpStream.Position) -lt 128) { break }
                    $nameBytes = $sprpReader.ReadBytes(128)
                    $modelName = Read-NullTerminatedStringFromBytes -Bytes $nameBytes
                    Add-ModelReference -Set $Set -Value $modelName
                }
            } finally {
                $sprpReader.Dispose()
                $sprpStream.Dispose()
            }
        }
    } catch {
        return
    } finally {
        $br.Dispose()
        $ms.Dispose()
    }
}

function Collect-BspReferences {
    param([byte[]]$Raw, [object[]]$Lumps, [string]$MapName, [bool]$IncludeExtras)

    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

    $entitiesBytes = Get-LumpBytes -Raw $Raw -Lumps $Lumps -Index $EntitiesLumpIndex
    if ($entitiesBytes.Length -gt 0) {
        $entityText = [System.Text.Encoding]::ASCII.GetString($entitiesBytes).Replace([char]0, "`n")
        $pairs = [regex]::Matches($entityText, '"([^"]*)"\s*"([^"]*)"')
        $pairIndex = 0
        foreach ($m in $pairs) {
            if (($pairIndex % 256) -eq 0) { Pump-UiMessages }
            $pairIndex++
            $key = $m.Groups[1].Value.ToLowerInvariant().Trim()
            $val = $m.Groups[2].Value.Trim()
            if ([string]::IsNullOrWhiteSpace($val)) { continue }

            switch ($key) {
                'model' { Add-ModelReference -Set $set -Value $val }
                'gibmodel' { Add-ModelReference -Set $set -Value $val }
                'detailmaterial' {
                    Add-MaterialReference -Set $set -Value $val
                }
                'skyname' {
                    $sky = $val.Replace('\', '/').Trim('/')
                    if (-not [string]::IsNullOrWhiteSpace($sky)) {
                        foreach ($side in @('up', 'dn', 'lf', 'rt', 'ft', 'bk')) {
                            Add-RefToSet -Set $set -Ref ("materials/skybox/${sky}${side}.vmt")
                            Add-RefToSet -Set $set -Ref ("materials/skybox/${sky}${side}.vtf")
                        }
                    }
                }
                default {
                    if ($key -match '(?i)(texture|decal|overlay|material|sprite)' -and $val -match '[\\/]') {
                        Add-MaterialReference -Set $set -Value $val
                    }
                    $matches = [regex]::Matches($val, '([A-Za-z0-9_\-\/\.]+\.(vmt|vtf|mdl|wav|mp3|pcf|txt|vcd))', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
                    foreach ($mv in $matches) {
                        $candidate = $mv.Groups[1].Value
                        if ([System.IO.Path]::GetExtension($candidate).ToLowerInvariant() -eq '.mdl') { Add-ModelWithCompanions -Set $set -ModelPath $candidate }
                        else { Add-RefToSet -Set $set -Ref $candidate }
                    }
                }
            }
        }
    }

    Add-StaticPropModelReferences -Raw $Raw -Lumps $Lumps -Set $set

    $strData = Get-LumpBytes -Raw $Raw -Lumps $Lumps -Index $TexDataStringDataLumpIndex
    $strTable = Get-LumpBytes -Raw $Raw -Lumps $Lumps -Index $TexDataStringTableLumpIndex
    if ($strData.Length -gt 0 -and $strTable.Length -gt 0) {
        for ($i = 0; $i -le ($strTable.Length - 4); $i += 4) {
            if ((($i / 4) % 256) -eq 0) { Pump-UiMessages }
            $off = [BitConverter]::ToInt32($strTable, $i)
            if ($off -lt 0 -or $off -ge $strData.Length) { continue }
            $mat = Read-NullTerminatedString -Bytes $strData -Offset $off
            if ([string]::IsNullOrWhiteSpace($mat)) { continue }
            if ($mat -match '\.vmt$') { Add-RefToSet -Set $set -Ref ("materials/$mat") }
            else { Add-RefToSet -Set $set -Ref ("materials/$mat.vmt") }
        }
    }

    if ($IncludeExtras -and -not [string]::IsNullOrWhiteSpace($MapName)) {
        Add-RefToSet -Set $set -Ref ("maps/$MapName.nav")
        Add-RefToSet -Set $set -Ref ("maps/$MapName.txt")
        Add-RefToSet -Set $set -Ref ("resource/overviews/$MapName.txt")
        Add-RefToSet -Set $set -Ref ("resource/overviews/$MapName.dds")
        Add-RefToSet -Set $set -Ref ("resource/overviews/${MapName}_radar.dds")
        Add-RefToSet -Set $set -Ref ("materials/overviews/$MapName.vmt")
        Add-RefToSet -Set $set -Ref ("materials/overviews/$MapName.vtf")
        Add-RefToSet -Set $set -Ref ("materials/overviews/${MapName}_radar.vmt")
        Add-RefToSet -Set $set -Ref ("materials/overviews/${MapName}_radar.vtf")
    }

    return ,$set
}

function Add-VmtDependencyRef {
    param(
        [System.Collections.Generic.List[string]]$Refs,
        [string]$Value,
        [string]$Extension
    )

    if ([string]::IsNullOrWhiteSpace($Value)) { return }
    $candidate = $Value.Trim()
    if ($candidate -match '^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$') { return }
    if ($candidate -match '^(?i)(env_cubemap|none|null|black|white)$') { return }
    if ($candidate -notmatch ([regex]::Escape($Extension) + '$')) { $candidate += $Extension }
    $ref = Normalize-GameRef -Value $candidate
    if ($null -eq $ref) { return }
    if ($ref -notmatch '^(?i)materials/') { $ref = "materials/$ref" }
    [void]$Refs.Add($ref)
}

function Add-MaterialReference {
    param([System.Collections.Generic.HashSet[string]]$Set, [string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return }
    $candidate = $Value.Trim().Trim('"').Replace('\', '/').Trim('/')
    if ([string]::IsNullOrWhiteSpace($candidate) -or $candidate.Contains('..') -or $candidate.Contains(':')) { return }

    $ext = [System.IO.Path]::GetExtension($candidate).ToLowerInvariant()
    if ($candidate -match '^(?i)materials/(.+)$') { $candidate = $Matches[1] }

    switch ($ext) {
        '.vmt' { Add-RefToSet -Set $Set -Ref ("materials/$candidate") }
        '.vtf' { Add-RefToSet -Set $Set -Ref ("materials/$candidate") }
        default {
            Add-RefToSet -Set $Set -Ref ("materials/$candidate.vmt")
        }
    }
}

function Get-RegexFirstValue {
    param([System.Text.RegularExpressions.Match]$Match, [int[]]$GroupIndexes)
    foreach ($index in $GroupIndexes) {
        if ($Match.Groups[$index].Success) { return $Match.Groups[$index].Value }
    }
    return ''
}

function Test-VmtPathLikeValue {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    $candidate = $Value.Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($candidate)) { return $false }
    if ($candidate -match '^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$') { return $false }
    if ($candidate -match '^[\{\[]?[+-]?\d+(?:\.\d+)?(?:\s+[+-]?\d+(?:\.\d+)?)+[\}\]]?$') { return $false }
    if ($candidate -match '^(?i)(env_cubemap|none|null|black|white)$') { return $false }
    if ($candidate -match '^(?i)[A-Za-z_][A-Za-z0-9_]*$') { return $false }
    return ($candidate -match '[\\/]' -or $candidate -match '(?i)\.(vmt|vtf)$')
}

function Get-VmtDependencyRefsFromContent {
    param([string]$Content)

    if ([string]::IsNullOrWhiteSpace($Content)) { return @() }

    $refs = New-Object System.Collections.Generic.List[string]
    $textureKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($k in @(
        'basetexture',
        'basetexture2',
        'texture2',
        'bumpmap',
        'bumpmap2',
        'normalmap',
        'envmapmask',
        'detail',
        'envmap',
        'selfillummask',
        'selfillumtexture',
        'flowmap',
        'dudvmap',
        'lightwarptexture',
        'phongexponenttexture',
        'iris',
        'blendmodulatetexture',
        'ambientocclusiontexture',
        'flashlighttexture',
        'refracttexture',
        'reflecttexture',
        'blurtexture',
        'normalmapalphaenvmapmask',
        'basetexturenoenvmap'
    )) {
        [void]$textureKeys.Add($k)
    }

    $includes = [regex]::Matches($Content, '(?im)^\s*"?include"?\s+(?:"([^"]+)"|([^\s{}]+))')
    foreach ($m in $includes) {
        Add-VmtDependencyRef -Refs $refs -Value (Get-RegexFirstValue -Match $m -GroupIndexes @(1, 2)) -Extension '.vmt'
    }

    $tex = [regex]::Matches($Content, '(?im)^\s*"?\$([A-Za-z0-9_]+)"?\s+(?:"([^"]+)"|([^\s{}]+))')
    foreach ($m in $tex) {
        $key = $m.Groups[1].Value.ToLowerInvariant()
        $value = Get-RegexFirstValue -Match $m -GroupIndexes @(2, 3)
        if ($textureKeys.Contains($key)) {
            Add-VmtDependencyRef -Refs $refs -Value $value -Extension '.vtf'
        } elseif ($key -eq 'bottommaterial' -or $key -eq 'crackmaterial') {
            Add-VmtDependencyRef -Refs $refs -Value $value -Extension '.vmt'
        } elseif (Test-VmtPathLikeValue -Value $value) {
            $extension = [System.IO.Path]::GetExtension($value).ToLowerInvariant()
            if ($extension -eq '.vmt') {
                Add-VmtDependencyRef -Refs $refs -Value $value -Extension '.vmt'
            } else {
                Add-VmtDependencyRef -Refs $refs -Value $value -Extension '.vtf'
            }
        }
    }

    return @([string[]]$refs.ToArray())
}

function Get-VmtDependencyRefs {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return @() }

    try {
        $info = New-Object System.IO.FileInfo($Path)
        if (-not $info.Exists) { return @() }
    } catch {
        return @()
    }

    $fullPath = $info.FullName
    if ($script:VmtDependencyCache.ContainsKey($fullPath)) {
        $cached = $script:VmtDependencyCache[$fullPath]
        if ($cached.Length -eq $info.Length -and $cached.LastWriteUtcTicks -eq $info.LastWriteTimeUtc.Ticks) {
            return @($cached.Refs)
        }
    }

    try { $content = [System.IO.File]::ReadAllText($fullPath) }
    catch { return @() }

    $refs = [string[]](Get-VmtDependencyRefsFromContent -Content $content)

    $result = [string[]]$refs
    $script:VmtDependencyCache[$fullPath] = [pscustomobject]@{
        Length = [int64]$info.Length
        LastWriteUtcTicks = [int64]$info.LastWriteTimeUtc.Ticks
        Refs = $result
    }
    return @($result)
}

function Get-PakTextEntry {
    param([string]$ArchivePath)

    if ([string]::IsNullOrWhiteSpace($ArchivePath) -or -not $script:State.Entries.Contains($ArchivePath)) { return $null }
    $entry = $script:State.Entries[$ArchivePath]
    if ($null -eq $entry -or $null -eq $entry.Data) { return $null }

    $bytes = [byte[]]$entry.Data
    if ($bytes.Length -eq 0 -or $bytes.Length -gt 1048576) { return $null }
    foreach ($b in $bytes) {
        if ($b -eq 0) { return $null }
    }

    try { return [System.Text.Encoding]::UTF8.GetString($bytes) }
    catch {
        try { return [System.Text.Encoding]::ASCII.GetString($bytes) }
        catch { return $null }
    }
}

function Get-PakBinaryEntry {
    param([string]$ArchivePath)

    if ([string]::IsNullOrWhiteSpace($ArchivePath) -or -not $script:State.Entries.Contains($ArchivePath)) { return $null }
    $entry = $script:State.Entries[$ArchivePath]
    if ($null -eq $entry -or $null -eq $entry.Data) { return $null }
    return [byte[]]$entry.Data
}

function Get-MdlMaterialRefs {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return @() }

    try {
        $info = New-Object System.IO.FileInfo($Path)
        if (-not $info.Exists) { return @() }
        if ($info.Length -le 0 -or $info.Length -gt $MaxPakEntryBytes) { return @() }
    } catch {
        return @()
    }

    $fullPath = $info.FullName
    $cacheKey = "mdl::$fullPath"
    if ($script:VmtDependencyCache.ContainsKey($cacheKey)) {
        $cached = $script:VmtDependencyCache[$cacheKey]
        if ($cached.Length -eq $info.Length -and $cached.LastWriteUtcTicks -eq $info.LastWriteTimeUtc.Ticks) {
            return @($cached.Refs)
        }
    }

    try { $bytes = Read-FileBytesResponsive -Path $fullPath -MaxBytes $MaxPakEntryBytes -Label $fullPath }
    catch { return @() }

    $refs = [string[]](Get-MdlMaterialRefsFromBytes -Bytes $bytes)
    $script:VmtDependencyCache[$cacheKey] = [pscustomobject]@{
        Length = [int64]$info.Length
        LastWriteUtcTicks = [int64]$info.LastWriteTimeUtc.Ticks
        Refs = $refs
    }
    return @($refs)
}

function Expand-ModelMaterialDependencies {
    param([System.Collections.Generic.HashSet[string]]$Set, [string]$GameRoot)
    if ($null -eq $Set) { return }

    $modelRefs = @($Set | Where-Object { [System.IO.Path]::GetExtension([string]$_).ToLowerInvariant() -eq '.mdl' })
    foreach ($modelRef in $modelRefs) {
        Pump-UiMessages
        $materialRefs = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

        if (-not [string]::IsNullOrWhiteSpace($GameRoot) -and [System.IO.Directory]::Exists($GameRoot)) {
            $modelPath = Join-Path $GameRoot (To-OsPath $modelRef)
            foreach ($matRef in (Get-MdlMaterialRefs -Path $modelPath)) {
                if (-not [string]::IsNullOrWhiteSpace($matRef)) { [void]$materialRefs.Add($matRef) }
            }
        }

        $pakModel = Get-PakBinaryEntry -ArchivePath $modelRef
        if ($null -ne $pakModel) {
            foreach ($matRef in (Get-MdlMaterialRefsFromBytes -Bytes $pakModel)) {
                if (-not [string]::IsNullOrWhiteSpace($matRef)) { [void]$materialRefs.Add($matRef) }
            }
        }

        foreach ($matRef in $materialRefs) {
            [void]$Set.Add($matRef)
        }
    }
}

function Expand-VmtDependencies {
    param([System.Collections.Generic.HashSet[string]]$Set, [string]$GameRoot)
    if ([string]::IsNullOrWhiteSpace($GameRoot) -or -not [System.IO.Directory]::Exists($GameRoot)) { return }

    $queue = New-Object 'System.Collections.Generic.Queue[string]'
    $visited = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($ref in $Set) { if ([System.IO.Path]::GetExtension($ref).ToLowerInvariant() -eq '.vmt') { $queue.Enqueue($ref) } }

    while ($queue.Count -gt 0) {
        Pump-UiMessages
        $vmtRef = $queue.Dequeue()
        if (-not $visited.Add($vmtRef)) { continue }

        $vmtPath = Join-Path $GameRoot (To-OsPath $vmtRef)
        $depRefs = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($depRef in (Get-VmtDependencyRefs -Path $vmtPath)) {
            if (-not [string]::IsNullOrWhiteSpace($depRef)) { [void]$depRefs.Add($depRef) }
        }
        $pakVmt = Get-PakTextEntry -ArchivePath $vmtRef
        if (-not [string]::IsNullOrWhiteSpace($pakVmt)) {
            foreach ($depRef in (Get-VmtDependencyRefsFromContent -Content $pakVmt)) {
                if (-not [string]::IsNullOrWhiteSpace($depRef)) { [void]$depRefs.Add($depRef) }
            }
        }

        $sameBaseVtf = [System.IO.Path]::ChangeExtension($vmtRef, '.vtf').Replace('\', '/')
        if (-not [string]::IsNullOrWhiteSpace($sameBaseVtf)) {
            $sameBaseDiskPath = Join-Path $GameRoot (To-OsPath $sameBaseVtf)
            if ([System.IO.File]::Exists($sameBaseDiskPath) -or $script:State.Entries.Contains($sameBaseVtf)) {
                [void]$depRefs.Add($sameBaseVtf)
            }
        }

        foreach ($depRef in $depRefs) {
            if ([string]::IsNullOrWhiteSpace($depRef)) { continue }
            if ($Set.Add($depRef) -and [System.IO.Path]::GetExtension($depRef).ToLowerInvariant() -eq '.vmt') {
                $queue.Enqueue($depRef)
            }
        }
    }
}

function Ensure-GameRoot {
    if ($script:GameRootBox -and -not [string]::IsNullOrWhiteSpace($script:GameRootBox.Text)) {
        [void](Set-CurrentGameRoot -PathValue $script:GameRootBox.Text -Persist:$false)
    }

    if (-not [string]::IsNullOrWhiteSpace($script:State.GameRoot) -and (Test-Path $script:State.GameRoot -PathType Container)) {
        return $script:State.GameRoot
    }

    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Select Game Path folder (hl2/cstrike/etc)'
    if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return $null }

    if (-not (Set-CurrentGameRoot -PathValue $dlg.SelectedPath -Persist:$true)) {
        return $null
    }
    return $script:State.GameRoot
}

function Invoke-Scan {
    Ensure-BspLoaded
    $gameRoot = Ensure-GameRoot
    if ($null -eq $gameRoot) { return $null }

    $mapName = if ($script:State.CurrentBspPath) { [System.IO.Path]::GetFileNameWithoutExtension($script:State.CurrentBspPath) } else { '' }
    Update-Status 'Scan: reading BSP references...'
    Pump-UiMessages -MinMilliseconds 0
    $refs = Collect-BspReferences -Raw $script:State.BspRaw -Lumps $script:State.Lumps -MapName $mapName -IncludeExtras:$script:State.IncludeExtrasInScan
    Update-Status 'Scan: expanding model materials...'
    Pump-UiMessages -MinMilliseconds 0
    Expand-ModelMaterialDependencies -Set $refs -GameRoot $gameRoot
    Update-Status 'Scan: expanding material dependencies...'
    Pump-UiMessages -MinMilliseconds 0
    Expand-VmtDependencies -Set $refs -GameRoot $gameRoot
    Update-Status 'Scan: checking base game VPKs...'
    Pump-UiMessages -MinMilliseconds 0
    $baseVpkEntries = Get-BaseVpkEntries -GameRoot $gameRoot -NeededRefs $refs
    $optionalExtras = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    if ($script:State.IncludeExtrasInScan -and -not [string]::IsNullOrWhiteSpace($mapName)) {
        foreach ($optionalRef in @(
            "maps/$mapName.nav",
            "maps/$mapName.txt",
            "resource/overviews/$mapName.txt",
            "resource/overviews/$mapName.dds",
            "resource/overviews/${mapName}_radar.dds",
            "materials/overviews/$mapName.vmt",
            "materials/overviews/$mapName.vtf",
            "materials/overviews/${mapName}_radar.vmt",
            "materials/overviews/${mapName}_radar.vtf"
        )) {
            [void]$optionalExtras.Add($optionalRef)
        }
    }

    Update-Status 'Scan: checking disk files...'
    Pump-UiMessages -MinMilliseconds 0
    $rows = New-Object System.Collections.Generic.List[object]
    $diskExistsCache = @{}
    foreach ($r in ($refs | Sort-Object @{Expression = { Get-ArchivePathSortKey -ArchivePath $_ }})) {
        Pump-UiMessages
        $inPak = $script:State.Entries.Contains($r)
        $full = Join-Path $gameRoot (To-OsPath $r)
        if ($diskExistsCache.ContainsKey($full)) {
            $exists = [bool]$diskExistsCache[$full]
        } else {
            $exists = [System.IO.File]::Exists($full)
            $diskExistsCache[$full] = $exists
        }
        if ((-not $exists) -and $optionalExtras.Contains($r) -and (-not $inPak)) { continue }
        $baseGame = (-not $inPak) -and (Test-BaseGameArchivePath -BaseVpkEntries $baseVpkEntries -ArchivePath $r)
        $addable = (-not $inPak) -and (-not $baseGame) -and $exists
        $status = if ($inPak) { 'Already in PAK' } elseif ($baseGame) { 'Base game VPK' } elseif ($exists) { 'Can add' } else { 'Missing on disk' }
        [void]$rows.Add([pscustomobject]@{ Path = $r; Exists = $exists; InPak = $inPak; BaseGame = $baseGame; Addable = $addable; Status = $status; FullDiskPath = $full })
    }
    return @($rows.ToArray())
}

function Update-ScanSummaryFromResults {
    param([object[]]$Results)

    if ($null -eq $Results -or $Results.Count -eq 0) {
        Set-ScanSummary -MissingTotal 0 -CanAdd 0 -NotFound 0 -AlreadyInPak 0
        Refresh-ScanSummaryUI
        return
    }

    $alreadyInPak = 0
    $missingTotal = 0
    $canAdd = 0
    $notFound = 0
    foreach ($row in $Results) {
        if ($row.InPak) {
            $alreadyInPak++
            continue
        }
        if ($row.BaseGame) { continue }
        $missingTotal++
        if ($row.Addable) { $canAdd++ }
        elseif (-not $row.Exists) { $notFound++ }
    }

    Set-ScanSummary -MissingTotal $missingTotal -CanAdd $canAdd -NotFound $notFound -AlreadyInPak $alreadyInPak
    Refresh-ScanSummaryUI
}

function Add-ScanResults {
    param([object[]]$ScanResults)

    $added = 0
    $replaced = 0
    $missingOnDisk = 0
    $readErrors = 0
    $alreadyInPak = 0
    $baseGameSkipped = 0

    foreach ($row in $ScanResults) {
        Pump-UiMessages
        if ($row.InPak) { $alreadyInPak++; continue }
        if ($row.BaseGame) { $baseGameSkipped++; continue }
        if (-not $row.Addable) {
            if (-not $row.Exists) { $missingOnDisk++ }
            continue
        }
        if (-not $row.Exists) { $missingOnDisk++; continue }
        if (-not [System.IO.File]::Exists($row.FullDiskPath)) { $missingOnDisk++; continue }

        try {
            $bytes = Read-FileBytesResponsive -Path $row.FullDiskPath -MaxBytes $MaxPakEntryBytes -Label $row.Path
            $res = Add-OrReplaceEntry -ArchivePath $row.Path -Data $bytes
            if ($res -eq 'Added') { $added++ } else { $replaced++ }
        } catch {
            $readErrors++
        }
    }

    if ($added -gt 0 -or $replaced -gt 0) {
        $script:State.IsDirty = $true
        Refresh-AllViews
    }

    $status = "Add all: +$added new | $replaced replaced | missing $missingOnDisk | base game $baseGameSkipped | errors $readErrors"
    Update-Status $status

    return [pscustomobject]@{
        Added = $added
        Replaced = $replaced
        MissingOnDisk = $missingOnDisk
        ReadErrors = $readErrors
        AlreadyInPak = $alreadyInPak
        BaseGameSkipped = $baseGameSkipped
        Summary = $status
    }
}

function Show-ScanDialog {
    param([object[]]$Results)

    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = 'Missing from BSP'
    $dlg.StartPosition = 'CenterParent'
    $dlg.Width = 980
    $dlg.Height = 640

    $bodyPanel = New-Object System.Windows.Forms.Panel
    $bodyPanel.Dock = 'Fill'
    $dlg.Controls.Add($bodyPanel)

    $scanRowsPanel = New-Object System.Windows.Forms.Panel
    $scanRowsPanel.Dock = 'Fill'
    $bodyPanel.Controls.Add($scanRowsPanel)

    $scanHeaderPanel = New-Object System.Windows.Forms.Panel
    $scanHeaderPanel.Dock = 'Top'
    $scanHeaderPanel.Height = 24
    $scanHeaderPanel.BackColor = $script:Theme.Header
    $bodyPanel.Controls.Add($scanHeaderPanel)
    $bodyPanel.Controls.SetChildIndex($scanRowsPanel, 0)
    $bodyPanel.Controls.SetChildIndex($scanHeaderPanel, 1)

    $scanScrollBar = New-Object PakRatWheelPanel
    $scanScrollBar.Dock = 'Right'
    $scanScrollBar.Width = 22
    $scanScrollBar.BackColor = $script:Theme.Panel
    $scanScrollBar.Visible = $false
    $scanRowsPanel.Controls.Add($scanScrollBar)

    $scanScrollTrack = New-Object PakRatWheelPanel
    $scanScrollTrack.Dock = 'Fill'
    $scanScrollTrack.BackColor = $script:Theme.Header
    $scanScrollBar.Controls.Add($scanScrollTrack)

    $scanScrollThumb = New-Object PakRatWheelPanel
    $scanScrollThumb.Left = 3
    $scanScrollThumb.Top = 0
    $scanScrollThumb.Width = 16
    $scanScrollThumb.Height = 48
    $scanScrollThumb.BackColor = [System.Drawing.Color]::FromArgb(88, 94, 106)
    $scanScrollTrack.Controls.Add($scanScrollThumb)

    $lv = New-Object System.Windows.Forms.ListView
    $lv.Dock = 'Fill'
    $lv.View = 'Details'
    $lv.HeaderStyle = 'None'
    $lv.FullRowSelect = $true
    $lv.GridLines = $false
    [void]$lv.Columns.Add('Path', 620)
    [void]$lv.Columns.Add('Status', 180)
    [void]$lv.Columns.Add('In PAK', 70)
    [void]$lv.Columns.Add('On disk', 70)
    $scanRowsPanel.Controls.Add($lv)
    $scanRowsPanel.Controls.SetChildIndex($lv, 0)
    $scanRowsPanel.Controls.SetChildIndex($scanScrollBar, 1)
    $scanScrollBar.BringToFront()

    $scanHeaderLeft = 0
    $scanHeaderSpecs = @(
        @{ Text = 'Path'; Width = 620 },
        @{ Text = 'Status'; Width = 180 },
        @{ Text = 'In PAK'; Width = 70 },
        @{ Text = 'On disk'; Width = 70 }
    )
    foreach ($spec in $scanHeaderSpecs) {
        $header = New-Object System.Windows.Forms.Label
        $header.Left = $scanHeaderLeft
        $header.Top = 0
        $header.Width = [int]$spec.Width
        $header.Height = $scanHeaderPanel.Height
        $header.Text = [string]$spec.Text
        $header.TextAlign = 'MiddleLeft'
        $header.Padding = New-Object System.Windows.Forms.Padding(4, 0, 4, 0)
        $header.BackColor = $script:Theme.Header
        $header.ForeColor = $script:Theme.Text
        $scanHeaderPanel.Controls.Add($header)
        $scanHeaderLeft += [int]$spec.Width
    }

    $panel = New-Object System.Windows.Forms.Panel
    $panel.Dock = 'Bottom'
    $panel.Height = 54
    $dlg.Controls.Add($panel)
    $dlg.Controls.SetChildIndex($bodyPanel, 0)
    $dlg.Controls.SetChildIndex($panel, 1)

    $addBtn = New-Object System.Windows.Forms.Button
    $addBtn.Text = 'Add all'
    $addBtn.Left = 10
    $addBtn.Top = 12
    $addBtn.Width = 110
    $panel.Controls.Add($addBtn)

    $addSaveBtn = New-Object System.Windows.Forms.Button
    $addSaveBtn.Text = 'Add all + Save'
    $addSaveBtn.Left = 126
    $addSaveBtn.Top = 12
    $addSaveBtn.Width = 130
    $panel.Controls.Add($addSaveBtn)

    $onlyCanAddChk = New-Object System.Windows.Forms.CheckBox
    $onlyCanAddChk.Text = 'Only can add'
    $onlyCanAddChk.Left = 276
    $onlyCanAddChk.Top = 16
    $onlyCanAddChk.Width = 130
    $panel.Controls.Add($onlyCanAddChk)

    $exportBtn = New-Object System.Windows.Forms.Button
    $exportBtn.Text = 'Export .txt'
    $exportBtn.Left = 414
    $exportBtn.Top = 12
    $exportBtn.Width = 110
    $panel.Controls.Add($exportBtn)

    $closeBtn = New-Object System.Windows.Forms.Button
    $closeBtn.Text = 'Close'
    $closeBtn.Left = 840
    $closeBtn.Top = 12
    $closeBtn.Width = 110
    $panel.Controls.Add($closeBtn)

    $script:ScanDialogScrollDragging = $false
    $script:ScanDialogScrollDragOffsetY = 0
    $getScanRowHeight = {
        if ($lv.Items.Count -eq 0) { return 18 }
        try {
            $rect = $lv.GetItemRect(0)
            if ($rect.Height -gt 0) { return $rect.Height }
        } catch { }
        return 18
    }
    $getScanVisibleRows = {
        $rowHeight = & $getScanRowHeight
        return [Math]::Max(1, [int][Math]::Floor($lv.ClientSize.Height / [double]$rowHeight))
    }
    $getScanTopIndex = {
        if ($lv.Items.Count -eq 0) { return 0 }
        try {
            if ($lv.TopItem) { return [int]$lv.TopItem.Index }
        } catch { }
        return 0
    }
    $updateScanScroll = {
        Hide-NativeListViewScrollBars -ListView $lv
        $scanScrollBar.BackColor = $script:Theme.Panel
        $scanScrollTrack.BackColor = $script:Theme.ScrollTrack
        $scanScrollThumb.BackColor = $script:Theme.ScrollThumb
        $total = [int]$lv.Items.Count
        $visibleRows = & $getScanVisibleRows
        $needsScroll = ($total -gt $visibleRows)
        $scanScrollBar.Visible = $needsScroll
        if (-not $needsScroll) { return }

        $trackHeight = [Math]::Max(1, $scanScrollTrack.ClientSize.Height)
        $thumbHeight = [Math]::Max(44, [int][Math]::Floor($trackHeight * ($visibleRows / [double]$total)))
        $thumbHeight = [Math]::Min($trackHeight, $thumbHeight)
        $range = [Math]::Max(0, $trackHeight - $thumbHeight)
        $maxTop = [Math]::Max(1, $total - $visibleRows)
        $topIndex = [Math]::Max(0, [Math]::Min((& $getScanTopIndex), $maxTop))
        $thumbTop = if ($range -gt 0) { [int][Math]::Round($range * ($topIndex / [double]$maxTop)) } else { 0 }

        $scanScrollThumb.Left = 3
        $scanScrollThumb.Width = [Math]::Max(10, $scanScrollTrack.ClientSize.Width - 6)
        $scanScrollThumb.Height = $thumbHeight
        $scanScrollThumb.Top = $thumbTop
    }
    $scrollScanToTopIndex = {
        param([int]$Index)
        if ($lv.Items.Count -eq 0) { return }
        $visibleRows = & $getScanVisibleRows
        $maxTop = [Math]::Max(0, $lv.Items.Count - $visibleRows)
        $target = [Math]::Max(0, [Math]::Min($Index, $maxTop))
        try { $lv.TopItem = $lv.Items[$target] }
        catch { try { $lv.EnsureVisible($target) } catch { } }
        & $updateScanScroll
    }
    $scrollScanFromThumbY = {
        param([int]$ThumbY)
        if ($lv.Items.Count -eq 0) { return }
        $visibleRows = & $getScanVisibleRows
        $maxTop = [Math]::Max(0, $lv.Items.Count - $visibleRows)
        $range = [Math]::Max(1, $scanScrollTrack.ClientSize.Height - $scanScrollThumb.Height)
        $clampedY = [Math]::Max(0, [Math]::Min($ThumbY, $range))
        $target = [int][Math]::Round($maxTop * ($clampedY / [double]$range))
        & $scrollScanToTopIndex $target
    }
    $scrollScanByRows = {
        param([int]$Rows)
        & $scrollScanToTopIndex ((& $getScanTopIndex) + $Rows)
    }
    $scrollScanByWheelDelta = {
        param([int]$Delta)
        $rows = Get-MouseWheelScrollRows -Delta $Delta -VisibleRows (& $getScanVisibleRows)
        if ($rows -eq 0) {
            & $updateScanScroll
            return
        }
        & $scrollScanByRows $rows
    }

    $refreshList = {
        $lv.BeginUpdate()
        try {
            $lv.Items.Clear()
            foreach ($r in $Results) {
                if ($onlyCanAddChk.Checked -and -not $r.Addable) { continue }

                $statusText = $r.Status
                $it = New-Object System.Windows.Forms.ListViewItem($r.Path)
                [void]$it.SubItems.Add($statusText)
                $inPakText = if ($r.InPak) { 'Yes' } else { 'No' }
                $existsText = if ($r.Exists) { 'Yes' } else { 'No' }
                [void]$it.SubItems.Add($inPakText)
                [void]$it.SubItems.Add($existsText)
                if ($r.Addable) { $it.ForeColor = $script:Theme.Success }
                else { $it.ForeColor = $script:Theme.Error }
                [void]$lv.Items.Add($it)
            }
        } finally {
            $lv.EndUpdate()
        }
        & $updateScanScroll
    }

    $addBtn.Add_Click({ $dlg.Tag = 'add'; $dlg.Close() })
    $addSaveBtn.Add_Click({ $dlg.Tag = 'add_save'; $dlg.Close() })
    $closeBtn.Add_Click({ $dlg.Close() })
    $onlyCanAddChk.Add_CheckedChanged({ & $refreshList })
    $exportBtn.Add_Click({
        try {
            $rows = if ($onlyCanAddChk.Checked) { @($Results | Where-Object { $_.Addable }) } else { @($Results) }
            if ($rows.Count -eq 0) {
                Show-InfoDialog -Message 'There are no rows to export.'
                return
            }

            $defaultBaseName = if ($script:State.CurrentBspPath) {
                [System.IO.Path]::GetFileNameWithoutExtension($script:State.CurrentBspPath)
            } else {
                'scan'
            }
            $defaultName = "${defaultBaseName}_missing_list.txt"

            $saveDlg = New-Object System.Windows.Forms.SaveFileDialog
            $saveDlg.Filter = 'Text files (*.txt)|*.txt|All files (*.*)|*.*'
            $saveDlg.FileName = $defaultName
            if ($script:State.CurrentBspPath) {
                $saveDlg.InitialDirectory = [System.IO.Path]::GetDirectoryName($script:State.CurrentBspPath)
            }
            if ($saveDlg.ShowDialog($dlg) -ne [System.Windows.Forms.DialogResult]::OK) { return }

            $lines = New-Object System.Collections.Generic.List[string]
            $lines.Add("Path`tStatus`tIn PAK`tOn disk")
            foreach ($r in $rows) {
                $statusText = $r.Status
                $inPakText = if ($r.InPak) { 'Yes' } else { 'No' }
                $existsText = if ($r.Exists) { 'Yes' } else { 'No' }
                $lines.Add(("{0}`t{1}`t{2}`t{3}" -f $r.Path, $statusText, $inPakText, $existsText))
            }

            [System.IO.File]::WriteAllLines($saveDlg.FileName, $lines, [System.Text.Encoding]::ASCII)
            Update-Status "Scan list exported: $($saveDlg.FileName)"
            Show-InfoDialog -Message "Exported $($rows.Count) rows."
        } catch {
            Show-ErrorDialog -Message $_.Exception.Message
        }
    })

    $lv.Add_HandleCreated({ & $updateScanScroll })
    $lv.Add_SizeChanged({ & $updateScanScroll })
    $lv.Add_MouseEnter({ [void]$lv.Focus() })
    $lv.Add_MouseWheel({
        param($sender, $eventArgs)
        & $scrollScanByWheelDelta $eventArgs.Delta
        Set-MouseWheelHandled -EventArgs $eventArgs
        try { [void]$lv.BeginInvoke([System.Windows.Forms.MethodInvoker]{ & $updateScanScroll }) }
        catch { & $updateScanScroll }
    })
    $lv.Add_KeyUp({ & $updateScanScroll })
    $lv.Add_SelectedIndexChanged({ & $updateScanScroll })
    $scanScrollTrack.Add_SizeChanged({ & $updateScanScroll })
    foreach ($scanScrollControl in @($scanScrollBar, $scanScrollTrack, $scanScrollThumb)) {
        $scanScrollControl.TabStop = $true
        $scanScrollControl.Add_MouseEnter({ Set-WheelFocus -Control $this })
        $scanScrollControl.Add_MouseDown({ Set-WheelFocus -Control $this })
        $scanScrollControl.Add_MouseUp({ Set-WheelFocus -Control $this })
        $scanScrollControl.Add_MouseWheel({
            param($sender, $eventArgs)
            Set-WheelFocus -Control $sender
            & $scrollScanByWheelDelta $eventArgs.Delta
            Set-MouseWheelHandled -EventArgs $eventArgs
        })
    }
    $scanScrollTrack.Add_MouseDown({
        if ($_.Button -ne [System.Windows.Forms.MouseButtons]::Left) { return }
        Set-WheelFocus -Control $this
        if ($scanScrollThumb.Bounds.Contains($_.Location)) {
            $script:ScanDialogScrollDragging = $true
            $script:ScanDialogScrollDragOffsetY = $_.Y - $scanScrollThumb.Top
        } elseif ($_.Y -lt $scanScrollThumb.Top) {
            & $scrollScanByRows (-( & $getScanVisibleRows ))
        } else {
            & $scrollScanByRows (& $getScanVisibleRows)
        }
    })
    $scanScrollTrack.Add_MouseMove({
        if (-not $script:ScanDialogScrollDragging) { return }
        & $scrollScanFromThumbY ($_.Y - $script:ScanDialogScrollDragOffsetY)
    })
    $scanScrollTrack.Add_MouseUp({
        $script:ScanDialogScrollDragging = $false
        Set-WheelFocus -Control $this
    })
    $scanScrollThumb.Add_MouseDown({
        if ($_.Button -ne [System.Windows.Forms.MouseButtons]::Left) { return }
        Set-WheelFocus -Control $this
        $script:ScanDialogScrollDragging = $true
        $script:ScanDialogScrollDragOffsetY = $_.Y
    })
    $scanScrollThumb.Add_MouseMove({
        if (-not $script:ScanDialogScrollDragging) { return }
        $screenPoint = $scanScrollThumb.PointToScreen($_.Location)
        $trackPoint = $scanScrollTrack.PointToClient($screenPoint)
        & $scrollScanFromThumbY ($trackPoint.Y - $script:ScanDialogScrollDragOffsetY)
    })
    $scanScrollThumb.Add_MouseUp({
        $script:ScanDialogScrollDragging = $false
        Set-WheelFocus -Control $this
    })
    $dlg.Add_MouseUp({ $script:ScanDialogScrollDragging = $false })

    & $refreshList

    Enable-DarkListViewRendering -ListView $lv -UseOwnerDraw:$false
    [void](Show-DarkDialog -Dialog $dlg)
    return [string]$dlg.Tag
}

function Run-Scan {
    if ($script:ScanInProgress) { Update-Status 'Scan already running...'; return }
    $scanBusy = $false
    try {
        Set-ScanBusy -Busy:$true -Message 'Scan: starting...'
        $scanBusy = $true
        $results = Invoke-Scan
        Set-ScanBusy -Busy:$false
        $scanBusy = $false
        if ($null -eq $results) { return }
        Update-ScanSummaryFromResults -Results $results
        if ($results.Count -eq 0) { Show-InfoDialog -Message 'No references were found in the map.'; return }

        $baseGameSkipped = 0
        $canAdd = 0
        $missingOnDisk = 0
        $missingRows = New-Object System.Collections.Generic.List[object]
        foreach ($row in $results) {
            if ($row.InPak) { continue }
            if ($row.BaseGame) {
                $baseGameSkipped++
                continue
            }
            [void]$missingRows.Add($row)
            if ($row.Addable) { $canAdd++ }
            elseif (-not $row.Exists) { $missingOnDisk++ }
        }
        $missingInPak = @($missingRows.ToArray())
        if ($missingInPak.Count -eq 0) {
            Show-InfoDialog -Message 'No files are missing from the BSP. It is already complete.'
            Update-Status "Scan: no missing files | base game skipped $baseGameSkipped"
            return
        }

        Update-Status "Scan: missing in BSP $($missingInPak.Count) | can add $canAdd | not found $missingOnDisk | base game skipped $baseGameSkipped"

        $scanAction = Show-ScanDialog -Results $missingInPak
        if ($scanAction -eq 'add' -or $scanAction -eq 'add_save') {
            Set-ScanBusy -Busy:$true -Message 'Adding scan results...'
            $scanBusy = $true
            $addResult = Add-ScanResults -ScanResults $missingInPak
            Set-ScanBusy -Busy:$false
            $scanBusy = $false
            Set-ScanBusy -Busy:$true -Message 'Refreshing scan summary...'
            $scanBusy = $true
            $updatedResults = Invoke-Scan
            Set-ScanBusy -Busy:$false
            $scanBusy = $false
            if ($null -ne $updatedResults) { Update-ScanSummaryFromResults -Results $updatedResults }

            if ($scanAction -eq 'add_save') {
                if (($addResult.Added + $addResult.Replaced) -gt 0) {
                    [void](Save-BspInPlaceCore -ConfirmOverwrite:$false)
                } else {
                    Show-InfoDialog -Message 'No files were added, so there is nothing new to save.'
                }
            }
        }
    } catch {
        Show-ErrorDialog -Message $_.Exception.Message
    } finally {
        if ($scanBusy) { Set-ScanBusy -Busy:$false }
    }
}

function Run-AutoAdd {
    if ($script:ScanInProgress) { Update-Status 'Scan already running...'; return }
    $scanBusy = $false
    try {
        Set-ScanBusy -Busy:$true -Message 'Scan: starting...'
        $scanBusy = $true
        $results = Invoke-Scan
        Set-ScanBusy -Busy:$false
        $scanBusy = $false
        if ($null -eq $results) { return }
        Update-ScanSummaryFromResults -Results $results

        $addableRows = New-Object System.Collections.Generic.List[object]
        foreach ($row in $results) {
            if (-not $row.InPak -and -not $row.BaseGame -and $row.Addable) {
                [void]$addableRows.Add($row)
            }
        }
        $addable = @($addableRows.ToArray())
        if ($addable.Count -eq 0) { Show-InfoDialog -Message 'There are no addable missing files in this BSP.'; return }
        if (-not (Confirm-Dialog -Message "Add $($addable.Count) missing files to the PAK now?")) { return }

        Set-ScanBusy -Busy:$true -Message 'Adding scan results...'
        $scanBusy = $true
        [void](Add-ScanResults -ScanResults $addable)
        Set-ScanBusy -Busy:$false
        $scanBusy = $false
        Set-ScanBusy -Busy:$true -Message 'Refreshing scan summary...'
        $scanBusy = $true
        $updatedResults = Invoke-Scan
        Set-ScanBusy -Busy:$false
        $scanBusy = $false
        if ($null -ne $updatedResults) { Update-ScanSummaryFromResults -Results $updatedResults }
    } catch {
        Show-ErrorDialog -Message $_.Exception.Message
    } finally {
        if ($scanBusy) { Set-ScanBusy -Busy:$false }
    }
}

function Browse-BspFile {
    $dlg = New-Object System.Windows.Forms.OpenFileDialog
    $dlg.Filter = 'BSP files (*.bsp)|*.bsp|All files (*.*)|*.*'
    if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        try {
            $script:PathBox.Text = $dlg.FileName
            Open-Bsp -Path $dlg.FileName
        } catch {
            Show-ErrorDialog -Message $_.Exception.Message
        }
    }
}

function Show-ExplorerFolderDialog {
    param(
        [string]$Title = 'Select folder',
        [string]$InitialDirectory = ''
    )

    try {
        $owner = if ($script:MainForm -and $script:MainForm.Handle -ne [IntPtr]::Zero) { $script:MainForm.Handle } else { [IntPtr]::Zero }
        $selected = [PakRatFolderPicker]::PickFolder($Title, $InitialDirectory, $owner)
        if (-not [string]::IsNullOrWhiteSpace($selected) -and [System.IO.Directory]::Exists($selected)) {
            return $selected
        }

        return $null
    } catch {
        Write-StartupLog -Message 'Modern folder picker failed; falling back to FolderBrowserDialog.' -Exception $_.Exception

        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description = $Title
        $dlg.ShowNewFolderButton = $false
        try { $dlg.AutoUpgradeEnabled = $true } catch { }
        if (-not [string]::IsNullOrWhiteSpace($InitialDirectory) -and (Test-Path -LiteralPath $InitialDirectory -PathType Container)) {
            $dlg.SelectedPath = $InitialDirectory
        }

        if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return $null }
        if ([System.IO.Directory]::Exists($dlg.SelectedPath)) { return $dlg.SelectedPath }
        return $null
    }
}

function Browse-GameRoot {
    $initialDirectory = ''
    if ($script:GameRootBox -and -not [string]::IsNullOrWhiteSpace($script:GameRootBox.Text) -and (Test-Path $script:GameRootBox.Text -PathType Container)) {
        $initialDirectory = $script:GameRootBox.Text
    } elseif (-not [string]::IsNullOrWhiteSpace($script:State.GameRoot) -and (Test-Path $script:State.GameRoot -PathType Container)) {
        $initialDirectory = $script:State.GameRoot
    }

    $selectedPath = Show-ExplorerFolderDialog -Title 'Select Game Path folder' -InitialDirectory $initialDirectory
    if ($null -eq $selectedPath) { return }

    if (Set-CurrentGameRoot -PathValue $selectedPath -Persist:$true) {
        Update-Status "Game Path set: $($script:State.GameRoot)"
    } else {
        Show-ErrorDialog -Message 'Invalid Game Path folder.'
    }
}

function Show-ManageGamePathsDialog {
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = 'Saved paths'
    $dlg.StartPosition = 'CenterParent'
    $dlg.Width = 860
    $dlg.Height = 420
    $dlg.MinimumSize = New-Object System.Drawing.Size(760, 360)

    $list = New-Object System.Windows.Forms.ListBox
    $list.Left = 12
    $list.Top = 12
    $list.Width = 680
    $list.Height = 330
    $list.SelectionMode = 'MultiExtended'
    $dlg.Controls.Add($list)

    $setActiveBtn = New-Object System.Windows.Forms.Button
    $setActiveBtn.Text = 'Use selected'
    $setActiveBtn.Left = 704
    $setActiveBtn.Top = 12
    $setActiveBtn.Width = 136
    $dlg.Controls.Add($setActiveBtn)

    $removeBtn = New-Object System.Windows.Forms.Button
    $removeBtn.Text = 'Remove selected'
    $removeBtn.Left = 704
    $removeBtn.Top = 50
    $removeBtn.Width = 136
    $dlg.Controls.Add($removeBtn)

    $removeMissingBtn = New-Object System.Windows.Forms.Button
    $removeMissingBtn.Text = 'Remove missing'
    $removeMissingBtn.Left = 704
    $removeMissingBtn.Top = 88
    $removeMissingBtn.Width = 136
    $dlg.Controls.Add($removeMissingBtn)

    $addCurrentBtn = New-Object System.Windows.Forms.Button
    $addCurrentBtn.Text = 'Save current'
    $addCurrentBtn.Left = 704
    $addCurrentBtn.Top = 126
    $addCurrentBtn.Width = 136
    $dlg.Controls.Add($addCurrentBtn)

    $closeBtn = New-Object System.Windows.Forms.Button
    $closeBtn.Text = 'Close'
    $closeBtn.Left = 704
    $closeBtn.Top = 306
    $closeBtn.Width = 136
    $dlg.Controls.Add($closeBtn)

    $help = New-Object System.Windows.Forms.Label
    $help.Left = 12
    $help.Top = 350
    $help.Width = 670
    $help.Height = 30
    $help.Text = 'Tip: double-click a path to activate it.'
    $dlg.Controls.Add($help)

    $refreshList = {
        $list.BeginUpdate()
        try {
            $list.Items.Clear()
            foreach ($p in ($script:State.SavedGameRoots | Sort-Object)) {
                [void]$list.Items.Add($p)
            }
        } finally {
            $list.EndUpdate()
        }
    }

    & $refreshList

    $activatePath = {
        if ($list.SelectedItems.Count -eq 0) { return }
        $selected = [string]$list.SelectedItems[0]
        if (Set-CurrentGameRoot -PathValue $selected -Persist:$true) {
            Update-Status "Game Path selected: $($script:State.GameRoot)"
        } else {
            Show-ErrorDialog -Message 'The selected path no longer exists.'
        }
    }

    $setActiveBtn.Add_Click($activatePath)
    $list.Add_DoubleClick($activatePath)

    $removeBtn.Add_Click({
        if ($list.SelectedItems.Count -eq 0) { return }
        $removed = 0
        $selectedItems = @()
        foreach ($it in $list.SelectedItems) { $selectedItems += [string]$it }
        foreach ($p in $selectedItems) {
            if (Remove-SavedGameRoot -PathValue $p) { $removed++ }
        }
        Refresh-GameRootSelector
        & $refreshList
        Save-Settings
        Update-Status "Paths removed: $removed"
    })

    $removeMissingBtn.Add_Click({
        $count = Remove-MissingSavedGameRoots
        Refresh-GameRootSelector
        & $refreshList
        Save-Settings
        Update-Status "Missing paths removed: $count"
    })

    $addCurrentBtn.Add_Click({
        if (-not $script:GameRootBox) { return }
        $current = $script:GameRootBox.Text
        if (Set-CurrentGameRoot -PathValue $current -Persist:$true) {
            & $refreshList
            Update-Status "Path saved: $($script:State.GameRoot)"
        } else {
            Show-ErrorDialog -Message 'Invalid path.'
        }
    })

    $closeBtn.Add_Click({ $dlg.Close() })

    [void](Show-DarkDialog -Dialog $dlg)
}

function Load-BspFromUi {
    try {
        if ([string]::IsNullOrWhiteSpace($script:PathBox.Text)) { throw 'Select a BSP first.' }
        Open-Bsp -Path $script:PathBox.Text
    } catch { Show-ErrorDialog -Message $_.Exception.Message }
}

function Validate-SaveReadiness {
    param([string]$TargetLabel)
    try {
        $check = Verify-PakEntries -Entries $script:State.Entries
        if ($check.Ok) { return $true }

        $msg = "Pre-save validation failed for $TargetLabel.`r`n`r`n$($check.Message)`r`n`r`nSave anyway?"
        return (Confirm-Dialog -Message $msg)
    } catch {
        $msg = "Pre-save validation could not run for $TargetLabel.`r`n`r`n$($_.Exception.Message)`r`n`r`nSave anyway?"
        return (Confirm-Dialog -Message $msg)
    }
}

function Save-BspAs {
    try {
        Ensure-BspLoaded
        $dlg = New-Object System.Windows.Forms.SaveFileDialog
        $dlg.Filter = 'BSP files (*.bsp)|*.bsp|All files (*.*)|*.*'
        if ($script:State.CurrentBspPath) {
            $dir = [System.IO.Path]::GetDirectoryName($script:State.CurrentBspPath)
            $name = [System.IO.Path]::GetFileNameWithoutExtension($script:State.CurrentBspPath)
            $dlg.InitialDirectory = $dir
            $dlg.FileName = "${name}_packed.bsp"
        }
        if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }
        if (-not (Validate-SaveReadiness -TargetLabel $dlg.FileName)) { return }
        Save-CurrentBsp -OutputPath $dlg.FileName -InPlace:$false -CreateBackup:$true
        $script:PathBox.Text = $dlg.FileName
        Refresh-AllViews
        Update-Status "Saved: $($dlg.FileName)"
        Show-InfoDialog -Message "BSP saved successfully.`r`n`r`n$($dlg.FileName)"
    } catch { Show-ErrorDialog -Message $_.Exception.Message }
}

function Save-BspInPlaceCore {
    param([bool]$ConfirmOverwrite = $true)
    Ensure-BspLoaded
    if (-not $script:State.CurrentBspPath) { throw 'No BSP is loaded.' }
    if ($ConfirmOverwrite -and -not (Confirm-Dialog -Message 'Overwrite current BSP now?')) { return $false }
    if (-not (Validate-SaveReadiness -TargetLabel $script:State.CurrentBspPath)) { return $false }
    Save-CurrentBsp -OutputPath $script:State.CurrentBspPath -InPlace:$true -CreateBackup:$script:State.BackupBeforeInPlaceSave
    Refresh-AllViews
    $bakStatus = if ($script:State.BackupBeforeInPlaceSave) { 'ON' } else { 'OFF' }
    Update-Status "Saved in place: $($script:State.CurrentBspPath) | backup: $bakStatus"
    return $true
}

function Save-BspInPlace {
    try {
        if (Save-BspInPlaceCore -ConfirmOverwrite:$true) {
            Show-InfoDialog -Message "BSP saved successfully.`r`n`r`n$($script:State.CurrentBspPath)"
        }
    } catch { Show-ErrorDialog -Message $_.Exception.Message }
}

function Verify-PakAction {
    try {
        Ensure-BspLoaded
        $check = Verify-PakEntries -Entries $script:State.Entries
        if ($check.Ok) { Show-InfoDialog -Message $check.Message; Update-Status $check.Message }
        else { Show-ErrorDialog -Message $check.Message }
    } catch { Show-ErrorDialog -Message $_.Exception.Message }
}

function Add-FilesAction {
    try {
        Ensure-BspLoaded
        $dlg = New-Object System.Windows.Forms.OpenFileDialog
        $dlg.Filter = 'All files (*.*)|*.*'
        $dlg.Multiselect = $true
        if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }
        Add-PathsWorkflow -RawPaths $dlg.FileNames
    } catch { Show-ErrorDialog -Message $_.Exception.Message }
}

function Add-FolderAction {
    try {
        Ensure-BspLoaded
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description = 'Select folder to add recursively'
        if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }
        Add-PathsWorkflow -RawPaths @($dlg.SelectedPath)
    } catch { Show-ErrorDialog -Message $_.Exception.Message }
}

function Handle-DroppedFiles {
    param([string[]]$Paths)
    try {
        if ($Paths.Count -eq 1 -and [System.IO.Path]::GetExtension($Paths[0]).ToLowerInvariant() -eq '.bsp' -and (Test-Path -LiteralPath $Paths[0] -PathType Leaf)) {
            Open-Bsp -Path $Paths[0]
            return
        }

        Ensure-BspLoaded
        Add-PathsWorkflow -RawPaths $Paths
    }
    catch { Show-ErrorDialog -Message $_.Exception.Message }
}

function Show-PreferencesDialog {
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = 'Preferences'
    $dlg.StartPosition = 'CenterParent'
    $dlg.Width = 700
    $dlg.Height = 260
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.MaximizeBox = $false
    $dlg.MinimizeBox = $false

    $lblRoot = New-Object System.Windows.Forms.Label
    $lblRoot.Text = 'Game Path:'
    $lblRoot.Left = 12
    $lblRoot.Top = 20
    $lblRoot.Width = 85
    $dlg.Controls.Add($lblRoot)

    $txtRoot = New-Object System.Windows.Forms.TextBox
    $txtRoot.Left = 100
    $txtRoot.Top = 16
    $txtRoot.Width = 490
    $txtRoot.Text = $script:State.GameRoot
    $dlg.Controls.Add($txtRoot)

    $browseRoot = New-Object System.Windows.Forms.Button
    $browseRoot.Text = 'Browse...'
    $browseRoot.Left = 596
    $browseRoot.Top = 14
    $browseRoot.Width = 80
    $dlg.Controls.Add($browseRoot)

    $lblFix = New-Object System.Windows.Forms.Label
    $lblFix.Text = 'Path Fixup:'
    $lblFix.Left = 12
    $lblFix.Top = 58
    $lblFix.Width = 85
    $dlg.Controls.Add($lblFix)

    $cmbFix = New-Object System.Windows.Forms.ComboBox
    $cmbFix.Left = 100
    $cmbFix.Top = 54
    $cmbFix.Width = 180
    $cmbFix.DropDownStyle = 'DropDownList'
    [void]$cmbFix.Items.Add('Ask')
    [void]$cmbFix.Items.Add('Always')
    [void]$cmbFix.Items.Add('Never')
    $cmbFix.SelectedItem = $script:State.PathFixupMode
    $dlg.Controls.Add($cmbFix)

    $chkExtras = New-Object System.Windows.Forms.CheckBox
    $chkExtras.Left = 100
    $chkExtras.Top = 88
    $chkExtras.Width = 460
    $chkExtras.Text = 'Include optional extras in scan (nav, overviews, map txt, radar files)'
    $chkExtras.Checked = $script:State.IncludeExtrasInScan
    $dlg.Controls.Add($chkExtras)

    $chkBackup = New-Object System.Windows.Forms.CheckBox
    $chkBackup.Left = 100
    $chkBackup.Top = 114
    $chkBackup.Width = 460
    $chkBackup.Text = 'Create .bak backup when saving in place'
    $chkBackup.Checked = $script:State.BackupBeforeInPlaceSave
    $dlg.Controls.Add($chkBackup)

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = 'OK'
    $ok.Left = 514
    $ok.Top = 160
    $ok.Width = 75
    $ok.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $dlg.Controls.Add($ok)

    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = 'Cancel'
    $cancel.Left = 596
    $cancel.Top = 160
    $cancel.Width = 80
    $cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $dlg.Controls.Add($cancel)

    $browseRoot.Add_Click({
        $initialDirectory = ''
        if (-not [string]::IsNullOrWhiteSpace($txtRoot.Text) -and (Test-Path $txtRoot.Text -PathType Container)) { $initialDirectory = $txtRoot.Text }
        $selectedPath = Show-ExplorerFolderDialog -Title 'Select Game Path folder' -InitialDirectory $initialDirectory
        if ($null -ne $selectedPath) { $txtRoot.Text = $selectedPath }
    })

    if ((Show-DarkDialog -Dialog $dlg) -ne [System.Windows.Forms.DialogResult]::OK) { return }

    if (-not [string]::IsNullOrWhiteSpace($txtRoot.Text.Trim())) {
        if (-not (Set-CurrentGameRoot -PathValue $txtRoot.Text.Trim() -Persist:$false)) {
            Show-ErrorDialog -Message 'Invalid Game Path folder.'
            return
        }
    }
    $script:State.PathFixupMode = [string]$cmbFix.SelectedItem
    $script:State.IncludeExtrasInScan = $chkExtras.Checked
    $script:State.BackupBeforeInPlaceSave = $chkBackup.Checked
    Save-Settings
    Update-Status 'Preferences saved.'
}

function Show-About {
    $msg = @(
        'PakRat Modern'
        "Version $($script:AppVersion)"
        'Author: Ayrton09'
        ''
        'Updated replacement for the classic PakRat workflow:'
        '- View/Edit/Add/Delete/Extract PAK files'
        '- Scan and auto-add from map references'
        '- Safer save and automatic backup'
    ) -join "`r`n"
    Show-InfoDialog -Message $msg
}

Enable-NativeDarkAppMode

$form = New-Object System.Windows.Forms.Form
$form.StartPosition = 'CenterScreen'
$form.Width = 1140
$form.Height = 760
$form.MinimumSize = New-Object System.Drawing.Size(980, 620)
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)
$form.Add_HandleCreated({ Enable-DarkTitleBar -Form $form })
if (Test-Path -LiteralPath $script:AppIconPath -PathType Leaf) {
    try { $form.Icon = New-Object System.Drawing.Icon($script:AppIconPath) }
    catch {
        try {
            $fs = [System.IO.File]::OpenRead($script:AppIconPath)
            try { $form.Icon = New-Object System.Drawing.Icon($fs) }
            finally { $fs.Dispose() }
        } catch { }
    }
}
$script:MainForm = $form

$menu = New-Object System.Windows.Forms.MenuStrip
$form.MainMenuStrip = $menu
$form.Controls.Add($menu)

$fileMenu = New-Object System.Windows.Forms.ToolStripMenuItem('File')
$viewMenu = New-Object System.Windows.Forms.ToolStripMenuItem('View')
$toolsMenu = New-Object System.Windows.Forms.ToolStripMenuItem('Tools')
$helpMenu = New-Object System.Windows.Forms.ToolStripMenuItem('Help')
[void]$menu.Items.Add($fileMenu)
[void]$menu.Items.Add($viewMenu)
[void]$menu.Items.Add($toolsMenu)
[void]$menu.Items.Add($helpMenu)

$miLoad = New-Object System.Windows.Forms.ToolStripMenuItem('Open BSP...')
$miLoad.Visible = $false
$miSave = New-Object System.Windows.Forms.ToolStripMenuItem('Save BSP')
$miSaveAs = New-Object System.Windows.Forms.ToolStripMenuItem('Save BSP As...')
$miPref = New-Object System.Windows.Forms.ToolStripMenuItem('Preferences...')
$miQuit = New-Object System.Windows.Forms.ToolStripMenuItem('Exit')
[void]$fileMenu.DropDownItems.Add($miLoad)
[void]$fileMenu.DropDownItems.Add($miSave)
[void]$fileMenu.DropDownItems.Add($miSaveAs)
[void]$fileMenu.DropDownItems.Add('-')
[void]$fileMenu.DropDownItems.Add($miPref)
[void]$fileMenu.DropDownItems.Add('-')
[void]$fileMenu.DropDownItems.Add($miQuit)

$miAsTree = New-Object System.Windows.Forms.ToolStripMenuItem('As Tree')
$miAsTree.CheckOnClick = $true
$miRefresh = New-Object System.Windows.Forms.ToolStripMenuItem('Refresh')
[void]$viewMenu.DropDownItems.Add($miAsTree)
[void]$viewMenu.DropDownItems.Add($miRefresh)

$miVerify = New-Object System.Windows.Forms.ToolStripMenuItem('Verify PAK')
$miScan = New-Object System.Windows.Forms.ToolStripMenuItem('Scan')
$miAuto = New-Object System.Windows.Forms.ToolStripMenuItem('Auto Add')
$miManagePaths = New-Object System.Windows.Forms.ToolStripMenuItem('Manage Game Paths')
[void]$toolsMenu.DropDownItems.Add($miVerify)
[void]$toolsMenu.DropDownItems.Add($miScan)
[void]$toolsMenu.DropDownItems.Add($miAuto)
[void]$toolsMenu.DropDownItems.Add('-')
[void]$toolsMenu.DropDownItems.Add($miManagePaths)

$miAbout = New-Object System.Windows.Forms.ToolStripMenuItem('About')
[void]$helpMenu.DropDownItems.Add($miAbout)

$topPanel = New-Object System.Windows.Forms.Panel
$topPanel.Dock = 'Top'
$topPanel.Height = 78
$form.Controls.Add($topPanel)

$topPanelSeparator = New-Object System.Windows.Forms.Panel
$topPanelSeparator.Dock = 'Bottom'
$topPanelSeparator.Height = 1
$topPanelSeparator.BackColor = $script:Theme.Border
$topPanel.Controls.Add($topPanelSeparator)

$pathBox = New-Object System.Windows.Forms.TextBox
$pathBox.Left = 10
$pathBox.Top = 10
$pathBox.Width = 610
$pathBox.AutoSize = $false
$pathBox.Height = 20
$topPanel.Controls.Add($pathBox)
$script:PathBox = $pathBox

$browseBtn = New-Object System.Windows.Forms.Button
$browseBtn.Text = 'Browse...'
$browseBtn.Left = 626
$browseBtn.Top = 8
$browseBtn.Width = 82
$topPanel.Controls.Add($browseBtn)

$loadBtn = New-Object System.Windows.Forms.Button
$loadBtn.Text = 'Load BSP'
$loadBtn.Left = 714
$loadBtn.Top = 8
$loadBtn.Width = 78
$loadBtn.Visible = $false
$topPanel.Controls.Add($loadBtn)

$saveBtnTop = New-Object System.Windows.Forms.Button
$saveBtnTop.Text = 'Save BSP'
$saveBtnTop.Left = 798
$saveBtnTop.Top = 8
$saveBtnTop.Width = 78
$topPanel.Controls.Add($saveBtnTop)

$saveAsBtnTop = New-Object System.Windows.Forms.Button
$saveAsBtnTop.Text = 'Save As...'
$saveAsBtnTop.Left = 882
$saveAsBtnTop.Top = 8
$saveAsBtnTop.Width = 78
$topPanel.Controls.Add($saveAsBtnTop)

$verifyBtnTop = New-Object System.Windows.Forms.Button
$verifyBtnTop.Text = 'Verify'
$verifyBtnTop.Left = 966
$verifyBtnTop.Top = 8
$verifyBtnTop.Width = 70
$topPanel.Controls.Add($verifyBtnTop)

$gameRootLabel = New-Object System.Windows.Forms.Label
$gameRootLabel.Text = 'Game Path:'
$gameRootLabel.Left = 10
$gameRootLabel.Top = 46
$gameRootLabel.Width = 75
$topPanel.Controls.Add($gameRootLabel)

$gameRootBox = New-Object System.Windows.Forms.TextBox
$gameRootBox.Left = 84
$gameRootBox.Top = 42
$gameRootBox.Width = 790
$gameRootBox.AutoSize = $false
$gameRootBox.Height = 20
$topPanel.Controls.Add($gameRootBox)
$script:GameRootBox = $gameRootBox

$browseGameBtn = New-Object System.Windows.Forms.Button
$browseGameBtn.Text = 'Browse...'
$browseGameBtn.Left = 882
$browseGameBtn.Top = 40
$browseGameBtn.Width = 78
$topPanel.Controls.Add($browseGameBtn)

$scanTopBtn = New-Object System.Windows.Forms.Button
$scanTopBtn.Text = 'Scan'
$scanTopBtn.Left = 966
$scanTopBtn.Top = 40
$scanTopBtn.Width = 70
$topPanel.Controls.Add($scanTopBtn)

$managePathsBtn = New-Object System.Windows.Forms.Button
$managePathsBtn.Text = 'Paths...'
$managePathsBtn.Left = 1042
$managePathsBtn.Top = 40
$managePathsBtn.Width = 80
$topPanel.Controls.Add($managePathsBtn)

$scanSummaryPanel = New-Object System.Windows.Forms.Panel
$scanSummaryPanel.Dock = 'Top'
$scanSummaryPanel.Height = 28
$form.Controls.Add($scanSummaryPanel)

$summarySeparator = New-Object System.Windows.Forms.Panel
$summarySeparator.Dock = 'Bottom'
$summarySeparator.Height = 1
$summarySeparator.BackColor = $script:Theme.Border
$scanSummaryPanel.Controls.Add($summarySeparator)

$summaryMissing = New-Object System.Windows.Forms.Label
$summaryMissing.Left = 10
$summaryMissing.Top = 6
$summaryMissing.Width = 220
$summaryMissing.Text = 'Missing in BSP: 0'
$scanSummaryPanel.Controls.Add($summaryMissing)
$script:ScanSummaryMissingLabel = $summaryMissing

$summaryCanAdd = New-Object System.Windows.Forms.Label
$summaryCanAdd.Left = 235
$summaryCanAdd.Top = 6
$summaryCanAdd.Width = 220
$summaryCanAdd.Text = 'Can add: 0'
$scanSummaryPanel.Controls.Add($summaryCanAdd)
$script:ScanSummaryCanAddLabel = $summaryCanAdd

$summaryNotFound = New-Object System.Windows.Forms.Label
$summaryNotFound.Left = 460
$summaryNotFound.Top = 6
$summaryNotFound.Width = 200
$summaryNotFound.Text = 'Not found: 0'
$scanSummaryPanel.Controls.Add($summaryNotFound)
$script:ScanSummaryNotFoundLabel = $summaryNotFound

$summaryInPak = New-Object System.Windows.Forms.Label
$summaryInPak.Left = 665
$summaryInPak.Top = 6
$summaryInPak.Width = 180
$summaryInPak.Text = 'Already in PAK: 0'
$scanSummaryPanel.Controls.Add($summaryInPak)
$script:ScanSummaryInPakLabel = $summaryInPak

$mainPanel = New-Object System.Windows.Forms.Panel
$mainPanel.Dock = 'Fill'
$form.Controls.Add($mainPanel)

$listBodyPanel = New-Object System.Windows.Forms.Panel
$listBodyPanel.Dock = 'Fill'
$mainPanel.Controls.Add($listBodyPanel)
$script:ListBodyPanel = $listBodyPanel

$listRowsPanel = New-Object System.Windows.Forms.Panel
$listRowsPanel.Dock = 'Fill'
$listBodyPanel.Controls.Add($listRowsPanel)
$script:ListRowsPanel = $listRowsPanel

$listScrollBar = New-Object PakRatWheelPanel
$listScrollBar.Dock = 'Right'
$listScrollBar.Width = 22
$listScrollBar.BackColor = $script:Theme.Panel
$listScrollBar.Visible = $false
$listRowsPanel.Controls.Add($listScrollBar)
$script:ListScrollBar = $listScrollBar

$listScrollTrack = New-Object PakRatWheelPanel
$listScrollTrack.Dock = 'Fill'
$listScrollTrack.BackColor = $script:Theme.Header
$listScrollBar.Controls.Add($listScrollTrack)
$script:ListScrollTrack = $listScrollTrack

$listScrollThumb = New-Object PakRatWheelPanel
$listScrollThumb.Left = 3
$listScrollThumb.Top = 0
$listScrollThumb.Width = 16
$listScrollThumb.Height = 48
$listScrollThumb.BackColor = $script:Theme.Border
$listScrollTrack.Controls.Add($listScrollThumb)
$script:ListScrollThumb = $listScrollThumb

$listView = New-Object System.Windows.Forms.ListView
$listView.Dock = 'Fill'
$listView.View = 'Details'
$listView.HeaderStyle = 'None'
$listView.FullRowSelect = $true
$listView.MultiSelect = $true
$listView.GridLines = $false
$listView.HideSelection = $false
$listView.AllowDrop = $true
[void]$listView.Columns.Add('Name', 300)
[void]$listView.Columns.Add('Path', 520)
[void]$listView.Columns.Add('Size', 120)
[void]$listView.Columns.Add('Type', 150)
$listRowsPanel.Controls.Add($listView)
$listScrollBar.BringToFront()
$script:ListView = $listView

$listHeaderPanel = New-Object System.Windows.Forms.Panel
$listHeaderPanel.Dock = 'Top'
$listHeaderPanel.Height = 24
$mainPanel.Controls.Add($listHeaderPanel)
$mainPanel.Controls.SetChildIndex($listBodyPanel, 0)
$mainPanel.Controls.SetChildIndex($listHeaderPanel, 1)
$script:ListHeaderPanel = $listHeaderPanel

$headerLabels = @('Name', 'Path', 'Size', 'Type')
$headerLeft = 0
for ($i = 0; $i -lt $headerLabels.Count; $i++) {
    $headerBtn = New-Object System.Windows.Forms.Button
    $headerBtn.Left = $headerLeft
    $headerBtn.Top = 0
    $headerBtn.Height = $listHeaderPanel.Height
    $headerBtn.Width = $listView.Columns[$i].Width
    $headerBtn.TextAlign = 'MiddleLeft'
    $headerBtn.Padding = New-Object System.Windows.Forms.Padding(6, 0, 6, 0)
    $headerBtn.FlatStyle = 'Flat'
    $headerBtn.FlatAppearance.BorderSize = 0
    $headerBtn.FlatAppearance.MouseOverBackColor = $script:Theme.Header
    $headerBtn.FlatAppearance.MouseDownBackColor = $script:Theme.Selection
    $headerBtn.BackColor = $script:Theme.Header
    $headerBtn.ForeColor = $script:Theme.Text
    $headerBtn.Text = $headerLabels[$i]
    $columnIndex = $i
    $headerBtn.Add_Click(({ Set-ListSort -ColumnIndex $columnIndex }).GetNewClosure())
    $listHeaderPanel.Controls.Add($headerBtn)
    $script:ListHeaderButtons += $headerBtn
    $headerLeft += $headerBtn.Width
}

$listHeaderSeparator = New-Object System.Windows.Forms.Panel
$listHeaderSeparator.Dock = 'Bottom'
$listHeaderSeparator.Height = 1
$listHeaderSeparator.BackColor = $script:Theme.Border
$listHeaderPanel.Controls.Add($listHeaderSeparator)

$treeView = New-Object System.Windows.Forms.TreeView
$treeView.Dock = 'Fill'
$treeView.HideSelection = $false
$treeView.AllowDrop = $true
$treeView.Visible = $false
$mainPanel.Controls.Add($treeView)
$script:TreeView = $treeView

$bottomPanel = New-Object System.Windows.Forms.Panel
$bottomPanel.Dock = 'Bottom'
$bottomPanel.Height = 54
$form.Controls.Add($bottomPanel)

$bottomTopSeparator = New-Object System.Windows.Forms.Panel
$bottomTopSeparator.Dock = 'Top'
$bottomTopSeparator.Height = 1
$bottomTopSeparator.BackColor = $script:Theme.Border
$bottomPanel.Controls.Add($bottomTopSeparator)

$viewBtn = New-Object System.Windows.Forms.Button
$viewBtn.Text = 'View'
$viewBtn.Left = 10
$viewBtn.Top = 12
$viewBtn.Width = 72
$bottomPanel.Controls.Add($viewBtn)

$editBtn = New-Object System.Windows.Forms.Button
$editBtn.Text = 'Edit'
$editBtn.Left = 88
$editBtn.Top = 12
$editBtn.Width = 72
$bottomPanel.Controls.Add($editBtn)

$addBtn = New-Object System.Windows.Forms.Button
$addBtn.Text = 'Add'
$addBtn.Left = 166
$addBtn.Top = 12
$addBtn.Width = 72
$bottomPanel.Controls.Add($addBtn)

$deleteBtn = New-Object System.Windows.Forms.Button
$deleteBtn.Text = 'Delete'
$deleteBtn.Left = 244
$deleteBtn.Top = 12
$deleteBtn.Width = 72
$bottomPanel.Controls.Add($deleteBtn)

$extractBtn = New-Object System.Windows.Forms.Button
$extractBtn.Text = 'Extract'
$extractBtn.Left = 322
$extractBtn.Top = 12
$extractBtn.Width = 72
$bottomPanel.Controls.Add($extractBtn)

$scanBtn = New-Object System.Windows.Forms.Button
$scanBtn.Text = 'Scan'
$scanBtn.Left = 400
$scanBtn.Top = 12
$scanBtn.Width = 72
$bottomPanel.Controls.Add($scanBtn)

$autoBtn = New-Object System.Windows.Forms.Button
$autoBtn.Text = 'Auto'
$autoBtn.Left = 478
$autoBtn.Top = 12
$autoBtn.Width = 72
$bottomPanel.Controls.Add($autoBtn)

$toggleViewBtn = New-Object System.Windows.Forms.Button
$toggleViewBtn.Text = 'Tree/List'
$toggleViewBtn.Left = 556
$toggleViewBtn.Top = 12
$toggleViewBtn.Width = 82
$bottomPanel.Controls.Add($toggleViewBtn)

$statusStrip = New-Object System.Windows.Forms.StatusStrip
$statusStrip.SizingGrip = $false
$statusLabel = New-Object System.Windows.Forms.ToolStripStatusLabel
$statusLabel.Spring = $true
$statusLabel.TextAlign = 'MiddleLeft'
[void]$statusStrip.Items.Add($statusLabel)
$form.Controls.Add($statusStrip)
$script:StatusLabel = $statusLabel

$form.Controls.SetChildIndex($mainPanel, 0)
$form.Controls.SetChildIndex($bottomPanel, 1)
$form.Controls.SetChildIndex($statusStrip, 2)
$form.Controls.SetChildIndex($menu, 3)
$form.Controls.SetChildIndex($topPanel, 4)
$form.Controls.SetChildIndex($scanSummaryPanel, 5)

$addMenu = New-Object System.Windows.Forms.ContextMenuStrip
$miAddFiles = New-Object System.Windows.Forms.ToolStripMenuItem('Add files...')
$miAddFolder = New-Object System.Windows.Forms.ToolStripMenuItem('Add folder...')
[void]$addMenu.Items.Add($miAddFiles)
[void]$addMenu.Items.Add($miAddFolder)

$miLoad.Add_Click({ Browse-BspFile })
$miSave.Add_Click({ Save-BspInPlace })
$miSaveAs.Add_Click({ Save-BspAs })
$miPref.Add_Click({ Show-PreferencesDialog })
$miQuit.Add_Click({ $form.Close() })
$miAsTree.Add_CheckedChanged({ Set-ViewMode -AsTree:$miAsTree.Checked })
$miRefresh.Add_Click({ Refresh-AllViews })
$miVerify.Add_Click({ Verify-PakAction })
$miScan.Add_Click({ Run-Scan })
$miAuto.Add_Click({ Run-AutoAdd })
$miManagePaths.Add_Click({ Show-ManageGamePathsDialog })
$miAbout.Add_Click({ Show-About })

$browseBtn.Add_Click({ Browse-BspFile })
$browseGameBtn.Add_Click({
    try { Browse-GameRoot }
    catch { Show-ErrorDialog -Message $_.Exception.Message }
})
$loadBtn.Add_Click({ Load-BspFromUi })
$saveBtnTop.Add_Click({ Save-BspInPlace })
$saveAsBtnTop.Add_Click({ Save-BspAs })
$verifyBtnTop.Add_Click({ Verify-PakAction })
$scanTopBtn.Add_Click({ Run-Scan })
$managePathsBtn.Add_Click({ Show-ManageGamePathsDialog })

$pathBox.Add_KeyDown({ if ($_.KeyCode -eq [System.Windows.Forms.Keys]::Enter) { Load-BspFromUi } })
$pathBox.Add_Leave({
    if ([string]::IsNullOrWhiteSpace($script:PathBox.Text)) { return }
    if (-not (Test-Path $script:PathBox.Text -PathType Leaf)) { return }
    $candidate = [System.IO.Path]::GetFullPath($script:PathBox.Text)
    if ($script:State.CurrentBspPath -and ($candidate -eq $script:State.CurrentBspPath)) { return }
    try { Open-Bsp -Path $candidate } catch { Show-ErrorDialog -Message $_.Exception.Message }
})
if ($gameRootBox -is [System.Windows.Forms.ComboBox]) {
    $gameRootBox.Add_SelectionChangeCommitted({
        $value = $gameRootBox.Text.Trim()
        if ([string]::IsNullOrWhiteSpace($value)) { return }
        if (Set-CurrentGameRoot -PathValue $value -Persist:$true) {
            Update-Status "Game Path selected: $($script:State.GameRoot)"
        }
    })
}
$gameRootBox.Add_KeyDown({
    if ($_.KeyCode -eq [System.Windows.Forms.Keys]::Enter) {
        $value = $gameRootBox.Text.Trim()
        if ([string]::IsNullOrWhiteSpace($value)) { return }
        if (Set-CurrentGameRoot -PathValue $value -Persist:$true) {
            Update-Status "Game Path saved: $($script:State.GameRoot)"
        } else {
            Show-ErrorDialog -Message 'Invalid Game Path folder.'
        }
    }
})
$gameRootBox.Add_Leave({
    $value = $gameRootBox.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($value)) { return }
    [void](Set-CurrentGameRoot -PathValue $value -Persist:$true)
})

$viewBtn.Add_Click({ Show-EntryViewer })
$editBtn.Add_Click({ Edit-SelectedEntry })
$addBtn.Add_Click({
    $pt = New-Object System.Drawing.Point($addBtn.Left, ($addBtn.Top + $addBtn.Height))
    $screenPt = $bottomPanel.PointToScreen($pt)
    $addMenu.Show($screenPt)
})
$deleteBtn.Add_Click({ Remove-SelectedEntries })
$extractBtn.Add_Click({ Extract-SelectedEntries })
$scanBtn.Add_Click({ Run-Scan })
$autoBtn.Add_Click({ Run-AutoAdd })
$toggleViewBtn.Add_Click({
    $newMode = -not $script:State.ViewAsTree
    $miAsTree.Checked = $newMode
    Set-ViewMode -AsTree:$newMode
})

$miAddFiles.Add_Click({ Add-FilesAction })
$miAddFolder.Add_Click({ Add-FolderAction })

$listView.Add_DoubleClick({ Show-EntryViewer })
$treeView.Add_DoubleClick({ if ($treeView.SelectedNode -and $treeView.SelectedNode.Tag) { Show-EntryViewer } })

$listView.Add_HandleCreated({
    Hide-NativeListViewScrollBars -ListView $listView
    Update-ListScrollBar
})
$listView.Add_SizeChanged({ Update-ListScrollBar })
$listView.Add_MouseEnter({ [void]$listView.Focus() })
$listView.Add_MouseWheel({
    param($sender, $eventArgs)
    Scroll-ListViewByWheelDelta -Delta $eventArgs.Delta
    Set-MouseWheelHandled -EventArgs $eventArgs
    try { [void]$listView.BeginInvoke([System.Windows.Forms.MethodInvoker]{ Update-ListScrollBar }) }
    catch { Update-ListScrollBar }
})
$listView.Add_KeyUp({ Update-ListScrollBar })
$listView.Add_SelectedIndexChanged({ Update-ListScrollBar })
$listScrollTrack.Add_SizeChanged({ Update-ListScrollBar })
foreach ($listScrollControl in @($listScrollBar, $listScrollTrack, $listScrollThumb)) {
    $listScrollControl.TabStop = $true
    $listScrollControl.Add_MouseEnter({ Set-WheelFocus -Control $this })
    $listScrollControl.Add_MouseDown({ Set-WheelFocus -Control $this })
    $listScrollControl.Add_MouseUp({ Set-WheelFocus -Control $this })
    $listScrollControl.Add_MouseWheel({
        param($sender, $eventArgs)
        Set-WheelFocus -Control $sender
        Scroll-ListViewByWheelDelta -Delta $eventArgs.Delta
        Set-MouseWheelHandled -EventArgs $eventArgs
    })
}
$listScrollTrack.Add_MouseDown({
    if ($_.Button -ne [System.Windows.Forms.MouseButtons]::Left) { return }
    Set-WheelFocus -Control $this
    if ($listScrollThumb.Bounds.Contains($_.Location)) {
        $script:ListScrollDragging = $true
        $script:ListScrollDragOffsetY = $_.Y - $listScrollThumb.Top
    } elseif ($_.Y -lt $listScrollThumb.Top) {
        Scroll-ListViewByRows -Rows (-(Get-ListViewVisibleRowCount))
    } else {
        Scroll-ListViewByRows -Rows (Get-ListViewVisibleRowCount)
    }
})
$listScrollTrack.Add_MouseMove({
    if (-not $script:ListScrollDragging) { return }
    Scroll-ListViewFromThumbY -ThumbY ($_.Y - $script:ListScrollDragOffsetY)
})
$listScrollTrack.Add_MouseUp({
    $script:ListScrollDragging = $false
    Set-WheelFocus -Control $this
})
$listScrollThumb.Add_MouseDown({
    if ($_.Button -ne [System.Windows.Forms.MouseButtons]::Left) { return }
    Set-WheelFocus -Control $this
    $script:ListScrollDragging = $true
    $script:ListScrollDragOffsetY = $_.Y
})
$listScrollThumb.Add_MouseMove({
    if (-not $script:ListScrollDragging) { return }
    $screenPoint = $listScrollThumb.PointToScreen($_.Location)
    $trackPoint = $listScrollTrack.PointToClient($screenPoint)
    Scroll-ListViewFromThumbY -ThumbY ($trackPoint.Y - $script:ListScrollDragOffsetY)
})
$listScrollThumb.Add_MouseUp({
    $script:ListScrollDragging = $false
    Set-WheelFocus -Control $this
})
$form.Add_MouseUp({ $script:ListScrollDragging = $false })

$listView.Add_ColumnClick({
    $clicked = $_.Column
    if ($script:State.SortColumn -eq $clicked) { $script:State.SortDescending = -not $script:State.SortDescending }
    else { $script:State.SortColumn = $clicked; $script:State.SortDescending = $false }
    Refresh-AllViews
})

$listView.Add_DragEnter({
    if ($_.Data.GetDataPresent([System.Windows.Forms.DataFormats]::FileDrop)) { $_.Effect = [System.Windows.Forms.DragDropEffects]::Copy }
    else { $_.Effect = [System.Windows.Forms.DragDropEffects]::None }
})

$treeView.Add_DragEnter({
    if ($_.Data.GetDataPresent([System.Windows.Forms.DataFormats]::FileDrop)) { $_.Effect = [System.Windows.Forms.DragDropEffects]::Copy }
    else { $_.Effect = [System.Windows.Forms.DragDropEffects]::None }
})

$listView.Add_DragDrop({ $paths = [string[]]$_.Data.GetData([System.Windows.Forms.DataFormats]::FileDrop); Handle-DroppedFiles -Paths $paths })
$treeView.Add_DragDrop({ $paths = [string[]]$_.Data.GetData([System.Windows.Forms.DataFormats]::FileDrop); Handle-DroppedFiles -Paths $paths })

$form.Add_FormClosing({
    if ($script:State.IsDirty) {
        if (-not (Confirm-Dialog -Message 'There are unsaved changes. Exit anyway?')) { $_.Cancel = $true }
    }
})

try {
    Write-StartupLog -Message "Startup begin. BasePath=$($script:AppBasePath)"
    Load-Settings
    Write-StartupLog -Message "Settings loaded."
    Refresh-GameRootSelector
    if ($script:GameRootBox -and -not [string]::IsNullOrWhiteSpace($script:State.GameRoot)) {
        $script:GameRootBox.Text = $script:State.GameRoot
    }
    Refresh-ScanSummaryUI
    Enable-DarkListViewRendering -ListView $listView -UseOwnerDraw:$false
    Apply-DarkThemeRecursive -Control $form
    Apply-DarkThemeToToolStrips -MenuStrip $menu -StatusStrip $statusStrip
    Apply-DarkThemeToContextMenu -Menu $addMenu
    Enable-DarkTitleBar -Form $form
    Set-ViewMode -AsTree:$false
    Set-WindowTitle
    Update-Status 'Load a BSP to start.'
    Write-StartupLog -Message "UI initialized."

    if (-not [string]::IsNullOrWhiteSpace($InitialBsp)) {
        if (Test-Path -LiteralPath $InitialBsp -PathType Leaf) {
            try {
                Open-Bsp -Path $InitialBsp
                Write-StartupLog -Message "Initial BSP opened: $InitialBsp"
            } catch {
                Write-StartupLog -Message "Initial BSP open failed: $InitialBsp" -Exception $_.Exception
                Show-ErrorDialog -Message $_.Exception.Message
            }
        } else {
            Update-Status "Initial BSP was not found: $InitialBsp"
            Write-StartupLog -Message "Initial BSP not found: $InitialBsp"
        }
    }

    Write-StartupLog -Message "Showing main window."
    [void]$form.ShowDialog()
    Write-StartupLog -Message "Main window closed normally."
} catch {
    Write-StartupLog -Message 'Fatal startup error.' -Exception $_.Exception
    $fatalMessage = "PakRat Modern could not start.`r`n`r`n$($_.Exception.Message)`r`n`r`nLog: $($script:StartupLogPath)"
    try {
        [System.Windows.Forms.MessageBox]::Show($fatalMessage, 'PakRat Modern', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    } catch { }
    throw
}
