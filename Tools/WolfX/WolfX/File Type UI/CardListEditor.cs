using System.ComponentModel;

namespace WolfX.Types
{
    /// <summary>A list of cards shown by name, with Add (opens the picker), Remove, Clear and Sort. The list keeps the order it was given.</summary>
    public sealed partial class CardListEditor : UserControl
    {
        private readonly List<ushort> _ids = [];
        private string _titleText = "";

        public CardListEditor()
        {
            InitializeComponent();
        }

        /// <summary>The heading above the list (also names the picker).</summary>
        [Category("Appearance"), DefaultValue("")]
        public string Title
        {
            get => _titleText;
            set
            {
                _titleText = value;
                _title.Text = value;
            }
        }

        /// <summary>The most cards the list may hold (0 = no limit).</summary>
        [Category("Behavior"), DefaultValue(0)]
        public int Capacity { get; set; }

        /// <summary>false for lists where a card appears once (pack contents).</summary>
        [Category("Behavior"), DefaultValue(true)]
        public bool AllowDuplicates { get; set; } = true;

        /// <summary>The most copies of one card the picker offers.</summary>
        [Category("Behavior"), DefaultValue(3)]
        public int MaxCopies { get; set; } = 3;

        public event EventHandler? Changed;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public List<ushort> Ids
        {
            get => [.. _ids];
            set
            {
                _ids.Clear();
                _ids.AddRange(value);
                Redraw();
            }
        }

        private void btnAdd_Click(object? sender, EventArgs e) => Add();

        private void btnRemove_Click(object? sender, EventArgs e) => RemoveSelected();

        private void btnSort_Click(object? sender, EventArgs e) => Sort();

        private void btnClear_Click(object? sender, EventArgs e) => Clear();

        private void Redraw()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (ushort id in _ids)
                _list.Items.Add($"{CardCatalog.NameOf(id)}  ({id})");
            _list.EndUpdate();
            _count.Text = Capacity > 0 ? $"{_ids.Count} / {Capacity}" : $"{_ids.Count} cards";
        }

        private void Add()
        {
            var database = CardCatalog.Get(this);
            using var picker = new CardPickerDialog(database, $"Add to {_titleText.TrimEnd(':')}", askCopies: AllowDuplicates, maxCopies: MaxCopies);
            if (picker.ShowDialog(this) != DialogResult.OK)
                return;

            foreach (var (card, copies) in picker.Result)
            {
                for (int i = 0; i < (AllowDuplicates ? copies : 1); i++)
                {
                    if (Capacity > 0 && _ids.Count >= Capacity)
                    {
                        MessageBox.Show(this, $"This list holds at most {Capacity} cards.", "Add cards", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Redraw();
                        Changed?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                    if (!AllowDuplicates && _ids.Contains((ushort)card.Id))
                        break;
                    _ids.Add((ushort)card.Id);
                }
            }
            Redraw();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void RemoveSelected()
        {
            foreach (int index in _list.SelectedIndices.Cast<int>().OrderByDescending(i => i))
                _ids.RemoveAt(index);
            Redraw();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Sort()
        {
            var sorted = _ids.OrderBy(id => CardCatalog.NameOf(id), StringComparer.OrdinalIgnoreCase).ThenBy(id => id).ToList();
            _ids.Clear();
            _ids.AddRange(sorted);
            Redraw();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Clear()
        {
            _ids.Clear();
            Redraw();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
