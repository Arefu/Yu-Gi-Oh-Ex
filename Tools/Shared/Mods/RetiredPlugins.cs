using System.Text.Json;
using System.Text.Json.Nodes;

namespace Wolf.Mods
{
    /// <summary>
    /// Plugins an installed plugin's manifest "replaces" (Dependencies\Yu-Gi-Oh-Ex\Yu-Gi-Oh-Manifest.h): renamed (Yu-Gi-Oh-Music -&gt;
    /// Yu-Gi-Oh-Media), merged in, or retired. Copying a new build over an old folder leaves their DLLs behind; the loader never loads
    /// them and offers to move them itself. This finds those files and moves them to Plugins\_retired\&lt;date&gt; (nothing is deleted).
    /// </summary>
    public static class RetiredPlugins
    {
        public sealed record Leftover(string Name, string ReplacedBy, string Note, bool Gui, IReadOnlyList<string> Files)
        {
            public string Folder => Gui ? @"Plugins\YGO-Ex" : "Plugins";
            public string Why => $"replaced by {ReplacedBy}" + (Note.Length > 0 ? $": {Note}" : "");
        }

        private static readonly string[] Extensions = [".dll", ".json", ".pdb", ".lib", ".exp", ".ini"];

        /// <summary>The old plugins' files still in the plugin folders.</summary>
        public static List<Leftover> Find(string? pluginsFolder)
        {
            var found = new List<Leftover>();
            if (string.IsNullOrEmpty(pluginsFolder) || !Directory.Exists(pluginsFolder))
                return found;
            string gui = Path.Combine(pluginsFolder, "YGO-Ex");
            var folders = new[] { pluginsFolder, gui }.Where(Directory.Exists).ToList();

            // every installed plugin's "replaces" (a manifest whose own DLL is there; a leftover's manifest doesn't count)
            var replaced = new Dictionary<string, (string By, string Note)>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in folders)
            {
                foreach (string manifest in Directory.EnumerateFiles(folder, "*.json"))
                {
                    string name = Path.GetFileNameWithoutExtension(manifest);
                    if (!File.Exists(Path.Combine(folder, name + ".dll")) || LostToItsSuccessor(manifest))
                        continue;
                    foreach (var (old, note) in Replaces(manifest))
                        if (!old.Equals(name, StringComparison.OrdinalIgnoreCase))
                            replaced.TryAdd(old, (name, note));
                }
            }
            // a plugin that is itself replaced doesn't replace anything (it is a leftover too)
            foreach (string folder in folders)
            {
                foreach (var (old, (by, note)) in replaced)
                {
                    if (!File.Exists(Path.Combine(folder, old + ".dll")))
                        continue;
                    var files = Extensions.Select(e => Path.Combine(folder, old + e)).Where(File.Exists).ToList();
                    found.Add(new Leftover(old, by, note, folder == gui, files));
                }
            }
            return found;
        }

        /// <summary>
        /// Two plugins can each name the other in "replaces" when a name came back (Yu-Gi-Oh-Cards -&gt; Yu-Gi-Oh-MoreCards -&gt; Yu-Gi-Oh-Cards, and an old manifest
        /// is still in the folder). The newer version wins, as in Yu-Gi-Oh-Manifest.h: true for the one that lost, whose own "replaces" then counts for nothing.
        /// </summary>
        public static bool LostToItsSuccessor(string manifestPath)
        {
            string folder = Path.GetDirectoryName(manifestPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(manifestPath);
            foreach (var (other, _) in Replaces(manifestPath))
            {
                string otherManifest = Path.Combine(folder, other + ".json");
                if (!File.Exists(otherManifest) || !File.Exists(Path.Combine(folder, other + ".dll")))
                    continue;
                if (!Replaces(otherManifest).Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                int newer = CompareVersions(ManifestVersion(otherManifest), ManifestVersion(manifestPath));
                if (newer > 0 || (newer == 0 && string.CompareOrdinal(other, name) < 0))
                    return true;
            }
            return false;
        }

        private static string ManifestVersion(string manifestPath)
        {
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(manifestPath), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                return root?["version"] is JsonValue value && value.TryGetValue(out string? version) ? version : "";
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return "";
            }
        }

        private static int CompareVersions(string a, string b)
        {
            static long[] Parts(string text) => text.Split('.').Select(p => long.TryParse(new string(p.TakeWhile(char.IsAsciiDigit).ToArray()), out long n) ? n : 0).ToArray();
            long[] left = Parts(a), right = Parts(b);
            for (int i = 0; i < Math.Max(left.Length, right.Length); ++i)
            {
                long x = i < left.Length ? left[i] : 0, y = i < right.Length ? right[i] : 0;
                if (x != y)
                    return x < y ? -1 : 1;
            }
            return 0;
        }

        /// <summary>A manifest's "replaces": names, or { "name", "note" }.</summary>
        public static IEnumerable<(string Name, string Note)> Replaces(string manifestPath)
        {
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(manifestPath), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                yield break;
            }
            if (root?["replaces"] is not JsonArray list)
                yield break;
            foreach (var item in list)
            {
                if (item is JsonValue value && value.TryGetValue(out string? name) && name.Length > 0)
                    yield return (StripDll(name), "");
                else if (item is JsonObject entry && entry["name"] is JsonValue n && n.TryGetValue(out string? entryName) && entryName.Length > 0)
                    yield return (StripDll(entryName), entry["note"] is JsonValue t && t.TryGetValue(out string? note) ? note : "");
            }
        }

