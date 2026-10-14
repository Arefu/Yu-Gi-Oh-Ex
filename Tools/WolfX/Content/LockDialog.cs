using System.Text.Json.Nodes;

namespace WolfEx
{
    /// <summary>
    /// A card's continuous lock, as Jinzo / Spell Canceller / Skill Drain have (the "lock" key Yu-Gi-Oh-Effects Lock.cpp reads; docs/EffectSystem.md
    /// section 44). While the card is face-up in its zone and its own effects aren't negated, the chosen kinds of card can't be activated and
    /// their effects on the field are negated.
    /// </summary>
    internal sealed class LockDialog : Form
    {
        private readonly CheckBox _traps = Check("Trap Cards (can't be activated; Trap effects on the field negated)");
        private readonly CheckBox _spells = Check("Spell Cards (the same for Spells)");
        private readonly CheckBox _monsters = Check("Monster effects (the effects of face-up monsters are negated)");
        private readonly CheckBox _continuousTraps = Check("Continuous Traps can't be activated (both players)");
        private readonly CheckBox _continuousSpells = Check("Continuous Spells can't be activated (both players)");
        private readonly CheckBox _fieldSpells = Check("Field Spells can't be activated (both players)");
        private readonly CheckBox _equipSpells = Check("Equip Spells can't be activated (both players)");
        private readonly ComboBox _side = Combo(["both players", "the opponent only", "its controller only"]);
        private readonly ComboBox _zone = Combo(["a Monster Zone", "a Spell/Trap Zone", "any zone"]);

        private static readonly string[] SideKeys = ["both", "opponent", "you"];
        private static readonly string[] ZoneKeys = ["monster", "spelltrap", "any"];

        /// <summary>The lock after OK; null = no lock (Remove lock, or nothing ticked).</summary>
        public JsonObject? Result { get; private set; }

