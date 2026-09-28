using System.IO;
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
            Card_Named.Load($"{State.Path}\\bin\\Card_named.bin");
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
            foreach (var combo in ArchetypeCombos())
            {
                combo.DisplayMember = nameof(ArchetypeChoice.Text);
                combo.ValueMember = nameof(ArchetypeChoice.Code);
                combo.DataSource = ArchetypeChoices();
            }
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

        // Names the Yu-Gi-Oh-Cards plugin understands in cards.json; any other value is written as the raw number.
        private static readonly HashSet<string> JsonKinds = new(Enum.GetNames<CARDS_INFO.CARD_Kind>().Where(n => n != "Default"), StringComparer.OrdinalIgnoreCase);   // every kind the game has; the plugin reads the names
        private static readonly HashSet<string> JsonTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Dragon", "Zombie", "Fiend", "Pyro", "SeaSerpent", "Rock", "Machine", "Fish", "Dinosaur", "Insect", "Beast", "BeastWarrior",
            "Plant", "Aqua", "Warrior", "WingedBeast", "Fairy", "Spellcaster", "Thunder", "Reptile", "Psychic", "Wyrm", "Cyberse",
            "DivineBeast", "Spell", "Trap",
        };
        private static readonly string[] JsonIcons = ["Normal", "Counter", "Field", "Equip", "Continuous", "QuickPlay", "Ritual"];

        private static object JsonEnum<T>(T value, HashSet<string> known) where T : struct, Enum
        {
            string name = value.ToString();
            return known.Contains(name) ? name : Convert.ToInt32(value);
        }

        /// <summary>Writes every card to cards.json in the form the Yu-Gi-Oh-Cards plugin reads. A card whose id the game already has overwrites that card.</summary>
        private void CARDS_BTN_ExportJson_Click(object sender, EventArgs e)
        {
            if (CARDS_Cards.Cards.Count == 0)
            {
                MessageBox.Show("Open the cards first (Open Cards).", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new SaveFileDialog { FileName = "cards.json", Filter = "cards.json|*.json", Title = "Export cards" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var cards = new List<Dictionary<string, object>>();
            for (int i = 0; i < CARDS_Cards.Cards.Count; i++)
            {
                var card = CARDS_Cards.Cards[i];
                var entry = new Dictionary<string, object>
                {
                    ["id"] = card.ID,
                    ["name"] = card.Name ?? "",
                    ["description"] = card.Desc ?? "",
                    ["kind"] = JsonEnum(card.Kind, JsonKinds),
                    ["attribute"] = card.Attribute == CARDS_INFO.CARD_Attribute.Unknown ? 0 : card.Attribute.ToString().Replace("Monster", ""),
                    ["type"] = JsonEnum(card.Type, JsonTypes),
                    ["level"] = card.Level,
                };
                if (card.Kind is CARDS_INFO.CARD_Kind.Spell or CARDS_INFO.CARD_Kind.Trap)   // only Spells and Traps have an icon (Continuous, Quick-Play...)
                    entry["icon"] = card.Ico >= 0 && card.Ico < JsonIcons.Length ? JsonIcons[card.Ico] : card.Ico;
                if (i < Card_Pass._Passwords.Count && Card_Pass._Passwords[i] != 0)   // the card's real passcode: match it to downloaded card data to know the card exists in the game
                    entry["password"] = Card_Pass._Passwords[i];
                if (card.Attack >= 0) entry["atk"] = card.Attack;   // -1 is "?" in the game data
                if (card.Defense >= 0) entry["def"] = card.Defense;
                cards.Add(entry);
            }

            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(dialog.FileName, System.Text.Json.JsonSerializer.Serialize(new { cards }, options), new System.Text.UTF8Encoding(false));
            MessageBox.Show($"Exported {cards.Count} cards to {dialog.FileName}", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            CARDS_CB_CardType.DataSource = null;
            CARDS_CB_CardType.Items.Clear();
            CARDS_CB_CardAttribute.DataSource = null;
            CARDS_CB_CardAttribute.Items.Clear();
            CARDS_CB_CardSearcher.DataSource = null;
            CARDS_CB_CardSearcher.Items.Clear();
            CARDS_TB_Kana.Clear();
            CARDS_RB_AlwaysSimilar.Checked = false;
            CARDS_RB_SimilarOnEffect.Checked = false;
            CARDS_TB_CardPassword.Clear();

            CARDS_TB_CardNumber.Clear();

            CARDS_CB_SimilarCardName.DataSource = null;
            CARDS_CB_SimilarCardName.Items.Clear();

            var archetypeCombos = new ComboBox[]
          {
                CARDS_CB_CardArchetypeNumberOne,
                CARDS_CB_CardArchetypeNumberTwo,
                CARDS_CB_CardArchetypeNumberThree,
                CARDS_CB_CardArchetypeNumberFour,
                CARDS_CB_CardArchetypeNumberFive,
                CARDS_CB_CardArchetypeNumberSix
          };
            for (int i = 0; i < archetypeCombos.Length; i++)
            {
                archetypeCombos[i].DataSource = null;
                archetypeCombos[i].Items.Clear();
            }
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
                else
                    CARDS_PB_CardPicture.Image = null;
            }

            // Update similarity type radio buttons
            var similarityCondition = Card_Same._SimilarCards
                .FirstOrDefault(card => card.PrimaryCard == selectedCardID)?
                .SimilarityType.ToString();

            CARDS_RB_AlwaysSimilar.Checked = similarityCondition == "ALWAYS";
            CARDS_RB_SimilarOnEffect.Checked = similarityCondition == "EFFECT";

            // Update similar card selection
            var similarCardEntry = Card_Same._SimilarCards.FirstOrDefault(card => card.PrimaryCard == selectedCardID);

            var similarCards = CARDS_CB_SimilarCardName.DataSource as List<KeyValuePair<string, int>>;

            KeyValuePair<string, int> selectedKvp = default;

            if (similarCardEntry != null && similarCards != null)
            {
                selectedKvp = similarCards.FirstOrDefault(kvp => kvp.Value == similarCardEntry.TargetCard);
            }

            if (!selectedKvp.Equals(default(KeyValuePair<string, int>)))
            {
                CARDS_CB_SimilarCardName.SelectedItem = selectedKvp;
            }
            else if (similarCards != null && similarCards.Count > 0)
            {
                CARDS_CB_SimilarCardName.SelectedItem = similarCards[0];
            }
            else
            {
                CARDS_CB_SimilarCardName.SelectedIndex = -1;
            }

            CARDS_TB_CardPassword.Text = Card_Pass._Passwords.ElementAt(CARDS_CB_CardID.SelectedIndex).ToString();
            CARDS_TB_Kana.Text = Card_Kana._Kana.ElementAt(CARDS_CB_CardID.SelectedIndex).ToString();
            CARDS_TB_CardNumber.Text = Card_PackID._CardNumbers.ElementAt(CARDS_CB_CardID.SelectedIndex).ToString();

            ShowArchetypes(Convert.ToInt32(CARDS_CB_CardID.Text));
        }

        #region CARD_EDIT_CHANGE_SAVE_FUNCTIONS

        // ---- archetypes -------------------------------------------------------------------------------------------------
        // A card can be in any number of archetypes: each of the six boxes holds one, "(none)" leaves it empty. A card in more
        // than six keeps the ones past the sixth untouched.

        private sealed record ArchetypeChoice(int Code, string Text);

        private bool _showingArchetypes;

        private ComboBox[] ArchetypeCombos() =>
        [
            CARDS_CB_CardArchetypeNumberOne, CARDS_CB_CardArchetypeNumberTwo, CARDS_CB_CardArchetypeNumberThree,
            CARDS_CB_CardArchetypeNumberFour, CARDS_CB_CardArchetypeNumberFive, CARDS_CB_CardArchetypeNumberSix,
        ];

        private static List<ArchetypeChoice> ArchetypeChoices()
        {
            var choices = new List<ArchetypeChoice> { new(0, "(none)") };
            choices.AddRange(Card_Named.CardsInArchetype.Keys.Where(code => code > 0).OrderBy(code => code)
                .Select(code => new ArchetypeChoice(code, $"{code} - {Card_Named.NameOf(code)}")));
            return choices;
        }

        private void ShowArchetypes(int konamiId)
        {
            var codes = Card_Named.ArchetypesOf(konamiId);
            _showingArchetypes = true;
            try
            {
                var combos = ArchetypeCombos();
                for (int i = 0; i < combos.Length; i++)
                    combos[i].SelectedValue = i < codes.Count ? codes[i] : 0;
            }
            finally { _showingArchetypes = false; }
        }

        private void ArchetypeCombo_Changed()
        {
            if (_showingArchetypes || !int.TryParse(CARDS_CB_CardID.Text, out int konamiId))
                return;

            var current = Card_Named.ArchetypesOf(konamiId);
            var wanted = ArchetypeCombos().Select(combo => combo.SelectedValue is int code ? code : 0).Where(code => code > 0).ToHashSet();
            foreach (int extra in current.Skip(6))
                wanted.Add(extra);

            foreach (var (code, list) in Card_Named.CardsInArchetype)
            {
                bool has = list.BinarySearch(konamiId) >= 0;
                if (wanted.Contains(code) && !has)
                {
                    list.Add(konamiId);
                    list.Sort();
                }
                else if (!wanted.Contains(code) && has)
                    list.Remove(konamiId);
            }
        }

        private void CARDS_CB_CardArchetypeNumberOne_SelectedIndexChanged(object sender, EventArgs e) => ArchetypeCombo_Changed();
        private void CARDS_CB_CardArchetypeNumberTwo_SelectedIndexChanged(object sender, EventArgs e) => ArchetypeCombo_Changed();
        private void CARDS_CB_CardArchetypeNumberThree_SelectedIndexChanged(object sender, EventArgs e) => ArchetypeCombo_Changed();
        private void CARDS_CB_CardArchetypeNumberFour_SelectedIndexChanged(object sender, EventArgs e) => ArchetypeCombo_Changed();
        private void CARDS_CB_CardArchetypeNumberFive_SelectedIndexChanged(object sender, EventArgs e) => ArchetypeCombo_Changed();
        private void CARDS_CB_CardArchetypeNumberSix_SelectedIndexChanged(object sender, EventArgs e) => ArchetypeCombo_Changed();

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
            if (CARDS_CB_SimilarCardName.SelectedItem is KeyValuePair<string, int> selectedKVP && CARDS_CB_CardID.SelectedItem is int selectedCardID)
            {
                var target = Card_Same._SimilarCards.FirstOrDefault(card => card.PrimaryCard == selectedCardID);
                target?.TargetCard = Convert.ToInt16(selectedKVP.Value);

                if (target == null)
                {
                    var similarCards = CARDS_CB_SimilarCardName.DataSource as List<KeyValuePair<string, int>>;
                    if (similarCards == null) return;

                    int selectedId = similarCards[CARDS_CB_SimilarCardName.SelectedIndex].Value;
                    if (CARDS_RB_SimilarOnEffect.Checked)
                    {
                        var Card = new Similar_Card(Convert.ToInt16(CARDS_CB_CardID.Text), (short)selectedId, CARD_Same.TYPE.EFFECT);
                        Card_Same._SimilarCards.Add(Card);
                    }
                    else if (CARDS_RB_AlwaysSimilar.Checked)
                    {
                        var Card = new Similar_Card(Convert.ToInt16(CARDS_CB_CardID.Text), (short)selectedId, CARD_Same.TYPE.ALWAYS);
                        Card_Same._SimilarCards.Add(Card);
                    }
                }
            }
        }

        private void CARDS_RB_SimilarOnEffect_CheckedChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_SimilarCardName.SelectedItem is KeyValuePair<string, int> selectedKVP && CARDS_CB_CardID.SelectedItem is int selectedCardID)
            {
                var target = Card_Same._SimilarCards.FirstOrDefault(card => card.PrimaryCard == selectedCardID);
                if (target == null)
                    return;
                if (CARDS_RB_SimilarOnEffect.Checked)
                    target.SimilarityType = TYPE.EFFECT;
            }
        }

        private void CARDS_RB_AlwaysSimilar_CheckedChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_SimilarCardName.SelectedItem is KeyValuePair<string, int> selectedKVP && CARDS_CB_CardID.SelectedItem is int selectedCardID)
            {
                var target = Card_Same._SimilarCards.FirstOrDefault(card => card.PrimaryCard == selectedCardID);
                if (target == null)
                    return;

                if (CARDS_RB_AlwaysSimilar.Checked)
                    target.SimilarityType = TYPE.ALWAYS;
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