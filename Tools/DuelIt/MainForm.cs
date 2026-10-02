namespace DuelIt
{
    /// <summary>
    /// Replays duels recorded in Duels.log (written by the Yu-Gi-Oh-MP plugin's DuelRecorder next to the game's console.log).
    /// Pick a duel, then step or play through it: the board shows LP, turn, phase and where every card is.
    /// </summary>
    public partial class MainForm : Form
    {
        private static readonly string DefaultGameFolder = @"C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution";
        // The game folder is shared with WolfX, which remembers it here.
        private static readonly string GameFolderSetting = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WolfX", "gamefolder.txt");
        private static readonly string UncensoredSetting = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WolfX", "duelit-uncensored.txt");

        private readonly string? _startPath;
        private string? _logPath;
        private string _gameFolder = DefaultGameFolder;
        private List<DuelRecord> _duels = [];
        private DuelRecord? _duel;
        private List<DuelState> _states = [];   // _states[i] = the duel after event i
        private int _step = -1;
        private GameData _data = GameData.Empty;
        private CardPopup? _popup;
        private int _hoverRow = -1;

        private static readonly HashSet<string> KeyTags = ["MOVE", "LP", "CHAIN", "START", "DUEL_BEGIN", "DUEL_END", "MATCH_END", "ROUND"];
        private static readonly HashSet<int> KeyMessages = [0x01, 0x02, 0x05, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x22, 0x2E, 0x2F, 0x50, 0x56];

        public MainForm(string? startPath = null)
        {
            _startPath = startPath;
            InitializeComponent();
            speedCombo.SelectedIndex = 1;
            board.HoverChanged += (target, screen) => _popup?.ShowFor(target, screen);
            eventList.MouseMove += EventList_MouseMove;
            eventList.MouseLeave += (_, _) => { _hoverRow = -1; _popup?.ShowFor(null, Point.Empty); };
            Deactivate += (_, _) => _popup?.ShowFor(null, Point.Empty);
        }

        private void MainForm_Load(object? sender, EventArgs e)
        {
            try
            {
                if (File.Exists(GameFolderSetting) && Directory.Exists(File.ReadAllText(GameFolderSetting).Trim()))
                    _gameFolder = File.ReadAllText(GameFolderSetting).Trim();
            }
            catch (IOException) { }
            try
            {
                uncensoredMenuItem.Checked = File.Exists(UncensoredSetting) && File.ReadAllText(UncensoredSetting).Trim() == "1";
            }
            catch (IOException) { }

            LoadGameData();
            string path = _startPath ?? Path.Combine(_gameFolder, "Duels.log");
            if (File.Exists(path))
                OpenLog(path);
            else
                statusLabel.Text = $"No duel log at {path} yet. Play a duel with Yu-Gi-Oh-MP loaded, or File > Open.";
        }

        /// <summary>Card names, stats, text and art from the game folder (in the background: the art index is a few MB of reading).</summary>
        private void LoadGameData()
        {
            string folder = _gameFolder;
            statusLabel.Text = "Loading cards and art...";
            Task.Run(() =>
            {
                try { return GameData.Load(folder); }
                catch (Exception) { return null; }
            }).ContinueWith(task =>
            {
                if (task.Result is { } data)
                {
                    _data = data;
                    _data.Uncensored = uncensoredMenuItem.Checked;
                    DuelText.CardNames = data.NameOf;
                    statusLabel.Text = data.Warnings.Count > 0 ? string.Join(" ", data.Warnings) : $"Cards and art loaded from {folder}";
                }
                else
                {
                    _data = GameData.Empty;
                    statusLabel.Text = $"Couldn't read the cards from {folder}; cards show as #id. File > Game folder to change it.";
                }
                board.Data = _data;
                _popup?.Dispose();
                _popup = new CardPopup(_data);
                eventList.Invalidate();
                board.Invalidate();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>The card an event is about, for the hover card: the card that moved, or the card an activation/effect message names.</summary>
        private HoverTarget? EventCard(int index)
        {
            if (_duel == null || index < 0 || index >= _duel.Events.Count)
                return null;
            var e = _duel.Events[index];
            var state = _states[index];
            if (e.Tag == "MOVE")
            {
                var (fromZone, _, _) = Zones.Decode(e.Int("from"));
                var (toZone, toSide, _) = Zones.Decode(e.Int("to"));
                return new HoverTarget(state.CardId(e.Int("slot")), $"{DuelText.Player(toSide, _duel.LocalSide)}: {Zones.Name(fromZone)} -> {Zones.Name(toZone)}");
            }
            if (e.Tag == "MSG" && e.Int("code") is 0x2E or 0x2F && e.Int("a1") > 0)
                return new HoverTarget(e.Int("a1"), $"{e.Get("name")}, {DuelText.Player(e.Int("side"), _duel.LocalSide)}");
            return null;
        }

        private void EventList_MouseMove(object? sender, MouseEventArgs e)
        {
            int row = eventList.GetItemAt(e.X, e.Y)?.Index ?? -1;
            if (row == _hoverRow)
            {
                if (row >= 0 && _popup is { Visible: true })
                    _popup.ShowFor(EventCard(row), eventList.PointToScreen(e.Location));
                return;
            }
            _hoverRow = row;
            _popup?.ShowFor(EventCard(row), eventList.PointToScreen(e.Location));
        }

        private void UncensoredMenuItem_CheckedChanged(object? sender, EventArgs e)
        {
            _data.Uncensored = uncensoredMenuItem.Checked;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(UncensoredSetting)!);
                File.WriteAllText(UncensoredSetting, uncensoredMenuItem.Checked ? "1" : "0");
            }
            catch (IOException) { }
            board.Invalidate();
            _popup?.Invalidate();
        }

        private void OpenLog(string path)
        {
            try
            {
                _duels = DuelLog.Load(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Couldn't read {path}:\n{ex.Message}", "DuelIt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _logPath = path;
            Text = $"DuelIt - {path}";
            duelCombo.Items.Clear();
            foreach (var duel in _duels)
                duelCombo.Items.Add(duel.Summary);
            if (_duels.Count > 0)
                duelCombo.SelectedIndex = _duels.Count - 1;   // newest
            else
            {
                ShowDuel(null);
                statusLabel.Text = $"{path} has no duels in it yet.";
            }
        }

        private void ShowDuel(DuelRecord? duel)
        {
            Stop();
            _duel = duel;
            _states = [];
            if (duel != null)
            {
                var state = new DuelState();
                foreach (var e in duel.Events)
                {
                    state.Apply(e);
                    _states.Add(state);
                    state = state.Clone();
                }
            }
            eventList.VirtualListSize = duel?.Events.Count ?? 0;
            eventList.Invalidate();
            _step = -1;
            GoTo(duel == null ? -1 : 0);

            if (duel != null)
            {
                int moves = duel.Events.Count(ev => ev.Tag == "MOVE");
                int net = duel.Events.Count(ev => ev.Tag.StartsWith("NET_"));
                statusLabel.Text = $"{duel.Id}: {duel.Events.Count} events, {moves} card moves" + (duel.Online ? $", {net} network events" : ", single player") +
                    (_states.Count > 0 && _states[^1].Mismatches > 0 ? $". {_states[^1].Mismatches} moves didn't match the tracked position (a gap in what's recorded)." : ".");
            }
        }

        private void GoTo(int index)
        {
            if (_duel == null || _duel.Events.Count == 0)
            {
                board.State = null;
                stepLabel.Text = "";
                return;
            }
            index = Math.Clamp(index, 0, _duel.Events.Count - 1);
            _step = index;
            board.State = _states[index];
            stepLabel.Text = $"{index + 1} / {_duel.Events.Count}";

            if (eventList.SelectedIndices.Count != 1 || eventList.SelectedIndices[0] != index)
            {
                eventList.SelectedIndices.Clear();
                eventList.SelectedIndices.Add(index);
            }
            eventList.EnsureVisible(index);
        }

        private bool IsKeyEvent(DuelEvent e) => KeyTags.Contains(e.Tag) || (e.Tag == "MSG" && KeyMessages.Contains(e.Int("code")));

        private void Step(int direction)
        {
            if (_duel == null)
                return;
            int index = _step;
            do
            {
                index += direction;
            } while (keyEventsButton.Checked && index >= 0 && index < _duel.Events.Count && !IsKeyEvent(_duel.Events[index]));

            if (index < 0 || index >= _duel.Events.Count)
            {
                Stop();
                return;
            }
            GoTo(index);
        }

        private void Stop()
        {
            playTimer.Stop();
            playButton.Text = "Play";
        }

        private void EventList_RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
        {
            var ev = _duel!.Events[e.ItemIndex];
            var item = new ListViewItem((e.ItemIndex + 1).ToString());
            item.SubItems.Add(ev.Time);
            item.SubItems.Add(ev.Tag == "MSG" ? ev.Get("name") : ev.Tag);
            item.SubItems.Add(DuelText.Describe(ev, _states[e.ItemIndex], _duel.LocalSide));
            if (!IsKeyEvent(ev))
                item.ForeColor = SystemColors.GrayText;
            else if (ev.Tag is "DUEL_BEGIN" or "DUEL_END")
                item.Font = new Font(eventList.Font, FontStyle.Bold);
            e.Item = item;
        }

        private void EventList_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (eventList.SelectedIndices.Count == 1 && eventList.SelectedIndices[0] != _step)
                GoTo(eventList.SelectedIndices[0]);
        }

        private void DuelCombo_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (duelCombo.SelectedIndex >= 0 && duelCombo.SelectedIndex < _duels.Count)
                ShowDuel(_duels[duelCombo.SelectedIndex]);
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (duelCombo.Focused)
                return;
            switch (e.KeyCode)
            {
                case Keys.Right: Step(1); break;
                case Keys.Left: Step(-1); break;
                case Keys.Home: GoTo(0); break;
                case Keys.End: GoTo(int.MaxValue); break;
                case Keys.Space: PlayButton_Click(sender, e); break;
                default: return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void PlayTimer_Tick(object? sender, EventArgs e) => Step(1);

        private void PlayButton_Click(object? sender, EventArgs e)
        {
            if (playTimer.Enabled)
                Stop();
            else if (_duel != null)
            {
                if (_step >= _duel.Events.Count - 1)
                    GoTo(0);
                playTimer.Start();
                playButton.Text = "Pause";
            }
        }

        private void SpeedCombo_SelectedIndexChanged(object? sender, EventArgs e)
        {
            double speed = speedCombo.SelectedIndex switch { 0 => 0.5, 1 => 1, 2 => 2, 3 => 4, _ => 10 };
            playTimer.Interval = Math.Max(15, (int)(600 / speed));
        }

        private void FirstButton_Click(object? sender, EventArgs e) => GoTo(0);
        private void PreviousButton_Click(object? sender, EventArgs e) => Step(-1);
        private void NextButton_Click(object? sender, EventArgs e) => Step(1);
        private void LastButton_Click(object? sender, EventArgs e) => GoTo(int.MaxValue);

        private void OpenMenuItem_Click(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Open a duel log",
                Filter = "Duel logs (*.log)|*.log|All files (*.*)|*.*",
                InitialDirectory = _logPath != null ? Path.GetDirectoryName(_logPath) : _gameFolder,
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                OpenLog(dialog.FileName);
        }

        private void ReloadMenuItem_Click(object? sender, EventArgs e)
        {
            if (_logPath == null)
                return;
            int selected = duelCombo.SelectedIndex;
            bool wasNewest = selected == _duels.Count - 1;
            OpenLog(_logPath);
            if (!wasNewest && selected >= 0 && selected < _duels.Count)
                duelCombo.SelectedIndex = selected;
        }

        private void GameFolderMenuItem_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog { Description = "The game folder (for card names and Duels.log)", SelectedPath = _gameFolder };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            _gameFolder = dialog.SelectedPath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(GameFolderSetting)!);
                File.WriteAllText(GameFolderSetting, _gameFolder);
            }
            catch (IOException) { }
            LoadGameData();
            string log = Path.Combine(_gameFolder, "Duels.log");
            if (_logPath == null && File.Exists(log))
                OpenLog(log);
        }

        private void ExitMenuItem_Click(object? sender, EventArgs e) => Close();
    }
}
