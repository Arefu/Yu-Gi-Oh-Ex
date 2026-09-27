#nullable disable

namespace WolfX.Types
{
    partial class StartingCollectionPage
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
            _info = new Label();
            bar = new FlowLayoutPanel();
            top = new TableLayoutPanel();
            _cards = new DataGridView();
            decksGroup = new GroupBox();
            _decks = new ListBox();
            btnAddByName = new Button();
            btnRemove = new Button();
            btnLoadJson = new Button();
            btnSaveJson = new Button();
            btnWriteUnlocks = new Button();
            _replaceDefaults = new CheckBox();
            lblGameFolder = new Label();
            _gameFolder = new TextBox();
            btnBrowse = new Button();
            btnBuild = new Button();
            bar.SuspendLayout();
            top.SuspendLayout();
            decksGroup.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)split).BeginInit();
            split.Panel1.SuspendLayout();
            split.Panel2.SuspendLayout();
            split.SuspendLayout();
            SuspendLayout();
            // 
            // split
            // 
            split.Panel1.Controls.Add(_cards);
            split.Panel2.Controls.Add(decksGroup);
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel2;
            split.SplitterDistance = 560;
            split.Name = "split";
            // 
            // _info
            // 
            _info.AutoSize = true;
            _info.Dock = DockStyle.Bottom;
            _info.Padding = new Padding(4, 6, 0, 4);
            _info.Name = "_info";
            // 
            // bar
            // 
            bar.Controls.Add(btnAddByName);
            bar.Controls.Add(btnRemove);
            bar.Controls.Add(btnLoadJson);
            bar.Controls.Add(btnSaveJson);
            bar.Controls.Add(btnWriteUnlocks);
            bar.Controls.Add(_replaceDefaults);
            bar.Dock = DockStyle.Top;
            bar.Height = 34;
            bar.Padding = new Padding(4);
            bar.Name = "bar";
            // 
            // top
            // 
            top.Controls.Add(lblGameFolder, 0, 0);
            top.Controls.Add(_gameFolder, 1, 0);
            top.Controls.Add(btnBrowse, 2, 0);
            top.Controls.Add(btnBuild, 3, 0);
            top.ColumnCount = 4;
            top.Dock = DockStyle.Top;
            top.Height = 38;
            top.Padding = new Padding(4);
            top.RowCount = 1;
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            top.Name = "top";
            // 
            // _cards
            // 
            _cards.Dock = DockStyle.Fill;
            _cards.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _cards.RowHeadersVisible = false;
            _cards.RowHeadersWidth = 51;
            _cards.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _cards.Name = "_cards";
            // 
            // decksGroup
            // 
            decksGroup.Controls.Add(_decks);
            decksGroup.Dock = DockStyle.Fill;
            decksGroup.Text = "Starter decks the game grants";
            decksGroup.Name = "decksGroup";
            // 
            // _decks
            // 
            _decks.Dock = DockStyle.Fill;
            _decks.FormattingEnabled = true;
            _decks.IntegralHeight = false;
            _decks.Name = "_decks";
            // 
            // btnAddByName
            // 
            btnAddByName.Text = "Add cards by name...";
            btnAddByName.AutoSize = true;
            btnAddByName.UseVisualStyleBackColor = true;
            btnAddByName.Name = "btnAddByName";
            btnAddByName.Click += btnAddByName_Click;
            // 
            // btnRemove
            // 
            btnRemove.Text = "Remove selected";
            btnRemove.AutoSize = true;
            btnRemove.UseVisualStyleBackColor = true;
            btnRemove.Name = "btnRemove";
            btnRemove.Click += btnRemove_Click;
            // 
            // btnLoadJson
            // 
            btnLoadJson.Text = "Load JSON...";
            btnLoadJson.AutoSize = true;
            btnLoadJson.UseVisualStyleBackColor = true;
            btnLoadJson.Name = "btnLoadJson";
            btnLoadJson.Click += btnLoadJson_Click;
            // 
            // btnSaveJson
            // 
            btnSaveJson.Text = "Save starting_collection.json...";
            btnSaveJson.AutoSize = true;
            btnSaveJson.UseVisualStyleBackColor = true;
            btnSaveJson.Name = "btnSaveJson";
            btnSaveJson.Click += btnSaveJson_Click;
            // 
            // btnWriteUnlocks
            // 
            btnWriteUnlocks.Text = "Write Yu-Gi-Oh-Ex/unlocks.json";
            btnWriteUnlocks.AutoSize = true;
            btnWriteUnlocks.UseVisualStyleBackColor = true;
            btnWriteUnlocks.Name = "btnWriteUnlocks";
            btnWriteUnlocks.Click += btnWriteUnlocks_Click;
            // 
            // _replaceDefaults
            // 
            _replaceDefaults.Text = "Replace the game's starting cards (replaceDefaults)";
            _replaceDefaults.AutoSize = true;
            _replaceDefaults.Checked = true;
            _replaceDefaults.CheckState = CheckState.Checked;
            _replaceDefaults.UseVisualStyleBackColor = true;
            _replaceDefaults.Name = "_replaceDefaults";
            // 
            // lblGameFolder
            // 
            lblGameFolder.Text = "Game folder:";
            lblGameFolder.AutoSize = true;
            lblGameFolder.Padding = new Padding(0, 6, 4, 0);
            lblGameFolder.Name = "lblGameFolder";
            // 
            // _gameFolder
            // 
            _gameFolder.ReadOnly = true;
            _gameFolder.Dock = DockStyle.Fill;
            _gameFolder.Name = "_gameFolder";
            // 
            // btnBrowse
            // 
            btnBrowse.Text = "Browse...";
            btnBrowse.AutoSize = true;
            btnBrowse.UseVisualStyleBackColor = true;
            btnBrowse.Name = "btnBrowse";
            btnBrowse.Click += btnBrowse_Click;
            // 
            // btnBuild
            // 
            btnBuild.Text = "Build from game data";
            btnBuild.AutoSize = true;
            btnBuild.UseVisualStyleBackColor = true;
            btnBuild.Name = "btnBuild";
            btnBuild.Click += btnBuild_Click;
            // 
            // this
            // 
            Controls.Add(split);
            Controls.Add(_info);
            Controls.Add(bar);
            Controls.Add(top);
            AutoScaleMode = AutoScaleMode.Font;
            Name = "StartingCollectionPage";
            Size = new Size(1000, 640);
            split.Panel1.ResumeLayout(false);
            split.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)split).EndInit();
            split.ResumeLayout(false);
            decksGroup.ResumeLayout(false);
            decksGroup.PerformLayout();
            top.ResumeLayout(false);
            top.PerformLayout();
            bar.ResumeLayout(false);
            bar.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private SplitContainer split;
        private Label _info;
        private FlowLayoutPanel bar;
        private TableLayoutPanel top;
        private DataGridView _cards;
        private GroupBox decksGroup;
        private ListBox _decks;
        private Button btnAddByName;
        private Button btnRemove;
        private Button btnLoadJson;
        private Button btnSaveJson;
        private Button btnWriteUnlocks;
        private CheckBox _replaceDefaults;
        private Label lblGameFolder;
        private TextBox _gameFolder;
        private Button btnBrowse;
        private Button btnBuild;
    }
}
