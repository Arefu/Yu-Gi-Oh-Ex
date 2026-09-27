#nullable disable

namespace WolfX.Types
{
    partial class SaveEditorPage
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
            top = new TableLayoutPanel();
            lblFile = new Label();
            _pathBox = new TextBox();
            btnOpen = new Button();
            btnSave = new Button();
            btnSaveAs = new Button();
            tabGeneral = new TabPage();
            tabStats = new TabPage();
            tabCards = new TabPage();
            tabCharacters = new TabPage();
            tabDecks = new TabPage();
            generalFlow = new FlowLayoutPanel();
            lblWallet = new Label();
            _wallet = new NumericUpDown();
            _info = new Label();
            _stats = new DataGridView();
            _cards = new DataGridView();
            cardsBar = new FlowLayoutPanel();
            btnAddByName = new Button();
            lblOrId = new Label();
            _addId = new TextBox();
            lblAddCopies = new Label();
            _addCopies = new NumericUpDown();
            btnAddCard = new Button();
            btnRemoveCards = new Button();
            btnMarkSeen = new Button();
            _characters = new CheckedListBox();
            decksSplit = new SplitContainer();
            _decks = new ListBox();
            deckRight = new FlowLayoutPanel();
            lblDeckName = new Label();
            _deckName = new TextBox();
            lblDeckCharacter = new Label();
            _deckCharacter = new NumericUpDown();
            _deckMain = new CardListEditor();
            _deckExtra = new CardListEditor();
            _deckSide = new CardListEditor();
            btnApplyDeck = new Button();
            _tabs.SuspendLayout();
            top.SuspendLayout();
            tabGeneral.SuspendLayout();
            tabStats.SuspendLayout();
            tabCards.SuspendLayout();
            tabCharacters.SuspendLayout();
            tabDecks.SuspendLayout();
            generalFlow.SuspendLayout();
            cardsBar.SuspendLayout();
            deckRight.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)decksSplit).BeginInit();
            decksSplit.Panel1.SuspendLayout();
            decksSplit.Panel2.SuspendLayout();
            decksSplit.SuspendLayout();
            SuspendLayout();
            // 
            // _tabs
            // 
            _tabs.Controls.Add(tabGeneral);
            _tabs.Controls.Add(tabStats);
            _tabs.Controls.Add(tabCards);
            _tabs.Controls.Add(tabCharacters);
            _tabs.Controls.Add(tabDecks);
            _tabs.Dock = DockStyle.Fill;
            _tabs.Enabled = false;
            _tabs.SelectedIndex = 0;
            _tabs.Name = "_tabs";
            // 
            // top
            // 
            top.Controls.Add(lblFile, 0, 0);
            top.Controls.Add(_pathBox, 1, 0);
            top.Controls.Add(btnOpen, 2, 0);
            top.Controls.Add(btnSave, 3, 0);
            top.Controls.Add(btnSaveAs, 4, 0);
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
            // lblFile
            // 
            lblFile.Text = "Save file:";
            lblFile.AutoSize = true;
            lblFile.Padding = new Padding(0, 6, 4, 0);
            lblFile.Name = "lblFile";
            // 
            // _pathBox
            // 
            _pathBox.ReadOnly = true;
            _pathBox.Dock = DockStyle.Fill;
            _pathBox.Name = "_pathBox";
            // 
            // btnOpen
            // 
            btnOpen.Text = "Open...";
            btnOpen.AutoSize = true;
            btnOpen.UseVisualStyleBackColor = true;
            btnOpen.Name = "btnOpen";
            btnOpen.Click += btnOpen_Click;
            // 
            // btnSave
            // 
            btnSave.Text = "Save";
            btnSave.AutoSize = true;
            btnSave.UseVisualStyleBackColor = true;
            btnSave.Name = "btnSave";
            btnSave.Click += btnSave_Click;
            // 
            // btnSaveAs
            // 
            btnSaveAs.Text = "Save as...";
            btnSaveAs.AutoSize = true;
            btnSaveAs.UseVisualStyleBackColor = true;
            btnSaveAs.Name = "btnSaveAs";
            btnSaveAs.Click += btnSaveAs_Click;
            // 
            // tabGeneral
            // 
            tabGeneral.Controls.Add(generalFlow);
            tabGeneral.Text = "General";
            tabGeneral.UseVisualStyleBackColor = true;
            tabGeneral.Name = "tabGeneral";
            // 
            // tabStats
            // 
            tabStats.Controls.Add(_stats);
            tabStats.Text = "Stats";
            tabStats.UseVisualStyleBackColor = true;
            tabStats.Name = "tabStats";
            // 
            // tabCards
            // 
            tabCards.Controls.Add(_cards);
            tabCards.Controls.Add(cardsBar);
            tabCards.Text = "Cards";
            tabCards.UseVisualStyleBackColor = true;
            tabCards.Name = "tabCards";
            // 
            // tabCharacters
            // 
            tabCharacters.Controls.Add(_characters);
            tabCharacters.Text = "Characters";
            tabCharacters.UseVisualStyleBackColor = true;
            tabCharacters.Name = "tabCharacters";
            // 
            // tabDecks
            // 
            tabDecks.Controls.Add(decksSplit);
            tabDecks.Text = "Decks";
            tabDecks.UseVisualStyleBackColor = true;
            tabDecks.Name = "tabDecks";
            // 
            // generalFlow
            // 
            generalFlow.Controls.Add(lblWallet);
            generalFlow.Controls.Add(_wallet);
            generalFlow.Controls.Add(_info);
            generalFlow.Dock = DockStyle.Fill;
            generalFlow.FlowDirection = FlowDirection.TopDown;
            generalFlow.Padding = new Padding(10);
            generalFlow.Name = "generalFlow";
            // 
            // lblWallet
            // 
            lblWallet.Text = "Points (wallet):";
            lblWallet.AutoSize = true;
            lblWallet.Name = "lblWallet";
            // 
            // _wallet
            // 
            _wallet.Maximum = new decimal(new int[] { -1, -1, 0, 0 });
            _wallet.ThousandsSeparator = true;
            _wallet.Width = 160;
            _wallet.Name = "_wallet";
            _wallet.ValueChanged += _wallet_ValueChanged;
            // 
            // _info
            // 
            _info.Text = "";
            _info.AutoSize = true;
            _info.Padding = new Padding(0, 6, 0, 0);
            _info.Name = "_info";
            // 
            // _stats
            // 
            _stats.Dock = DockStyle.Fill;
            _stats.AllowUserToAddRows = false;
            _stats.AllowUserToDeleteRows = false;
            _stats.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _stats.RowHeadersVisible = false;
            _stats.RowHeadersWidth = 51;
            _stats.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _stats.Name = "_stats";
            _stats.CellValueChanged += _stats_CellValueChanged;
            // 
            // _cards
            // 
            _cards.Dock = DockStyle.Fill;
            _cards.AllowUserToAddRows = false;
            _cards.AllowUserToDeleteRows = false;
            _cards.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _cards.RowHeadersVisible = false;
            _cards.RowHeadersWidth = 51;
            _cards.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _cards.Name = "_cards";
            _cards.CellValueChanged += _cards_CellValueChanged;
            _cards.CurrentCellDirtyStateChanged += _cards_CurrentCellDirtyStateChanged;
            // 
            // cardsBar
            // 
            cardsBar.Controls.Add(btnAddByName);
            cardsBar.Controls.Add(lblOrId);
            cardsBar.Controls.Add(_addId);
            cardsBar.Controls.Add(lblAddCopies);
            cardsBar.Controls.Add(_addCopies);
            cardsBar.Controls.Add(btnAddCard);
            cardsBar.Controls.Add(btnRemoveCards);
            cardsBar.Controls.Add(btnMarkSeen);
            cardsBar.Dock = DockStyle.Top;
            cardsBar.Height = 34;
            cardsBar.Padding = new Padding(4);
            cardsBar.Name = "cardsBar";
            // 
            // btnAddByName
            // 
            btnAddByName.Text = "Add cards by name...";
            btnAddByName.AutoSize = true;
            btnAddByName.UseVisualStyleBackColor = true;
            btnAddByName.Name = "btnAddByName";
            btnAddByName.Click += btnAddByName_Click;
            // 
            // lblOrId
            // 
            lblOrId.Text = "or card id";
            lblOrId.AutoSize = true;
            lblOrId.Padding = new Padding(8, 6, 0, 0);
            lblOrId.Name = "lblOrId";
            // 
            // _addId
            // 
            _addId.Width = 70;
            _addId.Name = "_addId";
            // 
            // lblAddCopies
            // 
            lblAddCopies.Text = "copies";
            lblAddCopies.AutoSize = true;
            lblAddCopies.Padding = new Padding(0, 6, 0, 0);
            lblAddCopies.Name = "lblAddCopies";
            // 
            // _addCopies
            // 
            _addCopies.Maximum = new decimal(new int[] { 3, 0, 0, 0 });
            _addCopies.Value = new decimal(new int[] { 3, 0, 0, 0 });
            _addCopies.Width = 50;
            _addCopies.Name = "_addCopies";
            // 
            // btnAddCard
            // 
            btnAddCard.Text = "Add / set";
            btnAddCard.AutoSize = true;
            btnAddCard.UseVisualStyleBackColor = true;
            btnAddCard.Name = "btnAddCard";
            btnAddCard.Click += btnAddCard_Click;
            // 
            // btnRemoveCards
            // 
            btnRemoveCards.Text = "Remove selected";
            btnRemoveCards.AutoSize = true;
            btnRemoveCards.UseVisualStyleBackColor = true;
            btnRemoveCards.Name = "btnRemoveCards";
            btnRemoveCards.Click += btnRemoveCards_Click;
            // 
            // btnMarkSeen
            // 
            btnMarkSeen.Text = "Mark all as seen";
            btnMarkSeen.AutoSize = true;
            btnMarkSeen.UseVisualStyleBackColor = true;
            btnMarkSeen.Name = "btnMarkSeen";
            btnMarkSeen.Click += btnMarkSeen_Click;
            // 
            // _characters
            // 
            _characters.Dock = DockStyle.Fill;
            _characters.CheckOnClick = true;
            _characters.ColumnWidth = 70;
            _characters.FormattingEnabled = true;
            _characters.MultiColumn = true;
            _characters.Name = "_characters";
            _characters.ItemCheck += _characters_ItemCheck;
            // 
            // decksSplit
            // 
            decksSplit.Panel1.Controls.Add(_decks);
            decksSplit.Panel2.Controls.Add(deckRight);
            decksSplit.Dock = DockStyle.Fill;
            decksSplit.FixedPanel = FixedPanel.Panel1;
            decksSplit.SplitterDistance = 260;
            decksSplit.Name = "decksSplit";
            // 
            // _decks
            // 
            _decks.Dock = DockStyle.Fill;
            _decks.FormattingEnabled = true;
            _decks.IntegralHeight = false;
            _decks.Name = "_decks";
            _decks.SelectedIndexChanged += _decks_SelectedIndexChanged;
            // 
            // deckRight
            // 
            deckRight.Controls.Add(lblDeckName);
            deckRight.Controls.Add(_deckName);
            deckRight.Controls.Add(lblDeckCharacter);
            deckRight.Controls.Add(_deckCharacter);
            deckRight.Controls.Add(_deckMain);
            deckRight.Controls.Add(_deckExtra);
            deckRight.Controls.Add(_deckSide);
            deckRight.Controls.Add(btnApplyDeck);
            deckRight.Dock = DockStyle.Fill;
            deckRight.AutoScroll = true;
            deckRight.FlowDirection = FlowDirection.TopDown;
            deckRight.Padding = new Padding(8);
            deckRight.WrapContents = false;
            deckRight.Name = "deckRight";
            // 
            // lblDeckName
            // 
            lblDeckName.Text = "Name:";
            lblDeckName.AutoSize = true;
            lblDeckName.Name = "lblDeckName";
            // 
            // _deckName
            // 
            _deckName.Width = 260;
            _deckName.Name = "_deckName";
            // 
            // lblDeckCharacter
            // 
            lblDeckCharacter.Text = "Character (0-239):";
            lblDeckCharacter.AutoSize = true;
            lblDeckCharacter.Name = "lblDeckCharacter";
            // 
            // _deckCharacter
            // 
            _deckCharacter.Maximum = new decimal(new int[] { 239, 0, 0, 0 });
            _deckCharacter.Width = 70;
            _deckCharacter.Name = "_deckCharacter";
            // 
            // _deckMain
            // 
            _deckMain.Title = "Main deck (up to 60):";
            _deckMain.Capacity = 60;
            _deckMain.Size = new Size(560, 200);
            _deckMain.Name = "_deckMain";
            // 
            // _deckExtra
            // 
            _deckExtra.Title = "Extra deck (up to 15):";
            _deckExtra.Capacity = 15;
            _deckExtra.Size = new Size(560, 170);
            _deckExtra.Name = "_deckExtra";
            // 
            // _deckSide
            // 
            _deckSide.Title = "Side deck (up to 15):";
            _deckSide.Capacity = 15;
            _deckSide.Size = new Size(560, 170);
            _deckSide.Name = "_deckSide";
            // 
            // btnApplyDeck
            // 
            btnApplyDeck.Text = "Apply to deck";
            btnApplyDeck.AutoSize = true;
            btnApplyDeck.UseVisualStyleBackColor = true;
            btnApplyDeck.Name = "btnApplyDeck";
            btnApplyDeck.Click += btnApplyDeck_Click;
            // 
            // this
            // 
            Controls.Add(_tabs);
            Controls.Add(top);
            AutoScaleMode = AutoScaleMode.Font;
            Name = "SaveEditorPage";
            Size = new Size(1000, 640);
            decksSplit.Panel1.ResumeLayout(false);
            decksSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)decksSplit).EndInit();
            decksSplit.ResumeLayout(false);
            deckRight.ResumeLayout(false);
            deckRight.PerformLayout();
            cardsBar.ResumeLayout(false);
            cardsBar.PerformLayout();
            generalFlow.ResumeLayout(false);
            generalFlow.PerformLayout();
            tabDecks.ResumeLayout(false);
            tabDecks.PerformLayout();
            tabCharacters.ResumeLayout(false);
            tabCharacters.PerformLayout();
            tabCards.ResumeLayout(false);
            tabCards.PerformLayout();
            tabStats.ResumeLayout(false);
            tabStats.PerformLayout();
            tabGeneral.ResumeLayout(false);
            tabGeneral.PerformLayout();
            top.ResumeLayout(false);
            top.PerformLayout();
            _tabs.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TabControl _tabs;
        private TableLayoutPanel top;
        private Label lblFile;
        private TextBox _pathBox;
        private Button btnOpen;
        private Button btnSave;
        private Button btnSaveAs;
        private TabPage tabGeneral;
        private TabPage tabStats;
        private TabPage tabCards;
        private TabPage tabCharacters;
        private TabPage tabDecks;
        private FlowLayoutPanel generalFlow;
        private Label lblWallet;
        private NumericUpDown _wallet;
        private Label _info;
        private DataGridView _stats;
        private DataGridView _cards;
        private FlowLayoutPanel cardsBar;
        private Button btnAddByName;
        private Label lblOrId;
        private TextBox _addId;
        private Label lblAddCopies;
        private NumericUpDown _addCopies;
        private Button btnAddCard;
        private Button btnRemoveCards;
        private Button btnMarkSeen;
        private CheckedListBox _characters;
        private SplitContainer decksSplit;
        private ListBox _decks;
        private FlowLayoutPanel deckRight;
        private Label lblDeckName;
        private TextBox _deckName;
        private Label lblDeckCharacter;
        private NumericUpDown _deckCharacter;
        private CardListEditor _deckMain;
        private CardListEditor _deckExtra;
        private CardListEditor _deckSide;
        private Button btnApplyDeck;
    }
}
