using System.Diagnostics;
using PACKDATA;

namespace WolfX
{
    public partial class WolfUI
    {
        private void PACKDEF_BTN_OpenPackDEF_Click(object sender, EventArgs e)
        {
            var File  = Utility.Get_UserSelectedFile("Open Packdata File", "packdata_#_#.bin (*.bin)|*.bin");

            PACKDATA.PACKDATA.Load(File);

            PACKDATA_LBL_NumberOfRare.Text = PACKDATA.PACKDATA._NumberOfRareCards.ToString();
            PACKDATA_LBL_NumberOfCommon.Text = PACKDATA.PACKDATA._NumberOfCommonCards.ToString();

            foreach(var Item in PACKDATA.PACKDATA._CommonCards)
                {
                PACKDATA_LV_CommonCards.Items.Add(Item.ToString());
            }
            foreach(var Item in PACKDATA.PACKDATA._RareCards)
            {
                PACKDATA_LV_RareCards.Items.Add(Item.ToString());
            }
        }
    }
}
