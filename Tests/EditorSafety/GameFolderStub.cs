// The report tests use temporary folders instead of opening the multi-gigabyte game archive.
namespace Wolf.Editors
{
    public sealed class GameFolderFiles
    {
        public string GameFolder { get; init; } = "";
        public string ExFolder { get; init; } = "";
        public string ExPath(string name) => Path.Combine(ExFolder, name);
    }
}
