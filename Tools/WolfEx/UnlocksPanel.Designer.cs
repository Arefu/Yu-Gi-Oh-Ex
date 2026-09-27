#nullable disable

namespace WolfEx
{
    partial class UnlocksPanel
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
            _info = new Label();
            buttons = new FlowLayoutPanel();
            colId = new DataGridViewTextBoxColumn();
            colName = new DataGridViewTextBoxColumn();
            colCopies = new DataGridViewTextBoxColumn();
            btnAddByName = new Button();
            btnAddById = new Button();
            btnRemove = new Button();
            btnAddFromGame = new Button();
            btnImport = new Button();
            _replace = new CheckBox();
            buttons.SuspendLayout();
            SuspendLayout();
            // 
            // _grid
            // 
            _grid.Dock = DockStyle.Fill;
            _grid.AutoGenerateColumns = false;
            _grid.AllowUserToAddRows = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.RowHeadersWidth = 51;
            _grid.Columns.AddRange(new DataGridViewColumn[] { colId, colName, colCopies });
            _grid.Name = "_grid";
            // 
            // _info
            // 
            _info.AutoSize = true;
            _info.Dock = DockStyle.Bottom;
            _info.Padding = new Padding(4, 6, 0, 0);
            _info.Name = "_info";
            // 
            // buttons
            // 
            buttons.Controls.Add(btnAddByName);
            buttons.Controls.Add(btnAddById);
            buttons.Controls.Add(btnRemove);
            buttons.Controls.Add(btnAddFromGame);
            buttons.Controls.Add(btnImport);
            buttons.Controls.Add(_replace);
            buttons.Dock = DockStyle.Top;
            buttons.Height = 38;
            buttons.Padding = new Padding(4);
            buttons.Name = "buttons";
            // 
            // colId
            // 
            colId.DataPropertyName = "Id";
            colId.HeaderText = "Card id";
            colId.Width = 90;
            colId.Name = "colId";
            // 
            // colName
            // 
            colName.DataPropertyName = "Name";
            colName.HeaderText = "Card";
            colName.Width = 320;
            colName.ReadOnly = true;
            colName.Name = "colName";
            // 
            // colCopies
            // 
            colCopies.DataPropertyName = "Copies";
            colCopies.HeaderText = "Owned at least (0 - 3)";
            colCopies.Width = 170;
            colCopies.Name = "colCopies";
            // 
            // btnAddByName
            // 
            btnAddByName.Text = "Add by name...";
            btnAddByName.AutoSize = true;
            btnAddByName.UseVisualStyleBackColor = true;
            btnAddByName.Name = "btnAddByName";
            btnAddByName.Click += btnAddByName_Click;
            // 
            // btnAddById
            // 
            btnAddById.Text = "Add by id";
            btnAddById.AutoSize = true;
            btnAddById.UseVisualStyleBackColor = true;
            btnAddById.Name = "btnAddById";
            btnAddById.Click += btnAddById_Click;
            // 
            // btnRemove
            // 
            btnRemove.Text = "Remove selected";
            btnRemove.AutoSize = true;
            btnRemove.UseVisualStyleBackColor = true;
            btnRemove.Name = "btnRemove";
            btnRemove.Click += btnRemove_Click;
            // 
            // btnAddFromGame
            // 
            btnAddFromGame.Text = "Add from game data";
            btnAddFromGame.AutoSize = true;
            btnAddFromGame.UseVisualStyleBackColor = true;
            btnAddFromGame.Name = "btnAddFromGame";
            btnAddFromGame.Click += btnAddFromGame_Click;
            // 
            // btnImport
            // 
            btnImport.Text = "Import JSON...";
            btnImport.AutoSize = true;
            btnImport.UseVisualStyleBackColor = true;
            btnImport.Name = "btnImport";
            btnImport.Click += btnImport_Click;
            // 
            // _replace
            // 
            _replace.Text = "Replace the game's default unlocks (a new save starts with only these cards)";
            _replace.AutoSize = true;
            _replace.Checked = true;
            _replace.CheckState = CheckState.Checked;
            _replace.UseVisualStyleBackColor = true;
            _replace.Name = "_replace";
            // 
            // this
            // 
            Controls.Add(_grid);
            Controls.Add(_info);
            Controls.Add(buttons);
            AutoScaleMode = AutoScaleMode.Font;
            Name = "UnlocksPanel";
            Size = new Size(1000, 640);
            buttons.ResumeLayout(false);
            buttons.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private DataGridView _grid;
        private Label _info;
        private FlowLayoutPanel buttons;
        private DataGridViewTextBoxColumn colId;
        private DataGridViewTextBoxColumn colName;
        private DataGridViewTextBoxColumn colCopies;
        private Button btnAddByName;
        private Button btnAddById;
        private Button btnRemove;
        private Button btnAddFromGame;
        private Button btnImport;
        private CheckBox _replace;
    }
}
