using System.ComponentModel;
using System.IO;
using savegame;

namespace WolfX.Types
{
    /// <summary>
    /// Edits a savegame.dat / savegame-ex.dat (44008 bytes): points, stats, owned cards, unlocked characters and the 32 decks.
    /// Saving fixes the checksum and bumps the save counter.
    /// </summary>
    public sealed partial class SaveEditorPage : UserControl
    {
        private sealed class CardRow
        {
            [DisplayName("Konami id")] public int Id { get; set; }
            [DisplayName("Card")] public string Name { get; set; } = "";
            [DisplayName("Copies (0-3)")] public int Copies { get; set; }
            [DisplayName("Seen (not NEW)")] public bool Seen { get; set; }
        }

        private sealed class StatRow
        {
            [DisplayName("#")] public int Index { get; set; }
            [DisplayName("Stat")] public string Name { get; set; } = "";
            [DisplayName("Value")] public ulong Value { get; set; }
        }

        private SaveFile? _save;
        private string? _path;

        private BindingList<CardRow> _cardRows = [];
        private BindingList<StatRow> _statRows = [];
        private bool _binding;
        private readonly Label _profile = new() { Dock = DockStyle.Top, Height = 48, Padding = new Padding(6), AutoEllipsis = true };

        public SaveEditorPage()
        {
            InitializeComponent();
            Controls.Add(_profile);
            top.BringToFront();
            _profile.BringToFront();
            _tabs.BringToFront();
            btnSave.Enabled = btnSaveAs.Enabled = btnComplete.Enabled = false;

            _characters.Items.AddRange(Enumerable.Range(0, PlayerData.CharacterCount).Select(i => (object)$"#{i}").ToArray());
        }

        // ---- events wired in the designer ----

        /// <summary>Open...: the saves found on this PC (Steam's, and the save slots in the game folder), or browse for one.</summary>
        private void btnOpen_Click(object? sender, EventArgs e)
        {
            var menu = new ContextMenuStrip();
            foreach (var (path, what) in FindSaves())
            {
                var info = new FileInfo(path);
                if (info.Length != SaveFile.Size) continue;
                string label = ProfileDescription(path).Split(". ")[0];
                menu.Items.Add($"{label}: {info.Name}  ({info.LastWriteTime:yyyy-MM-dd HH:mm}, {info.Length / 1024} KB)", null, (_, _) => Open(path)).ToolTipText = path;
            }
            if (menu.Items.Count > 0)
                menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Browse...", null, (_, _) => Open(null));
            menu.Show(btnOpen, new Point(0, btnOpen.Height));
        }

