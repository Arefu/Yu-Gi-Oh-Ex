namespace WolfX.Types
{
    /// <summary>A box to paste card names into, one per line. "3x Dark Magician" or "Dark Magician x3" sets the copies.</summary>
    public sealed partial class PasteNamesDialog : Form
    {
        public PasteNamesDialog()
        {
            InitializeComponent();
        }

        public string PastedText => _text.Text;
    }
}
