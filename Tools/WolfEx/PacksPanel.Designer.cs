#nullable disable

namespace WolfEx
{
    partial class PacksPanel
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
            _list = new ListBox();
            right = new TableLayoutPanel();
            _info = new Label();
            lblCommon = new Label();
            _common = new TextBox();
            btnCommonByName = new Button();
            lblRare = new Label();
            _rare = new TextBox();
            btnRareByName = new Button();
            _replace = new CheckBox();
            right.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)split).BeginInit();
            split.Panel1.SuspendLayout();
            split.Panel2.SuspendLayout();
            split.SuspendLayout();
            SuspendLayout();
            // 
            // split
            // 
            split.Panel1.Controls.Add(_list);
            split.Panel2.Controls.Add(right);
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel1;
            split.SplitterDistance = 340;
            split.Name = "split";
            // 
            // _list
            // 
            _list.Dock = DockStyle.Fill;
            _list.IntegralHeight = false;
            _list.FormattingEnabled = true;
            _list.Name = "_list";
            _list.SelectedIndexChanged += List_SelectedIndexChanged;
            // 
            // right
            // 
            right.Controls.Add(_info, 0, 0);
            right.Controls.Add(lblCommon, 0, 1);
            right.Controls.Add(_common, 0, 2);
            right.Controls.Add(btnCommonByName, 0, 3);
            right.Controls.Add(lblRare, 0, 4);
            right.Controls.Add(_rare, 0, 5);
            right.Controls.Add(btnRareByName, 0, 6);
            right.Controls.Add(_replace, 0, 7);
            right.AutoScroll = true;
            right.ColumnCount = 1;
            right.Dock = DockStyle.Fill;
            right.Padding = new Padding(8);
            right.RowCount = 8;
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.Name = "right";
            // 
            // _info
            // 
            _info.AutoSize = true;
            _info.Padding = new Padding(0, 6, 0, 6);
            _info.Name = "_info";
            // 
            // lblCommon
            // 
            lblCommon.Text = "Extra common cards (card ids, separated by commas or spaces):";
            lblCommon.AutoSize = true;
            lblCommon.Name = "lblCommon";
            // 
            // _common
            // 
            _common.Multiline = true;
            _common.Height = 120;
            _common.ScrollBars = ScrollBars.Vertical;
            _common.Dock = DockStyle.Top;
            _common.Name = "_common";
            _common.TextChanged += Editor_Changed;
            // 
            // btnCommonByName
            // 
            btnCommonByName.Text = "Add common cards by name...";
            btnCommonByName.AutoSize = true;
            btnCommonByName.UseVisualStyleBackColor = true;
            btnCommonByName.Name = "btnCommonByName";
            btnCommonByName.Click += btnCommonByName_Click;
            // 
            // lblRare
            // 
            lblRare.Text = "Extra rare cards:";
            lblRare.AutoSize = true;
            lblRare.Padding = new Padding(0, 8, 0, 0);
            lblRare.Name = "lblRare";
            // 
            // _rare
            // 
            _rare.Multiline = true;
            _rare.Height = 120;
            _rare.ScrollBars = ScrollBars.Vertical;
            _rare.Dock = DockStyle.Top;
            _rare.Name = "_rare";
            _rare.TextChanged += Editor_Changed;
            // 
            // btnRareByName
            // 
            btnRareByName.Text = "Add rare cards by name...";
            btnRareByName.AutoSize = true;
            btnRareByName.UseVisualStyleBackColor = true;
            btnRareByName.Name = "btnRareByName";
            btnRareByName.Click += btnRareByName_Click;
            // 
            // _replace
            // 
            _replace.Text = "Replace the game's cards in this pack (instead of adding to them)";
            _replace.AutoSize = true;
            _replace.Padding = new Padding(0, 8, 0, 0);
            _replace.UseVisualStyleBackColor = true;
            _replace.Name = "_replace";
            _replace.CheckedChanged += Editor_Changed;
            // 
            // this
            // 
            Controls.Add(split);
            AutoScaleMode = AutoScaleMode.Font;
            Name = "PacksPanel";
            Size = new Size(1000, 640);
            split.Panel1.ResumeLayout(false);
            split.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)split).EndInit();
            split.ResumeLayout(false);
            right.ResumeLayout(false);
            right.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private SplitContainer split;
        private ListBox _list;
        private TableLayoutPanel right;
        private Label _info;
        private Label lblCommon;
        private TextBox _common;
        private Button btnCommonByName;
        private Label lblRare;
        private TextBox _rare;
        private Button btnRareByName;
        private CheckBox _replace;
    }
}
