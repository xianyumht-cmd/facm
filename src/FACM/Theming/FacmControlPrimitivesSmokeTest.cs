using System;
using System.Windows.Forms;

namespace FACM.Theming
{
    internal static class FacmControlPrimitivesSmokeTest
    {
        public static void Validate()
        {
            using (var button = new FacmActionButton())
            using (var toggle = new FacmToggleSwitch())
            using (var badge = new FacmStatusBadge())
            {
                if (!(button is Button) || !button.TabStop)
                    throw new InvalidOperationException("FACM shared action button must retain native Button keyboard/focus semantics.");
                button.Tone = FacmButtonTone.Primary;
                if (button.Tone != FacmButtonTone.Primary)
                    throw new InvalidOperationException("FACM shared action button lost its semantic tone state.");

                if (!(toggle is CheckBox))
                    throw new InvalidOperationException("FACM shared toggle must retain native CheckBox semantics.");
                var changed = 0;
                toggle.CheckedChanged += delegate { changed++; };
                toggle.Checked = true;
                if (!toggle.Checked || changed != 1)
                    throw new InvalidOperationException("FACM shared toggle no longer raises CheckedChanged exactly once for a normal state change.");

                badge.Tone = FacmStatusTone.Success;
                if (badge.Tone != FacmStatusTone.Success)
                    throw new InvalidOperationException("FACM shared status badge lost its semantic tone state.");
            }

            if (FacmDesignSystem.ControlRadius < 0 || FacmDesignSystem.CardRadius < 0 || FacmDesignSystem.WindowRadius < 0)
                throw new InvalidOperationException("FACM shared design radii must remain non-negative.");
        }
    }
}
