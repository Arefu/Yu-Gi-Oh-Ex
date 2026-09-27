#nullable disable

namespace WolfEx
{
    partial class MainForm
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
            _tabs = new TabControl();
            _status = new Label();
            top = new TableLayoutPanel();
            tabCards = new TabPage();
            tabUnlocks = new TabPage();
            tabPacks = new TabPage();
            tabMenus = new TabPage();
            cardsPanel = new CardsPanel();
            unlocksPanel = new UnlocksPanel();
            packsPanel = new PacksPanel();
            menusPanel = new MenusPanel();
            lblGameFolder = new Label();
            _gameFolder = new TextBox();
            btnBrowse = new Button();
            btnReload = new Button();
            btnSaveAll = new Button();
            _tabs.SuspendLayout();
            top.SuspendLayout();
            tabCards.SuspendLayout();
            tabUnlocks.SuspendLayout();
            tabPacks.SuspendLayout();
            tabMenus.SuspendLayout();
            SuspendLayout();
            // 
            // _tabs
            // 
            _tabs.Controls.Add(tabCards);
            _tabs.Controls.Add(tabUnlocks);
            _tabs.Controls.Add(tabPacks);
            _tabs.Controls.Add(tabMenus);
            _tabs.Dock = DockStyle.Fill;
            _tabs.Name = "_tabs";
            _tabs.SelectedIndex = 0;
            // 
            // _status
            // 
            _status.AutoSize = true;
            _status.Dock = DockStyle.Bottom;
            _status.Padding = new Padding(4, 6, 0, 0);
            _status.Name = "_status";
            // 
            // top
            // 
            top.Controls.Add(lblGameFolder, 0, 0);
            top.Controls.Add(_gameFolder, 1, 0);
            top.Controls.Add(btnBrowse, 2, 0);
            top.Controls.Add(btnReload, 3, 0);
            top.Controls.Add(btnSaveAll, 4, 0);
            top.ColumnCount = 5;
            top.Dock = DockStyle.Top;
            top.Height = 38;
            top.Padding = new Padding(4);
            top.RowCount = 1;
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            top.Name = "top";
            // 
            // tabCards
            // 
            tabCards.Controls.Add(cardsPanel);
            tabCards.Text = "New cards";
            tabCards.UseVisualStyleBackColor = true;
            tabCards.Name = "tabCards";
            // 
            // tabUnlocks
            // 
            tabUnlocks.Controls.Add(unlocksPanel);
            tabUnlocks.Text = "Unlocks";
            tabUnlocks.UseVisualStyleBackColor = true;
            tabUnlocks.Name = "tabUnlocks";
            // 
            // tabPacks
            // 
            tabPacks.Controls.Add(packsPanel);
            tabPacks.Text = "Packs";
            tabPacks.UseVisualStyleBackColor = true;
            tabPacks.Name = "tabPacks";
            // 
            // tabMenus
            // 
            tabMenus.Controls.Add(menusPanel);
            tabMenus.Text = "Menus";
            tabMenus.UseVisualStyleBackColor = true;
            tabMenus.Name = "tabMenus";
            // 
            // cardsPanel
            // 
            cardsPanel.Dock = DockStyle.Fill;
            cardsPanel.Name = "cardsPanel";
            // 
            // unlocksPanel
            // 
            unlocksPanel.Dock = DockStyle.Fill;
            unlocksPanel.Name = "unlocksPanel";
            // 
            // packsPanel
            // 
            packsPanel.Dock = DockStyle.Fill;
            packsPanel.Name = "packsPanel";
            // 
            // menusPanel
            // 
            menusPanel.Dock = DockStyle.Fill;
            menusPanel.Name = "menusPanel";
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
            // btnReload
            // 
            btnReload.Text = "Reload";
            btnReload.AutoSize = true;
            btnReload.UseVisualStyleBackColor = true;
            btnReload.Name = "btnReload";
            btnReload.Click += btnReload_Click;
            // 
            // btnSaveAll
            // 
            btnSaveAll.Text = "Save all";
            btnSaveAll.AutoSize = true;
            btnSaveAll.UseVisualStyleBackColor = true;
            btnSaveAll.Name = "btnSaveAll";
            btnSaveAll.Click += btnSaveAll_Click;
            // 
            // this
            // 
            Controls.Add(_tabs);
            Controls.Add(_status);
            Controls.Add(top);
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1084, 721);
            Text = "WolfEx - new content for Legacy of the Duelist";
            Name = "MainForm";
            tabMenus.ResumeLayout(false);
            tabMenus.PerformLayout();
            tabPacks.ResumeLayout(false);
            tabPacks.PerformLayout();
            tabUnlocks.ResumeLayout(false);
            tabUnlocks.PerformLayout();
            tabCards.ResumeLayout(false);
            tabCards.PerformLayout();
            top.ResumeLayout(false);
            top.PerformLayout();
            _tabs.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TabControl _tabs;
        private Label _status;
        private TableLayoutPanel top;
        private TabPage tabCards;
        private TabPage tabUnlocks;
        private TabPage tabPacks;
        private TabPage tabMenus;
        private CardsPanel cardsPanel;
        private UnlocksPanel unlocksPanel;
        private PacksPanel packsPanel;
        private MenusPanel menusPanel;
        private Label lblGameFolder;
        private TextBox _gameFolder;
        private Button btnBrowse;
        private Button btnReload;
        private Button btnSaveAll;
    }
}
