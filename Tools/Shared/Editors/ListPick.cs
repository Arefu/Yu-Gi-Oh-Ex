namespace Wolf.Editors
{
    /// <summary>
    /// A page's list picks its first row when the page is shown with nothing picked, so the page opens on something instead of a greyed
    /// "Pick a ..." panel. (A selection made while the data loads, before the list has a window, is lost - a virtual ListView drops it.)
    /// </summary>
    public static class ListPick
    {
        public static void FirstWhenShown(ListView list)
        {
            void PickFirst()
            {
                if (list.IsDisposed || !list.IsHandleCreated || !list.Visible || list.SelectedIndices.Count > 0)
                    return;
                int count = list.VirtualMode ? list.VirtualListSize : list.Items.Count;
                if (count == 0)
                    return;
                list.SelectedIndices.Add(0);
                list.EnsureVisible(0);
            }

            // after the page is on screen (and again whenever it is shown), once the list has its window
            list.VisibleChanged += (_, _) =>
            {
                if (list.Visible && list.IsHandleCreated)
                    list.BeginInvoke(PickFirst);
            };
            list.HandleCreated += (_, _) => list.BeginInvoke(PickFirst);
        }
    }
}
