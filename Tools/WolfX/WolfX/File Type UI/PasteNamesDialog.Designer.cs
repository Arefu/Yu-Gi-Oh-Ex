#nullable disable

namespace WolfX.Types
{
    partial class PasteNamesDialog
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
            _text = new TextBox();
            hint = new Label();
            btnOk = new Button();
            SuspendLayout();
            // 
            // _text
            // 
            _text.Multiline = true;
            _text.Dock = DockStyle.Fill;
            _text.ScrollBars = ScrollBars.Vertical;
            _text.AcceptsReturn = true;
            _text.Name = "_text";
            // 
            // hint
            // 
            hint.Text = "One card per line: a name or a Konami id. \"3x Dark Magician\" or \"Dark Magician x3\" sets the copies.";
            hint.Dock = DockStyle.Top;
            hint.Height = 34;
            hint.Padding = new Padding(4);
            hint.Name = "hint";
            // 
            // btnOk
            // 
            btnOk.Text = "Tick these";
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Dock = DockStyle.Bottom;
            btnOk.Height = 32;
            btnOk.UseVisualStyleBackColor = true;
            btnOk.Name = "btnOk";
            // 
            // this
            // 
            Controls.Add(_text);
            Controls.Add(hint);
            Controls.Add(btnOk);
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(444, 381);
            Text = "Paste card names";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Name = "PasteNamesDialog";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox _text;
        private Label hint;
        private Button btnOk;
    }
}
