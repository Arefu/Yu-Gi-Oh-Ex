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
            btn_SaveCard = new Button();
            groupBox6 = new GroupBox();
            CB_LoadCensoredCards = new CheckBox();
            btn_NextCard = new Button();
            btn_LastCard = new Button();
            groupBox2 = new GroupBox();
            Nud_CardLevel = new NumericUpDown();
            comboBox6 = new ComboBox();
            CB_CardAttribute = new ComboBox();
            CB_CardTypes = new ComboBox();
            CB_CardImageID = new ComboBox();
            CB_CardID = new ComboBox();
            CB_CardLevel = new Label();
            CB_CardLimitedStatus = new Label();
            label6 = new Label();
            label5 = new Label();
            label4 = new Label();
            label3 = new Label();
            groupBox1 = new GroupBox();
            CB_CardName = new ComboBox();
            TB_CardDef = new TextBox();
            TB_CardAtk = new TextBox();
            label2 = new Label();
            label1 = new Label();
            TB_CardDesc = new TextBox();
            PB_CardPicture = new PictureBox();
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
            groupBox5 = new GroupBox();
            CREDITS_CheckB_IsCredit = new CheckBox();
            STRMAN_BTN_SaveStrings = new Button();
            STRMAN_BTN_OpenStrings = new Button();
            groupBox11 = new GroupBox();
            STRMAN_LBL_LocalCount = new Label();
            STRMAN_LBL_Local = new Label();
            tabPage1 = new TabPage();
            MenuBar = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            File_Open = new ToolStripMenuItem();
            closeToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator1 = new ToolStripSeparator();
            File_Exit = new ToolStripMenuItem();
            toolsToolStripMenuItem = new ToolStripMenuItem();
            WOLFUI_TOOLITEM_Extract = new ToolStripMenuItem();
            WOLFUI_TOOLITEM_Pack = new ToolStripMenuItem();
            Tools_Verify = new ToolStripMenuItem();
            toolStripSeparator2 = new ToolStripSeparator();
            languageToolStripMenuItem = new ToolStripMenuItem();
            Language_english = new ToolStripMenuItem();
            Language_french = new ToolStripMenuItem();
            Language_german = new ToolStripMenuItem();
            Language_italian = new ToolStripMenuItem();
            Language_japanese = new ToolStripMenuItem();
            Language_russian = new ToolStripMenuItem();
            Language_spanish = new ToolStripMenuItem();
            Status = new StatusStrip();
            toolStripStatusLabel1 = new ToolStripStatusLabel();
            LBL_GameStatusLabel = new ToolStripStatusLabel();
            WolfX_TabManager.SuspendLayout();
            Page_CardManager.SuspendLayout();
            groupBox6.SuspendLayout();
            groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)Nud_CardLevel).BeginInit();
            groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)PB_CardPicture).BeginInit();
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
            MenuBar.SuspendLayout();
            Status.SuspendLayout();
            SuspendLayout();
            // 
            // WolfX_TabManager
            // 
            WolfX_TabManager.Controls.Add(Page_CardManager);
            WolfX_TabManager.Controls.Add(Page_ZibManager);
            WolfX_TabManager.Controls.Add(Page_DFYMOOManager);
            WolfX_TabManager.Controls.Add(Page_ANIMLISTManager);
            WolfX_TabManager.Controls.Add(Page_BNDManager);
            WolfX_TabManager.Controls.Add(tabPage1);
            WolfX_TabManager.Dock = DockStyle.Fill;
            WolfX_TabManager.Location = new Point(0, 24);
            WolfX_TabManager.Name = "WolfX_TabManager";
            WolfX_TabManager.SelectedIndex = 0;
            WolfX_TabManager.Size = new Size(659, 657);
            WolfX_TabManager.TabIndex = 0;
            // 
            // Page_CardManager
            // 
            Page_CardManager.Controls.Add(btn_SaveCard);
            Page_CardManager.Controls.Add(groupBox6);
            Page_CardManager.Controls.Add(btn_NextCard);
            Page_CardManager.Controls.Add(btn_LastCard);
            Page_CardManager.Controls.Add(groupBox2);
            Page_CardManager.Controls.Add(groupBox1);
            Page_CardManager.Location = new Point(4, 24);
            Page_CardManager.Name = "Page_CardManager";
            Page_CardManager.Padding = new Padding(3);
            Page_CardManager.Size = new Size(651, 629);
            Page_CardManager.TabIndex = 0;
            Page_CardManager.Text = "Card Manager";
            Page_CardManager.UseVisualStyleBackColor = true;
            // 
            // btn_SaveCard
            // 
            btn_SaveCard.Location = new Point(447, 335);
            btn_SaveCard.Name = "btn_SaveCard";
            btn_SaveCard.Size = new Size(107, 25);
            btn_SaveCard.TabIndex = 6;
            btn_SaveCard.Text = "Save Card";
            btn_SaveCard.UseVisualStyleBackColor = true;
            btn_SaveCard.Click += btn_SaveCard_Click;
            // 
            // groupBox6
            // 
            groupBox6.Controls.Add(CB_LoadCensoredCards);
            groupBox6.Location = new Point(334, 409);
            groupBox6.Name = "groupBox6";
            groupBox6.Size = new Size(303, 154);
            groupBox6.TabIndex = 5;
            groupBox6.TabStop = false;
            groupBox6.Text = "Options";
            // 
            // CB_LoadCensoredCards
            // 
            CB_LoadCensoredCards.AutoSize = true;
            CB_LoadCensoredCards.Location = new Point(6, 22);
            CB_LoadCensoredCards.Name = "CB_LoadCensoredCards";
            CB_LoadCensoredCards.Size = new Size(138, 19);
            CB_LoadCensoredCards.TabIndex = 0;
            CB_LoadCensoredCards.Text = "Load Censored Cards";
            CB_LoadCensoredCards.UseVisualStyleBackColor = true;
            CB_LoadCensoredCards.CheckedChanged += CB_LoadCensoredCards_CheckedChanged;
            // 
            // btn_NextCard
            // 
            btn_NextCard.Location = new Point(334, 366);
            btn_NextCard.Name = "btn_NextCard";
            btn_NextCard.Size = new Size(107, 25);
            btn_NextCard.TabIndex = 4;
            btn_NextCard.Text = "Next Card";
            btn_NextCard.UseVisualStyleBackColor = true;
            btn_NextCard.Click += btn_NextCard_Click;
            // 
            // btn_LastCard
            // 
            btn_LastCard.Location = new Point(334, 335);
            btn_LastCard.Name = "btn_LastCard";
            btn_LastCard.Size = new Size(107, 25);
            btn_LastCard.TabIndex = 3;
            btn_LastCard.Text = "Previous Card";
            btn_LastCard.UseVisualStyleBackColor = true;
            btn_LastCard.Click += btn_LastCard_Click;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(Nud_CardLevel);
            groupBox2.Controls.Add(comboBox6);
            groupBox2.Controls.Add(CB_CardAttribute);
            groupBox2.Controls.Add(CB_CardTypes);
            groupBox2.Controls.Add(CB_CardImageID);
            groupBox2.Controls.Add(CB_CardID);
            groupBox2.Controls.Add(CB_CardLevel);
            groupBox2.Controls.Add(CB_CardLimitedStatus);
            groupBox2.Controls.Add(label6);
            groupBox2.Controls.Add(label5);
            groupBox2.Controls.Add(label4);
            groupBox2.Controls.Add(label3);
            groupBox2.Location = new Point(334, 6);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(320, 323);
            groupBox2.TabIndex = 2;
            groupBox2.TabStop = false;
            groupBox2.Text = "Card Information";
            // 
            // Nud_CardLevel
            // 
            Nud_CardLevel.Location = new Point(6, 238);
            Nud_CardLevel.Name = "Nud_CardLevel";
            Nud_CardLevel.Size = new Size(297, 23);
            Nud_CardLevel.TabIndex = 15;
            // 
            // comboBox6
            // 
            comboBox6.FormattingEnabled = true;
            comboBox6.Location = new Point(6, 287);
            comboBox6.Name = "comboBox6";
            comboBox6.Size = new Size(297, 23);
            comboBox6.TabIndex = 12;
            // 
            // CB_CardAttribute
            // 
            CB_CardAttribute.FormattingEnabled = true;
            CB_CardAttribute.Items.AddRange(new object[] { "Unknown", "Light Monster", "Dark Monster", "Water Monster", "Fire Monster", "Earth Monster", "Wind Monster", "Divine Monster", "Spell", "Trap" });
            CB_CardAttribute.Location = new Point(6, 189);
            CB_CardAttribute.Name = "CB_CardAttribute";
            CB_CardAttribute.Size = new Size(297, 23);
            CB_CardAttribute.TabIndex = 10;
            // 
            // CB_CardTypes
            // 
            CB_CardTypes.FormattingEnabled = true;
            CB_CardTypes.Location = new Point(6, 140);
            CB_CardTypes.Name = "CB_CardTypes";
            CB_CardTypes.Size = new Size(297, 23);
            CB_CardTypes.TabIndex = 9;
            // 
            // CB_CardImageID
            // 
            CB_CardImageID.FormattingEnabled = true;
            CB_CardImageID.Location = new Point(6, 91);
            CB_CardImageID.Name = "CB_CardImageID";
            CB_CardImageID.Size = new Size(297, 23);
            CB_CardImageID.TabIndex = 8;
            // 
            // CB_CardID
            // 
            CB_CardID.AutoCompleteMode = AutoCompleteMode.Suggest;
            CB_CardID.FormattingEnabled = true;
            CB_CardID.Location = new Point(6, 42);
            CB_CardID.Name = "CB_CardID";
            CB_CardID.Size = new Size(297, 23);
            CB_CardID.TabIndex = 7;
            CB_CardID.SelectedIndexChanged += CB_CardID_SelectedIndexChanged;
            // 
            // CB_CardLevel
            // 
            CB_CardLevel.AutoSize = true;
            CB_CardLevel.Font = new Font("Segoe UI", 11F);
            CB_CardLevel.Location = new Point(6, 215);
            CB_CardLevel.Name = "CB_CardLevel";
            CB_CardLevel.Size = new Size(78, 20);
            CB_CardLevel.TabIndex = 5;
            CB_CardLevel.Text = "Card Level";
            // 
            // CB_CardLimitedStatus
            // 
            CB_CardLimitedStatus.AutoSize = true;
            CB_CardLimitedStatus.Font = new Font("Segoe UI", 11F);
            CB_CardLimitedStatus.Location = new Point(6, 264);
            CB_CardLimitedStatus.Name = "CB_CardLimitedStatus";
            CB_CardLimitedStatus.Size = new Size(138, 20);
            CB_CardLimitedStatus.TabIndex = 4;
            CB_CardLimitedStatus.Text = "Card Limited Status";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Font = new Font("Segoe UI", 11F);
            label6.Location = new Point(6, 68);
            label6.Name = "label6";
            label6.Size = new Size(105, 20);
            label6.TabIndex = 3;
            label6.Text = "Card Image ID";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Font = new Font("Segoe UI", 11F);
            label5.Location = new Point(6, 117);
            label5.Name = "label5";
            label5.Size = new Size(75, 20);
            label5.TabIndex = 2;
            label5.Text = "Card Type";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Font = new Font("Segoe UI", 11F);
            label4.Location = new Point(6, 166);
            label4.Name = "label4";
            label4.Size = new Size(103, 20);
            label4.TabIndex = 1;
            label4.Text = "Card Attribute";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Font = new Font("Segoe UI", 11F);
            label3.Location = new Point(6, 19);
            label3.Name = "label3";
            label3.Size = new Size(59, 20);
            label3.TabIndex = 0;
            label3.Text = "Card ID";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(CB_CardName);
            groupBox1.Controls.Add(TB_CardDef);
            groupBox1.Controls.Add(TB_CardAtk);
            groupBox1.Controls.Add(label2);
            groupBox1.Controls.Add(label1);
            groupBox1.Controls.Add(TB_CardDesc);
            groupBox1.Controls.Add(PB_CardPicture);
            groupBox1.Location = new Point(8, 6);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(320, 595);
            groupBox1.TabIndex = 1;
            groupBox1.TabStop = false;
            groupBox1.Text = "Card Preview";
            // 
            // CB_CardName
            // 
            CB_CardName.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            CB_CardName.FormattingEnabled = true;
            CB_CardName.Location = new Point(6, 19);
            CB_CardName.Name = "CB_CardName";
            CB_CardName.Size = new Size(305, 23);
            CB_CardName.TabIndex = 7;
            CB_CardName.SelectedIndexChanged += CB_CardName_SelectedIndexChanged;
            // 
            // TB_CardDef
            // 
            TB_CardDef.Location = new Point(254, 561);
            TB_CardDef.Name = "TB_CardDef";
            TB_CardDef.Size = new Size(57, 23);
            TB_CardDef.TabIndex = 6;
            // 
            // TB_CardAtk
            // 
            TB_CardAtk.Location = new Point(50, 563);
            TB_CardAtk.Name = "TB_CardAtk";
            TB_CardAtk.Size = new Size(57, 23);
            TB_CardAtk.TabIndex = 5;
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
            // TB_CardDesc
            // 
            TB_CardDesc.Location = new Point(6, 403);
            TB_CardDesc.Multiline = true;
            TB_CardDesc.Name = "TB_CardDesc";
            TB_CardDesc.ScrollBars = ScrollBars.Vertical;
            TB_CardDesc.Size = new Size(305, 154);
            TB_CardDesc.TabIndex = 2;
            // 
            // PB_CardPicture
            // 
            PB_CardPicture.Location = new Point(6, 51);
            PB_CardPicture.Name = "PB_CardPicture";
            PB_CardPicture.Size = new Size(305, 346);
            PB_CardPicture.TabIndex = 0;
            PB_CardPicture.TabStop = false;
            // 
            // Page_ZibManager
            // 
            Page_ZibManager.Controls.Add(groupBox4);
            Page_ZibManager.Controls.Add(ARCHIVE_LV_ArchiveItems);
            Page_ZibManager.Controls.Add(groupBox3);
            Page_ZibManager.Location = new Point(4, 24);
            Page_ZibManager.Name = "Page_ZibManager";
            Page_ZibManager.Padding = new Padding(3);
            Page_ZibManager.Size = new Size(651, 629);
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
            ARCHIVE_BTN_PackZIB.Enabled = false;
            ARCHIVE_BTN_PackZIB.Location = new Point(87, 69);
            ARCHIVE_BTN_PackZIB.Name = "ARCHIVE_BTN_PackZIB";
            ARCHIVE_BTN_PackZIB.Size = new Size(107, 25);
            ARCHIVE_BTN_PackZIB.TabIndex = 6;
            ARCHIVE_BTN_PackZIB.Text = "Pack Content";
            ARCHIVE_BTN_PackZIB.UseVisualStyleBackColor = true;
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
            ARCHIVE_LV_ArchiveItems.Location = new Point(8, 112);
            ARCHIVE_LV_ArchiveItems.MultiSelect = false;
            ARCHIVE_LV_ArchiveItems.Name = "ARCHIVE_LV_ArchiveItems";
            ARCHIVE_LV_ArchiveItems.ShowGroups = false;
            ARCHIVE_LV_ArchiveItems.Size = new Size(637, 496);
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
            Page_DFYMOOManager.Size = new Size(651, 629);
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
            DFYMOO_ItemList.Size = new Size(639, 496);
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
            Page_ANIMLISTManager.Size = new Size(651, 629);
            Page_ANIMLISTManager.TabIndex = 3;
            Page_ANIMLISTManager.Text = "Animlist Manager";
            Page_ANIMLISTManager.UseVisualStyleBackColor = true;
            // 
            // ANIMS_LV_ItemsInScene
            // 
            ANIMS_LV_ItemsInScene.Location = new Point(6, 112);
            ANIMS_LV_ItemsInScene.Name = "ANIMS_LV_ItemsInScene";
            ANIMS_LV_ItemsInScene.Size = new Size(639, 496);
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
            Page_BNDManager.Controls.Add(groupBox5);
            Page_BNDManager.Controls.Add(groupBox11);
            Page_BNDManager.Location = new Point(4, 24);
            Page_BNDManager.Name = "Page_BNDManager";
            Page_BNDManager.Size = new Size(651, 629);
            Page_BNDManager.TabIndex = 4;
            Page_BNDManager.Text = "Strings Manager";
            Page_BNDManager.UseVisualStyleBackColor = true;
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(331, 178);
            label11.Name = "label11";
            label11.Size = new Size(124, 15);
            label11.TabIndex = 12;
            label11.Text = "Progress Through File:";
            // 
            // STRMAN_PB_HowFarThroughTheFile
            // 
            STRMAN_PB_HowFarThroughTheFile.Location = new Point(331, 196);
            STRMAN_PB_HowFarThroughTheFile.Name = "STRMAN_PB_HowFarThroughTheFile";
            STRMAN_PB_HowFarThroughTheFile.Size = new Size(312, 23);
            STRMAN_PB_HowFarThroughTheFile.TabIndex = 11;
            // 
            // STRMAN_TB_NewStringValue
            // 
            STRMAN_TB_NewStringValue.Location = new Point(331, 130);
            STRMAN_TB_NewStringValue.Multiline = true;
            STRMAN_TB_NewStringValue.Name = "STRMAN_TB_NewStringValue";
            STRMAN_TB_NewStringValue.Size = new Size(312, 45);
            STRMAN_TB_NewStringValue.TabIndex = 10;
            STRMAN_TB_NewStringValue.Leave += STRMAN_TB_NewStringValue_Leave;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(331, 112);
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
            STRMAN_LB_CurrentFileStrings.Location = new Point(6, 112);
            STRMAN_LB_CurrentFileStrings.Name = "STRMAN_LB_CurrentFileStrings";
            STRMAN_LB_CurrentFileStrings.Size = new Size(319, 484);
            STRMAN_LB_CurrentFileStrings.TabIndex = 8;
            STRMAN_LB_CurrentFileStrings.SelectedIndexChanged += STRMAN_LB_CurrentFileStrings_SelectedIndexChanged;
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
            // tabPage1
            // 
            tabPage1.Location = new Point(4, 24);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new Padding(3);
            tabPage1.Size = new Size(651, 629);
            tabPage1.TabIndex = 5;
            tabPage1.Text = "tabPage1";
            tabPage1.UseVisualStyleBackColor = true;
            // 
            // MenuBar
            // 
            MenuBar.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, toolsToolStripMenuItem });
            MenuBar.Location = new Point(0, 0);
            MenuBar.Name = "MenuBar";
            MenuBar.Size = new Size(659, 24);
            MenuBar.TabIndex = 1;
            MenuBar.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { File_Open, closeToolStripMenuItem, toolStripSeparator1, File_Exit });
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(37, 20);
            fileToolStripMenuItem.Text = "&File";
            // 
            // File_Open
            // 
            File_Open.Name = "File_Open";
            File_Open.ShortcutKeys = Keys.Control | Keys.O;
            File_Open.Size = new Size(148, 22);
            File_Open.Text = "&Open";
            File_Open.Click += File_Open_Click;
            // 
            // closeToolStripMenuItem
            // 
            closeToolStripMenuItem.Name = "closeToolStripMenuItem";
            closeToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.W;
            closeToolStripMenuItem.Size = new Size(148, 22);
            closeToolStripMenuItem.Text = "&Close";
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(145, 6);
            // 
            // File_Exit
            // 
            File_Exit.Name = "File_Exit";
            File_Exit.ShortcutKeys = Keys.Alt | Keys.F4;
            File_Exit.Size = new Size(148, 22);
            File_Exit.Text = "E&xit";
            File_Exit.Click += File_Exit_Click;
            // 
            // toolsToolStripMenuItem
            // 
            toolsToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { WOLFUI_TOOLITEM_Extract, WOLFUI_TOOLITEM_Pack, Tools_Verify, toolStripSeparator2, languageToolStripMenuItem });
            toolsToolStripMenuItem.Name = "toolsToolStripMenuItem";
            toolsToolStripMenuItem.Size = new Size(46, 20);
            toolsToolStripMenuItem.Text = "&Tools";
            // 
            // WOLFUI_TOOLITEM_Extract
            // 
            WOLFUI_TOOLITEM_Extract.Name = "WOLFUI_TOOLITEM_Extract";
            WOLFUI_TOOLITEM_Extract.ShortcutKeys = Keys.Control | Keys.E;
            WOLFUI_TOOLITEM_Extract.Size = new Size(184, 22);
            WOLFUI_TOOLITEM_Extract.Text = "&Extract Game";
            WOLFUI_TOOLITEM_Extract.Click += WOLFUI_TOOLITEM_Extract_Click;
            // 
            // WOLFUI_TOOLITEM_Pack
            // 
            WOLFUI_TOOLITEM_Pack.Name = "WOLFUI_TOOLITEM_Pack";
            WOLFUI_TOOLITEM_Pack.ShortcutKeys = Keys.Control | Keys.P;
            WOLFUI_TOOLITEM_Pack.Size = new Size(184, 22);
            WOLFUI_TOOLITEM_Pack.Text = "&Pack Game";
            WOLFUI_TOOLITEM_Pack.Click += WOLFUI_TOOLITEM_Pack_Click;
            // 
            // Tools_Verify
            // 
            Tools_Verify.Enabled = false;
            Tools_Verify.Name = "Tools_Verify";
            Tools_Verify.Size = new Size(184, 22);
            Tools_Verify.Text = "&Verify Extracted Files";
            Tools_Verify.Click += Tools_Verify_Click;
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new Size(181, 6);
            // 
            // languageToolStripMenuItem
            // 
            languageToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { Language_english, Language_french, Language_german, Language_italian, Language_japanese, Language_russian, Language_spanish });
            languageToolStripMenuItem.Name = "languageToolStripMenuItem";
            languageToolStripMenuItem.Size = new Size(184, 22);
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
            // Status
            // 
            Status.Items.AddRange(new ToolStripItem[] { toolStripStatusLabel1, LBL_GameStatusLabel });
            Status.Location = new Point(0, 659);
            Status.Name = "Status";
            Status.Size = new Size(659, 22);
            Status.SizingGrip = false;
            Status.TabIndex = 2;
            Status.Text = "statusStrip1";
            // 
            // toolStripStatusLabel1
            // 
            toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            toolStripStatusLabel1.Size = new Size(44, 17);
            toolStripStatusLabel1.Text = "Game: ";
            // 
            // LBL_GameStatusLabel
            // 
            LBL_GameStatusLabel.ForeColor = Color.Red;
            LBL_GameStatusLabel.Name = "LBL_GameStatusLabel";
            LBL_GameStatusLabel.Size = new Size(69, 17);
            LBL_GameStatusLabel.Text = "Not Loaded";
            // 
            // WolfUI
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(659, 681);
            Controls.Add(Status);
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
            groupBox6.ResumeLayout(false);
            groupBox6.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)Nud_CardLevel).EndInit();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)PB_CardPicture).EndInit();
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
            MenuBar.ResumeLayout(false);
            MenuBar.PerformLayout();
            Status.ResumeLayout(false);
            Status.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion


        private TabControl WolfX_TabManager;
        private TabPage Page_CardManager;
        private TabPage Page_ZibManager;
        private MenuStrip MenuBar;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem File_Open;
        private ToolStripMenuItem closeToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripMenuItem File_Exit;
        private GroupBox groupBox1;
        private Label label2;
        private Label label1;
        private GroupBox groupBox2;
        private ComboBox comboBox6;
        private ComboBox CB_CardImageID;
        private Label CB_CardLevel;
        private Label CB_CardLimitedStatus;
        private Label label6;
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
        private StatusStrip Status;
        private ToolStripStatusLabel toolStripStatusLabel1;
        public ToolStripStatusLabel LBL_GameStatusLabel;
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
        public TextBox TB_CardDesc;
        private Button btn_NextCard;
        private Button btn_LastCard;
        public PictureBox PB_CardPicture;
        public ComboBox CB_CardID;
        public TextBox TB_CardDef;
        public TextBox TB_CardAtk;
        public ComboBox CB_CardAttribute;
        public NumericUpDown Nud_CardLevel;
        public ComboBox CB_CardTypes;
        public ComboBox CB_CardName;
        private Button btn_SaveCard;
        private GroupBox groupBox6;
        public CheckBox CB_LoadCensoredCards;
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
        private TabPage tabPage1;
        private CheckBox CREDITS_CheckB_IsCredit;
    }
}