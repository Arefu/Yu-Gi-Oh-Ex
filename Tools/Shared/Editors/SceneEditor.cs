using System.Drawing.Drawing2D;
using System.IO;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// What a story scene looks like after a given step, worked out the way the game's StoryScene_PlayLine (0x14082F890) does it:
    /// backgrounds and props from "command" steps, characters moving between the stage slots (g_StorySceneSlots, 0x140A72370), the
    /// narrator box, and the line being said. docs/StoryScenes.md.
    /// </summary>
    public sealed class SceneState
    {
        /// <summary>Stage slots: x of the sprite's centre on the 1920x1080 stage. 2 LEFT, 3 CENTER, 4 RIGHT; 1 and 5 = pushed aside.</summary>
        public static readonly float[] SlotX = [-300, 120, 500, 960, 1460, 1780, 2220];

        /// <summary>Drawing order (the game's slot depth): the far slots first.</summary>
        public static readonly int[] SlotDepth = [0, 1, 2, 3, 2, 1, 0];

        /// <summary>Where the one in a slot goes when someone enters it (-1 = off stage).</summary>
        public static readonly int[] PushAside = [0, 0, 1, -1, 5, 6, 6];

        public sealed class Actor
        {
            public string Key = "";
            public int Slot;
            public string Expression = "";
        }

        public string? Background;
        public string? Prop;
        public List<Actor> Actors { get; } = [];

        /// <summary>The character key speaking at this step ("" = nobody / the narrator).</summary>
        public string Speaker = "";
        public bool Narrating;
        public string Text = "";

        public Actor? Find(string key) => Actors.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        public static int SlotOf(string position)
        {
            string p = position.ToUpperInvariant();
            if (p.Contains("FADEOUT") || p.Contains("NONE"))
                return -1;
            if (p.Contains("LEFT"))
                return 2;
            if (p.Contains("RIGHT"))
                return 4;
            if (p.Contains("CENTER"))
                return 3;
            return 7;   // "" (or FADEIN alone): stay, or the first free of LEFT / RIGHT
        }

        public static SceneState Run(IReadOnlyList<ScriptLine> lines, int through, char language)
        {
            var state = new SceneState();
            for (int i = 0; i <= through && i < lines.Count; i++)
                state.Step(lines[i], language, i == through);
            return state;
        }

        private void Step(ScriptLine line, char language, bool last)
        {
            if (last)
            {
                Speaker = "";
                Text = "";
                Narrating = false;
            }
            string position = line.Position.ToUpperInvariant();
            if (line.IsCommand)
            {
                if (position == "BG") Background = line.Expression;
                else if (position == "PROP_ON") Prop = line.Expression;
                else if (position == "PROP_OFF") Prop = null;
                return;
            }
            string text = line.Text(language);
            if (line.IsNarrator)
            {
                if (last)
                {
                    Narrating = text.Length > 0;
                    Text = text;
                }
                return;
            }
            var actor = Find(line.Speaker);
            if (position.Length == 0 && text.Length == 0 && line.Expression.Length > 0)
            {
                if (actor != null)
                    actor.Expression = line.Expression;   // expression only
                return;
            }
            int slot = SlotOf(position);
            if (slot == -1)
            {
                if (actor != null)
                    Actors.Remove(actor);
            }
            else
            {
                if (slot == 7)
                    slot = actor?.Slot ?? (Actors.All(a => a.Slot != 2) ? 2 : Actors.All(a => a.Slot != 4) ? 4 : 2);
                if (actor == null)
                {
                    actor = new Actor { Key = line.Speaker };
                    Actors.Add(actor);
                }
                if (actor.Slot != slot)
                {
                    var occupant = Actors.FirstOrDefault(a => a != actor && a.Slot == slot);
                    if (occupant != null)
                    {
                        int aside = PushAside[slot];
                        if (aside is <= 0 or >= 6)
                            Actors.Remove(occupant);
                        else
                        {
                            Actors.RemoveAll(a => a != actor && a != occupant && a.Slot == aside);
                            occupant.Slot = aside;
                        }
                    }
                    actor.Slot = slot;
                }
                if (line.Expression.Length > 0)
                    actor.Expression = line.Expression;
            }
            if (last && text.Length > 0)
            {
                Speaker = line.Speaker;
                Text = text;
            }
        }

        /// <summary>The sprite files for a character with an expression: base "&lt;key&gt;[_costume]_neutral" and an overlay (StoryScene_GetCharacterTextures).</summary>
        public static (string Base, string? Overlay) SpriteNames(string key, string expression)
        {
            string prefix = key, face = expression;
            int underscore = expression.IndexOf('_');
            if (underscore >= 0)
            {
                prefix = $"{key}_{expression[..underscore]}";
                face = expression[(underscore + 1)..];
            }
            string? overlay = face.Length == 0 || face.Equals("neutral", StringComparison.OrdinalIgnoreCase) ? null : $"{prefix}_{face}";
            return ($"{prefix}_neutral", overlay);
        }
    }

    /// <summary>
    /// The 1920x1080 story stage, scaled to fit: background, prop, characters in their slots and the dialogue box, as of one step.
    /// Drag a character to another slot (or off the stage's edge to make them leave): <see cref="ActorDropped"/>.
    /// </summary>
    public sealed class SceneStage : Control
    {
        public const int StageWidth = 1920, StageHeight = 1080;

        private readonly Dictionary<string, Bitmap?> _pictures = new(StringComparer.OrdinalIgnoreCase);
        private SceneState? _state;
        private SceneState.Actor? _dragging;
        private float _dragX;
        private Point _dragStart;
        private bool _dragMoved;

        public SceneStage()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.FromArgb(24, 24, 28);
        }

        /// <summary>Where the pictures come from (pdui\dialog_bg, dialog_props, dialog_chars).</summary>
        public IGameFiles? Files { get; set; }

        /// <summary>Name shown for a character key (the dialogue box's name plate).</summary>
        public Func<string, string>? NameOf { get; set; }

        /// <summary>The narrator's name (the game uses character 139's).</summary>
        public string NarratorName { get; set; } = "";

        /// <summary>A character was dragged to "LEFT", "CENTER", "RIGHT" or "NONE".</summary>
        public event Action<string, string>? ActorDropped;

        public SceneState? State
        {
            get => _state;
            set
            {
                _state = value;
                Invalidate();
            }
        }

        public void ClearPictures()
        {
            foreach (var picture in _pictures.Values)
                picture?.Dispose();
            _pictures.Clear();
            Invalidate();
        }

        private Bitmap? Picture(string folder, string name, params string[] extensions)
        {
            string id = $@"{folder}\{name}";
            if (_pictures.TryGetValue(id, out var cached))
                return cached;
            Bitmap? picture = null;
            foreach (string extension in extensions)
                if ((picture = Imaging.Decode(Files?.Read($@"pdui\{folder}\{name}{extension}"))) != null)
                    break;
            _pictures[id] = picture;
            return picture;
        }

        private RectangleF StageRect()
        {
            float scale = Math.Min((float)ClientSize.Width / StageWidth, (float)ClientSize.Height / StageHeight);
            float w = StageWidth * scale, h = StageHeight * scale;
            return new RectangleF((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
        }

        private float ViewScale => StageRect().Width / StageWidth;

        /// <summary>Where a character's sprite sits on the stage (stage pixels): centred on its slot, standing on the bottom edge.</summary>
        private RectangleF SpriteRect(SceneState.Actor actor, float? x = null)
        {
            var (baseName, _) = SceneState.SpriteNames(actor.Key, actor.Expression);
            var picture = Picture("dialog_chars", baseName, ".png");
            SizeF size = picture != null ? new SizeF(picture.Width, picture.Height) : new SizeF(600, 900);
            float cx = x ?? SceneState.SlotX[Math.Clamp(actor.Slot, 0, 6)];
            return new RectangleF(cx - size.Width / 2, StageHeight - size.Height, size.Width, size.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            var stage = StageRect();
            g.SetClip(stage);
            g.TranslateTransform(stage.X, stage.Y);
            g.ScaleTransform(ViewScale, ViewScale);

            var state = _state;
            var background = state?.Background is string bg && bg.Length > 0 ? Picture("dialog_bg", bg, ".jpg", ".png") : null;
            if (background != null)
                g.DrawImage(background, 0, 0, StageWidth, StageHeight);
            else
            {
                using var empty = new LinearGradientBrush(new Rectangle(0, 0, StageWidth, StageHeight), Color.FromArgb(40, 44, 60), Color.FromArgb(16, 16, 20), 90f);
                g.FillRectangle(empty, 0, 0, StageWidth, StageHeight);
                Centered(g, state?.Background is string missing && missing.Length > 0 ? $"background \"{missing}\" not found" : "no background yet (add a BG step)", 48, StageHeight / 2f);
            }

            if (state != null)
            {
                // the slot marks, so there's somewhere to drag to
                using (var mark = new Pen(Color.FromArgb(70, 255, 255, 255), 3) { DashStyle = DashStyle.Dash })
                    foreach (int slot in new[] { 2, 3, 4 })
                        g.DrawLine(mark, SceneState.SlotX[slot], StageHeight - 30, SceneState.SlotX[slot], StageHeight - 90);

                if (state.Prop is string prop && Picture("dialog_props", prop, ".png") is Bitmap propPicture)
                    g.DrawImage(propPicture, (StageWidth - propPicture.Width) / 2f, (StageHeight - propPicture.Height) / 2f - 120, propPicture.Width, propPicture.Height);

                foreach (var actor in state.Actors.OrderBy(a => SceneState.SlotDepth[Math.Clamp(a.Slot, 0, 6)])
                             .ThenBy(a => a.Key.Equals(state.Speaker, StringComparison.OrdinalIgnoreCase) ? 1 : 0))
                {
                    bool dragged = actor == _dragging && _dragMoved;
                    bool speaking = state.Speaker.Length > 0 && actor.Key.Equals(state.Speaker, StringComparison.OrdinalIgnoreCase);
                    DrawActor(g, actor, dragged ? _dragX : null, dim: state.Speaker.Length > 0 && !speaking, ghost: dragged);
                }

                if (state.Text.Length > 0)
                    DrawDialogue(g, state.Narrating ? NarratorName : NameOf?.Invoke(state.Speaker) ?? state.Speaker, state.Text, state.Narrating);

                if (_dragging != null && _dragMoved)
                {
                    string target = DropTarget(_dragX);
                    Centered(g, target == "NONE" ? "leave the stage (NONE)" : target, 44, 60);
                }
            }
            g.ResetTransform();
            g.ResetClip();
            using var border = new Pen(Color.FromArgb(90, 90, 100));
            g.DrawRectangle(border, stage.X, stage.Y, stage.Width - 1, stage.Height - 1);
        }

        private void DrawActor(Graphics g, SceneState.Actor actor, float? x, bool dim, bool ghost)
        {
            var rect = SpriteRect(actor, x);
            var (baseName, overlayName) = SceneState.SpriteNames(actor.Key, actor.Expression);
            var basePicture = Picture("dialog_chars", baseName, ".png");
            var overlay = overlayName != null ? Picture("dialog_chars", overlayName, ".png") : null;
            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            float shade = dim ? 0.6f : 1f, alpha = ghost ? 0.6f : 1f;
            attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(
            [
                [shade, 0, 0, 0, 0], [0, shade, 0, 0, 0], [0, 0, shade, 0, 0], [0, 0, 0, alpha, 0], [0, 0, 0, 0, 1],
            ]));
            if (basePicture == null && overlay == null)
            {
                using var box = new SolidBrush(Color.FromArgb(ghost ? 90 : 150, 80, 90, 120));
                g.FillRectangle(box, rect.X + 100, rect.Y + 200, rect.Width - 200, rect.Height - 200);
                Centered(g, $"{actor.Key}\n(no {baseName})", 30, rect.Y + rect.Height / 2, rect.X + rect.Width / 2);
                return;
            }
            foreach (var picture in new[] { basePicture, overlay })
                if (picture != null)
                    g.DrawImage(picture, Rectangle.Round(new RectangleF(rect.X, rect.Bottom - picture.Height, picture.Width, picture.Height)),
                        0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel, attributes);
        }

        private static void DrawDialogue(Graphics g, string name, string text, bool narrator)
        {
            var box = new RectangleF(160, 790, 1600, 250);
            using (var path = Rounded(box, 26))
            using (var fill = new SolidBrush(Color.FromArgb(narrator ? 215 : 200, 12, 16, 30)))
            using (var edge = new Pen(Color.FromArgb(200, 190, 160, 80), 4))
            {
                g.FillPath(fill, path);
                g.DrawPath(edge, path);
            }
            if (name.Length > 0)
            {
                var plate = new RectangleF(box.X + 40, box.Y - 34, Math.Max(260, name.Length * 26 + 60), 64);
                using var platePath = Rounded(plate, 14);
                using var plateFill = new SolidBrush(Color.FromArgb(235, 150, 110, 30));
                g.FillPath(plateFill, platePath);
                using var nameFont = new Font("Segoe UI", 30, FontStyle.Bold, GraphicsUnit.Pixel);
                g.DrawString(name, nameFont, Brushes.White, plate, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }
            using var font = new Font("Segoe UI", 38, GraphicsUnit.Pixel);
            g.DrawString(text.Replace("{GAMERTAG}", "Player"), font, Brushes.White, RectangleF.Inflate(box, -50, -40));
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void Centered(Graphics g, string text, float size, float y, float x = StageWidth / 2f)
        {
            using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = g.MeasureString(text, font);
            var rect = new RectangleF(x - measured.Width / 2 - 16, y - measured.Height / 2 - 8, measured.Width + 32, measured.Height + 16);
            using var back = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            g.FillRectangle(back, rect);
            g.DrawString(text, font, Brushes.White, rect, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }

        private PointF ToStage(Point p)
        {
            var stage = StageRect();
            return new PointF((p.X - stage.X) / ViewScale, (p.Y - stage.Y) / ViewScale);
        }

        /// <summary>The slot nearest a stage x; off the stage's edges = NONE.</summary>
        private static string DropTarget(float x)
        {
            if (x < 100 || x > StageWidth - 100)
                return "NONE";
            int slot = new[] { 2, 3, 4 }.OrderBy(s => Math.Abs(SceneState.SlotX[s] - x)).First();
            return slot switch { 2 => "LEFT", 3 => "CENTER", _ => "RIGHT" };
        }

        private SceneState.Actor? HitTest(PointF stagePoint)
        {
            if (_state == null)
                return null;
            // topmost first: the reverse of the drawing order
            foreach (var actor in _state.Actors.OrderByDescending(a => SceneState.SlotDepth[Math.Clamp(a.Slot, 0, 6)]))
            {
                var rect = SpriteRect(actor);
                rect.Inflate(-rect.Width * 0.2f, 0);   // the transparent margins of the pictures
                if (rect.Contains(stagePoint))
                    return actor;
            }
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            _dragging = HitTest(ToStage(e.Location));
            _dragStart = e.Location;
            _dragMoved = false;
            if (_dragging != null)
            {
                _dragX = SceneState.SlotX[Math.Clamp(_dragging.Slot, 0, 6)];
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging == null)
            {
                Cursor = HitTest(ToStage(e.Location)) != null ? Cursors.SizeWE : Cursors.Default;
                return;
            }
            if (!_dragMoved && Math.Abs(e.X - _dragStart.X) < 4)
                return;
            _dragMoved = true;
            _dragX = ToStage(e.Location).X;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var actor = _dragging;
            bool moved = _dragMoved;
            _dragging = null;
            _dragMoved = false;
            Capture = false;
            if (actor != null && moved)
                ActorDropped?.Invoke(actor.Key, DropTarget(ToStage(e.Location).X));
            Invalidate();
        }
    }

    /// <summary>
    /// One story scene: the steps on the left, the stage as of the selected step on the right (drag characters about), and the selected
    /// step's who / position / expression / text underneath. Edits the <see cref="StoryScript"/> it is given in place.
    /// </summary>
    public sealed class SceneEditor : UserControl
    {
        private readonly ListView _steps = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = false, VirtualMode = true,
        };
        private readonly SceneStage _stage = new() { Dock = DockStyle.Fill };
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ComboBox _who = new() { Width = 170, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
        private readonly ComboBox _position = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDown };
        private readonly ComboBox _expression = new() { Width = 190, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
        private readonly Label _whoName = new() { AutoSize = true, Margin = new Padding(6, 7, 3, 3) };
        private readonly DataGridView _texts = new()
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
        };
        private readonly Panel _empty = new() { Dock = DockStyle.Fill, Visible = false };
        private readonly Label _emptyText = new() { AutoSize = true, Location = new Point(20, 20) };
        private readonly Button _create = new() { Text = "Create this scene", AutoSize = true, Location = new Point(20, 50) };
        private readonly SplitContainer _main = new() { Dock = DockStyle.Fill };
        private readonly System.Windows.Forms.Timer _player = new();
        private ToolStripButton _play = null!;
        private readonly ToolStripComboBox _speed = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60, ToolTipText = "How fast Play goes" };
        private static readonly double[] Speeds = [1, 1.5, 2, 3];

        private StoryScript? _script;
        private string _sceneName = "";
        private bool _binding;
        private List<string> _characterKeys = [];
        private Dictionary<string, List<string>> _expressions = new(StringComparer.OrdinalIgnoreCase);
        private List<string> _backgrounds = [], _props = [];

        private static readonly string[] CharacterPositions = ["", "LEFT", "CENTER", "RIGHT", "NONE", "LEFT:FADEIN", "CENTER:FADEIN", "RIGHT:FADEIN", "FADEOUT"];
        private static readonly string[] CommandPositions = ["BG", "PROP_ON", "PROP_OFF"];

        public char Language { get; set; } = 'E';

        /// <summary>The background pictures (pdui\dialog_bg, names without the extension).</summary>
        public IReadOnlyList<string> Backgrounds => _backgrounds;

        /// <summary>Called when the scene changes (a step edited, added, removed or moved).</summary>
        public event Action? Changed;

        /// <summary>Called by "Create this scene" with the scene's name: return the new script (already added to the table) or null.</summary>
        public Func<string, StoryScript?>? CreateScene { get; set; }

        public Func<string, string>? NameOf
        {
            get => _stage.NameOf;
            set => _stage.NameOf = value;
        }

        public SceneEditor()
        {
            // the larger window gets the playback controls too, since the page's toolbar stays behind
            PreviewPopOut.Attach(_stage, "Story scene", false,
                new PopOutAction("◀ Prev", "The previous step", () => { StopPlaying(); SelectStep(SelectedIndex() - 1); }),
                new PopOutAction("▶ Play / ❚❚ Pause", "Play the scene from the selected step, line by line", TogglePlay),
                new PopOutAction("Next ▶", "The next step", () => { StopPlaying(); SelectStep(SelectedIndex() + 1); }));
            PreviewPopOut.SetShape(_stage, new Size(1920, 1080));   // the game screen it draws
            ListPick.FirstWhenShown(_steps);
            _player.Tick += (_, _) => PlayNext();
            Disposed += (_, _) => _player.Dispose();
            _steps.Columns.Add("#", 36);
            _steps.Columns.Add("Who", 110);
            _steps.Columns.Add("Position", 90);
            _steps.Columns.Add("Expression", 100);
            _steps.Columns.Add("Text", 300);
            _steps.RetrieveVirtualItem += (_, e) =>
            {
                var line = _script!.Lines[e.ItemIndex];
                e.Item = new ListViewItem([(e.ItemIndex + 1).ToString(), WhoText(line), line.Position, line.Expression, line.Text(Language)]);
                if (line.IsCommand)
                    e.Item.ForeColor = Color.SteelBlue;
                else if (line.IsNarrator)
                    e.Item.ForeColor = Color.DarkGoldenrod;
            };
            _steps.SelectedIndexChanged += (_, _) => ShowStep();

            ToolStripButton Tool(string text, string tip, Action click)
            {
                var button = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
                button.Click += (_, _) => click();
                _tools.Items.Add(button);
                return button;
            }
            Tool("Add line", "A character line after the selected step (the same speaker)", () => AddStep(SelectedLine() is ScriptLine l && !l.IsCommand && !l.IsNarrator ? l.Speaker : _characterKeys.FirstOrDefault() ?? ""));
            Tool("Narrator", "A narrator line after the selected step", () => AddStep(ScriptLine.Narrator));
            Tool("Background", "A background change after the selected step", () => AddStep(ScriptLine.Command, "BG", _backgrounds.FirstOrDefault() ?? ""));
            Tool("Prop", "Show a prop (Millennium items, cards...) after the selected step", () => AddStep(ScriptLine.Command, "PROP_ON", _props.FirstOrDefault() ?? ""));
            Tool("Duplicate", "A copy of the selected step after it", () => { if (SelectedLine() is ScriptLine l) Insert(l.Clone()); });
            _tools.Items.Add(new ToolStripSeparator());
            Tool("Up", "Move the selected step up", () => MoveStep(-1));
            Tool("Down", "Move the selected step down", () => MoveStep(1));
            Tool("Remove", "Remove the selected step", Remove);
            _tools.Items.Add(new ToolStripSeparator());
            Tool("◀ Prev", "The previous step (Page Up)", () => { StopPlaying(); SelectStep(SelectedIndex() - 1); });
            _play = Tool("▶ Play", "Play the scene from the selected step, line by line, as the game shows it (Space on the step list)", TogglePlay);
            Tool("Next ▶", "The next step (Page Down)", () => { StopPlaying(); SelectStep(SelectedIndex() + 1); });
            foreach (double speed in Speeds)
                _speed.Items.Add($"{speed}x");
            _speed.SelectedIndex = 1;   // 1.5x
            _tools.Items.Add(_speed);
            _steps.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Space)
                {
                    TogglePlay();
                    e.Handled = e.SuppressKeyPress = true;
                }
            };

            var props = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(2) };
            void Row(string label, params Control[] controls)
            {
                props.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
                var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                flow.Controls.AddRange(controls);
                props.Controls.Add(flow);
            }
            Row("Who:", _who, _whoName);
            Row("Position:", _position, new Label { Text = "Expression / picture:", AutoSize = true, Margin = new Padding(12, 7, 3, 3) }, _expression);
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Language", ReadOnly = true, FillWeight = 12 });
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Text ({GAMERTAG} = the player's name)", FillWeight = 88, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } });
            foreach (char language in StoryScriptTable.AllLanguages)
                _texts.Rows.Add(language.ToString(), "");

            _who.TextChanged += (_, _) => Edited(whoChanged: true);
            _position.TextChanged += (_, _) => Edited();
            _expression.TextChanged += (_, _) => Edited();
            _texts.CellValueChanged += (_, _) => Edited();
            _stage.ActorDropped += Dropped;

            var stepPanel = new Panel { Dock = DockStyle.Fill };
            stepPanel.Controls.Add(_texts);
            stepPanel.Controls.Add(props);
            var left = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            left.Panel1.Controls.Add(_steps);
            left.Panel2.Controls.Add(stepPanel);
            _main.Panel1.Controls.Add(left);
            _main.Panel2.Controls.Add(_stage);
            Load += (_, _) =>
            {
                _main.SplitterDistance = Math.Max(360, _main.Width * 36 / 100);
                left.SplitterDistance = Math.Max(150, left.Height * 55 / 100);
            };

            _empty.Controls.Add(_emptyText);
            _empty.Controls.Add(_create);
            _create.Click += (_, _) =>
            {
                if (CreateScene?.Invoke(_sceneName) is StoryScript script)
                {
                    Script = script;
                    Changed?.Invoke();
                }
            };

            Controls.Add(_main);
            Controls.Add(_empty);
            Controls.Add(_tools);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.PageDown) { SelectStep(SelectedIndex() + 1); return true; }
            if (keyData == Keys.PageUp) { SelectStep(SelectedIndex() - 1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>The game's pictures and what's in them: the sprite / background / prop lists come from the file names.</summary>
        public void UseFiles(IGameFiles? files, IEnumerable<string> characterKeys, string narratorName)
        {
            _stage.Files = files;
            _stage.NarratorName = narratorName;
            _stage.ClearPictures();
            _expressions = new(StringComparer.OrdinalIgnoreCase);
            _backgrounds = [];
            _props = [];
            var paths = files?.Paths.Select(p => p.Replace('/', '\\')).ToList() ?? [];
            var chars = paths.Where(p => p.StartsWith(@"pdui\dialog_chars\", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileNameWithoutExtension).OfType<string>().ToList();
            var keys = new SortedSet<string>(characterKeys.Where(k => k.Length > 0), StringComparer.OrdinalIgnoreCase);
            foreach (string name in chars)
            {
                // "<key>_<expression>" (the key itself has no underscore; costumes are "<key>_<costume>_<expression>")
                int underscore = name.IndexOf('_');
                if (underscore <= 0)
                    continue;
                string key = name[..underscore];
                keys.Add(key);
                if (!_expressions.TryGetValue(key, out var list))
                    _expressions[key] = list = [];
                list.Add(name[(underscore + 1)..]);
            }
            _characterKeys = [.. keys];
            _backgrounds = [.. paths.Where(p => p.StartsWith(@"pdui\dialog_bg\", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileNameWithoutExtension).OfType<string>().Distinct().Order()];
            _props = [.. paths.Where(p => p.StartsWith(@"pdui\dialog_props\", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileNameWithoutExtension).OfType<string>().Distinct().Order()];
            _who.Items.Clear();
            _who.Items.Add(ScriptLine.Command);
            _who.Items.Add(ScriptLine.Narrator);
            _who.Items.AddRange([.. _characterKeys]);
        }

        /// <summary>The scene's name (shown with "Create this scene" when the scene doesn't exist) and its script (null = none yet).</summary>
        public void Show(string sceneName, StoryScript? script)
        {
            _sceneName = sceneName;
            Script = script;
        }

        private StoryScript? Script
        {
            set
            {
                StopPlaying();
                _script = value;
                _main.Visible = value != null;
                _empty.Visible = value == null;
                _emptyText.Text = $"There is no {_sceneName} scene: the duel plays without it.";
                _create.Text = $"Create {_sceneName}";
                _steps.VirtualListSize = value?.Lines.Count ?? 0;
                _steps.Invalidate();
                SelectStep(value != null && value.Lines.Count > 0 ? 0 : -1);
            }
        }

        public void Reload(bool keepSelection)
        {
            _steps.VirtualListSize = _script?.Lines.Count ?? 0;
            _steps.Invalidate();
            SelectStep(keepSelection ? SelectedIndex() : 0);
        }

        // ---- playback ----

        /// <summary>Plays from the selected step (from the start when at the end), or pauses.</summary>
        private void TogglePlay()
        {
            if (_player.Enabled)
            {
                StopPlaying();
                return;
            }
            if (_script == null || _script.Lines.Count == 0)
                return;
            if (SelectedIndex() < 0 || SelectedIndex() >= _script.Lines.Count - 1)
                SelectStep(0);
            _player.Interval = Hold(SelectedLine());
            _player.Start();
            _play.Text = "❚❚ Pause";
        }

        private void StopPlaying()
        {
            _player.Stop();
            if (_play != null)
                _play.Text = "▶ Play";
        }

        private void PlayNext()
        {
            if (_script == null || SelectedIndex() >= _script.Lines.Count - 1)
            {
                StopPlaying();
                return;
            }
            SelectStep(SelectedIndex() + 1);
            _player.Interval = Hold(SelectedLine());
        }

        /// <summary>How long a step stays up: a moment for a background or prop, longer for a line with more to read; divided by the speed picked.</summary>
        private int Hold(ScriptLine? line)
        {
            int ms = line == null || line.IsCommand ? 350 : Math.Clamp(800 + line.Text(Language).Length * 30, 1200, 5500);
            double speed = Speeds[Math.Clamp(_speed.SelectedIndex, 0, Speeds.Length - 1)];
            return Math.Max(100, (int)(ms / speed));
        }

        private string WhoText(ScriptLine line) =>
            line.IsCommand ? "▣ command" : line.IsNarrator ? "✎ narrator" : line.Speaker;

        private int SelectedIndex() => _steps.SelectedIndices.Count > 0 ? _steps.SelectedIndices[0] : -1;

        private ScriptLine? SelectedLine() => _script != null && SelectedIndex() is int i && i >= 0 && i < _script.Lines.Count ? _script.Lines[i] : null;

        private void SelectStep(int index)
        {
            if (_script == null)
                return;
            index = Math.Clamp(index, _script.Lines.Count > 0 ? 0 : -1, _script.Lines.Count - 1);
            _steps.SelectedIndices.Clear();
            if (index >= 0)
            {
                _steps.SelectedIndices.Add(index);
                _steps.EnsureVisible(index);
            }
            ShowStep();
        }

        private void ShowStep()
        {
            var line = SelectedLine();
            _stage.State = _script != null && line != null ? SceneState.Run(_script.Lines, SelectedIndex(), Language) : new SceneState();
            _binding = true;
            try
            {
                foreach (Control control in new Control[] { _who, _position, _expression, _texts })
                    control.Enabled = line != null;
                if (line == null)
                    return;
                _who.Text = line.Speaker;
                FillChoices(line);
                _position.Text = line.Position;
                _expression.Text = line.Expression;
                for (int i = 0; i < StoryScriptTable.AllLanguages.Length; i++)
                    _texts.Rows[i].Cells[1].Value = line.Texts.GetValueOrDefault(StoryScriptTable.AllLanguages[i], "");
                _texts.Enabled = !line.IsCommand;
                _whoName.Text = line.IsCommand ? "a background / prop change" : line.IsNarrator ? "the narrator box" : NameOf?.Invoke(line.Speaker) ?? "";
            }
            finally
            {
                _binding = false;
            }
        }

        private void FillChoices(ScriptLine line)
        {
            _position.Items.Clear();
            _position.Items.AddRange(line.IsCommand ? CommandPositions : line.IsNarrator ? ["", "CENTER", "NONE"] : CharacterPositions);
            _expression.Items.Clear();
            if (line.IsCommand)
                _expression.Items.AddRange([.. (line.Position.Equals("BG", StringComparison.OrdinalIgnoreCase) ? _backgrounds : _props)]);
            else if (!line.IsNarrator)
            {
                _expression.Items.Add("");
                if (_expressions.TryGetValue(line.Speaker, out var list))
                    _expression.Items.AddRange([.. list.Order()]);
            }
        }

        private void Edited(bool whoChanged = false)
        {
            if (_binding || SelectedLine() is not ScriptLine line)
                return;
            line.Speaker = _who.Text.Trim();
            line.Position = _position.Text.Trim();
            line.Expression = _expression.Text.Trim();
            for (int i = 0; i < StoryScriptTable.AllLanguages.Length; i++)
            {
                char language = StoryScriptTable.AllLanguages[i];
                if (_texts.Rows[i].Cells[1].Value is string text && (text.Length > 0 || line.Texts.ContainsKey(language)))
                    line.Texts[language] = text;
            }
            if (whoChanged)
            {
                _binding = true;
                try
                {
                    string position = _position.Text, expression = _expression.Text;
                    FillChoices(line);
                    _position.Text = position;
                    _expression.Text = expression;
                    _whoName.Text = line.IsCommand ? "a background / prop change" : line.IsNarrator ? "the narrator box" : NameOf?.Invoke(line.Speaker) ?? "";
                }
                finally
                {
                    _binding = false;
                }
            }
            _steps.Invalidate();
            _stage.State = SceneState.Run(_script!.Lines, SelectedIndex(), Language);
            Changed?.Invoke();
        }

        private void AddStep(string who, string position = "", string expression = "") =>
            Insert(new ScriptLine { Speaker = who, Position = position, Expression = expression });

        private void Insert(ScriptLine line)
        {
            if (_script == null)
                return;
            int at = SelectedIndex() + 1;
            _script.Lines.Insert(at, line);
            _steps.VirtualListSize = _script.Lines.Count;
            _steps.Invalidate();
            SelectStep(at);
            Changed?.Invoke();
        }

        private void MoveStep(int by)
        {
            int i = SelectedIndex(), j = i + by;
            if (_script == null || i < 0 || j < 0 || j >= _script.Lines.Count)
                return;
            (_script.Lines[i], _script.Lines[j]) = (_script.Lines[j], _script.Lines[i]);
            _steps.Invalidate();
            SelectStep(j);
            Changed?.Invoke();
        }

        private void Remove()
        {
            int i = SelectedIndex();
            if (_script == null || i < 0)
                return;
            if (_script.Lines.Count == 1)
            {
                MessageBox.Show(this, "A scene needs at least one step. Use Remove scene above to drop the whole scene.", "Scene", MessageBoxButtons.OK);
                return;
            }
            _script.Lines.RemoveAt(i);
            _steps.VirtualListSize = _script.Lines.Count;
            _steps.Invalidate();
            SelectStep(Math.Min(i, _script.Lines.Count - 1));
            Changed?.Invoke();
        }

        /// <summary>A character dragged on the stage: the selected step moves them if it's theirs, otherwise a new step after it does.</summary>
        private void Dropped(string key, string position)
        {
            if (_script == null)
                return;
            var line = SelectedLine();
            if (line != null && line.Speaker.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                line.Position = position;
                _steps.Invalidate();
                ShowStep();
                Changed?.Invoke();
            }
            else
                Insert(new ScriptLine { Speaker = key, Position = position });
        }
    }
}
