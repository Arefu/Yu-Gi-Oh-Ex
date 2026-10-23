using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WolfX
{
    /// <summary>Help > About WolfX: this build's version (Updater.Details), where it runs from, and the plugins and tools installed with it.</summary>
    internal sealed class AboutBox : Form
    {
        private const string RepoPage = "https://github.com/arefu/yu-gi-oh-ex";

        public AboutBox()
        {
            Text = "About WolfX";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 440);
            Padding = new Padding(12);

            var title = new Label { Text = "WolfX", Dock = DockStyle.Top, Height = 34, Font = new Font("Segoe UI", 16f, FontStyle.Bold) };
            var version = new Label
            {
                Dock = DockStyle.Top, Height = 64,
                Text = $"Version {Updater.Details}" + (Updater.LocalBuild ? "  (local build)" : "") + "\n" +
                       $"Built {File.GetLastWriteTime(typeof(AboutBox).Assembly.Location):yyyy-MM-dd HH:mm}   .NET {Environment.Version}   {RuntimeInformation.OSDescription}\n" +
                       $"Installed in {Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."))}",
            };
            var link = new LinkLabel { Text = RepoPage, Dock = DockStyle.Top, Height = 22 };
            link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(RepoPage) { UseShellExecute = true });

            // what came with this install: the loader, its plugins and the other tools, with their file versions
            var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            list.Columns.Add("Component", 250);
            list.Columns.Add("Version", 120);
            list.Columns.Add("Date", 130);
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            void Add(string path)
            {
                if (!File.Exists(path))
                    return;
                var info = FileVersionInfo.GetVersionInfo(path);
                string v = info.ProductVersion is { Length: > 0 } p ? p.Split('+')[0] : info.FileVersion ?? "";
                list.Items.Add(new ListViewItem([Path.GetRelativePath(root, path), v, File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm")]));
            }
            Add(Path.Combine(root, "Yu-Gi-Oh_Loader.exe"));
            if (Directory.Exists(Path.Combine(root, "Plugins")))
                foreach (var dll in Directory.GetFiles(Path.Combine(root, "Plugins"), "*.dll").Order())
                    Add(dll);
            foreach (var exe in Directory.GetFiles(AppContext.BaseDirectory, "*.exe").Order())
                Add(exe);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            var ok = new System.Windows.Forms.Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var check = new System.Windows.Forms.Button { Text = "Check for updates...", AutoSize = true };
            check.Click += (_, _) => Updater.CheckNow(Owner ?? this);
            buttons.Controls.AddRange([ok, check]);
            AcceptButton = CancelButton = ok;

            Controls.Add(list);
            Controls.Add(new Label { Text = "Installed with it:", Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.BottomLeft });
            Controls.Add(link);
            Controls.Add(version);
            Controls.Add(title);
            Controls.Add(buttons);
        }
    }
}
