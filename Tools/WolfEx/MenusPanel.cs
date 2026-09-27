using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WolfEx
{
    /// <summary>
    /// Yu-Gi-Oh-Ex/menus/menus.json: buttons added to the main menu and the options menu, and changes to the game's own main menu buttons.
    /// The Yu-Gi-Oh-RIX plugin reads every JSON file in that folder when the game starts (see docs/MenuFiles.md); this page edits menus.json.
    /// </summary>
    internal sealed partial class MenusPanel : UserControl, IContentPanel
    {
        private static readonly string[] Screens =
        [
            "title", "signIn", "commonBg", "mainMenu", "loading", "gameBegin", "exGameDuel", "helpAndOptions", "settings", "videoSettings", "credits",
            "controllerSettings", "howToPlay", "statistics", "voices", "pauseMenu", "duelistChallenge", "campaignDialog", "campaignSelectDeck",
            "tutorialList", "deckEditor", "swapCards", "matchResult", "gameResult", "cardShop", "battlePack", "battlePackDraft", "battlePackEdit",
            "playerMatch", "liveSetting", "liveSession", "liveLobby", "liveLoading", "leaderboard", "inviteLanding", "safetyZone", "duelSelect",
            "selectRung", "scoreReview",
        ];

        private static readonly string[] MainItems =
        [
            "singlePlayerMenu", "soloDuel", "duelistChallenge", "multiplayerMenu", "multiplayerA", "multiplayerB", "leaderboard",
            "battlePack", "deckEditor", "cardShop", "helpAndOptions", "tutorials", "quitGame",
        ];

        private static readonly string[] OptionItems = ["howToPlay", "controllerSettings", "settings", "videoSettings", "credits"];

        private static readonly string[] ActionKinds = ["nothing", "goto", "press", "call", "quit"];

        private sealed class ButtonModel
        {
            public string Key = "";
            public string Menu = "main";
            public string Page = "main";
            public string Label = "New button";
            public string Description = "";
            public string Look = "deckEditor";
            public int ActionKind;            // index into ActionKinds
            public string ActionArg = "";
            public JsonNode? RawAction;       // an action the editor can't show (several steps): kept as it is

            public override string ToString() => $"{(Menu == "options" ? "[options] " : "")}{Label}";
        }

        private sealed class GameRow
        {
            public string Item { get; set; } = "";
            public string Label { get; set; } = "";
            public string Description { get; set; } = "";
            public bool Hidden { get; set; }
        }

        private readonly List<ButtonModel> _buttons = [];
        private readonly BindingList<GameRow> _gameRows = [];
        private bool _binding;

        public string Title => "Menus";

        public MenusPanel()
        {
            InitializeComponent();

            foreach (string item in MainItems)
                _gameRows.Add(new GameRow { Item = item });
            _game.DataSource = _gameRows;

            _gameInfo.Text = "Rename or hide the game's own main menu buttons. Leave a text empty to keep the game's. Changes apply when the game starts.";
            _help.Text = "Look borrows the picture of one of the game's buttons. goto opens a screen; press presses one of the game's main menu buttons; " +
                "call runs an action a plugin registered (for example funky.toggleTools).";
            SetEditorEnabled(false);
        }

        private ButtonModel? Selected => _list.SelectedIndex >= 0 ? _buttons[_list.SelectedIndex] : null;

        // ---- events wired in the designer ----

        private void List_SelectedIndexChanged(object? sender, EventArgs e) => BindSelected();

        private void Editor_Changed(object? sender, EventArgs e)
        {
            if (sender == _menu || sender == _actionType)
                RefreshChoices();
            Commit();
        }

        private void btnAdd_Click(object? sender, EventArgs e)
        {
            _buttons.Add(new ButtonModel { Key = $"button{_buttons.Count + 1}" });
            _list.Items.Add(_buttons[^1]);
            _list.SelectedIndex = _list.Items.Count - 1;
        }

        private void btnRemove_Click(object? sender, EventArgs e)
        {
            int index = _list.SelectedIndex;
            if (index < 0)
                return;

            _buttons.RemoveAt(index);
            _list.Items.RemoveAt(index);
            if (_list.Items.Count > 0)
                _list.SelectedIndex = Math.Min(index, _list.Items.Count - 1);
            else
                BindSelected();
        }

        private void btnUp_Click(object? sender, EventArgs e) => Move(-1);

        private void btnDown_Click(object? sender, EventArgs e) => Move(1);

        // ---- editor <-> model ----

        private void Move(int direction)
        {
            int from = _list.SelectedIndex, to = from + direction;
            if (from < 0 || to < 0 || to >= _buttons.Count)
                return;

            (_buttons[from], _buttons[to]) = (_buttons[to], _buttons[from]);
            _binding = true;
            try
            {
                _list.Items[from] = _buttons[from];
                _list.Items[to] = _buttons[to];
                _list.SelectedIndex = to;
            }
            finally { _binding = false; }
        }

        private void SetEditorEnabled(bool enabled)
        {
            foreach (Control control in new Control[] { _key, _menu, _page, _label, _description, _look, _actionType, _actionArg })
                control.Enabled = enabled;
        }

        /// <summary>The look list follows the menu, the argument list follows the kind of action.</summary>
        private void RefreshChoices()
        {
            bool wasBinding = _binding;
            _binding = true;
            try
            {
                string[] looks = _menu.Text == "options" ? OptionItems : MainItems;
                string look = _look.Text;
                _look.Items.Clear();
                _look.Items.AddRange(looks);
                _look.Text = looks.Contains(look) ? look : looks[Math.Min(looks.Length - 1, 2)];
                _page.Enabled = _menu.Text != "options" && _menu.Enabled;

                string[] choices = _actionType.SelectedIndex switch { 1 => Screens, 2 => MainItems, _ => [] };
                _actionArg.Items.Clear();
                _actionArg.Items.AddRange(choices);
                _actionArg.Enabled = _actionType.SelectedIndex is > 0 and < 4 && _actionType.Enabled;
            }
            finally { _binding = wasBinding; }
        }

        private void BindSelected()
        {
            if (_binding)
                return;

            var button = Selected;
            SetEditorEnabled(button != null);
            if (button == null)
                return;

            _binding = true;
            try
            {
                _key.Text = button.Key;
                _menu.Text = button.Menu;
                _page.Text = button.Page;
                _label.Text = button.Label;
                _description.Text = button.Description;
                _actionType.SelectedIndex = button.ActionKind;
            }
            finally { _binding = false; }

            RefreshChoices();

            _binding = true;
            try
            {
                _look.Text = button.Look;
                _actionArg.Text = button.ActionArg;
                if (button.RawAction != null)
                {
                    _actionType.Enabled = _actionArg.Enabled = false;
                    _help.Text = "This button runs several steps; they can only be edited in the file and are kept as they are.";
                }
            }
            finally { _binding = false; }
        }

        private void Commit()
        {
            var button = Selected;
            if (_binding || button == null)
                return;

            button.Key = _key.Text;
            button.Menu = _menu.Text;
            button.Page = _page.Text;
            button.Label = _label.Text;
            button.Description = _description.Text;
            button.Look = _look.Text;
            if (button.RawAction == null)
            {
                button.ActionKind = Math.Max(0, _actionType.SelectedIndex);
                button.ActionArg = _actionArg.Text;
            }

            _binding = true;
            try { _list.Items[_list.SelectedIndex] = button; }
            finally { _binding = false; }
        }

        // ---- IContentPanel ----

        private static string FilePath(string extraCardsFolder) => Path.Combine(extraCardsFolder, "menus", "menus.json");

        public void LoadFrom(string extraCardsFolder, string gameFolder)
        {
            _buttons.Clear();
            _list.Items.Clear();
            foreach (var row in _gameRows)
                (row.Label, row.Description, row.Hidden) = ("", "", false);

            string path = FilePath(extraCardsFolder);
            if (File.Exists(path))
            {
                try
                {
                    var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                        ?? throw new InvalidDataException("expected an object with \"buttons\" and \"edit\"");

                    foreach (var node in (root["buttons"] as JsonArray)?.OfType<JsonObject>() ?? [])
                        _buttons.Add(ReadButton(node));

                    foreach (var node in (root["edit"] as JsonArray)?.OfType<JsonObject>() ?? [])
                    {
                        var row = _gameRows.FirstOrDefault(r => string.Equals(r.Item, Text(node, "item"), StringComparison.OrdinalIgnoreCase));
                        if (row == null)
                            continue;
                        row.Label = Text(node, "label");
                        row.Description = Text(node, "description");
                        row.Hidden = node["hidden"] is JsonValue hidden && hidden.TryGetValue<bool>(out var flag) && flag;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not read {path}:\n{ex.Message}", "Menus", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _buttons.Clear();
                }
            }

            foreach (var button in _buttons)
                _list.Items.Add(button);
            _game.Refresh();

            if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;
            else
                BindSelected();
        }

        public bool SaveTo(string extraCardsFolder)
        {
            _game.EndEdit();

            var problems = new List<string>();
            foreach (var button in _buttons)
            {
                if (string.IsNullOrWhiteSpace(button.Label))
                    problems.Add($"\"{button.Key}\": the label is empty.");
                if (button.ActionKind is > 0 and < 4 && button.RawAction == null && string.IsNullOrWhiteSpace(button.ActionArg))
                    problems.Add($"\"{button.Label}\": say which {ActionKinds[button.ActionKind]} it should run.");
            }
            if (problems.Count > 0)
            {
                MessageBox.Show(string.Join("\n", problems), "Fix these before saving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var buttons = new JsonArray();
            foreach (var button in _buttons)
                buttons.Add(WriteButton(button));

            var edits = new JsonArray();
            foreach (var row in _gameRows.Where(r => r.Label.Length > 0 || r.Description.Length > 0 || r.Hidden))
            {
                var edit = new JsonObject { ["item"] = row.Item };
                if (row.Label.Length > 0)
                    edit["label"] = row.Label;
                if (row.Description.Length > 0)
                    edit["description"] = row.Description;
                if (row.Hidden)
                    edit["hidden"] = true;
                edits.Add(edit);
            }

            string path = FilePath(extraCardsFolder);
            if (buttons.Count == 0 && edits.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var root = new JsonObject { ["buttons"] = buttons, ["edit"] = edits };
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(path, root.ToJsonString(options) + "\n", new UTF8Encoding(false));
            return true;
        }

        // ---- json ----

        private static string Text(JsonObject node, string key) =>
            node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

        private static ButtonModel ReadButton(JsonObject node)
        {
            var button = new ButtonModel
            {
                Key = Text(node, "key"),
                Menu = Text(node, "menu") is { Length: > 0 } menu ? menu : "main",
                Page = Text(node, "page") is { Length: > 0 } page ? page : "main",
                Label = Text(node, "label"),
                Description = Text(node, "description"),
                Look = Text(node, "look") is { Length: > 0 } look ? look : "deckEditor",
            };

            if (node["action"] is JsonObject action)
            {
                for (int kind = 1; kind < ActionKinds.Length; kind++)
                {
                    if (action[ActionKinds[kind]] is JsonNode argument)
                    {
                        button.ActionKind = kind;
                        button.ActionArg = kind == 4 ? "" : argument.ToString();
                        break;
                    }
                }
            }
            else if (node["action"] is JsonArray steps)
                button.RawAction = steps.DeepClone();

            return button;
        }

        private static JsonObject WriteButton(ButtonModel button)
        {
            var json = new JsonObject
            {
                ["key"] = button.Key,
                ["menu"] = button.Menu,
            };
            if (button.Menu != "options")
                json["page"] = button.Page;
            json["label"] = button.Label;
            if (button.Description.Length > 0)
                json["description"] = button.Description;
            json["look"] = button.Look;

            if (button.RawAction != null)
                json["action"] = button.RawAction.DeepClone();
            else if (button.ActionKind == 4)
                json["action"] = new JsonObject { ["quit"] = true };
            else if (button.ActionKind > 0)
                json["action"] = new JsonObject { [ActionKinds[button.ActionKind]] = button.ActionArg };

            return json;
        }
    }
}
