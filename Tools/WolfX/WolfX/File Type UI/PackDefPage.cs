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
        private string? _path;
        private ZibArchive? _archive;
        private string? _zibPath;
        private readonly HashSet<string> _changed = [];
        private PackDefRecord? _shown;
        private bool _binding;

        public PackDefPage()
        {
            InitializeComponent();
            SetEditable(false);
        }

        private PackDefRecord? SelectedRecord => _grid.CurrentRow?.DataBoundItem as PackDefRecord;

        // ---- events wired in the designer ----

        private void btnOpen_Click(object? sender, EventArgs e) => Open();

        private void btnSave_Click(object? sender, EventArgs e) => Save();

        private void btnBrowseZib_Click(object? sender, EventArgs e) => PickZib();

        private void _grid_SelectionChanged(object? sender, EventArgs e)
        {
            var record = SelectedRecord;
            _details.Text = record == null ? "" : $"{record.Title}\r\n{record.Text}";
            RecordSelected(record);
        }

        private void Contents_Changed(object? sender, EventArgs e) => Commit();

        // ---- file ----

        private void Open()
        {
            using var dialog = new OpenFileDialog { Filter = "packdefdata (*.bin)|*.bin|All files|*.*", Title = "Pack definitions" };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            try
            {
                CardCatalog.Get(this); // names for the cards (asks for a folder once)
                _records = new BindingList<PackDefRecord>(PackDefFile.Load(dialog.FileName).Records) { AllowNew = true, AllowRemove = true };
                _path = dialog.FileName;
                _pathBox.Text = _path;
                _changed.Clear();

                string? zib = RecordFileHelpers.FindArchive(_path, "packs.zib");
                if (zib != null)
                    UseZib(zib);

                _grid.DataSource = _records;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open the file:\n{ex.Message}", "Pack definitions", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Save()
        {
            if (_path == null)
                return;

            try
            {
                _grid.EndEdit();
                RecordFileHelpers.Backup(_path);

                var file = new PackDefFile();
                file.Records.AddRange(_records);
                File.WriteAllBytes(_path, file.ToBytes());

                if (_archive != null && _zibPath != null && _changed.Count > 0)
                {
                    RecordFileHelpers.Backup(_zibPath);
                    _archive.Save(_zibPath);
                    _changed.Clear();
                }
                MessageBox.Show($"Saved {_path}\n(originals are kept as .bak next to them)", "Pack definitions", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save:\n{ex.Message}", "Pack definitions", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PickZib()
        {
            using var dialog = new OpenFileDialog { Filter = "packs.zib|*.zib|All files|*.*", Title = "packs.zib" };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            UseZib(dialog.FileName);
            RecordSelected(SelectedRecord);
        }

        private void UseZib(string path)
        {
            try
            {
                _archive = ZibArchive.Load(path);
                _zibPath = path;
                _zibBox.Text = path;
            }
            catch (Exception ex)
            {
                _archive = null;
                _zibPath = null;
                _zibBox.Text = "";
                MessageBox.Show($"Could not read packs.zib:\n{ex.Message}", "Pack definitions", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                    _note.Text = _archive == null ? "Pick packs.zib to see and edit this pack's cards." : $"{record.ContentsFile} isn't in packs.zib.";
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
