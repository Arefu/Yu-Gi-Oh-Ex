using System.Text.RegularExpressions;

namespace WolfX.WolfX.File_Type_UI
{
    public partial class SimpleCardAdd : Form
    {
        public List<int> CardIDs { get; set; }

        public SimpleCardAdd()
        {
            InitializeComponent();
            CardIDs = [];
        }

        private void YDCSimple_BTN_AddCard_Click(object sender, EventArgs e)
        {
            string TempCards = TB_YDCSimple_CardIDs.Text;
            Regex.Replace(TempCards, @"\s+", "");
            var StrCardIDs = TempCards.Split(',');
            foreach (var Card in StrCardIDs)
            {
                CardIDs.Add(Int32.Parse(Card));
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
