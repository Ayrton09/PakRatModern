using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PakRatModern.Core;

namespace PakRatModern.App
{
    /// <summary>
    /// Ventana principal. El orden de acoplado replica el de la version previa:
    /// resumen del escaneo arriba de todo, luego los campos, luego el menu, la
    /// lista al centro, la botonera y por ultimo la barra de estado.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private readonly AppSettings _settings;
        private PakDocument _document;

        private TextBox _pathBox;
        private TextBox _gameRootBox;
        private DarkListView _listView;
        private ListHeaderPanel _listHeader;
        private TreeView _treeView;
        private ToolStripStatusLabel _statusLabel;
        private StatusStrip _statusStrip;

        private Label _summaryMissing;
        private Label _summaryCanAdd;
        private Label _summaryNotFound;
        private Label _summaryInPak;

        private ToolStripMenuItem _asTreeMenuItem;
        private int _sortColumn;
        private bool _sortDescending;
        private bool _treeMode;

        // Scan y guardado bombean mensajes para refrescar la barra de estado;
        // sin este guard el usuario podia lanzar otra operacion (o cerrar la
        // ventana) con el documento a medio procesar.
        private bool _busy;

        // Cambio en disco por el que ya se pregunto y el usuario eligio no
        // recargar: no se vuelve a preguntar cada vez que la ventana se activa.
        private FileStamp? _dismissedDiskChange;
        private bool _checkingDisk;

        public MainForm(AppSettings settings, string initialBsp)
        {
            _settings = settings;
            BuildUi();

            _gameRootBox.Text = _settings.GameRoot;

            if (!string.IsNullOrWhiteSpace(initialBsp) && File.Exists(initialBsp))
                OpenBsp(initialBsp);
            else
                SetStatus("Load a BSP to start.");
        }

        protected override void OnLoad(EventArgs e)
        {
            // El minimo se fija despues de escalar, para no depender de si
            // Scale() lo escala o no.
            var minimum = MinimumSize;
            MinimumSize = Size.Empty;
            DarkTheme.ScaleForDpi(this);
            MinimumSize = new Size(LogicalToDeviceUnits(minimum.Width), LogicalToDeviceUnits(minimum.Height));

            base.OnLoad(e);
            if (DeviceDpi != 96) CenterToScreen();
        }

        // ------------------------------------------------------------------ UI

        private void BuildUi()
        {
            Text = "PakRat Modern - No BSP loaded";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1124, 722);
            MinimumSize = new Size(980, 620);
            Font = DarkTheme.UiFont;
            BackColor = DarkTheme.Back;
            ForeColor = DarkTheme.Text;
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            FormClosing += OnFormClosing;
            Activated += (s, e) => CheckDiskChanges();
            LoadIcon();

            var mainPanel = BuildMainPanel();
            var bottomPanel = BuildBottomPanel();
            _statusStrip = BuildStatusStrip();
            var menu = BuildMenu();
            var topPanel = BuildTopPanel();
            var summaryPanel = BuildSummaryPanel();

            // Se agregan de adentro hacia afuera: el ultimo Dock=Top queda arriba.
            Controls.Add(mainPanel);
            Controls.Add(bottomPanel);
            Controls.Add(_statusStrip);
            Controls.Add(menu);
            Controls.Add(topPanel);
            Controls.Add(summaryPanel);
            MainMenuStrip = menu;

