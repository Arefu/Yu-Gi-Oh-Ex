﻿using Types;

namespace WolfX
{
    public partial class WolfUI
    {
        private int PREVIOUS_INDEX = -1;

        private void CARDS_BTN_OpenCards_Click(object sender, EventArgs e)
        {
            var File = String.Empty;
            if (State.Path == null || State.Path == string.Empty)
                File = Utility.Get_UserSelectedFile($"{State.Language} Card Indx File|CarD_Indx_{State.Language.ToString()[0]}.bin|All Indx Files (*.bin)|*.bin", "Open Cards Indx File");
            else
                File = $"{State.Path}\\bin\\CARD_Indx_{State.Language.ToString()[0]}.bin";

            if (CARDS_Cards.Setup_CardBinder(File) == false)
            {
                MessageBox.Show("Failed to Setup Card Binder\nCheck Yu-Gi-Oh-Ex Wiki!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (State.Path == null || State.Path == string.Empty)
                File = Utility.Get_UserSelectedFile("Open ZIB Archive", "ZIB Archive (*.zib)|*.zib");
            else
                File = $"{State.Path}\\2020.full.illust_j.jpg.zib";


            ZIB.Load(File);

            CARDS_Cards.LoadCardInfo();
            CARDS_Cards.LoadCardProps();

            CARDS_CB_CardName.DataSource = CARDS_Cards.Cards.Select(Select => Select.Name).ToList();
            CARDS_CB_CardID.DataSource = CARDS_Cards.Cards.Select(Select => Select.ID).ToList();
            CARDS_CB_CardTypes.DataSource = CARDS_Cards.Cards.Select(Select => Select.Type).Distinct().ToList();
            CARDS_CB_CardAttribute.DataSource = CARDS_Cards.Cards.Select(Select => Select.Attribute).Distinct().ToList();
        }
        private void CARDS_CB_CardName_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_CardName.SelectedIndex == -1)
                return;

            if (PREVIOUS_INDEX != -1)
            {
                CARDS_UpdateInternalListWithProperties();
            }

            CARDS_CB_CardID.SelectedIndexChanged -= CARDS_CB_CardID_SelectedIndexChanged;

            var SelectedCard = CARDS_Cards.Cards.Where(Card => Card.Name == CARDS_CB_CardName.Text).First();
            CARDS_CB_CardID.Text = SelectedCard.ID.ToString();
            CARDS_CB_CardTypes.Text = SelectedCard.Type.ToString();
            CARDS_CB_CardAttribute.Text = SelectedCard.Attribute.ToString();
            CARDS_Nud_CardLevel.Text = SelectedCard.Level.ToString();
            CARDS_TB_CardDesc.Text = SelectedCard.Desc;

            if (SelectedCard.Attack == -1 && SelectedCard.Defense == -1)
            {
                TB_CardAtk.Text = "?";
                TB_CardDef.Text = "?";
            }
            else
            {
                TB_CardAtk.Text = SelectedCard.Attack.ToString();
                TB_CardDef.Text = SelectedCard.Defense.ToString();
            }

            var Obj = ZIB.Get_CardImageFromDefaultArchiveByYDCID(SelectedCard.ID.ToString());
            if (Obj != null)
                CARDS_PB_CardPicture.Image = Image.FromStream(Obj);

            CARDS_CB_CardID.SelectedIndexChanged += CARDS_CB_CardID_SelectedIndexChanged;
            PREVIOUS_INDEX = CARDS_CB_CardID.SelectedIndex;
        }
        private void CARDS_CB_CardID_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_CardID.SelectedIndex == -1)
                return;

            if (PREVIOUS_INDEX != -1)
            {
                CARDS_UpdateInternalListWithProperties();
            }

            CARDS_CB_CardName.SelectedIndexChanged -= CARDS_CB_CardName_SelectedIndexChanged;

            var SelectedCard = CARDS_Cards.Cards.Where(Card => Card.ID == int.Parse(CARDS_CB_CardID.Text)).First();
            CARDS_CB_CardName.Text = SelectedCard.Name;
            CARDS_CB_CardTypes.Text = SelectedCard.Type.ToString();
            CARDS_CB_CardAttribute.Text = SelectedCard.Attribute.ToString();
            CARDS_Nud_CardLevel.Text = SelectedCard.Level.ToString();
            CARDS_TB_CardDesc.Text = SelectedCard.Desc;

            textBox1.Text = SelectedCard.First.ToString();
            textBox2.Text = SelectedCard.Second.ToString();

            if (SelectedCard.Attack == -1 && SelectedCard.Defense == -1)
            {
                TB_CardAtk.Text = "?";
                TB_CardDef.Text = "?";
            }
            else
            {
                TB_CardAtk.Text = SelectedCard.Attack.ToString();
                TB_CardDef.Text = SelectedCard.Defense.ToString();
            }

            var Obj = ZIB.Get_CardImageFromDefaultArchiveByYDCID(SelectedCard.ID.ToString());
            if (Obj != null)
                CARDS_PB_CardPicture.Image = Image.FromStream(Obj);

            CARDS_CB_CardName.SelectedIndexChanged += CARDS_CB_CardName_SelectedIndexChanged;
            PREVIOUS_INDEX = CARDS_CB_CardID.SelectedIndex;
        }


        private void CARDS_BTN_CloseBinder_Click(object sender, EventArgs e)
        {
            CARDS_Cards.Close_CardBinder();
        }

        private void CARDS_BTN_SaveCard_Click(object sender, EventArgs e)
        {
            CARDS_Cards.SaveCardProps();
            CARDS_Cards.SaveCardInfo();
        }



        private void CARDS_UpdateInternalListWithProperties()
        {
            var previousCard = CARDS_Cards.Cards.FirstOrDefault(card => card.Name == CARDS_CB_CardName.Items[PREVIOUS_INDEX].ToString());
            if (previousCard != null)
            {
                previousCard.ID = int.Parse(CARDS_CB_CardID.Items[PREVIOUS_INDEX].ToString());
                previousCard.Type = (CARDS_INFO.CARD_Type)Enum.Parse(typeof(CARDS_INFO.CARD_Type), CARDS_CB_CardTypes.Text);
                previousCard.Attribute = (CARDS_INFO.CARD_Attribute)Enum.Parse(typeof(CARDS_INFO.CARD_Attribute), CARDS_CB_CardAttribute.Text);
                previousCard.Level = int.Parse(CARDS_Nud_CardLevel.Text);
                previousCard.Desc = CARDS_TB_CardDesc.Text;
                previousCard.Attack = TB_CardAtk.Text == "?" ? -1 : int.Parse(TB_CardAtk.Text);
                previousCard.Defense = TB_CardDef.Text == "?" ? -1 : int.Parse(TB_CardDef.Text);
            }

            var index = CARDS_Cards.Cards.FindIndex(card => card.ID == previousCard.ID);
            if (index != -1)
            {
                CARDS_Cards.Cards[index] = previousCard;
            }
        }

    }
}
