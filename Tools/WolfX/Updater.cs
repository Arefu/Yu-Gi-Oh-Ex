using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WolfX
{
    /// <summary>
    /// Updates from the GitHub releases (.github/workflows/build-and-release.yml): the latest release's YuGiOhEX.zip has the loader,
    /// Plugins and Tools as Binaries\Release lays them out, the same as the install WolfX runs from (Tools\WolfX.exe, so the install is
    /// the folder above Tools). WolfX can't replace its own files while it runs, so it downloads and extracts the zip, then a PowerShell
    /// script waits for it to close, copies the new files over the install (Redist is left out) and starts the new WolfX.
    /// A startup check runs at most every 8 hours (%APPDATA%\WolfX\update.json); Tools > Check for updates runs one any time.
    /// </summary>
    internal static class Updater
    {
        private const string Repo = "arefu/yu-gi-oh-ex";
        private const string Asset = "YuGiOhEX.zip";
        private static readonly TimeSpan StartupInterval = TimeSpan.FromHours(8);

        private static string StateFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WolfX", "update.json");
        private static string WorkFolder => Path.Combine(Path.GetTempPath(), "WolfX-update");

        /// <summary>The folder the release zip's contents go into: the one above Tools.</summary>
        private static string InstallFolder => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));

        /// <summary>This build's version: the release tag (the workflow's /p:Version, or git describe for a local build; WolfX.csproj).</summary>
        public static Version Current
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
            }
        }

        /// <summary>The full version text: "1.1.11" for a release, "1.1.11-3-gabc1234-dirty" (git describe) for a local build.</summary>
        public static string Details =>
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Current.ToString();

        /// <summary>Not built by the release workflow (/p:ReleaseBuild=true): it runs from Binaries\&lt;config&gt;, which an update must not overwrite.</summary>
        public static bool LocalBuild => Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ReleaseBuild")?.Value != "true";

        private sealed record Release(Version Version, string Tag, string Page, string? ZipUrl, long ZipSize, string Notes);

        /// <summary>At startup: says so when a newer release is out, at most every 8 hours, never for a local build or a skipped version.</summary>
        public static async void CheckAtStartup(Form owner)
        {
            CleanUp();
            if (LocalBuild)
                return;
            var state = ReadState();
            if (state["lastCheck"] is JsonValue last && last.TryGetValue<DateTime>(out var when) && DateTime.UtcNow - when < StartupInterval)
                return;
            var release = await Latest();
            if (release == null)
                return;   // offline or GitHub didn't answer: try again next start
            state["lastCheck"] = DateTime.UtcNow;
            WriteState(state);
            if (release.Version <= Current || (state["skipped"] is JsonValue skipped && skipped.TryGetValue<string>(out var tag) && tag == release.Tag))
                return;
            if (!owner.IsDisposed)
                Offer(owner, release, fromStartup: true);
        }

        /// <summary>Tools > Check for updates.</summary>
        public static async void CheckNow(Form owner)
        {
            Cursor.Current = Cursors.WaitCursor;
            var release = await Latest();
            Cursor.Current = Cursors.Default;
            if (release == null)
            {
                MessageBox.Show(owner, "Could not reach GitHub to check for updates. Check your connection and try again.", "Check for updates",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var state = ReadState();
            state["lastCheck"] = DateTime.UtcNow;
            state.Remove("skipped");   // asked by hand: a skipped version is offered again
            WriteState(state);
            if (release.Version <= Current)
            {
                MessageBox.Show(owner, $"WolfX is up to date ({Details}; the latest release is {release.Tag}).", "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Offer(owner, release, fromStartup: false);
        }

        private static void Offer(Form owner, Release release, bool fromStartup)
        {
            string current = LocalBuild ? $"a local build, {Details}" : Current.ToString();
            string notes = release.Notes.Length > 600 ? release.Notes[..600] + "..." : release.Notes;
            if (release.ZipUrl == null || LocalBuild)
            {
                // a local build runs from Binaries\<config>\Tools: installing there would overwrite what was just built
                string why = LocalBuild ? "This is a local build, so it isn't updated in place" : $"It has no {Asset} to install from";
                if (MessageBox.Show(owner, $"Yu-Gi-Oh-Ex {release.Tag} is out (you have {current}). {why}.\n\nOpen the release page?",
                        "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    OpenPage(release.Page);
                return;
            }
            string text = $"Yu-Gi-Oh-Ex {release.Tag} is out (you have {current}).\n\n" + (notes.Length > 0 ? notes + "\n\n" : "") +
                          $"Update now? WolfX downloads it ({release.ZipSize / 1048576.0:0.#} MB), closes, replaces the loader, Plugins and Tools in\n{InstallFolder}\n" +
                          "and opens again. Close the game first. Your Yu-Gi-Oh-Ex content, mods, saves and Config.ini are not touched.\n\n" +
                          (fromStartup ? "Yes = update, No = not now, Cancel = skip this version" : "Yes = update, No = not now, Cancel = open the release page");
            var answer = MessageBox.Show(owner, text, "Update available", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
                Install(owner, release);
            else if (answer == DialogResult.Cancel && fromStartup)
            {
                var state = ReadState();
                state["skipped"] = release.Tag;
                WriteState(state);
            }
            else if (answer == DialogResult.Cancel)
                OpenPage(release.Page);
        }

        // ---- GitHub ----

        private static HttpClient Client()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"WolfX/{Current}");   // GitHub refuses requests without one
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }

        /// <summary>The latest full release (not a draft or pre-release), null when GitHub can't be reached.</summary>
        private static async Task<Release?> Latest()
        {
            try
            {
                using var http = Client();
                var json = JsonNode.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
                if (json is not JsonObject release)
                    return null;
                string tag = release["tag_name"]?.GetValue<string>() ?? "";
                if (!Version.TryParse(tag.TrimStart('v', 'V').Split('-')[0], out var version))
                    return null;
                var zip = (release["assets"] as JsonArray)?.OfType<JsonObject>()
                    .FirstOrDefault(a => string.Equals(a["name"]?.GetValue<string>(), Asset, StringComparison.OrdinalIgnoreCase));
                return new Release(new Version(version.Major, version.Minor, Math.Max(0, version.Build)), tag,
                    release["html_url"]?.GetValue<string>() ?? $"https://github.com/{Repo}/releases",
                    zip?["browser_download_url"]?.GetValue<string>(), zip?["size"]?.GetValue<long>() ?? 0,
                    (release["body"]?.GetValue<string>() ?? "").Trim());
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                return null;
            }
        }

        // ---- installing ----

        private static void Install(Form owner, Release release)
        {
            if (owner is WolfUI ui && !ui.ConfirmDiscard("update WolfX"))
                return;
            string zip = Path.Combine(WorkFolder, Asset), staged = Path.Combine(WorkFolder, "files");
            using var progress = new UpdateProgress(release.Tag);
            progress.Shown += async (_, _) =>
            {
                try
                {
                    if (Directory.Exists(WorkFolder))
                        Directory.Delete(WorkFolder, true);
                    Directory.CreateDirectory(staged);
                    using (var http = Client())
                    {
                        http.Timeout = TimeSpan.FromMinutes(30);
                        using var response = await http.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();
                        long total = response.Content.Headers.ContentLength ?? release.ZipSize;
                        await using var source = await response.Content.ReadAsStreamAsync();
                        await using var target = File.Create(zip);
                        var buffer = new byte[1 << 16];
                        long done = 0;
                        int read;
                        while ((read = await source.ReadAsync(buffer)) > 0)
                        {
                            if (progress.Cancelled)
                                return;
                            await target.WriteAsync(buffer.AsMemory(0, read));
                            done += read;
                            progress.Report(total > 0 ? (int)(done * 100 / total) : 0, $"Downloading {done / 1048576.0:0.0} of {total / 1048576.0:0.0} MB");
                        }
                    }
                    progress.Report(100, "Extracting...");
                    await Task.Run(() => ZipFile.ExtractToDirectory(zip, staged, overwriteFiles: true));
                    if (!File.Exists(Path.Combine(staged, "Tools", "WolfX.exe")))
                        throw new InvalidDataException($"{Asset} has no Tools\\WolfX.exe: it is not laid out like the install, nothing was replaced.");
                    StartSwap(staged);
                    progress.Finished = true;
                    progress.Close();
                }
                catch (Exception ex)
                {
                    progress.Close();
                    MessageBox.Show(owner, "The update failed, nothing was replaced:\n\n" + ex.Message, "Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            progress.ShowDialog(owner);
            if (progress.Finished)
            {
                WolfUI.SkipCloseConfirm = true;   // the user agreed to close above
                Application.Exit();
            }
        }

        /// <summary>
        /// Writes and starts the script that finishes the update once WolfX has closed: robocopy retries files that are still locked (the
        /// game's plugins while it runs) for a minute, Redist is left out, files the release dropped are left alone, then WolfX starts again.
        /// </summary>
        private static void StartSwap(string staged)
        {
            static string Quote(string path) => "'" + path.Replace("'", "''") + "'";
            string install = InstallFolder.TrimEnd('\\');
            string log = Path.Combine(WorkFolder, "update.log");
            string script = string.Join("\r\n",
            [
                "$ErrorActionPreference = 'Continue'",
                "$Host.UI.RawUI.WindowTitle = 'Updating Yu-Gi-Oh-Ex'",
                "Write-Host 'Waiting for WolfX to close...'",
                $"Wait-Process -Id {Environment.ProcessId} -Timeout 120 -ErrorAction SilentlyContinue",
                "Write-Host 'Copying the new files...'",
                $"robocopy {Quote(staged)} {Quote(install)} /E /R:60 /W:1 /XD Redist /NFL /NDL /NP /LOG:{Quote(log)}",
                "$code = $LASTEXITCODE",
                "if ($code -ge 8) {",
                $"  Write-Host \"Some files could not be replaced (robocopy code $code). Is the game still running? See {log}\"",
                "  Read-Host 'Press Enter to close'",
                "} else {",
                $"  Start-Process -FilePath {Quote(Path.Combine(install, "Tools", "WolfX.exe"))}",
                "}",
            ]);
            string path = Path.Combine(WorkFolder, "update.ps1");
            File.WriteAllText(path, script);
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{path}\"")
            {
                UseShellExecute = true,
                WorkingDirectory = WorkFolder,
            });
        }

        /// <summary>The last update's download and extracted files, once the copy is long over (the new WolfX starts right after it).</summary>
        private static void CleanUp()
        {
            try
            {
                string log = Path.Combine(WorkFolder, "update.log");
                if (Directory.Exists(WorkFolder) && (!File.Exists(log) || File.GetLastWriteTimeUtc(log) < DateTime.UtcNow.AddMinutes(-10)))
                    Directory.Delete(WorkFolder, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void OpenPage(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        private static JsonObject ReadState()
        {
            try
            {
                return File.Exists(StateFile) && JsonNode.Parse(File.ReadAllText(StateFile)) is JsonObject state ? state : [];
            }
            catch (Exception ex) when (ex is IOException or JsonException) { return []; }
        }

        private static void WriteState(JsonObject state)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
                File.WriteAllText(StateFile, state.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (IOException) { }
        }

        /// <summary>The download's progress bar, with Cancel.</summary>
        private sealed class UpdateProgress : Form
        {
            private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 22 };
            private readonly Label _text = new() { Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleLeft };
            public bool Cancelled { get; private set; }
            public bool Finished { get; set; }

            public UpdateProgress(string tag)
            {
                Text = $"Updating to {tag}";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = MinimizeBox = false;
                StartPosition = FormStartPosition.CenterParent;
                ClientSize = new Size(420, 96);
                Padding = new Padding(10);
                var cancel = new System.Windows.Forms.Button { Text = "Cancel", Dock = DockStyle.Bottom, Height = 26 };
                cancel.Click += (_, _) => { Cancelled = true; Close(); };
                Controls.Add(cancel);
                Controls.Add(_bar);
                Controls.Add(_text);
                _text.Text = "Starting the download...";
                FormClosing += (_, _) => Cancelled |= !Finished;
            }

            public void Report(int percent, string text)
            {
                _bar.Value = Math.Clamp(percent, 0, 100);
                _text.Text = text;
            }
        }
    }
}
