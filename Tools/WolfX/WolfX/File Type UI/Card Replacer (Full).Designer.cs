namespace WolfX.WolfX.File_Type_UI
{
    partial class Card_Changer
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
            YDCFull_LV_ListOfAllCards = new ListView();
            YDCFull_BTN_Save = new Button();
            YDCFull_BTN_Cancel = new Button();
            SuspendLayout();
            // 
            // YDCFull_LV_ListOfAllCards
            // 
            YDCFull_LV_ListOfAllCards.Location = new Point(12, 12);
            YDCFull_LV_ListOfAllCards.Name = "YDCFull_LV_ListOfAllCards";
            YDCFull_LV_ListOfAllCards.Size = new Size(680, 388);
            YDCFull_LV_ListOfAllCards.TabIndex = 0;
            YDCFull_LV_ListOfAllCards.UseCompatibleStateImageBehavior = false;
            // 
            // YDCFull_BTN_Save
            // 
            YDCFull_BTN_Save.Location = new Point(617, 406);
            YDCFull_BTN_Save.Name = "YDCFull_BTN_Save";
            YDCFull_BTN_Save.Size = new Size(75, 23);
            YDCFull_BTN_Save.TabIndex = 1;
            YDCFull_BTN_Save.Text = "Save";
            YDCFull_BTN_Save.UseVisualStyleBackColor = true;
            // 
            // YDCFull_BTN_Cancel
            // 
            YDCFull_BTN_Cancel.Location = new Point(536, 406);
            YDCFull_BTN_Cancel.Name = "YDCFull_BTN_Cancel";
            YDCFull_BTN_Cancel.Size = new Size(75, 23);
            YDCFull_BTN_Cancel.TabIndex = 2;
            YDCFull_BTN_Cancel.Text = "Cancel";
            YDCFull_BTN_Cancel.UseVisualStyleBackColor = true;
            // 
            // Card_Changer
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(704, 441);
            Controls.Add(YDCFull_BTN_Cancel);
            Controls.Add(YDCFull_BTN_Save);
            Controls.Add(YDCFull_LV_ListOfAllCards);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "Card_Changer";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Card Replacer (Full)";
            ResumeLayout(false);
        }

        #endregion

        private ListView YDCFull_LV_ListOfAllCards;
        private Button YDCFull_BTN_Save;
        private Button YDCFull_BTN_Cancel;
    }
}