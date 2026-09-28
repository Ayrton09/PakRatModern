using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PakRatModern.Core;

namespace PakRatModern.App
{
    /// <summary>Base con el tema ya aplicado, para no repetirlo en cada dialogo.</summary>
    internal class DarkDialog : Form
    {
        public DarkDialog(string title, int width, int height, bool resizable = false)
        {
            Text = title;
            ClientSize = new Size(width, height);
            StartPosition = FormStartPosition.CenterParent;
            Font = DarkTheme.UiFont;
            BackColor = DarkTheme.Back;
            ForeColor = DarkTheme.Text;
            ShowInTaskbar = false;

            if (!resizable)
            {
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            // Antes de base.OnLoad: ahi se centra el dialogo, y tiene que ser
            // con el tamano ya escalado.
            DarkTheme.ScaleForDpi(this);
            base.OnLoad(e);
            DarkTheme.Apply(this);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTheme.ApplyTitleBar(this);
        }
    }

    /// <summary>Pedido de un texto en una linea.</summary>
    internal static class PromptDialog
    {
        public static string Show(IWin32Window owner, string title, string prompt, string initialValue)
        {
            using (var form = new DarkDialog(title, 560, 128))
            {
                var label = new Label { Text = prompt, AutoSize = true, Left = 12, Top = 14 };

                var input = new TextBox { Text = initialValue };
                var inputHost = new FieldBorder(input) { Left = 12, Top = 38, Width = 536, Height = 22 };

                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 376, Top = 82, Width = 84, Height = 26 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 466, Top = 82, Width = 84, Height = 26 };

                form.Controls.AddRange(new Control[] { label, inputHost, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                if (form.ShowDialog(owner) != DialogResult.OK) return null;

                var value = input.Text?.Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
    }

    /// <summary>Vista previa del contenido de una entrada.</summary>
    internal sealed class EntryViewerForm : DarkDialog
    {
        private const int PreviewBytes = 4096;

        public EntryViewerForm(PakEntry entry) : base($"View: {entry.FullPath}", 760, 560, resizable: true)
        {
            var info = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = $"{entry.FullPath}   |   {MainForm.FormatSize(entry.Size)}",
                ForeColor = DarkTheme.MutedText,
                Padding = new Padding(4, 4, 0, 0),
            };

            var text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9f),
                BorderStyle = BorderStyle.None,
                BackColor = DarkTheme.Input,
                ForeColor = DarkTheme.Text,
                Text = Render(entry.Data),
            };

            var close = new Button { Text = "Close", Dock = DockStyle.Bottom, Height = 30, DialogResult = DialogResult.OK };

            Controls.Add(text);
            Controls.Add(info);
            Controls.Add(close);
            CancelButton = close;
        }

        /// <summary>
        /// Los archivos de texto se muestran tal cual; los binarios como volcado
        /// hexadecimal, que es lo unico util para un .vtf o un .mdl.
        /// </summary>
        private static string Render(byte[] data)
        {
            if (data.Length == 0) return "(empty)";

            var slice = data.Take(PreviewBytes).ToArray();
            var truncated = data.Length > PreviewBytes;

            if (LooksLikeText(slice))
            {
                var text = Encoding.UTF8.GetString(slice);
                return truncated ? text + $"\r\n\r\n... ({MainForm.FormatSize(data.Length)} total)" : text;
            }

            var builder = new StringBuilder();
            for (var offset = 0; offset < slice.Length; offset += 16)
            {
                var count = Math.Min(16, slice.Length - offset);
                var hex = string.Join(" ", slice.Skip(offset).Take(count).Select(b => b.ToString("x2")));
                var ascii = new string(slice.Skip(offset).Take(count)
                    .Select(b => b >= 32 && b < 127 ? (char)b : '.').ToArray());

                builder.AppendLine($"{offset:x8}  {hex.PadRight(47)}  {ascii}");
            }

            if (truncated) builder.AppendLine($"\r\n... ({MainForm.FormatSize(data.Length)} total)");
            return builder.ToString();
        }

        private static bool LooksLikeText(byte[] data)
        {
            var control = data.Count(b => b < 9 || (b > 13 && b < 32));
            return control * 20 < data.Length;
        }
    }

    /// <summary>Resultados del escaneo, con seleccion de que empaquetar.</summary>
    internal sealed class ScanResultsForm : DarkDialog
    {
        private readonly DarkListView _list;
        private readonly ScanResult _result;

        public List<ScanRow> RowsToAdd { get; } = new List<ScanRow>();

