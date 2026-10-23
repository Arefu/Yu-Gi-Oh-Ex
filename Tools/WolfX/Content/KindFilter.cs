namespace WolfEx
{
    /// <summary>
    /// A "Kinds" drop-down for a card list's toolbar: every kind the list has, ticked when shown. Untick a kind to hide its cards from the list
    /// (only the list: nothing about the cards changes), e.g. the ones known to work already. Not kept: every kind shows again next time.
    /// </summary>
    internal sealed class KindFilter : ToolStripDropDownButton
    {
        private readonly Func<IEnumerable<string>> _kinds;
        private readonly HashSet<string> _hidden = [];

        /// <summary>Raised when a kind is shown or hidden: the list fills again.</summary>
        public event Action? Changed;

        /// <param name="kinds">the kinds the list's cards have now (the menu lists them when it opens)</param>
        public KindFilter(Func<IEnumerable<string>> kinds) : base("Kinds")
        {
            _kinds = kinds;
            DisplayStyle = ToolStripItemDisplayStyle.Text;
            ToolTipText = "Show or hide cards by kind (only in this list)";
            DropDown.Closing += (_, e) => e.Cancel = e.CloseReason == ToolStripDropDownCloseReason.ItemClicked;   // stays open while ticking
            DropDownOpening += (_, _) => Fill();
        }

        /// <summary>True when cards of this kind are shown.</summary>
        public bool Shows(string kind) => !_hidden.Contains(kind);

        private void Fill()
        {
            DropDownItems.Clear();
            DropDownItems.Add(new ToolStripMenuItem("Show all", null, (_, _) => Set(() => _hidden.Clear())));
            DropDownItems.Add(new ToolStripSeparator());
            foreach (string kind in _kinds().Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase))
            {
                var item = new ToolStripMenuItem(kind) { Checked = Shows(kind), CheckOnClick = true };
                item.CheckedChanged += (_, _) => Set(() => { if (item.Checked) _hidden.Remove(kind); else _hidden.Add(kind); });
                DropDownItems.Add(item);
            }
        }

        private void Set(Action change)
        {
            change();
            Text = _hidden.Count == 0 ? "Kinds" : $"Kinds ({_hidden.Count} hidden)";
            if (DropDown.Visible)
                foreach (var item in DropDownItems.OfType<ToolStripMenuItem>().Where(i => i.CheckOnClick))
                    item.Checked = Shows(item.Text ?? "");
            Changed?.Invoke();
        }
    }
}
