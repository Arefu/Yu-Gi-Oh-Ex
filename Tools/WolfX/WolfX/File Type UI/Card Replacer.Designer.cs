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
            groupBox2 = new GroupBox();
            label2 = new Label();
            CARDREP_BTN_UpdateCard = new Button();
            CARDREP_TB_DEF = new TextBox();
            CARDREP_TB_ATK = new TextBox();
            CARDREP_LBL_NewCardDesc = new Label();
            CARDREP_CB_NewCardSelector = new ComboBox();
            CARDREP_PB_NewCardImage = new PictureBox();
            groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)CARDREP_PB_NewCardImage).BeginInit();
            SuspendLayout();
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(label2);
            groupBox2.Controls.Add(CARDREP_BTN_UpdateCard);
            groupBox2.Controls.Add(CARDREP_TB_DEF);
            groupBox2.Controls.Add(CARDREP_TB_ATK);
            groupBox2.Controls.Add(CARDREP_LBL_NewCardDesc);
            groupBox2.Controls.Add(CARDREP_CB_NewCardSelector);
            groupBox2.Controls.Add(CARDREP_PB_NewCardImage);
            groupBox2.Location = new Point(-1, 0);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(310, 441);
            groupBox2.TabIndex = 1;
            groupBox2.TabStop = false;
            groupBox2.Text = "New Card";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(87, 410);
            label2.Name = "label2";
            label2.Size = new Size(12, 15);
            label2.TabIndex = 7;
            label2.Text = "/";
            // 
            // CARDREP_BTN_UpdateCard
            // 
            CARDREP_BTN_UpdateCard.Location = new Point(223, 405);
            CARDREP_BTN_UpdateCard.Name = "CARDREP_BTN_UpdateCard";
            CARDREP_BTN_UpdateCard.Size = new Size(75, 23);
            CARDREP_BTN_UpdateCard.TabIndex = 6;
            CARDREP_BTN_UpdateCard.Text = "Confirm";
            CARDREP_BTN_UpdateCard.UseVisualStyleBackColor = true;
            CARDREP_BTN_UpdateCard.Click += CARDREP_BTN_UpdateCard_Click;
            // 
            // CARDREP_TB_DEF
            // 
            CARDREP_TB_DEF.Location = new Point(105, 406);
            CARDREP_TB_DEF.Name = "CARDREP_TB_DEF";
            CARDREP_TB_DEF.ReadOnly = true;
            CARDREP_TB_DEF.Size = new Size(75, 23);
            CARDREP_TB_DEF.TabIndex = 5;
            // 
            // CARDREP_TB_ATK
            // 
            CARDREP_TB_ATK.Location = new Point(6, 406);
            CARDREP_TB_ATK.Name = "CARDREP_TB_ATK";
            CARDREP_TB_ATK.ReadOnly = true;
            CARDREP_TB_ATK.Size = new Size(75, 23);
            CARDREP_TB_ATK.TabIndex = 4;
            // 
            // CARDREP_LBL_NewCardDesc
            // 
            CARDREP_LBL_NewCardDesc.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            CARDREP_LBL_NewCardDesc.Location = new Point(6, 182);
            CARDREP_LBL_NewCardDesc.Name = "CARDREP_LBL_NewCardDesc";
            CARDREP_LBL_NewCardDesc.Size = new Size(292, 220);
            CARDREP_LBL_NewCardDesc.TabIndex = 2;
            CARDREP_LBL_NewCardDesc.Text = "label2";
            // 
            // CARDREP_CB_NewCardSelector
            // 
            CARDREP_CB_NewCardSelector.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            CARDREP_CB_NewCardSelector.AutoCompleteSource = AutoCompleteSource.ListItems;
            CARDREP_CB_NewCardSelector.FormattingEnabled = true;
            CARDREP_CB_NewCardSelector.Location = new Point(6, 22);
            CARDREP_CB_NewCardSelector.Name = "CARDREP_CB_NewCardSelector";
            CARDREP_CB_NewCardSelector.Size = new Size(292, 23);
            CARDREP_CB_NewCardSelector.TabIndex = 1;
            CARDREP_CB_NewCardSelector.SelectedIndexChanged += CARDREP_CB_NewCardSelector_SelectedIndexChanged;
            // 
            // CARDREP_PB_NewCardImage
            // 
            CARDREP_PB_NewCardImage.Location = new Point(6, 51);
            CARDREP_PB_NewCardImage.Name = "CARDREP_PB_NewCardImage";
            CARDREP_PB_NewCardImage.Size = new Size(292, 128);
            CARDREP_PB_NewCardImage.SizeMode = PictureBoxSizeMode.Zoom;
            CARDREP_PB_NewCardImage.TabIndex = 0;
            CARDREP_PB_NewCardImage.TabStop = false;
            // 
            // Card_Changer
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(304, 441);
            Controls.Add(groupBox2);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "Card_Changer";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Deck Editor - Card Replacer";
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)CARDREP_PB_NewCardImage).EndInit();
            ResumeLayout(false);
        }

        #endregion
        private GroupBox groupBox2;
        private Label CARDREP_LBL_NewCardDesc;
        private ComboBox CARDREP_CB_NewCardSelector;
        private PictureBox CARDREP_PB_NewCardImage;
        private Button CARDREP_BTN_UpdateCard;
        private TextBox CARDREP_TB_DEF;
        private TextBox CARDREP_TB_ATK;
        private Label label2;
    }
}