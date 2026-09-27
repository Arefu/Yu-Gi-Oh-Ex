using System.ComponentModel;
using StartingCollection;

namespace WolfX.Types
{
    /// <summary>Search cards by name (or id), tick as many as you like, or paste a list of names. Returns the chosen cards.</summary>
    public sealed partial class CardPickerDialog : Form
    {
        private sealed class Row
        {
            public bool Selected { get; set; }
            public string Name { get; set; } = "";
            public int Id { get; set; }
        }

        private const int MaxShown = 3000;

        private CardDatabase _database = CardDatabase.IdsOnly();
        private readonly Dictionary<int, int> _chosen = [];   // id -> copies (from a pasted list), or 0 for "use the copies box"
        private BindingList<Row> _rows = [];
        private bool _loading;

        /// <summary>The picked cards and how many copies of each.</summary>
        public List<(CardEntry Card, int Copies)> Result { get; } = [];

        /// <summary>For the designer.</summary>
        public CardPickerDialog()
        {
            InitializeComponent();
        }

        public CardPickerDialog(CardDatabase database, string title = "Add cards", bool askCopies = true, int maxCopies = 3) : this()
        {
            _database = database;
            Text = title;
            _copies.Maximum = Math.Max(1, maxCopies);
            lblCopies.Visible = _copies.Visible = askCopies;
            Refill();
        }

        // ---- events wired in the designer ----

        private void _search_TextChanged(object? sender, EventArgs e) => Refill();

        private void btnTickAll_Click(object? sender, EventArgs e) => SetShown(true);

        private void btnTickHighlighted_Click(object? sender, EventArgs e) => TickHighlighted();

        private void btnClear_Click(object? sender, EventArgs e)
        {
            _chosen.Clear();
            Refill();
        }

        private void btnPaste_Click(object? sender, EventArgs e) => PasteNames();

        private void _grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (_loading || e.RowIndex < 0 || e.ColumnIndex != colAdd.Index)
                return;
            Toggle(_rows[e.RowIndex], _rows[e.RowIndex].Selected);
        }

        private void _grid_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
        {
            if (_grid.IsCurrentCellDirty)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void _grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex == colAdd.Index)
                return;

            var row = _rows[e.RowIndex];
            row.Selected = !row.Selected;
            Toggle(row, row.Selected);
            _grid.Refresh();
        }

        private void CardPickerDialog_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK)
                return;

            Result.Clear();
            foreach (var (id, copies) in _chosen)
                Result.Add((_database.ById(id), copies == 0 ? (int)_copies.Value : copies));
        }

        // ---- logic ----

        private void Toggle(Row row, bool on)
        {
            if (on)
                _chosen.TryAdd(row.Id, 0);
            else
                _chosen.Remove(row.Id);
            UpdateStatus();
        }

        private void Refill()
        {
            _loading = true;
            try
            {
                var shown = _database.Search(_search.Text).Take(MaxShown);
                _rows = new BindingList<Row>(shown.Select(card => new Row { Selected = _chosen.ContainsKey(card.Id), Name = card.Name + (card.IsCustom ? "  [custom]" : ""), Id = card.Id }).ToList())
                {
                    AllowNew = false,
                    AllowEdit = true,
                    AllowRemove = false,
                };
                _grid.DataSource = _rows;
            }
            finally { _loading = false; }
            UpdateStatus();
        }

        private void SetShown(bool on)
        {
            _loading = true;
            try
            {
                foreach (var row in _rows)
                {
                    row.Selected = on;
                    if (on)
                        _chosen.TryAdd(row.Id, 0);
                    else
                        _chosen.Remove(row.Id);
                }
            }
            finally { _loading = false; }
            _grid.Refresh();
            UpdateStatus();
        }

        private void TickHighlighted()
        {
            _loading = true;
            try
            {
                foreach (DataGridViewRow gridRow in _grid.SelectedRows)
                {
                    var row = _rows[gridRow.Index];
                    row.Selected = true;
                    _chosen.TryAdd(row.Id, 0);
                }
            }
            finally { _loading = false; }
            _grid.Refresh();
            UpdateStatus();
        }

        private void PasteNames()
        {
            using var dialog = new PasteNamesDialog();
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var matches = _database.Match(dialog.PastedText, out var unmatched);
            foreach (var (card, count) in matches)
                _chosen[card.Id] = count > 1 ? count : 0;

            Refill();
            if (unmatched.Count > 0)
                MessageBox.Show(this, "These didn't match exactly one card:\n\n" + string.Join("\n", unmatched.Take(40)), "Paste names", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UpdateStatus() =>
            _status.Text = $"{_rows.Count} shown{(_rows.Count == MaxShown ? " (first 3000 - type to narrow)" : "")}, {_chosen.Count} ticked overall.";
    }
}
