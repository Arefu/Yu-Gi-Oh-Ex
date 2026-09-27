#nullable disable

namespace WolfX.Types
{
    partial class CardListEditor
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
            _list = new ListBox();
            bar = new FlowLayoutPanel();
            _count = new Label();
            _title = new Label();
            btnAdd = new Button();
            btnRemove = new Button();
            btnSort = new Button();
            btnClear = new Button();
            bar.SuspendLayout();
            SuspendLayout();
            // 
            // _list
            // 
            _list.Dock = DockStyle.Fill;
            _list.SelectionMode = SelectionMode.MultiExtended;
            _list.IntegralHeight = false;
            _list.FormattingEnabled = true;
            _list.Name = "_list";
            // 
            // bar
            // 
            bar.Controls.Add(btnAdd);
            bar.Controls.Add(btnRemove);
            bar.Controls.Add(btnSort);
            bar.Controls.Add(btnClear);
            bar.Dock = DockStyle.Right;
            bar.Width = 92;
            bar.FlowDirection = FlowDirection.TopDown;
            bar.WrapContents = false;
            bar.Name = "bar";
            // 
            // _count
            // 
            _count.AutoSize = true;
            _count.Dock = DockStyle.Bottom;
            _count.Name = "_count";
            // 
            // _title
            // 
            _title.AutoSize = true;
            _title.Dock = DockStyle.Top;
            _title.Padding = new Padding(0, 0, 0, 2);
            _title.Name = "_title";
            // 
            // btnAdd
            // 
            btnAdd.Text = "Add...";
            btnAdd.AutoSize = true;
            btnAdd.UseVisualStyleBackColor = true;
            btnAdd.AutoSize = false;
            btnAdd.Width = 84;
            btnAdd.Name = "btnAdd";
            btnAdd.Click += btnAdd_Click;
            // 
            // btnRemove
            // 
            btnRemove.Text = "Remove";
            btnRemove.AutoSize = true;
            btnRemove.UseVisualStyleBackColor = true;
            btnRemove.AutoSize = false;
            btnRemove.Width = 84;
            btnRemove.Name = "btnRemove";
            btnRemove.Click += btnRemove_Click;
            // 
            // btnSort
            // 
            btnSort.Text = "Sort A-Z";
            btnSort.AutoSize = true;
            btnSort.UseVisualStyleBackColor = true;
            btnSort.AutoSize = false;
            btnSort.Width = 84;
            btnSort.Name = "btnSort";
            btnSort.Click += btnSort_Click;
            // 
            // btnClear
            // 
            btnClear.Text = "Clear";
            btnClear.AutoSize = true;
            btnClear.UseVisualStyleBackColor = true;
            btnClear.AutoSize = false;
            btnClear.Width = 84;
            btnClear.Name = "btnClear";
            btnClear.Click += btnClear_Click;
            // 
            // this
            // 
            Controls.Add(_list);
            Controls.Add(bar);
            Controls.Add(_count);
            Controls.Add(_title);
            AutoScaleMode = AutoScaleMode.Font;
            MinimumSize = new Size(300, 120);
            Name = "CardListEditor";
            Size = new Size(400, 170);
            bar.ResumeLayout(false);
            bar.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ListBox _list;
        private FlowLayoutPanel bar;
        private Label _count;
        private Label _title;
        private Button btnAdd;
        private Button btnRemove;
        private Button btnSort;
        private Button btnClear;
    }
}
