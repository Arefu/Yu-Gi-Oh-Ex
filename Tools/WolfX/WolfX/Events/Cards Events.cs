using CARD_Named;
using CARD_Kana;
using CARD_PackID;
using CARD_Pass;
using CARD_Same;
using Types;


namespace WolfX
{
    public partial class WolfUI
    {
        private int PREVIOUS_INDEX = -1;


        private void CARDS_BTN_OpenCards_Click(object sender, EventArgs e)
        {
            var langChar = State.Language.ToString()[0];
            var file = Utility.Get_FilePath(
                "Open Cards Indx File",
                $"{State.Language} Card Indx File|CARD_Indx_{langChar}.bin|All Indx Files (*.bin)|*.bin",
                $"{State.Path}\\bin\\CARD_Indx_{langChar}.bin"
            );

            if (!CARDS_Cards.Setup_CardBinder(file, (CARDS_INFO.CARD_Language)State.Language))
            {
                MessageBox.Show("Failed to Setup Card Binder\nCheck Yu-Gi-Oh-Ex Wiki!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (CARDS_CB_LoadCards.Checked)
            {
                file = Utility.Get_FilePath(
                    "Open ZIB Archive",
                    "ZIB Archive (*.zib)|*.zib",
                    $"{State.Path}\\2020.full.illust_j.jpg.zib"
                );

                ZIB.Load(file);
            }

            CARDS_Cards.LoadCardInfo();
            CARDS_Cards.LoadCardProps();

            Card_Same.Load(Utility.Get_FilePath("Open CARD_Same.bin", "BIN File (*.bin)|*.bin", $"{State.Path}\\bin\\CARD_Same.bin"));
            CARD_Named.CARD_Named.Load(Utility.Get_FilePath("Open CARD_Named.bin", "BIN File (*.bin)|*.bin", $"{State.Path}\\bin\\CARD_Named.bin"));
            CARD_Pass.CARD_Pass.Load(Utility.Get_FilePath("Open CARD_Pass.bin", "BIN File (*.bin)|*.bin", $"{State.Path}\\bin\\CARD_Pass.bin"));
            CARD_Kana.CARD_Kana.Load(Utility.Get_FilePath("Open CARD_Kana.bin", "BIN File (*.bin)|*.bin", $"{State.Path}\\bin\\CARD_Kana1_{State.Language.ToString()[0]}.bin"), State.Language.ToString());
            CARD_PackID.CARD_PackID.Load(Utility.Get_FilePath("", "BIN File (*.bin)|*.bin", $"{State.Path}\\bin\\CARD_PackID.bin"));

            CARDS_CB_SimilarCardName.DisplayMember = "Key";
            CARDS_CB_SimilarCardName.ValueMember = "Value";
            CARDS_CB_SimilarCardName.DataSource = CARDS_Cards.Cards.Select(card => new KeyValuePair<string, int>($"{card.Name} ({card.ID})", card.ID)).ToList();
            CARDS_CB_SimilarCardName.AutoCompleteSource = AutoCompleteSource.ListItems;
            CARDS_CB_CardID.DataSource = CARDS_Cards.Cards.Select(Select => Select.ID).ToList();
            CARDS_CB_CardTypes.DataSource = CARDS_Cards.Cards.Select(Select => Select.Kind).Distinct().ToList();
            CARDS_CB_CardAttribute.DataSource = CARDS_Cards.Cards.Select(Select => Select.Attribute).Distinct().ToList();
        }
        private void CARDS_BTN_SaveCard_Click(object sender, EventArgs e)
        {
            CARDS_Cards.SaveCardProps();
            CARDS_Cards.SaveCardInfo();

            Card_Same.Save();

            CARD_Pass.CARD_Pass.Save();

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
                CARDS_UpdateInternalListWithProperties();

            if (!int.TryParse(CARDS_CB_CardID.Text, out int selectedCardID))
                return;

            var selectedCard = CARDS_Cards.Cards.FirstOrDefault(card => card.ID == selectedCardID);
            if (selectedCard == null)
                return;

            // Update card display fields
            CARDS_TB_CardName.Text = selectedCard.Name;
            CARDS_CB_CardTypes.Text = selectedCard.Kind.ToString();
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

            if (CARDS_CB_SimilarCardName.DataSource is List<KeyValuePair<string, int>> similarCardList)
            {
                if (similarCardEntry != null)
                {
                    var selectedKvp = similarCardList.FirstOrDefault(kvp => kvp.Value == similarCardEntry.TargetCard);
                    CARDS_CB_SimilarCardName.SelectedItem = !selectedKvp.Equals(default)
                        ? selectedKvp
                        : similarCardList.Count > 0 ? similarCardList[0] : null;
                }
                else
                {
                    CARDS_CB_SimilarCardName.SelectedIndex = -1;
                }
            }
            else
            {
                CARDS_CB_SimilarCardName.SelectedIndex = -1;
            }

            // Update additional fields
            int index = CARDS_CB_CardID.SelectedIndex;
            CARDS_TB_CardPassword.Text = CARD_Pass.CARD_Pass._Passwords.ElementAt(index).ToString();
            CARDS_TB_Kana.Text = CARD_Kana.CARD_Kana._Kana.ElementAt(index).ToString();
            CARDS_TB_CardNumber.Text = CARD_PackID.CARD_PackID._CardNumbers.ElementAt(index).ToString();

            PREVIOUS_INDEX = index;
        }

        private void CARDS_BTN_CloseBinder_Click(object sender, EventArgs e)
        {
            CARDS_Cards.Close_CardBinder();

            CARDS_CB_CardAttribute.DataSource = null;
            CARDS_CB_CardID.DataSource = null;
            CARDS_TB_CardDesc.Clear();
            CARDS_CB_CardTypes.DataSource = null;
            CARDS_Nud_CardLevel.ResetText();
            CARDS_PB_CardPicture.Image = null;

            CARDS_TB_CardName.Clear();
            CARDS_TB_Kana.Clear();
            CARDS_RB_AlwaysSimilar.Checked = false;
            CARDS_RB_SimilarOnEffect.Checked = false;
            CARDS_TB_CardPassword.Clear();

            CARDS_TB_CardNumber.Clear();
        }
        private void CARDS_UpdateInternalListWithProperties()
        {
            var previousCard = CARDS_Cards.Cards.FirstOrDefault(card => card.ID == Convert.ToInt32(CARDS_CB_CardID.Items[PREVIOUS_INDEX]));
            if (previousCard != null)
            {
                previousCard.ID = int.Parse(CARDS_CB_CardID.Items[PREVIOUS_INDEX].ToString());

                if (string.IsNullOrEmpty(CARDS_CB_CardTypes.Text) == false)
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
        private void CARDS_CB_SimilarCardName_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_SimilarCardName.SelectedItem is KeyValuePair<string, int> selectedKVP &&
                CARDS_CB_CardID.SelectedItem is int selectedCardID)
            {
                var target = Card_Same._SimilarCards.FirstOrDefault(card => card.PrimaryCard == selectedCardID);
                if (target != null)
                    target.TargetCard = Convert.ToInt16(selectedKVP.Value);
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

                if (index >= 0 && index < CARD_Pass.CARD_Pass._Passwords.Count)
                {
                    CARD_Pass.CARD_Pass._Passwords[index] = newPassword;
                }
            }
        }

        private void CARDS_TB_Kana_TextChanged(object sender, EventArgs e)
        {
            if (CARDS_TB_Kana.Text is string Kana)
            {
                int index = CARDS_CB_CardID.SelectedIndex;
                if (index >= 0 && index < CARD_Pass.CARD_Pass._Passwords.Count)
                {
                    CARD_Kana.CARD_Kana._Kana[index] = Kana;
                }
            }
        }
    }
}
