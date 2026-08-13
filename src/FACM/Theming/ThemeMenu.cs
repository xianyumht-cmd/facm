using System;
using System.Drawing;
using System.Windows.Forms;
using FACM.Services;

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

            var panelTheme = CreateItem(UiTextRuntime.Text(UiTextKeys.ThemePanelAppearance), menu.ForeColor);
            panelTheme.Click += delegate
            {
                AppLog.Info("Theme menu action: panel appearance");
                PostOwnerAction(owner, owner.OpenPanelThemeSelector);
            };

            var desktop = CreateItem(UiTextRuntime.Text(UiTextKeys.ThemeDesktopMode), menu.ForeColor);
            var shell = CreateItem(UiTextRuntime.Text(UiTextKeys.ThemeFacmShell), menu.ForeColor);
            shell.Click += delegate
            {
                AppLog.Info("Theme menu action: FACM shell");
                PostOwnerAction(owner, owner.RestoreDefaultBall);
            };
            var pet = CreateItem(UiTextRuntime.Text(UiTextKeys.ThemeSelectDesktopPet), menu.ForeColor);
            pet.Click += delegate
            {
                AppLog.Info("Theme menu action: desktop pet picker");
                PostOwnerAction(owner, owner.OpenPetSelector);
            };
            var reset = CreateItem(UiTextRuntime.Text(UiTextKeys.ThemeResetDesktopPosition), menu.ForeColor);
            reset.Click += delegate
            {
                AppLog.Info("Theme menu action: reset desktop position");
                PostOwnerAction(owner, owner.ResetAnimalPet);
            };
            desktop.DropDownItems.Add(shell);
            desktop.DropDownItems.Add(pet);
            desktop.DropDownItems.Add(new ToolStripSeparator());
            desktop.DropDownItems.Add(reset);

            // Child ToolStripDropDownMenu does not reliably inherit the visual properties assigned to
            // the root ContextMenuStrip. Apply the same surface/renderer explicitly so the submenu does
            // not fall back to dark default text on FACM's dark menu background.
            desktop.DropDown.BackColor = menu.BackColor;
            desktop.DropDown.ForeColor = menu.ForeColor;
            desktop.DropDown.Renderer = menu.Renderer;
            var desktopMenu = desktop.DropDown as ToolStripDropDownMenu;
            if (desktopMenu != null)
                desktopMenu.ShowImageMargin = false;

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
            };

            // Apply legacy [Replace] rules too. The stable [Text] values above are the primary path;
            // this explicit pass keeps older user configurations working for this ephemeral popup.
            UiTextRuntime.Apply(menu);
            menu.Show(screenLocation);
        }

        private static ToolStripMenuItem CreateItem(string text, Color foreColor)
        {
            return new ToolStripMenuItem(text) { ForeColor = foreColor };
        }

        private static void PostOwnerAction(MainForm owner, Action action)
        {
            if (owner == null || action == null || owner.IsDisposed) return;
            try
            {
                owner.BeginInvoke(new Action(delegate
                {
                    if (!owner.IsDisposed) action();
                }));
            }
            catch (InvalidOperationException)
            {
                // The owner is closing. There is no UI action left to perform.
            }
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
