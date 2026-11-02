using System.Globalization;

namespace NetMute
{
    // Two-language text: Chinese on a Chinese Windows, English everywhere else.
    internal static class L
    {
        public static readonly bool IsChinese =
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";

        public static string T(string zh, string en)
        {
            return IsChinese ? zh : en;
        }
    }
}