        public ScanResultsForm(ScanResult result, string gameRoot)
            : base("Scan results", 940, 600, resizable: true)
        {
            _result = result;

            _list = new DarkListView { CheckBoxes = true, AllowDrop = false };
            _list.Columns.Add("File", 520);
            _list.Columns.Add("Status", 150);
            _list.Columns.Add("On disk", 200);

            foreach (var row in result.Rows)
            {
                _list.Items.Add(new ListViewItem(new[] { row.Path, row.StatusText, row.ExistsOnDisk ? "yes" : "no" })
                {
                    Tag = row,
                    Checked = row.Addable,   // solo se premarca lo que se puede empaquetar
                    ForeColor = ColorFor(row.Status),
                });
            }

            // El ancho va antes de agregar hijos: el anclaje a la derecha se
            // calcula contra el tamano que el panel tiene en ese momento.
            var bottom = new Panel
            {
                Dock = DockStyle.Bottom, Height = 96, Width = ClientSize.Width, BackColor = DarkTheme.Panel,
            };
            bottom.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = DarkTheme.Border });

            var summary = new Label
            {
                Left = 10, Top = 10, Width = 900, Height = 18, AutoSize = false,
                Text = $"Missing in BSP: {result.Summary.MissingTotal}     Can add: {result.Summary.CanAdd}     " +
                       $"Not found: {result.Summary.NotFound}     Already in PAK: {result.Summary.AlreadyInPak}",
            };

            // AutoEllipsis: una ruta larga sin espacios cortaba de linea y, con
            // una sola linea de alto, desaparecia entera. Asi se ve el comienzo y
            // el tooltip muestra la ruta completa.
            var hint = new Label
            {
                Left = 10, Top = 30, Width = 900, Height = 18, AutoSize = false, AutoEllipsis = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = DarkTheme.MutedText,
                Text = $"Game Path: {gameRoot}",
            };

            var selectAddable = Button("Select addable", 10, 58, 120, () => SetChecked(r => r.Addable));
            var selectNone = Button("Select none", 138, 58, 100, () => SetChecked(r => false));
            var export = Button("Export...", 246, 58, 90, Export);

            var add = new Button
            {
                Text = "Add checked", Left = 700, Top = 58, Width = 120, Height = 26,
                Anchor = AnchorStyles.Top | AnchorStyles.Right, DialogResult = DialogResult.OK,
            };
            add.Click += (s, e) => CollectChecked();

            var close = new Button
            {
                Text = "Close", Left = 828, Top = 58, Width = 90, Height = 26,
                Anchor = AnchorStyles.Top | AnchorStyles.Right, DialogResult = DialogResult.Cancel,
            };

            bottom.Controls.AddRange(new Control[] { summary, hint, selectAddable, selectNone, export, add, close });

            // La lista y su encabezado van juntos en un contenedor, igual que en
            // la ventana principal.
            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = DarkTheme.Input };
            listHost.Controls.Add(_list);
            listHost.Controls.Add(new ListHeaderPanel(_list));

