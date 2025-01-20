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
            foreach(var i in Forbidden)
            {
                PDL_LV_ForbiddenCards.Items.Add(i.ToString());
            }

            var Limited = PDLimits.PDLimits.GetLimited();
            foreach(var i in Limited)
            {
                PDL_LV_LimitedCards.Items.Add(i.ToString());
            }

            PDL_LBL_NumOfForbidden.Text = PDLimits.PDLimits.GetForbiddenCount().ToString();
            PDL_LBL_NumOfLimited.Text = PDLimits.PDLimits.GetLimitedCount().ToString();
        }

    }
}
