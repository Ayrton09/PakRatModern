using System;
using System.Drawing;
using System.Windows.Forms;

namespace PakRatModern.App
{
    /// <summary>
    /// ListView con el encabezado nativo oculto y las filas dibujadas por la
    /// aplicacion. El encabezado real es un control del sistema que ignora
    /// BackColor y siempre queda claro; se reemplaza por <see cref="ListHeaderPanel"/>.
    /// </summary>
    internal sealed class DarkListView : ListView
    {
        private const int CheckBoxSize = 13;
        private const int CheckBoxMargin = 4;

        /// <remarks>
        /// El encabezado nativo se oculta siempre: aun dibujandolo por
        /// OwnerDraw queda sin pintar la esquina que hay sobre la barra de
        /// desplazamiento. El encabezado visible lo provee
        /// <see cref="ListHeaderPanel"/>, que ademas permite ordenar al hacer clic.
        /// </remarks>
        public DarkListView()
        {
            Dock = DockStyle.Fill;
            View = View.Details;
            HeaderStyle = ColumnHeaderStyle.None;
            FullRowSelect = true;
            MultiSelect = true;
            GridLines = false;
            HideSelection = false;
            AllowDrop = true;
            BorderStyle = BorderStyle.None;
            BackColor = DarkTheme.Input;
            ForeColor = DarkTheme.Text;
            OwnerDraw = true;
            DoubleBuffered = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTheme.ApplyNativeControlTheme(this);
        }

        /// <summary>
        /// El tema DarkMode_Explorer se necesita para que la barra de
        /// desplazamiento sea oscura, pero de paso dibuja separadores verticales
        /// de columna a lo alto de todo el control. Pintando el fondo nosotros se
        /// eliminan sin renunciar al resto del tema.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            const int WmEraseBackground = 0x0014;

            if (m.Msg == WmEraseBackground)
            {
                using (var graphics = Graphics.FromHdc(m.WParam))
                using (var brush = new SolidBrush(DarkTheme.Input))
                    graphics.FillRectangle(brush, ClientRectangle);

                m.Result = (IntPtr)1;
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            // En vista Details el dibujo efectivo ocurre por sub-item.
            e.DrawDefault = false;
        }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            var selected = e.Item.Selected;
            var backColor = selected
                ? DarkTheme.Selection
                : (e.ItemIndex % 2 == 0 ? DarkTheme.Input : DarkTheme.RowAlt);

            using (var background = new SolidBrush(backColor))
                e.Graphics.FillRectangle(background, e.Bounds);

            var foreColor = selected ? DarkTheme.AccentText : e.Item.ForeColor;
            var bounds = Rectangle.Inflate(e.Bounds, -6, 0);

            // Con OwnerDraw el control deja de dibujar la casilla, hay que hacerlo
            // aca o la columna se ve vacia aunque el estado cambie al hacer clic.
            if (CheckBoxes && e.ColumnIndex == 0)
            {
                var box = new Rectangle(
                    e.Bounds.Left + CheckBoxMargin,
                    e.Bounds.Top + (e.Bounds.Height - CheckBoxSize) / 2,
                    CheckBoxSize,
                    CheckBoxSize);

                DrawCheckBox(e.Graphics, box, e.Item.Checked);
                bounds = new Rectangle(
                    box.Right + CheckBoxMargin,
                    e.Bounds.Top,
                    Math.Max(0, e.Bounds.Right - box.Right - CheckBoxMargin * 2),
                    e.Bounds.Height);
            }

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (Columns[e.ColumnIndex].TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;

            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, bounds, foreColor, flags);
        }

        private static void DrawCheckBox(Graphics graphics, Rectangle box, bool isChecked)
        {
            using (var fill = new SolidBrush(isChecked ? DarkTheme.Accent : DarkTheme.Input))
                graphics.FillRectangle(fill, box);

            using (var border = new Pen(isChecked ? DarkTheme.Accent : DarkTheme.Border))
                graphics.DrawRectangle(border, box);

            if (!isChecked) return;

            using (var check = new Pen(DarkTheme.AccentText, 2f))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.DrawLines(check, new[]
                {
                    new Point(box.Left + 3, box.Top + 6),
                    new Point(box.Left + 5, box.Top + 9),
                    new Point(box.Left + 10, box.Top + 3),
                });
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
            }
        }
    }

    /// <summary>
    /// Marco de 1px alrededor de un campo de texto.
    ///
    /// Sin borde, un campo vacio se confunde con el panel que lo rodea y no se ve
    /// donde hay que escribir. BorderStyle.FixedSingle no sirve porque usa un
    /// color del sistema que en tema oscuro queda claro.
    /// </summary>
    internal sealed class FieldBorder : Panel
    {
        public FieldBorder(Control field)
        {
            BackColor = DarkTheme.Border;
            Padding = new Padding(1);

            field.Dock = DockStyle.Fill;
            field.BackColor = DarkTheme.Input;
            field.ForeColor = DarkTheme.Text;
            if (field is TextBox textBox) textBox.BorderStyle = BorderStyle.None;

            Controls.Add(field);
        }
    }

    /// <summary>
    /// Encabezado de columnas hecho con botones planos, para poder darle el color
    /// del tema y manejar el ordenamiento al hacer clic.
    /// </summary>
    internal sealed class ListHeaderPanel : Panel
    {
        private readonly Button[] _buttons;
        private readonly string[] _titles;
        private int _sortColumn = -1;
        private bool _sortDescending;

        public event Action<int> ColumnClicked;

        public ListHeaderPanel(ListView listView)
        {
            Dock = DockStyle.Top;
            Height = 24;
            BackColor = DarkTheme.Header;

            _titles = new string[listView.Columns.Count];
            _buttons = new Button[listView.Columns.Count];

            var left = 0;
            for (var i = 0; i < listView.Columns.Count; i++)
            {
                _titles[i] = listView.Columns[i].Text;

                var button = new Button
                {
                    Left = left,
                    Top = 0,
                    Height = Height,
                    Width = listView.Columns[i].Width,
                    Text = _titles[i],
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(6, 0, 6, 0),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = DarkTheme.Header,
                    ForeColor = DarkTheme.Text,
                    UseVisualStyleBackColor = false,
                    TabStop = false,
                };
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = DarkTheme.Header;
                button.FlatAppearance.MouseDownBackColor = DarkTheme.Selection;

                var index = i;
                button.Click += (s, e) => ColumnClicked?.Invoke(index);

                Controls.Add(button);
                _buttons[i] = button;
                left += button.Width;
            }

            var separator = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = DarkTheme.Border };
            Controls.Add(separator);
        }

        public void SetSort(int column, bool descending)
        {
            _sortColumn = column;
            _sortDescending = descending;

            for (var i = 0; i < _buttons.Length; i++)
            {
                var marker = i != _sortColumn ? string.Empty : (_sortDescending ? "  v" : "  ^");
                _buttons[i].Text = _titles[i] + marker;
            }
        }
    }
}
