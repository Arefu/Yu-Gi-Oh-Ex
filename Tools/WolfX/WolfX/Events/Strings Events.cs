using Types;

namespace WolfX
{
    public partial class WolfUI
    {
        public List<BNDString> BNDStrings { get; set; } = new List<BNDString>(); //Reassess Protection Level

        private void STRMAN_BTN_OpenStrings_Click(object sender, EventArgs e)
        {
            using (var FileDialog = new OpenFileDialog())
            {
                if (CREDITS_CheckB_IsCredit.Checked)
            {
                    FileDialog.Title = "Open Credits *.DAT File";
                    FileDialog.Filter = "Credits File|credits.dat|All DAT Files (*.DAT)|*.DAT";
                    if(FileDialog.ShowDialog() != DialogResult.OK)
                    {
                        MessageBox.Show("Please Select a Credits *.DAT File", "No Credits File Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    var Credits = CRED.Load(FileDialog.FileName);

                    STRMAN_LB_CurrentFileStrings.Items.Clear();
                    STRMAN_LBL_LocalCount.Text = Credits.Count.ToString();

                    foreach(var String in Credits)
                    {
                        STRMAN_LB_CurrentFileStrings.Items.Add(String);
                    }
                    return;
            }

            
                FileDialog.Title = "Open Strings *.BND File";
                FileDialog.Filter = $"{State.Language} String File|Strings_STEAM_{State.Language.ToString()[0]}.BND|All BND Files (*.BND)|*.BND";

                if (FileDialog.ShowDialog() != DialogResult.OK)
                {
                    MessageBox.Show("Please Select a Strings *.BND File", "No Strings File Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                STRMAN_LB_CurrentFileStrings.Items.Clear();

                BNDStrings = BND.Load(FileDialog.FileName);
                STRMAN_LBL_LocalCount.Text = BNDStrings.Count.ToString();
                STRMAN_PB_HowFarThroughTheFile.Maximum = BNDStrings.Count;
                STRMAN_PB_HowFarThroughTheFile.Value = 0;

                foreach (var String in BNDStrings)
                {
                    STRMAN_LB_CurrentFileStrings.Items.Add(String.String);
                }

            }
        }
        private void STRMAN_BTN_SaveStrings_Click(object sender, EventArgs e)
        {
            BND.Save(BNDStrings);
        }

        private void STRMAN_LB_CurrentFileStrings_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (STRMAN_LB_CurrentFileStrings.SelectedIndex < 0) return;

            STRMAN_PB_HowFarThroughTheFile.Value = STRMAN_LB_CurrentFileStrings.SelectedIndex;
            STRMAN_TB_NewStringValue.Text = BNDStrings[STRMAN_LB_CurrentFileStrings.SelectedIndex].String;
        }


        private void STRMAN_TB_NewStringValue_Leave(object sender, EventArgs e)
        {
            if (STRMAN_LB_CurrentFileStrings.SelectedIndex < 0) return;

            BNDStrings[STRMAN_LB_CurrentFileStrings.SelectedIndex].String = STRMAN_TB_NewStringValue.Text;
            STRMAN_LB_CurrentFileStrings.Items[STRMAN_LB_CurrentFileStrings.SelectedIndex] = STRMAN_TB_NewStringValue.Text;
        }
    }
}