        private static string StripDll(string name) => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

        /// <summary>Moves the files to Plugins\_retired\&lt;yyyy-MM-dd&gt;[\YGO-Ex]; returns how many moved.</summary>
        public static int MoveToRetired(string pluginsFolder, IEnumerable<Leftover> leftovers)
        {
            string target = Path.Combine(pluginsFolder, "_retired", DateTime.Now.ToString("yyyy-MM-dd"));
            int moved = 0;
            foreach (var leftover in leftovers)
            {
                string folder = leftover.Gui ? Path.Combine(target, "YGO-Ex") : target;
                Directory.CreateDirectory(folder);
                foreach (string file in leftover.Files)
                {
                    string destination = Path.Combine(folder, Path.GetFileName(file));
                    if (File.Exists(destination))
                        destination = Path.Combine(folder, Path.GetFileNameWithoutExtension(file) + "-" + DateTime.Now.ToString("HHmmss") + Path.GetExtension(file));
                    File.Move(file, destination);
                    moved++;
                }
            }
            return moved;
        }

        /// <summary>The window WolfX and the Mod Manager show: the leftovers, why, and a button that moves them.</summary>
        public static void ShowDialog(IWin32Window owner, string? pluginsFolder)
        {
            var leftovers = Find(pluginsFolder);
            if (leftovers.Count == 0)
            {
                MessageBox.Show(owner, "No leftover plugins: every plugin in the Plugins folder is current.", "Retired plugins", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var dialog = new Form { Text = "Retired plugins", Width = 900, Height = 380, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
            var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
            list.Columns.Add("Plugin", 200);
            list.Columns.Add("Folder", 110);
            list.Columns.Add("Why", 540);
            foreach (var leftover in leftovers)
                list.Items.Add(new ListViewItem([leftover.Name, leftover.Folder, leftover.Why]));
            var note = new Label
            {
                Dock = DockStyle.Top, Height = 46, Padding = new Padding(8),
                Text = "These files are left over from an older Yu-Gi-Oh-Ex (a newer plugin's manifest replaces them). The game doesn't load them. " +
                       "Move them out of the way to Plugins\\_retired - nothing is deleted.",
            };
            var move = new Button { Text = "Move to Plugins\\_retired", AutoSize = true };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            move.Click += (_, _) =>
            {
                try
                {
                    int moved = MoveToRetired(pluginsFolder!, leftovers);
                    MessageBox.Show(dialog, $"Moved {moved} file(s) to {Path.Combine(pluginsFolder!, "_retired")}.", "Retired plugins", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    dialog.Close();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    MessageBox.Show(dialog, "Could not move the files (is the game running?):\n" + ex.Message, "Retired plugins", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(6) };
            buttons.Controls.Add(close);
            buttons.Controls.Add(move);
            dialog.Controls.Add(list);
            dialog.Controls.Add(note);
            dialog.Controls.Add(buttons);
            dialog.CancelButton = close;
            dialog.ShowDialog(owner);
        }
    }
}
