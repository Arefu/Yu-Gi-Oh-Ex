namespace WolfX.WolfX.File_Type_UI
{
    partial class Card_Replacer__Simple_
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            YDC_BTN_AddCard = new Button();
            textBox1 = new TextBox();
            label1 = new Label();
            SuspendLayout();
            // 
            // YDC_BTN_AddCard
            // 
            YDC_BTN_AddCard.Location = new Point(171, 47);
            YDC_BTN_AddCard.Name = "YDC_BTN_AddCard";
            YDC_BTN_AddCard.Size = new Size(75, 23);
            YDC_BTN_AddCard.TabIndex = 0;
            YDC_BTN_AddCard.Text = "Add Card";
            YDC_BTN_AddCard.UseVisualStyleBackColor = true;
            // 
            // textBox1
            // 
            textBox1.Location = new Point(12, 47);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(153, 23);
            textBox1.TabIndex = 1;
            // 
            // label1
            // 
            label1.Location = new Point(12, 9);
            label1.Name = "label1";
            label1.Size = new Size(234, 35);
            label1.TabIndex = 2;
            label1.Text = "You're able to seperate cards with a comma (\",\")to specify multiple ";
            // 
            // Card_Replacer__Simple_
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(258, 79);
            Controls.Add(label1);
            Controls.Add(textBox1);
            Controls.Add(YDC_BTN_AddCard);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "Card_Replacer__Simple_";
            Text = "Card Replacer (Simple)";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button YDC_BTN_AddCard;
        private TextBox textBox1;
        private Label label1;
    }
}