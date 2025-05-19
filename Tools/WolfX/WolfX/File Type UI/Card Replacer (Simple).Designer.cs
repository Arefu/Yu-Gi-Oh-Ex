namespace WolfX.WolfX.File_Type_UI
{
    partial class SimpleCardAdd
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
            YDCSimple_BTN_AddCard = new Button();
            TB_YDCSimple_CardIDs = new TextBox();
            label1 = new Label();
            SuspendLayout();
            // 
            // YDCSimple_BTN_AddCard
            // 
            YDCSimple_BTN_AddCard.Location = new Point(171, 47);
            YDCSimple_BTN_AddCard.Name = "YDCSimple_BTN_AddCard";
            YDCSimple_BTN_AddCard.Size = new Size(75, 23);
            YDCSimple_BTN_AddCard.TabIndex = 0;
            YDCSimple_BTN_AddCard.Text = "Add Card";
            YDCSimple_BTN_AddCard.UseVisualStyleBackColor = true;
            YDCSimple_BTN_AddCard.Click += YDCSimple_BTN_AddCard_Click;
            // 
            // TB_YDCSimple_CardIDs
            // 
            TB_YDCSimple_CardIDs.Location = new Point(12, 47);
            TB_YDCSimple_CardIDs.MaxLength = 65535;
            TB_YDCSimple_CardIDs.Multiline = true;
            TB_YDCSimple_CardIDs.Name = "TB_YDCSimple_CardIDs";
            TB_YDCSimple_CardIDs.ScrollBars = ScrollBars.Vertical;
            TB_YDCSimple_CardIDs.Size = new Size(153, 74);
            TB_YDCSimple_CardIDs.TabIndex = 1;
            // 
            // label1
            // 
            label1.Location = new Point(12, 9);
            label1.Name = "label1";
            label1.Size = new Size(234, 35);
            label1.TabIndex = 2;
            label1.Text = "You're able to seperate cards with a comma (\",\")to specify multiple ";
            // 
            // SimpleCardAdd
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(321, 133);
            Controls.Add(label1);
            Controls.Add(TB_YDCSimple_CardIDs);
            Controls.Add(YDCSimple_BTN_AddCard);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "SimpleCardAdd";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Card Replacer (Simple)";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button YDCSimple_BTN_AddCard;
        private TextBox TB_YDCSimple_CardIDs;
        private Label label1;
    }
}