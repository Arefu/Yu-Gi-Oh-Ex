using System.ComponentModel;
using System.IO;
using PackDef;
using Types;

namespace WolfX.Types
{
    /// <summary>
    /// main/packdefdata_#.bin (the packs: name, cost, kind, texts) plus the cards inside each pack, which live in packs.zib: a reward pack's
    /// common and rare lists (packdata_&lt;name&gt;.bin), or a battle pack's slots (bpack_&lt;name&gt;.bin: one pool per card of an opened pack,
    /// a card listed more often coming up more often). All editable, so vanilla packs can be changed without adding any new card.
    /// </summary>
    public sealed partial class PackDefPage : UserControl
    {
        private BindingList<PackDefRecord> _records = [];
        private ZibArchive? _archive;
        private readonly HashSet<string> _changed = [];
        private PackDefRecord? _shown;
        private BattlePackContents? _battle;
        private bool _binding;
        private readonly FlowLayoutPanel _slotBar = new() { Dock = DockStyle.Top, Height = 30, Visible = false, WrapContents = false };
        private readonly ComboBox _slot = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };

        public PackDefPage()
        {
            InitializeComponent();
            SetEditable(false);
            btnOpen.Text = "Reload";
            btnBrowseZib.Visible = false;
            // a battle pack: pick the slot, its pool is shown in the first list (repeats allowed: they are the odds)
            _slotBar.Controls.Add(new Label { Text = "Slot:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
            _slotBar.Controls.Add(_slot);
            _slotBar.Controls.Add(new Label
            {
                Text = "Each card of an opened battle pack is drawn from its slot's list; a card listed more often comes up more often.",
                AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 7, 3, 3),
            });
            _detailsPanel.Controls.Add(_slotBar);
            _slot.SelectedIndexChanged += (_, _) => ShowSlot();
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

        /// <summary>Reward pack: common + rare, each card once. Battle pack: the slot picker and one list, repeats allowed.</summary>
        private void UseLayout(bool battle)
        {
            _slotBar.Visible = battle;
            _rare.Visible = !battle;
            _common.AllowDuplicates = battle;
            _common.MaxCopies = battle ? 20 : 3;
            if (!battle)
                _common.Title = "Common cards:";
        }

        private void ShowSlot()
        {
            if (_battle == null || _slot.SelectedIndex < 0 || _slot.SelectedIndex >= _battle.Slots.Count)
                return;
            bool was = _binding;
            _binding = true;
            try
            {
                var pool = _battle.Slots[_slot.SelectedIndex];
                _common.Title = $"Slot {_slot.SelectedIndex + 1}: {pool.Count} entries, {pool.Distinct().Count()} different cards";
                _common.Ids = pool;
            }
            finally { _binding = was; }
        }

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

                _battle = null;
                byte[]? data = _archive?.Get(record.ContentsFile);
                if (data == null)
                {
                    UseLayout(false);
                    SetEditable(false);
                    _note.Text = _archive == null ? "packs.zib isn't in the open data, so the cards can't be shown." : $"{record.ContentsFile} isn't in packs.zib.";
                    return;
                }

                if (!record.IsReward)
                {
                    _battle = BattlePackContents.Parse(data);
                    UseLayout(true);
                    _slot.Items.Clear();
                    for (int i = 0; i < _battle.Slots.Count; i++)
                        _slot.Items.Add($"Slot {i + 1} ({_battle.Slots[i].Distinct().Count()} cards)");
                    if (_slot.Items.Count > 0)
                        _slot.SelectedIndex = 0;
                    ShowSlot();
                    SetEditable(true);
                    _note.Text = $"{record.ContentsFile} (battle pack, {_battle.Slots.Count} slots): saved into packs.zib when you press Save.";
                    return;
                }

                UseLayout(false);
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
            if (_binding || _shown == null || _archive == null)
                return;
            if (!_shown.IsReward)
            {
                if (_battle == null || _slot.SelectedIndex < 0)
                    return;
                _battle.Slots[_slot.SelectedIndex] = _common.Ids;
                _slot.Items[_slot.SelectedIndex] = $"Slot {_slot.SelectedIndex + 1} ({_common.Ids.Distinct().Count()} cards)";
                _archive.Set(_shown.ContentsFile, _battle.ToBytes());
                _changed.Add(_shown.ContentsFile);
                return;
            }

            var contents = new PackContents();
            contents.Common.AddRange(_common.Ids);
            contents.Rare.AddRange(_rare.Ids);
            _archive.Set(_shown.ContentsFile, contents.ToBytes());
            _changed.Add(_shown.ContentsFile);
        }
    }
}
