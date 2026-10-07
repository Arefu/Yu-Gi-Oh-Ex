using Wolf.Editors;
using WolfX.Types;
using Limits = PDLimits.PDLimits;

namespace WolfX
{
    /// <summary>
    /// The Forbidden &amp; Limited page: bin\pd_limits.bin from the open game data, saved back into it. Names come from the card catalog,
    /// pictures (when "Load images" is ticked) from the art .zib, both through the open data.
    /// </summary>
    public partial class WolfUI
    {
        private bool _limitsOpen;

        /// <summary>
        /// MP ban lists: Yu-Gi-Oh-Ex\banlists\&lt;file&gt;.json, { "name": "...", "forbidden": [ids], "limited": [ids], "semiLimited": [ids] }
        /// (Konami ids; unlisted cards are unlimited). The Yu-Gi-Oh-MP plugin offers each one as a "Ban list" choice when hosting and sends it
        /// to the lobby. The game's own list (pd_limits.bin, Save) is what plays everywhere else, and ships in the patch DAT.
        /// </summary>
        private void InitLimitsExtras()
        {
            groupBox16.Height = 160;
            var saveMp = new Button { Text = "Save as MP list...", Location = new Point(6, 88), Size = new Size(120, 25), Enabled = false };
            var openMp = new Button { Text = "Open MP list...", Location = new Point(6, 119), Size = new Size(120, 25) };
            saveMp.Click += (_, _) => SaveMpBanList();
            openMp.Click += (_, _) => OpenMpBanList();
            groupBox16.Controls.Add(saveMp);
            groupBox16.Controls.Add(openMp);
            PDL_BTN_SavePDL.EnabledChanged += (_, _) => saveMp.Enabled = PDL_BTN_SavePDL.Enabled;
        }

        private string? BanListFolder() =>
            GameFolderFiles.Current is { } files ? Path.Combine(files.ExFolder, "banlists") : null;

        private static System.Text.Json.Nodes.JsonArray IdArray(IEnumerable<ushort> ids) =>
            new(ids.Select(id => (System.Text.Json.Nodes.JsonNode?)(int)id).ToArray());

