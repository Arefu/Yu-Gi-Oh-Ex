using WolfX.Types;

namespace WolfX
{
    internal enum Language
    {
        English = 'E',
        French = 'F',
        German = 'G',
        Spanish = 'S',
        Italian = 'I',
        Russian = 'R', /*Doesn't seem to be supported */
        Japanese = 'J'
    }

    internal static class State
        {
            internal static string? Path { get; set; }
            public static Language Language { get; set; } = Language.English;
        }
}