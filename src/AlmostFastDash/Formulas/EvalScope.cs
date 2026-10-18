using System;
using System.Collections.Generic;

namespace AlmostFastDash.Formulas
{
    /// <summary>Everything a formula can see while a frame is drawn.</summary>
    internal sealed class EvalScope
    {
        public TelemetrySnapshot T;
        public UnitSystem Units = UnitSystem.Default;
        public LayoutTheme Theme = new LayoutTheme();
        public string Accent = "#2DD4BF";
        public bool ShiftFlashOn;
        public DateTime Now = DateTime.Now;
        /// <summary>The page shown (from 1) and how many pages the design has.</summary>
        public int Page = 1, PageCount = 1;
        /// <summary>The number of the group a widget belongs to (such as a driver card's position), 0 outside a group.</summary>
        public double Slot;
        /// <summary>Which view each tap zone is on, by tap name (0 for the first).</summary>
        public Dictionary<string, int> Taps;
        /// <summary>The grouped widget a formula belongs to, whose module options option() reads.</summary>
        public DashWidget Self;

        /// <summary>The same scope for a widget in a numbered group.</summary>
        public EvalScope WithSlot(double slot) => slot == Slot ? this : new EvalScope
        {
            T = T, Units = Units, Theme = Theme, Accent = Accent, ShiftFlashOn = ShiftFlashOn, Now = Now, Page = Page,
                PageCount = PageCount, Slot = slot, Taps = Taps, Self = Self
        };

        /// <summary>The same scope for a widget in a group: its group's Slot, and its own module options.</summary>
        public EvalScope ForGroup(DashWidget widget) => widget == Self ? this : new EvalScope
        {
            T = T, Units = Units, Theme = Theme, Accent = Accent, ShiftFlashOn = ShiftFlashOn, Now = Now, Page = Page,
                PageCount = PageCount, Slot = widget.Slot, Taps = Taps, Self = widget
        };
    }
}
