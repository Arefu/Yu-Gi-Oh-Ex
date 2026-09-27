namespace PluginManifest
{
    partial class MainForm
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
            folderLabel = new Label();
            folderBox = new TextBox();
            browseButton = new Button();
            reloadButton = new Button();
            pluginList = new ListBox();
            pluginListLabel = new Label();
            manifestGroup = new GroupBox();
            nameLabel = new Label();
            titleLabel = new Label();
            titleBox = new TextBox();
            descriptionLabel = new Label();
            descriptionBox = new TextBox();
            descriptionCount = new Label();
            enforcedBox = new CheckBox();
            requiresLabel = new Label();
            requiresList = new CheckedListBox();
            dllsLabel = new Label();
            dllsList = new CheckedListBox();
            previewLabel = new Label();
            previewBox = new TextBox();
            saveButton = new Button();
            removeButton = new Button();
            statusLabel = new Label();
            manifestGroup.SuspendLayout();
            SuspendLayout();
            //
            // folderLabel
            //
            folderLabel.AutoSize = true;
            folderLabel.Location = new Point(12, 15);
            folderLabel.Name = "folderLabel";
            folderLabel.Size = new Size(90, 15);
            folderLabel.TabIndex = 0;
            folderLabel.Text = "Plugins folder:";
            //
            // folderBox
            //
            folderBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            folderBox.Location = new Point(108, 12);
            folderBox.Name = "folderBox";
            folderBox.ReadOnly = true;
            folderBox.Size = new Size(652, 23);
            folderBox.TabIndex = 1;
            //
            // browseButton
            //
            browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browseButton.Location = new Point(766, 11);
            browseButton.Name = "browseButton";
            browseButton.Size = new Size(88, 25);
            browseButton.TabIndex = 2;
            browseButton.Text = "Browse...";
            browseButton.UseVisualStyleBackColor = true;
            browseButton.Click += browseButton_Click;
            //
            // reloadButton
            //
            reloadButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            reloadButton.Location = new Point(860, 11);
            reloadButton.Name = "reloadButton";
            reloadButton.Size = new Size(88, 25);
            reloadButton.TabIndex = 3;
            reloadButton.Text = "Reload";
            reloadButton.UseVisualStyleBackColor = true;
            reloadButton.Click += reloadButton_Click;
            //
            // pluginListLabel
            //
            pluginListLabel.AutoSize = true;
            pluginListLabel.Location = new Point(12, 48);
            pluginListLabel.Name = "pluginListLabel";
            pluginListLabel.Size = new Size(48, 15);
            pluginListLabel.TabIndex = 4;
            pluginListLabel.Text = "Plugins:";
            //
            // pluginList
            //
            pluginList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            pluginList.FormattingEnabled = true;
            pluginList.IntegralHeight = false;
            pluginList.ItemHeight = 15;
            pluginList.Location = new Point(12, 66);
            pluginList.Name = "pluginList";
            pluginList.Size = new Size(250, 522);
            pluginList.TabIndex = 5;
            pluginList.SelectedIndexChanged += pluginList_SelectedIndexChanged;
            //
            // manifestGroup
            //
            manifestGroup.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            manifestGroup.Controls.Add(nameLabel);
            manifestGroup.Controls.Add(titleLabel);
            manifestGroup.Controls.Add(titleBox);
            manifestGroup.Controls.Add(descriptionLabel);
            manifestGroup.Controls.Add(descriptionBox);
            manifestGroup.Controls.Add(descriptionCount);
            manifestGroup.Controls.Add(enforcedBox);
            manifestGroup.Controls.Add(requiresLabel);
            manifestGroup.Controls.Add(requiresList);
            manifestGroup.Controls.Add(dllsLabel);
            manifestGroup.Controls.Add(dllsList);
            manifestGroup.Controls.Add(previewLabel);
            manifestGroup.Controls.Add(previewBox);
            manifestGroup.Location = new Point(272, 48);
            manifestGroup.Name = "manifestGroup";
            manifestGroup.Size = new Size(676, 540);
            manifestGroup.TabIndex = 6;
            manifestGroup.TabStop = false;
            manifestGroup.Text = "Manifest";
            //
            // nameLabel
            //
            nameLabel.AutoSize = true;
            nameLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            nameLabel.Location = new Point(12, 22);
            nameLabel.Name = "nameLabel";
            nameLabel.Size = new Size(140, 20);
            nameLabel.TabIndex = 0;
            nameLabel.Text = "Pick a plugin on the left";
            //
            // titleLabel
            //
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(12, 58);
            titleLabel.Name = "titleLabel";
            titleLabel.Size = new Size(32, 15);
            titleLabel.TabIndex = 1;
            titleLabel.Text = "Title:";
            //
            // titleBox
            //
            titleBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            titleBox.Location = new Point(96, 55);
            titleBox.Name = "titleBox";
            titleBox.Size = new Size(568, 23);
            titleBox.TabIndex = 2;
            titleBox.TextChanged += anyField_Changed;
            //
            // descriptionLabel
            //
            descriptionLabel.AutoSize = true;
            descriptionLabel.Location = new Point(12, 88);
            descriptionLabel.Name = "descriptionLabel";
            descriptionLabel.Size = new Size(70, 15);
            descriptionLabel.TabIndex = 3;
            descriptionLabel.Text = "Description:";
            //
            // descriptionBox
            //
            descriptionBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            descriptionBox.Location = new Point(96, 85);
            descriptionBox.Name = "descriptionBox";
            descriptionBox.Size = new Size(568, 23);
            descriptionBox.TabIndex = 4;
            descriptionBox.TextChanged += anyField_Changed;
            //
            // descriptionCount
            //
            descriptionCount.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            descriptionCount.ForeColor = SystemColors.GrayText;
            descriptionCount.Location = new Point(96, 111);
            descriptionCount.Name = "descriptionCount";
            descriptionCount.Size = new Size(568, 15);
            descriptionCount.TabIndex = 5;
            descriptionCount.Text = "One short line: the game shows it in a small box, and it does not scroll.";
            //
            // enforcedBox
            //
            enforcedBox.AutoSize = true;
            enforcedBox.Location = new Point(96, 134);
            enforcedBox.Name = "enforcedBox";
            enforcedBox.Size = new Size(320, 19);
            enforcedBox.TabIndex = 6;
            enforcedBox.Text = "Enforced: always on, it can not be switched off in the list";
            enforcedBox.UseVisualStyleBackColor = true;
            enforcedBox.CheckedChanged += anyField_Changed;
            //
            // requiresLabel
            //
            requiresLabel.AutoSize = true;
            requiresLabel.Location = new Point(12, 168);
            requiresLabel.Name = "requiresLabel";
            requiresLabel.Size = new Size(300, 15);
            requiresLabel.TabIndex = 7;
            requiresLabel.Text = "Requires (these must be on for this plugin to load):";
            //
            // requiresList
            //
            requiresList.CheckOnClick = true;
            requiresList.FormattingEnabled = true;
            requiresList.IntegralHeight = false;
            requiresList.Location = new Point(12, 186);
            requiresList.Name = "requiresList";
            requiresList.Size = new Size(316, 150);
            requiresList.TabIndex = 8;
            requiresList.ItemCheck += anyList_ItemCheck;
            //
            // dllsLabel
            //
            dllsLabel.AutoSize = true;
            dllsLabel.Location = new Point(344, 168);
            dllsLabel.Name = "dllsLabel";
            dllsLabel.Size = new Size(300, 15);
            dllsLabel.TabIndex = 9;
            dllsLabel.Text = "Extra DLLs that belong to this plugin (load with it):";
            //
            // dllsList
            //
            dllsList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            dllsList.CheckOnClick = true;
            dllsList.FormattingEnabled = true;
            dllsList.IntegralHeight = false;
            dllsList.Location = new Point(344, 186);
            dllsList.Name = "dllsList";
            dllsList.Size = new Size(320, 150);
            dllsList.TabIndex = 10;
            dllsList.ItemCheck += anyList_ItemCheck;
            //
            // previewLabel
            //
            previewLabel.AutoSize = true;
            previewLabel.Location = new Point(12, 352);
            previewLabel.Name = "previewLabel";
            previewLabel.Size = new Size(120, 15);
            previewLabel.TabIndex = 11;
            previewLabel.Text = "What will be written:";
            //
            // previewBox
            //
            previewBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            previewBox.Font = new Font("Consolas", 9.75F);
            previewBox.Location = new Point(12, 370);
            previewBox.Multiline = true;
            previewBox.Name = "previewBox";
            previewBox.ReadOnly = true;
            previewBox.ScrollBars = ScrollBars.Vertical;
            previewBox.Size = new Size(652, 158);
            previewBox.TabIndex = 12;
            //
            // saveButton
            //
            saveButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            saveButton.Enabled = false;
            saveButton.Location = new Point(766, 596);
            saveButton.Name = "saveButton";
            saveButton.Size = new Size(88, 27);
            saveButton.TabIndex = 7;
            saveButton.Text = "Save";
            saveButton.UseVisualStyleBackColor = true;
            saveButton.Click += saveButton_Click;
            //
            // removeButton
            //
            removeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            removeButton.Enabled = false;
            removeButton.Location = new Point(860, 596);
            removeButton.Name = "removeButton";
            removeButton.Size = new Size(88, 27);
            removeButton.TabIndex = 8;
            removeButton.Text = "Delete";
            removeButton.UseVisualStyleBackColor = true;
            removeButton.Click += removeButton_Click;
            //
            // statusLabel
            //
            statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            statusLabel.Location = new Point(12, 601);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(748, 20);
            statusLabel.TabIndex = 9;
            //
            // MainForm
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(960, 632);
            Controls.Add(statusLabel);
            Controls.Add(removeButton);
            Controls.Add(saveButton);
            Controls.Add(manifestGroup);
            Controls.Add(pluginList);
            Controls.Add(pluginListLabel);
            Controls.Add(reloadButton);
            Controls.Add(browseButton);
            Controls.Add(folderBox);
            Controls.Add(folderLabel);
            MinimumSize = new Size(820, 600);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Plugin Manifests";
            Load += MainForm_Load;
            manifestGroup.ResumeLayout(false);
            manifestGroup.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label folderLabel;
        private TextBox folderBox;
        private Button browseButton;
        private Button reloadButton;
        private ListBox pluginList;
        private Label pluginListLabel;
        private GroupBox manifestGroup;
        private Label nameLabel;
        private Label titleLabel;
        private TextBox titleBox;
        private Label descriptionLabel;
        private TextBox descriptionBox;
        private Label descriptionCount;
        private CheckBox enforcedBox;
        private Label requiresLabel;
        private CheckedListBox requiresList;
        private Label dllsLabel;
        private CheckedListBox dllsList;
        private Label previewLabel;
        private TextBox previewBox;
        private Button saveButton;
        private Button removeButton;
        private Label statusLabel;
    }
}