        private void SaveMpBanList()
        {
            if (BanListFolder() is not { } folder || !_limitsOpen)
                return;
            Directory.CreateDirectory(folder);
            using var dialog = new SaveFileDialog { InitialDirectory = folder, Filter = "MP ban list (*.json)|*.json", FileName = "My ban list.json", Title = "Save as MP ban list" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            var json = new System.Text.Json.Nodes.JsonObject
            {
                ["name"] = Path.GetFileNameWithoutExtension(dialog.FileName),
                ["forbidden"] = IdArray(Limits.GetForbidden()),
                ["limited"] = IdArray(Limits.GetLimited()),
                ["semiLimited"] = IdArray(Limits.GetSemiLimited()),
            };
            File.WriteAllText(dialog.FileName, json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            SetStatus($"Saved the MP ban list {dialog.FileName} (the Yu-Gi-Oh-MP plugin offers it when hosting).");
        }

        private void OpenMpBanList()
        {
            if (BanListFolder() is not { } folder)
                return;
            using var dialog = new OpenFileDialog { InitialDirectory = Directory.Exists(folder) ? folder : "", Filter = "MP ban list (*.json)|*.json", Title = "Open MP ban list" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(dialog.FileName));
                List<ushort> Ids(string key) => json?[key] is System.Text.Json.Nodes.JsonArray list
                    ? list.Select(n => (ushort)(n?.GetValue<int>() ?? 0)).Where(id => id != 0).Distinct().ToList() : [];
                var forbidden = Ids("forbidden");
                var limited = Ids("limited").Except(forbidden).ToList();
                var semi = Ids("semiLimited").Except(forbidden).Except(limited).ToList();
                foreach (var (list, ids) in new[] { (Limits.GetForbidden(), forbidden), (Limits.GetLimited(), limited), (Limits.GetSemiLimited(), semi) })
                {
                    list.Clear();
                    list.AddRange(ids);
                }
                _limitsOpen = true;
                FillLimits();
                PDL_BTN_SavePDL.Enabled = PDL_BTN_AddCardToList.Enabled = GameFolderFiles.Current != null;
                SetStatus($"Opened {dialog.FileName}: {forbidden.Count} forbidden, {limited.Count} limited, {semi.Count} semi-limited. Save writes it as the game's own list.");
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
            {
                MessageBox.Show(this, ex.Message, "Open MP ban list", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private (ListView List, List<ushort> Cards, Label Count)[] LimitLists =>
        [
            (PDL_LV_ForbiddenCards, Limits.GetForbidden(), PDL_LBL_NumOfForbidden),
            (PDL_LV_LimitedCards, Limits.GetLimited(), PDL_LBL_NumOfLimited),
            (PDL_LV_SemiLimitedCards, Limits.GetSemiLimited(), PDL_LBL_NumOfSemiLimited),
        ];

        private void PDL_BTN_OpenPDL_Click(object sender, EventArgs e)
        {
            if (GameFolderFiles.Current is not { } files || files.Read(Limits.GamePath) is not { } data)
            {
                MessageBox.Show(this, GameFolderFiles.Current == null ? "Open the game folder or an extracted YGO_2020 folder first (File > Open)." : $"The open data has no {Limits.GamePath}.",
                    "Forbidden & Limited", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Limits.Parse(data);
            _limitsOpen = true;
            FillLimits();
            PDL_BTN_SavePDL.Enabled = PDL_BTN_AddCardToList.Enabled = true;
            SetStatus($"{Limits.GamePath} from {files.Describe(Limits.GamePath)}: {Limits.GetForbiddenCount()} forbidden, {Limits.GetLimitedCount()} limited, {Limits.GetSemiLimitedCount()} semi-limited.");
        }

        private void FillLimits()
        {
            State.Images.Images.Clear();
            State.Images.ImageSize = new Size(64, 64);
            foreach (var (list, cards, count) in LimitLists)
            {
                list.BeginUpdate();
                list.Items.Clear();
                list.View = PDL_CB_LoadImages.Checked ? View.LargeIcon : View.List;
                list.LargeImageList = State.Images;
                foreach (ushort card in cards)
                    list.Items.Add(LimitItem(card));
                list.EndUpdate();
                count.Text = cards.Count.ToString();
            }
        }

        private ListViewItem LimitItem(ushort card)
        {
            string key = card.ToString();
            if (PDL_CB_LoadImages.Checked && !State.Images.Images.ContainsKey(key) && CardArt.Get(card) is { } art)
                State.Images.Images.Add(key, art);
            return new ListViewItem(PDL_CB_UseCardID.Checked ? key : CardCatalog.NameOf(card), key) { Tag = card };
        }

        private void PDL_BTN_SavePDL_Click(object sender, EventArgs e)
        {
            if (GameFolderFiles.Current is not { } files || !_limitsOpen)
                return;
            try
            {
                files.Write(Limits.GamePath, Limits.ToBytes());
                SetStatus($"Saved {Limits.GamePath} into {files.Describe(Limits.GamePath)}.");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, ex.Message, "Forbidden & Limited", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PDL_CB_LoadImages_CheckedChanged(object sender, EventArgs e)
        {
            if (_limitsOpen)
                FillLimits();
        }

        private void PDL_CB_UseCardID_CheckedChanged(object sender, EventArgs e)
        {
            if (_limitsOpen)
                FillLimits();
        }

        private void PDL_LV_ItemSelectionChanged(object sender, EventArgs e) =>
            PDL_BTN_RemoveCardFromList.Enabled = LimitLists.Any(l => l.List.SelectedItems.Count > 0);

        private void PDL_BTN_RemoveCardFromList_Click(object sender, EventArgs e)
        {
            foreach (var (list, cards, count) in LimitLists)
            {
                foreach (ListViewItem item in list.SelectedItems.Cast<ListViewItem>().ToList())
                {
                    cards.Remove((ushort)item.Tag!);
                    list.Items.Remove(item);
                }
                count.Text = cards.Count.ToString();
            }
        }

        /// <summary>Adds cards (picked by name) to the list on the open tab.</summary>
        private void PDL_BTN_AddCardToList_Click(object sender, EventArgs e)
        {
            var target = tabControl2.SelectedTab?.Text switch
            {
                "Forbidden" => LimitLists[0],
                "Limited" => LimitLists[1],
                _ => LimitLists[2],
            };
            using var picker = new CardPickerDialog(CardCatalog.Get(this), $"Add to {tabControl2.SelectedTab?.Text}", askCopies: false, maxCopies: 1);
            if (picker.ShowDialog(this) != DialogResult.OK)
                return;
            foreach (int id in picker.Result.Select(r => r.Card.Id).Where(id => id is > 0 and <= ushort.MaxValue))
            {
                ushort card = (ushort)id;
                if (LimitLists.Any(l => l.Cards.Contains(card)))
                    continue;   // a card is on one list only
                target.Cards.Add(card);
                target.List.Items.Add(LimitItem(card));
            }
            target.Count.Text = target.Cards.Count.ToString();
        }
    }
}
