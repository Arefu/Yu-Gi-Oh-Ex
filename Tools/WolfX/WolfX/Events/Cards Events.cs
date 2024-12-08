using Types;
using WolfX.Types;

namespace WolfX
{
    public partial class WolfUI
    {
        private void CARDS_BTN_OpenCards_Click(object sender, EventArgs e)
        {
            using (var OpenFile = new OpenFileDialog())
            {
                OpenFile.Filter = $"{State.Language} Card Indx File|CarD_Indx_{State.Language.ToString()[0]}.bin|All Indx Files (*.bin)|*.bin";
                OpenFile.Title = "Open Cards Indx File";
                OpenFile.Multiselect = false;
                OpenFile.InitialDirectory = State.WorkingDirectory;
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

                CARDS_Cards.LoadCardInfo();
                CARDS_Cards.LoadCardProps();

                CARDS_CB_CardName.DataSource = CARDS_Cards.Cards.Select(Select => Select.Name).ToList();
                CARDS_CB_CardID.DataSource = CARDS_Cards.Cards.Select(Select => Select.ID).ToList();

            }
        }

        private void CARDS_BTN_SaveCard_Click(object sender, EventArgs e)
        {
            CARDS_Cards.SaveCardInfo();
        }
    }
}
