using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FACM.Theming;

namespace FACM.League
{
    /// <summary>
    /// Bounded visual convergence layer for the unified recommendation page. The form remains the
    /// owner of recommendation reads, confirmation, loadout/item writes and auto-apply state; this
    /// class only replaces its legacy cyan/violet presentation with shared FACM desktop semantics.
    /// </summary>
    internal static class LeagueRecommendationDesignEnhancer
    {
        private const string HeaderAccentOverlayName = "FACM.Recommendation.HeaderAccent";

        private static readonly string[] RequiredFieldNames =
        {
            "_runesChoice",
            "_spellsChoice",
            "_itemsChoice",
            "_autoToggle",
            "_autoStatus",
            "_contextValue",
            "_runePreview",
            "_spellPreview",
            "_itemPreview",
            "_skillsValue",
            "_countersValue",
            "_statusValue",
            "_refreshButton",
            "_applyButton"
        };

        public static T Apply<T>(T form) where T : Form
        {
            if (form == null || form.IsDisposed) return form;

            ValidateForSmokeTest();
            ApplyCurrentTheme(form);

            var choices = new[]
            {
                GetField<CheckBox>(form, "_runesChoice"),
                GetField<CheckBox>(form, "_spellsChoice"),
                GetField<CheckBox>(form, "_itemsChoice")
            };
            foreach (var choice in choices)
            {
                if (choice == null) continue;
                choice.CheckedChanged += HandleChoiceChanged;
            }

            EventHandler themeChanged = null;
            themeChanged = delegate
            {
                if (!form.IsDisposed) ApplyCurrentTheme(form);
            };
            FacmThemeRuntime.ThemeChanged += themeChanged;
            form.FormClosed += delegate
            {
                FacmThemeRuntime.ThemeChanged -= themeChanged;
                foreach (var choice in choices)
                {
                    if (choice != null && !choice.IsDisposed)
                        choice.CheckedChanged -= HandleChoiceChanged;
                }
            };
            return form;
        }

