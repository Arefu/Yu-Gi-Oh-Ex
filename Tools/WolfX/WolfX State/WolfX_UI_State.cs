using WolfX.Types;

namespace WolfX
{
    partial class WolfUI
    {
        internal static class State
        {
            internal static string? WorkingDirectory { get; set; }

            public static Language Language { get; set; } = Language.English;

        }
    }
}