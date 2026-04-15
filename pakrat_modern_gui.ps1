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
            $exeDir = [System.IO.Path]::GetDirectoryName($exePath)
            if (-not [string]::IsNullOrWhiteSpace($exeDir) -and (Test-Path -LiteralPath $exeDir -PathType Container)) {
                return $exeDir
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
$script:AppVersion = '1.0.0'

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

$LumpCount = 64
$PakLumpIndex = 40
$GameLumpIndex = 35
$EntitiesLumpIndex = 0
$TexDataStringDataLumpIndex = 43
$TexDataStringTableLumpIndex = 44
$HeaderSize = 4 + 4 + ($LumpCount * 16) + 4

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
$script:ListHeaderPanel = $null
$script:ListHeaderButtons = @()
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
    SortColumn = 2
    SortDescending = $false
    ScanMissingTotal = 0
    ScanCanAdd = 0
    ScanNotFound = 0
    ScanAlreadyInPak = 0
}

function Show-ErrorDialog {
    param([string]$Message)
    [System.Windows.Forms.MessageBox]::Show($Message, 'PakRat Modern', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
}

function Show-InfoDialog {
    param([string]$Message)
    [System.Windows.Forms.MessageBox]::Show($Message, 'PakRat Modern', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}

function Confirm-Dialog {
    param([string]$Message)
    $result = [System.Windows.Forms.MessageBox]::Show($Message, 'PakRat Modern', [System.Windows.Forms.MessageBoxButtons]::YesNo, [System.Windows.Forms.MessageBoxIcon]::Question)
    return $result -eq [System.Windows.Forms.DialogResult]::Yes
}

function Update-Status {
    param([string]$Message)
    if ($script:StatusLabel) {
        $script:StatusLabel.Text = $Message
    }
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
    }
    elseif ($Control -is [System.Windows.Forms.ComboBox]) {
        $Control.BackColor = $script:Theme.Input
        $Control.ForeColor = $script:Theme.Text
        $Control.FlatStyle = 'Flat'
    }
    elseif ($Control -is [System.Windows.Forms.ListBox]) {
        $Control.BackColor = $script:Theme.Input
        $Control.ForeColor = $script:Theme.Text
    }
    elseif ($Control -is [System.Windows.Forms.ListView]) {
        Set-ControlDoubleBuffered -Control $Control
        $Control.BackColor = $script:Theme.Input
        $Control.ForeColor = $script:Theme.Text
        $Control.BorderStyle = 'None'
    }
    elseif ($Control -is [System.Windows.Forms.TreeView]) {
        Set-ControlDoubleBuffered -Control $Control
        $Control.BackColor = $script:Theme.Input
        $Control.ForeColor = $script:Theme.Text
        $Control.BorderStyle = 'None'
    }
    elseif ($Control -is [System.Windows.Forms.Button]) {
        $Control.BackColor = $script:Theme.Panel
        $Control.ForeColor = $script:Theme.Text
        $Control.FlatStyle = 'Flat'
        $Control.FlatAppearance.BorderColor = $script:Theme.Border
        $Control.FlatAppearance.MouseDownBackColor = $script:Theme.Selection
        $Control.FlatAppearance.MouseOverBackColor = $script:Theme.Header
    }
    elseif ($Control -is [System.Windows.Forms.CheckBox] -or $Control -is [System.Windows.Forms.Label]) {
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
        }
    })

    $ListView.Add_DrawSubItem({
        $isSelected = ($_.ItemState -band [System.Windows.Forms.ListViewItemStates]::Selected) -ne 0
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
    if ($Length -le 0) { return [byte[]]@() }
    $dest = New-Object byte[] $Length
    [Array]::Copy($Source, $Start, $dest, 0, $Length)
    return $dest
}

function Normalize-ArchivePath {
    param([string]$PathValue)
    if ([string]::IsNullOrWhiteSpace($PathValue)) { throw 'Invalid internal path.' }
    $p = $PathValue.Replace('\\', '/').Trim()
    while ($p.StartsWith('/')) { $p = $p.Substring(1) }
    if ([string]::IsNullOrWhiteSpace($p) -or $p -eq '.') { throw 'Invalid internal path.' }
    foreach ($part in $p.Split('/')) {
        if ($part -eq '..') { throw "Unsafe internal path: $PathValue" }
    }
    return $p
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

function New-EntryRecord {
    param([string]$FullPath, [byte[]]$Data, [bool]$InOriginal, [bool]$Modified)
    $normalized = Normalize-ArchivePath -PathValue $FullPath
    $osPath = To-OsPath $normalized
    $dir = [System.IO.Path]::GetDirectoryName($osPath)
    if ($null -eq $dir) { $dir = '' } else { $dir = $dir.Replace('\\', '/') }
    $name = [System.IO.Path]::GetFileName($osPath)
    [pscustomobject]@{
        FullPath = $normalized
        Directory = $dir
        Name = $name
        Size = $Data.Length
        Type = Get-EntryType -Name $name
        Data = $Data
        InOriginal = $InOriginal
        Modified = $Modified
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

    $labels = @('In', 'Name', 'Path', 'Size', 'Type')
    for ($i = 0; $i -lt $script:ListHeaderButtons.Count; $i++) {
        $btn = $script:ListHeaderButtons[$i]
        if (-not $btn) { continue }

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
        return $ms.ToArray()
    } finally {
        $ms.Dispose()
    }
}

function Parse-Bsp {
    param([string]$Path)
    $raw = [System.IO.File]::ReadAllBytes($Path)
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

function Read-PakEntries {
    param([byte[]]$PakBytes)
    $entries = [ordered]@{}
    if ($PakBytes.Length -eq 0) { return $entries }

    $ms = New-Object System.IO.MemoryStream(,$PakBytes)
    try {
        $zip = New-Object System.IO.Compression.ZipArchive($ms, [System.IO.Compression.ZipArchiveMode]::Read, $false)
        try {
            foreach ($entry in $zip.Entries) {
                if ($entry.FullName.EndsWith('/')) { continue }
                $name = Normalize-ArchivePath -PathValue $entry.FullName
                $stream = $entry.Open()
                try {
                    $tmp = New-Object System.IO.MemoryStream
                    try {
                        $stream.CopyTo($tmp)
                        $entries[$name] = New-EntryRecord -FullPath $name -Data $tmp.ToArray() -InOriginal:$true -Modified:$false
                    } finally { $tmp.Dispose() }
                } finally { $stream.Dispose() }
            }
        } finally { $zip.Dispose() }
    } catch {
        throw "PAK lump is not a valid ZIP: $($_.Exception.Message)"
    } finally { $ms.Dispose() }

    return $entries
}

function Write-PakEntries {
    param([System.Collections.IDictionary]$Entries)
    $ms = New-Object System.IO.MemoryStream
    try {
        $zip = New-Object System.IO.Compression.ZipArchive($ms, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($k in (@($Entries.Keys) | Sort-Object)) {
                $rec = $Entries[$k]
                $zentry = $zip.CreateEntry($k, [System.IO.Compression.CompressionLevel]::Optimal)
                $zstream = $zentry.Open()
                try {
                    $bytes = [byte[]]$rec.Data
                    if ($bytes.Length -gt 0) { $zstream.Write($bytes, 0, $bytes.Length) }
                } finally { $zstream.Dispose() }
            }
        } finally { $zip.Dispose() }
        return $ms.ToArray()
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
        0 { $all | Sort-Object @{Expression = { if ($_.InOriginal) { 1 } else { 0 } }}, @{Expression = { $_.FullPath }} }
        1 { $all | Sort-Object @{Expression = { $_.Name }}, @{Expression = { $_.FullPath }} }
        2 { $all | Sort-Object @{Expression = { $_.Directory }}, @{Expression = { $_.Name }} }
        3 { $all | Sort-Object @{Expression = { $_.Size }}, @{Expression = { $_.FullPath }} }
        4 { $all | Sort-Object @{Expression = { $_.Type }}, @{Expression = { $_.FullPath }} }
        default { $all | Sort-Object FullPath }
    }

    if ($desc) { [array]::Reverse($sorted) }
    return $sorted
}

function Refresh-ListView {
    $script:ListView.BeginUpdate()
    try {
        $script:ListView.View = 'Details'
        $script:ListView.Items.Clear()
        foreach ($entry in (Get-SortedEntries)) {
            $inMark = if ($entry.InOriginal) { 'X' } else { '' }
            $item = New-Object System.Windows.Forms.ListViewItem($inMark)
            [void]$item.SubItems.Add($entry.Name)
            [void]$item.SubItems.Add($entry.Directory)
            [void]$item.SubItems.Add($entry.Size.ToString())
            [void]$item.SubItems.Add($entry.Type)
            $item.Tag = $entry.FullPath
            if ($entry.Modified -or -not $entry.InOriginal) {
                $item.ForeColor = $script:Theme.ModifiedText
            } else {
                $item.ForeColor = $script:Theme.Text
            }
            [void]$script:ListView.Items.Add($item)
        }
    } finally {
        $script:ListView.EndUpdate()
    }
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
        $script:TreeView.ExpandAll()
    } finally {
        $script:TreeView.EndUpdate()
    }
}

function Refresh-AllViews {
    if (-not $script:ListView -or -not $script:TreeView) { return }
    Refresh-ListView
    Refresh-TreeView
    Refresh-ListHeaderUI

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
    if ($script:ListHeaderPanel) { $script:ListHeaderPanel.Visible = -not $AsTree }
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

    if ($InPlace -and $CreateBackup) { [System.IO.File]::Copy($OutputPath, "$OutputPath.bak", $true) }

    [System.IO.File]::WriteAllBytes($OutputPath, [byte[]]$result.Raw)
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
    $parsed = Parse-Bsp -Path $fullPath
    $pakBytes = Get-LumpBytes -Raw $parsed.Raw -Lumps $parsed.Lumps -Index $PakLumpIndex
    $entries = Read-PakEntries -PakBytes $pakBytes

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
    Refresh-AllViews
    Update-Status "BSP loaded: $fullPath"
}

function Resolve-ArchivePathFromFile {
    param([string]$FilePath, [string]$BasePath, [bool]$UseGameRootFixup)

    $source = if ($UseGameRootFixup -and -not [string]::IsNullOrWhiteSpace($script:State.GameRoot)) { $script:State.GameRoot } else { $BasePath }
    $rel = Get-RelativePath -BasePath $source -FullPath $FilePath
    if ($null -eq $rel) { return $null }
    return Normalize-ArchivePath -PathValue ($rel.Replace('\\', '/'))
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

        $bytes = [System.IO.File]::ReadAllBytes($f)
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

    Apply-DarkThemeRecursive -Control $dlg
    [void]$dlg.ShowDialog($script:MainForm)
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

    Apply-DarkThemeRecursive -Control $dlg
    if ($dlg.ShowDialog($script:MainForm) -ne [System.Windows.Forms.DialogResult]::OK) { return }

    $newName = $txtName.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($newName)) { Show-ErrorDialog -Message 'Name cannot be empty.'; return }

    $newPath = $txtPath.Text.Trim().Replace('\\', '/')
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
    $v = $Value.Trim().Trim('"').Replace('\\', '/')
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
    $mdl = Normalize-GameRef -Value $ModelPath
    if ($null -eq $mdl) { return }
    [void]$Set.Add($mdl)
    $base = [System.IO.Path]::ChangeExtension($mdl, $null)
    [void]$Set.Add("$base.vvd")
    [void]$Set.Add("$base.phy")
    [void]$Set.Add("$base.vtx")
    [void]$Set.Add("$base.dx80.vtx")
    [void]$Set.Add("$base.dx90.vtx")
    [void]$Set.Add("$base.sw.vtx")
}

function Read-NullTerminatedString {
    param([byte[]]$Bytes, [int]$Offset)
    if ($Offset -lt 0 -or $Offset -ge $Bytes.Length) { return '' }
    $end = $Offset
    while ($end -lt $Bytes.Length -and $Bytes[$end] -ne 0) { $end++ }
    if ($end -le $Offset) { return '' }
    return [System.Text.Encoding]::ASCII.GetString($Bytes, $Offset, ($end - $Offset))
}

function Collect-BspReferences {
    param([byte[]]$Raw, [object[]]$Lumps, [string]$MapName, [bool]$IncludeExtras)

    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

    $entitiesBytes = Get-LumpBytes -Raw $Raw -Lumps $Lumps -Index $EntitiesLumpIndex
    if ($entitiesBytes.Length -gt 0) {
        $entityText = [System.Text.Encoding]::ASCII.GetString($entitiesBytes).Replace([char]0, "`n")
        $pairs = [regex]::Matches($entityText, '"([^"]*)"\s*"([^"]*)"')
        foreach ($m in $pairs) {
            $key = $m.Groups[1].Value.ToLowerInvariant().Trim()
            $val = $m.Groups[2].Value.Trim()
            if ([string]::IsNullOrWhiteSpace($val)) { continue }

            switch ($key) {
                'model' { if (-not $val.StartsWith('*') -and $val.ToLowerInvariant().EndsWith('.mdl')) { Add-ModelWithCompanions -Set $set -ModelPath $val } }
                'gibmodel' { if ($val.ToLowerInvariant().EndsWith('.mdl')) { Add-ModelWithCompanions -Set $set -ModelPath $val } }
                'detailmaterial' {
                    if ($val -notmatch '\.vmt$') { Add-RefToSet -Set $set -Ref ("materials/$val.vmt") }
                    else { Add-RefToSet -Set $set -Ref $val }
                }
                'skyname' {
                    $sky = $val.Replace('\\', '/').Trim('/')
                    if (-not [string]::IsNullOrWhiteSpace($sky)) {
                        foreach ($side in @('up', 'dn', 'lf', 'rt', 'ft', 'bk')) {
                            Add-RefToSet -Set $set -Ref ("materials/skybox/${sky}${side}.vmt")
                            Add-RefToSet -Set $set -Ref ("materials/skybox/${sky}${side}.vtf")
                        }
                    }
                }
                default {
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

    $strData = Get-LumpBytes -Raw $Raw -Lumps $Lumps -Index $TexDataStringDataLumpIndex
    $strTable = Get-LumpBytes -Raw $Raw -Lumps $Lumps -Index $TexDataStringTableLumpIndex
    if ($strData.Length -gt 0 -and $strTable.Length -gt 0) {
        for ($i = 0; $i -le ($strTable.Length - 4); $i += 4) {
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
    }

    return $set
}
function Expand-VmtDependencies {
    param([System.Collections.Generic.HashSet[string]]$Set, [string]$GameRoot)
    if ([string]::IsNullOrWhiteSpace($GameRoot) -or -not (Test-Path $GameRoot -PathType Container)) { return }

    $queue = New-Object 'System.Collections.Generic.Queue[string]'
    $visited = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($ref in $Set) { if ([System.IO.Path]::GetExtension($ref).ToLowerInvariant() -eq '.vmt') { $queue.Enqueue($ref) } }

    while ($queue.Count -gt 0) {
        $vmtRef = $queue.Dequeue()
        if (-not $visited.Add($vmtRef)) { continue }

        $vmtPath = Join-Path $GameRoot (To-OsPath $vmtRef)
        if (-not (Test-Path $vmtPath -PathType Leaf)) { continue }

        try { $content = [System.IO.File]::ReadAllText($vmtPath) }
        catch { continue }

        $includes = [regex]::Matches($content, '(?im)^\s*include\s+"([^"]+)"')
        foreach ($m in $includes) {
            $inc = $m.Groups[1].Value
            if ($inc -notmatch '\.vmt$') { $inc += '.vmt' }
            $incRef = Normalize-GameRef -Value $inc
            if ($null -ne $incRef) {
                if ($incRef -notmatch '^(?i)materials/') { $incRef = "materials/$incRef" }
                if ($Set.Add($incRef)) { $queue.Enqueue($incRef) }
            }
        }

        $tex = [regex]::Matches($content, '(?im)^\s*"\$([A-Za-z0-9_]+)"\s+"([^"]+)"')
        foreach ($m in $tex) {
            $k = $m.Groups[1].Value.ToLowerInvariant()
            $v = $m.Groups[2].Value
            if ([string]::IsNullOrWhiteSpace($v)) { continue }

            if ($k -match 'texture|bump|normal|envmapmask|detail|selfillum|flowmap|dudv|lightwarp|phongexponent|iris') {
                if ($v -notmatch '\.vtf$') { $v += '.vtf' }
                $ref = Normalize-GameRef -Value $v
                if ($null -ne $ref) {
                    if ($ref -notmatch '^(?i)materials/') { $ref = "materials/$ref" }
                    [void]$Set.Add($ref)
                }
            } elseif ($k -eq 'bottommaterial') {
                if ($v -notmatch '\.vmt$') { $v += '.vmt' }
                $ref = Normalize-GameRef -Value $v
                if ($null -ne $ref) {
                    if ($ref -notmatch '^(?i)materials/') { $ref = "materials/$ref" }
                    if ($Set.Add($ref)) { $queue.Enqueue($ref) }
                }
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
    $refs = Collect-BspReferences -Raw $script:State.BspRaw -Lumps $script:State.Lumps -MapName $mapName -IncludeExtras:$script:State.IncludeExtrasInScan
    Expand-VmtDependencies -Set $refs -GameRoot $gameRoot

    $rows = @()
    foreach ($r in ($refs | Sort-Object)) {
        $inPak = $script:State.Entries.Contains($r)
        $full = Join-Path $gameRoot (To-OsPath $r)
        $exists = Test-Path -LiteralPath $full -PathType Leaf
        $status = if ($inPak) { 'Already in PAK' } elseif ($exists) { 'Can add' } else { 'Missing on disk' }
        $rows += [pscustomobject]@{ Path = $r; Exists = $exists; InPak = $inPak; Status = $status; FullDiskPath = $full }
    }
    return $rows
}

function Update-ScanSummaryFromResults {
    param([object[]]$Results)

    if ($null -eq $Results -or $Results.Count -eq 0) {
        Set-ScanSummary -MissingTotal 0 -CanAdd 0 -NotFound 0 -AlreadyInPak 0
        Refresh-ScanSummaryUI
        return
    }

    $alreadyInPak = @($Results | Where-Object { $_.InPak }).Count
    $missingInPak = @($Results | Where-Object { -not $_.InPak })
    $canAdd = @($missingInPak | Where-Object { $_.Exists }).Count
    $notFound = @($missingInPak | Where-Object { -not $_.Exists }).Count

    Set-ScanSummary -MissingTotal $missingInPak.Count -CanAdd $canAdd -NotFound $notFound -AlreadyInPak $alreadyInPak
    Refresh-ScanSummaryUI
}

function Add-ScanResults {
    param([object[]]$ScanResults)

    $added = 0
    $replaced = 0
    $missingOnDisk = 0
    $readErrors = 0
    $alreadyInPak = 0

    foreach ($row in $ScanResults) {
        if ($row.InPak) { $alreadyInPak++; continue }
        if (-not $row.Exists) { $missingOnDisk++; continue }
        if (-not (Test-Path -LiteralPath $row.FullDiskPath -PathType Leaf)) { $missingOnDisk++; continue }

        try {
            $bytes = [System.IO.File]::ReadAllBytes($row.FullDiskPath)
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

    $status = "Add all: +$added new | $replaced replaced | missing $missingOnDisk | errors $readErrors"
    Update-Status $status

    return [pscustomobject]@{
        Added = $added
        Replaced = $replaced
        MissingOnDisk = $missingOnDisk
        ReadErrors = $readErrors
        AlreadyInPak = $alreadyInPak
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

    $lv = New-Object System.Windows.Forms.ListView
    $lv.Dock = 'Fill'
    $lv.View = 'Details'
    $lv.FullRowSelect = $true
    $lv.GridLines = $false
    [void]$lv.Columns.Add('Path', 620)
    [void]$lv.Columns.Add('Status', 180)
    [void]$lv.Columns.Add('In PAK', 70)
    [void]$lv.Columns.Add('On disk', 70)
    $dlg.Controls.Add($lv)

    $panel = New-Object System.Windows.Forms.Panel
    $panel.Dock = 'Bottom'
    $panel.Height = 54
    $dlg.Controls.Add($panel)

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

    $refreshList = {
        $lv.BeginUpdate()
        try {
            $lv.Items.Clear()
            foreach ($r in $Results) {
                if ($onlyCanAddChk.Checked -and -not $r.Exists) { continue }

                $statusText = if ($r.Exists) { 'Can add' } else { 'Not found' }
                $it = New-Object System.Windows.Forms.ListViewItem($r.Path)
                [void]$it.SubItems.Add($statusText)
                $inPakText = if ($r.InPak) { 'Yes' } else { 'No' }
                $existsText = if ($r.Exists) { 'Yes' } else { 'No' }
                [void]$it.SubItems.Add($inPakText)
                [void]$it.SubItems.Add($existsText)
                if ($r.Exists) { $it.ForeColor = $script:Theme.Success }
                else { $it.ForeColor = $script:Theme.Error }
                [void]$lv.Items.Add($it)
            }
        } finally {
            $lv.EndUpdate()
        }
    }

    $addBtn.Add_Click({ $dlg.Tag = 'add'; $dlg.Close() })
    $addSaveBtn.Add_Click({ $dlg.Tag = 'add_save'; $dlg.Close() })
    $closeBtn.Add_Click({ $dlg.Close() })
    $onlyCanAddChk.Add_CheckedChanged({ & $refreshList })
    $exportBtn.Add_Click({
        try {
            $rows = if ($onlyCanAddChk.Checked) { @($Results | Where-Object { $_.Exists }) } else { @($Results) }
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
                $statusText = if ($r.Exists) { 'Can add' } else { 'Not found' }
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

    & $refreshList

    Enable-DarkListViewRendering -ListView $lv -UseOwnerDraw:$false
    Apply-DarkThemeRecursive -Control $dlg
    [void]$dlg.ShowDialog($script:MainForm)
    return [string]$dlg.Tag
}

function Run-Scan {
    try {
        $results = Invoke-Scan
        if ($null -eq $results) { return }
        Update-ScanSummaryFromResults -Results $results
        if ($results.Count -eq 0) { Show-InfoDialog -Message 'No references were found in the map.'; return }

        $missingInPak = @($results | Where-Object { -not $_.InPak })
        if ($missingInPak.Count -eq 0) {
            Show-InfoDialog -Message 'No files are missing from the BSP. It is already complete.'
            Update-Status 'Scan: no missing files.'
            return
        }

        $canAdd = @($missingInPak | Where-Object { $_.Exists }).Count
        $missingOnDisk = @($missingInPak | Where-Object { -not $_.Exists }).Count
        Update-Status "Scan: missing in BSP $($missingInPak.Count) | can add $canAdd | not found $missingOnDisk"

        $scanAction = Show-ScanDialog -Results $missingInPak
        if ($scanAction -eq 'add' -or $scanAction -eq 'add_save') {
            $addResult = Add-ScanResults -ScanResults $missingInPak
            $updatedResults = Invoke-Scan
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
    }
}

function Run-AutoAdd {
    try {
        $results = Invoke-Scan
        if ($null -eq $results) { return }
        Update-ScanSummaryFromResults -Results $results

        $missingInPak = @($results | Where-Object { -not $_.InPak })
        $addable = @($missingInPak | Where-Object { $_.Exists })
        if ($addable.Count -eq 0) { Show-InfoDialog -Message 'There are no addable missing files in this BSP.'; return }
        if (-not (Confirm-Dialog -Message "Add $($addable.Count) missing files to the PAK now?")) { return }

        [void](Add-ScanResults -ScanResults $addable)
        $updatedResults = Invoke-Scan
        if ($null -ne $updatedResults) { Update-ScanSummaryFromResults -Results $updatedResults }
    } catch {
        Show-ErrorDialog -Message $_.Exception.Message
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

function Browse-GameRoot {
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Select Game Path folder (hl2/cstrike/etc)'
    if ($script:GameRootBox -and -not [string]::IsNullOrWhiteSpace($script:GameRootBox.Text) -and (Test-Path $script:GameRootBox.Text -PathType Container)) {
        $dlg.SelectedPath = $script:GameRootBox.Text
    } elseif (-not [string]::IsNullOrWhiteSpace($script:State.GameRoot) -and (Test-Path $script:State.GameRoot -PathType Container)) {
        $dlg.SelectedPath = $script:State.GameRoot
    }

    if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        if (Set-CurrentGameRoot -PathValue $dlg.SelectedPath -Persist:$true) {
            Update-Status "Game Path set: $($script:State.GameRoot)"
        } else {
            Show-ErrorDialog -Message 'Invalid Game Path folder.'
        }
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

    Apply-DarkThemeRecursive -Control $dlg
    [void]$dlg.ShowDialog($script:MainForm)
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
        Save-CurrentBsp -OutputPath $dlg.FileName -InPlace:$false -CreateBackup:$false
        $script:PathBox.Text = $dlg.FileName
        Refresh-AllViews
        Update-Status "Saved: $($dlg.FileName)"
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
        [void](Save-BspInPlaceCore -ConfirmOverwrite:$true)
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
    $chkExtras.Text = 'Include optional extras in scan (nav, overviews, map txt, radar dds)'
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
        $fd = New-Object System.Windows.Forms.FolderBrowserDialog
        if (-not [string]::IsNullOrWhiteSpace($txtRoot.Text) -and (Test-Path $txtRoot.Text -PathType Container)) { $fd.SelectedPath = $txtRoot.Text }
        if ($fd.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $txtRoot.Text = $fd.SelectedPath }
    })

    Apply-DarkThemeRecursive -Control $dlg
    if ($dlg.ShowDialog($script:MainForm) -ne [System.Windows.Forms.DialogResult]::OK) { return }

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
        'Author: Ayrton'
        ''
        'Updated replacement for the classic PakRat workflow:'
        '- View/Edit/Add/Delete/Extract PAK files'
        '- Scan and auto-add from map references'
        '- Safer save and automatic backup'
        '- Dark UI with modern release packaging'
    ) -join "`r`n"
    Show-InfoDialog -Message $msg
}

$form = New-Object System.Windows.Forms.Form
$form.StartPosition = 'CenterScreen'
$form.Width = 1140
$form.Height = 760
$form.MinimumSize = New-Object System.Drawing.Size(980, 620)
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)
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

$listView = New-Object System.Windows.Forms.ListView
$listView.Dock = 'Fill'
$listView.View = 'Details'
$listView.HeaderStyle = 'None'
$listView.FullRowSelect = $true
$listView.MultiSelect = $true
$listView.GridLines = $false
$listView.HideSelection = $false
$listView.AllowDrop = $true
[void]$listView.Columns.Add('In', 40)
[void]$listView.Columns.Add('Name', 260)
[void]$listView.Columns.Add('Path', 480)
[void]$listView.Columns.Add('Size', 120)
[void]$listView.Columns.Add('Type', 150)
$mainPanel.Controls.Add($listView)
$script:ListView = $listView

$listHeaderPanel = New-Object System.Windows.Forms.Panel
$listHeaderPanel.Dock = 'Top'
$listHeaderPanel.Height = 24
$mainPanel.Controls.Add($listHeaderPanel)
$script:ListHeaderPanel = $listHeaderPanel

$headerLabels = @('In', 'Name', 'Path', 'Size', 'Type')
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
$browseGameBtn.Add_Click({ Browse-GameRoot })
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
        $res = [System.Windows.Forms.MessageBox]::Show('There are unsaved changes. Exit anyway?', 'PakRat Modern', [System.Windows.Forms.MessageBoxButtons]::YesNo, [System.Windows.Forms.MessageBoxIcon]::Warning)
        if ($res -ne [System.Windows.Forms.DialogResult]::Yes) { $_.Cancel = $true }
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

