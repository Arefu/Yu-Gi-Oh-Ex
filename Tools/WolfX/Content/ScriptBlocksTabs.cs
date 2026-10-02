namespace WolfEx
{
    /// <summary>
    /// The script editor on a "Script" tab and the blocks on a "Blocks" tab: one or the other is shown, both always say the same thing.
    /// A change to the blocks rewrites the script at once; the blocks are redrawn from the script when their tab is opened and whenever the
    /// script changes while it is open (another card picked, a template used). A script that doesn't parse, or uses something there is no
    /// block for yet, leaves the blocks locked with a note (effect-blocks.js), so they never overwrite it.
    /// </summary>
    internal sealed class ScriptBlocksTabs : TabControl
    {
        private readonly ScriptEditor _source;
        private readonly TabPage _blocksTab = new("Blocks (drag and drop)") { UseVisualStyleBackColor = true };
        private bool _fromBlocks;

        public EffectBlocksEditor Blocks { get; } = new() { Dock = DockStyle.Fill };

        public ScriptBlocksTabs(ScriptEditor source)
        {
            _source = source;
            Dock = DockStyle.Fill;
            source.Dock = DockStyle.Fill;
            var scriptTab = new TabPage("Script") { UseVisualStyleBackColor = true };
            scriptTab.Controls.Add(source);
            _blocksTab.Controls.Add(Blocks);
            TabPages.AddRange([scriptTab, _blocksTab]);

            SelectedIndexChanged += (_, _) =>
            {
                if (SelectedTab == _blocksTab)
                    Blocks.ShowScript(_source.Text);
            };
            Blocks.ScriptChanged += script =>
            {
                if (_source.ReadOnly)
                    return;
                _fromBlocks = true;
                try { _source.Text = script; }
                finally { _fromBlocks = false; }
            };
            _source.TextChanged += (_, _) =>
            {
                if (!_fromBlocks && SelectedTab == _blocksTab)
                    Blocks.ShowScript(_source.Text);
            };
        }

        /// <summary>Puts the tabs where <paramref name="source"/> is now (a designer-made page's table cell, or a docked panel).</summary>
        public static ScriptBlocksTabs Replace(ScriptEditor source)
        {
            var parent = source.Parent!;
            if (parent is TableLayoutPanel table)
            {
                var cell = table.GetCellPosition(source);
                table.Controls.Remove(source);
                var tabs = new ScriptBlocksTabs(source);
                table.Controls.Add(tabs, cell.Column, cell.Row);
                return tabs;
            }
            int index = parent.Controls.GetChildIndex(source);
            parent.Controls.Remove(source);
            var docked = new ScriptBlocksTabs(source);
            parent.Controls.Add(docked);
            parent.Controls.SetChildIndex(docked, index);
            return docked;
        }
    }
}
