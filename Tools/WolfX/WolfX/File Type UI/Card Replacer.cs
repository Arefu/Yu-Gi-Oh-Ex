using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Types;

namespace WolfX.WolfX.File_Type_UI
{
    public partial class Card_Changer : Form
    {
       public bool HasLoaded = false;
        public Card_Changer()
        { 
            InitializeComponent();
        }

        
        public void LoadCards(CARDS_INFO.CARD_Language Language, string Path)
        {
            HasLoaded = true;
            if (Path == null || Path.Length == 0)
            {
                using (var OpenFile = new OpenFileDialog())
                {
                    OpenFile.Filter = $"{Language} Card Indx File|Card_Indx_{Language.ToString()[0]}.bin|All Indx Files (*.bin)|*.bin";
                    OpenFile.Title = "Open Cards Indx File";

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

                }
            }
                if (CARDS_Cards.Setup_CardBinder($"{Path}\\bin\\CARD_Indx_{Language.ToString()[0]}.bin") == false)
                {
                    MessageBox.Show("Failed to Setup Card Binder\nCheck Yu-Gi-Oh-Ex Wiki!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                CARDS_Cards.LoadCardInfo();
                CARDS_Cards.LoadCardProps();

                CARDREP_CB_NewCardSelector.DataSource = CARDS_Cards.Cards.Select(Select => Select.Name).ToList();
            }
        

        private void CARDREP_CB_NewCardSelector_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CARDS_Cards.Cards.Count == 0)
                return;

            var ID = CARDS_Cards.Cards.Where(Card => Card.Name == CARDREP_CB_NewCardSelector.Text).First();
            if (ID != null && ID.ID != 0)
            {
                
            }

            CARDREP_TB_ATK.Text = ID.Attack.ToString();
            CARDREP_TB_DEF.Text = ID.Defense.ToString();

            CARDREP_LBL_NewCardDesc.Text = ID.Desc;
        }

        public CARD_Card Card = new CARD_Card();
        private void CARDREP_BTN_UpdateCard_Click(object sender, EventArgs e)
        {
            Card = CARDS_Cards.Cards.Where(Card => Card.Name == CARDREP_CB_NewCardSelector.Text).First();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
