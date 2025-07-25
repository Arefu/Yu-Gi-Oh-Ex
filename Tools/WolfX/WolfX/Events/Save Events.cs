using savegame;

namespace WolfX
{
    public partial class WolfUI
    {
        private void SaveGame_BTN_OpenSave_Click(object sender, EventArgs e)
        {
            using var SaveGameFile = new OpenFileDialog();
            SaveGameFile.Title = "Select \"savegame.dat\"";

            if (SaveGameFile.ShowDialog() != DialogResult.OK)
                MessageBox.Show("Please \"savegame.dat\"", "Invalid File", MessageBoxButtons.OK, MessageBoxIcon.Error);

            SaveGame.Load(SaveGameFile.FileName);

            SaveGame.Load_Decks();
        }
    }
}