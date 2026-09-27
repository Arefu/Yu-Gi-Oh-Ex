#nullable disable

namespace WolfX.Types
{
    partial class CardPickerDialog
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
            _status = new Label();
            buttons = new FlowLayoutPanel();
            bar = new FlowLayoutPanel();
            top = new TableLayoutPanel();
            colAdd = new DataGridViewCheckBoxColumn();
            colCard = new DataGridViewTextBoxColumn();
            colId = new DataGridViewTextBoxColumn();
            btnCancel = new Button();
            btnOk = new Button();
            btnTickAll = new Button();
            btnTickHighlighted = new Button();
            btnClear = new Button();
            btnPaste = new Button();
            lblCopies = new Label();
            _copies = new NumericUpDown();
            lblSearch = new Label();
            _search = new TextBox();
            buttons.SuspendLayout();
            bar.SuspendLayout();
            top.SuspendLayout();
            SuspendLayout();
            // 
            // _grid
            // 
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AutoGenerateColumns = false;
            _grid.RowHeadersVisible = false;
            _grid.RowHeadersWidth = 51;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.Columns.AddRange(new DataGridViewColumn[] { colAdd, colCard, colId });
            _grid.Name = "_grid";
            _grid.CellValueChanged += _grid_CellValueChanged;
            _grid.CurrentCellDirtyStateChanged += _grid_CurrentCellDirtyStateChanged;
            _grid.CellDoubleClick += _grid_CellDoubleClick;
            // 
            // _status
            // 
            _status.AutoSize = true;
            _status.Dock = DockStyle.Bottom;
            _status.Padding = new Padding(6, 4, 0, 4);
            _status.Name = "_status";
            // 
            // buttons
            // 
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnOk);
            buttons.Dock = DockStyle.Bottom;
            buttons.Height = 40;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Padding = new Padding(4);
            buttons.Name = "buttons";
            // 
            // bar
            // 
            bar.Controls.Add(btnTickAll);
            bar.Controls.Add(btnTickHighlighted);
            bar.Controls.Add(btnClear);
            bar.Controls.Add(btnPaste);
            bar.Controls.Add(lblCopies);
            bar.Controls.Add(_copies);
            bar.Dock = DockStyle.Top;
            bar.Height = 36;
            bar.Padding = new Padding(4);
            bar.Name = "bar";
            // 
            // top
            // 
            top.Controls.Add(lblSearch, 0, 0);
            top.Controls.Add(_search, 1, 0);
            top.ColumnCount = 2;
            top.Dock = DockStyle.Top;
            top.Height = 38;
            top.Padding = new Padding(4);
            top.RowCount = 1;
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            top.Name = "top";
            // 
            // colAdd
            // 
            colAdd.DataPropertyName = "Selected";
            colAdd.HeaderText = "Add";
            colAdd.Width = 50;
            colAdd.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            colAdd.Name = "colAdd";
            // 
            // colCard
            // 
            colCard.DataPropertyName = "Name";
            colCard.HeaderText = "Card";
            colCard.ReadOnly = true;
            colCard.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colCard.Name = "colCard";
            // 
            // colId
            // 
            colId.DataPropertyName = "Id";
            colId.HeaderText = "Konami id";
            colId.ReadOnly = true;
            colId.Width = 90;
            colId.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            colId.Name = "colId";
            // 
            // btnCancel
            // 
            btnCancel.Text = "Cancel";
            btnCancel.AutoSize = true;
            btnCancel.UseVisualStyleBackColor = true;
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Name = "btnCancel";
            // 
            // btnOk
            // 
            btnOk.Text = "Add";
            btnOk.AutoSize = true;
            btnOk.UseVisualStyleBackColor = true;
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Name = "btnOk";
            // 
            // btnTickAll
            // 
            btnTickAll.Text = "Tick all shown";
            btnTickAll.AutoSize = true;
            btnTickAll.UseVisualStyleBackColor = true;
            btnTickAll.Name = "btnTickAll";
            btnTickAll.Click += btnTickAll_Click;
            // 
            // btnTickHighlighted
            // 
            btnTickHighlighted.Text = "Tick highlighted";
            btnTickHighlighted.AutoSize = true;
            btnTickHighlighted.UseVisualStyleBackColor = true;
            btnTickHighlighted.Name = "btnTickHighlighted";
            btnTickHighlighted.Click += btnTickHighlighted_Click;
            // 
            // btnClear
            // 
            btnClear.Text = "Clear ticks";
            btnClear.AutoSize = true;
            btnClear.UseVisualStyleBackColor = true;
            btnClear.Name = "btnClear";
            btnClear.Click += btnClear_Click;
            // 
            // btnPaste
            // 
            btnPaste.Text = "Paste names...";
            btnPaste.AutoSize = true;
            btnPaste.UseVisualStyleBackColor = true;
            btnPaste.Name = "btnPaste";
            btnPaste.Click += btnPaste_Click;
            // 
            // lblCopies
            // 
            lblCopies.Text = "Copies of each:";
            lblCopies.AutoSize = true;
            lblCopies.Padding = new Padding(12, 6, 0, 0);
            lblCopies.Name = "lblCopies";
            // 
            // _copies
            // 
            _copies.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            _copies.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            _copies.Value = new decimal(new int[] { 1, 0, 0, 0 });
            _copies.Width = 60;
            _copies.Name = "_copies";
            // 
            // lblSearch
            // 
            lblSearch.Text = "Search (name or id):";
            lblSearch.AutoSize = true;
            lblSearch.Padding = new Padding(0, 6, 4, 0);
            lblSearch.Name = "lblSearch";
            // 
            // _search
            // 
            _search.Dock = DockStyle.Fill;
            _search.Name = "_search";
            _search.TextChanged += _search_TextChanged;
            // 
            // this
            // 
            Controls.Add(_grid);
            Controls.Add(_status);
            Controls.Add(buttons);
            Controls.Add(bar);
            Controls.Add(top);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(704, 601);
            Text = "Add cards";
            StartPosition = FormStartPosition.CenterParent;
            Name = "CardPickerDialog";
            FormClosing += CardPickerDialog_FormClosing;
            top.ResumeLayout(false);
            top.PerformLayout();
            bar.ResumeLayout(false);
            bar.PerformLayout();
            buttons.ResumeLayout(false);
            buttons.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private DataGridView _grid;
        private Label _status;
        private FlowLayoutPanel buttons;
        private FlowLayoutPanel bar;
        private TableLayoutPanel top;
        private DataGridViewCheckBoxColumn colAdd;
        private DataGridViewTextBoxColumn colCard;
        private DataGridViewTextBoxColumn colId;
        private Button btnCancel;
        private Button btnOk;
        private Button btnTickAll;
        private Button btnTickHighlighted;
        private Button btnClear;
        private Button btnPaste;
        private Label lblCopies;
        private NumericUpDown _copies;
        private Label lblSearch;
        private TextBox _search;
    }
}
