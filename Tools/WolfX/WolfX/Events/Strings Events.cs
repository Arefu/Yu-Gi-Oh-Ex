using Types;
using Wolf.Editors;

namespace WolfX
{
    /// <summary>
    /// The Strings page: the UI strings (strings\Strings_STEAM_&lt;lang&gt;.BND, language from Tools > Language) or, with "Credits file" ticked,
    /// the credits (main\ui\credits\credits.dat), read from the open game data and saved back into it.
    /// </summary>
    public partial class WolfUI
    {
        private List<BNDString> BNDStrings { get; set; } = [];
        private List<string> Credits { get; set; } = [];
        private List<int> _shownStrings = [];   // the list box row -> the string's index (the search shows some of them)
        private string? _stringsPath;           // what is open: a BND or the credits

        private bool EditingCredits => _stringsPath == CRED.GamePath;

        private List<string> StringValues => EditingCredits ? Credits : BNDStrings.Select(s => s.String).ToList();

        private void STRMAN_BTN_OpenStrings_Click(object sender, EventArgs e)
        {
            if (GameFolderFiles.Current is not { } files)
            {
                MessageBox.Show(this, "Open the game folder or an extracted YGO_2020 folder first (File > Open).", "Strings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string path = CREDITS_CheckB_IsCredit.Checked ? CRED.GamePath : BND.GamePath((char)State.Language);
            if (files.Read(path) is not { } data)
            {
                MessageBox.Show(this, $"The open data has no {path}.", "Strings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _stringsPath = path;
            if (EditingCredits)
                Credits = CRED.Parse(data);
            else
                BNDStrings = BND.Parse(data);
            STRMAN_LBL_LocalCount.Text = StringValues.Count.ToString();
            STRMAN_PB_HowFarThroughTheFile.Maximum = Math.Max(1, StringValues.Count);
            STRMAN_PB_HowFarThroughTheFile.Value = 0;
            STRMAN_TB_Search.Text = "";
            ShowStrings();
            SetStatus($"{path} from {files.Describe(path)}: {StringValues.Count} {(EditingCredits ? "lines" : "strings")}.");
        }

        private void STRMAN_BTN_SaveStrings_Click(object sender, EventArgs e)
        {
            if (GameFolderFiles.Current is not { } files || _stringsPath == null)
                return;
            ApplyStringEdit();
            try
            {
                files.Write(_stringsPath, EditingCredits ? CRED.ToBytes(Credits) : BND.ToBytes(BNDStrings));
                SetStatus($"Saved {_stringsPath} into {files.Describe(_stringsPath)}.");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, ex.Message, "Strings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private int? SelectedString() =>
            STRMAN_LB_CurrentFileStrings.SelectedIndex is int row and >= 0 && row < _shownStrings.Count ? _shownStrings[row] : null;

        private void STRMAN_LB_CurrentFileStrings_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (SelectedString() is not int index)
                return;
            STRMAN_PB_HowFarThroughTheFile.Value = Math.Min(index, STRMAN_PB_HowFarThroughTheFile.Maximum);
            STRMAN_TB_NewStringValue.Text = StringValues[index];
        }

        private void STRMAN_TB_NewStringValue_Leave(object sender, EventArgs e) => ApplyStringEdit();

        /// <summary>The edit box's text into the selected string.</summary>
        private void ApplyStringEdit()
        {
            if (SelectedString() is not int index || StringValues[index] == STRMAN_TB_NewStringValue.Text)
                return;
            if (EditingCredits)
                Credits[index] = STRMAN_TB_NewStringValue.Text;
            else
                BNDStrings[index].String = STRMAN_TB_NewStringValue.Text;
            int row = STRMAN_LB_CurrentFileStrings.SelectedIndex;
            STRMAN_LB_CurrentFileStrings.Items[row] = STRMAN_TB_NewStringValue.Text;
        }

        private void STRMAN_BTN_Search_Click(object sender, EventArgs e) => ShowStrings();

        /// <summary>Fills the list with the strings that contain the search text (all of them when it's empty).</summary>
        private void ShowStrings()
        {
            string find = STRMAN_TB_Search.Text;
            var comparison = STRMAN_CheckB_CaseSensitive.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var values = StringValues;
            _shownStrings = Enumerable.Range(0, values.Count).Where(i => find.Length == 0 || (values[i]?.Contains(find, comparison) ?? false)).ToList();
            STRMAN_LB_CurrentFileStrings.BeginUpdate();
            STRMAN_LB_CurrentFileStrings.Items.Clear();
            foreach (int i in _shownStrings)
                STRMAN_LB_CurrentFileStrings.Items.Add(values[i]);
            STRMAN_LB_CurrentFileStrings.EndUpdate();
        }
    }
}
