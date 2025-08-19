using Types;
using WolfX.WolfX.File_Type_UI;

namespace WolfX
{
    public partial class WolfUI
    {
        private void YDC_BTN_OpenDeck_Click(object sender, EventArgs e)
        {
            var Images = new ImageList();
            if (YDC_CB_LoadPictures.Checked)
            {
                Images.ImageSize = new Size(64, 64);
                YDC_LV_MainDeckCards.View = View.LargeIcon;
                YDC_LV_MainDeckCards.LargeImageList = Images;
            }

            using var OpenFile = new OpenFileDialog();
            OpenFile.Filter = "YDC Files|*.ydc";
            OpenFile.Title = "Select a YDC File";
            if (OpenFile.ShowDialog() != DialogResult.OK)
            {
                MessageBox.Show("No File Selected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            YDC.Load(OpenFile.FileName);
            YDC_TB_DeckName.Text = OpenFile.SafeFileName;
            YDC_LV_MainDeckCards.Items.Clear();
            YDC_LBL_NumOfCardInMain.Text = YDC.NumberOfCardsInDeck.ToString();
            YDC_LBL_NumOfCardsInSide.Text = YDC.NumberOfCardsInSideDeck.ToString();
            YDC_LBL_NumOfCardsInExtra.Text = YDC.NumberOfCardsInExtraDeck.ToString();

            foreach (var cardId in YDC.CardsInMainDeck)
            {
                if (YDC_CB_LoadPictures.Checked)
                    Images.Images.Add(cardId.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{cardId}.jpg")));

                var text = YDC_CB_UseCardID.Checked ? cardId.ToString() : CARDS_Cards.Get_CardNameFromID(cardId);

                var item = new ListViewItem(text)
                {
                    Name = cardId.ToString(),
                    Tag = cardId
                };

                if (YDC_CB_LoadPictures.Checked)
                    item.ImageKey = cardId.ToString();

                YDC_LV_MainDeckCards.Items.Add(item);
            }

            foreach (var cardId in YDC.CardsInSideDeck)
            {
                if (YDC_CB_LoadPictures.Checked)
                    Images.Images.Add(cardId.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{cardId}.jpg")));

                var text = YDC_CB_UseCardID.Checked ? cardId.ToString() : CARDS_Cards.Get_CardNameFromID(cardId);

                var item = new ListViewItem(text)
                {
                    Name = cardId.ToString(),
                    Tag = cardId
                };

                if (YDC_CB_LoadPictures.Checked)
                    item.ImageKey = cardId.ToString();

                YDC_LV_SideDeckCards.Items.Add(item);
            }

            foreach (var cardId in YDC.CardsInExtraDeck)
            {
                if (YDC_CB_LoadPictures.Checked)
                    Images.Images.Add(cardId.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{cardId}.jpg")));

                var text = YDC_CB_UseCardID.Checked ? cardId.ToString() : CARDS_Cards.Get_CardNameFromID(cardId);

                var item = new ListViewItem(text)
                {
                    Name = cardId.ToString(),
                    Tag = cardId
                };

                if (YDC_CB_LoadPictures.Checked)
                    item.ImageKey = cardId.ToString();

                YDC_LV_ExtraDeckCards.Items.Add(item);
            }

            YDC_BTN_AddCard.Enabled = true;
            YDC_BTN_RemoveCard.Enabled = true;
            YDC_BTN_ReplaceCard.Enabled = true;
        }

        private void YDC_BTN_SaveDeck_Click(object sender, EventArgs e)
        {
            YDC.Save();
        }

        private void YDC_CHKBOX_UseCardID_CheckedChanged(object sender, EventArgs e)
        {
            if (!YDC_CB_UseCardID.Checked)
            {
                if (State.Path != null)
                {
                    CARDS_Cards.Setup_CardBinder($"{State.Path}\\bin\\CARD_Indx_{State.Language.ToString()[0]}.bin", (CARDS_INFO.CARD_Language)State.Language);
                    CARDS_Cards.LoadCardInfo();
                    CARDS_Cards.LoadCardProps();
                }
                else
                {
                    using var OpenFile = new OpenFileDialog();
                    OpenFile.Filter = $"{State.Language} Card Indx File|CarD_Indx_{State.Language.ToString()[0]}.bin|All Indx Files (*.bin)|*.bin";
                    OpenFile.Title = "Open Cards Indx File";
                    OpenFile.Multiselect = false;
                    OpenFile.InitialDirectory = State.Path;
                    var Res = OpenFile.ShowDialog();
                    if (Res != DialogResult.OK || !OpenFile.SafeFileName.StartsWith("CARD_Indx"))
                    {
                        MessageBox.Show("Please Select a Cards Indx File", "No Cards Indx File Selected", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    if (!CARDS_Cards.Setup_CardBinder(OpenFile.FileName, (CARDS_INFO.CARD_Language)State.Language))
                    {
                        MessageBox.Show("Failed to Setup Card Binder\nCheck Yu-Gi-Oh-Ex Wiki!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    CARDS_Cards.LoadCardInfo();
                    CARDS_Cards.LoadCardProps();
                }
            }
        }

        private void YDC_CHKBOX_LoadPictures_CheckedChanged(object sender, EventArgs e)
        {
            if (YDC_CB_LoadPictures.Checked)
            {
                if (State.Path != null)
                {
                    ZIB.Load($"{State.Path}\\2020.full.illust_j.jpg.zib");
                    YDC_LV_MainDeckCards.View = View.LargeIcon;
                }
                else
                {
                    using (var OpenDialog = new OpenFileDialog())
                    {
                        OpenDialog.Title = "Open ZIB Archive";
                        OpenDialog.Filter = "ZIB Archive (*.zib)|*.zib";
                        if (OpenDialog.ShowDialog() != DialogResult.OK)
                        {
                            MessageBox.Show("No ZIB Archive Selected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                            return;
                        }
                        ZIB.Load(OpenDialog.FileName);
                    }
                }
            }
            else
            {
                YDC_LV_MainDeckCards.View = View.List;
            }
        }

        private void YDC_BTN_AddCard_Click(object sender, EventArgs e)
        {
            if (YDC_CB_UseSimpleEditor.Checked)
            {
                var SimpleAdder = new SimpleCardAdd();
                var Result = SimpleAdder.ShowDialog();
                if (Result != DialogResult.OK) return;

                var deckName = YDC_TC_CardsInDeck.SelectedTab?.Text ?? string.Empty;
                if (string.IsNullOrEmpty(deckName))
                    return;

                List<short> targetDeck;
                ListView targetListView;

                switch (deckName)
                {
                    case "Main Deck":
                        targetDeck = YDC.CardsInMainDeck;
                        targetListView = YDC_LV_MainDeckCards;
                        break;

                    case "Side Deck":
                        targetDeck = YDC.CardsInSideDeck;
                        targetListView = YDC_LV_SideDeckCards;
                        break;

                    case "Extra Deck":
                        targetDeck = YDC.CardsInExtraDeck;
                        targetListView = YDC_LV_ExtraDeckCards;
                        break;

                    default:
                        return; // unknown deck type
                }

                foreach (var card in SimpleAdder.CardIDs)
                {
                    var cardKey = card.ToString();

                    if (YDC_CB_LoadPictures.Checked)
                        Images.Images.Add(cardKey, Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{card}.jpg")));

                    targetListView.Items.Add(
                        YDC_CB_UseCardID.Checked ? cardKey : CARDS_Cards.Get_CardNameFromID((short)card),
                        cardKey
                    );

                    targetDeck.Add((short)card);
                }

                if (deckName == "Main Deck")
                    YDC_LBL_NumOfCardInMain.Text = YDC.CardsInMainDeck.Count.ToString();
            }
        }

        private void YDC_BTN_ReplaceCard_Click(object sender, EventArgs e)
        {
            if (YDC_LV_MainDeckCards.SelectedItems.Count == 0)
            {
                MessageBox.Show("No Card Selected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (YDC_CB_UseSimpleEditor.Checked)
            {
                var SimpleAdder = new SimpleCardAdd();
                var Result = SimpleAdder.ShowDialog();
            }
        }

        private void YDC_BTN_RemoveCard_Click(object sender, EventArgs e)
        {
            if (YDC_LV_MainDeckCards.SelectedItems.Count == 0)
            {
                MessageBox.Show("No Card Selected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var deckName = YDC_TC_CardsInDeck.SelectedTab?.Text ?? string.Empty;
            if (string.IsNullOrEmpty(deckName))
                return;

            List<short> targetDeck;
            ListView targetListView;

            switch (deckName)
            {
                case "Main Deck":
                    targetDeck = YDC.CardsInMainDeck;
                    targetListView = YDC_LV_MainDeckCards;
                    break;

                case "Side Deck":
                    targetDeck = YDC.CardsInSideDeck;
                    targetListView = YDC_LV_SideDeckCards;
                    break;

                case "Extra Deck":
                    targetDeck = YDC.CardsInExtraDeck;
                    targetListView = YDC_LV_ExtraDeckCards;
                    break;

                default:
                    return; // unknown deck type
            }

            foreach (ListViewItem item in targetListView.SelectedItems.Cast<ListViewItem>().ToList())
            {
                targetListView.Items.Remove(item);
                short selectedItemId = Convert.ToInt16(item.Tag ?? item.Text);

                if (deckName == "Main Deck")
                    YDC.CardsInMainDeck.Remove(selectedItemId);
            }

            YDC_LBL_NumOfCardInMain.Text = YDC_LV_MainDeckCards.Items.Count.ToString();
        }
    }
}