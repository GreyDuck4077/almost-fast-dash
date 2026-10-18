namespace AlmostFastDash.Output
{
    internal sealed class ConnectionResult
    {
        public string ModelName { get; set; }
        public string DisplayId { get; set; }
    }

    /// <summary>
    /// One of VoCore's M-PRO screens: its panel's own size, and whether a landscape picture goes onto it turned a
    /// quarter (most panels are portrait). Every model answers to the same USB ID and says which it is when asked;
    /// the models and sizes are those in VoCore's open driver (mpro.c).
    /// </summary>
    internal sealed class ScreenModel
    {
        public string Name { get; private set; }
        public int PanelWidth { get; private set; }
        public int PanelHeight { get; private set; }

        /// <summary>A portrait panel: the landscape picture goes onto it turned a quarter.</summary>
        public bool Turned => PanelWidth < PanelHeight;

        /// <summary>The picture's size, landscape on a portrait panel.</summary>
        public int Width => Turned ? PanelHeight : PanelWidth;
        public int Height => Turned ? PanelWidth : PanelHeight;

        /// <summary>
        /// Whole frames go with the original draw command, which the 5" LCD's panels expect 320 bytes of padding
        /// after (all but the D500FPC931A-A, version 3). Every other model, and any part of a frame, goes with the
        /// draw command that names its area.
        /// </summary>
        public bool WholeFrameCommand { get; private set; }
        public int FramePadding { get; private set; }

        /// <summary>The 5" LCD at 854×480, the screen this app was built on.</summary>
        public static readonly ScreenModel FiveInch = new ScreenModel { Name = "5\" M-PRO", PanelWidth = 480, PanelHeight = 854, WholeFrameCommand = true, FramePadding = 320 };

        /// <summary>The model a screen reports (its screen and version answers).</summary>
        public static ScreenModel For(uint screen, uint version)
        {
            switch (screen)
            {
                case 0x00000005: return new ScreenModel { Name = "5\" M-PRO", PanelWidth = 480, PanelHeight = 854, WholeFrameCommand = true, FramePadding = (version & 0xFF) == 3 ? 0 : 320 };
                case 0x00001005: return new ScreenModel { Name = "5\" OLED M-PRO", PanelWidth = 720, PanelHeight = 1280 };
                case 0x00000304: return new ScreenModel { Name = "4.3\" M-PRO", PanelWidth = 480, PanelHeight = 800 };
                case 0x00000004:
                case 0x00000104:
                case 0x00000B04: return new ScreenModel { Name = "4\" M-PRO", PanelWidth = 480, PanelHeight = 800 };
                case 0x00000007: return new ScreenModel { Name = "6.8\" M-PRO", PanelWidth = 800, PanelHeight = 480 };
                case 0x00000403: return new ScreenModel { Name = "3.4\" M-PRO", PanelWidth = 800, PanelHeight = 800 };
                case 0x0000000A: return new ScreenModel { Name = "10\" M-PRO", PanelWidth = 1024, PanelHeight = 600 };
                // No answer at all: treat it as the 5", as the app always has.
                case 0xFFFFFFFF: return FiveInch;
                // A model newer than this list: VoCore's driver assumes a 480×800 panel.
                default: return new ScreenModel { Name = "M-PRO", PanelWidth = 480, PanelHeight = 800 };
            }
        }
    }
}