            DarkTheme.Apply(this);
            _listHeader.BackColor = DarkTheme.Header;
            RefreshSummary(new ScanSummary());
        }

        private void LoadIcon()
        {
            var iconPath = Path.Combine(AppPaths.AppDirectory, "pakrat_modern.ico");
            if (!File.Exists(iconPath)) return;

            try { Icon = new Icon(iconPath); }
            catch (Exception) { /* Un icono ilegible no debe impedir arrancar. */ }
        }

        private Panel BuildSummaryPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Top, Height = 28, Width = ClientSize.Width, BackColor = DarkTheme.Panel,
            };
            panel.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = DarkTheme.Border });

            _summaryMissing = SummaryLabel("Missing in BSP: 0", 10, 220);
            _summaryCanAdd = SummaryLabel("Can add: 0", 235, 220);
            _summaryNotFound = SummaryLabel("Not found: 0", 460, 200);
            _summaryInPak = SummaryLabel("Already in PAK: 0", 665, 180);

            panel.Controls.AddRange(new Control[] { _summaryMissing, _summaryCanAdd, _summaryNotFound, _summaryInPak });
            return panel;
        }

        private static Label SummaryLabel(string text, int left, int width) => new Label
        {
            Text = text,
            Left = left,
            Top = 6,
            Width = width,
            AutoSize = false,
            BackColor = Color.Transparent,
            ForeColor = DarkTheme.Text,
        };

        private Panel BuildTopPanel()
        {
            // El ancho se fija antes de agregar hijos: el anclaje a la derecha se
            // calcula contra el tamano que el panel tiene en ese momento, y si
            // todavia es el de por defecto los botones quedan fuera de la vista.
            var panel = new Panel
            {
                Dock = DockStyle.Top, Height = 78, Width = ClientSize.Width, BackColor = DarkTheme.Panel,
            };
            panel.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = DarkTheme.Border });

            _pathBox = new TextBox { AutoSize = false };
            var pathHost = new FieldBorder(_pathBox)
            {
                Left = 10, Top = 9, Width = 610, Height = 22,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            var browse = TopButton("Browse...", 626, 8, 82, AnchorStyles.Top | AnchorStyles.Right);
            browse.Click += (s, e) => BrowseBsp();

            var save = TopButton("Save BSP", 798, 8, 78, AnchorStyles.Top | AnchorStyles.Right);
            save.Click += (s, e) => SaveInPlace();

            var saveAs = TopButton("Save As...", 882, 8, 78, AnchorStyles.Top | AnchorStyles.Right);
            saveAs.Click += (s, e) => SaveAs();

            var verify = TopButton("Verify", 966, 8, 70, AnchorStyles.Top | AnchorStyles.Right);
            verify.Click += (s, e) => VerifyPak();

            var gameLabel = new Label
            {
                Text = "Game Path:", Left = 10, Top = 46, Width = 75,
                AutoSize = false, BackColor = Color.Transparent,
            };

            _gameRootBox = new TextBox { AutoSize = false };
            _gameRootBox.Leave += (s, e) => PersistGameRoot();

            var gameHost = new FieldBorder(_gameRootBox)
            {
                Left = 84, Top = 41, Width = 790, Height = 22,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            var browseGame = TopButton("Browse...", 882, 40, 78, AnchorStyles.Top | AnchorStyles.Right);
            browseGame.Click += (s, e) => BrowseGameRoot();

            var scan = TopButton("Scan", 966, 40, 70, AnchorStyles.Top | AnchorStyles.Right);
            scan.Click += (s, e) => RunScan();

            var paths = TopButton("Paths...", 1042, 40, 80, AnchorStyles.Top | AnchorStyles.Right);
            paths.Click += (s, e) => ShowPreferences();

            panel.Controls.AddRange(new Control[]
            {
                pathHost, browse, save, saveAs, verify,
                gameLabel, gameHost, browseGame, scan, paths,
            });

            return panel;
        }

        private static Button TopButton(string text, int left, int top, int width, AnchorStyles anchor) => new Button
        {
            Text = text, Left = left, Top = top, Width = width, Height = 24, Anchor = anchor,
        };

        private MenuStrip BuildMenu()
        {
            var menu = new MenuStrip { Dock = DockStyle.Top, BackColor = DarkTheme.Panel };

            var file = new ToolStripMenuItem("File");
            file.DropDownItems.Add(MenuItem("Open BSP...", Keys.Control | Keys.O, BrowseBsp));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuItem("Save BSP", Keys.Control | Keys.S, SaveInPlace));
            file.DropDownItems.Add(MenuItem("Save BSP As...", Keys.Control | Keys.Shift | Keys.S, SaveAs));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuItem("Exit", Keys.None, Close));

            var view = new ToolStripMenuItem("View");
            _asTreeMenuItem = MenuItem("As Tree", Keys.Control | Keys.L, ToggleView);
            _asTreeMenuItem.CheckOnClick = false;
            view.DropDownItems.Add(_asTreeMenuItem);
            view.DropDownItems.Add(MenuItem("Refresh", Keys.F5, RefreshViews));

            var tools = new ToolStripMenuItem("Tools");
            tools.DropDownItems.Add(MenuItem("Add files...", Keys.Control | Keys.F, AddFiles));
            tools.DropDownItems.Add(MenuItem("Add folder...", Keys.Control | Keys.D, AddFolder));
            tools.DropDownItems.Add(new ToolStripSeparator());
            tools.DropDownItems.Add(MenuItem("Scan", Keys.F6, RunScan));
            tools.DropDownItems.Add(MenuItem("Auto Add", Keys.F7, RunAutoAdd));
            tools.DropDownItems.Add(MenuItem("Verify PAK", Keys.Control | Keys.T, VerifyPak));
            tools.DropDownItems.Add(new ToolStripSeparator());
            tools.DropDownItems.Add(MenuItem("Manage Game Paths", Keys.None, ShowGamePaths));
            tools.DropDownItems.Add(MenuItem("Preferences...", Keys.None, ShowPreferences));

            var help = new ToolStripMenuItem("Help");
            help.DropDownItems.Add(MenuItem("About", Keys.None, ShowAbout));

            menu.Items.AddRange(new ToolStripItem[] { file, view, tools, help });
            return menu;
        }

        private static ToolStripMenuItem MenuItem(string text, Keys shortcut, Action onClick)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) => onClick();
            if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
            return item;
        }

        private Panel BuildMainPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = DarkTheme.Input };

            _listView = new DarkListView();
            _listView.Columns.Add("Name", 300);
            _listView.Columns.Add("Path", 520);
            _listView.Columns.Add("Size", 120);
            _listView.Columns.Add("Type", 150);
            _listView.DoubleClick += (s, e) => EditSelectedPath();
            _listView.ContextMenuStrip = BuildEntryContextMenu();
            _listView.DragEnter += OnDragEnter;
            _listView.DragDrop += OnDragDrop;

            var body = new Panel { Dock = DockStyle.Fill, BackColor = DarkTheme.Input };
            body.Controls.Add(_listView);

            _treeView = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                AllowDrop = true,
                Visible = false,
                BorderStyle = BorderStyle.None,
                BackColor = DarkTheme.Input,
                ForeColor = DarkTheme.Text,
            };

            _listHeader = new ListHeaderPanel(_listView);
            _listHeader.ColumnClicked += SetSort;

            panel.Controls.Add(body);
            panel.Controls.Add(_treeView);
            panel.Controls.Add(_listHeader);
            return panel;
        }

        private ContextMenuStrip BuildEntryContextMenu()
        {
            var menu = new ContextMenuStrip { BackColor = DarkTheme.Panel, ForeColor = DarkTheme.Text };
            menu.Items.Add(MenuItem("Edit internal path...", Keys.None, EditSelectedPath));
            menu.Items.Add(MenuItem("Extract selected...", Keys.None, ExtractSelected));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuItem("Delete selected", Keys.None, DeleteSelected));
            return menu;
        }

        private Panel BuildBottomPanel()
        {
            var panel = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = DarkTheme.Panel };
            panel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = DarkTheme.Border });

            var buttons = new (string Text, int Left, int Width, Action OnClick)[]
            {
                ("View",      10,  72, ViewSelected),
                ("Edit",      88,  72, EditSelectedPath),
                ("Add",       166, 72, null),          // abre su propio desplegable
                ("Delete",    244, 72, DeleteSelected),
                ("Extract",   322, 72, ExtractSelected),
                ("Scan",      400, 72, RunScan),
                ("Auto",      478, 72, RunAutoAdd),
                ("Tree/List", 556, 82, ToggleView),
            };

            foreach (var (text, left, width, onClick) in buttons)
            {
                var button = new Button { Text = text, Left = left, Top = 12, Width = width, Height = 26 };

                if (onClick == null) button.Click += (s, e) => ShowAddMenu(button);
                else button.Click += (s, e) => onClick();

                panel.Controls.Add(button);
            }

            return panel;
        }

        private StatusStrip BuildStatusStrip()
        {
            var strip = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = false, BackColor = DarkTheme.Panel };
            _statusLabel = new ToolStripStatusLabel
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = DarkTheme.MutedText,
            };
            strip.Items.Add(_statusLabel);
            return strip;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTheme.ApplyTitleBar(this);
        }

        // ----------------------------------------------------------- Documento

        private void OpenBsp(string path, bool confirmDiscard = true)
        {
            if (confirmDiscard && !ConfirmDiscardChanges()) return;

            try
            {
                SetStatusImmediate($"Loading BSP: {path}");
                _document = PakDocument.Open(path);
                _dismissedDiskChange = null;
                _pathBox.Text = _document.Path;
                RefreshViews();
                RefreshSummary(new ScanSummary());
                SetStatus($"BSP loaded: {_document.Entries.Count} entries, {FormatSize(_document.TotalSize)}");
                WarnAboutSkippedEntries();
            }
            catch (Exception ex)
            {
                ShowError("Could not open BSP", ex);
                SetStatus("Load a BSP to start.");
            }
        }

        private void RefreshViews()
        {
            RefreshListView();
            RefreshTreeView();
            UpdateTitle();
        }

        /// <summary>
        /// Estas entradas venian con rutas inseguras y no se cargaron. Guardar
        /// las quita del mapa, asi que el usuario tiene que enterarse.
        /// </summary>
        private void WarnAboutSkippedEntries()
        {
            var skipped = _document.SkippedEntries;
            if (skipped != null && skipped.Count > 0)
            {
                MessageBox.Show(this,
                    $"{skipped.Count} entr{(skipped.Count == 1 ? "y" : "ies")} in this PAK had unsafe paths " +
                    "and were skipped:\n\n" + Sample(skipped) +
                    "\n\nThey are not loaded, and saving the BSP will remove them from the map.",
                    "Unsafe entries skipped", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            var duplicates = _document.DuplicateEntries;
            if (duplicates != null && duplicates.Count > 0)
            {
                MessageBox.Show(this,
                    $"{duplicates.Count} duplicate entr{(duplicates.Count == 1 ? "y was" : "ies were")} merged:\n\n" +
                    Sample(duplicates) +
                    "\n\nThe engine cannot tell them apart, so only one copy is kept. " +
                    "Saving the BSP will drop the redundant copies and make the file smaller.",
                    "Duplicate entries merged", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        internal static string Sample(IReadOnlyList<string> names)
        {
            var sample = string.Join("\n", names.Take(8));
            return names.Count > 8 ? sample + $"\n... and {names.Count - 8} more" : sample;
        }

        private IEnumerable<PakEntry> SortedEntries()
        {
            if (_document == null) return Enumerable.Empty<PakEntry>();

            IEnumerable<PakEntry> sorted;
            switch (_sortColumn)
            {
                case 1: sorted = _document.Entries.Values.OrderBy(e => e.Directory, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase); break;
                case 2: sorted = _document.Entries.Values.OrderBy(e => e.Size); break;
                case 3: sorted = _document.Entries.Values.OrderBy(GetEntryType, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase); break;
                default: sorted = _document.Entries.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase); break;
            }

            return _sortDescending ? sorted.Reverse() : sorted;
        }

        private void SetSort(int column)
        {
            if (column == _sortColumn) _sortDescending = !_sortDescending;
            else { _sortColumn = column; _sortDescending = false; }

            _listHeader.SetSort(_sortColumn, _sortDescending);
            RefreshListView();
        }

        private void RefreshListView()
        {
            _listView.BeginUpdate();
            try
            {
                _listView.Items.Clear();
                foreach (var entry in SortedEntries())
                {
                    _listView.Items.Add(new ListViewItem(new[]
                    {
                        entry.Name,
                        entry.Directory,
                        FormatSize(entry.Size),
                        GetEntryType(entry),
                    })
                    {
                        Tag = entry.FullPath,
                        ForeColor = DarkTheme.Text,
                    });
                }
            }
            finally
            {
                _listView.EndUpdate();
            }
        }

        private void RefreshTreeView()
        {
            _treeView.BeginUpdate();
            try
            {
                _treeView.Nodes.Clear();
                if (_document == null) return;

                var folders = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in _document.SortedEntries)
                {
                    var parent = EnsureFolderNode(folders, entry.Directory);
                    var node = new TreeNode(entry.Name) { Tag = entry.FullPath };
                    (parent?.Nodes ?? _treeView.Nodes).Add(node);
                }
            }
            finally
            {
                _treeView.EndUpdate();
            }
        }

        private TreeNode EnsureFolderNode(IDictionary<string, TreeNode> folders, string directory)
        {
            if (string.IsNullOrEmpty(directory)) return null;
            if (folders.TryGetValue(directory, out var existing)) return existing;

            var slash = directory.LastIndexOf('/');
            var parent = EnsureFolderNode(folders, slash < 0 ? string.Empty : directory.Substring(0, slash));
            var node = new TreeNode(slash < 0 ? directory : directory.Substring(slash + 1));

            (parent?.Nodes ?? _treeView.Nodes).Add(node);
            folders[directory] = node;
            return node;
        }

        private void ToggleView()
        {
            _treeMode = !_treeMode;
            _treeView.Visible = _treeMode;
            _listHeader.Visible = !_treeMode;
            if (_treeMode) _treeView.BringToFront();

            _asTreeMenuItem.Checked = _treeMode;
            SetStatus(_treeMode ? "Tree view" : "List view");
        }

        private void UpdateTitle()
        {
            Text = _document == null
                ? "PakRat Modern - No BSP loaded"
                : $"PakRat Modern - {Path.GetFileName(_document.Path)}{(_document.IsDirty ? " *" : string.Empty)}";
        }

        private void RefreshSummary(ScanSummary summary)
        {
            _summaryMissing.Text = $"Missing in BSP: {summary.MissingTotal}";
            _summaryCanAdd.Text = $"Can add: {summary.CanAdd}";
            _summaryNotFound.Text = $"Not found: {summary.NotFound}";
            _summaryInPak.Text = $"Already in PAK: {summary.AlreadyInPak}";
        }

        private IReadOnlyList<string> GetSelectedPaths()
        {
            if (_treeMode)
            {
                var tag = _treeView.SelectedNode?.Tag as string;
                return tag == null ? Array.Empty<string>() : new[] { tag };
            }

            return _listView.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToList();
        }

        // ------------------------------------------------------------ Acciones

        private void BrowseBsp()
        {
            using (var dialog = new OpenFileDialog { Filter = "Source BSP (*.bsp)|*.bsp|All files (*.*)|*.*" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) OpenBsp(dialog.FileName);
            }
        }

        private void BrowseGameRoot()
        {
            var actual = CurrentGameRoot();
            var inicial = Directory.Exists(actual) ? actual : _settings.GameRoot;

            var elegido = FolderPicker.Pick(this, "Select Game Path folder (hl2/cstrike/etc)", inicial);
            if (elegido == null) return;

            _gameRootBox.Text = elegido;
            PersistGameRoot();
        }

        private void PersistGameRoot()
        {
            var value = _gameRootBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(value)) return;
            if (string.Equals(value, _settings.GameRoot, StringComparison.OrdinalIgnoreCase)) return;

            _settings.RememberGameRoot(value);
            _settings.Save();
        }

        private void ViewSelected()
        {
            if (!RequireDocument()) return;

            var selected = GetSelectedPaths();
            if (selected.Count != 1) { SetStatus("Select one entry to view"); return; }

            if (!_document.Entries.TryGetValue(selected[0], out var entry)) return;
            using (var viewer = new EntryViewerForm(entry)) viewer.ShowDialog(this);
        }

        private void AddFiles()
        {
            if (!RequireDocument()) return;

            using (var dialog = new OpenFileDialog { Multiselect = true, Filter = "All files (*.*)|*.*" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) AddPaths(dialog.FileNames);
            }
        }

        private void AddFolder()
        {
            if (!RequireDocument()) return;

            var carpeta = FolderPicker.Pick(this, "Select a folder to add", CurrentGameRoot());
            if (carpeta != null) AddPaths(new[] { carpeta });
        }

        /// <summary>El boton Add ofrece archivos o carpeta, como el menu Tools.</summary>
        private void ShowAddMenu(Control anchor)
        {
            if (!RequireDocument()) return;

            var menu = new ContextMenuStrip { BackColor = DarkTheme.Panel, ForeColor = DarkTheme.Text };
            menu.Items.Add(MenuItem("Add files...", Keys.None, AddFiles));
            menu.Items.Add(MenuItem("Add folder...", Keys.None, AddFolder));
            DarkTheme.Apply(menu);

            menu.Show(anchor, new Point(0, anchor.Height));
        }

        private void AddPaths(IEnumerable<string> paths)
        {
            if (!RequireDocument()) return;

            // Una carpeta agregada entera ancla las rutas: la carpeta que
            // equivale a la raiz del PAK se busca una vez, no archivo por archivo.
            var files = new List<(string File, string ContentRoot)>();
            try
            {
                foreach (var path in paths)
                {
                    if (File.Exists(path))
                    {
                        files.Add((path, null));
                    }
                    else if (Directory.Exists(path))
                    {
                        var contentRoot = ArchivePath.FindContentRoot(path);
                        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                            files.Add((file, contentRoot));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ShowError("Could not read the folder", ex);
                return;
            }

            if (files.Count == 0) return;

            var gameRoot = CurrentGameRoot();
            var planned = new List<(string File, string ArchivePath)>();
            var unmapped = new List<string>();

            foreach (var (file, contentRoot) in files)
            {
                string archivePath = null;
                try { archivePath = ArchivePath.FromDiskPath(file, gameRoot, contentRoot); }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { }

                if (archivePath == null) unmapped.Add(file);
                else planned.Add((file, archivePath));
            }

            // Fuera del Game Path la ruta interna se deduce de los nombres de
            // carpeta. Se muestra antes de agregar: una ruta mal deducida da un
            // mapa con texturas rosas y ningun error.
            var outside = planned.Where(p => !ArchivePath.IsInside(p.File, gameRoot)).ToList();
            if (outside.Count > 0 && !ConfirmDeducedPaths(outside))
            {
                SetStatus("Add cancelled");
                return;
            }

            int added = 0, replaced = 0;
            var unreadable = new List<string>();

            foreach (var (file, archivePath) in planned)
            {
                try
                {
                    if (_document.AddOrReplace(archivePath, File.ReadAllBytes(file))) replaced++;
                    else added++;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
                {
                    unreadable.Add(file);
                }
            }

            RefreshViews();
            var notAdded = unmapped.Count + unreadable.Count;
            SetStatus($"Added {added}, replaced {replaced}" + (notAdded > 0 ? $", skipped {notAdded}" : string.Empty));

            if (unmapped.Count > 0)
            {
                MessageBox.Show(this,
                    "These files could not be mapped to an internal PAK path.\n\n" +
                    "Set Game Path to the folder containing materials/ and models/, or add the folder that contains them, then try again:\n\n" +
                    Sample(unmapped),
                    "Some files were skipped", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (unreadable.Count > 0)
            {
                MessageBox.Show(this,
                    "These files could not be read from disk:\n\n" + Sample(unreadable),
                    "Some files were not added", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool ConfirmDeducedPaths(IReadOnlyList<(string File, string ArchivePath)> outside)
        {
            var lines = outside.Select(p => $"{Path.GetFileName(p.File)}  ->  {p.ArchivePath}").ToList();

            return MessageBox.Show(this,
                $"{outside.Count} file{(outside.Count == 1 ? " is" : "s are")} outside the Game Path, so the internal path " +
                "was deduced from the folder names:\n\n" + Sample(lines) +
                "\n\nAdd them with these paths?",
                "Check the internal paths", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void DeleteSelected()
        {
            if (!RequireDocument()) return;

            var selected = GetSelectedPaths();
            if (selected.Count == 0) return;

            var answer = MessageBox.Show(this,
                $"Delete {selected.Count} entr{(selected.Count == 1 ? "y" : "ies")} from the PAK?",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;

            var removed = selected.Count(p => _document.Remove(p));
            RefreshViews();
            SetStatus($"Deleted {removed}");
        }

        private void EditSelectedPath()
        {
            if (!RequireDocument()) return;

            var selected = GetSelectedPaths();
            if (selected.Count != 1) { SetStatus("Select one entry to edit its path"); return; }

            var current = selected[0];
            var updated = PromptDialog.Show(this, "Edit internal path", "Internal PAK path:", current);
            if (updated == null || updated == current) return;

            try
            {
                _document.Rename(current, updated);
                RefreshViews();
                SetStatus($"Renamed to {updated}");
            }
            catch (Exception ex)
            {
                ShowError("Could not rename entry", ex);
            }
        }

        private void ExtractSelected()
        {
            if (!RequireDocument()) return;

            var selected = GetSelectedPaths();
            if (selected.Count == 0) return;

            var destino = FolderPicker.Pick(this, "Select destination folder for extraction", CurrentGameRoot());
            if (destino == null) return;

            try
            {
                var overwrite = true;
                var existing = _document.FindExistingExtractionTargets(destino, selected);
                if (existing.Count > 0)
                {
                    var answer = MessageBox.Show(this,
                        $"{existing.Count} file{(existing.Count == 1 ? string.Empty : "s")} already exist in the destination:\n\n" +
                        Sample(existing) +
                        "\n\nYes: overwrite them.\nNo: keep the existing files and extract only the rest.",
                        "Files already exist", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

                    if (answer == DialogResult.Cancel) return;
                    overwrite = answer == DialogResult.Yes;
                }

                var written = _document.ExtractTo(destino, selected, overwrite);
                var skipped = selected.Count - written;
                SetStatus($"Extracted: {written}" + (skipped > 0 ? $", kept existing: {skipped}" : string.Empty));
            }
            catch (Exception ex)
            {
                ShowError("Could not extract", ex);
            }
        }

        private void VerifyPak()
        {
            if (!RequireDocument()) return;

            var (ok, message) = _document.Verify();
            MessageBox.Show(this, message, ok ? "PAK is valid" : "PAK is not valid",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            SetStatus(message);
        }

        // -------------------------------------------------------------- Guardar

        private void SaveInPlace()
        {
            if (!RequireDocument()) return;
            SaveTo(_document.Path);
        }

        private void SaveAs()
        {
            if (!RequireDocument()) return;

            using (var dialog = new SaveFileDialog
            {
                Filter = "Source BSP (*.bsp)|*.bsp",
                FileName = Path.GetFileName(_document.Path),
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) SaveTo(dialog.FileName);
            }
        }

        private void SaveTo(string path)
        {
            if (!BeginBusy()) return;

            var reload = false;
            try
            {
                SetStatusImmediate("Saving...");
                if (TrySave(path, out reload))
                {
                    _pathBox.Text = _document.Path;
                    UpdateTitle();
                    SetStatus($"Saved: {path}");
                }
            }
            catch (Exception ex)
            {
                ShowError("Could not save BSP", ex);
                SetStatus("Save failed");
            }
            finally
            {
                EndBusy();
            }

            if (reload) ReloadFromDisk();
        }

        /// <summary>
        /// Guarda. Si el BSP cambio en disco desde que se abrio (una
        /// recompilacion desde Hammer), pregunta antes de pisarlo: el documento
        /// tiene la version vieja completa, geometria incluida.
        /// </summary>
        private bool TrySave(string path, out bool reload)
        {
            reload = false;
            try
            {
                _document.Save(path, _settings.BackupBeforeInPlaceSave);
                return true;
            }
            catch (FileChangedOnDiskException)
            {
                var answer = MessageBox.Show(this,
                    $"{Path.GetFileName(path)} changed on disk after it was opened here (for example, it was recompiled).\n\n" +
                    "Saving now would replace it with the older version loaded in PakRat Modern.\n\n" +
                    "Yes: reload the BSP from disk (changes made here since opening are lost).\n" +
                    "No: overwrite it anyway.\n" +
                    "Cancel: do nothing.",
                    "BSP changed on disk", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

                if (answer == DialogResult.No)
                {
                    _document.Save(path, _settings.BackupBeforeInPlaceSave, overwriteChangedFile: true);
                    return true;
                }

                reload = answer == DialogResult.Yes;
                SetStatus(reload ? "Save cancelled: reloading the BSP from disk" : "Save cancelled");
                return false;
            }
        }

        /// <summary>
        /// Al volver a la ventana, si el BSP cambio en disco, se ofrece recargarlo
        /// antes de que Scan, Auto o Save trabajen sobre la version vieja. Si el
        /// usuario dice que no, no se vuelve a preguntar por ese mismo cambio;
        /// guardar encima igual pide confirmacion.
        /// </summary>
        private void CheckDiskChanges()
        {
            if (_document == null || _busy || _checkingDisk) return;

            _checkingDisk = true;
            try
            {
                if (!_document.HasChangedOnDisk(out var current)) return;
                if (current.HasValue && _dismissedDiskChange.HasValue && current.Value.Equals(_dismissedDiskChange.Value)) return;

                var answer = MessageBox.Show(this,
                    $"{Path.GetFileName(_document.Path)} changed on disk after it was opened here (for example, it was recompiled).\n\n" +
                    "Reload it now?" + (_document.IsDirty ? " Changes made here since opening will be lost." : string.Empty),
                    "BSP changed on disk", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (answer == DialogResult.Yes) ReloadFromDisk();
                else _dismissedDiskChange = current;
            }
            finally
            {
                _checkingDisk = false;
            }
        }

        private void ReloadFromDisk()
        {
            if (_document == null) return;
            OpenBsp(_document.Path, confirmDiscard: false);
        }

        /// <summary>
        /// Bloquea la ventana mientras dura una operacion que bombea mensajes.
        /// Devuelve false si ya hay una en curso.
        /// </summary>
        private bool BeginBusy()
        {
            if (_busy) return false;
            _busy = true;
            Enabled = false;
            UseWaitCursor = true;
            return true;
        }

        private void EndBusy()
        {
            _busy = false;
            UseWaitCursor = false;
            Enabled = true;
        }

        // ----------------------------------------------------------------- Scan

        private ScanResult RunScanCore()
        {
            var gameRoot = CurrentGameRoot();
            if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            {
                MessageBox.Show(this, "Set a valid Game Path first (the folder with gameinfo.txt).",
                    "Game Path required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                BrowseGameRoot();
                return null;
            }

            if (!BeginBusy()) return null;
            try
            {
                var service = new ScanService(_document.Entries, gameRoot);
                var result = service.Scan(_document.Bsp, _document.MapName,
                    _settings.IncludeExtrasInScan, SetStatusImmediate);

                RefreshSummary(result.Summary);
                return result;
            }
            finally
            {
                EndBusy();
            }
        }

        private void RunScan()
        {
            if (!RequireDocument()) return;

            try
            {
                var result = RunScanCore();
                if (result == null) return;

                SetStatus($"Scan complete: {result.Rows.Count} references");

                using (var dialog = new ScanResultsForm(result, CurrentGameRoot()))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK && dialog.RowsToAdd.Count > 0)
                        AddScannedFiles(dialog.RowsToAdd);
                }
            }
            catch (Exception ex)
            {
                ShowError("Scan failed", ex);
            }
        }

        /// <summary>Escanea y empaqueta todo lo agregable sin pasar por el dialogo.</summary>
        private void RunAutoAdd()
        {
            if (!RequireDocument()) return;

            try
            {
                var result = RunScanCore();
                if (result == null) return;

                var addable = result.Rows.Where(r => r.Addable).ToList();
                if (addable.Count == 0)
                {
                    SetStatus("Nothing to add");
                    MessageBox.Show(this, "No missing files could be added automatically.",
                        "Auto add", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                AddScannedFiles(addable);
            }
            catch (Exception ex)
            {
                ShowError("Auto add failed", ex);
            }
        }

        /// <summary>
        /// Empaqueta filas del scan. Se usa la ruta de disco que el scan
        /// resolvio (puede estar en otro SearchPath, no solo en el Game Path).
        /// </summary>
        private void AddScannedFiles(IReadOnlyList<ScanRow> rows)
        {
            var added = 0;
            var failed = new List<string>();

            foreach (var row in rows)
            {
                var diskPath = row.FullDiskPath;
                if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath)) continue;

                try
                {
                    _document.AddOrReplace(row.Path, File.ReadAllBytes(diskPath));
                    added++;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    failed.Add(row.Path);
                }
            }

            RefreshViews();
            SetStatus($"Added {added} file(s) from scan" + (failed.Count > 0 ? $", {failed.Count} could not be read" : string.Empty));

            if (failed.Count > 0)
            {
                MessageBox.Show(this,
                    "These files could not be read from disk:\n\n" + Sample(failed),
                    "Some files were not added", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // -------------------------------------------------------------- Dialogos

        private void ShowGamePaths()
        {
            using (var dialog = new GamePathsForm(_settings))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                _settings.Save();
                if (!string.IsNullOrWhiteSpace(_settings.GameRoot)) _gameRootBox.Text = _settings.GameRoot;
            }
        }

        private void ShowPreferences()
        {
            using (var dialog = new PreferencesForm(_settings))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                _settings.Save();
                if (!string.IsNullOrWhiteSpace(_settings.GameRoot)) _gameRootBox.Text = _settings.GameRoot;
            }
        }

        private void ShowAbout()
        {
            var version = typeof(MainForm).Assembly.GetName().Version;
            MessageBox.Show(this,
                $"PakRat Modern {version.Major}.{version.Minor}.{version.Build}\n\n" +
                "Editor of the PAKFILE lump in Source .bsp maps.\n" +
                "Native build: no PowerShell runtime, no embedded script.\n\n" +
                $"Settings: {AppPaths.SettingsPath}",
                "About PakRat Modern", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // -------------------------------------------------------- Drag and drop

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] paths) || paths.Length == 0) return;

            if (paths.Length == 1 && paths[0].EndsWith(".bsp", StringComparison.OrdinalIgnoreCase))
            {
                OpenBsp(paths[0]);
                return;
            }

            AddPaths(paths);
        }

        // --------------------------------------------------------------- Varios

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_busy || !ConfirmDiscardChanges()) e.Cancel = true;
        }

        private bool ConfirmDiscardChanges()
        {
            if (_document == null || !_document.IsDirty) return true;

            return MessageBox.Show(this,
                "There are unsaved changes. Continue and discard them?",
                "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private bool RequireDocument()
        {
            if (_document != null) return true;
            SetStatus("Load a BSP to start.");
            return false;
        }

        private string CurrentGameRoot() => _gameRootBox.Text?.Trim() ?? string.Empty;

        private void SetStatus(string message) => _statusLabel.Text = message;

        private void SetStatusImmediate(string message)
        {
            _statusLabel.Text = message;
            _statusStrip.Refresh();
            Application.DoEvents();
        }

        private void ShowError(string title, Exception ex) =>
            MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);

        internal static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
            return $"{bytes / (1024.0 * 1024.0):0.0} MB";
        }

        /// <summary>Nombres de tipo mostrados en la columna Type.</summary>
        private static readonly Dictionary<string, string> EntryTypeNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".vmt"] = "Material",
                [".vtf"] = "Texture",
                [".dds"] = "Radar Texture",
                [".mdl"] = "Model",
                [".vvd"] = "Model data",
                [".vtx"] = "Model mesh",
                [".phy"] = "Model physics",
                [".nav"] = "Navigation Mesh",
                [".ain"] = "Node Graph",
                [".wav"] = "Sound",
                [".mp3"] = "Sound",
                [".pcf"] = "Particles",
                [".txt"] = "Text",
            };

        private static string GetEntryType(PakEntry entry)
        {
            var dot = entry.FullPath.LastIndexOf('.');
            if (dot < 0) return "File";

            var extension = entry.FullPath.Substring(dot);
            return EntryTypeNames.TryGetValue(extension, out var name) ? name : "File";
        }
    }
}
