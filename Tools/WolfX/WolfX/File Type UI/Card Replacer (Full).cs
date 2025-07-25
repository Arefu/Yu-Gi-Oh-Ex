using System.Data;
using Types;

namespace WolfX.WolfX.File_Type_UI
{
    public partial class CardAdder : Form
    {
        public bool HasLoaded = false;

        public CardAdder()
        {
            InitializeComponent();
            Shown += CardAdder_ShownAsync;
        }

        private async void CardAdder_ShownAsync(object sender, EventArgs e)
        {
            ZIB.Load($"{State.Path}\\2020.full.illust_j.jpg.zib");
            dataGridView1.AutoGenerateColumns = false;

            CardAdder_TB_NameOfCard.DataPropertyName = "Name";
            CardAdder_TB_IDOfCard.DataPropertyName = "ID";

            dataGridView1.DataSource = CARDS_Cards.Cards.Where(card => card.ID != 0).ToList();
        }
    }
}