        internal static void ValidateForSmokeTest()
        {
            var type = typeof(LeagueRecommendationForm);
            foreach (var name in RequiredFieldNames)
            {
                if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) == null)
                    throw new InvalidOperationException("League recommendation design enhancer lost its bounded control contract: " + name);
            }
        }

        private static void ApplyCurrentTheme(Form form)
        {
            form.BackColor = FacmDesignSystem.Canvas;
            form.ForeColor = FacmDesignSystem.Text;
            form.Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            StyleHeader(form);
            StyleTopLevelLabels(form);

            StyleChoice(GetField<CheckBox>(form, "_runesChoice"));
            StyleChoice(GetField<CheckBox>(form, "_spellsChoice"));
            StyleChoice(GetField<CheckBox>(form, "_itemsChoice"));

            var autoToggle = GetField<CheckBox>(form, "_autoToggle");
            if (autoToggle != null)
            {
                autoToggle.BackColor = Color.Transparent;
                autoToggle.ForeColor = FacmDesignSystem.Text;
                autoToggle.Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
                autoToggle.TabStop = true;
            }

            StyleLabel(form, "_autoStatus", FacmDesignSystem.Accent, false);
            StyleLabel(form, "_contextValue", FacmDesignSystem.Text, false);
            StyleLabel(form, "_skillsValue", FacmDesignSystem.Text, false);
            StyleLabel(form, "_countersValue", FacmDesignSystem.Text, false);
            StyleLabel(form, "_statusValue", FacmDesignSystem.TextMuted, false);

            StylePreview(GetField<TextBox>(form, "_runePreview"));
            StylePreview(GetField<TextBox>(form, "_spellPreview"));
            StylePreview(GetField<TextBox>(form, "_itemPreview"));

            StyleAction(GetField<Button>(form, "_refreshButton"), false);
            StyleAction(GetField<Button>(form, "_applyButton"), true);
            form.Invalidate(true);
        }

        private static void StyleHeader(Form form)
        {
            var header = form.Controls.Cast<Control>()
                .OfType<Panel>()
                .FirstOrDefault(panel => string.Equals(panel.GetType().Name, "RecommendationHeaderPanel", StringComparison.Ordinal));
            if (header == null) return;

            header.BackColor = FacmDesignSystem.CanvasRaised;
            foreach (var label in header.Controls.OfType<Label>())
            {
                var title = label.Font != null && label.Font.Size >= 14F;
                label.ForeColor = title ? FacmDesignSystem.Text : FacmDesignSystem.TextMuted;
                label.Font = new Font(
                    FacmThemeRuntime.Current.FontName,
                    label.Font == null ? (title ? 18F : 9F) : label.Font.Size,
                    title ? FontStyle.Bold : FontStyle.Regular);
            }

            var overlay = header.Controls.Find(HeaderAccentOverlayName, false).FirstOrDefault() as Panel;
            if (overlay == null)
            {
                overlay = new Panel
                {
                    Name = HeaderAccentOverlayName,
                    Dock = DockStyle.Bottom,
                    Height = 2,
                    TabStop = false
                };
                header.Controls.Add(overlay);
            }
            overlay.BackColor = FacmDesignSystem.Accent;
            overlay.BringToFront();
        }

        private static void StyleTopLevelLabels(Form form)
        {
            var protectedLabels = new HashSet<Label>
            {
                GetField<Label>(form, "_autoStatus"),
                GetField<Label>(form, "_contextValue"),
                GetField<Label>(form, "_skillsValue"),
                GetField<Label>(form, "_countersValue"),
                GetField<Label>(form, "_statusValue")
            };

            foreach (var label in form.Controls.OfType<Label>())
            {
                if (protectedLabels.Contains(label)) continue;
                if (label.Top >= 338 && label.Top <= 366)
                {
                    label.ForeColor = FacmDesignSystem.Accent;
                    label.Font = new Font(FacmThemeRuntime.Current.FontName, 8.5F, FontStyle.Bold);
                }
                else if (label.Font != null && label.Font.Bold && label.Font.Size >= 9.5F)
                {
                    label.ForeColor = FacmDesignSystem.Text;
                    label.Font = new Font(FacmThemeRuntime.Current.FontName, label.Font.Size, FontStyle.Bold);
                }
                else
                {
                    label.ForeColor = FacmDesignSystem.TextMuted;
                    label.Font = new Font(FacmThemeRuntime.Current.FontName, label.Font == null ? 9F : label.Font.Size);
                }
            }
        }

        private static void StyleChoice(CheckBox choice)
        {
            if (choice == null || choice.IsDisposed) return;
            choice.FlatStyle = FlatStyle.Flat;
            choice.FlatAppearance.BorderSize = 1;
            choice.FlatAppearance.BorderColor = choice.Checked ? FacmDesignSystem.Accent : FacmDesignSystem.BorderSoft;
            choice.FlatAppearance.MouseOverBackColor = FacmDesignSystem.SurfaceHover;
            choice.FlatAppearance.MouseDownBackColor = FacmDesignSystem.Blend(FacmDesignSystem.SurfaceHover, FacmDesignSystem.Accent, 0.08F);
            choice.BackColor = choice.Checked ? FacmDesignSystem.SurfaceRaised : FacmDesignSystem.Surface;
            choice.ForeColor = choice.Checked ? FacmDesignSystem.Text : FacmDesignSystem.TextMuted;
            choice.Font = new Font(FacmThemeRuntime.Current.FontName, 8.8F, FontStyle.Bold);
            choice.TabStop = true;
        }

        private static void HandleChoiceChanged(object sender, EventArgs e)
        {
            StyleChoice(sender as CheckBox);
        }

        private static void StylePreview(TextBox box)
        {
            if (box == null || box.IsDisposed) return;
            box.BackColor = FacmDesignSystem.CanvasRaised;
            box.ForeColor = FacmDesignSystem.Text;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Font = new Font(FacmThemeRuntime.Current.FontName, 8.8F);
        }

        private static void StyleAction(Button button, bool primary)
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

        private static void StyleLabel(Form form, string fieldName, Color color, bool bold)
        {
            var label = GetField<Label>(form, fieldName);
            if (label == null) return;
            label.ForeColor = color;
            label.Font = new Font(
                FacmThemeRuntime.Current.FontName,
                label.Font == null ? 9F : label.Font.Size,
                bold ? FontStyle.Bold : FontStyle.Regular);
        }

        private static T GetField<T>(object owner, string name) where T : class
        {
            if (owner == null || string.IsNullOrWhiteSpace(name)) return null;
            var field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(owner) as T;
        }
    }
}
