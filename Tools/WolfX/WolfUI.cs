using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Types;
using WolfX.Handler.Tools;
using WolfX.Handlers;

namespace WolfX
{
    public partial class WolfUI : Form
    {
        public WolfUI()
        {
            CheckForIllegalCrossThreadCalls = false;
            Form = this;
            InitializeComponent();
            this.ARCHIVE_LV_ArchiveItems.SmallImageList = Preview_Generator.Get_ArchiveImageFromDll();
        }

        private void File_Open_Click(object sender, EventArgs e)
        {
            using var FolderBrowser = new FolderBrowserDialog();
            var Res = FolderBrowser.ShowDialog();

            if (Res != DialogResult.OK || string.IsNullOrWhiteSpace(FolderBrowser.SelectedPath)) return;
            if (new DirectoryInfo(FolderBrowser.SelectedPath).Name != "YGO_2020")
            {
                MessageBox.Show("Please Select \"YGO_2020\"", "Incorrect Folder Selected", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            LBL_GameStatusLabel.Text = @"Opening Game Directory...";
            LBL_GameStatusLabel.ForeColor = Color.Green;

            WolfX_TabManager.Enabled = true;
            Tools_Verify.Enabled = true;
            State.WorkingDirectory = FolderBrowser.SelectedPath;
            State.IsLoaded = true;

            new Thread(Card_Loader.Load).Start();

        }

        private void File_Exit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void Lv_ArchivePreviewer_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.ARCHIVE_LV_ArchiveItems.SuspendLayout();
            var Img = Image.FromStream(ZIB.Get_SpecificItemFromArchive(ARCHIVE_LV_ArchiveItems.SelectedItems[0].Text));

        }

        internal void Tools_Verify_Click(object sender, EventArgs e)
        {
            var Missing = false;
            using var OpenFile = new OpenFileDialog();
            OpenFile.Title = "Select Yu-Gi-Oh! Legacy of the Duelist Link Evolution YGO_2020.toc File";
            OpenFile.Filter = "YGO_2020.toc|YGO_2020.toc";
            if (OpenFile.ShowDialog() != DialogResult.OK) return;

            foreach (var Line in File.ReadLines(OpenFile.FileName))
            {
                if (Line == "UT") continue;

                var CLine = Line.TrimStart(' ');
                CLine = Regex.Replace(CLine, @"  +", " ", RegexOptions.Compiled);
                var Information = CLine.Split(' ', 3);

                if (File.Exists($"{State.WorkingDirectory}/{Information[2]}") == false)
                {
                    Missing = true;
                    File.AppendAllLines("Missing.txt", new[] { Information[2] });
                }
            }

            if (Missing)
                MessageBox.Show("Missing Files Found, Please Check Missing.txt and Re-Run Yami-Yugi.exe", "Missing Files", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else
                MessageBox.Show("No Missing Files Found", "No Missing Files", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }


        private void btn_NextCard_Click(object sender, EventArgs e)
        {
            if (State.CardIndex == State.Cards.Count)
                return;

            State.CardIndex++;

            Form.CARDS_CB_CardName.Text = State.Cards[State.CardIndex]._Name;
            Form.TB_CardDesc.Text = State.Cards[State.CardIndex]._Description;
            Form.CARDS_CB_CardID.SelectedIndex = State.CardIndex;
            Form.TB_CardAtk.Text = State.Cards[State.CardIndex]._Atk.ToString();
            Form.TB_CardDef.Text = State.Cards[State.CardIndex]._Def.ToString();
            // Form.CB_CardAttribute.SelectedIndex = (int)State.Cards[State.CardIndex]._Attribute;
            Form.Nud_CardLevel.Text = State.Cards[State.CardIndex]._Level.ToString();
            Form.CB_CardTypes.Text = State.Cards[State.CardIndex]._Type.ToString();
            //   Form.PB_CardPicture.Image =
            //  Preview_Generator.Get_CardImageFromArchive(State.Cards[State.CardIndex]._Id.ToString(), Form.CB_LoadCensoredCards.Checked);
        }

        private void btn_LastCard_Click(object sender, EventArgs e)
        {
            if (State.CardIndex == 0)
                return;

            State.CardIndex--;

            Form.CARDS_CB_CardName.Text = State.Cards[State.CardIndex]._Name;
            Form.TB_CardDesc.Text = State.Cards[State.CardIndex]._Description;
            Form.CARDS_CB_CardID.SelectedIndex = State.CardIndex;
            Form.TB_CardAtk.Text = State.Cards[State.CardIndex]._Atk.ToString();
            Form.TB_CardDef.Text = State.Cards[State.CardIndex]._Def.ToString();
            //   Form.CB_CardAttribute.SelectedIndex = (int)State.Cards[State.CardIndex]._Attribute;
            Form.Nud_CardLevel.Text = State.Cards[State.CardIndex]._Level.ToString();
            Form.CB_CardTypes.Text = State.Cards[State.CardIndex]._Type.ToString();
            //Form.PB_CardPicture.Image =
            //   Preview_Generator.Get_CardImageFromArchive(State.Cards[State.CardIndex]._Id.ToString(), Form.CB_LoadCensoredCards.Checked);
        }

        private void CB_CardID_SelectedIndexChanged(object sender, EventArgs e)
        {
            State.CardIndex = Form.CARDS_CB_CardID.SelectedIndex;

            Form.CARDS_CB_CardName.Text = State.Cards[State.CardIndex]._Name;
            Form.TB_CardDesc.Text = State.Cards[State.CardIndex]._Description;
            Form.CARDS_CB_CardID.SelectedIndex = State.CardIndex;
            Form.TB_CardAtk.Text = State.Cards[State.CardIndex]._Atk.ToString();
            Form.TB_CardDef.Text = State.Cards[State.CardIndex]._Def.ToString();
            //     Form.CB_CardAttribute.SelectedIndex = (int)State.Cards[State.CardIndex]._Attribute;
            Form.Nud_CardLevel.Text = State.Cards[State.CardIndex]._Level.ToString();
            Form.CB_CardTypes.Text = State.Cards[State.CardIndex]._Type.ToString();
            //  Form.PB_CardPicture.Image =
            //    Preview_Generator.Get_CardImageFromArchive(State.Cards[State.CardIndex]._Id.ToString(), Form.CB_LoadCensoredCards.Checked);
        }

        private void btn_SaveCard_Click(object sender, EventArgs e)
        {
            var thing = State.Cards.First(x => x._Id == int.Parse(Form.CARDS_CB_CardID.Text.Split('-')[0]));
            var Message =
                $"{State.Cards.First(x => x._Id == int.Parse(Form.CARDS_CB_CardID.Text.Split('-')[0]))._Bit1}";
            Debug.WriteLine(Message);

            var Bit1 = thing._Bit1;
            var bit1_mrk = BitVector32.CreateSection(16383);
            var bit1_attack = BitVector32.CreateSection(511, bit1_mrk);
            var bit1_defence = BitVector32.CreateSection(511, bit1_attack);
            Bit1[bit1_attack] = Convert.ToInt32(Form.TB_CardAtk.Text) / 10;
            Bit1[bit1_defence] = Convert.ToInt32(Form.TB_CardDef.Text) / 10;
            Debug.WriteLine(Bit1.ToString());
            /*
             * _Atk = (int)(Bit1[bit1_attack] * 10),
            _Def = (int)(Bit1[bit1_defence] * 10),
             */
        }

        private void CB_CardName_SelectedIndexChanged(object sender, EventArgs e)
        {

            State.CardIndex = CARDS_CB_CardName.SelectedIndex;
            Form.CARDS_CB_CardName.Text = State.Cards[State.CardIndex]._Name;
            Form.TB_CardDesc.Text = State.Cards[State.CardIndex]._Description;
            Form.CARDS_CB_CardID.SelectedIndex = State.CardIndex;
            Form.TB_CardAtk.Text = State.Cards[State.CardIndex]._Atk.ToString();
            Form.TB_CardDef.Text = State.Cards[State.CardIndex]._Def.ToString();
            //   Form.CB_CardAttribute.SelectedIndex = (int)State.Cards[State.CardIndex]._Attribute;
            Form.Nud_CardLevel.Text = State.Cards[State.CardIndex]._Level.ToString();
            Form.CB_CardTypes.Text = State.Cards[State.CardIndex]._Type.ToString();
            //Form.PB_CardPicture.Image =
            //    Preview_Generator.Get_CardImageFromArchive(State.Cards[State.CardIndex]._Id.ToString(), Form.CB_LoadCensoredCards.Checked);
        }

        private void ReplaceImage_Click(object sender, EventArgs e)
        {

        }

        private void CB_LoadCensoredCards_CheckedChanged(object sender, EventArgs e)
        {
            // Form.PB_CardPicture.Image = Preview_Generator.Get_CardImageFromArchive(State.Cards[State.CardIndex]._Id.ToString(), CB_LoadCensoredCards.Checked);
        }

     
    }
}