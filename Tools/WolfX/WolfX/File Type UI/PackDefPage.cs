using System.ComponentModel;
using System.IO;
using PackDef;
using Types;

namespace WolfX.Types
{
    /// <summary>
    /// main/packdefdata_#.bin (the packs: name, cost, kind, texts) plus the cards inside each reward pack, which live in packs.zib as
    /// packdata_&lt;name&gt;.bin. Both are editable, so vanilla packs can be changed without adding any new card.
    /// </summary>
    public sealed partial class PackDefPage : UserControl
    {
        private BindingList<PackDefRecord> _records = [];
        private ZibArchive? _archive;
        private readonly HashSet<string> _changed = [];
        private PackDefRecord? _shown;
        private bool _binding;

        public PackDefPage()
        {
            InitializeComponent();
            SetEditable(false);
            btnOpen.Text = "Reload";
            btnBrowseZib.Visible = false;
            Wolf.Editors.GameFolderFiles.CurrentChanged += () => { if (IsHandleCreated) Open(); };
            HandleCreated += (_, _) => Open();
        }

        private static string FilePath => Path.Combine("main", $"packdefdata_{(char)State.Language}.bin");

        private PackDefRecord? SelectedRecord => _grid.CurrentRow?.DataBoundItem as PackDefRecord;

        // ---- events wired in the designer ----

        private void btnOpen_Click(object? sender, EventArgs e) => Open();

        private void btnSave_Click(object? sender, EventArgs e) => Save();

        private void btnBrowseZib_Click(object? sender, EventArgs e) { }

        private void _grid_SelectionChanged(object? sender, EventArgs e)
        {
            var record = SelectedRecord;
            _details.Text = record == null ? "" : $"{record.Title}\r\n{record.Text}";
            RecordSelected(record);
        }

        private void Contents_Changed(object? sender, EventArgs e) => Commit();

        // ---- file: the open game data (Wolf.Editors.GameFolderFiles.Current) ----

        private void Open()
        {
            var files = Wolf.Editors.GameFolderFiles.Current;
            if (files?.Available != true || files.Read(FilePath) is not { } data)
            {
                _pathBox.Text = files == null ? "Open the game folder or an extracted YGO_2020 folder." : $"{FilePath} isn't in the open data.";
                return;
            }
            try
            {
                _records = new BindingList<PackDefRecord>(PackDefFile.Parse(data).Records) { AllowNew = true, AllowRemove = true };
                _pathBox.Text = $"{FilePath} ({files.Describe(FilePath)})";
                _changed.Clear();
                _archive = files.Read("packs.zib") is { } zib ? ZibArchive.Parse(zib) : null;
                _zibBox.Text = _archive != null ? $"packs.zib ({files.Describe("packs.zib")})" : "packs.zib isn't in the open data.";
                _grid.DataSource = _records;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open {FilePath}:\n{ex.Message}", "Pack definitions", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Save()
        {
            var files = Wolf.Editors.GameFolderFiles.Current;
            if (files == null || _grid.DataSource == null)
                return;

            try
            {
                _grid.EndEdit();
                var file = new PackDefFile();
                file.Records.AddRange(_records);
                var write = new Dictionary<string, byte[]> { [FilePath] = file.ToBytes() };
                if (_archive != null && _changed.Count > 0)
                    write["packs.zib"] = _archive.ToBytes();
                files.Write(write);
                _changed.Clear();
                MessageBox.Show($"Saved {string.Join(" and ", write.Keys)} into {files.Describe(FilePath)}.", "Pack definitions", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save:\n{ex.Message}", "Pack definitions", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---- the cards of the selected pack ----

        private void SetEditable(bool editable) => _common.Enabled = _rare.Enabled = editable;

        private void RecordSelected(PackDefRecord? record)
        {
            _shown = record;
            _binding = true;
            try
            {
                _common.Ids = [];
                _rare.Ids = [];

                if (record == null)
                {
                    SetEditable(false);
                    _note.Text = "";
                    return;
                }

                if (!record.IsReward)
                {
                    SetEditable(false);
                    _note.Text = "Battle pack contents (bpack files) can't be edited yet.";
                    return;
                }

                byte[]? data = _archive?.Get(record.ContentsFile);
                if (data == null)
                {
                    SetEditable(false);
                    _note.Text = _archive == null ? "packs.zib isn't in the open data, so the cards can't be shown." : $"{record.ContentsFile} isn't in packs.zib.";
                    return;
                }

                var contents = PackContents.Parse(data);
                _common.Ids = contents.Common;
                _rare.Ids = contents.Rare;
                SetEditable(true);
                _note.Text = $"{record.ContentsFile}: saved into packs.zib when you press Save.";
            }
            finally { _binding = false; }
        }

        private void Commit()
        {
            if (_binding || _shown == null || _archive == null || !_shown.IsReward)
                return;

            var contents = new PackContents();
            contents.Common.AddRange(_common.Ids);
            contents.Rare.AddRange(_rare.Ids);
            _archive.Set(_shown.ContentsFile, contents.ToBytes());
            _changed.Add(_shown.ContentsFile);
        }
    }
}
