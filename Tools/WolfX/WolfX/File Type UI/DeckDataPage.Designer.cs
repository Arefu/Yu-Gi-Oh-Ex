#nullable disable

namespace WolfX.Types
{
    partial class DeckDataPage
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
            _grid = new DataGridView();
            _details = new Label();
            _detailsPanel = new Panel();
            extraBar = new FlowLayoutPanel();
            top = new TableLayoutPanel();
            lblFile = new Label();
            _pathBox = new TextBox();
            btnOpen = new Button();
            btnSave = new Button();
            lblZib = new Label();
            _zibBox = new TextBox();
            btnBrowseZib = new Button();
            lblKind = new Label();
            _note = new Label();
            _side = new CardListEditor();
            _extra = new CardListEditor();
            _main = new CardListEditor();
            _detailsPanel.SuspendLayout();
            extraBar.SuspendLayout();
            top.SuspendLayout();
            SuspendLayout();
            // 
            // _grid
            // 
            _grid.Dock = DockStyle.Fill;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            _grid.RowHeadersVisible = false;
            _grid.RowHeadersWidth = 51;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.Name = "_grid";
            _grid.SelectionChanged += _grid_SelectionChanged;
            // 
            // _details
            // 
            _details.AutoSize = true;
            _details.Dock = DockStyle.Bottom;
            _details.Padding = new Padding(4);
            _details.Name = "_details";
            // 
            // _detailsPanel
            // 
            _detailsPanel.Controls.Add(_note);
            _detailsPanel.Controls.Add(_side);
            _detailsPanel.Controls.Add(_extra);
            _detailsPanel.Controls.Add(_main);
            _detailsPanel.Dock = DockStyle.Bottom;
            _detailsPanel.Height = 250;
            _detailsPanel.Name = "_detailsPanel";
            // 
            // extraBar
            // 
            extraBar.Controls.Add(lblZib);
            extraBar.Controls.Add(_zibBox);
            extraBar.Controls.Add(btnBrowseZib);
            extraBar.Controls.Add(lblKind);
            extraBar.Dock = DockStyle.Top;
            extraBar.Height = 34;
            extraBar.Padding = new Padding(4);
            extraBar.Name = "extraBar";
            // 
            // top
            // 
            top.Controls.Add(lblFile, 0, 0);
            top.Controls.Add(_pathBox, 1, 0);
            top.Controls.Add(btnOpen, 2, 0);
            top.Controls.Add(btnSave, 3, 0);
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
            // lblFile
            // 
            lblFile.Text = "File:";
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
            // lblZib
            // 
            lblZib.Text = "decks.zib:";
            lblZib.AutoSize = true;
            lblZib.Padding = new Padding(0, 6, 0, 0);
            lblZib.Name = "lblZib";
            // 
            // _zibBox
            // 
            _zibBox.ReadOnly = true;
            _zibBox.Width = 420;
            _zibBox.Name = "_zibBox";
            // 
            // btnBrowseZib
            // 
            btnBrowseZib.Text = "Browse...";
            btnBrowseZib.AutoSize = true;
            btnBrowseZib.UseVisualStyleBackColor = true;
            btnBrowseZib.Name = "btnBrowseZib";
            btnBrowseZib.Click += btnBrowseZib_Click;
            // 
            // lblKind
            // 
            lblKind.Text = "Sku -1 = base game";
            lblKind.AutoSize = true;
            lblKind.Padding = new Padding(12, 6, 0, 0);
            lblKind.Name = "lblKind";
            // 
            // _note
            // 
            _note.AutoSize = true;
            _note.Dock = DockStyle.Bottom;
            _note.Padding = new Padding(4);
            _note.Name = "_note";
            // 
            // _side
            // 
            _side.Title = "Side deck:";
            _side.Dock = DockStyle.Left;
            _side.Size = new Size(300, 176);
            _side.Capacity = 15;
            _side.Name = "_side";
            _side.Changed += Contents_Changed;
            // 
            // _extra
            // 
            _extra.Title = "Extra deck:";
            _extra.Dock = DockStyle.Left;
            _extra.Size = new Size(300, 176);
            _extra.Capacity = 15;
            _extra.Name = "_extra";
            _extra.Changed += Contents_Changed;
            // 
            // _main
            // 
            _main.Title = "Main deck:";
            _main.Dock = DockStyle.Left;
            _main.Size = new Size(380, 176);
            _main.Capacity = 60;
            _main.Name = "_main";
            _main.Changed += Contents_Changed;
            // 
            // this
            // 
            Controls.Add(_grid);
            Controls.Add(_details);
            Controls.Add(_detailsPanel);
            Controls.Add(extraBar);
            Controls.Add(top);
            AutoScaleMode = AutoScaleMode.Font;
            Name = "DeckDataPage";
            Size = new Size(1000, 640);
            top.ResumeLayout(false);
            top.PerformLayout();
            extraBar.ResumeLayout(false);
            extraBar.PerformLayout();
            _detailsPanel.ResumeLayout(false);
            _detailsPanel.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private DataGridView _grid;
        private Label _details;
        private Panel _detailsPanel;
        private FlowLayoutPanel extraBar;
        private TableLayoutPanel top;
        private Label lblFile;
        private TextBox _pathBox;
        private Button btnOpen;
        private Button btnSave;
        private Label lblZib;
        private TextBox _zibBox;
        private Button btnBrowseZib;
        private Label lblKind;
        private Label _note;
        private CardListEditor _side;
        private CardListEditor _extra;
        private CardListEditor _main;
    }
}
