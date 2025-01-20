using System.Runtime.InteropServices;
using Types;
using WolfX.Types;

namespace WolfX
{
    public partial class WolfUI
    {
        private void CARDS_BTN_OpenCards_Click(object sender, EventArgs e)
        {
            using var OpenFile = new OpenFileDialog();
            OpenFile.Filter = $"{State.Language} Card Indx File|CarD_Indx_{State.Language.ToString()[0]}.bin|All Indx Files (*.bin)|*.bin";
            OpenFile.Title = "Open Cards Indx File";
            OpenFile.Multiselect = false;
            OpenFile.InitialDirectory = State.Path;
            var Res = OpenFile.ShowDialog();
            if (Res != DialogResult.OK)
            {
                MessageBox.Show("Please Select a Cards Indx File", "No Cards Indx File Selected", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (OpenFile.SafeFileName.StartsWith("CARD_Indx") != true)
            {
                MessageBox.Show("Please Select a Cards Indx File", "Incorrect File Selected", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (CARDS_Cards.Setup_CardBinder(OpenFile.FileName) == false)
            {
                MessageBox.Show("Failed to Setup Card Binder\nCheck Yu-Gi-Oh-Ex Wiki!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            OpenFile.Filter = "ZIB Archive|*.zib";
            OpenFile.Title = "Open Cards Image Archive";
            Res = OpenFile.ShowDialog();
            if (Res != DialogResult.OK)
            {
                MessageBox.Show("Please Select a Cards Image Archive", "No Cards Image Archive Selected", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ZIB.Load(OpenFile.FileName);

            CARDS_Cards.LoadCardInfo();
            CARDS_Cards.LoadCardProps();

            CARDS_CB_CardName.DataSource = CARDS_Cards.Cards.Select(Select => Select.Name).ToList();
            CARDS_CB_CardID.DataSource = CARDS_Cards.Cards.Select(Select => Select.ID).ToList();
            CARDS_CB_CardTypes.DataSource = CARDS_Cards.Cards.Select(Select => Select.Type).Distinct().ToList();
            CARDS_CB_CardAttribute.DataSource = CARDS_Cards.Cards.Select(Select => Select.Attribute).Distinct().ToList();

        }
        private void CARDS_CB_CardName_SelectedIndexChanged(object sender, EventArgs e)
        {
            if(CARDS_CB_CardName.SelectedIndex == -1)
                return;

            CARDS_CB_CardID.SelectedIndexChanged -= CARDS_CB_CardID_SelectedIndexChanged;

            var SelectedCard = CARDS_Cards.Cards.Where(Card => Card.Name == CARDS_CB_CardName.Text).First();
            CARDS_CB_CardID.Text = SelectedCard.ID.ToString();
            CARDS_CB_CardTypes.Text = SelectedCard.Type.ToString();
            CARDS_CB_CardAttribute.Text = SelectedCard.Attribute.ToString();
            CARDS_Nud_CardLevel.Text = SelectedCard.Level.ToString();
            CARDS_TB_CardDesc.Text = SelectedCard.Desc;

            if(SelectedCard.Attack == -1 && SelectedCard.Defense == -1)
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
        }
        private void CARDS_CB_CardID_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CARDS_CB_CardID.SelectedIndex == -1)
                return;

            CARDS_CB_CardName.SelectedIndexChanged -= CARDS_CB_CardName_SelectedIndexChanged;

            var SelectedCard = CARDS_Cards.Cards.Where(Card => Card.ID == int.Parse(CARDS_CB_CardID.Text)).First();
            CARDS_CB_CardName.Text = SelectedCard.Name;
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

            CARDS_CB_CardName.SelectedIndexChanged += CARDS_CB_CardName_SelectedIndexChanged;

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
    }
}
