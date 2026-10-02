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
