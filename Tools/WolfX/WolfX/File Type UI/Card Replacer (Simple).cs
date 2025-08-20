using System.Text.RegularExpressions;

namespace WolfX.WolfX.File_Type_UI
{
    public partial class SimpleCardAdd : Form
    {
        public List<short> CardIDs { get; set; }

        public SimpleCardAdd()
        {
            InitializeComponent();
            CardIDs = [];
        }

        private void YDCSimple_BTN_AddCard_Click(object sender, EventArgs e)
        {
            string TempCards = TB_YDCSimple_CardIDs.Text;
            if (string.IsNullOrWhiteSpace(TempCards))
            {
                this.Close();
                return;
            }

            TempCards = Regex.Replace(TempCards, @"\s+", "");
            var StrCardIDs = TempCards.Split(',');

            if (StrCardIDs.Length == 0)
            {
                this.Close();
                return;
            }

            foreach (var Card in StrCardIDs)
            {
                if (short.TryParse(Card, out short parsedID))
                {
                    CardIDs.Add(parsedID);
                }
            }

            this.DialogResult = DialogResult.OK;
        }
    }
}