        /// <summary>
        /// The game's saves: Steam keeps them in &lt;Steam&gt;\userdata\&lt;account&gt;\1150640\remote (savegame.dat; Steam Cloud may put its own copy
        /// back while Steam runs), and Yu-Gi-Oh-Core's extra save slots are savegame-ex*.dat in the game folder.
        /// </summary>
        private static List<(string Path, string What)> FindSaves()
        {
            var found = new List<(string, string)>();
            try
            {
                if (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steam &&
                    Directory.Exists(Path.Combine(steam, "userdata")))
                {
                    foreach (string account in Directory.EnumerateDirectories(Path.Combine(steam, "userdata")))
                    {
                        string remote = Path.Combine(account, "1150640", "remote");
                        if (Directory.Exists(remote))
                            foreach (string file in Directory.EnumerateFiles(remote, "*.dat"))
                                found.Add((file, $"Steam account {Path.GetFileName(account)}"));
                    }
                }
                if (Wolf.Editors.GameFolderFiles.Current?.GameFolder is { } game && Directory.Exists(game))
                    foreach (string file in Directory.EnumerateFiles(game, "savegame*.dat"))
                        found.Add((file, "Game folder save"));
                if (Wolf.Editors.GameFolderFiles.Current?.GameFolder is { } folder)
                {
                    string primary = Path.GetFullPath(Path.Combine(folder, PrimarySaveName(folder)));
                    for (int slot = 1; slot <= 5; slot++)
                    {
                        string candidate = SaveProfileIdentity.SlotPath(primary, slot);
                        if (File.Exists(candidate) && !found.Any(f => Path.GetFullPath(f.Item1).Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                            found.Add((candidate, $"Toolkit slot {slot}"));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // nothing found is fine: Browse is still there
            }
            return found;
        }

        private void btnSave_Click(object? sender, EventArgs e) => Save(_path);

        private void btnSaveAs_Click(object? sender, EventArgs e) => SaveAs();

        private void _wallet_ValueChanged(object? sender, EventArgs e)
        {
            if (!_binding && _save != null)
                _save.Player.Wallet = (ulong)_wallet.Value;
        }

        private void _stats_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (_binding || _save == null || e.RowIndex < 0)
                return;

            var row = _statRows[e.RowIndex];
            _save.SetStat(row.Index, row.Value);
        }

        private void _cards_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (_binding || _save == null || e.RowIndex < 0)
                return;

            WriteCard(_cardRows[e.RowIndex]);
        }

        private void _cards_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
        {
            if (_cards.IsCurrentCellDirty)
                _cards.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void btnAddByName_Click(object? sender, EventArgs e) => AddByName();

        private void btnAddCard_Click(object? sender, EventArgs e) => AddCard();

        private void btnRemoveCards_Click(object? sender, EventArgs e) => RemoveSelectedCards();

        private void btnMarkSeen_Click(object? sender, EventArgs e) => MarkAllSeen();

        private void _characters_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (!_binding && _save != null)
                _save.Player.SetCharacterUnlocked(e.Index, e.NewValue == CheckState.Checked);
        }

        private void btnComplete_Click(object? sender, EventArgs e) => CompleteSave();

        private void _decks_SelectedIndexChanged(object? sender, EventArgs e) => BindDeck();

        private void btnApplyDeck_Click(object? sender, EventArgs e) => ApplyDeck();

        // ---- file ----

        private void Open(string? path)
        {
            if (path == null)
            {
                using var dialog = new OpenFileDialog { Filter = "Save files (*.dat)|*.dat|All files|*.*", Title = "Open savegame.dat or savegame-ex.dat" };
                if (FindSaves().FirstOrDefault().Path is { } first)
                    dialog.InitialDirectory = Path.GetDirectoryName(first);
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;
                path = dialog.FileName;
            }

            try
            {
                _save = SaveFile.Load(path);
                CardCatalog.Get(this); // names for the cards (asks for a folder once)
                _path = path;
                _pathBox.Text = _path;
                Bind();
                _tabs.Enabled = true;
                btnSave.Enabled = btnSaveAs.Enabled = btnComplete.Enabled = true;
                UpdateProfile();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open the save:\n{ex.Message}", "Save editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveAs()
        {
            using var dialog = new SaveFileDialog { Filter = "Save files (*.dat)|*.dat", FileName = Path.GetFileName(_path ?? "savegame-ex.dat") };
            if (_save != null && dialog.ShowDialog() == DialogResult.OK)
                Save(dialog.FileName);
        }

        private void Save(string? path)
        {
            if (_save == null || path == null)
                return;

            try
            {
                _stats.EndEdit();
                _cards.EndEdit();
                _save.Save(path);
                _path = path;
                _pathBox.Text = path;
                UpdateInfo();
                UpdateProfile();            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save:\n{ex.Message}", "Save editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---- binding ----

        private static string PrimarySaveName(string folder)
        {
            string name = ContentManifest.IniValue(folder, "Yu-Gi-Oh-Core", "GameSaveName", "savegame-ex.dat");
            return string.IsNullOrWhiteSpace(name) ? "savegame-ex.dat" : name;
        }

        private static string ProfileDescription(string path)
        {
            string? game = Wolf.Editors.GameFolderFiles.Current?.GameFolder;
            string? steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            return SaveProfileIdentity.Describe(path, game, game == null ? "savegame-ex.dat" : PrimarySaveName(game), steam);
        }

        private void UpdateProfile() { if (_path != null) _profile.Text = ProfileDescription(_path); }

        private void Bind()
        {
            if (_save == null)
                return;

            _binding = true;
            try
            {
                _wallet.Value = _save.Player.Wallet;

                _statRows = [];
                for (int i = 0; i < SaveFile.StatCount; i++)
                    _statRows.Add(new StatRow { Index = i, Name = Enum.IsDefined(typeof(SaveStat), i) ? ((SaveStat)i).ToString() : "(unused)", Value = _save.GetStat(i) });
                _stats.DataSource = _statRows;
                _stats.Columns[nameof(StatRow.Index)]!.ReadOnly = true;
                _stats.Columns[nameof(StatRow.Name)]!.ReadOnly = true;

                _cardRows = [];
                foreach (var (id, copies) in _save.Cards.Owned())
                    _cardRows.Add(new CardRow { Id = id, Name = CardCatalog.NameOf(id), Copies = copies, Seen = !_save.Cards.IsNew(id) });
                _cards.DataSource = _cardRows;
                _cards.Columns[nameof(CardRow.Id)]!.ReadOnly = true;
                _cards.Columns[nameof(CardRow.Name)]!.ReadOnly = true;

                for (int i = 0; i < PlayerData.CharacterCount; i++)
                    _characters.SetItemChecked(i, _save.Player.IsCharacterUnlocked(i));

                _decks.Items.Clear();
                for (int i = 0; i < _save.Decks.Length; i++)
                    _decks.Items.Add(DeckLabel(i));
            }
            finally { _binding = false; }

            UpdateInfo();
            if (_decks.Items.Count > 0)
                _decks.SelectedIndex = 0;
        }

        private string DeckLabel(int index)
        {
            var deck = _save!.Decks[index];
            return $"{index + 1,2}. {(deck.IsEmpty ? "(empty)" : deck.Name)}  [{deck.MainCount}/{deck.ExtraCount}/{deck.SideCount}]";
        }

        private void UpdateInfo()
        {
            if (_save == null)
                return;

            _info.Text = $"Header {(_save.HasValidHeader ? "OK" : "INVALID")}, checksum {(_save.ChecksumIsValid ? "valid" : "will be fixed on save")}, " +
                $"save counter {_save.SaveCounter}, {_save.Cards.Owned().Count()} card ids owned, " +
                $"{_save.Player.UnlockedCharacters().Count()} characters unlocked.";
        }

        // ---- cards ----

        private void WriteCard(CardRow row)
        {
            row.Copies = Math.Clamp(row.Copies, 0, CardTable.MaxCopies);
            _save!.Cards.SetCopies(row.Id, row.Copies);
            _save.Cards.SetNew(row.Id, !row.Seen);
        }

        private void AddCard()
        {
            if (_save == null)
                return;

            if (!int.TryParse(_addId.Text.Trim(), out int id) || id < 0 || id >= _save.Cards.Length)
            {
                MessageBox.Show($"A card id is a number from 0 to {_save.Cards.Length - 1}.", "Save editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var existing = _cardRows.FirstOrDefault(row => row.Id == id);
            _binding = true;
            try
            {
                if (existing == null)
                {
                    existing = new CardRow { Id = id, Name = CardCatalog.NameOf(id), Copies = (int)_addCopies.Value, Seen = true };
                    _cardRows.Add(existing);
                }
                else
                {
                    existing.Copies = (int)_addCopies.Value;
                    _cardRows.ResetBindings();
                }
                WriteCard(existing);
            }
            finally { _binding = false; }
            UpdateInfo();
        }

        private void AddByName()
        {
            if (_save == null)
                return;

            using var picker = new CardPickerDialog(CardCatalog.Get(this), "Add cards to the save");
            if (picker.ShowDialog(this) != DialogResult.OK)
                return;

            _binding = true;
            try
            {
                foreach (var (card, copies) in picker.Result)
                {
                    if (card.Id >= _save.Cards.Length)
                        continue;

                    var row = _cardRows.FirstOrDefault(existing => existing.Id == card.Id);
                    if (row == null)
                    {
                        row = new CardRow { Id = card.Id, Name = card.Name, Seen = true };
                        _cardRows.Add(row);
                    }
                    row.Copies = Math.Min(copies, CardTable.MaxCopies);
                    WriteCard(row);
                }
                _cardRows.ResetBindings();
            }
            finally { _binding = false; }
            UpdateInfo();
        }

        private void RemoveSelectedCards()
        {
            if (_save == null)
                return;

            var rows = _cards.SelectedRows.Cast<DataGridViewRow>().Select(row => (CardRow)row.DataBoundItem).ToList();
            _binding = true;
            try
            {
                foreach (var row in rows)
                {
                    _save.Cards.SetRaw(row.Id, 0);
                    _cardRows.Remove(row);
                }
            }
            finally { _binding = false; }
            UpdateInfo();
        }

        private void MarkAllSeen()
        {
            if (_save == null)
                return;

            _binding = true;
            try
            {
                foreach (var row in _cardRows)
                {
                    row.Seen = true;
                    WriteCard(row);
                }
                _cardRows.ResetBindings();
            }
            finally { _binding = false; }
        }

        // ---- complete save ----

        /// <summary>DP a completed save is given.</summary>
        private const ulong CompleteWallet = 999_999;

        /// <summary>
        /// Everything a fully played profile has, from the open game data plus Yu-Gi-Oh-Ex's JSON (new cards, characters, decks, duels):
        /// 3 seen copies of every card, every character and deck, every pack in the shop, the three locked main menu modes, every
        /// campaign duel (normal and reverse) and series cleared, and 999,999 DP. Apart from the DP it only sets things, so no earned progress is lost.
        /// The same fields DuelResult_GrantRewards (0x140880240) writes as the campaign is won.
        /// </summary>
        private void CompleteSave()
        {
            if (_save == null)
                return;

            var files = Wolf.Editors.GameFolderFiles.Current;
            if (files?.Available != true)
            {
                MessageBox.Show("Open the game folder first.", "Complete save", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show("Unlock everything?", "Complete save", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            var problems = new List<string>();
            var player = _save.Player;

            CardCatalog.Reload();   // cards.json may have new cards since the save was opened
            foreach (var card in CardCatalog.Get(this).Cards)
            {
                if (card.Id < 0 || card.Id >= _save.Cards.Length)
                    continue;
                _save.Cards.SetCopies(card.Id, CardTable.MaxCopies);
                _save.Cards.SetNew(card.Id, false);
            }

            try
            {
                var table = global::Types.CharacterTable.Parse(ReadLanguages(files, global::Types.CharacterTable.AllLanguages, global::Types.CharacterTable.GamePath));
                global::Types.CharacterJson.Apply(global::Types.CharacterJson.Load(files.ExPath(global::Types.CharacterJson.FileName)), table);
                foreach (var character in table.Characters.Where(c => c.Id >= 0 && c.Id < PlayerData.CharacterCount))
                    player.SetCharacterUnlocked(character.Id, true);
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException)
            {
                problems.Add("characters: " + ex.Message);
            }

            try
            {
                if (files.Read(DeckData.DeckDataTable.GamePath('E')) is not byte[] deckData)
                    throw new InvalidDataException($"{DeckData.DeckDataTable.FileName('E')} was not found.");
                var deckFile = DeckData.DeckDataFile.Parse(deckData);
                DeckData.DeckJson.ApplyTo(deckFile, DeckData.DeckJson.Load(files.ExPath(DeckData.DeckJson.FileName)), 'E');
                foreach (var deck in deckFile.Records.Where(d => d.Id < PlayerData.DeckCount))
                {
                    // state 0 = locked; the game raises it to 1 (MarkDeckStateKnown) and sets the owned bit when it grants a deck
                    if (player.GetDeckState((int)deck.Id) == 0)
                        player.SetDeckState((int)deck.Id, 1);
                    player.SetDeckOwned((int)deck.Id, true);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException or ArgumentException)
            {
                problems.Add("decks: " + ex.Message);
            }

            try
            {
                var table = global::Types.StoryDuelTable.Parse(ReadLanguages(files, global::Types.StoryDuelTable.AllLanguages, global::Types.StoryDuelTable.GamePath));
                global::Types.StoryDuelJson.Apply(global::Types.StoryDuelJson.Load(files.ExPath(global::Types.StoryDuelJson.FileName)), table);
                // the save has a block for the game's 6 series only; Yu-Gi-Oh-Campaign's new series keep their progress elsewhere
                foreach (var duel in table.Duels.Where(d => d.Series >= 0 && d.Series < ProfileSection.BlockCount && d.Order >= 1 && d.Order <= global::Types.StoryDuelTable.MaxOrder))
                {
                    _save.Section.SetDuelState(duel.Series, duel.Order, reverse: false, DuelState.Cleared);
                    if (duel.Order != 1)   // order 1 is the series' tutorial and has no reverse duel
                        _save.Section.SetDuelState(duel.Series, duel.Order, reverse: true, DuelState.Cleared);
                    _save.Section.SetSeriesState(duel.Series, DuelState.Cleared);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException)
            {
                problems.Add("campaign: " + ex.Message);
            }

            // every pack id the shop can have (Yu-Gi-Oh-Core's new packs use the free ids, so all 128); unused ids are never listed
            for (int pack = 0; pack < PlayerData.FlagCount; pack++)
                player.SetPackUnlocked(pack, true);
            player.ModeBits |= PlayerData.AllModes;

            player.Wallet = CompleteWallet;

            Bind();
            if (problems.Count > 0)
                MessageBox.Show(string.Join("\n", problems), "Complete save", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static Dictionary<char, byte[]> ReadLanguages(Wolf.Editors.GameFolderFiles files, char[] languages, Func<char, string> path)
        {
            var found = new Dictionary<char, byte[]>();
            foreach (char language in languages)
                if (files.Read(path(language)) is byte[] bytes)
                    found[language] = bytes;
            if (found.Count == 0)
                throw new InvalidDataException($"{path('E')} was not found.");
            return found;
        }

        // ---- decks ----

        private void BindDeck()
        {
            if (_save == null || _decks.SelectedIndex < 0)
                return;

            var deck = _save.Decks[_decks.SelectedIndex];
            _binding = true;
            try
            {
                _deckName.Text = deck.Name;
                _deckCharacter.Value = Math.Min(deck.Character, 239u);
                _deckMain.Ids = deck.Main;
                _deckExtra.Ids = deck.Extra;
                _deckSide.Ids = deck.Side;
            }
            finally { _binding = false; }
        }

        private void ApplyDeck()
        {
            if (_save == null || _decks.SelectedIndex < 0)
                return;

            var deck = _save.Decks[_decks.SelectedIndex];
            try
            {
                deck.Name = _deckName.Text;
                deck.Character = (uint)_deckCharacter.Value;
                deck.SetCards(_deckMain.Ids, _deckExtra.Ids, _deckSide.Ids);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message, "Save editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _binding = true;
            try { _decks.Items[_decks.SelectedIndex] = DeckLabel(_decks.SelectedIndex); }
            finally { _binding = false; }
        }
    }
}
