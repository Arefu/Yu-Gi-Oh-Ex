using Types;
using WolfX.WolfX.File_Type_UI;

namespace WolfX
{
    public partial class WolfUI
    {
        private void PACKDEF_BTN_OpenPackDEF_Click(object sender, EventArgs e)
        {
            var File = Utility.Get_UserSelectedFile("Open Packdata File", "packdata_#_#.bin (*.bin)|*.bin");
            if (File == "-1")
                return;
            PACKDATA.PACKDATA.Load(File);
            PACKDATA_LV_CommonCards.Items.Clear();
            PACKDATA_LV_RareCards.Items.Clear();

            if (PACKDATA_CB_LoadImages.Checked)
            {
                State.Images.ImageSize = new Size(64, 64);

                PACKDATA_LV_CommonCards.View = View.LargeIcon;
                PACKDATA_LV_CommonCards.LargeImageList = State.Images;

                PACKDATA_LV_RareCards.View = View.LargeIcon;
                PACKDATA_LV_RareCards.LargeImageList = State.Images;
            }

            foreach (short id in PACKDATA.PACKDATA._CommonCards)
            {
                if (PACKDATA_CB_LoadImages.Checked)
                    Utility.Add_ItemToStateImageList(id.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{id}.jpg")));

                if (!PACKDATA_CB_UseCardID.Checked)
                    PACKDATA_LV_CommonCards.Items.Add(CARDS_Cards.Get_CardNameFromID(id), id.ToString());
                else
                    PACKDATA_LV_CommonCards.Items.Add(id.ToString(), id.ToString());
            }

            foreach (short id in PACKDATA.PACKDATA._RareCards)
            {
                if (PACKDATA_CB_LoadImages.Checked)
                    Utility.Add_ItemToStateImageList(id.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{id}.jpg")));

                if (!PACKDATA_CB_UseCardID.Checked)
                    PACKDATA_LV_RareCards.Items.Add(CARDS_Cards.Get_CardNameFromID(id), id.ToString());
                else
                    PACKDATA_LV_RareCards.Items.Add(id.ToString(), id.ToString());
            }

            PACKDATA_LBL_NumberOfCommon.Text = PACKDATA.PACKDATA._CommonCards.Count.ToString();
            PACKDATA_LBL_NumberOfRare.Text = PACKDATA.PACKDATA._RareCards.Count.ToString();

            PACKDATA_BTN_AddCards.Enabled = true;
            PACKDATA_BTN_RemoveCard.Enabled = true;
        }

        private void PACKDATA_BTN_RemoveCard_Click(object sender, EventArgs e)
        {
            if (PACKDATA_LV_CommonCards.SelectedItems.Count == 0 && PACKDATA_LV_RareCards.SelectedItems.Count == 0)
                return;

            foreach (ListViewItem Item in PACKDATA_LV_CommonCards.SelectedItems)
            {
                PACKDATA_LV_CommonCards.Items.Remove(Item);
                PACKDATA.PACKDATA._CommonCards.Remove(Convert.ToInt16(Item.Text));
            }
            foreach (ListViewItem Item in PACKDATA_LV_RareCards.SelectedItems)
            {
                PACKDATA_LV_RareCards.Items.Remove(Item);

                PACKDATA.PACKDATA._RareCards.Remove(Convert.ToInt16(Item.Text));
            }
            PACKDATA.PACKDATA._NumberOfCommonCards = (short)PACKDATA_LV_CommonCards.Items.Count;
            PACKDATA.PACKDATA._NumberOfRareCards = (short)PACKDATA_LV_RareCards.Items.Count;
        }

        private void PACKDATA_BTN_AddCards_Click(object sender, EventArgs e)
        {
            if (CardShop_CB_UseSimpleAdd.Checked)
            {
                var SimpleAdder = new SimpleCardAdd();
                var Result = SimpleAdder.ShowDialog();

                if (Result == DialogResult.OK)
                {
                    foreach (var Card in SimpleAdder.CardIDs)
                    {
                        if (PACKDATA_TC_ListOfCardsSoldAtShop.SelectedTab.Text == "Common")
                        {
                            PACKDATA.PACKDATA._CommonCards.Add((short)Card);

                            bool exists = PACKDATA_LV_CommonCards.Items.Cast<ListViewItem>().Any(item => item.Text == Card.ToString());
                            if (!exists)
                                PACKDATA_LV_CommonCards.Items.Add(Card.ToString(), Card.ToString());

                            var sortedItems = PACKDATA_LV_CommonCards.Items.Cast<ListViewItem>().OrderBy(item => int.Parse(item.Text)).ToList();

                            PACKDATA_LV_CommonCards.Items.Clear();
                            PACKDATA_LV_CommonCards.Items.AddRange(sortedItems.ToArray());

                            PACKDATA.PACKDATA._NumberOfCommonCards = (short)PACKDATA_LV_CommonCards.Items.Count;
                        }
                        else
                        {
                            PACKDATA.PACKDATA._RareCards.Add((short)Card);

                            bool exists = PACKDATA_LV_RareCards.Items.Cast<ListViewItem>().Any(item => item.Text == Card.ToString());
                            if (!exists)
                                PACKDATA_LV_RareCards.Items.Add(Card.ToString(), Card.ToString());

                            var sortedItems = PACKDATA_LV_CommonCards.Items.Cast<ListViewItem>().OrderBy(item => int.Parse(item.Text)).ToList();

                            PACKDATA_LV_RareCards.Items.Clear();
                            PACKDATA_LV_RareCards.Items.AddRange(sortedItems.ToArray());

                            PACKDATA.PACKDATA._NumberOfRareCards = (short)PACKDATA_LV_RareCards.Items.Count;
                        }
                    }
                }
            }
            else
            {
                if (string.IsNullOrEmpty(State.Path))
                {
                    MessageBox.Show("Please Load Game", "Error - State not Set", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                var CardAdder = new CardAdder();
                var Result = CardAdder.ShowDialog();

                if (Result == DialogResult.OK)
                {
                }
            }
        }

        private void PACKDATA_BTN_SavePackDEF_Click(object sender, EventArgs e)
        {
            PACKDATA.PACKDATA.Save();
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
                    ZIB.Load($"{State.Path}\\2020.full.illust_j.jpg.zib");
                else
                    ZIB.Load(Utility.Get_UserSelectedFile("Open ZIB Archive", "ZIB Archive (*.zib)|*.zib"));

                PACKDATA_LV_CommonCards.View = View.LargeIcon;
                PACKDATA_LV_RareCards.View = View.LargeIcon;
            }
            else
            {
                PACKDATA_LV_CommonCards.View = View.List;
                PACKDATA_LV_RareCards.View = View.List;
            }
        }
    }
}