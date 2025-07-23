namespace WolfX.WolfX.File_Type_UI
{
    partial class CardAdder
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
            YDCFull_BTN_Save = new Button();
            YDCFull_BTN_Cancel = new Button();
            dataGridView1 = new DataGridView();
            CardAdder_TB_NameOfCard = new DataGridViewTextBoxColumn();
            CardAdder_TB_IDOfCard = new DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)dataGridView1).BeginInit();
            SuspendLayout();
            // 
            // YDCFull_BTN_Save
            // 
            YDCFull_BTN_Save.Location = new Point(1177, 646);
            YDCFull_BTN_Save.Name = "YDCFull_BTN_Save";
            YDCFull_BTN_Save.Size = new Size(75, 23);
            YDCFull_BTN_Save.TabIndex = 1;
            YDCFull_BTN_Save.Text = "Save";
            YDCFull_BTN_Save.UseVisualStyleBackColor = true;
            // 
            // YDCFull_BTN_Cancel
            // 
            YDCFull_BTN_Cancel.Location = new Point(1096, 646);
            YDCFull_BTN_Cancel.Name = "YDCFull_BTN_Cancel";
            YDCFull_BTN_Cancel.Size = new Size(75, 23);
            YDCFull_BTN_Cancel.TabIndex = 2;
            YDCFull_BTN_Cancel.Text = "Cancel";
            YDCFull_BTN_Cancel.UseVisualStyleBackColor = true;
            // 
            // dataGridView1
            // 
            dataGridView1.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dataGridView1.Columns.AddRange(new DataGridViewColumn[] { CardAdder_TB_NameOfCard, CardAdder_TB_IDOfCard });
            dataGridView1.Location = new Point(12, 12);
            dataGridView1.Name = "dataGridView1";
            dataGridView1.Size = new Size(1240, 628);
            dataGridView1.TabIndex = 3;
            // 
            // CardAdder_TB_NameOfCard
            // 
            CardAdder_TB_NameOfCard.HeaderText = "Name";
            CardAdder_TB_NameOfCard.Name = "CardAdder_TB_NameOfCard";
            // 
            // CardAdder_TB_IDOfCard
            // 
            CardAdder_TB_IDOfCard.HeaderText = "ID";
            CardAdder_TB_IDOfCard.Name = "CardAdder_TB_IDOfCard";
            // 
            // CardAdder
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1264, 681);
            Controls.Add(dataGridView1);
            Controls.Add(YDCFull_BTN_Cancel);
            Controls.Add(YDCFull_BTN_Save);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "CardAdder";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Card Replacer (Full)";
            ((System.ComponentModel.ISupportInitialize)dataGridView1).EndInit();
            ResumeLayout(false);
        }

        #endregion
        private Button YDCFull_BTN_Save;
        private Button YDCFull_BTN_Cancel;
        private DataGridView dataGridView1;
        private DataGridViewTextBoxColumn CardAdder_TB_NameOfCard;
        private DataGridViewTextBoxColumn CardAdder_TB_IDOfCard;
    }
}