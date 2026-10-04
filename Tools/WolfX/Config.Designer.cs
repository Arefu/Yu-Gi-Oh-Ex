namespace WolfX.WolfX.File_Type_UI
{
    partial class Config
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            topPanel = new Panel();
            filterBox = new TextBox();
            filterLabel = new Label();
            browseButton = new Button();
            pathBox = new TextBox();
            split = new SplitContainer();
            listSplit = new SplitContainer();
            sectionTree = new TreeView();
            grid = new DataGridView();
            colSection = new DataGridViewTextBoxColumn();
            colSetting = new DataGridViewTextBoxColumn();
            colValue = new DataGridViewTextBoxColumn();
            colDefault = new DataGridViewTextBoxColumn();
            colInFile = new DataGridViewTextBoxColumn();
            helpText = new TextBox();
            helpTitle = new Label();
            bottomPanel = new Panel();
            statusLabel = new Label();
            saveButton = new Button();
            resetButton = new Button();
            reloadButton = new Button();
            topPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)split).BeginInit();
            split.Panel1.SuspendLayout();
            split.Panel2.SuspendLayout();
            split.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)listSplit).BeginInit();
            listSplit.Panel1.SuspendLayout();
            listSplit.Panel2.SuspendLayout();
            listSplit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)grid).BeginInit();
            bottomPanel.SuspendLayout();
            SuspendLayout();
            //
            // topPanel
            //
            topPanel.Controls.Add(filterBox);
            topPanel.Controls.Add(filterLabel);
            topPanel.Controls.Add(browseButton);
            topPanel.Controls.Add(pathBox);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(1180, 76);
            topPanel.TabIndex = 0;
            //
            // pathBox
            //
            pathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            pathBox.Location = new Point(12, 9);
            pathBox.Name = "pathBox";
            pathBox.ReadOnly = true;
            pathBox.Size = new Size(1075, 23);
            pathBox.TabIndex = 0;
            //
            // browseButton
            //
            browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browseButton.Location = new Point(1093, 8);
            browseButton.Name = "browseButton";
            browseButton.Size = new Size(75, 25);
            browseButton.TabIndex = 1;
            browseButton.Text = "Browse...";
            browseButton.UseVisualStyleBackColor = true;
            browseButton.Click += browseButton_Click;
            //
            // filterLabel
            //
            filterLabel.AutoSize = true;
            filterLabel.Location = new Point(12, 46);
            filterLabel.Name = "filterLabel";
            filterLabel.Size = new Size(36, 15);
            filterLabel.Text = "Filter:";
            //
            // filterBox
            //
            filterBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            filterBox.Location = new Point(54, 43);
            filterBox.Name = "filterBox";
            filterBox.PlaceholderText = "Type a plugin, a setting or a word from its help";
            filterBox.Size = new Size(1114, 23);
            filterBox.TabIndex = 2;
            filterBox.TextChanged += filterBox_TextChanged;
            //
            // split
            //
            split.Dock = DockStyle.Fill;
            split.Location = new Point(0, 76);
            split.Name = "split";
            split.Orientation = Orientation.Horizontal;
            split.Panel1.Controls.Add(listSplit);
            split.Panel2.Controls.Add(helpText);
            split.Panel2.Controls.Add(helpTitle);
            split.Size = new Size(1180, 560);
            split.SplitterDistance = 400;
            split.TabIndex = 1;
            //
            // listSplit
            //
            listSplit.Dock = DockStyle.Fill;
            listSplit.FixedPanel = FixedPanel.Panel1;
            listSplit.Name = "listSplit";
            listSplit.Panel1.Controls.Add(sectionTree);
            listSplit.Panel1MinSize = 150;
            listSplit.Panel2.Controls.Add(grid);
            listSplit.Size = new Size(1180, 400);
            listSplit.SplitterDistance = 260;
            listSplit.TabIndex = 0;
            //
            // sectionTree
            //
            sectionTree.Dock = DockStyle.Fill;
            sectionTree.FullRowSelect = true;
            sectionTree.HideSelection = false;
            sectionTree.ItemHeight = 20;
            sectionTree.Name = "sectionTree";
            sectionTree.ShowLines = false;
            sectionTree.TabIndex = 0;
            sectionTree.AfterSelect += sectionTree_AfterSelect;
            //
            // grid
            //
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.BackgroundColor = SystemColors.Window;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.Columns.AddRange(new DataGridViewColumn[] { colSection, colSetting, colValue, colDefault, colInFile });
            grid.Dock = DockStyle.Fill;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.MultiSelect = false;
            grid.Name = "grid";
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.TabIndex = 1;
            grid.CellValueChanged += grid_CellValueChanged;
            grid.CurrentCellDirtyStateChanged += grid_CurrentCellDirtyStateChanged;
            grid.DataError += grid_DataError;
            grid.SelectionChanged += grid_SelectionChanged;
            //
            // colSection
            //
            colSection.HeaderText = "Plugin";
            colSection.Name = "colSection";
            colSection.ReadOnly = true;
            colSection.Visible = false;   // the plugin list on the left says which plugin the rows belong to
            colSection.Width = 200;
            //
            // colSetting
            //
            colSetting.HeaderText = "Setting";
            colSetting.Name = "colSetting";
            colSetting.ReadOnly = true;
            colSetting.Width = 200;
            //
            // colValue
            //
            colValue.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colValue.HeaderText = "Value";
            colValue.Name = "colValue";
            //
            // colDefault
            //
            colDefault.HeaderText = "Default";
            colDefault.Name = "colDefault";
            colDefault.ReadOnly = true;
            colDefault.Width = 140;
            //
            // colInFile
            //
            colInFile.HeaderText = "Set in Config.ini";
            colInFile.Name = "colInFile";
            colInFile.ReadOnly = true;
            colInFile.Width = 110;
            //
            // helpTitle
            //
            helpTitle.Dock = DockStyle.Top;
            helpTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            helpTitle.Name = "helpTitle";
            helpTitle.Padding = new Padding(6, 6, 0, 2);
            helpTitle.Size = new Size(1180, 26);
            //
            // helpText
            //
            helpText.BorderStyle = BorderStyle.None;
            helpText.Dock = DockStyle.Fill;
            helpText.Multiline = true;
            helpText.Name = "helpText";
            helpText.ReadOnly = true;
            helpText.Font = new Font("Segoe UI", 10F);
            helpText.ScrollBars = ScrollBars.Vertical;
            helpText.TabIndex = 1;
            //
            // bottomPanel
            //
            bottomPanel.Controls.Add(statusLabel);
            bottomPanel.Controls.Add(resetButton);
            bottomPanel.Controls.Add(reloadButton);
            bottomPanel.Controls.Add(saveButton);
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Location = new Point(0, 636);
            bottomPanel.Name = "bottomPanel";
            bottomPanel.Size = new Size(1180, 44);
            bottomPanel.TabIndex = 2;
            //
            // statusLabel
            //
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusLabel.Location = new Point(12, 12);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(790, 18);
            //
            // resetButton
            //
            resetButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            resetButton.Location = new Point(808, 9);
            resetButton.Name = "resetButton";
            resetButton.Size = new Size(120, 25);
            resetButton.Text = "Reset to default";
            resetButton.UseVisualStyleBackColor = true;
            resetButton.Click += resetButton_Click;
            //
            // reloadButton
            //
            reloadButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            reloadButton.Location = new Point(934, 9);
            reloadButton.Name = "reloadButton";
            reloadButton.Size = new Size(110, 25);
            reloadButton.Text = "Discard changes";
            reloadButton.UseVisualStyleBackColor = true;
            reloadButton.Click += reloadButton_Click;
            //
            // saveButton
            //
            saveButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            saveButton.Location = new Point(1050, 9);
            saveButton.Name = "saveButton";
            saveButton.Size = new Size(118, 25);
            saveButton.Text = "Save";
            saveButton.UseVisualStyleBackColor = true;
            saveButton.Click += saveButton_Click;
            //
            // Config
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1200, 720);
            Controls.Add(split);
            Controls.Add(bottomPanel);
            Controls.Add(topPanel);
            MinimumSize = new Size(800, 520);
            Name = "Config";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Plugin Settings (Config.ini)";
            Load += Config_Load;
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            split.Panel1.ResumeLayout(false);
            split.Panel2.ResumeLayout(false);
            split.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)split).EndInit();
            split.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)grid).EndInit();
            listSplit.Panel1.ResumeLayout(false);
            listSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)listSplit).EndInit();
            listSplit.ResumeLayout(false);
            bottomPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private Panel topPanel;
        private TextBox filterBox;
        private Label filterLabel;
        private Button browseButton;
        private TextBox pathBox;
        private SplitContainer split;
        private SplitContainer listSplit;
        private TreeView sectionTree;
        private DataGridView grid;
        private DataGridViewTextBoxColumn colSection;
        private DataGridViewTextBoxColumn colSetting;
        private DataGridViewTextBoxColumn colValue;
        private DataGridViewTextBoxColumn colDefault;
        private DataGridViewTextBoxColumn colInFile;
        private TextBox helpText;
        private Label helpTitle;
        private Panel bottomPanel;
        private Label statusLabel;
        private Button saveButton;
        private Button resetButton;
        private Button reloadButton;
    }
}
