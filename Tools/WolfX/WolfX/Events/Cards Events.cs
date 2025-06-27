using CARD_Same;
using Types;


namespace WolfX
{
    public partial class WolfUI
    {
        private int PREVIOUS_INDEX = -1;
        private List<object> _allCardItems;

        private void CARDS_BTN_OpenCards_Click(object sender, EventArgs e)
        {
            var File = String.Empty;
            if (State.Path == null || State.Path == string.Empty)
                File = Utility.Get_UserSelectedFile("Open Cards Indx File", $"{State.Language} Card Indx File|CARD_Indx_{State.Language.ToString()[0]}.bin|All Indx Files (*.bin)|*.bin");
            else
                File = $"{State.Path}\\bin\\CARD_Indx_{State.Language.ToString()[0]}.bin";

            if (CARDS_Cards.Setup_CardBinder(File) == false)
            {
                MessageBox.Show("Failed to Setup Card Binder\nCheck Yu-Gi-Oh-Ex Wiki!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (CARDS_CB_LoadCards.Checked == true)
            {


                if (State.Path == null || State.Path == string.Empty)
                    File = Utility.Get_UserSelectedFile("Open ZIB Archive", "ZIB Archive (*.zib)|*.zib");
                else
                    File = $"{State.Path}\\2020.full.illust_j.jpg.zib";


                ZIB.Load(File);
            } 
            CARDS_Cards.LoadCardInfo();
            CARDS_Cards.LoadCardProps();

            if (State.Path == null || State.Path == string.Empty)
                File = Utility.Get_UserSelectedFile("Open CARD_Same.bin", "BIN File (*.bin)|*.bin");
            else
                File = $"{State.Path}\\bin\\CARD_Same.bin";

            Card_Same.Load(File);

          //  CARDS_CB_CardName.DataSource = CARDS_Cards.Cards.Select(Select => Select.Name).ToList();
            CARDS_CB_CardID.DataSource = CARDS_Cards.Cards.Select(Select => Select.ID).ToList();
            CARDS_CB_CardTypes.DataSource = CARDS_Cards.Cards.Select(Select => Select.Kind).Distinct().ToList();
            CARDS_CB_CardAttribute.DataSource = CARDS_Cards.Cards.Select(Select => Select.Attribute).Distinct().ToList();
            CARDS_CB_SimilarID.DataSource = CARDS_Cards.Cards.Select(Select => Select.ID).ToList();
        }
        private void CARDS_CB_CardName_TextChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_CardID.SelectedItem is int cardId)
            {
                var card = CARDS_Cards.Cards.FirstOrDefault(c => c.ID == cardId);
                card?.Name = CARDS_TB_CardName.Text;
            }
        }
        private void CARDS_Nud_CardLevel_ValueChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_CardID.SelectedItem is int cardId)
            {
                var card = CARDS_Cards.Cards.FirstOrDefault(c => c.ID == cardId);
                card?.Level = Convert.ToInt32(CARDS_Nud_CardLevel.Value);
            }
        }
        private void CARDS_CB_CardTypes_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_CardID.SelectedItem is int cardId)
            {
                var card = CARDS_Cards.Cards.FirstOrDefault(c => c.ID == cardId);
                card?.Kind = (CARDS_INFO.CARD_Kind)Enum.Parse(typeof(CARDS_INFO.CARD_Kind), CARDS_CB_CardTypes.Text);
            }
        }
        private void CARDS_TB_CardDesc_TextChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_CardID.SelectedItem is int cardId)
            {
                var card = CARDS_Cards.Cards.FirstOrDefault(c => c.ID == cardId);
                card?.Desc = CARDS_TB_CardDesc.Text;
            }
        }
        private void TB_CardAtk_TextChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_CardID.SelectedItem is int cardId)
            {
                var card = CARDS_Cards.Cards.FirstOrDefault(c => c.ID == cardId);
                card?.Attack = Convert.ToInt32(CARDS_TB_CardAtk.Text);
            }
        }
        private void TB_CardDef_TextChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_CardID.SelectedItem is int cardId)
            {
                var card = CARDS_Cards.Cards.FirstOrDefault(c => c.ID == cardId);
                card?.Defense = Convert.ToInt32(CARDS_TB_CardDef.Text);
            }
        }
        private void CARDS_CB_CardAttribute_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(CARDS_CB_CardAttribute.Text))
                return;

            if (CARDS_CB_CardID.SelectedItem is int cardId)
            {
                var card = CARDS_Cards.Cards.FirstOrDefault(c => c.ID == cardId);
                card?.Attribute = (CARDS_INFO.CARD_Attribute)Enum.Parse(typeof(CARDS_INFO.CARD_Attribute), CARDS_CB_CardAttribute.Text);
                foreach (var carder in CARDS_Cards.Cards)
                {
                    if (carder.ID == 4844)
                        card.Type = CARDS_INFO.CARD_Type.TunerEffect;
                }
            }
        }
        private void CARDS_CB_CardID_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (CARDS_CB_CardID.SelectedIndex == -1)
                return;

            if (PREVIOUS_INDEX != -1)
            {
                CARDS_UpdateInternalListWithProperties();
            }


            var SelectedCard = CARDS_Cards.Cards.Where(Card => Card.ID == int.Parse(CARDS_CB_CardID.Text)).FirstOrDefault();
            if (SelectedCard == null)
                return;

            CARDS_TB_CardName.Text = SelectedCard.Name;
            CARDS_CB_CardTypes.Text = SelectedCard.Kind.ToString();
            CARDS_CB_CardAttribute.Text = SelectedCard.Attribute.ToString();
            CARDS_Nud_CardLevel.Text = SelectedCard.Level.ToString();
            CARDS_TB_CardDesc.Text = SelectedCard.Desc;

            if (SelectedCard.Attack == -1 && SelectedCard.Defense == -1)
            {
                CARDS_TB_CardAtk.Text = "?";
                CARDS_TB_CardDef.Text = "?";
            }
            else
            {
                CARDS_TB_CardAtk.Text = SelectedCard.Attack.ToString();
                CARDS_TB_CardDef.Text = SelectedCard.Defense.ToString();
            }

            if (CARDS_CB_LoadCards.Checked == true)
            {
                var Obj = ZIB.Get_CardImageFromDefaultArchiveByYDCID(SelectedCard.ID.ToString());
                if (Obj != null)
                    CARDS_PB_CardPicture.Image = Image.FromStream(Obj);
            }
            PREVIOUS_INDEX = CARDS_CB_CardID.SelectedIndex;

            CARDS_CB_SimilarID.Text = Card_Same._SimilarCards.FirstOrDefault(Card => Card.PrimaryCard == Convert.ToInt16(CARDS_CB_CardID.Text))?.TargetCard.ToString() ?? "Not Applicable";
            var Condition = Card_Same._SimilarCards.FirstOrDefault(Card => Card.PrimaryCard == Convert.ToInt16(CARDS_CB_CardID.Text))?.SimilarityType.ToString();

            if (Condition == "ALWAYS")
            {
                CARDS_RB_AlwaysSimilar.Checked = true;
                CARDS_RB_SimilarOnEffect.Checked = false;
            }
            else if (Condition == "EFFECT")
              {
                CARDS_RB_AlwaysSimilar.Checked = false;
                CARDS_RB_SimilarOnEffect.Checked = true;
            }
            else
               {
                CARDS_RB_AlwaysSimilar.Checked = false;
                CARDS_RB_SimilarOnEffect.Checked = false;
            }
        }
        private void CARDS_BTN_CloseBinder_Click(object sender, EventArgs e)
        {
            CARDS_Cards.Close_CardBinder();

            CARDS_CB_CardAttribute.DataSource  = null;
            CARDS_CB_CardID.DataSource = null;
            CARDS_TB_CardDesc.Clear();
            CARDS_CB_CardTypes.DataSource  = null;
            CARDS_Nud_CardLevel.ResetText();
            CARDS_PB_CardPicture.Image = null;
        }
        private void CARDS_BTN_SaveCard_Click(object sender, EventArgs e)
        {
            CARDS_Cards.SaveCardProps();
            CARDS_Cards.SaveCardInfo();
        }
        private void CARDS_UpdateInternalListWithProperties()
        {
            var previousCard = CARDS_Cards.Cards.FirstOrDefault(card => card.ID == Convert.ToInt32(CARDS_CB_CardID.Items[PREVIOUS_INDEX]));
            if (previousCard != null)
            {
                previousCard.ID = int.Parse(CARDS_CB_CardID.Items[PREVIOUS_INDEX].ToString());

                if(string.IsNullOrEmpty(CARDS_CB_CardTypes.Text) == false)
                previousCard.Kind = (CARDS_INFO.CARD_Kind)Enum.Parse(typeof(CARDS_INFO.CARD_Kind), CARDS_CB_CardTypes.Text);

                if (string.IsNullOrEmpty(CARDS_CB_CardAttribute.Text) == false)
                    previousCard.Attribute = (CARDS_INFO.CARD_Attribute)Enum.Parse(typeof(CARDS_INFO.CARD_Attribute), CARDS_CB_CardAttribute.Text);


                if (string.IsNullOrEmpty(CARDS_Nud_CardLevel.Text) == false)
                    previousCard.Level = int.Parse(CARDS_Nud_CardLevel.Text);

                previousCard.Desc = CARDS_TB_CardDesc.Text;

                if (string.IsNullOrEmpty(CARDS_TB_CardAtk.Text) == false)
                    previousCard.Attack = CARDS_TB_CardAtk.Text == "?" ? -1 : int.Parse(CARDS_TB_CardAtk.Text);

                if (string.IsNullOrEmpty(CARDS_TB_CardDef.Text) == false)
                    previousCard.Defense = CARDS_TB_CardDef.Text == "?" ? -1 : int.Parse(CARDS_TB_CardDef.Text);

                var index = CARDS_Cards.Cards.FindIndex(card => card.ID == previousCard.ID);
                if (index != -1)
                {
                    CARDS_Cards.Cards[index] = previousCard;
                }
            }
        }
        private void CARDS_CB_SimilarID_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_SimilarID.Text == "0" || CARDS_CB_SimilarID.Text == "0")
                return;
            if (CARDS_CB_CardID.Text == CARDS_CB_SimilarID.Text)
            {
                MessageBox.Show("You can not related a card to it self!","Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                CARDS_CB_SimilarID.SelectedIndex = -1;
                return;
            }
        }

        private void CARDS_LBL_SimilarID_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (!short.TryParse(CARDS_CB_SimilarID.Text, out short card))
                return;

            CARDS_CB_CardID.SelectedItem = CARDS_CB_CardID.Items.Cast<object>().FirstOrDefault(item => Convert.ToInt32(item) == card);
        }

    }
}
