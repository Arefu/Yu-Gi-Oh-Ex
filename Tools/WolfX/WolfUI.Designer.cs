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

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
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
            Context = new ContextMenuStrip(components);
            ReplaceImage = new ToolStripMenuItem();
            Page_ZibManager = new TabPage();
            textBox1 = new TextBox();
            groupBox5 = new GroupBox();
            checkBox1 = new CheckBox();
            button1 = new Button();
            groupBox4 = new GroupBox();
            btn_CloseArchive = new Button();
            cb_ShowFileName = new CheckBox();
            btn_ExtractAll = new Button();
            cb_ShowPicturePreview = new CheckBox();
            lv_ArchivePreviewer = new ListView();
            groupBox3 = new GroupBox();
            lbl_ItemCount = new Label();
            lbl_Size = new Label();
            lbl_Name = new Label();
            lbl_NumberOfItemsPrompt = new Label();
            lbl_FileSizePrompt = new Label();
            lbl_NamePrompt = new Label();
            Page_AnimationManager = new TabPage();
            MenuBar = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            File_Open = new ToolStripMenuItem();
            closeToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator1 = new ToolStripSeparator();
            File_Exit = new ToolStripMenuItem();
            toolsToolStripMenuItem = new ToolStripMenuItem();
            extractGameToolStripMenuItem = new ToolStripMenuItem();
            packGameToolStripMenuItem = new ToolStripMenuItem();
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
            Context.SuspendLayout();
            Page_ZibManager.SuspendLayout();
            groupBox5.SuspendLayout();
            groupBox4.SuspendLayout();
            groupBox3.SuspendLayout();
            MenuBar.SuspendLayout();
            Status.SuspendLayout();
            SuspendLayout();
            // 
            // WolfX_TabManager
            // 
            WolfX_TabManager.Controls.Add(Page_CardManager);
            WolfX_TabManager.Controls.Add(Page_ZibManager);
            WolfX_TabManager.Controls.Add(Page_AnimationManager);
            WolfX_TabManager.Dock = DockStyle.Fill;
            WolfX_TabManager.Enabled = false;
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
            PB_CardPicture.ContextMenuStrip = Context;
            PB_CardPicture.Location = new Point(6, 51);
            PB_CardPicture.Name = "PB_CardPicture";
            PB_CardPicture.Size = new Size(305, 346);
            PB_CardPicture.TabIndex = 0;
            PB_CardPicture.TabStop = false;
            // 
            // Context
            // 
            Context.Font = new Font("Comic Sans MS", 9F);
            Context.Items.AddRange(new ToolStripItem[] { ReplaceImage });
            Context.Name = "Context";
            Context.Size = new Size(155, 26);
            // 
            // ReplaceImage
            // 
            ReplaceImage.Name = "ReplaceImage";
            ReplaceImage.Size = new Size(154, 22);
            ReplaceImage.Text = "Replace Image";
            ReplaceImage.Click += ReplaceImage_Click;
            // 
            // Page_ZibManager
            // 
            Page_ZibManager.Controls.Add(textBox1);
            Page_ZibManager.Controls.Add(groupBox5);
            Page_ZibManager.Controls.Add(groupBox4);
            Page_ZibManager.Controls.Add(lv_ArchivePreviewer);
            Page_ZibManager.Controls.Add(groupBox3);
            Page_ZibManager.Location = new Point(4, 24);
            Page_ZibManager.Name = "Page_ZibManager";
            Page_ZibManager.Padding = new Padding(3);
            Page_ZibManager.Size = new Size(651, 629);
            Page_ZibManager.TabIndex = 1;
            Page_ZibManager.Text = "Archive Manager";
            Page_ZibManager.UseVisualStyleBackColor = true;
            // 
            // textBox1
            // 
            textBox1.Location = new Point(24, 398);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(8, 23);
            textBox1.TabIndex = 3;
            // 
            // groupBox5
            // 
            groupBox5.Controls.Add(checkBox1);
            groupBox5.Controls.Add(button1);
            groupBox5.Location = new Point(420, 6);
            groupBox5.Name = "groupBox5";
            groupBox5.Size = new Size(200, 100);
            groupBox5.TabIndex = 2;
            groupBox5.TabStop = false;
            groupBox5.Text = "Packing Tools";
            // 
            // checkBox1
            // 
            checkBox1.AutoSize = true;
            checkBox1.Location = new Point(6, 22);
            checkBox1.Name = "checkBox1";
            checkBox1.Size = new Size(158, 19);
            checkBox1.TabIndex = 1;
            checkBox1.Text = "Auto Copy When Packed";
            checkBox1.UseVisualStyleBackColor = true;
            // 
            // button1
            // 
            button1.Location = new Point(6, 69);
            button1.Name = "button1";
            button1.Size = new Size(107, 25);
            button1.TabIndex = 0;
            button1.Text = "Pack Content";
            button1.UseVisualStyleBackColor = true;
            // 
            // groupBox4
            // 
            groupBox4.Controls.Add(btn_CloseArchive);
            groupBox4.Controls.Add(cb_ShowFileName);
            groupBox4.Controls.Add(btn_ExtractAll);
            groupBox4.Controls.Add(cb_ShowPicturePreview);
            groupBox4.Location = new Point(214, 6);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(200, 100);
            groupBox4.TabIndex = 1;
            groupBox4.TabStop = false;
            groupBox4.Text = "Archive Tools";
            // 
            // btn_CloseArchive
            // 
            btn_CloseArchive.Enabled = false;
            btn_CloseArchive.Location = new Point(119, 69);
            btn_CloseArchive.Name = "btn_CloseArchive";
            btn_CloseArchive.Size = new Size(75, 25);
            btn_CloseArchive.TabIndex = 4;
            btn_CloseArchive.Text = "Close";
            btn_CloseArchive.UseVisualStyleBackColor = true;
            btn_CloseArchive.Click += btn_CloseArchive_Click;
            // 
            // cb_ShowFileName
            // 
            cb_ShowFileName.AutoSize = true;
            cb_ShowFileName.Checked = true;
            cb_ShowFileName.CheckState = CheckState.Checked;
            cb_ShowFileName.Location = new Point(6, 44);
            cb_ShowFileName.Name = "cb_ShowFileName";
            cb_ShowFileName.Size = new Size(111, 19);
            cb_ShowFileName.TabIndex = 3;
            cb_ShowFileName.Text = "Show File Name";
            cb_ShowFileName.UseVisualStyleBackColor = true;
            // 
            // btn_ExtractAll
            // 
            btn_ExtractAll.Location = new Point(6, 69);
            btn_ExtractAll.Name = "btn_ExtractAll";
            btn_ExtractAll.Size = new Size(107, 25);
            btn_ExtractAll.TabIndex = 2;
            btn_ExtractAll.Text = "Extract Content";
            btn_ExtractAll.UseVisualStyleBackColor = true;
            btn_ExtractAll.Click += btn_ExtractAll_Click;
            // 
            // cb_ShowPicturePreview
            // 
            cb_ShowPicturePreview.AutoSize = true;
            cb_ShowPicturePreview.Checked = true;
            cb_ShowPicturePreview.CheckState = CheckState.Checked;
            cb_ShowPicturePreview.Location = new Point(6, 22);
            cb_ShowPicturePreview.Name = "cb_ShowPicturePreview";
            cb_ShowPicturePreview.Size = new Size(139, 19);
            cb_ShowPicturePreview.TabIndex = 1;
            cb_ShowPicturePreview.Text = "Show Picture Preview";
            cb_ShowPicturePreview.UseVisualStyleBackColor = true;
            // 
            // lv_ArchivePreviewer
            // 
            lv_ArchivePreviewer.Location = new Point(8, 112);
            lv_ArchivePreviewer.MultiSelect = false;
            lv_ArchivePreviewer.Name = "lv_ArchivePreviewer";
            lv_ArchivePreviewer.ShowGroups = false;
            lv_ArchivePreviewer.Size = new Size(637, 496);
            lv_ArchivePreviewer.TabIndex = 1;
            lv_ArchivePreviewer.UseCompatibleStateImageBehavior = false;
            lv_ArchivePreviewer.View = View.SmallIcon;
            lv_ArchivePreviewer.MouseDoubleClick += Lv_ArchivePreviewer_MouseDoubleClick;
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(lbl_ItemCount);
            groupBox3.Controls.Add(lbl_Size);
            groupBox3.Controls.Add(lbl_Name);
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
            // lbl_ItemCount
            // 
            lbl_ItemCount.AutoSize = true;
            lbl_ItemCount.Location = new Point(114, 65);
            lbl_ItemCount.Name = "lbl_ItemCount";
            lbl_ItemCount.Size = new Size(0, 15);
            lbl_ItemCount.TabIndex = 5;
            // 
            // lbl_Size
            // 
            lbl_Size.AutoSize = true;
            lbl_Size.Location = new Point(63, 42);
            lbl_Size.Name = "lbl_Size";
            lbl_Size.Size = new Size(0, 15);
            lbl_Size.TabIndex = 4;
            // 
            // lbl_Name
            // 
            lbl_Name.AutoSize = true;
            lbl_Name.Location = new Point(54, 19);
            lbl_Name.Name = "lbl_Name";
            lbl_Name.Size = new Size(0, 15);
            lbl_Name.TabIndex = 3;
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
            // Page_AnimationManager
            // 
            Page_AnimationManager.Location = new Point(4, 24);
            Page_AnimationManager.Name = "Page_AnimationManager";
            Page_AnimationManager.Padding = new Padding(3);
            Page_AnimationManager.Size = new Size(651, 629);
            Page_AnimationManager.TabIndex = 2;
            Page_AnimationManager.Text = "Animation Manager";
            Page_AnimationManager.UseVisualStyleBackColor = true;
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
            toolsToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { extractGameToolStripMenuItem, packGameToolStripMenuItem, Tools_Verify, toolStripSeparator2, languageToolStripMenuItem });
            toolsToolStripMenuItem.Name = "toolsToolStripMenuItem";
            toolsToolStripMenuItem.Size = new Size(46, 20);
            toolsToolStripMenuItem.Text = "&Tools";
            // 
            // extractGameToolStripMenuItem
            // 
            extractGameToolStripMenuItem.Name = "extractGameToolStripMenuItem";
            extractGameToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.E;
            extractGameToolStripMenuItem.Size = new Size(184, 22);
            extractGameToolStripMenuItem.Text = "&Extract Game";
            extractGameToolStripMenuItem.Click += extractGameToolStripMenuItem_Click;
            // 
            // packGameToolStripMenuItem
            // 
            packGameToolStripMenuItem.Name = "packGameToolStripMenuItem";
            packGameToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.P;
            packGameToolStripMenuItem.Size = new Size(184, 22);
            packGameToolStripMenuItem.Text = "&Pack Game";
            packGameToolStripMenuItem.Click += packGameToolStripMenuItem_Click;
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
            Language_english.Size = new Size(180, 22);
            Language_english.Text = "English";
            Language_english.Click += Language_english_Click;
            // 
            // Language_french
            // 
            Language_french.Name = "Language_french";
            Language_french.Size = new Size(180, 22);
            Language_french.Text = "Français";
            Language_french.Click += Language_french_Click;
            // 
            // Language_german
            // 
            Language_german.Name = "Language_german";
            Language_german.Size = new Size(180, 22);
            Language_german.Text = "Deutsch";
            Language_german.Click += Language_german_Click;
            // 
            // Language_italian
            // 
            Language_italian.Name = "Language_italian";
            Language_italian.Size = new Size(180, 22);
            Language_italian.Text = "Italiano";
            Language_italian.Click += Language_italian_Click;
            // 
            // Language_japanese
            // 
            Language_japanese.Name = "Language_japanese";
            Language_japanese.Size = new Size(180, 22);
            Language_japanese.Text = "Nihon";
            Language_japanese.Click += Language_japanese_Click;
            // 
            // Language_russian
            // 
            Language_russian.Name = "Language_russian";
            Language_russian.Size = new Size(180, 22);
            Language_russian.Text = "Russkiy";
            Language_russian.Click += Language_russian_Click;
            // 
            // Language_spanish
            // 
            Language_spanish.Name = "Language_spanish";
            Language_spanish.Size = new Size(180, 22);
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
            Context.ResumeLayout(false);
            Page_ZibManager.ResumeLayout(false);
            Page_ZibManager.PerformLayout();
            groupBox5.ResumeLayout(false);
            groupBox5.PerformLayout();
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
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
        private Button btn_ExtractAll;
        public ListView lv_ArchivePreviewer;
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
        public Button btn_CloseArchive;
        public CheckBox cb_ShowPicturePreview;
        public Label lbl_Size;
        public Label lbl_Name;
        public Label lbl_ItemCount;
        public CheckBox cb_ShowFileName;
        private GroupBox groupBox5;
        private CheckBox checkBox1;
        private Button button1;
        private ToolStripSeparator toolStripSeparator2;
        private ToolStripMenuItem languageToolStripMenuItem;
        private ToolStripMenuItem Language_english;
        private ToolStripMenuItem Language_french;
        private ToolStripMenuItem Language_german;
        private ToolStripMenuItem Language_italian;
        private ToolStripMenuItem Language_japanese;
        private ToolStripMenuItem Language_russian;
        private ToolStripMenuItem Language_spanish;
        private TabPage Page_AnimationManager;
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
        private ContextMenuStrip Context;
        private ToolStripMenuItem ReplaceImage;
        private GroupBox groupBox6;
        public CheckBox CB_LoadCensoredCards;
        private TextBox textBox1;
        private ToolStripMenuItem extractGameToolStripMenuItem;
        private ToolStripMenuItem packGameToolStripMenuItem;
    }
}