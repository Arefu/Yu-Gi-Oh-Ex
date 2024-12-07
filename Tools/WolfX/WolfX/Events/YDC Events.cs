﻿using Types;
using WolfX.Types;
using WolfX.WolfX.File_Type_UI;

namespace WolfX
{
    public partial class WolfUI
    {
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
                    Images.Images.Add(Card.ToString(), Image.FromStream(ZIB.Get_CardImageFromDefaultArchiveByYDCID(Card.ToString())));

                if(YDC_CHKBOX_UseCardID.Checked)
                    YDC_LV_MainDeckCards.Items.Add(Card.ToString(), Card.ToString(), Card.ToString());
                else
                    YDC_LV_MainDeckCards.Items.Add(CARDS_Cards.Get_CardNameFromID(Card), Card.ToString());
            }

            YDC_BTN_AddCard.Enabled = true;
        }
        private void YDC_CHKBOX_UseCardID_CheckedChanged(object sender, EventArgs e)
        {
            if(YDC_CHKBOX_UseCardID.Checked == false)
            {
                CARDS_BTN_OpenCards_Click(0, e);
                YDC_BTN_OpenDeck_Click(0, e);
            }
        }

        private void YDC_CHKBOX_LoadPictures_CheckedChanged(object sender, EventArgs e)
        {
            if (YDC_CHKBOX_LoadPictures.Checked)
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
                YDC_LV_MainDeckCards.View = View.List;
            }

            YDC_BTN_OpenDeck_Click(0, e);
        }

        private void YDC_BTN_AddCard_Click(object sender, EventArgs e)
        {
            Card_Changer C = new Card_Changer();
            C.LoadCards(CARDS_INFO.CARD_Language.English);

            using var OpenZib = new OpenFileDialog();
            OpenZib.Title = "Open ZIB Archive";
            OpenZib.Filter = "ZIB Archive (*.zib)|*.zib";
            if (OpenZib.ShowDialog() != DialogResult.OK)
            {
                MessageBox.Show("No ZIB Archive Selected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            ZIB.Load(OpenZib.FileName);


            if (C.ShowDialog() == DialogResult.OK)
            {
                var Card = CARDS_Cards.Cards.Where(Card => Card.Name == C.Card.Name).First();
                if (Card != null)
                {
                    YDC_LV_MainDeckCards.LargeImageList.Images.Add(Image.FromStream(ZIB.Get_CardImageFromDefaultArchiveByYDCID(Card.ID.ToString())));
                    YDC_LV_MainDeckCards.Items.Add(Card.Name, Card.ID.ToString());
                }
            }
        }
     
    }
}
