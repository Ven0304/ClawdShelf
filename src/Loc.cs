using System.Globalization;

namespace ClawdShelf
{
    /// <summary>Tiny two-language switch: Chinese when Windows is in Chinese, English otherwise.</summary>
    public static class Loc
    {
        public static bool Zh = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";

        public static void Apply(string setting)
        {
            if (setting == "zh") Zh = true;
            else if (setting == "en") Zh = false;
        }

        public static string T(string zh, string en) { return Zh ? zh : en; }
    }
}
