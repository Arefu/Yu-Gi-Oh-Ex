#nullable disable

namespace WolfEx
{
    partial class MenusPanel
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
            tabButtons = new TabPage();
            tabGame = new TabPage();
            split = new SplitContainer();
            _list = new ListBox();
            leftButtons = new FlowLayoutPanel();
            btnAdd = new Button();
            btnRemove = new Button();
            btnUp = new Button();
            btnDown = new Button();
            right = new Panel();
            form = new TableLayoutPanel();
            lblKey = new Label();
            _key = new TextBox();
            lblMenu = new Label();
            _menu = new ComboBox();
            lblPage = new Label();
            _page = new ComboBox();
            lblLabel = new Label();
            _label = new TextBox();
            lblDescription = new Label();
            _description = new TextBox();
            lblLook = new Label();
            _look = new ComboBox();
            lblAction = new Label();
            _actionType = new ComboBox();
            lblArg = new Label();
            _actionArg = new ComboBox();
            _help = new Label();
            _game = new DataGridView();
            _gameInfo = new Label();
            colItem = new DataGridViewTextBoxColumn();
            colLabel = new DataGridViewTextBoxColumn();
            colDescription = new DataGridViewTextBoxColumn();
            colHidden = new DataGridViewCheckBoxColumn();
            _tabs.SuspendLayout();
            tabButtons.SuspendLayout();
            tabGame.SuspendLayout();
            leftButtons.SuspendLayout();
            right.SuspendLayout();
            form.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)split).BeginInit();
            split.Panel1.SuspendLayout();
            split.Panel2.SuspendLayout();
            split.SuspendLayout();
            SuspendLayout();
            // 
            // _tabs
            // 
            _tabs.Controls.Add(tabButtons);
            _tabs.Controls.Add(tabGame);
            _tabs.Dock = DockStyle.Fill;
            _tabs.SelectedIndex = 0;
            _tabs.Name = "_tabs";
            // 
            // tabButtons
            // 
            tabButtons.Controls.Add(split);
            tabButtons.Text = "Buttons";
            tabButtons.UseVisualStyleBackColor = true;
            tabButtons.Name = "tabButtons";
            // 
            // tabGame
            // 
            tabGame.Controls.Add(_game);
            tabGame.Controls.Add(_gameInfo);
            tabGame.Text = "Game buttons";
            tabGame.UseVisualStyleBackColor = true;
            tabGame.Name = "tabGame";
            // 
            // split
            // 
            split.Panel1.Controls.Add(_list);
            split.Panel1.Controls.Add(leftButtons);
            split.Panel2.Controls.Add(right);
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel1;
            split.SplitterDistance = 300;
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
            // leftButtons
            // 
            leftButtons.Controls.Add(btnAdd);
            leftButtons.Controls.Add(btnRemove);
            leftButtons.Controls.Add(btnUp);
            leftButtons.Controls.Add(btnDown);
            leftButtons.Dock = DockStyle.Bottom;
            leftButtons.Height = 34;
            leftButtons.Padding = new Padding(2);
            leftButtons.Name = "leftButtons";
            // 
            // btnAdd
            // 
            btnAdd.Text = "Add";
            btnAdd.AutoSize = true;
            btnAdd.UseVisualStyleBackColor = true;
            btnAdd.Name = "btnAdd";
            btnAdd.Click += btnAdd_Click;
            // 
            // btnRemove
            // 
            btnRemove.Text = "Remove";
            btnRemove.AutoSize = true;
            btnRemove.UseVisualStyleBackColor = true;
            btnRemove.Name = "btnRemove";
            btnRemove.Click += btnRemove_Click;
            // 
            // btnUp
            // 
            btnUp.Text = "Up";
            btnUp.AutoSize = true;
            btnUp.UseVisualStyleBackColor = true;
            btnUp.Name = "btnUp";
            btnUp.Click += btnUp_Click;
            // 
            // btnDown
            // 
            btnDown.Text = "Down";
            btnDown.AutoSize = true;
            btnDown.UseVisualStyleBackColor = true;
            btnDown.Name = "btnDown";
            btnDown.Click += btnDown_Click;
            // 
            // right
            // 
            right.Controls.Add(form);
            right.Dock = DockStyle.Fill;
            right.AutoScroll = true;
            right.Name = "right";
            // 
            // form
            // 
            form.Controls.Add(lblKey, 0, 0);
            form.Controls.Add(_key, 1, 0);
            form.Controls.Add(lblMenu, 0, 1);
            form.Controls.Add(_menu, 1, 1);
            form.Controls.Add(lblPage, 0, 2);
            form.Controls.Add(_page, 1, 2);
            form.Controls.Add(lblLabel, 0, 3);
            form.Controls.Add(_label, 1, 3);
            form.Controls.Add(lblDescription, 0, 4);
            form.Controls.Add(_description, 1, 4);
            form.Controls.Add(lblLook, 0, 5);
            form.Controls.Add(_look, 1, 5);
            form.Controls.Add(lblAction, 0, 6);
            form.Controls.Add(_actionType, 1, 6);
            form.Controls.Add(lblArg, 0, 7);
            form.Controls.Add(_actionArg, 1, 7);
            form.Controls.Add(_help, 1, 8);
            form.AutoSize = true;
            form.ColumnCount = 2;
            form.Dock = DockStyle.Top;
            form.Padding = new Padding(8);
            form.RowCount = 9;
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.Name = "form";
            // 
            // lblKey
            // 
            lblKey.Text = "Key (a name for you):";
            lblKey.AutoSize = true;
            lblKey.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblKey.Padding = new Padding(0, 6, 8, 0);
            lblKey.Name = "lblKey";
            // 
            // _key
            // 
            _key.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _key.Margin = new Padding(3);
            _key.Name = "_key";
            _key.TextChanged += Editor_Changed;
            // 
            // lblMenu
            // 
            lblMenu.Text = "Menu:";
            lblMenu.AutoSize = true;
            lblMenu.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblMenu.Padding = new Padding(0, 6, 8, 0);
            lblMenu.Name = "lblMenu";
            // 
            // _menu
            // 
            _menu.DropDownStyle = ComboBoxStyle.DropDownList;
            _menu.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _menu.FormattingEnabled = true;
            _menu.Margin = new Padding(3);
            _menu.Items.AddRange(new object[] { "main", "options" });
            _menu.Name = "_menu";
            _menu.SelectedIndexChanged += Editor_Changed;
            // 
            // lblPage
            // 
            lblPage.Text = "Page (main menu):";
            lblPage.AutoSize = true;
            lblPage.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblPage.Padding = new Padding(0, 6, 8, 0);
            lblPage.Name = "lblPage";
            // 
            // _page
            // 
            _page.DropDownStyle = ComboBoxStyle.DropDownList;
            _page.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _page.FormattingEnabled = true;
            _page.Margin = new Padding(3);
            _page.Items.AddRange(new object[] { "main", "singlePlayer", "multiplayer" });
            _page.Name = "_page";
            _page.SelectedIndexChanged += Editor_Changed;
            // 
            // lblLabel
            // 
            lblLabel.Text = "Label:";
            lblLabel.AutoSize = true;
            lblLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblLabel.Padding = new Padding(0, 6, 8, 0);
            lblLabel.Name = "lblLabel";
            // 
            // _label
            // 
            _label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _label.Margin = new Padding(3);
            _label.Name = "_label";
            _label.TextChanged += Editor_Changed;
            // 
            // lblDescription
            // 
            lblDescription.Text = "Description:";
            lblDescription.AutoSize = true;
            lblDescription.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblDescription.Padding = new Padding(0, 6, 8, 0);
            lblDescription.Name = "lblDescription";
            // 
            // _description
            // 
            _description.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _description.Margin = new Padding(3);
            _description.Name = "_description";
            _description.TextChanged += Editor_Changed;
            // 
            // lblLook
            // 
            lblLook.Text = "Look (borrowed picture):";
            lblLook.AutoSize = true;
            lblLook.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblLook.Padding = new Padding(0, 6, 8, 0);
            lblLook.Name = "lblLook";
            // 
            // _look
            // 
            _look.DropDownStyle = ComboBoxStyle.DropDownList;
            _look.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _look.FormattingEnabled = true;
            _look.Margin = new Padding(3);
            _look.Items.AddRange(new object[] {  });
            _look.Name = "_look";
            _look.SelectedIndexChanged += Editor_Changed;
            // 
            // lblAction
            // 
            lblAction.Text = "When pressed:";
            lblAction.AutoSize = true;
            lblAction.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblAction.Padding = new Padding(0, 6, 8, 0);
            lblAction.Name = "lblAction";
            // 
            // _actionType
            // 
            _actionType.DropDownStyle = ComboBoxStyle.DropDownList;
            _actionType.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _actionType.FormattingEnabled = true;
            _actionType.Margin = new Padding(3);
            _actionType.Items.AddRange(new object[] { "nothing", "goto a screen", "press a game button", "call a plugin action", "quit the game" });
            _actionType.Name = "_actionType";
            _actionType.SelectedIndexChanged += Editor_Changed;
            // 
            // lblArg
            // 
            lblArg.Text = "Which one:";
            lblArg.AutoSize = true;
            lblArg.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblArg.Padding = new Padding(0, 6, 8, 0);
            lblArg.Name = "lblArg";
            // 
            // _actionArg
            // 
            _actionArg.DropDownStyle = ComboBoxStyle.DropDown;
            _actionArg.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _actionArg.FormattingEnabled = true;
            _actionArg.Margin = new Padding(3);
            _actionArg.Items.AddRange(new object[] {  });
            _actionArg.Name = "_actionArg";
            _actionArg.SelectedIndexChanged += Editor_Changed;
            _actionArg.TextChanged += Editor_Changed;
            // 
            // _help
            // 
            _help.AutoSize = true;
            _help.MaximumSize = new Size(520, 0);
            _help.Name = "_help";
            // 
            // _game
            // 
            _game.Dock = DockStyle.Fill;
            _game.AllowUserToAddRows = false;
            _game.AllowUserToDeleteRows = false;
            _game.AutoGenerateColumns = false;
            _game.RowHeadersVisible = false;
            _game.RowHeadersWidth = 51;
            _game.Columns.AddRange(new DataGridViewColumn[] { colItem, colLabel, colDescription, colHidden });
            _game.Name = "_game";
            // 
            // _gameInfo
            // 
            _gameInfo.AutoSize = true;
            _gameInfo.Dock = DockStyle.Bottom;
            _gameInfo.Padding = new Padding(4, 6, 0, 6);
            _gameInfo.Name = "_gameInfo";
            // 
            // colItem
            // 
            colItem.DataPropertyName = "Item";
            colItem.HeaderText = "Game button";
            colItem.ReadOnly = true;
            colItem.Width = 170;
            colItem.Name = "colItem";
            // 
            // colLabel
            // 
            colLabel.DataPropertyName = "Label";
            colLabel.HeaderText = "New label (empty = leave)";
            colLabel.Width = 240;
            colLabel.Name = "colLabel";
            // 
            // colDescription
            // 
            colDescription.DataPropertyName = "Description";
            colDescription.HeaderText = "New description (empty = leave)";
            colDescription.Width = 320;
            colDescription.Name = "colDescription";
            // 
            // colHidden
            // 
            colHidden.DataPropertyName = "Hidden";
            colHidden.HeaderText = "Hide";
            colHidden.Width = 60;
            colHidden.Name = "colHidden";
            // 
            // this
            // 
            Controls.Add(_tabs);
            AutoScaleMode = AutoScaleMode.Font;
            Name = "MenusPanel";
            Size = new Size(1000, 640);
            split.Panel1.ResumeLayout(false);
            split.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)split).EndInit();
            split.ResumeLayout(false);
            form.ResumeLayout(false);
            form.PerformLayout();
            right.ResumeLayout(false);
            right.PerformLayout();
            leftButtons.ResumeLayout(false);
            leftButtons.PerformLayout();
            tabGame.ResumeLayout(false);
            tabGame.PerformLayout();
            tabButtons.ResumeLayout(false);
            tabButtons.PerformLayout();
            _tabs.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TabControl _tabs;
        private TabPage tabButtons;
        private TabPage tabGame;
        private SplitContainer split;
        private ListBox _list;
        private FlowLayoutPanel leftButtons;
        private Button btnAdd;
        private Button btnRemove;
        private Button btnUp;
        private Button btnDown;
        private Panel right;
        private TableLayoutPanel form;
        private Label lblKey;
        private TextBox _key;
        private Label lblMenu;
        private ComboBox _menu;
        private Label lblPage;
        private ComboBox _page;
        private Label lblLabel;
        private TextBox _label;
        private Label lblDescription;
        private TextBox _description;
        private Label lblLook;
        private ComboBox _look;
        private Label lblAction;
        private ComboBox _actionType;
        private Label lblArg;
        private ComboBox _actionArg;
        private Label _help;
        private DataGridView _game;
        private Label _gameInfo;
        private DataGridViewTextBoxColumn colItem;
        private DataGridViewTextBoxColumn colLabel;
        private DataGridViewTextBoxColumn colDescription;
        private DataGridViewCheckBoxColumn colHidden;
    }
}
