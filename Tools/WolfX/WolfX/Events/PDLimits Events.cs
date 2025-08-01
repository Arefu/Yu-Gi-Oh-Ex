﻿using Types;
using WolfX.WolfX.File_Type_UI;

namespace WolfX
{
    public partial class WolfUI
    {
        private ImageList Images = new ImageList();

        private void PDL_BTN_OpenPDL_Click(object sender, EventArgs e)
        {
            PDL_LV_ForbiddenCards.Items.Clear();
            PDL_LV_LimitedCards.Items.Clear();
            PDL_LV_SemiLimitedCards.Items.Clear();

            if (PDL_CB_LoadImages.Checked)
            {
                Images.ImageSize = new Size(64, 64);
                PDL_LV_ForbiddenCards.View = View.LargeIcon;
                PDL_LV_ForbiddenCards.LargeImageList = Images;

                PDL_LV_LimitedCards.View = View.LargeIcon;
                PDL_LV_LimitedCards.LargeImageList = Images;

                PDL_LV_SemiLimitedCards.View = View.LargeIcon;
                PDL_LV_SemiLimitedCards.LargeImageList = Images;
            }

            var Limits = "-1";
            if (State.Path == null || State.Path == "")
            {
                Limits = Utility.Get_UserSelectedFile("Select PDLimits.bin", "pd_limits.bin|pd_limits.bin");
                if (Limits == "-1")
                    return;
            }
            else
                Limits = State.Path + "\\bin\\pd_limits.bin";

            PDLimits.PDLimits.Load(Limits);
            if (PDLimits.PDLimits.GetForbiddenCount() > 0)
            {
                foreach (short i in PDLimits.PDLimits.GetForbidden())
                {
                    if (PDL_CB_LoadImages.Checked)
                        Images.Images.Add(i.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{i}.jpg")));

                    if (!PDL_CB_UseCardID.Checked)
                        PDL_LV_ForbiddenCards.Items.Add(CARDS_Cards.Get_CardNameFromID(i), i.ToString());
                    else
                        PDL_LV_ForbiddenCards.Items.Add(i.ToString(), i.ToString());
                }
            }

            if (PDLimits.PDLimits.GetLimitedCount() > 0)
            {
                foreach (short i in PDLimits.PDLimits.GetLimited())
                {
                    if (PDL_CB_LoadImages.Checked)
                        Images.Images.Add(i.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{i}.jpg")));

                    if (!PDL_CB_UseCardID.Checked)
                        PDL_LV_LimitedCards.Items.Add(CARDS_Cards.Get_CardNameFromID(i), i.ToString());
                    else
                        PDL_LV_LimitedCards.Items.Add(i.ToString(), i.ToString());
                }
            }

            if (PDLimits.PDLimits.GetSemiLimitedCount() > 0)
            {
                foreach (short i in PDLimits.PDLimits.GetSemiLimited())
                {
                    if (PDL_CB_LoadImages.Checked)
                        Images.Images.Add(i.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{i}.jpg")));

                    if (!PDL_CB_UseCardID.Checked)
                        PDL_LV_SemiLimitedCards.Items.Add(CARDS_Cards.Get_CardNameFromID(i), i.ToString());
                    else
                        PDL_LV_SemiLimitedCards.Items.Add(i.ToString(), i.ToString());
                }
            }

            PDL_LBL_NumOfForbidden.Text = PDLimits.PDLimits.GetForbiddenCount().ToString();
            PDL_LBL_NumOfLimited.Text = PDLimits.PDLimits.GetLimitedCount().ToString();
            PDL_LBL_NumOfSemiLimited.Text = PDLimits.PDLimits.GetSemiLimitedCount().ToString();

            PDL_BTN_SavePDL.Enabled = true;
            PDL_BTN_AddCardToList.Enabled = true;
        }

        private void PDL_BTN_SavePDL_Click(object sender, EventArgs e)
        {
            PDLimits.PDLimits.Save();
        }

        private void PDL_CB_LoadImages_CheckedChanged(object sender, EventArgs e)
        {
            if (PDL_CB_LoadImages.Checked)
            {
                if (State.Path != null)
                {
                    ZIB.Load($"{State.Path}\\2020.full.illust_j.jpg.zib");
                    PDL_LV_ForbiddenCards.View = View.LargeIcon;
                    PDL_LV_LimitedCards.View = View.LargeIcon;
                    PDL_LV_SemiLimitedCards.View = View.LargeIcon;
                }
                else
                {
                    ZIB.Load(Utility.Get_UserSelectedFile("Open ZIB Archive", "ZIB Archive (*.zib)|*.zib"));
                    PDL_LV_ForbiddenCards.View = View.LargeIcon;
                    PDL_LV_LimitedCards.View = View.LargeIcon;
                    PDL_LV_SemiLimitedCards.View = View.LargeIcon;
                }
            }
            else
            {
                PDL_LV_ForbiddenCards.View = View.List;
                PDL_LV_LimitedCards.View = View.List;
                PDL_LV_SemiLimitedCards.View = View.List;
            }
        }

        private void PDL_CB_UseCardID_CheckedChanged(object sender, EventArgs e)
        {
            if (PDL_CB_UseCardID.Checked == false)
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

        private void PDL_LV_ItemSelectionChanged(object sender, EventArgs e)
        {
            if (PDL_LV_ForbiddenCards.SelectedItems.Count > 0 || PDL_LV_LimitedCards.SelectedItems.Count > 0 || PDL_LV_SemiLimitedCards.SelectedItems.Count > 0)
                PDL_BTN_RemoveCardFromList.Enabled = true;
            else
                PDL_BTN_RemoveCardFromList.Enabled = false;
        }

        private void PDL_BTN_RemoveCardFromList_Click(object sender, EventArgs e)
        {
            if (PDL_LV_ForbiddenCards.SelectedItems.Count > 0)
            {
                foreach (ListViewItem i in PDL_LV_ForbiddenCards.SelectedItems)
                {
                    PDLimits.PDLimits.Remove_CardFromForbidden(Convert.ToUInt16(i.ImageKey));
                    PDL_LV_ForbiddenCards.Items.Remove(i);
                    PDL_LBL_NumOfForbidden.Text = PDLimits.PDLimits.GetForbiddenCount().ToString();
                }
            }
            if (PDL_LV_LimitedCards.SelectedItems.Count > 0)
            {
                foreach (ListViewItem i in PDL_LV_LimitedCards.SelectedItems)
                {
                    PDLimits.PDLimits.Remove_CardFromLimited(Convert.ToUInt16(i.ImageKey));
                    PDL_LV_LimitedCards.Items.Remove(i);
                    PDL_LBL_NumOfLimited.Text = PDLimits.PDLimits.GetLimitedCount().ToString();
                }
            }
            else if (PDL_LV_SemiLimitedCards.SelectedItems.Count > 0)
            {
                foreach (ListViewItem i in PDL_LV_SemiLimitedCards.SelectedItems)
                {
                    PDLimits.PDLimits.Remove_CardFromSemiLimited(Convert.ToUInt16(i.ImageKey));
                    PDL_LV_SemiLimitedCards.Items.Remove(i);
                    PDL_LBL_NumOfSemiLimited.Text = PDLimits.PDLimits.GetSemiLimitedCount().ToString();
                }
            }
        }

        private void PDL_BTN_AddCardToList_Click(object sender, EventArgs e)
        {
            if (PDL_CB_IsUsingSimpleAddBox.Checked)
            {
                var SimpleAdder = new SimpleCardAdd();
                var Result = SimpleAdder.ShowDialog();

                if (Result == DialogResult.OK)
                {
                    switch (tabControl2.SelectedTab.Text)
                    {
                        case "Forbidden":
                            foreach (var Card in SimpleAdder.CardIDs)
                            {
                                if (PDL_CB_LoadImages.Checked)
                                    Images.Images.Add(Card.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{Card}.jpg")));

                                if (!PDL_CB_UseCardID.Checked)
                                    PDL_LV_ForbiddenCards.Items.Add(CARDS_Cards.Get_CardNameFromID((short)Card), Card.ToString());
                                else
                                    PDL_LV_ForbiddenCards.Items.Add(Card.ToString(), Card.ToString());

                                PDLimits.PDLimits.Add_CardToForbidden((ushort)Card);
                            }

                            PDL_LBL_NumOfForbidden.Text = SimpleAdder.CardIDs.Count.ToString();
                            break;

                        case "Semi-Limited":
                            foreach (var Card in SimpleAdder.CardIDs)
                            {
                                if (PDL_CB_LoadImages.Checked)
                                    Images.Images.Add(Card.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{Card}.jpg")));

                                if (!PDL_CB_UseCardID.Checked)
                                    PDL_LV_SemiLimitedCards.Items.Add(CARDS_Cards.Get_CardNameFromID((short)Card), Card.ToString());
                                else
                                    PDL_LV_SemiLimitedCards.Items.Add(Card.ToString(), Card.ToString());

                                PDLimits.PDLimits.Add_CardToSemiLimited((ushort)Card);
                            }

                            PDL_LBL_NumOfSemiLimited.Text = SimpleAdder.CardIDs.Count.ToString();
                            break;

                        case "Limited":
                            foreach (var Card in SimpleAdder.CardIDs)
                            {
                                if (PDL_CB_LoadImages.Checked)
                                    Images.Images.Add(Card.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{Card}.jpg")));

                                if (!PDL_CB_UseCardID.Checked)
                                    PDL_LV_LimitedCards.Items.Add(CARDS_Cards.Get_CardNameFromID((short)Card), Card.ToString());
                                else
                                    PDL_LV_LimitedCards.Items.Add(Card.ToString(), Card.ToString());

                                PDLimits.PDLimits.Add_CardToLimited((ushort)Card);
                            }

                            PDL_LBL_NumOfLimited.Text = SimpleAdder.CardIDs.Count.ToString();
                            break;
                    }
                }
            }
            else
            {
            }
        }
    }
}