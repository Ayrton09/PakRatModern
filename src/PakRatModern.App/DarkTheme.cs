using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PakRatModern.App
{
    /// <summary>
    /// Paleta y tematizado, replicando exactamente los colores y el criterio por
    /// tipo de control de la version en PowerShell para que la interfaz se vea
    /// igual tras el port.
    /// </summary>
    internal static class DarkTheme
    {
        public static readonly Color Back = Color.FromArgb(32, 34, 37);
        public static readonly Color Panel = Color.FromArgb(40, 43, 48);
        public static readonly Color Input = Color.FromArgb(28, 30, 33);
        public static readonly Color Border = Color.FromArgb(52, 56, 62);
        public static readonly Color Text = Color.FromArgb(230, 233, 239);
        public static readonly Color MutedText = Color.FromArgb(180, 186, 198);
        public static readonly Color Accent = Color.FromArgb(72, 133, 237);
        public static readonly Color ModifiedText = Color.FromArgb(138, 198, 255);
        public static readonly Color AccentText = Color.FromArgb(244, 247, 255);
        public static readonly Color Success = Color.FromArgb(76, 175, 80);
        public static readonly Color Error = Color.FromArgb(244, 67, 54);
        public static readonly Color RowAlt = Color.FromArgb(36, 38, 43);
        public static readonly Color Selection = Color.FromArgb(64, 96, 150);
        public static readonly Color Header = Color.FromArgb(45, 48, 54);

        public static readonly Font UiFont = new Font("Segoe UI", 9f);

        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
        private const int PreferredAppModeForceDark = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        // SetPreferredAppMode solo se exporta por ordinal; no tiene nombre publico
        // ni esta documentada. En builds anteriores a 1809 el ordinal 135 es
        // AllowDarkModeForApp(bool), que interpreta el 2 como "true" sin dano;
        // si Microsoft la quita, EnableAppDarkMode falla en silencio y los menus
        // quedan claros, nada mas. Es cosmetica: no se depende de ella.
        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode(int mode);

        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        private static extern void FlushMenuThemes();

        public static void EnableAppDarkMode()
        {
            try
            {
                SetPreferredAppMode(PreferredAppModeForceDark);
                FlushMenuThemes();
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
        }

        public static void ApplyTitleBar(Form form)
        {
            if (!form.IsHandleCreated) return;

            var enabled = 1;
            if (DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }

        /// <summary>
        /// Escala la ventana al DPI del sistema. Toda la interfaz se arma con
        /// coordenadas pensadas para 96 DPI (100 %); sin esto, con 125 % o 150 %
        /// Windows estiraba la ventana como una imagen y el texto salia borroso.
        /// Las fuentes estan en puntos y ya crecen solas; aca se escalan
        /// posiciones, tamanos y anchos de columna. A 100 % no hace nada.
        /// </summary>
        public static void ScaleForDpi(Form form)
        {
            var factor = form.DeviceDpi / 96f;
            if (Math.Abs(factor - 1f) < 0.01f) return;

            form.Scale(new SizeF(factor, factor));
            ScaleListColumns(form, factor);
        }

        private static void ScaleListColumns(Control control, float factor)
        {
            if (control is ListView listView)
            {
                foreach (ColumnHeader column in listView.Columns)
                    column.Width = (int)Math.Round(column.Width * factor);
            }

            foreach (Control child in control.Controls) ScaleListColumns(child, factor);
        }

        /// <summary>Barras de desplazamiento y bordes oscuros en listas y arboles.</summary>
        public static void ApplyNativeControlTheme(Control control)
        {
            if (control.IsHandleCreated)
                SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        }

        public static void Apply(Control control)
        {
            switch (control)
            {
                case Form form:
                    form.BackColor = Back;
                    form.ForeColor = Text;
                    break;

                case TextBox textBox:
                    textBox.BackColor = Input;
                    textBox.ForeColor = Text;
                    textBox.BorderStyle = BorderStyle.None;
                    ApplyNativeControlTheme(textBox);
                    break;

                case ComboBox comboBox:
                    comboBox.BackColor = Input;
                    comboBox.ForeColor = Text;
                    comboBox.FlatStyle = FlatStyle.Flat;
                    break;

                case ListBox listBox:
                    listBox.BackColor = Input;
                    listBox.ForeColor = Text;
                    listBox.BorderStyle = BorderStyle.FixedSingle;
                    ApplyNativeControlTheme(listBox);
                    break;

                case ListView listView:
                    listView.BackColor = Input;
                    listView.ForeColor = Text;
                    listView.BorderStyle = BorderStyle.None;
                    ApplyNativeControlTheme(listView);
                    break;

                case TreeView treeView:
                    treeView.BackColor = Input;
                    treeView.ForeColor = Text;
                    treeView.BorderStyle = BorderStyle.None;
                    ApplyNativeControlTheme(treeView);
                    break;

                case Button button:
                    button.UseVisualStyleBackColor = false;
                    button.BackColor = Panel;
                    button.ForeColor = Text;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = Border;
                    button.FlatAppearance.MouseDownBackColor = Selection;
                    button.FlatAppearance.MouseOverBackColor = Header;
                    break;

                case DarkCheckBox darkCheckBox:
                    // Se dibuja sola con los colores del tema.
                    darkCheckBox.ForeColor = Text;
                    break;

                case CheckBox checkBox:
                    // FlatStyle.Flat pinta la tilde con el color del texto sobre un
                    // recuadro blanco: con el texto claro del tema no se ve. El
                    // estilo del sistema al menos muestra el estado real.
                    checkBox.UseVisualStyleBackColor = false;
                    checkBox.BackColor = Color.Transparent;
                    checkBox.ForeColor = Text;
                    checkBox.FlatStyle = FlatStyle.Standard;
                    break;

                case Label label:
                    label.BackColor = Color.Transparent;
                    label.ForeColor = Text;
                    break;

                case MenuStrip menuStrip:
                    menuStrip.BackColor = Panel;
                    menuStrip.ForeColor = Text;
                    menuStrip.Renderer = new DarkMenuRenderer();
                    break;

                case StatusStrip statusStrip:
                    statusStrip.BackColor = Panel;
                    statusStrip.ForeColor = MutedText;
                    statusStrip.Renderer = new DarkMenuRenderer();
                    break;

                case ContextMenuStrip contextMenu:
                    contextMenu.BackColor = Panel;
                    contextMenu.ForeColor = Text;
                    contextMenu.Renderer = new DarkMenuRenderer();
                    break;

                case FieldBorder fieldBorder:
                    // Su color ES el borde del campo; no se pisa.
                    fieldBorder.ForeColor = Text;
                    break;

                case Panel panel:
                    panel.ForeColor = Text;
                    // Los separadores de 1px llevan su propio color; no se pisan.
                    if (panel.Height > 1) panel.BackColor = Panel;
                    break;
            }

            foreach (Control child in control.Controls) Apply(child);

            if (control.ContextMenuStrip != null) Apply(control.ContextMenuStrip);
        }

        /// <summary>Menus y barra de estado en oscuro, incluidos los desplegables.</summary>
        private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkColorTable()) { }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Text : MutedText;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                e.Graphics.Clear(Panel);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var pen = new Pen(Border))
                {
                    var b = e.AffectedBounds;
                    e.Graphics.DrawRectangle(pen, b.X, b.Y, b.Width - 1, b.Height - 1);
                }
            }
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Selection;
            public override Color MenuItemSelectedGradientBegin => Selection;
            public override Color MenuItemSelectedGradientEnd => Selection;
            public override Color MenuItemBorder => Selection;
            public override Color MenuBorder => Border;
            public override Color MenuItemPressedGradientBegin => Header;
            public override Color MenuItemPressedGradientEnd => Header;
            public override Color ToolStripDropDownBackground => Panel;
            public override Color ImageMarginGradientBegin => Panel;
            public override Color ImageMarginGradientMiddle => Panel;
            public override Color ImageMarginGradientEnd => Panel;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Border;
            public override Color StatusStripGradientBegin => Panel;
            public override Color StatusStripGradientEnd => Panel;
        }
    }
}
