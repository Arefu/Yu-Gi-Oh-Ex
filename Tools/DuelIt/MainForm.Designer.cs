namespace DuelIt
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            menuStrip = new MenuStrip();
            fileMenu = new ToolStripMenuItem();
            openMenuItem = new ToolStripMenuItem();
            reloadMenuItem = new ToolStripMenuItem();
            gameFolderMenuItem = new ToolStripMenuItem();
            fileSeparator = new ToolStripSeparator();
            exitMenuItem = new ToolStripMenuItem();
            viewMenu = new ToolStripMenuItem();
            uncensoredMenuItem = new ToolStripMenuItem();
            toolStrip = new ToolStrip();
            duelLabel = new ToolStripLabel();
            duelCombo = new ToolStripComboBox();
            toolSeparator1 = new ToolStripSeparator();
            firstButton = new ToolStripButton();
            previousButton = new ToolStripButton();
            playButton = new ToolStripButton();
            nextButton = new ToolStripButton();
            lastButton = new ToolStripButton();
            toolSeparator2 = new ToolStripSeparator();
            speedLabel = new ToolStripLabel();
            speedCombo = new ToolStripComboBox();
            keyEventsButton = new ToolStripButton();
            stepLabel = new ToolStripLabel();
            splitContainer = new SplitContainer();
            board = new BoardView();
            eventList = new ListView();
            numberColumn = new ColumnHeader();
            timeColumn = new ColumnHeader();
            tagColumn = new ColumnHeader();
            textColumn = new ColumnHeader();
            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel();
            playTimer = new System.Windows.Forms.Timer(components);
            menuStrip.SuspendLayout();
            toolStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            statusStrip.SuspendLayout();
            SuspendLayout();
            //
            // menuStrip
            //
            menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, viewMenu });
            menuStrip.Location = new Point(0, 0);
            menuStrip.Name = "menuStrip";
            menuStrip.Size = new Size(1384, 24);
            menuStrip.TabIndex = 0;
            //
            // fileMenu
            //
            fileMenu.DropDownItems.AddRange(new ToolStripItem[] { openMenuItem, reloadMenuItem, gameFolderMenuItem, fileSeparator, exitMenuItem });
            fileMenu.Name = "fileMenu";
            fileMenu.Size = new Size(37, 20);
            fileMenu.Text = "&File";
            //
            // openMenuItem
            //
            openMenuItem.Name = "openMenuItem";
            openMenuItem.ShortcutKeys = Keys.Control | Keys.O;
            openMenuItem.Size = new Size(220, 22);
            openMenuItem.Text = "&Open duel log...";
            openMenuItem.Click += OpenMenuItem_Click;
            //
            // reloadMenuItem
            //
            reloadMenuItem.Name = "reloadMenuItem";
            reloadMenuItem.ShortcutKeys = Keys.F5;
            reloadMenuItem.Size = new Size(220, 22);
            reloadMenuItem.Text = "&Reload";
            reloadMenuItem.Click += ReloadMenuItem_Click;
            //
            // gameFolderMenuItem
            //
            gameFolderMenuItem.Name = "gameFolderMenuItem";
            gameFolderMenuItem.Size = new Size(220, 22);
            gameFolderMenuItem.Text = "&Game folder (card names)...";
            gameFolderMenuItem.Click += GameFolderMenuItem_Click;
            //
            // fileSeparator
            //
            fileSeparator.Name = "fileSeparator";
            fileSeparator.Size = new Size(217, 6);
            //
            // exitMenuItem
            //
            exitMenuItem.Name = "exitMenuItem";
            exitMenuItem.Size = new Size(220, 22);
            exitMenuItem.Text = "E&xit";
            exitMenuItem.Click += ExitMenuItem_Click;
            // 
            // viewMenu
            // 
            viewMenu.DropDownItems.AddRange(new ToolStripItem[] { uncensoredMenuItem });
            viewMenu.Name = "viewMenu";
            viewMenu.Size = new Size(44, 20);
            viewMenu.Text = "&View";
            // 
            // uncensoredMenuItem
            // 
            uncensoredMenuItem.CheckOnClick = true;
            uncensoredMenuItem.Name = "uncensoredMenuItem";
            uncensoredMenuItem.Size = new Size(220, 22);
            uncensoredMenuItem.Text = "&Uncensored card art";
            uncensoredMenuItem.ToolTipText = "Use the game's uncensored illustrations (2020.full.illust_a.jpg.zib) where it has them";
            uncensoredMenuItem.CheckedChanged += UncensoredMenuItem_CheckedChanged;
            //
            // toolStrip
            //
            toolStrip.Items.AddRange(new ToolStripItem[] { duelLabel, duelCombo, toolSeparator1, firstButton, previousButton, playButton, nextButton, lastButton, toolSeparator2, speedLabel, speedCombo, keyEventsButton, stepLabel });
            toolStrip.Location = new Point(0, 24);
            toolStrip.Name = "toolStrip";
            toolStrip.Size = new Size(1384, 25);
            toolStrip.TabIndex = 1;
            //
            // duelLabel
            //
            duelLabel.Name = "duelLabel";
            duelLabel.Size = new Size(33, 22);
            duelLabel.Text = "Duel:";
            //
            // duelCombo
            //
            duelCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            duelCombo.Name = "duelCombo";
            duelCombo.Size = new Size(460, 25);
            duelCombo.SelectedIndexChanged += DuelCombo_SelectedIndexChanged;
            //
            // toolSeparator1
            //
            toolSeparator1.Name = "toolSeparator1";
            toolSeparator1.Size = new Size(6, 25);
            //
            // firstButton
            //
            firstButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            firstButton.Name = "firstButton";
            firstButton.Size = new Size(27, 22);
            firstButton.Text = "|<";
            firstButton.ToolTipText = "First event (Home)";
            firstButton.Click += FirstButton_Click;
            //
            // previousButton
            //
            previousButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            previousButton.Name = "previousButton";
            previousButton.Size = new Size(23, 22);
            previousButton.Text = "<";
            previousButton.ToolTipText = "Previous event (Left)";
            previousButton.Click += PreviousButton_Click;
            //
            // playButton
            //
            playButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            playButton.Name = "playButton";
            playButton.Size = new Size(33, 22);
            playButton.Text = "Play";
            playButton.ToolTipText = "Play / pause (Space)";
            playButton.Click += PlayButton_Click;
            //
            // nextButton
            //
            nextButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            nextButton.Name = "nextButton";
            nextButton.Size = new Size(23, 22);
            nextButton.Text = ">";
            nextButton.ToolTipText = "Next event (Right)";
            nextButton.Click += NextButton_Click;
            //
            // lastButton
            //
            lastButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            lastButton.Name = "lastButton";
            lastButton.Size = new Size(27, 22);
            lastButton.Text = ">|";
            lastButton.ToolTipText = "Last event (End)";
            lastButton.Click += LastButton_Click;
            //
            // toolSeparator2
            //
            toolSeparator2.Name = "toolSeparator2";
            toolSeparator2.Size = new Size(6, 25);
            //
            // speedLabel
            //
            speedLabel.Name = "speedLabel";
            speedLabel.Size = new Size(42, 22);
            speedLabel.Text = "Speed:";
            //
            // speedCombo
            //
            speedCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            speedCombo.Items.AddRange(new object[] { "0.5x", "1x", "2x", "4x", "10x" });
            speedCombo.Name = "speedCombo";
            speedCombo.Size = new Size(70, 25);
            speedCombo.SelectedIndexChanged += SpeedCombo_SelectedIndexChanged;
            //
            // keyEventsButton
            //
            keyEventsButton.Checked = true;
            keyEventsButton.CheckOnClick = true;
            keyEventsButton.CheckState = CheckState.Checked;
            keyEventsButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            keyEventsButton.Name = "keyEventsButton";
            keyEventsButton.Size = new Size(99, 22);
            keyEventsButton.Text = "Step key events";
            keyEventsButton.ToolTipText = "Step and play only through card moves, LP changes, turns, phases, chains and results (the list still shows everything)";
            //
            // stepLabel
            //
            stepLabel.Alignment = ToolStripItemAlignment.Right;
            stepLabel.Name = "stepLabel";
            stepLabel.Size = new Size(0, 22);
            //
            // splitContainer
            //
            splitContainer.Dock = DockStyle.Fill;
            splitContainer.Location = new Point(0, 49);
            splitContainer.Name = "splitContainer";
            //
            // splitContainer.Panel1
            //
            splitContainer.Panel1.Controls.Add(board);
            //
            // splitContainer.Panel2
            //
            splitContainer.Panel2.Controls.Add(eventList);
            splitContainer.Size = new Size(1384, 790);
            splitContainer.SplitterDistance = 860;
            splitContainer.TabIndex = 2;
            //
            // board
            //
            board.Dock = DockStyle.Fill;
            board.Font = new Font("Segoe UI", 8.25F);
            board.Location = new Point(0, 0);
            board.Name = "board";
            board.Size = new Size(860, 790);
            board.TabIndex = 0;
            //
            // eventList
            //
            eventList.Columns.AddRange(new ColumnHeader[] { numberColumn, timeColumn, tagColumn, textColumn });
            eventList.Dock = DockStyle.Fill;
            eventList.FullRowSelect = true;
            eventList.HideSelection = false;
            eventList.Location = new Point(0, 0);
            eventList.MultiSelect = false;
            eventList.Name = "eventList";
            eventList.Size = new Size(520, 790);
            eventList.TabIndex = 0;
            eventList.UseCompatibleStateImageBehavior = false;
            eventList.View = View.Details;
            eventList.VirtualMode = true;
            eventList.RetrieveVirtualItem += EventList_RetrieveVirtualItem;
            eventList.SelectedIndexChanged += EventList_SelectedIndexChanged;
            //
            // numberColumn
            //
            numberColumn.Text = "#";
            numberColumn.Width = 50;
            //
            // timeColumn
            //
            timeColumn.Text = "Time";
            timeColumn.Width = 85;
            //
            // tagColumn
            //
            tagColumn.Text = "Event";
            tagColumn.Width = 75;
            //
            // textColumn
            //
            textColumn.Text = "What happened";
            textColumn.Width = 600;
            //
            // statusStrip
            //
            statusStrip.Items.AddRange(new ToolStripItem[] { statusLabel });
            statusStrip.Location = new Point(0, 839);
            statusStrip.Name = "statusStrip";
            statusStrip.Size = new Size(1384, 22);
            statusStrip.TabIndex = 3;
            //
            // statusLabel
            //
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(1369, 17);
            statusLabel.Spring = true;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // playTimer
            //
            playTimer.Interval = 600;
            playTimer.Tick += PlayTimer_Tick;
            //
            // MainForm
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1384, 861);
            Controls.Add(splitContainer);
            Controls.Add(statusStrip);
            Controls.Add(toolStrip);
            Controls.Add(menuStrip);
            KeyPreview = true;
            MainMenuStrip = menuStrip;
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "DuelIt";
            Load += MainForm_Load;
            KeyDown += MainForm_KeyDown;
            menuStrip.ResumeLayout(false);
            menuStrip.PerformLayout();
            toolStrip.ResumeLayout(false);
            toolStrip.PerformLayout();
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            statusStrip.ResumeLayout(false);
            statusStrip.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private MenuStrip menuStrip;
        private ToolStripMenuItem fileMenu;
        private ToolStripMenuItem openMenuItem;
        private ToolStripMenuItem reloadMenuItem;
        private ToolStripMenuItem gameFolderMenuItem;
        private ToolStripSeparator fileSeparator;
        private ToolStripMenuItem exitMenuItem;
        private ToolStripMenuItem viewMenu;
        private ToolStripMenuItem uncensoredMenuItem;
        private ToolStrip toolStrip;
        private ToolStripLabel duelLabel;
        private ToolStripComboBox duelCombo;
        private ToolStripSeparator toolSeparator1;
        private ToolStripButton firstButton;
        private ToolStripButton previousButton;
        private ToolStripButton playButton;
        private ToolStripButton nextButton;
        private ToolStripButton lastButton;
        private ToolStripSeparator toolSeparator2;
        private ToolStripLabel speedLabel;
        private ToolStripComboBox speedCombo;
        private ToolStripButton keyEventsButton;
        private ToolStripLabel stepLabel;
        private SplitContainer splitContainer;
        private BoardView board;
        private ListView eventList;
        private ColumnHeader numberColumn;
        private ColumnHeader timeColumn;
        private ColumnHeader tagColumn;
        private ColumnHeader textColumn;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;
        private System.Windows.Forms.Timer playTimer;
    }
}
