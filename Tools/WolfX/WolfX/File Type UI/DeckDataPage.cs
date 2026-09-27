using System.ComponentModel;
using System.IO;
using DeckData;
using Types;

namespace WolfX.Types
{
    /// <summary>
    /// main/deckdata_#.bin (the decks: slot, price, SKU, file name, texts) plus the cards of each deck, which live in decks.zib as
    /// &lt;file name&gt;.ydc. Both are editable, so vanilla decks can be changed without adding any new card.
    /// </summary>
    public sealed partial class DeckDataPage : UserControl
    {
        private BindingList<DeckRecord> _records = [];
        private string? _path;
        private ZibArchive? _archive;
        private string? _zibPath;
        private readonly Dictionary<string, YdcDeck> _decks = [];
        private readonly HashSet<string> _changed = [];
        private DeckRecord? _shown;
        private bool _binding;

        public DeckDataPage()
        {
            InitializeComponent();
            SetEditable(false);
        }

        private DeckRecord? SelectedRecord => _grid.CurrentRow?.DataBoundItem as DeckRecord;

        private static string DeckFile(DeckRecord record) => record.FileName + ".ydc";

        // ---- events wired in the designer ----

        private void btnOpen_Click(object? sender, EventArgs e) => Open();

        private void btnSave_Click(object? sender, EventArgs e) => Save();

        private void btnBrowseZib_Click(object? sender, EventArgs e) => PickZib();

        private void _grid_SelectionChanged(object? sender, EventArgs e)
        {
            var record = SelectedRecord;
            _details.Text = record == null ? "" : $"{record.Title}\r\n{record.Text2}\r\n{record.Text3}";
            RecordSelected(record);
        }

        private void Contents_Changed(object? sender, EventArgs e) => Commit();

        // ---- file ----

        private void Open()
        {
            using var dialog = new OpenFileDialog { Filter = "deckdata (*.bin)|*.bin|All files|*.*", Title = "Deck data" };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            try
            {
                CardCatalog.Get(this); // names for the cards (asks for a folder once)
                _records = new BindingList<DeckRecord>(DeckDataFile.Load(dialog.FileName).Records) { AllowNew = true, AllowRemove = true };
                _path = dialog.FileName;
                _pathBox.Text = _path;
                _changed.Clear();
                _decks.Clear();

                string? zib = RecordFileHelpers.FindArchive(_path, "decks.zib");
                if (zib != null)
                    UseZib(zib);

                _grid.DataSource = _records;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open the file:\n{ex.Message}", "Deck data", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

                var file = new DeckDataFile();
                file.Records.AddRange(_records);
                file.Save(_path);

                if (_archive != null && _zibPath != null && _changed.Count > 0)
                {
                    foreach (string name in _changed)
                        _archive.Set(name, _decks[name].ToBytes());

                    RecordFileHelpers.Backup(_zibPath);
                    _archive.Save(_zibPath);
                    _changed.Clear();
                }
                MessageBox.Show($"Saved {_path}\n(originals are kept as .bak next to them)", "Deck data", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save:\n{ex.Message}", "Deck data", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PickZib()
        {
            using var dialog = new OpenFileDialog { Filter = "decks.zib|*.zib|All files|*.*", Title = "decks.zib" };
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
                _decks.Clear();
                _changed.Clear();
            }
            catch (Exception ex)
            {
                _archive = null;
                _zibPath = null;
                _zibBox.Text = "";
                MessageBox.Show($"Could not read decks.zib:\n{ex.Message}", "Deck data", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---- the cards of the selected deck ----

        private void SetEditable(bool editable) => _main.Enabled = _extra.Enabled = _side.Enabled = editable;

        private void RecordSelected(DeckRecord? record)
        {
            _shown = record;
            _binding = true;
            try
            {
                _main.Ids = [];
                _extra.Ids = [];
                _side.Ids = [];

                if (record == null)
                {
                    SetEditable(false);
                    _note.Text = "";
                    return;
                }

                string file = DeckFile(record);
                if (!_decks.TryGetValue(file, out var deck))
                {
                    byte[]? data = _archive?.Get(file);
                    if (data == null)
                    {
                        SetEditable(false);
                        _note.Text = _archive == null ? "Pick decks.zib to see and edit this deck's cards." : $"{file} isn't in decks.zib.";
                        return;
                    }
                    _decks[file] = deck = YdcDeck.Parse(data);
                }

                _main.Ids = deck.Main;
                _extra.Ids = deck.Extra;
                _side.Ids = deck.Side;
                SetEditable(true);
                _note.Text = $"{file}: saved into decks.zib when you press Save.";
            }
            finally { _binding = false; }
        }

        private void Commit()
        {
            if (_binding || _shown == null || _archive == null)
                return;

            string file = DeckFile(_shown);
            if (!_decks.TryGetValue(file, out var deck))
                return;

            deck.Main.Clear();
            deck.Main.AddRange(_main.Ids);
            deck.Extra.Clear();
            deck.Extra.AddRange(_extra.Ids);
            deck.Side.Clear();
            deck.Side.AddRange(_side.Ids);
            _changed.Add(file);
        }
    }
}
