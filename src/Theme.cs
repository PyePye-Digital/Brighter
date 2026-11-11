using System.Windows.Media;

namespace Brighter
{
    /// <summary>Colours and fonts shared by the whole UI.</summary>
    internal static class Theme
    {
        public static readonly Color AccentStart = FromHex("#FF9F43");
        public static readonly Color AccentEnd = FromHex("#FFD86B");

        public static readonly Brush WindowBg = Make("#14161C");
        public static readonly Brush Card = Make("#1D2029");
        public static readonly Brush CardBorder = Make("#2A2E3A");
        public static readonly Brush Chip = Make("#272B37");
        public static readonly Brush ChipHover = Make("#363B4B");
        public static readonly Brush Track = Make("#2C3040");
        public static readonly Brush Text = Make("#F2F4F8");
        public static readonly Brush Muted = Make("#8B91A3");
        public static readonly Brush Accent = Make("#FFB84D");
        public static readonly Brush AccentGlow = Make("#40FFB84D");
        public static readonly Brush Thumb = Make("#FFFFFF");
        public static readonly Brush CloseHover = Make("#E5484D");

        public static readonly FontFamily Ui = new FontFamily("Segoe UI");
        public static readonly FontFamily Icons = new FontFamily("Segoe MDL2 Assets");

        private static Color FromHex(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        private static Brush Make(string hex)
        {
            SolidColorBrush brush = new SolidColorBrush(FromHex(hex));
            brush.Freeze();
            return brush;
        }
    }
}
