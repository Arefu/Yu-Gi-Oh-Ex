using PACKDATA;
using System.Diagnostics;
using Types;

namespace WolfX
{
    public partial class WolfUI
    {
        private void PACKDEF_BTN_OpenPackDEF_Click(object sender, EventArgs e)
        {
            var File = Utility.Get_UserSelectedFile("Open Packdata File", "packdata_#_#.bin (*.bin)|*.bin");

            PACKDATA.PACKDATA.Load(File);
            PACKDATA_LV_CommonCards.Items.Clear();
            PACKDATA_LV_RareCards.Items.Clear();

            if (PACKDATA_CB_LoadImages.Checked)
            {
                Images.ImageSize = new Size(64, 64);

                PACKDATA_LV_CommonCards.View = View.LargeIcon;
                PACKDATA_LV_CommonCards.LargeImageList = Images;

                PACKDATA_LV_RareCards.View = View.LargeIcon;
                PACKDATA_LV_RareCards.LargeImageList = Images;
            }

            foreach (short id in PACKDATA.PACKDATA._CommonCards)
            {
                if (PACKDATA_CB_LoadImages.Checked)
                    Images.Images.Add(id.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{id}.jpg")));

                if (!PACKDATA_CB_UseCardID.Checked)
                    PACKDATA_LV_CommonCards.Items.Add(CARDS_Cards.Get_CardNameFromID(id), id.ToString());
                else
                    PACKDATA_LV_CommonCards.Items.Add(id.ToString(), id.ToString());
            }

            foreach (short id in PACKDATA.PACKDATA._RareCards)
            {
                if (PACKDATA_CB_LoadImages.Checked)
                    Images.Images.Add(id.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{id}.jpg")));

                if (!PACKDATA_CB_UseCardID.Checked)
                    PACKDATA_LV_RareCards.Items.Add(CARDS_Cards.Get_CardNameFromID(id), id.ToString());
                else
                    PACKDATA_LV_RareCards.Items.Add(id.ToString(), id.ToString());
            }

            PACKDATA_LBL_NumberOfCommon.Text = PACKDATA.PACKDATA._CommonCards.Count.ToString();
            PACKDATA_LBL_NumberOfRare.Text = PACKDATA.PACKDATA._RareCards.Count.ToString();

            //PACKDATA_BTN_Save.Enabled = true;
            //PACKDATA_BTN_AddCardToList.Enabled = true;
        }

        private void PACKDATA_CB_UseCardID_CheckedChanged(object sender, EventArgs e)
        {
            if (PACKDATA_CB_UseCardID.Checked == false)
            {
                if (State.Path != null)
                {
                    CARDS_Cards.Setup_CardBinder($"{State.Path}\\bin\\CARD_Indx_{State.Language.ToString()[0]}.bin", (CARDS_INFO.CARD_Language)State.Language);
                    CARDS_Cards.LoadCardInfo();
                    CARDS_Cards.LoadCardProps();
                }
                else
                {
                    if (CARDS_Cards.Setup_CardBinder(Utility.Get_UserSelectedFile("Open Cards Indx File", $"{State.Language} Card Indx File|CarD_Indx_{State.Language.ToString()[0]}.bin|All Indx Files (*.bin)|*.bin"), (CARDS_INFO.CARD_Language)State.Language) == false)
                    {
                        MessageBox.Show("Failed to Setup Card Binder\nCheck Yu-Gi-Oh-Ex Wiki!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    CARDS_Cards.LoadCardInfo();
                    CARDS_Cards.LoadCardProps();
                }
            }
        }

        private void PACKDATA_CB_LoadImages_CheckedChanged(object sender, EventArgs e)
        {

            if (PACKDATA_CB_LoadImages.Checked)
            {
                if (State.Path != null)
                {
                    ZIB.Load($"{State.Path}\\2020.full.illust_j.jpg.zib");
                    PACKDATA_LV_CommonCards.View = View.LargeIcon;
                    PACKDATA_LV_RareCards.View = View.LargeIcon;
                }
                else
                {
                    ZIB.Load(Utility.Get_UserSelectedFile("Open ZIB Archive", "ZIB Archive (*.zib)|*.zib"));
                    PACKDATA_LV_CommonCards.View = View.LargeIcon;
                    PACKDATA_LV_RareCards.View = View.LargeIcon;
                }
            }
            else
            {
                PACKDATA_LV_CommonCards.View = View.List;
                PACKDATA_LV_RareCards.View = View.List  ;
            }
        }
    }
}
