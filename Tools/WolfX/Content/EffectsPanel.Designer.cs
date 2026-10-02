#nullable disable

namespace WolfEx
{
    partial class EffectsPanel
    {
        /// <summary>Required designer variable.</summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>Clean up any resources being used.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>Required method for Designer support - do not modify the contents of this method with the code editor.</summary>
        private void InitializeComponent()
        {
            split = new SplitContainer();
            left = new TableLayoutPanel();
            _filter = new TextBox();
            _btnSelectAll = new Button();
            _list = new CheckedListBox();
            _btnClearChecks = new Button();
            _btnApplyAll = new Button();
            _btnAuto = new Button();
            right = new SplitContainer();
            top = new TableLayoutPanel();
            lblSource = new Label();
            _source = new ScriptEditor();
            buttons = new FlowLayoutPanel();
            _btnCompile = new Button();
            _status = new Label();
            bottom = new TableLayoutPanel();
            lblCompiled = new Label();
            _compiled = new TextBox();
            ((System.ComponentModel.ISupportInitialize)split).BeginInit();
            split.Panel1.SuspendLayout();
            split.Panel2.SuspendLayout();
            split.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)right).BeginInit();
            right.Panel1.SuspendLayout();
            right.Panel2.SuspendLayout();
            right.SuspendLayout();
            top.SuspendLayout();
            buttons.SuspendLayout();
            bottom.SuspendLayout();
            SuspendLayout();
            //
            // split
            //
            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(right);
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel1;
            split.SplitterDistance = 260;
            split.Name = "split";
            //
            // left
            //
            left.ColumnCount = 1;
            left.RowCount = 4;
            left.Dock = DockStyle.Fill;
            left.Controls.Add(_filter, 0, 0);
            left.Controls.Add(_list, 0, 1);
            left.Controls.Add(_btnSelectAll, 0, 2);
            left.Controls.Add(_btnClearChecks, 0, 3);
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Name = "left";
            //
            // _filter
            //
            _filter.Dock = DockStyle.Top;
            _filter.PlaceholderText = "Filter: name, id or text";
            _filter.Name = "_filter";
            _filter.TextChanged += Filter_TextChanged;
            //
            // _btnSelectAll
            //
            _btnSelectAll.Text = "Check all shown";
            _btnSelectAll.Dock = DockStyle.Top;
            _btnSelectAll.UseVisualStyleBackColor = true;
            _btnSelectAll.Name = "_btnSelectAll";
            _btnSelectAll.Click += btnSelectAll_Click;
            //
            // _btnClearChecks
            //
            _btnClearChecks.Text = "Clear all checks";
            _btnClearChecks.Dock = DockStyle.Top;
            _btnClearChecks.UseVisualStyleBackColor = true;
            _btnClearChecks.Name = "_btnClearChecks";
            _btnClearChecks.Click += btnClearChecks_Click;
            //
            // _btnApplyAll
            //
            _btnApplyAll.Text = "Apply script to all checked";
            _btnApplyAll.AutoSize = true;
            _btnApplyAll.Margin = new Padding(0, 6, 8, 0);
            _btnApplyAll.UseVisualStyleBackColor = true;
            _btnApplyAll.Name = "_btnApplyAll";
            _btnApplyAll.Click += btnApplyAll_Click;
            //
            // _btnAuto
            //
            _btnAuto.Text = "Attach from card text...";
            _btnAuto.AutoSize = true;
            _btnAuto.Margin = new Padding(0, 6, 8, 0);
            _btnAuto.UseVisualStyleBackColor = true;
            _btnAuto.Name = "_btnAuto";
            _btnAuto.Click += btnAuto_Click;
            //
            // _list
            //
            _list.Dock = DockStyle.Fill;
            _list.CheckOnClick = true;
            _list.IntegralHeight = false;
            _list.ItemCheck += List_ItemCheck;
            _list.FormattingEnabled = true;
            _list.Name = "_list";
            _list.SelectedIndexChanged += List_SelectedIndexChanged;
            //
            // right
            //
            right.Dock = DockStyle.Fill;
            right.Orientation = Orientation.Horizontal;
            right.Panel1.Controls.Add(top);
            right.Panel2.Controls.Add(bottom);
            right.SplitterDistance = 360;
            right.Name = "right";
            //
            // top
            //
            top.ColumnCount = 1;
            top.RowCount = 3;
            top.Dock = DockStyle.Fill;
            top.Padding = new Padding(8);
            top.Controls.Add(lblSource, 0, 0);
            top.Controls.Add(_source, 0, 1);
            top.Controls.Add(buttons, 0, 2);
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.Name = "top";
            //
            // lblSource
            //
            lblSource.Text = "EffectScript source (see Script\\EffectScript.g4 for the grammar):";
            lblSource.AutoSize = true;
            lblSource.Dock = DockStyle.Top;
            lblSource.Padding = new Padding(0, 0, 0, 4);
            lblSource.Name = "lblSource";
            //
            // _source
            //
            _source.Dock = DockStyle.Fill;
            _source.Name = "_source";
            _source.TextChanged += Source_TextChanged;
            //
            // buttons
            //
            buttons.Dock = DockStyle.Top;
            buttons.AutoSize = true;
            buttons.Controls.Add(_btnCompile);
            buttons.Controls.Add(_btnApplyAll);
            buttons.Controls.Add(_btnAuto);
            buttons.Controls.Add(_status);
            buttons.Name = "buttons";
            //
            // _btnCompile
            //
            _btnCompile.Text = "Compile";
            _btnCompile.AutoSize = true;
            _btnCompile.Margin = new Padding(0, 6, 8, 0);
            _btnCompile.UseVisualStyleBackColor = true;
            _btnCompile.Name = "_btnCompile";
            _btnCompile.Click += btnCompile_Click;
            //
            // _status
            //
            _status.AutoSize = true;
            _status.Anchor = AnchorStyles.Left;
            _status.Padding = new Padding(0, 12, 0, 0);
            _status.Name = "_status";
            //
            // bottom
            //
            bottom.ColumnCount = 1;
            bottom.RowCount = 2;
            bottom.Dock = DockStyle.Fill;
            bottom.Padding = new Padding(8);
            bottom.Controls.Add(lblCompiled, 0, 0);
            bottom.Controls.Add(_compiled, 0, 1);
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            bottom.Name = "bottom";
            //
            // lblCompiled
            //
            lblCompiled.Text = "Compiled (the card's \"effectClone\" the game reads - read only):";
            lblCompiled.AutoSize = true;
            lblCompiled.Dock = DockStyle.Top;
            lblCompiled.Padding = new Padding(0, 0, 0, 4);
            lblCompiled.Name = "lblCompiled";
            //
            // _compiled
            //
            _compiled.Dock = DockStyle.Fill;
            _compiled.Multiline = true;
            _compiled.ScrollBars = ScrollBars.Both;
            _compiled.WordWrap = false;
            _compiled.ReadOnly = true;
            _compiled.Font = new Font(FontFamily.GenericMonospace, 9F);
            _compiled.BackColor = SystemColors.Control;
            _compiled.Name = "_compiled";
            //
            // EffectsPanel
            //
            Controls.Add(split);
            AutoScaleMode = AutoScaleMode.Font;
            Name = "EffectsPanel";
            Size = new Size(1000, 640);
            split.Panel1.ResumeLayout(false);
            split.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)split).EndInit();
            split.ResumeLayout(false);
            right.Panel1.ResumeLayout(false);
            right.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)right).EndInit();
            right.ResumeLayout(false);
            top.ResumeLayout(false);
            top.PerformLayout();
            buttons.ResumeLayout(false);
            buttons.PerformLayout();
            bottom.ResumeLayout(false);
            bottom.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private SplitContainer split;
        private TableLayoutPanel left;
        private TextBox _filter;
        private Button _btnSelectAll;
        private Button _btnApplyAll;
        private Button _btnAuto;
        private CheckedListBox _list;
        private Button _btnClearChecks;
        private SplitContainer right;
        private TableLayoutPanel top;
        private Label lblSource;
        private ScriptEditor _source;
        private FlowLayoutPanel buttons;
        private Button _btnCompile;
        private Label _status;
        private TableLayoutPanel bottom;
        private Label lblCompiled;
        private TextBox _compiled;
    }
}
