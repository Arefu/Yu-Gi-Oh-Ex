using Types;
using Windows.Devices.Scanners;
using WolfX.Types;
using WolfX.WolfX.File_Type_UI;

namespace WolfX
{
    public partial class WolfUI
    {
        private void PDL_BTN_OpenPDL_Click(object sender, EventArgs e)
        {
            var Images = new ImageList();
            if (PDL_CB_LoadImages.Checked)
            {
                Images.ImageSize = new Size(64, 64);
                PDL_LV_ForbiddenCards.View = View.LargeIcon;
                PDL_LV_ForbiddenCards.LargeImageList = Images;

                PDL_LV_LimitedCards.View = View.LargeIcon;
                PDL_LV_LimitedCards.LargeImageList = Images;
            }

            var Limits = "-1";
            if (State.Path == null || State.Path == "")
            {
                var OFD = new OpenFileDialog();
                OFD.Title = "Select PDLimits.bin";
                OFD.Filter = "pd_limits.bin|pd_limits.bin";
                if (OFD.ShowDialog() == DialogResult.OK)
                {
                    Limits = OFD.FileName;
                }
                else
                {
                    MessageBox.Show("Please Select the PDLimits.bin", "Invalid File", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            if (Limits == "-1")
                Limits = State.Path + "\\bin\\pd_limits.bin";

            PDLimits.PDLimits.Load(Limits);

            var Forbidden = PDLimits.PDLimits.GetForbidden();
            foreach (var i in Forbidden)
            {
                if (PDL_CB_LoadImages.Checked)
                    Images.Images.Add(i.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{i}.jpg")));

                PDL_LV_ForbiddenCards.Items.Add(i.ToString(), i.ToString());
            }

            var Limited = PDLimits.PDLimits.GetLimited();
            foreach (var i in Limited)
            {
                if (PDL_CB_LoadImages.Checked)
                    Images.Images.Add(i.ToString(), Image.FromStream(ZIB.Get_SpecificItemFromArchive($"{i}.jpg")));

                PDL_LV_LimitedCards.Items.Add(i.ToString(), i.ToString());
            }

            PDL_LBL_NumOfForbidden.Text = PDLimits.PDLimits.GetForbiddenCount().ToString();
            PDL_LBL_NumOfLimited.Text = PDLimits.PDLimits.GetLimitedCount().ToString();
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
                }
                else
                {
                    using (var OpenDialog = new OpenFileDialog())
                    {
                        OpenDialog.Title = "Open ZIB Archive";
                        OpenDialog.Filter = "ZIB Archive (*.zib)|*.zib";
                        if (OpenDialog.ShowDialog() != DialogResult.OK)
                        {
                            MessageBox.Show("No ZIB Archive Selected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                            return;
                        }
                        ZIB.Load(OpenDialog.FileName);
                        PDL_LV_ForbiddenCards.View = View.LargeIcon;
                        PDL_LV_LimitedCards.View = View.LargeIcon;
                    }

                }
            }
            else
            {
                PDL_LV_ForbiddenCards.View = View.List;
                PDL_LV_LimitedCards.View = View.List;
            }
        }

    }
}
