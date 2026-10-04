using System.Buffers.Binary;
using System.Text;
using DeckData;
using PackDef;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// A game id picked by name: a dropdown of "id: name" (characters, decks, packs, arenas, content packs...) instead of a bare number box.
    /// Typing filters the list (suggest / append); a typed number that isn't in the list is kept too, shown as "id: (not in the list)".
    /// </summary>
    public sealed class IdCombo : ComboBox
    {
        private sealed record Item(int Id, string Text)
        {
            public override string ToString() => Text;
        }

        private int _value;
        private bool _setting;

        /// <summary>The id changed (the user picked or typed one; setting <see cref="Value"/> from code doesn't raise it).</summary>
        public event EventHandler? ValueChanged;

        public IdCombo(int width = 260)
        {
            Width = width;
            DropDownStyle = ComboBoxStyle.DropDown;
            AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            AutoCompleteSource = AutoCompleteSource.ListItems;
            MaxDropDownItems = 20;
            SelectedIndexChanged += (_, _) =>
            {
                if (!_setting && SelectedItem is Item item)
                    Take(item.Id);
            };
            Validating += (_, _) => TakeTyped();
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    TakeTyped();
                    e.SuppressKeyPress = true;
                }
            };
        }

        /// <summary>The ids and names to list, in this order.</summary>
        public void SetItems(IEnumerable<(int Id, string Name)> items)
        {
            _setting = true;
            try
            {
                BeginUpdate();
                Items.Clear();
                foreach (var (id, name) in items)
                    Items.Add(new Item(id, $"{id}: {name}"));
                EndUpdate();
                Show(_value);
            }
            finally
            {
                _setting = false;
            }
        }

        public int Value
        {
            get => _value;
            set
            {
                _setting = true;
                try
                {
                    _value = value;
                    Show(value);
                }
                finally
                {
                    _setting = false;
                }
            }
        }

        private void Show(int id)
        {
            int index = -1;
            for (int i = 0; i < Items.Count && index < 0; i++)
                if (Items[i] is Item item && item.Id == id)
                    index = i;
            SelectedIndex = index;
            if (index < 0)
                Text = $"{id}: (not in the list)";
        }

        private void Take(int id)
        {
            if (id == _value)
                return;
            _value = id;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The typed text: a listed name (or the start of one), else the number it starts with.</summary>
        private void TakeTyped()
        {
            if (_setting)
                return;
            string text = Text.Trim();
            var match = Items.OfType<Item>().FirstOrDefault(i => i.Text.Equals(text, StringComparison.OrdinalIgnoreCase))
                        ?? Items.OfType<Item>().FirstOrDefault(i => i.Text.Contains(text, StringComparison.OrdinalIgnoreCase) && text.Length > 0 && !char.IsDigit(text[0]));
            if (match != null)
            {
                Take(match.Id);
                Value = match.Id;
                return;
            }
            string digits = new([.. text.TakeWhile((c, i) => char.IsDigit(c) || (i == 0 && c == '-'))]);
            if (int.TryParse(digits, out int id))
                Take(id);
            Value = _value;   // show it the way the list does
        }
    }

    /// <summary>The names <see cref="IdCombo"/> lists, read from the game's own files (and the Yu-Gi-Oh-Ex JSON on top where an editor has it).</summary>
    public static class GameNames
    {
        public static IEnumerable<(int, string)> Characters(CharacterTable? table, char language) =>
            table?.Characters.OrderBy(c => c.Id).Select(c => (c.Id, c.Name(language) is { Length: > 0 } name ? name : c.Key)) ?? [];

        public static IEnumerable<(int, string)> Decks(DeckDataFile? decks) =>
            decks?.Records.OrderBy(d => d.Id).Select(d => ((int)d.Id, d.Title.Length > 0 ? d.Title : d.FileName)) ?? [];

        public static IEnumerable<(int, string)> Packs(IGameFiles? files, string none = "none")
        {
            var list = new List<(int, string)> { (-1, none) };
            if (files?.Read(@"main\packdefdata_E.bin") is byte[] bytes)
            {
                try
                {
                    list.AddRange(PackDefFile.Parse(bytes).Records.OrderBy(p => p.Id)
                        .Select(p => ((int)p.Id, (p.Title.Length > 0 ? p.Title : p.Name) + (p.IsReward ? "" : " (battle pack)"))));
                }
                catch (InvalidDataException)
                {
                }
            }
            return list;
        }

        // The game's arenas if arenadata_E.bin can't be read (ids from the exe's g_ArenaFolderTable, see docs: arenadata)
        private static readonly (int, string)[] KnownArenas =
        [
            (1, "Classic"), (2, "5D's"), (3, "ZEXAL"), (4, "GX"), (5, "ARC-V"), (6, "Blimp"), (7, "Urban"), (8, "Barian"),
            (13, "Fusion Dimension"), (14, "Synchro Dimension"), (15, "Xyz Dimension"), (16, "You Show Duel School"), (17, "VRAINS"), (18, "VRAINS 2019"),
        ];

        /// <summary>main/arenadata_E.bin: u64 count, packed 28 byte records {i32 id, u64 key (ASCII), u64 name (UTF-16), u64 description}.</summary>
        public static IEnumerable<(int, string)> Arenas(IGameFiles? files, string? none = "default")
        {
            var list = new List<(int, string)>();
            if (none != null)
                list.Add((-1, none));
            var read = new List<(int, string)>();
            try
            {
                if (files?.Read(@"main\arenadata_E.bin") is byte[] data && data.Length >= 8)
                {
                    long count = BinaryPrimitives.ReadInt64LittleEndian(data);
                    for (long i = 0; i < count && 8 + (i + 1) * 28 <= data.Length; i++)
                    {
                        var record = data.AsSpan((int)(8 + i * 28), 28);
                        int id = BinaryPrimitives.ReadInt32LittleEndian(record);
                        string key = Ascii(data, BinaryPrimitives.ReadInt64LittleEndian(record[4..]));
                        string name = Utf16(data, BinaryPrimitives.ReadInt64LittleEndian(record[12..]));
                        read.Add((id, name.Length > 0 ? name : key));
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                read.Clear();
            }
            list.AddRange(read.Count > 0 ? read : KnownArenas);
            return list;
        }

        /// <summary>main/skudata_E.bin: u64 count, records {i32 id, i32 ?, u64 key (ASCII), u64 name (UTF-16)}; -1 in a file = 1, the base game.</summary>
        public static IEnumerable<(int, string)> ContentPacks(IGameFiles? files)
        {
            var list = new List<(int, string)> { (-1, "base game (written as -1)") };
            try
            {
                if (files?.Read(@"main\skudata_E.bin") is byte[] data && data.Length >= 8)
                {
                    long count = BinaryPrimitives.ReadInt64LittleEndian(data);
                    for (long i = 0; i < count && 8 + (i + 1) * 24 <= data.Length; i++)
                    {
                        var record = data.AsSpan((int)(8 + i * 24), 24);
                        int id = BinaryPrimitives.ReadInt32LittleEndian(record);
                        string key = Ascii(data, BinaryPrimitives.ReadInt64LittleEndian(record[8..]));
                        string name = Utf16(data, BinaryPrimitives.ReadInt64LittleEndian(record[16..]));
                        list.Add((id, (name.Length > 0 ? name : key) + (id == 1 ? " (base game)" : "")));
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            if (!list.Any(x => x.Item1 == 1))
                list.Add((1, "LAUNCH (base game)"));
            return list;
        }

        private static string Ascii(byte[] data, long offset)
        {
            if (offset <= 0 || offset >= data.Length)
                return "";
            int end = Array.IndexOf(data, (byte)0, (int)offset);
            return Encoding.ASCII.GetString(data, (int)offset, (end < 0 ? data.Length : end) - (int)offset);
        }

        private static string Utf16(byte[] data, long offset)
        {
            if (offset <= 0 || offset >= data.Length)
                return "";
            int end = (int)offset;
            while (end + 1 < data.Length && (data[end] != 0 || data[end + 1] != 0))
                end += 2;
            return Encoding.Unicode.GetString(data, (int)offset, end - (int)offset);
        }
    }
}
