using System.Security.Cryptography.X509Certificates;
using WolfX.Types;

namespace WolfX
{
    partial class WolfUI
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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

        public static DfymooUI DFY_Editor;
        public static AnimlistUI ANIMS_Editor;

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            WolfX_TabManager = new TabControl();
            Page_CardManager = new TabPage();
            groupBox18 = new GroupBox();
            label26 = new Label();
            CARDS_TB_CardNumber = new TextBox();
            label29 = new Label();
            CARDS_TB_Kana = new TextBox();
            label28 = new Label();
            CARDS_TB_CardPassword = new TextBox();
            CARDS_CB_SimilarCardName = new ComboBox();
            CARDS_RB_SimilarOnEffect = new RadioButton();
            CARDS_RB_AlwaysSimilar = new RadioButton();
            label25 = new Label();
            label6 = new Label();
            groupBox6 = new GroupBox();
            CARDS_CB_LoadCards = new CheckBox();
            CARDS_BTN_CloseBinder = new Button();
            CARDS_BTN_OpenCards = new Button();
            CARDS_BTN_SaveCard = new Button();
            groupBox2 = new GroupBox();
            CARDS_Nud_CardLevel = new NumericUpDown();
            CARDS_CB_CardAttribute = new ComboBox();
            CARDS_CB_CardTypes = new ComboBox();
            CARDS_CB_CardID = new ComboBox();
            CB_CardLevel = new Label();
            label5 = new Label();
            label4 = new Label();
            label3 = new Label();
            groupBox1 = new GroupBox();
            CARDS_TB_CardName = new TextBox();
            CARDS_TB_CardDef = new TextBox();
            CARDS_TB_CardAtk = new TextBox();
            label2 = new Label();
            label1 = new Label();
            CARDS_TB_CardDesc = new TextBox();
            CARDS_PB_CardPicture = new PictureBox();
            Page_ZibManager = new TabPage();
            groupBox4 = new GroupBox();
            ARCHIVE_BTN_PackZIB = new Button();
            ARCHIVE_BTN_OpenZIB = new Button();
            ARCHIVE_BTN_ExtractZIB = new Button();
            ARCHIVE_CB_ShowPreviewImage = new CheckBox();
            ARCHIVE_LV_ArchiveItems = new ListView();
            groupBox3 = new GroupBox();
            ARCHIVE_LBL_ArchiveItems = new Label();
            ARCHIVE_LBL_ArchiveSize = new Label();
            ARCHIVE_LBL_ArchiveName = new Label();
            lbl_NumberOfItemsPrompt = new Label();
            lbl_FileSizePrompt = new Label();
            lbl_NamePrompt = new Label();
            Page_DFYMOOManager = new TabPage();
            groupBox7 = new GroupBox();
            label7 = new Label();
            DFY_NUD_W = new NumericUpDown();
            DFY_NUD_Y = new NumericUpDown();
            DFY_NUD_H = new NumericUpDown();
            DFY_NUD_X = new NumericUpDown();
            DFY_POS_Y = new Label();
            label8 = new Label();
            DFY_POS_X = new Label();
            DFYMOO_ItemList = new ListView();
            groupBox8 = new GroupBox();
            DFY_BTN_Save = new Button();
            DFY_BTN_Load = new Button();
            groupBox9 = new GroupBox();
            lbl_Dfymoo_NumOfItems = new Label();
            lbl_Dfymoo_name = new Label();
            label10 = new Label();
            label12 = new Label();
            Page_ANIMLISTManager = new TabPage();
            ANIMS_LV_ItemsInScene = new ListView();
            groupBox10 = new GroupBox();
            ANIMS_BTN_SaveScene = new Button();
            ANIMS_BTN_OpenScene = new Button();
            ANIM_GB_Info = new GroupBox();
            ANIMS_LBL_Count = new Label();
            ANIMS_LBL_NumOfItems = new Label();
            Page_BNDManager = new TabPage();
            label11 = new Label();
            STRMAN_PB_HowFarThroughTheFile = new ProgressBar();
            STRMAN_TB_NewStringValue = new TextBox();
            label9 = new Label();
            STRMAN_LB_CurrentFileStrings = new ListBox();
            STRMAN_TB_Search = new TextBox();
            STRMAN_BTN_Search = new Button();
            STRMAN_CheckB_CaseSensitive = new CheckBox();
            groupBox5 = new GroupBox();
            CREDITS_CheckB_IsCredit = new CheckBox();
            STRMAN_BTN_SaveStrings = new Button();
            STRMAN_BTN_OpenStrings = new Button();
            groupBox11 = new GroupBox();
            STRMAN_LBL_LocalCount = new Label();
            STRMAN_LBL_Local = new Label();
            Page_YDCManager = new TabPage();
            groupBox14 = new GroupBox();
            YDC_CB_UseSimpleEditor = new CheckBox();
            YDC_BTN_ReplaceCard = new Button();
            YDC_BTN_RemoveCard = new Button();
            YDC_BTN_AddCard = new Button();
            tabControl1 = new TabControl();
            tabPage1 = new TabPage();
            YDC_LV_MainDeckCards = new ListView();
            tabPage2 = new TabPage();
            tabPage3 = new TabPage();
            groupBox12 = new GroupBox();
            YDC_CHKBOX_UseCardID = new CheckBox();
            YDC_BTN_OpenSaveFile = new Button();
            YDC_CHKBOX_LoadPictures = new CheckBox();
            YDC_BTN_SaveDeck = new Button();
            YDC_BTN_OpenDeck = new Button();
            groupBox13 = new GroupBox();
            YDC_TB_DeckName = new TextBox();
            label13 = new Label();
            YDC_LBL_NumOfCardsInExtra = new Label();
            label18 = new Label();
            YDC_LBL_NumOfCardsInSide = new Label();
            label16 = new Label();
            YDC_LBL_NumOfCardInMain = new Label();
            label14 = new Label();
            Page_PDLimitsManager = new TabPage();
            tabControl2 = new TabControl();
            PDL_ForbiddenCards = new TabPage();
            PDL_LV_ForbiddenCards = new ListView();
            PDL_SemiLimitedCards = new TabPage();
            PDL_LV_SemiLimitedCards = new ListView();
            PDL_LimitedCards = new TabPage();
            PDL_LV_LimitedCards = new ListView();
            groupBox15 = new GroupBox();
            PDL_CB_IsUsingSimpleAddBox = new CheckBox();
            button1 = new Button();
            PDL_BTN_RemoveCardFromList = new Button();
            PDL_BTN_AddCardToList = new Button();
            groupBox16 = new GroupBox();
            PDL_CB_UseCardID = new CheckBox();
            PDL_CB_LoadImages = new CheckBox();
            PDL_BTN_SavePDL = new Button();
            PDL_BTN_OpenPDL = new Button();
            groupBox17 = new GroupBox();
            PDL_LBL_NumOfSemiLimited = new Label();
            label17 = new Label();
            PDL_LBL_NumOfLimited = new Label();
            label19 = new Label();
            PDL_LBL_NumOfForbidden = new Label();
            label21 = new Label();
            label23 = new Label();
            Page_CardShopManager = new TabPage();
            groupBox20 = new GroupBox();
            button2 = new Button();
            PACKDEF_BTN_OpenPackDEF = new Button();
            groupBox19 = new GroupBox();
            label24 = new Label();
            label22 = new Label();
            label15 = new Label();
            label20 = new Label();
            label27 = new Label();
            MenuBar = new MenuStrip();
            toolsToolStripMenuItem = new ToolStripMenuItem();
            WOLFUI_TOOLITEM_OpenConfigEditor = new ToolStripMenuItem();
            toolStripSeparator1 = new ToolStripSeparator();
            loadGameToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator2 = new ToolStripSeparator();
            WOLFUI_TOOLITEM_Extract = new ToolStripMenuItem();
            WOLFUI_TOOLITEM_Pack = new ToolStripMenuItem();
            toolStripSeparator3 = new ToolStripSeparator();
            WOLFUI_TOOLITEM_SetPath = new ToolStripMenuItem();
            Tools_Verify = new ToolStripMenuItem();
            toolStripSeparator4 = new ToolStripSeparator();
            languageToolStripMenuItem = new ToolStripMenuItem();
            Language_english = new ToolStripMenuItem();
            Language_french = new ToolStripMenuItem();
            Language_german = new ToolStripMenuItem();
            Language_italian = new ToolStripMenuItem();
            Language_japanese = new ToolStripMenuItem();
            Language_russian = new ToolStripMenuItem();
            Language_spanish = new ToolStripMenuItem();
            WolfX_TabManager.SuspendLayout();
            Page_CardManager.SuspendLayout();
            groupBox18.SuspendLayout();
            groupBox6.SuspendLayout();
            groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)CARDS_Nud_CardLevel).BeginInit();
            groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)CARDS_PB_CardPicture).BeginInit();
            Page_ZibManager.SuspendLayout();
            groupBox4.SuspendLayout();
            groupBox3.SuspendLayout();
            Page_DFYMOOManager.SuspendLayout();
            groupBox7.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)DFY_NUD_W).BeginInit();
            ((System.ComponentModel.ISupportInitialize)DFY_NUD_Y).BeginInit();
            ((System.ComponentModel.ISupportInitialize)DFY_NUD_H).BeginInit();
            ((System.ComponentModel.ISupportInitialize)DFY_NUD_X).BeginInit();
            groupBox8.SuspendLayout();
            groupBox9.SuspendLayout();
            Page_ANIMLISTManager.SuspendLayout();
            groupBox10.SuspendLayout();
            ANIM_GB_Info.SuspendLayout();
            Page_BNDManager.SuspendLayout();
            groupBox5.SuspendLayout();
            groupBox11.SuspendLayout();
            Page_YDCManager.SuspendLayout();
            groupBox14.SuspendLayout();
            tabControl1.SuspendLayout();
            tabPage1.SuspendLayout();
            groupBox12.SuspendLayout();
            groupBox13.SuspendLayout();
            Page_PDLimitsManager.SuspendLayout();
            tabControl2.SuspendLayout();
            PDL_ForbiddenCards.SuspendLayout();
            PDL_SemiLimitedCards.SuspendLayout();
            PDL_LimitedCards.SuspendLayout();
            groupBox15.SuspendLayout();
            groupBox16.SuspendLayout();
            groupBox17.SuspendLayout();
            Page_CardShopManager.SuspendLayout();
            groupBox20.SuspendLayout();
            groupBox19.SuspendLayout();
            MenuBar.SuspendLayout();
            SuspendLayout();
            // 
            // WolfX_TabManager
            // 
            WolfX_TabManager.Controls.Add(Page_CardManager);
            WolfX_TabManager.Controls.Add(Page_ZibManager);
            WolfX_TabManager.Controls.Add(Page_DFYMOOManager);
            WolfX_TabManager.Controls.Add(Page_ANIMLISTManager);
            WolfX_TabManager.Controls.Add(Page_BNDManager);
            WolfX_TabManager.Controls.Add(Page_YDCManager);
            WolfX_TabManager.Controls.Add(Page_PDLimitsManager);
            WolfX_TabManager.Controls.Add(Page_CardShopManager);
            WolfX_TabManager.Dock = DockStyle.Fill;
            WolfX_TabManager.Location = new Point(0, 24);
            WolfX_TabManager.Name = "WolfX_TabManager";
            WolfX_TabManager.SelectedIndex = 0;
            WolfX_TabManager.Size = new Size(1264, 657);
            WolfX_TabManager.TabIndex = 0;
            // 
            // Page_CardManager
            // 
            Page_CardManager.Controls.Add(groupBox18);
            Page_CardManager.Controls.Add(groupBox6);
            Page_CardManager.Controls.Add(groupBox2);
            Page_CardManager.Controls.Add(groupBox1);
            Page_CardManager.Location = new Point(4, 24);
            Page_CardManager.Name = "Page_CardManager";
            Page_CardManager.Padding = new Padding(3);
            Page_CardManager.Size = new Size(1256, 629);
            Page_CardManager.TabIndex = 0;
            Page_CardManager.Text = "Card Manager";
            Page_CardManager.UseVisualStyleBackColor = true;
            // 
            // groupBox18
            // 
            groupBox18.Controls.Add(label26);
            groupBox18.Controls.Add(CARDS_TB_CardNumber);
            groupBox18.Controls.Add(label29);
            groupBox18.Controls.Add(CARDS_TB_Kana);
            groupBox18.Controls.Add(label28);
            groupBox18.Controls.Add(CARDS_TB_CardPassword);
            groupBox18.Controls.Add(CARDS_CB_SimilarCardName);
            groupBox18.Controls.Add(CARDS_RB_SimilarOnEffect);
            groupBox18.Controls.Add(CARDS_RB_AlwaysSimilar);
            groupBox18.Controls.Add(label25);
            groupBox18.Controls.Add(label6);
            groupBox18.Location = new Point(334, 175);
            groupBox18.Name = "groupBox18";
            groupBox18.Size = new Size(320, 266);
            groupBox18.TabIndex = 6;
            groupBox18.TabStop = false;
            groupBox18.Text = "Card Properties";
            // 
            // label26
            // 
            label26.AutoSize = true;
            label26.Font = new Font("Segoe UI", 11F);
            label26.Location = new Point(205, 63);
            label26.Name = "label26";
            label26.Size = new Size(98, 20);
            label26.TabIndex = 23;
            label26.Text = "Card Number";
            // 
            // CARDS_TB_CardNumber
            // 
            CARDS_TB_CardNumber.Location = new Point(205, 86);
            CARDS_TB_CardNumber.MaxLength = 3;
            CARDS_TB_CardNumber.Name = "CARDS_TB_CardNumber";
            CARDS_TB_CardNumber.Size = new Size(77, 23);
            CARDS_TB_CardNumber.TabIndex = 22;
            // 
            // label29
            // 
            label29.AutoSize = true;
            label29.Font = new Font("Segoe UI", 11F);
            label29.Location = new Point(204, 14);
            label29.Name = "label29";
            label29.Size = new Size(77, 20);
            label29.TabIndex = 21;
            label29.Text = "Card Kana";
            // 
            // CARDS_TB_Kana
            // 
            CARDS_TB_Kana.Location = new Point(204, 37);
            CARDS_TB_Kana.MaxLength = 3;
            CARDS_TB_Kana.Name = "CARDS_TB_Kana";
            CARDS_TB_Kana.Size = new Size(77, 23);
            CARDS_TB_Kana.TabIndex = 20;
            CARDS_TB_Kana.TextChanged += CARDS_TB_Kana_TextChanged;
            // 
            // label28
            // 
            label28.AutoSize = true;
            label28.Font = new Font("Segoe UI", 11F);
            label28.Location = new Point(6, 128);
            label28.Name = "label28";
            label28.Size = new Size(105, 20);
            label28.TabIndex = 19;
            label28.Text = "Card Password";
            // 
            // CARDS_TB_CardPassword
            // 
            CARDS_TB_CardPassword.Location = new Point(6, 151);
            CARDS_TB_CardPassword.Name = "CARDS_TB_CardPassword";
            CARDS_TB_CardPassword.Size = new Size(141, 23);
            CARDS_TB_CardPassword.TabIndex = 18;
            CARDS_TB_CardPassword.TextChanged += CARDS_TB_CardPassword_TextChanged;
            // 
            // CARDS_CB_SimilarCardName
            // 
            CARDS_CB_SimilarCardName.AutoCompleteMode = AutoCompleteMode.Suggest;
            CARDS_CB_SimilarCardName.FormattingEnabled = true;
            CARDS_CB_SimilarCardName.Location = new Point(6, 37);
            CARDS_CB_SimilarCardName.Name = "CARDS_CB_SimilarCardName";
            CARDS_CB_SimilarCardName.Size = new Size(186, 23);
            CARDS_CB_SimilarCardName.TabIndex = 7;
            CARDS_CB_SimilarCardName.SelectedIndexChanged += CARDS_CB_SimilarCardName_SelectedIndexChanged;
            // 
            // CARDS_RB_SimilarOnEffect
            // 
            CARDS_RB_SimilarOnEffect.AutoSize = true;
            CARDS_RB_SimilarOnEffect.Location = new Point(6, 106);
            CARDS_RB_SimilarOnEffect.Name = "CARDS_RB_SimilarOnEffect";
            CARDS_RB_SimilarOnEffect.Size = new Size(71, 19);
            CARDS_RB_SimilarOnEffect.TabIndex = 6;
            CARDS_RB_SimilarOnEffect.TabStop = true;
            CARDS_RB_SimilarOnEffect.Text = "By Effect";
            CARDS_RB_SimilarOnEffect.UseVisualStyleBackColor = true;
            CARDS_RB_SimilarOnEffect.CheckedChanged += CARDS_RB_SimilarOnEffect_CheckedChanged;
            // 
            // CARDS_RB_AlwaysSimilar
            // 
            CARDS_RB_AlwaysSimilar.AutoSize = true;
            CARDS_RB_AlwaysSimilar.Location = new Point(6, 81);
            CARDS_RB_AlwaysSimilar.Name = "CARDS_RB_AlwaysSimilar";
            CARDS_RB_AlwaysSimilar.Size = new Size(62, 19);
            CARDS_RB_AlwaysSimilar.TabIndex = 5;
            CARDS_RB_AlwaysSimilar.TabStop = true;
            CARDS_RB_AlwaysSimilar.Text = "Always";
            CARDS_RB_AlwaysSimilar.UseVisualStyleBackColor = true;
            CARDS_RB_AlwaysSimilar.CheckedChanged += CARDS_RB_AlwaysSimilar_CheckedChanged;
            // 
            // label25
            // 
            label25.AutoSize = true;
            label25.Location = new Point(6, 63);
            label25.Name = "label25";
            label25.Size = new Size(102, 15);
            label25.TabIndex = 2;
            label25.Text = "Similar Condition:";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(6, 19);
            label6.Name = "label6";
            label6.Size = new Size(74, 15);
            label6.TabIndex = 0;
            label6.Text = "Similar Card:";
            // 
            // groupBox6
            // 
            groupBox6.Controls.Add(CARDS_CB_LoadCards);
            groupBox6.Controls.Add(CARDS_BTN_CloseBinder);
            groupBox6.Controls.Add(CARDS_BTN_OpenCards);
            groupBox6.Controls.Add(CARDS_BTN_SaveCard);
            groupBox6.Location = new Point(334, 447);
            groupBox6.Name = "groupBox6";
            groupBox6.Size = new Size(303, 154);
            groupBox6.TabIndex = 5;
            groupBox6.TabStop = false;
            groupBox6.Text = "Options";
            // 
            // CARDS_CB_LoadCards
            // 
            CARDS_CB_LoadCards.AutoSize = true;
            CARDS_CB_LoadCards.Checked = true;
            CARDS_CB_LoadCards.CheckState = CheckState.Checked;
            CARDS_CB_LoadCards.Location = new Point(204, 22);
            CARDS_CB_LoadCards.Name = "CARDS_CB_LoadCards";
            CARDS_CB_LoadCards.Size = new Size(93, 19);
            CARDS_CB_LoadCards.TabIndex = 3;
            CARDS_CB_LoadCards.Text = "Load Images";
            CARDS_CB_LoadCards.UseVisualStyleBackColor = true;
            // 
            // CARDS_BTN_CloseBinder
            // 
            CARDS_BTN_CloseBinder.Location = new Point(6, 123);
            CARDS_BTN_CloseBinder.Name = "CARDS_BTN_CloseBinder";
            CARDS_BTN_CloseBinder.Size = new Size(107, 25);
            CARDS_BTN_CloseBinder.TabIndex = 2;
            CARDS_BTN_CloseBinder.Text = "Close Cards";
            CARDS_BTN_CloseBinder.UseVisualStyleBackColor = true;
            CARDS_BTN_CloseBinder.Click += CARDS_BTN_CloseBinder_Click;
            // 
            // CARDS_BTN_OpenCards
            // 
            CARDS_BTN_OpenCards.Location = new Point(6, 22);
            CARDS_BTN_OpenCards.Name = "CARDS_BTN_OpenCards";
            CARDS_BTN_OpenCards.Size = new Size(107, 25);
            CARDS_BTN_OpenCards.TabIndex = 1;
            CARDS_BTN_OpenCards.Text = "Open Cards";
            CARDS_BTN_OpenCards.UseVisualStyleBackColor = true;
            CARDS_BTN_OpenCards.Click += CARDS_BTN_OpenCards_Click;
            // 
            // CARDS_BTN_SaveCard
            // 
            CARDS_BTN_SaveCard.Location = new Point(6, 52);
            CARDS_BTN_SaveCard.Name = "CARDS_BTN_SaveCard";
            CARDS_BTN_SaveCard.Size = new Size(107, 25);
            CARDS_BTN_SaveCard.TabIndex = 0;
            CARDS_BTN_SaveCard.Text = "Save Card";
            CARDS_BTN_SaveCard.UseVisualStyleBackColor = true;
            CARDS_BTN_SaveCard.Click += CARDS_BTN_SaveCard_Click;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(CARDS_Nud_CardLevel);
            groupBox2.Controls.Add(CARDS_CB_CardAttribute);
            groupBox2.Controls.Add(CARDS_CB_CardTypes);
            groupBox2.Controls.Add(CARDS_CB_CardID);
            groupBox2.Controls.Add(CB_CardLevel);
            groupBox2.Controls.Add(label5);
            groupBox2.Controls.Add(label4);
            groupBox2.Controls.Add(label3);
            groupBox2.Location = new Point(334, 6);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(320, 163);
            groupBox2.TabIndex = 2;
            groupBox2.TabStop = false;
            groupBox2.Text = "*";
            // 
            // CARDS_Nud_CardLevel
            // 
            CARDS_Nud_CardLevel.Location = new Point(175, 42);
            CARDS_Nud_CardLevel.Name = "CARDS_Nud_CardLevel";
            CARDS_Nud_CardLevel.Size = new Size(128, 23);
            CARDS_Nud_CardLevel.TabIndex = 15;
            CARDS_Nud_CardLevel.ValueChanged += CARDS_Nud_CardLevel_ValueChanged;
            // 
            // CARDS_CB_CardAttribute
            // 
            CARDS_CB_CardAttribute.FormattingEnabled = true;
            CARDS_CB_CardAttribute.Items.AddRange(new object[] { "Unknown", "Light Monster", "Dark Monster", "Water Monster", "Fire Monster", "Earth Monster", "Wind Monster", "Divine Monster", "Spell", "Trap" });
            CARDS_CB_CardAttribute.Location = new Point(175, 91);
            CARDS_CB_CardAttribute.Name = "CARDS_CB_CardAttribute";
            CARDS_CB_CardAttribute.Size = new Size(128, 23);
            CARDS_CB_CardAttribute.TabIndex = 10;
            CARDS_CB_CardAttribute.SelectedIndexChanged += CARDS_CB_CardAttribute_SelectedIndexChanged;
            // 
            // CARDS_CB_CardTypes
            // 
            CARDS_CB_CardTypes.FormattingEnabled = true;
            CARDS_CB_CardTypes.Location = new Point(6, 91);
            CARDS_CB_CardTypes.Name = "CARDS_CB_CardTypes";
            CARDS_CB_CardTypes.Size = new Size(128, 23);
            CARDS_CB_CardTypes.TabIndex = 9;
            CARDS_CB_CardTypes.SelectedIndexChanged += CARDS_CB_CardTypes_SelectedIndexChanged;
            // 
            // CARDS_CB_CardID
            // 
            CARDS_CB_CardID.AutoCompleteMode = AutoCompleteMode.Suggest;
            CARDS_CB_CardID.FormattingEnabled = true;
            CARDS_CB_CardID.Location = new Point(6, 42);
            CARDS_CB_CardID.Name = "CARDS_CB_CardID";
            CARDS_CB_CardID.Size = new Size(128, 23);
            CARDS_CB_CardID.TabIndex = 7;
            CARDS_CB_CardID.SelectedIndexChanged += CARDS_CB_CardID_SelectedIndexChanged;
            // 
            // CB_CardLevel
            // 
            CB_CardLevel.AutoSize = true;
            CB_CardLevel.Font = new Font("Segoe UI", 11F);
            CB_CardLevel.Location = new Point(175, 18);
            CB_CardLevel.Name = "CB_CardLevel";
            CB_CardLevel.Size = new Size(78, 20);
            CB_CardLevel.TabIndex = 5;
            CB_CardLevel.Text = "Card Level";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Font = new Font("Segoe UI", 11F);
            label5.Location = new Point(6, 68);
            label5.Name = "label5";
            label5.Size = new Size(74, 20);
            label5.TabIndex = 2;
            label5.Text = "Card Kind";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Font = new Font("Segoe UI", 11F);
            label4.Location = new Point(175, 68);
            label4.Name = "label4";
            label4.Size = new Size(103, 20);
            label4.TabIndex = 1;
            label4.Text = "Card Attribute";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Font = new Font("Segoe UI", 11F);
            label3.Location = new Point(6, 18);
            label3.Name = "label3";
            label3.Size = new Size(59, 20);
            label3.TabIndex = 0;
            label3.Text = "Card ID";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(CARDS_TB_CardName);
            groupBox1.Controls.Add(CARDS_TB_CardDef);
            groupBox1.Controls.Add(CARDS_TB_CardAtk);
            groupBox1.Controls.Add(label2);
            groupBox1.Controls.Add(label1);
            groupBox1.Controls.Add(CARDS_TB_CardDesc);
            groupBox1.Controls.Add(CARDS_PB_CardPicture);
            groupBox1.Location = new Point(8, 6);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(320, 595);
            groupBox1.TabIndex = 1;
            groupBox1.TabStop = false;
            groupBox1.Text = "Card Preview";
            // 
            // CARDS_TB_CardName
            // 
            CARDS_TB_CardName.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            CARDS_TB_CardName.Location = new Point(6, 19);
            CARDS_TB_CardName.Name = "CARDS_TB_CardName";
            CARDS_TB_CardName.Size = new Size(305, 23);
            CARDS_TB_CardName.TabIndex = 7;
            CARDS_TB_CardName.TextChanged += CARDS_CB_CardName_TextChanged;
            // 
            // CARDS_TB_CardDef
            // 
            CARDS_TB_CardDef.Location = new Point(254, 561);
            CARDS_TB_CardDef.Name = "CARDS_TB_CardDef";
            CARDS_TB_CardDef.Size = new Size(57, 23);
            CARDS_TB_CardDef.TabIndex = 6;
            CARDS_TB_CardDef.TextChanged += TB_CardDef_TextChanged;
            // 
            // CARDS_TB_CardAtk
            // 
            CARDS_TB_CardAtk.Location = new Point(50, 563);
            CARDS_TB_CardAtk.Name = "CARDS_TB_CardAtk";
            CARDS_TB_CardAtk.Size = new Size(57, 23);
            CARDS_TB_CardAtk.TabIndex = 5;
            CARDS_TB_CardAtk.TextChanged += TB_CardAtk_TextChanged;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Font = new Font("Segoe UI", 11F);
            label2.Location = new Point(210, 564);
            label2.Name = "label2";
            label2.Size = new Size(38, 20);
            label2.TabIndex = 4;
            label2.Text = "DEF:";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 11F);
            label1.Location = new Point(6, 564);
            label1.Name = "label1";
            label1.Size = new Size(38, 20);
            label1.TabIndex = 3;
            label1.Text = "ATK:";
            // 
            // CARDS_TB_CardDesc
            // 
            CARDS_TB_CardDesc.Location = new Point(6, 403);
            CARDS_TB_CardDesc.Multiline = true;
            CARDS_TB_CardDesc.Name = "CARDS_TB_CardDesc";
            CARDS_TB_CardDesc.ScrollBars = ScrollBars.Vertical;
            CARDS_TB_CardDesc.Size = new Size(305, 154);
            CARDS_TB_CardDesc.TabIndex = 2;
            CARDS_TB_CardDesc.TextChanged += CARDS_TB_CardDesc_TextChanged;
            // 
            // CARDS_PB_CardPicture
            // 
            CARDS_PB_CardPicture.Location = new Point(6, 51);
            CARDS_PB_CardPicture.Name = "CARDS_PB_CardPicture";
            CARDS_PB_CardPicture.Size = new Size(305, 346);
            CARDS_PB_CardPicture.TabIndex = 0;
            CARDS_PB_CardPicture.TabStop = false;
            // 
            // Page_ZibManager
            // 
            Page_ZibManager.Controls.Add(groupBox4);
            Page_ZibManager.Controls.Add(ARCHIVE_LV_ArchiveItems);
            Page_ZibManager.Controls.Add(groupBox3);
            Page_ZibManager.Location = new Point(4, 24);
            Page_ZibManager.Name = "Page_ZibManager";
            Page_ZibManager.Padding = new Padding(3);
            Page_ZibManager.Size = new Size(1256, 629);
            Page_ZibManager.TabIndex = 1;
            Page_ZibManager.Text = "Archive Manager";
            Page_ZibManager.UseVisualStyleBackColor = true;
            // 
            // groupBox4
            // 
            groupBox4.Controls.Add(ARCHIVE_BTN_PackZIB);
            groupBox4.Controls.Add(ARCHIVE_BTN_OpenZIB);
            groupBox4.Controls.Add(ARCHIVE_BTN_ExtractZIB);
            groupBox4.Controls.Add(ARCHIVE_CB_ShowPreviewImage);
            groupBox4.Location = new Point(214, 6);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(200, 100);
            groupBox4.TabIndex = 1;
            groupBox4.TabStop = false;
            groupBox4.Text = "Archive Tools";
            // 
            // ARCHIVE_BTN_PackZIB
            // 
            ARCHIVE_BTN_PackZIB.Location = new Point(87, 69);
            ARCHIVE_BTN_PackZIB.Name = "ARCHIVE_BTN_PackZIB";
            ARCHIVE_BTN_PackZIB.Size = new Size(107, 25);
            ARCHIVE_BTN_PackZIB.TabIndex = 6;
            ARCHIVE_BTN_PackZIB.Text = "Pack Content";
            ARCHIVE_BTN_PackZIB.UseVisualStyleBackColor = true;
            ARCHIVE_BTN_PackZIB.Click += ARCHIVE_BTN_SaveZIB_Click;
            // 
            // ARCHIVE_BTN_OpenZIB
            // 
            ARCHIVE_BTN_OpenZIB.Location = new Point(6, 42);
            ARCHIVE_BTN_OpenZIB.Name = "ARCHIVE_BTN_OpenZIB";
            ARCHIVE_BTN_OpenZIB.Size = new Size(75, 25);
            ARCHIVE_BTN_OpenZIB.TabIndex = 5;
            ARCHIVE_BTN_OpenZIB.Text = "Open";
            ARCHIVE_BTN_OpenZIB.UseVisualStyleBackColor = true;
            ARCHIVE_BTN_OpenZIB.Click += ARCHIVE_BTN_OpenZIB_Click;
            // 
            // ARCHIVE_BTN_ExtractZIB
            // 
            ARCHIVE_BTN_ExtractZIB.Enabled = false;
            ARCHIVE_BTN_ExtractZIB.Location = new Point(87, 42);
            ARCHIVE_BTN_ExtractZIB.Name = "ARCHIVE_BTN_ExtractZIB";
            ARCHIVE_BTN_ExtractZIB.Size = new Size(107, 25);
            ARCHIVE_BTN_ExtractZIB.TabIndex = 2;
            ARCHIVE_BTN_ExtractZIB.Text = "Extract Content";
            ARCHIVE_BTN_ExtractZIB.UseVisualStyleBackColor = true;
            ARCHIVE_BTN_ExtractZIB.Click += ARCHIVE_BTN_ExtractZIB_Click;
            // 
            // ARCHIVE_CB_ShowPreviewImage
            // 
            ARCHIVE_CB_ShowPreviewImage.AutoSize = true;
            ARCHIVE_CB_ShowPreviewImage.Location = new Point(6, 17);
            ARCHIVE_CB_ShowPreviewImage.Name = "ARCHIVE_CB_ShowPreviewImage";
            ARCHIVE_CB_ShowPreviewImage.Size = new Size(139, 19);
            ARCHIVE_CB_ShowPreviewImage.TabIndex = 1;
            ARCHIVE_CB_ShowPreviewImage.Text = "Show Picture Preview";
            ARCHIVE_CB_ShowPreviewImage.UseVisualStyleBackColor = true;
            // 
            // ARCHIVE_LV_ArchiveItems
            // 
            ARCHIVE_LV_ArchiveItems.Location = new Point(8, 134);
            ARCHIVE_LV_ArchiveItems.MultiSelect = false;
            ARCHIVE_LV_ArchiveItems.Name = "ARCHIVE_LV_ArchiveItems";
            ARCHIVE_LV_ArchiveItems.ShowGroups = false;
            ARCHIVE_LV_ArchiveItems.Size = new Size(1240, 474);
            ARCHIVE_LV_ArchiveItems.TabIndex = 1;
            ARCHIVE_LV_ArchiveItems.UseCompatibleStateImageBehavior = false;
            ARCHIVE_LV_ArchiveItems.MouseDoubleClick += ARCHIVE_LV_ArchiveItems_MouseDoubleClick;
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(ARCHIVE_LBL_ArchiveItems);
            groupBox3.Controls.Add(ARCHIVE_LBL_ArchiveSize);
            groupBox3.Controls.Add(ARCHIVE_LBL_ArchiveName);
            groupBox3.Controls.Add(lbl_NumberOfItemsPrompt);
            groupBox3.Controls.Add(lbl_FileSizePrompt);
            groupBox3.Controls.Add(lbl_NamePrompt);
            groupBox3.Location = new Point(8, 6);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(200, 100);
            groupBox3.TabIndex = 0;
            groupBox3.TabStop = false;
            groupBox3.Text = "Archive Info";
            // 
            // ARCHIVE_LBL_ArchiveItems
            // 
            ARCHIVE_LBL_ArchiveItems.AutoSize = true;
            ARCHIVE_LBL_ArchiveItems.Location = new Point(114, 65);
            ARCHIVE_LBL_ArchiveItems.Name = "ARCHIVE_LBL_ArchiveItems";
            ARCHIVE_LBL_ArchiveItems.Size = new Size(0, 15);
            ARCHIVE_LBL_ArchiveItems.TabIndex = 5;
            // 
            // ARCHIVE_LBL_ArchiveSize
            // 
            ARCHIVE_LBL_ArchiveSize.AutoSize = true;
            ARCHIVE_LBL_ArchiveSize.Location = new Point(63, 42);
            ARCHIVE_LBL_ArchiveSize.Name = "ARCHIVE_LBL_ArchiveSize";
            ARCHIVE_LBL_ArchiveSize.Size = new Size(0, 15);
            ARCHIVE_LBL_ArchiveSize.TabIndex = 4;
            // 
            // ARCHIVE_LBL_ArchiveName
            // 
            ARCHIVE_LBL_ArchiveName.AutoSize = true;
            ARCHIVE_LBL_ArchiveName.Location = new Point(54, 19);
            ARCHIVE_LBL_ArchiveName.Name = "ARCHIVE_LBL_ArchiveName";
            ARCHIVE_LBL_ArchiveName.Size = new Size(0, 15);
            ARCHIVE_LBL_ArchiveName.TabIndex = 3;
            // 
            // lbl_NumberOfItemsPrompt
            // 
            lbl_NumberOfItemsPrompt.AutoSize = true;
            lbl_NumberOfItemsPrompt.Location = new Point(6, 65);
            lbl_NumberOfItemsPrompt.Name = "lbl_NumberOfItemsPrompt";
            lbl_NumberOfItemsPrompt.Size = new Size(102, 15);
            lbl_NumberOfItemsPrompt.TabIndex = 2;
            lbl_NumberOfItemsPrompt.Text = "Number Of Items:";
            // 
            // lbl_FileSizePrompt
            // 
            lbl_FileSizePrompt.AutoSize = true;
            lbl_FileSizePrompt.Location = new Point(6, 42);
            lbl_FileSizePrompt.Name = "lbl_FileSizePrompt";
            lbl_FileSizePrompt.Size = new Size(51, 15);
            lbl_FileSizePrompt.TabIndex = 1;
            lbl_FileSizePrompt.Text = "File Size:";
            // 
            // lbl_NamePrompt
            // 
            lbl_NamePrompt.AutoSize = true;
            lbl_NamePrompt.Location = new Point(6, 19);
            lbl_NamePrompt.Name = "lbl_NamePrompt";
            lbl_NamePrompt.Size = new Size(42, 15);
            lbl_NamePrompt.TabIndex = 0;
            lbl_NamePrompt.Text = "Name:";
            // 
            // Page_DFYMOOManager
            // 
            Page_DFYMOOManager.Controls.Add(groupBox7);
            Page_DFYMOOManager.Controls.Add(DFYMOO_ItemList);
            Page_DFYMOOManager.Controls.Add(groupBox8);
            Page_DFYMOOManager.Controls.Add(groupBox9);
            Page_DFYMOOManager.Location = new Point(4, 24);
            Page_DFYMOOManager.Name = "Page_DFYMOOManager";
            Page_DFYMOOManager.Padding = new Padding(3);
            Page_DFYMOOManager.Size = new Size(1256, 629);
            Page_DFYMOOManager.TabIndex = 2;
            Page_DFYMOOManager.Text = "DFYMOO Manager";
            Page_DFYMOOManager.UseVisualStyleBackColor = true;
            // 
            // groupBox7
            // 
            groupBox7.Controls.Add(label7);
            groupBox7.Controls.Add(DFY_NUD_W);
            groupBox7.Controls.Add(DFY_NUD_Y);
            groupBox7.Controls.Add(DFY_NUD_H);
            groupBox7.Controls.Add(DFY_NUD_X);
            groupBox7.Controls.Add(DFY_POS_Y);
            groupBox7.Controls.Add(label8);
            groupBox7.Controls.Add(DFY_POS_X);
            groupBox7.Location = new Point(420, 6);
            groupBox7.Name = "groupBox7";
            groupBox7.Size = new Size(200, 100);
            groupBox7.TabIndex = 5;
            groupBox7.TabStop = false;
            groupBox7.Text = "DFYMOO Pos and Size";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(96, 51);
            label7.Name = "label7";
            label7.Size = new Size(21, 15);
            label7.TabIndex = 5;
            label7.Text = "W:";
            // 
            // DFY_NUD_W
            // 
            DFY_NUD_W.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            DFY_NUD_W.Location = new Point(123, 47);
            DFY_NUD_W.Maximum = new decimal(new int[] { -1, int.MaxValue, 0, 0 });
            DFY_NUD_W.Name = "DFY_NUD_W";
            DFY_NUD_W.Size = new Size(64, 22);
            DFY_NUD_W.TabIndex = 7;
            DFY_NUD_W.ValueChanged += DFY_NUD_W_ValueChanged;
            // 
            // DFY_NUD_Y
            // 
            DFY_NUD_Y.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            DFY_NUD_Y.Location = new Point(28, 47);
            DFY_NUD_Y.Maximum = new decimal(new int[] { -1, int.MaxValue, 0, 0 });
            DFY_NUD_Y.Name = "DFY_NUD_Y";
            DFY_NUD_Y.Size = new Size(64, 22);
            DFY_NUD_Y.TabIndex = 3;
            DFY_NUD_Y.ValueChanged += DFY_NUD_Y_ValueChanged;
            // 
            // DFY_NUD_H
            // 
            DFY_NUD_H.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            DFY_NUD_H.Location = new Point(123, 19);
            DFY_NUD_H.Maximum = new decimal(new int[] { -1, int.MaxValue, 0, 0 });
            DFY_NUD_H.Name = "DFY_NUD_H";
            DFY_NUD_H.Size = new Size(64, 22);
            DFY_NUD_H.TabIndex = 6;
            DFY_NUD_H.ValueChanged += DFY_NUD_H_ValueChanged;
            // 
            // DFY_NUD_X
            // 
            DFY_NUD_X.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            DFY_NUD_X.Location = new Point(28, 19);
            DFY_NUD_X.Maximum = new decimal(new int[] { -1, int.MaxValue, 0, 0 });
            DFY_NUD_X.Name = "DFY_NUD_X";
            DFY_NUD_X.Size = new Size(64, 22);
            DFY_NUD_X.TabIndex = 2;
            DFY_NUD_X.ValueChanged += DFY_NUD_X_ValueChanged;
            // 
            // DFY_POS_Y
            // 
            DFY_POS_Y.AutoSize = true;
            DFY_POS_Y.Location = new Point(6, 51);
            DFY_POS_Y.Name = "DFY_POS_Y";
            DFY_POS_Y.Size = new Size(17, 15);
            DFY_POS_Y.TabIndex = 1;
            DFY_POS_Y.Text = "Y:";
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(98, 23);
            label8.Name = "label8";
            label8.Size = new Size(19, 15);
            label8.TabIndex = 4;
            label8.Text = "H:";
            // 
            // DFY_POS_X
            // 
            DFY_POS_X.AutoSize = true;
            DFY_POS_X.Location = new Point(5, 23);
            DFY_POS_X.Name = "DFY_POS_X";
            DFY_POS_X.Size = new Size(17, 15);
            DFY_POS_X.TabIndex = 0;
            DFY_POS_X.Text = "X:";
            // 
            // DFYMOO_ItemList
            // 
            DFYMOO_ItemList.GridLines = true;
            DFYMOO_ItemList.Location = new Point(6, 112);
            DFYMOO_ItemList.MultiSelect = false;
            DFYMOO_ItemList.Name = "DFYMOO_ItemList";
            DFYMOO_ItemList.Size = new Size(1242, 496);
            DFYMOO_ItemList.TabIndex = 5;
            DFYMOO_ItemList.UseCompatibleStateImageBehavior = false;
            DFYMOO_ItemList.View = View.List;
            DFYMOO_ItemList.ItemSelectionChanged += DFYMOO_ItemList_ItemSelectionChanged;
            // 
            // groupBox8
            // 
            groupBox8.Controls.Add(DFY_BTN_Save);
            groupBox8.Controls.Add(DFY_BTN_Load);
            groupBox8.Location = new Point(214, 6);
            groupBox8.Name = "groupBox8";
            groupBox8.Size = new Size(200, 100);
            groupBox8.TabIndex = 4;
            groupBox8.TabStop = false;
            groupBox8.Text = "DFYMOO Tools";
            // 
            // DFY_BTN_Save
            // 
            DFY_BTN_Save.Location = new Point(119, 22);
            DFY_BTN_Save.Name = "DFY_BTN_Save";
            DFY_BTN_Save.Size = new Size(75, 25);
            DFY_BTN_Save.TabIndex = 4;
            DFY_BTN_Save.Text = "Save";
            DFY_BTN_Save.UseVisualStyleBackColor = true;
            DFY_BTN_Save.Click += DFY_BTN_Save_Click;
            // 
            // DFY_BTN_Load
            // 
            DFY_BTN_Load.Location = new Point(6, 22);
            DFY_BTN_Load.Name = "DFY_BTN_Load";
            DFY_BTN_Load.Size = new Size(107, 25);
            DFY_BTN_Load.TabIndex = 2;
            DFY_BTN_Load.Text = "Open Dfymoo";
            DFY_BTN_Load.UseVisualStyleBackColor = true;
            DFY_BTN_Load.Click += DFY_BTN_Load_Click;
            // 
            // groupBox9
            // 
            groupBox9.Controls.Add(lbl_Dfymoo_NumOfItems);
            groupBox9.Controls.Add(lbl_Dfymoo_name);
            groupBox9.Controls.Add(label10);
            groupBox9.Controls.Add(label12);
            groupBox9.Location = new Point(8, 6);
            groupBox9.Name = "groupBox9";
            groupBox9.Size = new Size(200, 100);
            groupBox9.TabIndex = 3;
            groupBox9.TabStop = false;
            groupBox9.Text = "DFYMOO Info";
            // 
            // lbl_Dfymoo_NumOfItems
            // 
            lbl_Dfymoo_NumOfItems.AutoSize = true;
            lbl_Dfymoo_NumOfItems.Location = new Point(114, 39);
            lbl_Dfymoo_NumOfItems.Name = "lbl_Dfymoo_NumOfItems";
            lbl_Dfymoo_NumOfItems.Size = new Size(0, 15);
            lbl_Dfymoo_NumOfItems.TabIndex = 5;
            // 
            // lbl_Dfymoo_name
            // 
            lbl_Dfymoo_name.AutoSize = true;
            lbl_Dfymoo_name.Location = new Point(54, 19);
            lbl_Dfymoo_name.Name = "lbl_Dfymoo_name";
            lbl_Dfymoo_name.Size = new Size(0, 15);
            lbl_Dfymoo_name.TabIndex = 3;
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Location = new Point(6, 39);
            label10.Name = "label10";
            label10.Size = new Size(102, 15);
            label10.TabIndex = 2;
            label10.Text = "Number Of Items:";
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Location = new Point(6, 19);
            label12.Name = "label12";
            label12.Size = new Size(42, 15);
            label12.TabIndex = 0;
            label12.Text = "Name:";
            // 
            // Page_ANIMLISTManager
            // 
            Page_ANIMLISTManager.Controls.Add(ANIMS_LV_ItemsInScene);
            Page_ANIMLISTManager.Controls.Add(groupBox10);
            Page_ANIMLISTManager.Controls.Add(ANIM_GB_Info);
            Page_ANIMLISTManager.Location = new Point(4, 24);
            Page_ANIMLISTManager.Name = "Page_ANIMLISTManager";
            Page_ANIMLISTManager.Size = new Size(1256, 629);
            Page_ANIMLISTManager.TabIndex = 3;
            Page_ANIMLISTManager.Text = "Animlist Manager";
            Page_ANIMLISTManager.UseVisualStyleBackColor = true;
            // 
            // ANIMS_LV_ItemsInScene
            // 
            ANIMS_LV_ItemsInScene.Location = new Point(6, 112);
            ANIMS_LV_ItemsInScene.Name = "ANIMS_LV_ItemsInScene";
            ANIMS_LV_ItemsInScene.Size = new Size(1242, 496);
            ANIMS_LV_ItemsInScene.TabIndex = 6;
            ANIMS_LV_ItemsInScene.UseCompatibleStateImageBehavior = false;
            ANIMS_LV_ItemsInScene.View = View.List;
            // 
            // groupBox10
            // 
            groupBox10.Controls.Add(ANIMS_BTN_SaveScene);
            groupBox10.Controls.Add(ANIMS_BTN_OpenScene);
            groupBox10.Location = new Point(214, 6);
            groupBox10.Name = "groupBox10";
            groupBox10.Size = new Size(200, 100);
            groupBox10.TabIndex = 5;
            groupBox10.TabStop = false;
            groupBox10.Text = "Animlist Tools";
            // 
            // ANIMS_BTN_SaveScene
            // 
            ANIMS_BTN_SaveScene.Location = new Point(119, 22);
            ANIMS_BTN_SaveScene.Name = "ANIMS_BTN_SaveScene";
            ANIMS_BTN_SaveScene.Size = new Size(75, 25);
            ANIMS_BTN_SaveScene.TabIndex = 4;
            ANIMS_BTN_SaveScene.Text = "Save";
            ANIMS_BTN_SaveScene.UseVisualStyleBackColor = true;
            // 
            // ANIMS_BTN_OpenScene
            // 
            ANIMS_BTN_OpenScene.Location = new Point(6, 22);
            ANIMS_BTN_OpenScene.Name = "ANIMS_BTN_OpenScene";
            ANIMS_BTN_OpenScene.Size = new Size(107, 25);
            ANIMS_BTN_OpenScene.TabIndex = 2;
            ANIMS_BTN_OpenScene.Text = "Open Animlist";
            ANIMS_BTN_OpenScene.UseVisualStyleBackColor = true;
            ANIMS_BTN_OpenScene.Click += ANIMS_BTN_OpenScene_Click;
            // 
            // ANIM_GB_Info
            // 
            ANIM_GB_Info.Controls.Add(ANIMS_LBL_Count);
            ANIM_GB_Info.Controls.Add(ANIMS_LBL_NumOfItems);
            ANIM_GB_Info.Location = new Point(8, 6);
            ANIM_GB_Info.Name = "ANIM_GB_Info";
            ANIM_GB_Info.Size = new Size(200, 100);
            ANIM_GB_Info.TabIndex = 0;
            ANIM_GB_Info.TabStop = false;
            ANIM_GB_Info.Text = "Animlist Information";
            // 
            // ANIMS_LBL_Count
            // 
            ANIMS_LBL_Count.AutoSize = true;
            ANIMS_LBL_Count.Location = new Point(98, 19);
            ANIMS_LBL_Count.Name = "ANIMS_LBL_Count";
            ANIMS_LBL_Count.Size = new Size(13, 15);
            ANIMS_LBL_Count.TabIndex = 1;
            ANIMS_LBL_Count.Text = "0";
            // 
            // ANIMS_LBL_NumOfItems
            // 
            ANIMS_LBL_NumOfItems.AutoSize = true;
            ANIMS_LBL_NumOfItems.Location = new Point(6, 19);
            ANIMS_LBL_NumOfItems.Name = "ANIMS_LBL_NumOfItems";
            ANIMS_LBL_NumOfItems.Size = new Size(86, 15);
            ANIMS_LBL_NumOfItems.TabIndex = 0;
            ANIMS_LBL_NumOfItems.Text = "Items In Scene:";
            // 
            // Page_BNDManager
            // 
            Page_BNDManager.Controls.Add(label11);
            Page_BNDManager.Controls.Add(STRMAN_PB_HowFarThroughTheFile);
            Page_BNDManager.Controls.Add(STRMAN_TB_NewStringValue);
            Page_BNDManager.Controls.Add(label9);
            Page_BNDManager.Controls.Add(STRMAN_LB_CurrentFileStrings);
            Page_BNDManager.Controls.Add(STRMAN_TB_Search);
            Page_BNDManager.Controls.Add(STRMAN_BTN_Search);
            Page_BNDManager.Controls.Add(STRMAN_CheckB_CaseSensitive);
            Page_BNDManager.Controls.Add(groupBox5);
            Page_BNDManager.Controls.Add(groupBox11);
            Page_BNDManager.Location = new Point(4, 24);
            Page_BNDManager.Name = "Page_BNDManager";
            Page_BNDManager.Size = new Size(1256, 629);
            Page_BNDManager.TabIndex = 4;
            Page_BNDManager.Text = "Strings Manager";
            Page_BNDManager.UseVisualStyleBackColor = true;
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(420, 72);
            label11.Name = "label11";
            label11.Size = new Size(125, 15);
            label11.TabIndex = 12;
            label11.Text = "Progress Through File:";
            // 
            // STRMAN_PB_HowFarThroughTheFile
            // 
            STRMAN_PB_HowFarThroughTheFile.Location = new Point(420, 90);
            STRMAN_PB_HowFarThroughTheFile.Name = "STRMAN_PB_HowFarThroughTheFile";
            STRMAN_PB_HowFarThroughTheFile.Size = new Size(312, 23);
            STRMAN_PB_HowFarThroughTheFile.TabIndex = 11;
            // 
            // STRMAN_TB_NewStringValue
            // 
            STRMAN_TB_NewStringValue.Location = new Point(420, 24);
            STRMAN_TB_NewStringValue.Multiline = true;
            STRMAN_TB_NewStringValue.Name = "STRMAN_TB_NewStringValue";
            STRMAN_TB_NewStringValue.Size = new Size(312, 45);
            STRMAN_TB_NewStringValue.TabIndex = 10;
            STRMAN_TB_NewStringValue.Leave += STRMAN_TB_NewStringValue_Leave;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(420, 6);
            label9.Name = "label9";
            label9.Size = new Size(64, 15);
            label9.TabIndex = 9;
            label9.Text = "Edit String:";
            // 
            // STRMAN_LB_CurrentFileStrings
            // 
            STRMAN_LB_CurrentFileStrings.FormattingEnabled = true;
            STRMAN_LB_CurrentFileStrings.HorizontalScrollbar = true;
            STRMAN_LB_CurrentFileStrings.ItemHeight = 15;
            STRMAN_LB_CurrentFileStrings.Items.AddRange(new object[] { "Strings_STEAM_.BND" });
            STRMAN_LB_CurrentFileStrings.Location = new Point(6, 171);
            STRMAN_LB_CurrentFileStrings.Name = "STRMAN_LB_CurrentFileStrings";
            STRMAN_LB_CurrentFileStrings.Size = new Size(1242, 439);
            STRMAN_LB_CurrentFileStrings.TabIndex = 8;
            STRMAN_LB_CurrentFileStrings.SelectedIndexChanged += STRMAN_LB_CurrentFileStrings_SelectedIndexChanged;
            // 
            // STRMAN_TB_Search
            // 
            STRMAN_TB_Search.Location = new Point(6, 112);
            STRMAN_TB_Search.Name = "STRMAN_TB_Search";
            STRMAN_TB_Search.Size = new Size(319, 23);
            STRMAN_TB_Search.TabIndex = 13;
            // 
            // STRMAN_BTN_Search
            // 
            STRMAN_BTN_Search.Location = new Point(6, 140);
            STRMAN_BTN_Search.Name = "STRMAN_BTN_Search";
            STRMAN_BTN_Search.Size = new Size(72, 25);
            STRMAN_BTN_Search.TabIndex = 14;
            STRMAN_BTN_Search.Text = "Search";
            STRMAN_BTN_Search.UseVisualStyleBackColor = true;
            STRMAN_BTN_Search.Click += STRMAN_BTN_Search_Click;
            // 
            // STRMAN_CheckB_CaseSensitive
            // 
            STRMAN_CheckB_CaseSensitive.AutoSize = true;
            STRMAN_CheckB_CaseSensitive.Location = new Point(84, 144);
            STRMAN_CheckB_CaseSensitive.Name = "STRMAN_CheckB_CaseSensitive";
            STRMAN_CheckB_CaseSensitive.Size = new Size(100, 19);
            STRMAN_CheckB_CaseSensitive.TabIndex = 15;
            STRMAN_CheckB_CaseSensitive.Text = "Case Sensitive";
            STRMAN_CheckB_CaseSensitive.UseVisualStyleBackColor = true;
            // 
            // groupBox5
            // 
            groupBox5.Controls.Add(CREDITS_CheckB_IsCredit);
            groupBox5.Controls.Add(STRMAN_BTN_SaveStrings);
            groupBox5.Controls.Add(STRMAN_BTN_OpenStrings);
            groupBox5.Location = new Point(214, 6);
            groupBox5.Name = "groupBox5";
            groupBox5.Size = new Size(200, 100);
            groupBox5.TabIndex = 7;
            groupBox5.TabStop = false;
            groupBox5.Text = "String Tools";
            // 
            // CREDITS_CheckB_IsCredit
            // 
            CREDITS_CheckB_IsCredit.AutoSize = true;
            CREDITS_CheckB_IsCredit.Location = new Point(84, 26);
            CREDITS_CheckB_IsCredit.Name = "CREDITS_CheckB_IsCredit";
            CREDITS_CheckB_IsCredit.Size = new Size(84, 19);
            CREDITS_CheckB_IsCredit.TabIndex = 5;
            CREDITS_CheckB_IsCredit.Text = "Credits File";
            CREDITS_CheckB_IsCredit.UseVisualStyleBackColor = true;
            // 
            // STRMAN_BTN_SaveStrings
            // 
            STRMAN_BTN_SaveStrings.Location = new Point(6, 53);
            STRMAN_BTN_SaveStrings.Name = "STRMAN_BTN_SaveStrings";
            STRMAN_BTN_SaveStrings.Size = new Size(72, 25);
            STRMAN_BTN_SaveStrings.TabIndex = 4;
            STRMAN_BTN_SaveStrings.Text = "Save ";
            STRMAN_BTN_SaveStrings.UseVisualStyleBackColor = true;
            STRMAN_BTN_SaveStrings.Click += STRMAN_BTN_SaveStrings_Click;
            // 
            // STRMAN_BTN_OpenStrings
            // 
            STRMAN_BTN_OpenStrings.Location = new Point(6, 22);
            STRMAN_BTN_OpenStrings.Name = "STRMAN_BTN_OpenStrings";
            STRMAN_BTN_OpenStrings.Size = new Size(72, 25);
            STRMAN_BTN_OpenStrings.TabIndex = 2;
            STRMAN_BTN_OpenStrings.Text = "Open ";
            STRMAN_BTN_OpenStrings.UseVisualStyleBackColor = true;
            STRMAN_BTN_OpenStrings.Click += STRMAN_BTN_OpenStrings_Click;
            // 
            // groupBox11
            // 
            groupBox11.Controls.Add(STRMAN_LBL_LocalCount);
            groupBox11.Controls.Add(STRMAN_LBL_Local);
            groupBox11.Location = new Point(8, 6);
            groupBox11.Name = "groupBox11";
            groupBox11.Size = new Size(200, 100);
            groupBox11.TabIndex = 6;
            groupBox11.TabStop = false;
            groupBox11.Text = "String Information";
            // 
            // STRMAN_LBL_LocalCount
            // 
            STRMAN_LBL_LocalCount.AutoSize = true;
            STRMAN_LBL_LocalCount.Location = new Point(121, 19);
            STRMAN_LBL_LocalCount.Name = "STRMAN_LBL_LocalCount";
            STRMAN_LBL_LocalCount.Size = new Size(13, 15);
            STRMAN_LBL_LocalCount.TabIndex = 1;
            STRMAN_LBL_LocalCount.Text = "0";
            // 
            // STRMAN_LBL_Local
            // 
            STRMAN_LBL_Local.AutoSize = true;
            STRMAN_LBL_Local.Location = new Point(6, 19);
            STRMAN_LBL_Local.Name = "STRMAN_LBL_Local";
            STRMAN_LBL_Local.Size = new Size(109, 15);
            STRMAN_LBL_Local.TabIndex = 0;
            STRMAN_LBL_Local.Text = "Localization Count:";
            // 
            // Page_YDCManager
            // 
            Page_YDCManager.Controls.Add(groupBox14);
            Page_YDCManager.Controls.Add(tabControl1);
            Page_YDCManager.Controls.Add(groupBox12);
            Page_YDCManager.Controls.Add(groupBox13);
            Page_YDCManager.Location = new Point(4, 24);
            Page_YDCManager.Name = "Page_YDCManager";
            Page_YDCManager.Padding = new Padding(3);
            Page_YDCManager.Size = new Size(1256, 629);
            Page_YDCManager.TabIndex = 5;
            Page_YDCManager.Text = "YDC Manager";
            Page_YDCManager.UseVisualStyleBackColor = true;
            // 
            // groupBox14
            // 
            groupBox14.Controls.Add(YDC_CB_UseSimpleEditor);
            groupBox14.Controls.Add(YDC_BTN_ReplaceCard);
            groupBox14.Controls.Add(YDC_BTN_RemoveCard);
            groupBox14.Controls.Add(YDC_BTN_AddCard);
            groupBox14.Location = new Point(420, 3);
            groupBox14.Name = "groupBox14";
            groupBox14.Size = new Size(200, 100);
            groupBox14.TabIndex = 11;
            groupBox14.TabStop = false;
            groupBox14.Text = "Deck Editing Tools";
            // 
            // YDC_CB_UseSimpleEditor
            // 
            YDC_CB_UseSimpleEditor.AutoSize = true;
            YDC_CB_UseSimpleEditor.Location = new Point(84, 25);
            YDC_CB_UseSimpleEditor.Name = "YDC_CB_UseSimpleEditor";
            YDC_CB_UseSimpleEditor.Size = new Size(109, 19);
            YDC_CB_UseSimpleEditor.TabIndex = 6;
            YDC_CB_UseSimpleEditor.Text = "Simple Add Box";
            YDC_CB_UseSimpleEditor.UseVisualStyleBackColor = true;
            // 
            // YDC_BTN_ReplaceCard
            // 
            YDC_BTN_ReplaceCard.Enabled = false;
            YDC_BTN_ReplaceCard.Location = new Point(84, 53);
            YDC_BTN_ReplaceCard.Name = "YDC_BTN_ReplaceCard";
            YDC_BTN_ReplaceCard.Size = new Size(72, 25);
            YDC_BTN_ReplaceCard.TabIndex = 5;
            YDC_BTN_ReplaceCard.Text = "Replace";
            YDC_BTN_ReplaceCard.UseVisualStyleBackColor = true;
            YDC_BTN_ReplaceCard.Click += YDC_BTN_ReplaceCard_Click;
            // 
            // YDC_BTN_RemoveCard
            // 
            YDC_BTN_RemoveCard.Enabled = false;
            YDC_BTN_RemoveCard.Location = new Point(6, 53);
            YDC_BTN_RemoveCard.Name = "YDC_BTN_RemoveCard";
            YDC_BTN_RemoveCard.Size = new Size(72, 25);
            YDC_BTN_RemoveCard.TabIndex = 4;
            YDC_BTN_RemoveCard.Text = "Remove";
            YDC_BTN_RemoveCard.UseVisualStyleBackColor = true;
            YDC_BTN_RemoveCard.Click += YDC_BTN_RemoveCard_Click;
            // 
            // YDC_BTN_AddCard
            // 
            YDC_BTN_AddCard.Enabled = false;
            YDC_BTN_AddCard.Location = new Point(6, 22);
            YDC_BTN_AddCard.Name = "YDC_BTN_AddCard";
            YDC_BTN_AddCard.Size = new Size(72, 25);
            YDC_BTN_AddCard.TabIndex = 2;
            YDC_BTN_AddCard.Text = "Add";
            YDC_BTN_AddCard.UseVisualStyleBackColor = true;
            YDC_BTN_AddCard.Click += YDC_BTN_AddCard_Click;
            // 
            // tabControl1
            // 
            tabControl1.Controls.Add(tabPage1);
            tabControl1.Controls.Add(tabPage2);
            tabControl1.Controls.Add(tabPage3);
            tabControl1.Location = new Point(8, 112);
            tabControl1.Name = "tabControl1";
            tabControl1.SelectedIndex = 0;
            tabControl1.Size = new Size(635, 496);
            tabControl1.TabIndex = 10;
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(YDC_LV_MainDeckCards);
            tabPage1.Location = new Point(4, 24);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new Padding(3);
            tabPage1.Size = new Size(627, 468);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "Main Deck";
            tabPage1.UseVisualStyleBackColor = true;
            // 
            // YDC_LV_MainDeckCards
            // 
            YDC_LV_MainDeckCards.Dock = DockStyle.Fill;
            YDC_LV_MainDeckCards.Location = new Point(3, 3);
            YDC_LV_MainDeckCards.Name = "YDC_LV_MainDeckCards";
            YDC_LV_MainDeckCards.Size = new Size(621, 462);
            YDC_LV_MainDeckCards.TabIndex = 0;
            YDC_LV_MainDeckCards.UseCompatibleStateImageBehavior = false;
            YDC_LV_MainDeckCards.View = View.List;
            // 
            // tabPage2
            // 
            tabPage2.Location = new Point(4, 24);
            tabPage2.Name = "tabPage2";
            tabPage2.Padding = new Padding(3);
            tabPage2.Size = new Size(627, 468);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "Side Deck";
            tabPage2.UseVisualStyleBackColor = true;
            // 
            // tabPage3
            // 
            tabPage3.Location = new Point(4, 24);
            tabPage3.Name = "tabPage3";
            tabPage3.Size = new Size(627, 468);
            tabPage3.TabIndex = 2;
            tabPage3.Text = "Extra Deck";
            tabPage3.UseVisualStyleBackColor = true;
            // 
            // groupBox12
            // 
            groupBox12.Controls.Add(YDC_CHKBOX_UseCardID);
            groupBox12.Controls.Add(YDC_BTN_OpenSaveFile);
            groupBox12.Controls.Add(YDC_CHKBOX_LoadPictures);
            groupBox12.Controls.Add(YDC_BTN_SaveDeck);
            groupBox12.Controls.Add(YDC_BTN_OpenDeck);
            groupBox12.Location = new Point(214, 6);
            groupBox12.Name = "groupBox12";
            groupBox12.Size = new Size(200, 100);
            groupBox12.TabIndex = 9;
            groupBox12.TabStop = false;
            groupBox12.Text = "Deck Tools";
            // 
            // YDC_CHKBOX_UseCardID
            // 
            YDC_CHKBOX_UseCardID.AutoSize = true;
            YDC_CHKBOX_UseCardID.Checked = true;
            YDC_CHKBOX_UseCardID.CheckState = CheckState.Checked;
            YDC_CHKBOX_UseCardID.Location = new Point(84, 72);
            YDC_CHKBOX_UseCardID.Name = "YDC_CHKBOX_UseCardID";
            YDC_CHKBOX_UseCardID.Size = new Size(92, 19);
            YDC_CHKBOX_UseCardID.TabIndex = 7;
            YDC_CHKBOX_UseCardID.Text = "Use Card IDs";
            YDC_CHKBOX_UseCardID.UseVisualStyleBackColor = true;
            YDC_CHKBOX_UseCardID.CheckedChanged += YDC_CHKBOX_UseCardID_CheckedChanged;
            // 
            // YDC_BTN_OpenSaveFile
            // 
            YDC_BTN_OpenSaveFile.Location = new Point(84, 22);
            YDC_BTN_OpenSaveFile.Name = "YDC_BTN_OpenSaveFile";
            YDC_BTN_OpenSaveFile.Size = new Size(107, 25);
            YDC_BTN_OpenSaveFile.TabIndex = 6;
            YDC_BTN_OpenSaveFile.Text = "Extract Save";
            YDC_BTN_OpenSaveFile.UseVisualStyleBackColor = true;
            // 
            // YDC_CHKBOX_LoadPictures
            // 
            YDC_CHKBOX_LoadPictures.AutoSize = true;
            YDC_CHKBOX_LoadPictures.Location = new Point(84, 53);
            YDC_CHKBOX_LoadPictures.Name = "YDC_CHKBOX_LoadPictures";
            YDC_CHKBOX_LoadPictures.Size = new Size(97, 19);
            YDC_CHKBOX_LoadPictures.TabIndex = 5;
            YDC_CHKBOX_LoadPictures.Text = "Load Pictures";
            YDC_CHKBOX_LoadPictures.UseVisualStyleBackColor = true;
            YDC_CHKBOX_LoadPictures.CheckedChanged += YDC_CHKBOX_LoadPictures_CheckedChanged;
            // 
            // YDC_BTN_SaveDeck
            // 
            YDC_BTN_SaveDeck.Enabled = false;
            YDC_BTN_SaveDeck.Location = new Point(6, 53);
            YDC_BTN_SaveDeck.Name = "YDC_BTN_SaveDeck";
            YDC_BTN_SaveDeck.Size = new Size(72, 25);
            YDC_BTN_SaveDeck.TabIndex = 4;
            YDC_BTN_SaveDeck.Text = "Save ";
            YDC_BTN_SaveDeck.UseVisualStyleBackColor = true;
            // 
            // YDC_BTN_OpenDeck
            // 
            YDC_BTN_OpenDeck.Location = new Point(6, 22);
            YDC_BTN_OpenDeck.Name = "YDC_BTN_OpenDeck";
            YDC_BTN_OpenDeck.Size = new Size(72, 25);
            YDC_BTN_OpenDeck.TabIndex = 2;
            YDC_BTN_OpenDeck.Text = "Open ";
            YDC_BTN_OpenDeck.UseVisualStyleBackColor = true;
            YDC_BTN_OpenDeck.Click += YDC_BTN_OpenDeck_Click;
            // 
            // groupBox13
            // 
            groupBox13.Controls.Add(YDC_TB_DeckName);
            groupBox13.Controls.Add(label13);
            groupBox13.Controls.Add(YDC_LBL_NumOfCardsInExtra);
            groupBox13.Controls.Add(label18);
            groupBox13.Controls.Add(YDC_LBL_NumOfCardsInSide);
            groupBox13.Controls.Add(label16);
            groupBox13.Controls.Add(YDC_LBL_NumOfCardInMain);
            groupBox13.Controls.Add(label14);
            groupBox13.Location = new Point(8, 6);
            groupBox13.Name = "groupBox13";
            groupBox13.Size = new Size(200, 100);
            groupBox13.TabIndex = 8;
            groupBox13.TabStop = false;
            groupBox13.Text = "Deck Information";
            // 
            // YDC_TB_DeckName
            // 
            YDC_TB_DeckName.Font = new Font("Segoe UI", 8F);
            YDC_TB_DeckName.Location = new Point(84, 14);
            YDC_TB_DeckName.Name = "YDC_TB_DeckName";
            YDC_TB_DeckName.Size = new Size(110, 22);
            YDC_TB_DeckName.TabIndex = 7;
            // 
            // label13
            // 
            label13.AutoSize = true;
            label13.Location = new Point(7, 19);
            label13.Name = "label13";
            label13.Size = new Size(71, 15);
            label13.TabIndex = 6;
            label13.Text = "Deck Name:";
            // 
            // YDC_LBL_NumOfCardsInExtra
            // 
            YDC_LBL_NumOfCardsInExtra.AutoSize = true;
            YDC_LBL_NumOfCardsInExtra.Location = new Point(78, 82);
            YDC_LBL_NumOfCardsInExtra.Name = "YDC_LBL_NumOfCardsInExtra";
            YDC_LBL_NumOfCardsInExtra.Size = new Size(13, 15);
            YDC_LBL_NumOfCardsInExtra.TabIndex = 5;
            YDC_LBL_NumOfCardsInExtra.Text = "0";
            // 
            // label18
            // 
            label18.AutoSize = true;
            label18.Location = new Point(7, 82);
            label18.Name = "label18";
            label18.Size = new Size(64, 15);
            label18.TabIndex = 4;
            label18.Text = "Extra Deck:";
            // 
            // YDC_LBL_NumOfCardsInSide
            // 
            YDC_LBL_NumOfCardsInSide.AutoSize = true;
            YDC_LBL_NumOfCardsInSide.Location = new Point(79, 61);
            YDC_LBL_NumOfCardsInSide.Name = "YDC_LBL_NumOfCardsInSide";
            YDC_LBL_NumOfCardsInSide.Size = new Size(13, 15);
            YDC_LBL_NumOfCardsInSide.TabIndex = 3;
            YDC_LBL_NumOfCardsInSide.Text = "0";
            // 
            // label16
            // 
            label16.AutoSize = true;
            label16.Location = new Point(7, 61);
            label16.Name = "label16";
            label16.Size = new Size(61, 15);
            label16.TabIndex = 2;
            label16.Text = "Side Deck:";
            // 
            // YDC_LBL_NumOfCardInMain
            // 
            YDC_LBL_NumOfCardInMain.AutoSize = true;
            YDC_LBL_NumOfCardInMain.Location = new Point(79, 40);
            YDC_LBL_NumOfCardInMain.Name = "YDC_LBL_NumOfCardInMain";
            YDC_LBL_NumOfCardInMain.Size = new Size(13, 15);
            YDC_LBL_NumOfCardInMain.TabIndex = 1;
            YDC_LBL_NumOfCardInMain.Text = "0";
            // 
            // label14
            // 
            label14.AutoSize = true;
            label14.Location = new Point(7, 40);
            label14.Name = "label14";
            label14.Size = new Size(66, 15);
            label14.TabIndex = 0;
            label14.Text = "Main Deck:";
            // 
            // Page_PDLimitsManager
            // 
            Page_PDLimitsManager.Controls.Add(tabControl2);
            Page_PDLimitsManager.Controls.Add(groupBox15);
            Page_PDLimitsManager.Controls.Add(groupBox16);
            Page_PDLimitsManager.Controls.Add(groupBox17);
            Page_PDLimitsManager.Location = new Point(4, 24);
            Page_PDLimitsManager.Name = "Page_PDLimitsManager";
            Page_PDLimitsManager.Padding = new Padding(3);
            Page_PDLimitsManager.Size = new Size(1256, 629);
            Page_PDLimitsManager.TabIndex = 6;
            Page_PDLimitsManager.Text = "Status Editor";
            Page_PDLimitsManager.UseVisualStyleBackColor = true;
            // 
            // tabControl2
            // 
            tabControl2.Controls.Add(PDL_ForbiddenCards);
            tabControl2.Controls.Add(PDL_SemiLimitedCards);
            tabControl2.Controls.Add(PDL_LimitedCards);
            tabControl2.Location = new Point(8, 112);
            tabControl2.Name = "tabControl2";
            tabControl2.SelectedIndex = 0;
            tabControl2.Size = new Size(1240, 496);
            tabControl2.TabIndex = 15;
            // 
            // PDL_ForbiddenCards
            // 
            PDL_ForbiddenCards.Controls.Add(PDL_LV_ForbiddenCards);
            PDL_ForbiddenCards.Location = new Point(4, 24);
            PDL_ForbiddenCards.Name = "PDL_ForbiddenCards";
            PDL_ForbiddenCards.Padding = new Padding(3);
            PDL_ForbiddenCards.Size = new Size(1232, 468);
            PDL_ForbiddenCards.TabIndex = 0;
            PDL_ForbiddenCards.Text = "Forbidden";
            PDL_ForbiddenCards.UseVisualStyleBackColor = true;
            // 
            // PDL_LV_ForbiddenCards
            // 
            PDL_LV_ForbiddenCards.Dock = DockStyle.Fill;
            PDL_LV_ForbiddenCards.Location = new Point(3, 3);
            PDL_LV_ForbiddenCards.Name = "PDL_LV_ForbiddenCards";
            PDL_LV_ForbiddenCards.Size = new Size(1226, 462);
            PDL_LV_ForbiddenCards.TabIndex = 0;
            PDL_LV_ForbiddenCards.UseCompatibleStateImageBehavior = false;
            PDL_LV_ForbiddenCards.View = View.List;
            PDL_LV_ForbiddenCards.SelectedIndexChanged += PDL_LV_ItemSelectionChanged;
            // 
            // PDL_SemiLimitedCards
            // 
            PDL_SemiLimitedCards.Controls.Add(PDL_LV_SemiLimitedCards);
            PDL_SemiLimitedCards.Location = new Point(4, 24);
            PDL_SemiLimitedCards.Name = "PDL_SemiLimitedCards";
            PDL_SemiLimitedCards.Size = new Size(1232, 468);
            PDL_SemiLimitedCards.TabIndex = 2;
            PDL_SemiLimitedCards.Text = "Semi-Limited";
            PDL_SemiLimitedCards.UseVisualStyleBackColor = true;
            // 
            // PDL_LV_SemiLimitedCards
            // 
            PDL_LV_SemiLimitedCards.Dock = DockStyle.Fill;
            PDL_LV_SemiLimitedCards.Location = new Point(0, 0);
            PDL_LV_SemiLimitedCards.Name = "PDL_LV_SemiLimitedCards";
            PDL_LV_SemiLimitedCards.Size = new Size(1232, 468);
            PDL_LV_SemiLimitedCards.TabIndex = 1;
            PDL_LV_SemiLimitedCards.UseCompatibleStateImageBehavior = false;
            PDL_LV_SemiLimitedCards.View = View.List;
            PDL_LV_SemiLimitedCards.SelectedIndexChanged += PDL_LV_ItemSelectionChanged;
            // 
            // PDL_LimitedCards
            // 
            PDL_LimitedCards.Controls.Add(PDL_LV_LimitedCards);
            PDL_LimitedCards.Location = new Point(4, 24);
            PDL_LimitedCards.Name = "PDL_LimitedCards";
            PDL_LimitedCards.Padding = new Padding(3);
            PDL_LimitedCards.Size = new Size(1232, 468);
            PDL_LimitedCards.TabIndex = 1;
            PDL_LimitedCards.Text = "Limited";
            PDL_LimitedCards.UseVisualStyleBackColor = true;
            // 
            // PDL_LV_LimitedCards
            // 
            PDL_LV_LimitedCards.Dock = DockStyle.Fill;
            PDL_LV_LimitedCards.Location = new Point(3, 3);
            PDL_LV_LimitedCards.Name = "PDL_LV_LimitedCards";
            PDL_LV_LimitedCards.Size = new Size(1226, 462);
            PDL_LV_LimitedCards.TabIndex = 1;
            PDL_LV_LimitedCards.UseCompatibleStateImageBehavior = false;
            PDL_LV_LimitedCards.View = View.List;
            PDL_LV_LimitedCards.SelectedIndexChanged += PDL_LV_ItemSelectionChanged;
            // 
            // groupBox15
            // 
            groupBox15.Controls.Add(PDL_CB_IsUsingSimpleAddBox);
            groupBox15.Controls.Add(button1);
            groupBox15.Controls.Add(PDL_BTN_RemoveCardFromList);
            groupBox15.Controls.Add(PDL_BTN_AddCardToList);
            groupBox15.Location = new Point(420, 3);
            groupBox15.Name = "groupBox15";
            groupBox15.Size = new Size(200, 100);
            groupBox15.TabIndex = 14;
            groupBox15.TabStop = false;
            groupBox15.Text = "Limiter Tools";
            // 
            // PDL_CB_IsUsingSimpleAddBox
            // 
            PDL_CB_IsUsingSimpleAddBox.AutoSize = true;
            PDL_CB_IsUsingSimpleAddBox.Checked = true;
            PDL_CB_IsUsingSimpleAddBox.CheckState = CheckState.Checked;
            PDL_CB_IsUsingSimpleAddBox.Location = new Point(84, 25);
            PDL_CB_IsUsingSimpleAddBox.Name = "PDL_CB_IsUsingSimpleAddBox";
            PDL_CB_IsUsingSimpleAddBox.Size = new Size(109, 19);
            PDL_CB_IsUsingSimpleAddBox.TabIndex = 6;
            PDL_CB_IsUsingSimpleAddBox.Text = "Simple Add Box";
            PDL_CB_IsUsingSimpleAddBox.UseVisualStyleBackColor = true;
            // 
            // button1
            // 
            button1.Enabled = false;
            button1.Location = new Point(84, 53);
            button1.Name = "button1";
            button1.Size = new Size(72, 25);
            button1.TabIndex = 5;
            button1.Text = "Replace";
            button1.UseVisualStyleBackColor = true;
            // 
            // PDL_BTN_RemoveCardFromList
            // 
            PDL_BTN_RemoveCardFromList.Enabled = false;
            PDL_BTN_RemoveCardFromList.Location = new Point(6, 53);
            PDL_BTN_RemoveCardFromList.Name = "PDL_BTN_RemoveCardFromList";
            PDL_BTN_RemoveCardFromList.Size = new Size(72, 25);
            PDL_BTN_RemoveCardFromList.TabIndex = 4;
            PDL_BTN_RemoveCardFromList.Text = "Remove";
            PDL_BTN_RemoveCardFromList.UseVisualStyleBackColor = true;
            PDL_BTN_RemoveCardFromList.Click += PDL_BTN_RemoveCardFromList_Click;
            // 
            // PDL_BTN_AddCardToList
            // 
            PDL_BTN_AddCardToList.Enabled = false;
            PDL_BTN_AddCardToList.Location = new Point(6, 22);
            PDL_BTN_AddCardToList.Name = "PDL_BTN_AddCardToList";
            PDL_BTN_AddCardToList.Size = new Size(72, 25);
            PDL_BTN_AddCardToList.TabIndex = 2;
            PDL_BTN_AddCardToList.Text = "Add";
            PDL_BTN_AddCardToList.UseVisualStyleBackColor = true;
            PDL_BTN_AddCardToList.Click += PDL_BTN_AddCardToList_Click;
            // 
            // groupBox16
            // 
            groupBox16.Controls.Add(PDL_CB_ManualOpenWhenPathSet);
            groupBox16.Controls.Add(PDL_CB_UseCardID);
            groupBox16.Controls.Add(PDL_CB_LoadImages);
            groupBox16.Controls.Add(PDL_BTN_SavePDL);
            groupBox16.Controls.Add(PDL_BTN_OpenPDL);
            groupBox16.Location = new Point(214, 6);
            groupBox16.Name = "groupBox16";
            groupBox16.Size = new Size(200, 100);
            groupBox16.TabIndex = 13;
            groupBox16.TabStop = false;
            groupBox16.Text = "Limiter File Handler";
            // 
            // PDL_CB_UseCardID
            // 
            PDL_CB_UseCardID.AutoSize = true;
            PDL_CB_UseCardID.Checked = true;
            PDL_CB_UseCardID.CheckState = CheckState.Checked;
            PDL_CB_UseCardID.Location = new Point(84, 53);
            PDL_CB_UseCardID.Name = "PDL_CB_UseCardID";
            PDL_CB_UseCardID.Size = new Size(92, 19);
            PDL_CB_UseCardID.TabIndex = 7;
            PDL_CB_UseCardID.Text = "Use Card IDs";
            PDL_CB_UseCardID.UseVisualStyleBackColor = true;
            PDL_CB_UseCardID.CheckedChanged += PDL_CB_UseCardID_CheckedChanged;
            // 
            // PDL_CB_LoadImages
            // 
            PDL_CB_LoadImages.AutoSize = true;
            PDL_CB_LoadImages.Location = new Point(84, 23);
            PDL_CB_LoadImages.Name = "PDL_CB_LoadImages";
            PDL_CB_LoadImages.Size = new Size(97, 19);
            PDL_CB_LoadImages.TabIndex = 5;
            PDL_CB_LoadImages.Text = "Load Pictures";
            PDL_CB_LoadImages.UseVisualStyleBackColor = true;
            PDL_CB_LoadImages.CheckedChanged += PDL_CB_LoadImages_CheckedChanged;
            // 
            // PDL_BTN_SavePDL
            // 
            PDL_BTN_SavePDL.Enabled = false;
            PDL_BTN_SavePDL.Location = new Point(6, 53);
            PDL_BTN_SavePDL.Name = "PDL_BTN_SavePDL";
            PDL_BTN_SavePDL.Size = new Size(72, 25);
            PDL_BTN_SavePDL.TabIndex = 4;
            PDL_BTN_SavePDL.Text = "Save ";
            PDL_BTN_SavePDL.UseVisualStyleBackColor = true;
            PDL_BTN_SavePDL.Click += PDL_BTN_SavePDL_Click;
            // 
            // PDL_BTN_OpenPDL
            // 
            PDL_BTN_OpenPDL.Location = new Point(6, 22);
            PDL_BTN_OpenPDL.Name = "PDL_BTN_OpenPDL";
            PDL_BTN_OpenPDL.Size = new Size(72, 25);
            PDL_BTN_OpenPDL.TabIndex = 2;
            PDL_BTN_OpenPDL.Text = "Open ";
            PDL_BTN_OpenPDL.UseVisualStyleBackColor = true;
            PDL_BTN_OpenPDL.Click += PDL_BTN_OpenPDL_Click;
            // 
            // groupBox17
            // 
            groupBox17.Controls.Add(PDL_LBL_NumOfSemiLimited);
            groupBox17.Controls.Add(label17);
            groupBox17.Controls.Add(PDL_LBL_NumOfLimited);
            groupBox17.Controls.Add(label19);
            groupBox17.Controls.Add(PDL_LBL_NumOfForbidden);
            groupBox17.Controls.Add(label21);
            groupBox17.Controls.Add(label23);
            groupBox17.Location = new Point(8, 6);
            groupBox17.Name = "groupBox17";
            groupBox17.Size = new Size(200, 100);
            groupBox17.TabIndex = 12;
            groupBox17.TabStop = false;
            groupBox17.Text = "Limited/Forbidden Information";
            // 
            // PDL_LBL_NumOfSemiLimited
            // 
            PDL_LBL_NumOfSemiLimited.AutoSize = true;
            PDL_LBL_NumOfSemiLimited.Location = new Point(92, 81);
            PDL_LBL_NumOfSemiLimited.Name = "PDL_LBL_NumOfSemiLimited";
            PDL_LBL_NumOfSemiLimited.Size = new Size(13, 15);
            PDL_LBL_NumOfSemiLimited.TabIndex = 7;
            PDL_LBL_NumOfSemiLimited.Text = "0";
            // 
            // label17
            // 
            label17.AutoSize = true;
            label17.Location = new Point(7, 81);
            label17.Name = "label17";
            label17.Size = new Size(79, 15);
            label17.TabIndex = 6;
            label17.Text = "Semi Limited:";
            // 
            // PDL_LBL_NumOfLimited
            // 
            PDL_LBL_NumOfLimited.AutoSize = true;
            PDL_LBL_NumOfLimited.Location = new Point(74, 50);
            PDL_LBL_NumOfLimited.Name = "PDL_LBL_NumOfLimited";
            PDL_LBL_NumOfLimited.Size = new Size(13, 15);
            PDL_LBL_NumOfLimited.TabIndex = 5;
            PDL_LBL_NumOfLimited.Text = "0";
            // 
            // label19
            // 
            label19.AutoSize = true;
            label19.Location = new Point(7, 50);
            label19.Name = "label19";
            label19.Size = new Size(50, 15);
            label19.TabIndex = 4;
            label19.Text = "Limited:";
            // 
            // PDL_LBL_NumOfForbidden
            // 
            PDL_LBL_NumOfForbidden.AutoSize = true;
            PDL_LBL_NumOfForbidden.Location = new Point(74, 19);
            PDL_LBL_NumOfForbidden.Name = "PDL_LBL_NumOfForbidden";
            PDL_LBL_NumOfForbidden.Size = new Size(13, 15);
            PDL_LBL_NumOfForbidden.TabIndex = 3;
            PDL_LBL_NumOfForbidden.Text = "0";
            // 
            // label21
            // 
            label21.AutoSize = true;
            label21.Location = new Point(7, 19);
            label21.Name = "label21";
            label21.Size = new Size(64, 15);
            label21.TabIndex = 2;
            label21.Text = "Forbidden:";
            // 
            // label23
            // 
            label23.AutoSize = true;
            label23.Location = new Point(7, 40);
            label23.Name = "label23";
            label23.Size = new Size(0, 15);
            label23.TabIndex = 0;
            // 
            // Page_CardShopManager
            // 
            Page_CardShopManager.Controls.Add(groupBox20);
            Page_CardShopManager.Controls.Add(groupBox19);
            Page_CardShopManager.Location = new Point(4, 24);
            Page_CardShopManager.Name = "Page_CardShopManager";
            Page_CardShopManager.Padding = new Padding(3);
            Page_CardShopManager.Size = new Size(1256, 629);
            Page_CardShopManager.TabIndex = 7;
            Page_CardShopManager.Text = "Card Shop Manager";
            Page_CardShopManager.UseVisualStyleBackColor = true;
            // 
            // groupBox20
            // 
            groupBox20.Controls.Add(button2);
            groupBox20.Controls.Add(PACKDEF_BTN_OpenPackDEF);
            groupBox20.Location = new Point(214, 6);
            groupBox20.Name = "groupBox20";
            groupBox20.Size = new Size(200, 100);
            groupBox20.TabIndex = 14;
            groupBox20.TabStop = false;
            groupBox20.Text = "Pack Def File Handler";
            // 
            // button2
            // 
            button2.Enabled = false;
            button2.Location = new Point(122, 22);
            button2.Name = "button2";
            button2.Size = new Size(72, 25);
            button2.TabIndex = 4;
            button2.Text = "Save ";
            button2.UseVisualStyleBackColor = true;
            // 
            // PACKDEF_BTN_OpenPackDEF
            // 
            PACKDEF_BTN_OpenPackDEF.Location = new Point(6, 22);
            PACKDEF_BTN_OpenPackDEF.Name = "PACKDEF_BTN_OpenPackDEF";
            PACKDEF_BTN_OpenPackDEF.Size = new Size(72, 25);
            PACKDEF_BTN_OpenPackDEF.TabIndex = 2;
            PACKDEF_BTN_OpenPackDEF.Text = "Open ";
            PACKDEF_BTN_OpenPackDEF.UseVisualStyleBackColor = true;
            PACKDEF_BTN_OpenPackDEF.Click += PACKDEF_BTN_OpenPackDEF_Click;
            // 
            // groupBox19
            // 
            groupBox19.Controls.Add(label24);
            groupBox19.Controls.Add(label22);
            groupBox19.Controls.Add(label15);
            groupBox19.Controls.Add(label20);
            groupBox19.Controls.Add(label27);
            groupBox19.Location = new Point(8, 6);
            groupBox19.Name = "groupBox19";
            groupBox19.Size = new Size(200, 100);
            groupBox19.TabIndex = 13;
            groupBox19.TabStop = false;
            groupBox19.Text = "Series Information";
            // 
            // label24
            // 
            label24.AutoSize = true;
            label24.Location = new Point(133, 40);
            label24.Name = "label24";
            label24.Size = new Size(13, 15);
            label24.TabIndex = 9;
            label24.Text = "0";
            // 
            // label22
            // 
            label22.AutoSize = true;
            label22.Location = new Point(7, 40);
            label22.Name = "label22";
            label22.Size = new Size(103, 15);
            label22.TabIndex = 8;
            label22.Text = "Number of Stores:";
            // 
            // label15
            // 
            label15.AutoSize = true;
            label15.Location = new Point(133, 19);
            label15.Name = "label15";
            label15.Size = new Size(13, 15);
            label15.TabIndex = 7;
            label15.Text = "0";
            // 
            // label20
            // 
            label20.AutoSize = true;
            label20.Location = new Point(6, 19);
            label20.Name = "label20";
            label20.Size = new Size(121, 15);
            label20.TabIndex = 6;
            label20.Text = "Number of Chapters: ";
            // 
            // label27
            // 
            label27.AutoSize = true;
            label27.Location = new Point(7, 40);
            label27.Name = "label27";
            label27.Size = new Size(0, 15);
            label27.TabIndex = 0;
            // 
            // MenuBar
            // 
            MenuBar.Items.AddRange(new ToolStripItem[] { toolsToolStripMenuItem });
            MenuBar.Location = new Point(0, 0);
            MenuBar.Name = "MenuBar";
            MenuBar.Size = new Size(1264, 24);
            MenuBar.TabIndex = 1;
            MenuBar.Text = "menuStrip1";
            // 
            // toolsToolStripMenuItem
            // 
            toolsToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { WOLFUI_TOOLITEM_OpenConfigEditor, toolStripSeparator1, loadGameToolStripMenuItem, toolStripSeparator2, WOLFUI_TOOLITEM_Extract, WOLFUI_TOOLITEM_Pack, toolStripSeparator3, WOLFUI_TOOLITEM_SetPath, Tools_Verify, toolStripSeparator4, languageToolStripMenuItem });
            toolsToolStripMenuItem.Name = "toolsToolStripMenuItem";
            toolsToolStripMenuItem.Size = new Size(47, 20);
            toolsToolStripMenuItem.Text = "&Tools";
            // 
            // WOLFUI_TOOLITEM_OpenConfigEditor
            // 
            WOLFUI_TOOLITEM_OpenConfigEditor.Name = "WOLFUI_TOOLITEM_OpenConfigEditor";
            WOLFUI_TOOLITEM_OpenConfigEditor.Size = new Size(183, 22);
            WOLFUI_TOOLITEM_OpenConfigEditor.Text = "Config Editor";
            WOLFUI_TOOLITEM_OpenConfigEditor.Click += WOLFUI_TOOLITEM_OpenConfigEditor_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(180, 6);
            // 
            // loadGameToolStripMenuItem
            // 
            loadGameToolStripMenuItem.Name = "loadGameToolStripMenuItem";
            loadGameToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.L;
            loadGameToolStripMenuItem.Size = new Size(183, 22);
            loadGameToolStripMenuItem.Text = "&Load Game";
            loadGameToolStripMenuItem.Click += WOLFUI_TOOLITEM_LoadGame_Click;
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new Size(180, 6);
            // 
            // WOLFUI_TOOLITEM_Extract
            // 
            WOLFUI_TOOLITEM_Extract.Name = "WOLFUI_TOOLITEM_Extract";
            WOLFUI_TOOLITEM_Extract.ShortcutKeys = Keys.Control | Keys.E;
            WOLFUI_TOOLITEM_Extract.Size = new Size(183, 22);
            WOLFUI_TOOLITEM_Extract.Text = "&Extract Game";
            WOLFUI_TOOLITEM_Extract.Click += WOLFUI_TOOLITEM_Extract_Click;
            // 
            // WOLFUI_TOOLITEM_Pack
            // 
            WOLFUI_TOOLITEM_Pack.Name = "WOLFUI_TOOLITEM_Pack";
            WOLFUI_TOOLITEM_Pack.ShortcutKeys = Keys.Control | Keys.P;
            WOLFUI_TOOLITEM_Pack.Size = new Size(183, 22);
            WOLFUI_TOOLITEM_Pack.Text = "&Pack Game";
            WOLFUI_TOOLITEM_Pack.Click += WOLFUI_TOOLITEM_Pack_Click;
            // 
            // toolStripSeparator3
            // 
            toolStripSeparator3.Name = "toolStripSeparator3";
            toolStripSeparator3.Size = new Size(180, 6);
            // 
            // WOLFUI_TOOLITEM_SetPath
            // 
            WOLFUI_TOOLITEM_SetPath.Name = "WOLFUI_TOOLITEM_SetPath";
            WOLFUI_TOOLITEM_SetPath.Size = new Size(183, 22);
            WOLFUI_TOOLITEM_SetPath.Text = "Set Game Path";
            WOLFUI_TOOLITEM_SetPath.Click += WOLFUI_TOOLITEM_SetPath_Click;
            // 
            // Tools_Verify
            // 
            Tools_Verify.Name = "Tools_Verify";
            Tools_Verify.Size = new Size(183, 22);
            Tools_Verify.Text = "&Verify Extracted Files";
            Tools_Verify.Click += WOLFUI_TOOLITEM_VerifyFiles_Click;
            // 
            // toolStripSeparator4
            // 
            toolStripSeparator4.Name = "toolStripSeparator4";
            toolStripSeparator4.Size = new Size(180, 6);
            // 
            // languageToolStripMenuItem
            // 
            languageToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { Language_english, Language_french, Language_german, Language_italian, Language_japanese, Language_russian, Language_spanish });
            languageToolStripMenuItem.Name = "languageToolStripMenuItem";
            languageToolStripMenuItem.Size = new Size(183, 22);
            languageToolStripMenuItem.Text = "Language";
            // 
            // Language_english
            // 
            Language_english.Checked = true;
            Language_english.CheckState = CheckState.Checked;
            Language_english.Name = "Language_english";
            Language_english.Size = new Size(117, 22);
            Language_english.Text = "English";
            Language_english.Click += Language_english_Click;
            // 
            // Language_french
            // 
            Language_french.Name = "Language_french";
            Language_french.Size = new Size(117, 22);
            Language_french.Text = "Français";
            Language_french.Click += Language_french_Click;
            // 
            // Language_german
            // 
            Language_german.Name = "Language_german";
            Language_german.Size = new Size(117, 22);
            Language_german.Text = "Deutsch";
            Language_german.Click += Language_german_Click;
            // 
            // Language_italian
            // 
            Language_italian.Name = "Language_italian";
            Language_italian.Size = new Size(117, 22);
            Language_italian.Text = "Italiano";
            Language_italian.Click += Language_italian_Click;
            // 
            // Language_japanese
            // 
            Language_japanese.Name = "Language_japanese";
            Language_japanese.Size = new Size(117, 22);
            Language_japanese.Text = "Nihon";
            Language_japanese.Click += Language_japanese_Click;
            // 
            // Language_russian
            // 
            Language_russian.Name = "Language_russian";
            Language_russian.Size = new Size(117, 22);
            Language_russian.Text = "Russkiy";
            Language_russian.Click += Language_russian_Click;
            // 
            // Language_spanish
            // 
            Language_spanish.Name = "Language_spanish";
            Language_spanish.Size = new Size(117, 22);
            Language_spanish.Text = "Español";
            Language_spanish.Click += Language_spanish_Click;
            // 
            // WolfUI
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1264, 681);
            Controls.Add(WolfX_TabManager);
            Controls.Add(MenuBar);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MainMenuStrip = MenuBar;
            MaximizeBox = false;
            Name = "WolfUI";
            Text = "WolfX";
            WolfX_TabManager.ResumeLayout(false);
            Page_CardManager.ResumeLayout(false);
            groupBox18.ResumeLayout(false);
            groupBox18.PerformLayout();
            groupBox6.ResumeLayout(false);
            groupBox6.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)CARDS_Nud_CardLevel).EndInit();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)CARDS_PB_CardPicture).EndInit();
            Page_ZibManager.ResumeLayout(false);
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            Page_DFYMOOManager.ResumeLayout(false);
            groupBox7.ResumeLayout(false);
            groupBox7.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)DFY_NUD_W).EndInit();
            ((System.ComponentModel.ISupportInitialize)DFY_NUD_Y).EndInit();
            ((System.ComponentModel.ISupportInitialize)DFY_NUD_H).EndInit();
            ((System.ComponentModel.ISupportInitialize)DFY_NUD_X).EndInit();
            groupBox8.ResumeLayout(false);
            groupBox9.ResumeLayout(false);
            groupBox9.PerformLayout();
            Page_ANIMLISTManager.ResumeLayout(false);
            groupBox10.ResumeLayout(false);
            ANIM_GB_Info.ResumeLayout(false);
            ANIM_GB_Info.PerformLayout();
            Page_BNDManager.ResumeLayout(false);
            Page_BNDManager.PerformLayout();
            groupBox5.ResumeLayout(false);
            groupBox5.PerformLayout();
            groupBox11.ResumeLayout(false);
            groupBox11.PerformLayout();
            Page_YDCManager.ResumeLayout(false);
            groupBox14.ResumeLayout(false);
            groupBox14.PerformLayout();
            tabControl1.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            groupBox12.ResumeLayout(false);
            groupBox12.PerformLayout();
            groupBox13.ResumeLayout(false);
            groupBox13.PerformLayout();
            Page_PDLimitsManager.ResumeLayout(false);
            tabControl2.ResumeLayout(false);
            PDL_ForbiddenCards.ResumeLayout(false);
            PDL_SemiLimitedCards.ResumeLayout(false);
            PDL_LimitedCards.ResumeLayout(false);
            groupBox15.ResumeLayout(false);
            groupBox15.PerformLayout();
            groupBox16.ResumeLayout(false);
            groupBox16.PerformLayout();
            groupBox17.ResumeLayout(false);
            groupBox17.PerformLayout();
            Page_CardShopManager.ResumeLayout(false);
            groupBox20.ResumeLayout(false);
            groupBox19.ResumeLayout(false);
            groupBox19.PerformLayout();
            MenuBar.ResumeLayout(false);
            MenuBar.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion


        private TabControl WolfX_TabManager;
        private TabPage Page_CardManager;
        private TabPage Page_ZibManager;
        private MenuStrip MenuBar;
        private GroupBox groupBox1;
        private Label label2;
        private Label label1;
        private GroupBox groupBox2;
        private Label CB_CardLevel;
        private Label label5;
        private Label label4;
        private Label label3;
        private GroupBox groupBox4;
        private Button ARCHIVE_BTN_ExtractZIB;
        public ListView ARCHIVE_LV_ArchiveItems;
        private GroupBox groupBox3;
        private Label lbl_NumberOfItemsPrompt;
        private Label lbl_FileSizePrompt;
        private Label lbl_NamePrompt;
        public static WolfUI Form;
        private ToolStripMenuItem toolsToolStripMenuItem;
        private ToolStripMenuItem Tools_Verify;
        public CheckBox ARCHIVE_CB_ShowPreviewImage;
        public Label ARCHIVE_LBL_ArchiveSize;
        public Label ARCHIVE_LBL_ArchiveName;
        public Label ARCHIVE_LBL_ArchiveItems;
        private ToolStripSeparator toolStripSeparator2;
        private ToolStripMenuItem languageToolStripMenuItem;
        private ToolStripMenuItem Language_english;
        private ToolStripMenuItem Language_french;
        private ToolStripMenuItem Language_german;
        private ToolStripMenuItem Language_italian;
        private ToolStripMenuItem Language_japanese;
        private ToolStripMenuItem Language_russian;
        private ToolStripMenuItem Language_spanish;
        private TabPage Page_DFYMOOManager;
        public TextBox CARDS_TB_CardDesc;
        public PictureBox CARDS_PB_CardPicture;
        public ComboBox CARDS_CB_CardID;
        public TextBox CARDS_TB_CardDef;
        public TextBox CARDS_TB_CardAtk;
        public ComboBox CARDS_CB_CardAttribute;
        public NumericUpDown CARDS_Nud_CardLevel;
        public ComboBox CARDS_CB_CardTypes;
        public TextBox CARDS_TB_CardName;
        private GroupBox groupBox6;
        private ToolStripMenuItem WOLFUI_TOOLITEM_Extract;
        private ToolStripMenuItem WOLFUI_TOOLITEM_Pack;
        private GroupBox groupBox8;
        public Button DFY_BTN_Save;
        private Button DFY_BTN_Load;
        private GroupBox groupBox9;
        public Label lbl_Dfymoo_NumOfItems;
        public Label lbl_Dfymoo_name;
        private Label label10;
        private Label label12;
        private ListView DFYMOO_ItemList;
        private GroupBox groupBox7;
        public NumericUpDown DFY_NUD_Y;
        public NumericUpDown DFY_NUD_X;
        private Label DFY_POS_Y;
        private Label DFY_POS_X;
        public NumericUpDown DFY_NUD_W;
        public NumericUpDown DFY_NUD_H;
        private Label label7;
        private Label label8;
        private TabPage Page_ANIMLISTManager;
        private GroupBox groupBox10;
        public Button ANIMS_BTN_SaveScene;
        private Button ANIMS_BTN_OpenScene;
        private GroupBox ANIM_GB_Info;
        private Label ANIMS_LBL_Count;
        private Label ANIMS_LBL_NumOfItems;
        public Button ARCHIVE_BTN_OpenZIB;
        public ListView ANIMS_LV_ItemsInScene;
        private Button ARCHIVE_BTN_PackZIB;
        private TabPage Page_BNDManager;
        private ListBox STRMAN_LB_CurrentFileStrings;
        private TextBox STRMAN_TB_Search;
        public Button STRMAN_BTN_Search;
        private CheckBox STRMAN_CheckB_CaseSensitive;
        private GroupBox groupBox5;
        public Button STRMAN_BTN_SaveStrings;
        private Button STRMAN_BTN_OpenStrings;
        private GroupBox groupBox11;
        private Label STRMAN_LBL_LocalCount;
        private Label STRMAN_LBL_Local;
        private TextBox STRMAN_TB_NewStringValue;
        private Label label9;
        private ProgressBar STRMAN_PB_HowFarThroughTheFile;
        private Label label11;
        private TabPage Page_YDCManager;
        private CheckBox CREDITS_CheckB_IsCredit;
        private GroupBox groupBox12;
        private CheckBox YDC_CHKBOX_LoadPictures;
        public Button YDC_BTN_SaveDeck;
        private Button YDC_BTN_OpenDeck;
        private GroupBox groupBox13;
        private Label YDC_LBL_NumOfCardInMain;
        private Label label14;
        private TabControl tabControl1;
        private TabPage tabPage1;
        private TabPage tabPage2;
        private Button YDC_BTN_OpenSaveFile;
        private TabPage tabPage3;
        private GroupBox groupBox14;
        public Button YDC_BTN_ReplaceCard;
        public Button YDC_BTN_RemoveCard;
        private Button YDC_BTN_AddCard;
        private Label YDC_LBL_NumOfCardsInExtra;
        private Label label18;
        private Label YDC_LBL_NumOfCardsInSide;
        private Label label16;
        private ListView YDC_LV_MainDeckCards;
        private Label label13;
        private TextBox YDC_TB_DeckName;
        private Button CARDS_BTN_SaveCard;
        private Button CARDS_BTN_OpenCards;
        private CheckBox YDC_CHKBOX_UseCardID;
        private CheckBox YDC_CB_UseSimpleEditor;
        private ToolStripMenuItem WOLFUI_TOOLITEM_SetPath;
        private Button CARDS_BTN_CloseBinder;
        private ToolStripMenuItem loadGameToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripMenuItem WOLFUI_TOOLITEM_OpenConfigEditor;
        private ToolStripSeparator toolStripSeparator3;
        private ToolStripSeparator toolStripSeparator4;
        private TabPage Page_PDLimitsManager;
        private GroupBox groupBox15;
        private CheckBox PDL_CB_IsUsingSimpleAddBox;
        public Button button1;
        public Button PDL_BTN_RemoveCardFromList;
        private Button PDL_BTN_AddCardToList;
        private GroupBox groupBox16;
        private CheckBox PDL_CB_UseCardID;
        private CheckBox PDL_CB_LoadImages;
        public Button PDL_BTN_SavePDL;
        private Button PDL_BTN_OpenPDL;
        private GroupBox groupBox17;
        private Label PDL_LBL_NumOfLimited;
        private Label label19;
        private Label PDL_LBL_NumOfForbidden;
        private Label label21;
        private Label label23;
        private TabControl tabControl2;
        private TabPage PDL_ForbiddenCards;
        private ListView PDL_LV_ForbiddenCards;
        private TabPage PDL_LimitedCards;
        private ListView PDL_LV_LimitedCards;
        private TabPage PDL_SemiLimitedCards;
        private ListView PDL_LV_SemiLimitedCards;
        private Label PDL_LBL_NumOfSemiLimited;
        private Label label17;
        private TabPage Page_CardShopManager;
        private GroupBox groupBox20;
        private CheckBox checkBox2;
        private CheckBox checkBox3;
        public Button button2;
        private Button PACKDEF_BTN_OpenPackDEF;
        private GroupBox groupBox19;
        private Label label24;
        private Label label22;
        private Label label15;
        private Label label20;
        private Label label27;
        private CheckBox CARDS_CB_LoadCards;
        private GroupBox groupBox18;
        private Label label25;
        private Label label6;
        private RadioButton CARDS_RB_SimilarOnEffect;
        private RadioButton CARDS_RB_AlwaysSimilar;
        private ComboBox CARDS_CB_SimilarID;
        private ComboBox CARDS_CB_SimilarCardName;
        private Label label28;
        private TextBox CARDS_TB_CardPassword;
        private Label label29;
        private TextBox CARDS_TB_Kana;
        private Label label26;
        private TextBox CARDS_TB_CardNumber;
        private CheckBox PDL_CB_ManualOpenWhenPathSet;
    }
}