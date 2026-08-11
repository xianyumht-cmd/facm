using System;
using System.Drawing;
using System.Windows.Forms;

namespace FACM.Theming
{
    internal static class ThemeMenu
    {
        public static void Show(MainForm owner, Control anchor)
        {
            if (anchor == null || anchor.IsDisposed)
            {
                Show(owner, Cursor.Position);
                return;
            }

            var point = anchor.PointToScreen(new Point(0, anchor.Height + 6));
            Show(owner, point);
        }

        public static void Show(MainForm owner, Point screenLocation)
        {
            if (owner == null || owner.IsDisposed) return;

            var menu = new ContextMenuStrip
            {
                Font = new Font("Microsoft YaHei UI", 9F),
                ShowImageMargin = false,
                BackColor = Color.FromArgb(24, 27, 33),
                ForeColor = Color.FromArgb(238, 240, 245),
                Renderer = new ToolStripProfessionalRenderer(new ThemeMenuColorTable())
            };

            var panelTheme = new ToolStripMenuItem("面板外观…");
            panelTheme.Click += delegate { owner.OpenPanelThemeSelector(); };

            var desktop = new ToolStripMenuItem("桌面形态");
            var shell = new ToolStripMenuItem("FACM 悬浮入口");
            shell.Click += delegate { owner.RestoreDefaultBall(); };
            var pet = new ToolStripMenuItem("选择桌面宠物…");
            pet.Click += delegate { owner.OpenPetSelector(); };
            var reset = new ToolStripMenuItem("复位桌面位置");
            reset.Click += delegate { owner.ResetAnimalPet(); };
            desktop.DropDownItems.Add(shell);
            desktop.DropDownItems.Add(pet);
            desktop.DropDownItems.Add(new ToolStripSeparator());
            desktop.DropDownItems.Add(reset);

            menu.Items.Add(panelTheme);
            menu.Items.Add(desktop);
            menu.Closed += delegate
            {
                // ToolStripDropDown raises Closed before its internal SetVisibleCore/OnItemClicked/
                // ModalMenuFilter stack has fully unwound. Disposing synchronously from Closed leaves
                // WinForms finishing the current mouse/menu message against an already disposed object.
                // Post disposal to the owner message queue so the current dropdown transaction can end.
                try
                {
                    if (!owner.IsDisposed && owner.IsHandleCreated)
                    {
                        owner.BeginInvoke(new Action(delegate
                        {
                            if (!menu.IsDisposed) menu.Dispose();
                        }));
                    }
                }
                catch (InvalidOperationException)
                {
                    // Owner is shutting down; the application teardown will release remaining handles.
                }
                catch (ObjectDisposedException)
                {
                    // Same shutdown case, already at the desired final state.
                }
            };
            menu.Show(screenLocation);
        }

        private sealed class ThemeMenuColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(24, 27, 33); } }
            public override Color ImageMarginGradientBegin { get { return Color.FromArgb(24, 27, 33); } }
            public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(24, 27, 33); } }
            public override Color ImageMarginGradientEnd { get { return Color.FromArgb(24, 27, 33); } }
            public override Color MenuItemSelected { get { return Color.FromArgb(42, 46, 56); } }
            public override Color MenuItemBorder { get { return Color.FromArgb(62, 68, 82); } }
            public override Color MenuBorder { get { return Color.FromArgb(58, 63, 75); } }
            public override Color SeparatorDark { get { return Color.FromArgb(54, 59, 70); } }
            public override Color SeparatorLight { get { return Color.FromArgb(54, 59, 70); } }
        }
    }
}
