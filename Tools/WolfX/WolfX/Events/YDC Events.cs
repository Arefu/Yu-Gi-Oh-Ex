using Types;
using WolfX.WolfX.File_Type_UI;

namespace WolfX
{
    public partial class WolfUI
    {
        Card_Changer C = new Card_Changer();

        private void YDC_BTN_OpenDeck_Click(object sender, EventArgs e)
        {
            var Images = new ImageList();
            if (YDC_CHKBOX_LoadPictures.Checked)
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

            var Cards = YDC.Load(OpenFile.FileName);
            YDC_TB_DeckName.Text = OpenFile.SafeFileName;
            YDC_LV_MainDeckCards.Items.Clear();
            YDC_LBL_NumOfCardInMain.Text = YDC.MAX_NUMBER_OF_CARDS_IN_DECK.ToString();
            foreach (var Card in Cards)
            {
                if (YDC_CHKBOX_LoadPictures.Checked)
                    Images.Images.Add(Card.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{Card}.jpg")));

                if (YDC_CHKBOX_UseCardID.Checked)
                    YDC_LV_MainDeckCards.Items.Add(Card.ToString(), Card.ToString(), Card.ToString());
                else
                    YDC_LV_MainDeckCards.Items.Add(CARDS_Cards.Get_CardNameFromID(Card), Card.ToString());
            }

            YDC_BTN_AddCard.Enabled = true;
            YDC_BTN_RemoveCard.Enabled = true;
            YDC_BTN_ReplaceCard.Enabled = true;
        }

        private void YDC_CHKBOX_UseCardID_CheckedChanged(object sender, EventArgs e)
        {
            if (YDC_CHKBOX_UseCardID.Checked == false)
            {
                if (State.Path != null)
                {
                    CARDS_Cards.Setup_CardBinder($"{State.Path}\\bin\\CARD_Indx_{State.Language.ToString()[0]}.bin", (CARDS_INFO.CARD_Language)State.Language);
                    CARDS_Cards.LoadCardInfo();
                    CARDS_Cards.LoadCardProps();
                }
                else
                {
                    using (var OpenFile = new OpenFileDialog())
                    {
                        OpenFile.Filter = $"{State.Language} Card Indx File|CarD_Indx_{State.Language.ToString()[0]}.bin|All Indx Files (*.bin)|*.bin";
                        OpenFile.Title = "Open Cards Indx File";
                        OpenFile.Multiselect = false;
                        OpenFile.InitialDirectory = State.Path;
                        var Res = OpenFile.ShowDialog();
                        if (Res != DialogResult.OK || OpenFile.SafeFileName.StartsWith("CARD_Indx") != true)
                        {
                            MessageBox.Show("Please Select a Cards Indx File", "No Cards Indx File Selected", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        if (CARDS_Cards.Setup_CardBinder(OpenFile.FileName, (CARDS_INFO.CARD_Language)State.Language) == false)
                        {
                            MessageBox.Show("Failed to Setup Card Binder\nCheck Yu-Gi-Oh-Ex Wiki!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        CARDS_Cards.LoadCardInfo();
                        CARDS_Cards.LoadCardProps();

                    }
                }
            }
        }

        private void YDC_CHKBOX_LoadPictures_CheckedChanged(object sender, EventArgs e)
        {
            if (YDC_CHKBOX_LoadPictures.Checked)
            {
                if (State.Path != null)
                {
                    CARDS_Cards.Setup_CardBinder($"{State.Path}\\bin\\CARD_Indx_{State.Language.ToString()[0]}.bin", (CARDS_INFO.CARD_Language)State.Language);
                    CARDS_Cards.LoadCardInfo();
                    CARDS_Cards.LoadCardProps();
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
            if (State.Path == null && YDC_CHKBOX_LoadPictures.Checked == true)
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
            else
            {
                if (C.HasLoaded == false)
                    C.LoadCards((CARDS_INFO.CARD_Language)State.Language, State.Path);
            }

            if (C.ShowDialog() == DialogResult.OK)
            {
                var Card = CARDS_Cards.Cards.Where(Card => Card.Name == C.Card.Name).First();
                if (Card != null)
                {
                    if (YDC_CHKBOX_UseCardID.Checked)
                    {
                        YDC_LV_MainDeckCards.Items.Add(Card.ID.ToString(), Card.ID.ToString(), Card.ID.ToString());
                    }
                    else
                    {
                        YDC_LV_MainDeckCards.Items.Add(Card.Name, Card.ID.ToString());
                    }

                    YDC_LV_MainDeckCards.Refresh();
                }
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
            { }

            if (State.Path != null && State.Path != String.Empty)
            {
                C.LoadCards((CARDS_INFO.CARD_Language)State.Language, State.Path);
            }
            if (C.ShowDialog() == DialogResult.OK)
            {
                var Card = CARDS_Cards.Cards.Where(Card => Card.Name == C.Card.Name).First();
                if (Card != null)
                {
                    if (YDC_CHKBOX_UseCardID.Checked)
                        YDC_LV_MainDeckCards.Items[YDC_LV_MainDeckCards.SelectedItems[0].Index].Text = Card.ID.ToString();
                    else
                        YDC_LV_MainDeckCards.Items[YDC_LV_MainDeckCards.SelectedItems[0].Index].Text = Card.Name;


                    YDC_LV_MainDeckCards.Refresh();
                }
            }
        }

        private void YDC_BTN_RemoveCard_Click(object sender, EventArgs e)
        {
            if (YDC_LV_MainDeckCards.SelectedItems.Count == 0)
            {
                MessageBox.Show("No Card Selected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            YDC_LV_MainDeckCards.Items.Remove(YDC_LV_MainDeckCards.SelectedItems[0]);

            YDC_LBL_NumOfCardInMain.Text = YDC_LV_MainDeckCards.Items.Count.ToString();
        }
    }
}