        public LockDialog(string cardName, JsonObject? current)
        {
            Text = "Lock - " + cardName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(10);

            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            flow.Controls.Add(Note("While this card is face-up in the zone below and its effects aren't negated, the cards ticked here can't be " +
                                   "activated and their effects on the field are negated."));
            flow.Controls.Add(Heading("Lock"));
            foreach (var box in new[] { _traps, _spells, _monsters })
                flow.Controls.Add(box);
            flow.Controls.Add(Row(new Label { Text = "Affects:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, _side));
            flow.Controls.Add(Heading("Only certain Spell / Trap types (either player)"));
            foreach (var box in new[] { _continuousTraps, _continuousSpells, _fieldSpells, _equipSpells })
                flow.Controls.Add(box);
            flow.Controls.Add(Row(new Label { Text = "Works while this card is in:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, _zone));


            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var remove = new Button { Text = "Remove lock", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            ok.Click += (_, _) => Result = Build();
            remove.Click += (_, _) => Result = null;
            flow.Controls.Add(Row(ok, remove, cancel));
            Controls.Add(flow);
            AcceptButton = ok;
            CancelButton = cancel;

            Fill(current ?? new JsonObject { ["traps"] = true, ["side"] = "both", ["zone"] = "monster" });
            remove.Enabled = current != null;
        }

        private void Fill(JsonObject lockJson)
        {
            bool Flag(string key, bool fallback) => lockJson[key] is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;
            _traps.Checked = Flag("traps", true);   // Yu-Gi-Oh-Effects' default too
            _spells.Checked = Flag("spells", false);
            _monsters.Checked = Flag("monsters", false);
            _continuousTraps.Checked = Flag("continuousTraps", false);
            _continuousSpells.Checked = Flag("continuousSpells", false);
            _fieldSpells.Checked = Flag("fieldSpells", false);
            _equipSpells.Checked = Flag("equipSpells", false);
            _side.SelectedIndex = Math.Max(0, Array.IndexOf(SideKeys, lockJson["side"]?.GetValue<string>() ?? "both"));
            _zone.SelectedIndex = Math.Max(0, Array.IndexOf(ZoneKeys, lockJson["zone"]?.GetValue<string>() ?? "monster"));
        }

        private JsonObject? Build()
        {
            var boxes = new (CheckBox Box, string Key)[] { (_traps, "traps"), (_spells, "spells"), (_monsters, "monsters"), (_continuousTraps, "continuousTraps"),
                (_continuousSpells, "continuousSpells"), (_fieldSpells, "fieldSpells"), (_equipSpells, "equipSpells") };
            if (!boxes.Any(b => b.Box.Checked))
                return null;
            var json = new JsonObject { ["traps"] = _traps.Checked };   // written always: Yu-Gi-Oh-Effects takes a missing "traps" as true
            foreach (var (box, key) in boxes.Skip(1))
                if (box.Checked)
                    json[key] = true;
            json["side"] = SideKeys[_side.SelectedIndex];
            json["zone"] = ZoneKeys[_zone.SelectedIndex];
            return json;
        }

        /// <summary>The script line for a lock: lock(trap, spell, opponent, spelltrap_zone); "" for none (EffectScript lockStatement).</summary>
        public static string ToScript(JsonObject? lockJson)
        {
            if (lockJson == null)
                return "";
            bool Flag(string key, bool fallback) => lockJson[key] is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;
            var words = new List<string>();
            foreach (var (key, word, fallback) in new[] { ("traps", "trap", true), ("spells", "spell", false), ("monsters", "monster", false),
                ("continuousTraps", "continuous_trap", false), ("continuousSpells", "continuous_spell", false), ("fieldSpells", "field_spell", false),
                ("equipSpells", "equip_spell", false) })
                if (Flag(key, fallback))
                    words.Add(word);
            if (words.Count == 0)
                return "";
            switch (lockJson["side"]?.GetValue<string>())
            {
                case "opponent": words.Add("opponent"); break;
                case "you": words.Add("own"); break;
            }
            switch (lockJson["zone"]?.GetValue<string>())
            {
                case "spelltrap": words.Add("spelltrap_zone"); break;
                case "any": words.Add("any_zone"); break;
            }
            return "lock(" + string.Join(", ", words) + ");";
        }

        /// <summary>The script with its lock(...) statement replaced by <paramref name="lockLine"/> (first line; "" takes it out).</summary>
        public static string WithLock(string script, string lockLine)
        {
            string rest = System.Text.RegularExpressions.Regex.Replace(script, @"^[ \t]*lock\s*\([^)]*\)\s*;?[ \t]*(\r?\n)?", "",
                System.Text.RegularExpressions.RegexOptions.Multiline).TrimStart('\r', '\n');
            return lockLine.Length == 0 ? rest : rest.Length == 0 ? lockLine : lockLine + "\r\n" + rest;
        }

        /// <summary>One line for the Effects page's status, e.g. "Lock: Traps for both players (Monster Zone)".</summary>
        public static string Describe(JsonObject? lockJson)
        {
            if (lockJson == null)
                return "";
            bool Flag(string key, bool fallback) => lockJson[key] is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;
            var what = new List<string>();
            if (Flag("traps", true)) what.Add("Traps");
            if (Flag("spells", false)) what.Add("Spells");
            if (Flag("monsters", false)) what.Add("monster effects");
            if (Flag("continuousTraps", false)) what.Add("Continuous Traps");
            if (Flag("continuousSpells", false)) what.Add("Continuous Spells");
            if (Flag("fieldSpells", false)) what.Add("Field Spells");
            if (Flag("equipSpells", false)) what.Add("Equip Spells");
            string side = (lockJson["side"]?.GetValue<string>() ?? "both") switch { "opponent" => "the opponent", "you" => "its controller", _ => "both players" };
            return $"Lock: {string.Join(", ", what)} for {side}";
        }

        private static CheckBox Check(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(12, 2, 3, 2) };

        private static ComboBox Combo(string[] items)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
            combo.Items.AddRange(items);
            combo.SelectedIndex = 0;
            return combo;
        }

        private static Label Heading(string text) => new() { Text = text, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold), Margin = new Padding(3, 10, 3, 3) };

        private static Label Note(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 3, 3, 6) };

        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            row.Controls.AddRange(controls);
            return row;
        }
    }
}
