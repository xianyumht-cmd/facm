using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FACM.Theming;

namespace FACM.League
{
    /// <summary>
    /// Visual convergence layer for the legacy League Live form. It deliberately leaves polling,
    /// bench-swap writes and ListView data ownership inside LeagueLiveForm; this class only maps the
    /// existing controls onto the shared FACM desktop palette and accessibility contract.
    /// </summary>
    internal static class LeagueLiveDesignEnhancer
    {
        private static readonly string[] RequiredFieldNames =
        {
            "_phaseLabel",
            "_summaryLabel",
            "_detailLabel",
            "_bansLabel",
            "_statusLabel",
            "_benchCard",
            "_benchStateLabel",
            "_benchFlow",
            "_playersList",
            "_refreshButton"
        };

        public static T Apply<T>(T form) where T : Form
        {
            if (form == null || form.IsDisposed) return form;

            form.BackColor = FacmDesignSystem.Canvas;
            form.ForeColor = FacmDesignSystem.Text;
            form.Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            foreach (var label in form.Controls.OfType<Label>())
            {
                if (label.Font != null && label.Font.Size >= 14F)
                {
                    label.ForeColor = FacmDesignSystem.Text;
                    label.Font = new Font(FacmThemeRuntime.Current.FontName, label.Font.Size, FontStyle.Bold);
                }
                else if (label.Top < 90)
                {
                    label.ForeColor = FacmDesignSystem.TextMuted;
                    label.Font = new Font(FacmThemeRuntime.Current.FontName, label.Font == null ? 9F : label.Font.Size, label.Font == null ? FontStyle.Regular : label.Font.Style);
                }
            }

            StyleInfoLabel(form, "_phaseLabel", FacmDesignSystem.Text, true);
            StyleInfoLabel(form, "_summaryLabel", FacmDesignSystem.Text, false);
            StyleInfoLabel(form, "_detailLabel", FacmDesignSystem.TextMuted, false);
            StyleInfoLabel(form, "_bansLabel", FacmDesignSystem.TextMuted, false);
            StyleInfoLabel(form, "_statusLabel", FacmDesignSystem.TextMuted, false);
            StyleInfoLabel(form, "_benchStateLabel", FacmDesignSystem.TextMuted, false);

            var benchCard = GetField<Panel>(form, "_benchCard");
            if (benchCard != null)
            {
                benchCard.BackColor = FacmDesignSystem.Surface;
                benchCard.Padding = Padding.Empty;
                foreach (var title in benchCard.Controls.OfType<Label>().Where(label => !ReferenceEquals(label, GetField<Label>(form, "_benchStateLabel"))))
                {
                    title.ForeColor = FacmDesignSystem.Accent;
                    title.Font = new Font(FacmThemeRuntime.Current.FontName, title.Font == null ? 9F : title.Font.Size, FontStyle.Bold);
                }
            }

            var benchFlow = GetField<FlowLayoutPanel>(form, "_benchFlow");
            if (benchFlow != null)
            {
                benchFlow.BackColor = FacmDesignSystem.CanvasRaised;
                benchFlow.ControlAdded -= HandleBenchButtonAdded;
                benchFlow.ControlAdded += HandleBenchButtonAdded;
                foreach (Control control in benchFlow.Controls) StyleBenchButton(control as Button);
            }

            var players = GetField<ListView>(form, "_playersList");
            if (players != null)
            {
                players.BackColor = FacmDesignSystem.CanvasRaised;
                players.ForeColor = FacmDesignSystem.Text;
                players.BorderStyle = BorderStyle.None;
                players.GridLines = false;
                players.HideSelection = false;
            }

            var refresh = GetField<Button>(form, "_refreshButton");
            if (refresh != null) StyleActionButton(refresh, true);
            foreach (var button in form.Controls.OfType<Button>())
            {
                if (!ReferenceEquals(button, refresh)) StyleActionButton(button, false);
            }

            return form;
        }

        internal static void ValidateForSmokeTest()
        {
            var type = typeof(LeagueLiveForm);
            foreach (var name in RequiredFieldNames)
            {
                if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) == null)
                    throw new InvalidOperationException("League Live design enhancer lost its bounded control contract: " + name);
            }
        }

        private static void StyleInfoLabel(Form form, string fieldName, Color color, bool bold)
        {
            var label = GetField<Label>(form, fieldName);
            if (label == null) return;
            label.ForeColor = color;
            label.Font = new Font(
                FacmThemeRuntime.Current.FontName,
                bold ? 10F : 9F,
                bold ? FontStyle.Bold : FontStyle.Regular);
        }

        private static void StyleActionButton(Button button, bool primary)
        {
            if (button == null || button.IsDisposed) return;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = FacmDesignSystem.BorderSoft;
            button.FlatAppearance.MouseOverBackColor = primary
                ? FacmDesignSystem.Blend(FacmDesignSystem.Accent, FacmDesignSystem.Text, 0.08F)
                : FacmDesignSystem.SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = primary
                ? FacmDesignSystem.Blend(FacmDesignSystem.Accent, FacmDesignSystem.Canvas, 0.12F)
                : FacmDesignSystem.Blend(FacmDesignSystem.SurfaceHover, FacmDesignSystem.Accent, 0.08F);
            button.BackColor = primary ? FacmDesignSystem.Accent : FacmDesignSystem.SurfaceRaised;
            button.ForeColor = primary ? Color.White : FacmDesignSystem.Text;
            button.Font = new Font(FacmThemeRuntime.Current.FontName, 8.8F, FontStyle.Bold);
            button.TabStop = true;
            FacmDesignSystem.Round(button, FacmDesignSystem.ControlRadius);
        }

        private static void HandleBenchButtonAdded(object sender, ControlEventArgs e)
        {
            StyleBenchButton(e.Control as Button);
        }

        private static void StyleBenchButton(Button button)
        {
            if (button == null || button.IsDisposed) return;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = FacmDesignSystem.BorderSoft;
            button.FlatAppearance.MouseOverBackColor = FacmDesignSystem.SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = FacmDesignSystem.Blend(FacmDesignSystem.SurfaceHover, FacmDesignSystem.Accent, 0.08F);
            button.BackColor = FacmDesignSystem.SurfaceRaised;
            button.ForeColor = FacmDesignSystem.Text;
            button.Font = new Font(FacmThemeRuntime.Current.FontName, 7F, FontStyle.Regular);
            button.TabStop = true;
            FacmDesignSystem.Round(button, Math.Min(4, FacmDesignSystem.ControlRadius));
        }

        private static T GetField<T>(object owner, string name) where T : class
        {
            if (owner == null || string.IsNullOrWhiteSpace(name)) return null;
            var field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(owner) as T;
        }
    }
}
