using System.Data;
using Types;

namespace WolfX.WolfX.File_Type_UI
{
    public partial class Card_Changer : Form
    {
        public bool HasLoaded = false;
        public Card_Changer(List<ZIB_Item> IllustrationImages, List<CARD_Card> Cards)
        {
            InitializeComponent();
        }
    }
}
