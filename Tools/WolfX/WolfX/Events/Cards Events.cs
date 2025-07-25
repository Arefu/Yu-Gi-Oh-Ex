using CARD_Kana;
using CARD_Named;
using CARD_PackID;
using CARD_Pass;
using CARD_Same;
using Types;

namespace WolfX
{
    public partial class WolfUI
    {
        private void CARDS_BTN_OpenCards_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(State.Path))
                WOLFUI_TOOLITEM_LoadGame_Click(sender, e);

            if (!CARDS_Cards.Setup_CardBinder($"{State.Path}\\bin\\CARD_Indx_{State.Language.ToString()[0]}.bin", (CARDS_INFO.CARD_Language)State.Language))
            {
                MessageBox.Show("Failed to Setup Card Binder\nCheck Yu-Gi-Oh-Ex Wiki!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (CARDS_CB_LoadCards.Checked)
                ZIB.Load($"{State.Path}\\2020.full.illust_j.jpg.zib");

            CARDS_Cards.LoadCardInfo();
            CARDS_Cards.LoadCardProps();

            Card_Same.Load($"{State.Path}\\bin\\CARD_Same.bin");
            Card_Named.Load($"{State.Path}\\bin\\CARD_Named.bin");
            Card_Pass.Load($"{State.Path}\\bin\\CARD_Pass.bin");
            Card_Kana.Load($"{State.Path}\\bin\\CARD_Kana1_{State.Language.ToString()[0]}.bin", State.Language.ToString());
            Card_PackID.Load($"{State.Path}\\bin\\CARD_PackID.bin");

            CARDS_CB_SimilarCardName.DisplayMember = "Key";
            CARDS_CB_SimilarCardName.ValueMember = "Value";
            CARDS_CB_SimilarCardName.DataSource = CARDS_Cards.Cards.Select(card => new KeyValuePair<string, int>($"{card.Name} ({card.ID})", card.ID)).ToList();

            CARDS_CB_CardSearcher.DisplayMember = "Key";
            CARDS_CB_CardSearcher.ValueMember = "Value";
            CARDS_CB_CardSearcher.DataSource = CARDS_Cards.Cards.Select(card => new KeyValuePair<string, int>($"{card.Name}", card.ID)).ToList();

            CARDS_CB_CardID.DataSource = CARDS_Cards.Cards.Select(Select => Select.ID).ToList();
            CARDS_CB_CardKind.DataSource = CARDS_Cards.Cards.Select(Select => Select.Kind).Distinct().ToList();
            CARDS_CB_CardAttribute.DataSource = CARDS_Cards.Cards.Select(Select => Select.Attribute).Distinct().ToList();
            CARDS_CB_CardType.DataSource = CARDS_Cards.Cards.Select(Select => Select.Type).Distinct().ToList();
        }

        private void CARDS_BTN_SaveCard_Click(object sender, EventArgs e)
        {
            CARDS_Cards.SaveCardProps();
            CARDS_Cards.SaveCardInfo();

            Card_Same.Save();
            Card_Named.Save();
            Card_Pass.Save();
            Card_Kana.Save(State.Language.ToString()[0]);
            Card_PackID.Save();
        }

        private void CARDS_BTN_CloseBinder_Click(object sender, EventArgs e)
        {
            CARDS_Cards.Close_CardBinder();

            CARDS_CB_CardAttribute.DataSource = null;
            CARDS_CB_CardID.DataSource = null;
            CARDS_TB_CardDesc.Clear();
            CARDS_CB_CardKind.DataSource = null;
            CARDS_Nud_CardLevel.ResetText();
            CARDS_PB_CardPicture.Image = null;

            CARDS_CB_CardSearcher.DataSource = null;
            CARDS_CB_CardSearcher.Items.Clear();
            CARDS_TB_Kana.Clear();
            CARDS_RB_AlwaysSimilar.Checked = false;
            CARDS_RB_SimilarOnEffect.Checked = false;
            CARDS_TB_CardPassword.Clear();

            CARDS_TB_CardNumber.Clear();
        }

        private void CARDS_CB_CardName_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_CardSearcher.SelectedValue != null)
                CARDS_CB_CardID.SelectedIndex = CARDS_CB_CardID.Items.IndexOf((int)CARDS_CB_CardSearcher.SelectedValue);
        }

        private void CARDS_CB_CardID_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (CARDS_CB_CardID.SelectedIndex == -1)
                return;

            if (!int.TryParse(CARDS_CB_CardID.Text, out int selectedCardID))
                return;

            var selectedCard = CARDS_Cards.Cards.FirstOrDefault(card => card.ID == selectedCardID);
            if (selectedCard == null)
                return;

            // Update card display fields
            CARDS_CB_CardSearcher.Text = selectedCard.Name;
            CARDS_TB_CardName.Text = selectedCard.Name;
            CARDS_CB_CardKind.Text = selectedCard.Kind.ToString();
            CARDS_CB_CardType.Text = selectedCard.Type.ToString();
            CARDS_CB_CardAttribute.Text = selectedCard.Attribute.ToString();
            CARDS_Nud_CardLevel.Text = selectedCard.Level.ToString();
            CARDS_TB_CardDesc.Text = selectedCard.Desc;
            CARDS_TB_CardAtk.Text = selectedCard.Attack.ToString();
            CARDS_TB_CardDef.Text = selectedCard.Defense.ToString();

            // Load card image
            if (CARDS_CB_LoadCards.Checked)
            {
                var imageStream = ZIB.Get_CardImageFromDefaultArchiveByYDCID(selectedCard.ID.ToString());
                if (imageStream != null)
                    CARDS_PB_CardPicture.Image = Image.FromStream(imageStream);
            }

            // Update similarity type radio buttons
            var similarityCondition = Card_Same._SimilarCards
                .FirstOrDefault(card => card.PrimaryCard == selectedCardID)?
                .SimilarityType.ToString();

            CARDS_RB_AlwaysSimilar.Checked = similarityCondition == "ALWAYS";
            CARDS_RB_SimilarOnEffect.Checked = similarityCondition == "EFFECT";

            // Update similar card selection
            var similarCardEntry = Card_Same._SimilarCards
                .FirstOrDefault(card => card.PrimaryCard == selectedCardID);

            var SimilarCards = CARDS_CB_SimilarCardName.DataSource as List<KeyValuePair<string, int>>;
            var selectedKvp = SimilarCards?.FirstOrDefault(kvp => kvp.Value == similarCardEntry?.TargetCard);
            CARDS_CB_SimilarCardName.SelectedItem = !selectedKvp.Equals(default) ? selectedKvp : SimilarCards?.Count > 0 ? SimilarCards[0] : null;

            CARDS_TB_CardPassword.Text = Card_Pass._Passwords.ElementAt(CARDS_CB_CardID.SelectedIndex).ToString();
            CARDS_TB_Kana.Text = Card_Kana._Kana.ElementAt(CARDS_CB_CardID.SelectedIndex).ToString();
            CARDS_TB_CardNumber.Text = Card_PackID._CardNumbers.ElementAt(CARDS_CB_CardID.SelectedIndex).ToString();
        }

        #region CARD_EDIT_CHANGE_SAVE_FUNCTIONS

        /// <summary>
        /// Updates the Card's Name in CARD_Props ready for calling Save.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CARDS_TB_CardName_TextChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_CardID.SelectedItem is int cardId)
            {
                var card = CARDS_Cards.Cards.FirstOrDefault(c => c.ID == cardId);
                card?.Name = CARDS_TB_CardName.Text;
            }
        }

        /// <summary>
        /// Updates the Card's Attribute in CARD_Props ready for calling Save.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CARDS_CB_CardAttribute_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(CARDS_CB_CardAttribute.Text))
                return;

            if (CARDS_CB_CardID.SelectedItem is int cardId)
            {
                var card = CARDS_Cards.Cards.FirstOrDefault(c => c.ID == cardId);
                card?.Attribute = (CARDS_INFO.CARD_Attribute)Enum.Parse(typeof(CARDS_INFO.CARD_Attribute), CARDS_CB_CardAttribute.Text);
            }
        }

        private void CARDS_CB_SimilarCardName_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_SimilarCardName.SelectedItem is KeyValuePair<string, int> selectedKVP &&
                CARDS_CB_CardID.SelectedItem is int selectedCardID)
            {
                var target = Card_Same._SimilarCards.FirstOrDefault(card => card.PrimaryCard == selectedCardID);
                target?.TargetCard = Convert.ToInt16(selectedKVP.Value);
            }
        }

        private void CARDS_RB_SimilarOnEffect_CheckedChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_SimilarCardName.SelectedItem is KeyValuePair<string, int> selectedKVP && CARDS_CB_CardID.SelectedItem is int selectedCardID)
            {
                var target = Card_Same._SimilarCards.FirstOrDefault(card => card.PrimaryCard == selectedCardID);
                if (target == null) return;

                if (CARDS_RB_SimilarOnEffect.Checked)
                    target.SimilarityType = TYPE.EFFECT;
            }
        }

        private void CARDS_RB_AlwaysSimilar_CheckedChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_SimilarCardName.SelectedItem is KeyValuePair<string, int> selectedKVP && CARDS_CB_CardID.SelectedItem is int selectedCardID)
            {
                var target = Card_Same._SimilarCards.FirstOrDefault(card => card.PrimaryCard == selectedCardID);
                if (target == null) return;

                if (CARDS_RB_AlwaysSimilar.Checked)
                    target.SimilarityType = TYPE.EFFECT;
            }
        }

        private void CARDS_TB_CardPassword_TextChanged(object sender, EventArgs e)
        {
            if (int.TryParse(CARDS_TB_CardPassword.Text, out int newPassword))
            {
                int index = CARDS_CB_CardID.SelectedIndex;

                if (index >= 0 && index < Card_Pass._Passwords.Count)
                {
                    Card_Pass._Passwords[index] = newPassword;
                }
            }
        }

        private void CARDS_TB_Kana_TextChanged(object sender, EventArgs e)
        {
            if (CARDS_TB_Kana.Text is string Kana)
            {
                int index = CARDS_CB_CardID.SelectedIndex;
                if (index >= 0 && index < Card_Pass._Passwords.Count)
                {
                    Card_Kana._Kana[index] = Kana;
                }
            }
        }

        private void CARDS_CB_CardKind_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_CardID.SelectedItem is int cardId)
            {
                var card = CARDS_Cards.Cards.FirstOrDefault(c => c.ID == cardId);
                card?.Kind = (CARDS_INFO.CARD_Kind)Enum.Parse(typeof(CARDS_INFO.CARD_Kind), CARDS_CB_CardKind.Text);
            }
        }

        private void CARDS_CB_CardType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_CardID.SelectedItem is int cardId)
            {
                var card = CARDS_Cards.Cards.FirstOrDefault(c => c.ID == cardId);
                card?.Type = (CARDS_INFO.CARD_Type)Enum.Parse(typeof(CARDS_INFO.CARD_Type), CARDS_CB_CardType.Text);
            }
        }

        private void CARDS_NUD_CardLevel_ValueChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_CardID.SelectedItem is int cardId)
            {
                var card = CARDS_Cards.Cards.FirstOrDefault(c => c.ID == cardId);
                card?.Level = Convert.ToInt32(CARDS_Nud_CardLevel.Value);
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

        #endregion CARD_EDIT_CHANGE_SAVE_FUNCTIONS
    }
}