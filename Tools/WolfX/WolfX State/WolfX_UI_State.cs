using WolfX.Types;

namespace WolfX
{
    partial class WolfUI
    {
        internal static class State
        {
            internal static string? WorkingDirectory { get; set; }
            internal static bool? IsLoaded { get; set; }
            public static bool HasArchiveOpen { get; set; }

            public static Language Language { get; set; } = Language.English;
            public static List<Card> Cards { get; set; } = new List<Card>();

            public static int CardIndex = 1;
        }
    }
}