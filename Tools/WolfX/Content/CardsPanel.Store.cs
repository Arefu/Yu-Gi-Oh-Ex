using System.Text.Json.Nodes;
using Wolf.Editors;

namespace WolfEx
{
    /// <summary>
    /// cards.json as the place every custom card's data lives (<see cref="ICustomCardStore"/>): the Card genres, Related cards and Card links
    /// pages keep a custom card's part in its entry here ("genres", "related", "links"), and this page shows those same editors as tabs for
    /// the picked card, as the Card Manager does for game cards.
    /// </summary>
    internal sealed partial class CardsPanel : ICustomCardStore
    {
        // ---------------------------------------------------------------- ICustomCardStore

        private bool _saving;

        public bool Has(int id) => _cards.Any(card => card.Id == id);

        public IReadOnlyCollection<int> Ids => [.. _cards.Select(card => card.Id).Distinct()];

        public JsonNode? Get(int id, string key) => _cards.FirstOrDefault(card => card.Id == id)?.Extra?[key];

        public void Set(int id, string key, JsonNode? value)
        {
            if (_cards.FirstOrDefault(card => card.Id == id) is not { } card)
                return;
            if (value == null)
                card.Extra?.Remove(key);
            else
            {
                card.Extra ??= [];
                card.Extra[key] = value.Parent == null ? value : value.DeepClone();   // a node has one parent
            }
        }

        bool ICustomCardStore.Save()
        {
            if (_saving || !Dirty)
                return true;
            _saving = true;
            try
            {
                return SaveRequested?.Invoke() ?? false;
            }
            finally
            {
                _saving = false;
            }
        }

        /// <summary>The card list changed shape (loaded, added, deleted, renumbered): the pages that keep a part of each card read theirs again.</summary>
        public event Action? Changed;

        private void StoreChanged() => Changed?.Invoke();

        // ---------------------------------------------------------------- the shared editors as tabs

        private (TabPage Tab, Control Editor)[] _embedded = [];

        /// <summary>
        /// Shows these pages (Card genres, Related cards, Card links: the same instances as their own pages and the Card Manager's tabs) as
        /// tabs here, for the picked card. What they change for a custom card goes into its cards.json entry.
        /// </summary>
        internal void ShowEditors(params (string Title, Control Editor)[] editors)
        {
            _embedded = [.. editors.Select(e => (new TabPage(e.Title) { UseVisualStyleBackColor = true }, e.Editor))];
            foreach (var (tab, _) in _embedded)
                _tabs.TabPages.Add(tab);
            _tabs.SelectedIndexChanged += (_, _) => AttachEmbedded();
            VisibleChanged += (_, _) => AttachEmbedded();
        }

        /// <summary>Puts the open tab's page in it, showing only the picked card (it may have been on its own page or in the Card Manager).</summary>
        private void AttachEmbedded()
        {
            if (!Visible || Selected is not { } card)
                return;
            foreach (var (tab, editor) in _embedded)
            {
                if (tab != _tabs.SelectedTab)
                    continue;
                if (editor.Parent != tab)
                {
                    editor.Dock = DockStyle.Fill;
                    tab.Controls.Add(editor);
                }
                if (editor is ICardFocus focus)
                {
                    focus.CardOnly = true;
                    focus.ShowCard(card.Id);
                }
            }
        }
    }
}