            Controls.Add(listHost);
            Controls.Add(bottom);
            AcceptButton = add;
            CancelButton = close;
        }

        private static Button Button(string text, int left, int top, int width, Action onClick)
        {
            var button = new Button { Text = text, Left = left, Top = top, Width = width, Height = 26 };
            button.Click += (s, e) => onClick();
            return button;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            DarkTheme.ApplyNativeControlTheme(_list);

            // El tematizado uniforma el color de texto; el estado se recolorea.
            foreach (ListViewItem item in _list.Items)
                item.ForeColor = ColorFor(((ScanRow)item.Tag).Status);
        }

        private static Color ColorFor(ScanStatus status)
        {
            switch (status)
            {
                case ScanStatus.AlreadyInPak: return DarkTheme.Success;
                case ScanStatus.CanAdd: return DarkTheme.ModifiedText;
                case ScanStatus.MissingOnDisk: return DarkTheme.Error;
                default: return DarkTheme.MutedText;
            }
        }

        private void SetChecked(Func<ScanRow, bool> predicate)
        {
            foreach (ListViewItem item in _list.Items) item.Checked = predicate((ScanRow)item.Tag);
        }

        private void CollectChecked()
        {
            RowsToAdd.Clear();
            foreach (ListViewItem item in _list.Items)
            {
                var row = (ScanRow)item.Tag;
                if (item.Checked && row.Addable) RowsToAdd.Add(row);
            }
        }

        private void Export()
        {
            using (var dialog = new SaveFileDialog { Filter = "Text file (*.txt)|*.txt", FileName = "scan-results.txt" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var header = new[]
                {
                    $"Missing in BSP: {_result.Summary.MissingTotal}",
                    $"Can add: {_result.Summary.CanAdd}",
                    $"Not found: {_result.Summary.NotFound}",
                    $"Already in PAK: {_result.Summary.AlreadyInPak}",
                    string.Empty,
                };

                File.WriteAllLines(dialog.FileName,
                    header.Concat(_result.Rows.Select(r => $"{r.StatusText,-18} {r.Path}")));
            }
        }
    }

    /// <summary>Lista de rutas de juego recordadas.</summary>
    internal sealed class GamePathsForm : DarkDialog
    {
        private readonly AppSettings _settings;
        private readonly ListBox _roots;

        // "Set as current" se guarda aca y recien pasa a los settings con OK:
        // modificarlos directo hacia que Cancel no deshiciera la eleccion.
        private string _current;

        public GamePathsForm(AppSettings settings) : base("Manage Game Paths", 560, 300)
        {
            _settings = settings;
            _current = settings.GameRoot;

            var rootsLabel = new Label { Text = "Saved game paths", AutoSize = true, Left = 12, Top = 12 };

            _roots = new ListBox
            {
                Left = 12, Top = 34, Width = 536, Height = 182,
                BackColor = DarkTheme.Input,
                ForeColor = DarkTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
            };
            foreach (var root in settings.SavedGameRoots) _roots.Items.Add(root);

            var add = MakeButton("Add...", 12, 224, 90, AddRoot);
            var remove = MakeButton("Remove", 110, 224, 90, RemoveRoot);
            var setCurrent = MakeButton("Set as current", 208, 224, 120, SetCurrent);

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 374, Top = 262, Width = 84, Height = 26 };
            ok.Click += (s, e) => Commit();

            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 464, Top = 262, Width = 84, Height = 26 };

            Controls.AddRange(new Control[] { rootsLabel, _roots, add, remove, setCurrent, ok, cancel });

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static Button MakeButton(string text, int left, int top, int width, Action onClick)
        {
            var button = new Button { Text = text, Left = left, Top = top, Width = width, Height = 26 };
            button.Click += (s, e) => onClick();
            return button;
        }

        private void AddRoot()
        {
            var carpeta = FolderPicker.Pick(this, "Select the game folder", _settings.GameRoot);
            if (carpeta == null) return;
            if (!_roots.Items.Contains(carpeta)) _roots.Items.Add(carpeta);
        }

        private void RemoveRoot()
        {
            if (_roots.SelectedItem == null) return;
            _roots.Items.Remove(_roots.SelectedItem);
        }

        private void SetCurrent()
        {
            if (_roots.SelectedItem is string selected) _current = selected;
        }

        private void Commit()
        {
            _settings.SavedGameRoots = _roots.Items.Cast<string>().ToList();
            _settings.GameRoot = _settings.SavedGameRoots.Contains(_current, StringComparer.OrdinalIgnoreCase)
                ? _current
                : _settings.SavedGameRoots.FirstOrDefault() ?? string.Empty;
        }
    }

    /// <summary>Opciones de escaneo y guardado.</summary>
    internal sealed class PreferencesForm : DarkDialog
    {
        private readonly AppSettings _settings;
        private readonly TextBox _gameRoot;
        private readonly DarkCheckBox _includeExtras;
        private readonly DarkCheckBox _backup;

        public PreferencesForm(AppSettings settings) : base("Preferences", 700, 196)
        {
            _settings = settings;

            var rootLabel = new Label { Text = "Game Path:", Left = 12, Top = 18, Width = 84, AutoSize = false };

            _gameRoot = new TextBox { Text = settings.GameRoot };
            var rootHost = new FieldBorder(_gameRoot) { Left = 100, Top = 14, Width = 480, Height = 22 };

            var browse = new Button { Text = "Browse...", Left = 588, Top = 13, Width = 88, Height = 24 };
            browse.Click += (s, e) => BrowseRoot();

            _includeExtras = new DarkCheckBox
            {
                Text = "Include optional extras in scan (nav, overviews, particles manifest, soundscapes, radar files)",
                Left = 100, Top = 54, Width = 580, Height = 22,
                Checked = settings.IncludeExtrasInScan,
            };

            // El respaldo se hace siempre que se sobrescriba un BSP existente,
            // tambien con Save As sobre otro mapa; el texto lo dice tal cual.
            _backup = new DarkCheckBox
            {
                Text = "Create a .bak copy before overwriting an existing BSP (replaces the previous .bak)",
                Left = 100, Top = 80, Width = 580, Height = 22,
                Checked = settings.BackupBeforeInPlaceSave,
            };

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 514, Top = 126, Width = 75, Height = 26 };
            ok.Click += (s, e) => Commit();

            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 596, Top = 126, Width = 80, Height = 26 };

            Controls.AddRange(new Control[]
            {
                rootLabel, rootHost, browse, _includeExtras, _backup, ok, cancel,
            });

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void BrowseRoot()
        {
            var carpeta = FolderPicker.Pick(this, "Select Game Path folder", _gameRoot.Text);
            if (carpeta != null) _gameRoot.Text = carpeta;
        }

        private void Commit()
        {
            var root = _gameRoot.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(root)) _settings.RememberGameRoot(root);

            _settings.IncludeExtrasInScan = _includeExtras.Checked;
            _settings.BackupBeforeInPlaceSave = _backup.Checked;
        }
    }
}
