// Open Keydial — user interface languages.
// English is the default; [General] Language = en | fr in the settings file.
// Strings live next to the code that uses them: L.T("English", "Français").

namespace OpenKeydial
{
    static class L
    {
        public static bool Fr { get; private set; }
        public static string Code => Fr ? "fr" : "en";
        public static void Set(string code) => Fr = Str.Norm(code ?? "").StartsWith("fr");
        public static string T(string en, string fr) => Fr ? fr : en;

        public static readonly string[] Codes = { "en", "fr" };
        public static readonly string[] Names = { "English", "Français" };
    }
}
