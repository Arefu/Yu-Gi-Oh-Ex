#nullable disable

namespace WolfEx
{
    partial class CardsPanel
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
            split = new SplitContainer();
            left = new Panel();
            right = new Panel();
            _list = new ListBox();
            leftButtons = new FlowLayoutPanel();
            btnAdd = new Button();
            btnDuplicate = new Button();
            btnDelete = new Button();
            form = new TableLayoutPanel();
            lblId = new Label();
            _id = new NumericUpDown();
            lblName = new Label();
            _name = new TextBox();
            lblDesc = new Label();
            _desc = new TextBox();
            lblKind = new Label();
            _kind = new ComboBox();
            lblType = new Label();
            _type = new ComboBox();
            lblAttribute = new Label();
            _attribute = new ComboBox();
            lblIcon = new Label();
            _icon = new ComboBox();
            lblLevel = new Label();
            _level = new NumericUpDown();
            lblAtk = new Label();
            _atk = new NumericUpDown();
            lblDef = new Label();
            _def = new NumericUpDown();
            lblLimitation = new Label();
            _limitation = new ComboBox();
            lblCopies = new Label();
            _copies = new NumericUpDown();
            lblArchetypes = new Label();
            archRow = new TableLayoutPanel();
            _archetypes = new TextBox();
            btnArchetypes = new Button();
            lblArt = new Label();
            artRow = new TableLayoutPanel();
            _artPath = new TextBox();
            btnChooseArt = new Button();
            _preview = new PictureBox();
            _artInfo = new Label();
            left.SuspendLayout();
            right.SuspendLayout();
            leftButtons.SuspendLayout();
            form.SuspendLayout();
            artRow.SuspendLayout();
            archRow.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)split).BeginInit();
            split.Panel1.SuspendLayout();
            split.Panel2.SuspendLayout();
            split.SuspendLayout();
            SuspendLayout();
            // 
            // split
            // 
            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(right);
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel1;
            split.SplitterDistance = 340;
            split.Name = "split";
            // 
            // left
            // 
            left.Controls.Add(_list);
            left.Controls.Add(leftButtons);
            left.Dock = DockStyle.Fill;
            left.Name = "left";
            // 
            // right
            // 
            right.Controls.Add(form);
            right.Dock = DockStyle.Fill;
            right.AutoScroll = true;
            right.Name = "right";
            // 
            // _list
            // 
            _list.Dock = DockStyle.Fill;
            _list.IntegralHeight = false;
            _list.FormattingEnabled = true;
            _list.Name = "_list";
            _list.SelectedIndexChanged += List_SelectedIndexChanged;
            // 
            // leftButtons
            // 
            leftButtons.Controls.Add(btnAdd);
            leftButtons.Controls.Add(btnDuplicate);
            leftButtons.Controls.Add(btnDelete);
            leftButtons.Dock = DockStyle.Bottom;
            leftButtons.Height = 34;
            leftButtons.Padding = new Padding(2);
            leftButtons.Name = "leftButtons";
            // 
            // btnAdd
            // 
            btnAdd.Text = "Add";
            btnAdd.AutoSize = true;
            btnAdd.UseVisualStyleBackColor = true;
            btnAdd.Name = "btnAdd";
            btnAdd.Click += btnAdd_Click;
            // 
            // btnDuplicate
            // 
            btnDuplicate.Text = "Duplicate";
            btnDuplicate.AutoSize = true;
            btnDuplicate.UseVisualStyleBackColor = true;
            btnDuplicate.Name = "btnDuplicate";
            btnDuplicate.Click += btnDuplicate_Click;
            // 
            // btnDelete
            // 
            btnDelete.Text = "Delete";
            btnDelete.AutoSize = true;
            btnDelete.UseVisualStyleBackColor = true;
            btnDelete.Name = "btnDelete";
            btnDelete.Click += btnDelete_Click;
            // 
            // form
            // 
            form.Controls.Add(lblId, 0, 0);
            form.Controls.Add(_id, 1, 0);
            form.Controls.Add(lblName, 0, 1);
            form.Controls.Add(_name, 1, 1);
            form.Controls.Add(lblDesc, 0, 2);
            form.Controls.Add(_desc, 1, 2);
            form.Controls.Add(lblKind, 0, 3);
            form.Controls.Add(_kind, 1, 3);
            form.Controls.Add(lblType, 0, 4);
            form.Controls.Add(_type, 1, 4);
            form.Controls.Add(lblAttribute, 0, 5);
            form.Controls.Add(_attribute, 1, 5);
            form.Controls.Add(lblIcon, 0, 6);
            form.Controls.Add(_icon, 1, 6);
            form.Controls.Add(lblLevel, 0, 7);
            form.Controls.Add(_level, 1, 7);
            form.Controls.Add(lblAtk, 0, 8);
            form.Controls.Add(_atk, 1, 8);
            form.Controls.Add(lblDef, 0, 9);
            form.Controls.Add(_def, 1, 9);
            form.Controls.Add(lblLimitation, 0, 10);
            form.Controls.Add(_limitation, 1, 10);
            form.Controls.Add(lblCopies, 0, 11);
            form.Controls.Add(_copies, 1, 11);
            form.Controls.Add(lblArchetypes, 0, 12);
            form.Controls.Add(archRow, 1, 12);
            form.Controls.Add(lblArt, 0, 13);
            form.Controls.Add(artRow, 1, 13);
            form.Controls.Add(_preview, 2, 0);
            form.SetRowSpan(_preview, 8);
            form.Controls.Add(_artInfo, 2, 8);
            form.SetRowSpan(_artInfo, 6);
            form.AutoSize = true;
            form.ColumnCount = 3;
            form.Dock = DockStyle.Top;
            form.Padding = new Padding(8);
            form.RowCount = 14;
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260F));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.Name = "form";
            // 
            // lblId
            // 
            lblId.Text = "Card ID:";
            lblId.AutoSize = true;
            lblId.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblId.Padding = new Padding(0, 6, 8, 0);
            lblId.Name = "lblId";
            // 
            // _id
            // 
            _id.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _id.Margin = new Padding(3);
            _id.Minimum = new decimal(new int[] { 15300, 0, 0, 0 });
            _id.Maximum = new decimal(new int[] { 19999, 0, 0, 0 });
            _id.Value = new decimal(new int[] { 15300, 0, 0, 0 });
            _id.Name = "_id";
            _id.ValueChanged += Editor_Changed;
            // 
            // lblName
            // 
            lblName.Text = "Name:";
            lblName.AutoSize = true;
            lblName.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblName.Padding = new Padding(0, 6, 8, 0);
            lblName.Name = "lblName";
            // 
            // _name
            // 
            _name.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _name.Margin = new Padding(3);
            _name.Name = "_name";
            _name.TextChanged += Editor_Changed;
            // 
            // lblDesc
            // 
            lblDesc.Text = "Description:";
            lblDesc.AutoSize = true;
            lblDesc.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblDesc.Padding = new Padding(0, 6, 8, 0);
            lblDesc.Name = "lblDesc";
            // 
            // _desc
            // 
            _desc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _desc.Margin = new Padding(3);
            _desc.Multiline = true;
            _desc.Height = 90;
            _desc.ScrollBars = ScrollBars.Vertical;
            _desc.Name = "_desc";
            _desc.TextChanged += Editor_Changed;
            // 
            // lblKind
            // 
            lblKind.Text = "Kind:";
            lblKind.AutoSize = true;
            lblKind.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblKind.Padding = new Padding(0, 6, 8, 0);
            lblKind.Name = "lblKind";
            // 
            // _kind
            // 
            _kind.DropDownStyle = ComboBoxStyle.DropDownList;
            _kind.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _kind.FormattingEnabled = true;
            _kind.Margin = new Padding(3);
            _kind.Items.AddRange(new object[] { "Normal", "Effect", "Fusion", "Fusion Effect", "Ritual", "Ritual Effect", "Toon", "Spirit", "Union", "Gemini", "Token", "Spell", "Trap", "Tuner Normal", "Tuner Effect", "Synchro", "Synchro Effect", "Synchro Tuner Effect", "Xyz", "Xyz Effect", "Flip Effect", "Pendulum", "Pendulum Effect", "Special Summoned Effect", "Toon Effect", "Spirit Effect", "Tuner", "Tuner Flip Effect", "Pendulum Tuner Effect", "Xyz Pendulum Effect", "Pendulum Flip Effect", "Synchro Pendulum Effect", "Union Tuner Effect", "Ritual Spirit Effect", "Fusion Tuner", "Pendulum Effect Alt", "Fusion Pendulum Effect", "Link", "Link Effect", "Pendulum Tuner Normal", "Pendulum Spirit Effect" });
            _kind.SelectedIndex = 0;
            _kind.Name = "_kind";
            _kind.SelectedIndexChanged += Editor_Changed;
            // 
            // lblType
            // 
            lblType.Text = "Type:";
            lblType.AutoSize = true;
            lblType.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblType.Padding = new Padding(0, 6, 8, 0);
            lblType.Name = "lblType";
            // 
            // _type
            // 
            _type.DropDownStyle = ComboBoxStyle.DropDownList;
            _type.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _type.FormattingEnabled = true;
            _type.Margin = new Padding(3);
            _type.Items.AddRange(new object[] { "Dragon", "Zombie", "Fiend", "Pyro", "SeaSerpent", "Rock", "Machine", "Fish", "Dinosaur", "Insect", "Beast", "BeastWarrior", "Plant", "Aqua", "Warrior", "WingedBeast", "Fairy", "Spellcaster", "Thunder", "Reptile", "Psychic", "Wyrm", "Cyberse", "DivineBeast", "CreatorGod", "Spell", "Trap" });
            _type.SelectedIndex = 0;
            _type.Name = "_type";
            _type.SelectedIndexChanged += Editor_Changed;
            // 
            // lblAttribute
            // 
            lblAttribute.Text = "Attribute:";
            lblAttribute.AutoSize = true;
            lblAttribute.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblAttribute.Padding = new Padding(0, 6, 8, 0);
            lblAttribute.Name = "lblAttribute";
            // 
            // _attribute
            // 
            _attribute.DropDownStyle = ComboBoxStyle.DropDownList;
            _attribute.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _attribute.FormattingEnabled = true;
            _attribute.Margin = new Padding(3);
            _attribute.Items.AddRange(new object[] { "Special", "Light", "Dark", "Water", "Fire", "Earth", "Wind", "Divine", "Spell", "Trap" });
            _attribute.SelectedIndex = 0;
            _attribute.Name = "_attribute";
            _attribute.SelectedIndexChanged += Editor_Changed;
            // 
            // lblIcon
            // 
            lblIcon.Text = "Icon (Spell/Trap):";
            lblIcon.AutoSize = true;
            lblIcon.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblIcon.Padding = new Padding(0, 6, 8, 0);
            lblIcon.Name = "lblIcon";
            // 
            // _icon
            // 
            _icon.DropDownStyle = ComboBoxStyle.DropDownList;
            _icon.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _icon.FormattingEnabled = true;
            _icon.Margin = new Padding(3);
            _icon.Items.AddRange(new object[] { "Normal", "Counter", "Field", "Equip", "Continuous", "QuickPlay", "Ritual" });
            _icon.SelectedIndex = 0;
            _icon.Name = "_icon";
            _icon.SelectedIndexChanged += Editor_Changed;
            // 
            // lblLevel
            // 
            lblLevel.Text = "Level:";
            lblLevel.AutoSize = true;
            lblLevel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblLevel.Padding = new Padding(0, 6, 8, 0);
            lblLevel.Name = "lblLevel";
            // 
            // _level
            // 
            _level.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _level.Margin = new Padding(3);
            _level.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            _level.Maximum = new decimal(new int[] { 12, 0, 0, 0 });
            _level.Value = new decimal(new int[] { 4, 0, 0, 0 });
            _level.Name = "_level";
            _level.ValueChanged += Editor_Changed;
            // 
            // lblAtk
            // 
            lblAtk.Text = "ATK:";
            lblAtk.AutoSize = true;
            lblAtk.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblAtk.Padding = new Padding(0, 6, 8, 0);
            lblAtk.Name = "lblAtk";
            // 
            // _atk
            // 
            _atk.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _atk.Margin = new Padding(3);
            _atk.Increment = new decimal(new int[] { 100, 0, 0, 0 });
            _atk.Maximum = new decimal(new int[] { 9990, 0, 0, 0 });
            _atk.Name = "_atk";
            _atk.ValueChanged += Editor_Changed;
            // 
            // lblDef
            // 
            lblDef.Text = "DEF:";
            lblDef.AutoSize = true;
            lblDef.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblDef.Padding = new Padding(0, 6, 8, 0);
            lblDef.Name = "lblDef";
            // 
            // _def
            // 
            _def.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _def.Margin = new Padding(3);
            _def.Increment = new decimal(new int[] { 100, 0, 0, 0 });
            _def.Maximum = new decimal(new int[] { 9990, 0, 0, 0 });
            _def.Name = "_def";
            _def.ValueChanged += Editor_Changed;
            // 
            // lblLimitation
            // 
            lblLimitation.Text = "Limitation:";
            lblLimitation.AutoSize = true;
            lblLimitation.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblLimitation.Padding = new Padding(0, 6, 8, 0);
            lblLimitation.Name = "lblLimitation";
            // 
            // _limitation
            // 
            _limitation.DropDownStyle = ComboBoxStyle.DropDownList;
            _limitation.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _limitation.FormattingEnabled = true;
            _limitation.Margin = new Padding(3);
            _limitation.Items.AddRange(new object[] { "Forbidden", "Limited", "SemiLimited", "Unlimited" });
            _limitation.SelectedIndex = 0;
            _limitation.Name = "_limitation";
            _limitation.SelectedIndexChanged += Editor_Changed;
            // 
            // lblCopies
            // 
            lblCopies.Text = "Owned from the start:";
            lblCopies.AutoSize = true;
            lblCopies.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblCopies.Padding = new Padding(0, 6, 8, 0);
            lblCopies.Name = "lblCopies";
            // 
            // _copies
            // 
            _copies.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _copies.Margin = new Padding(3);
            _copies.Maximum = new decimal(new int[] { 3, 0, 0, 0 });
            _copies.Value = new decimal(new int[] { 3, 0, 0, 0 });
            _copies.Name = "_copies";
            _copies.ValueChanged += Editor_Changed;
            // 
            // lblArchetypes
            //
            lblArchetypes.Text = "Archetypes (one or many):";
            lblArchetypes.AutoSize = true;
            lblArchetypes.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblArchetypes.Padding = new Padding(0, 6, 8, 0);
            lblArchetypes.Name = "lblArchetypes";
            //
            // archRow
            //
            archRow.Controls.Add(_archetypes, 0, 0);
            archRow.Controls.Add(btnArchetypes, 1, 0);
            archRow.AutoSize = true;
            archRow.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            archRow.ColumnCount = 2;
            archRow.RowCount = 1;
            archRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            archRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            archRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            archRow.Name = "archRow";
            //
            // _archetypes
            //
            _archetypes.ReadOnly = true;
            _archetypes.Multiline = true;
            _archetypes.WordWrap = true;
            _archetypes.Height = 58;
            _archetypes.ScrollBars = ScrollBars.Vertical;
            _archetypes.Dock = DockStyle.Fill;
            _archetypes.Name = "_archetypes";
            //
            // btnArchetypes
            //
            btnArchetypes.Text = "Choose archetypes...";
            btnArchetypes.AutoSize = true;
            btnArchetypes.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnArchetypes.UseVisualStyleBackColor = true;
            btnArchetypes.Name = "btnArchetypes";
            btnArchetypes.Click += btnArchetypes_Click;
            //
            // lblArt
            // 
            lblArt.Text = "Art (png / jpg):";
            lblArt.AutoSize = true;
            lblArt.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblArt.Padding = new Padding(0, 6, 8, 0);
            lblArt.Name = "lblArt";
            // 
            // artRow
            // 
            artRow.Controls.Add(_artPath, 0, 0);
            artRow.Controls.Add(btnChooseArt, 1, 0);
            artRow.AutoSize = true;
            artRow.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            artRow.ColumnCount = 2;
            artRow.RowCount = 1;
            artRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            artRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            artRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            artRow.Name = "artRow";
            // 
            // _artPath
            // 
            _artPath.ReadOnly = true;
            _artPath.Dock = DockStyle.Fill;
            _artPath.Name = "_artPath";
            // 
            // btnChooseArt
            // 
            btnChooseArt.Text = "Choose art...";
            btnChooseArt.AutoSize = true;
            btnChooseArt.UseVisualStyleBackColor = true;
            btnChooseArt.Name = "btnChooseArt";
            btnChooseArt.Click += btnChooseArt_Click;
            // 
            // _preview
            // 
            _preview.SizeMode = PictureBoxSizeMode.Zoom;
            _preview.BorderStyle = BorderStyle.FixedSingle;
            _preview.Size = new Size(244, 244);
            _preview.Margin = new Padding(3);
            _preview.TabStop = false;
            _preview.Name = "_preview";
            // 
            // _artInfo
            // 
            _artInfo.AutoSize = true;
            _artInfo.MaximumSize = new Size(244, 0);
            _artInfo.Name = "_artInfo";
            // 
            // this
            // 
            Controls.Add(split);
            AutoScaleMode = AutoScaleMode.Font;
            Name = "CardsPanel";
            Size = new Size(1000, 640);
            split.Panel1.ResumeLayout(false);
            split.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)split).EndInit();
            split.ResumeLayout(false);
            archRow.ResumeLayout(false);
            archRow.PerformLayout();
            artRow.ResumeLayout(false);
            artRow.PerformLayout();
            form.ResumeLayout(false);
            form.PerformLayout();
            leftButtons.ResumeLayout(false);
            leftButtons.PerformLayout();
            right.ResumeLayout(false);
            right.PerformLayout();
            left.ResumeLayout(false);
            left.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private SplitContainer split;
        private Panel left;
        private Panel right;
        private ListBox _list;
        private FlowLayoutPanel leftButtons;
        private Button btnAdd;
        private Button btnDuplicate;
        private Button btnDelete;
        private TableLayoutPanel form;
        private Label lblId;
        private NumericUpDown _id;
        private Label lblName;
        private TextBox _name;
        private Label lblDesc;
        private TextBox _desc;
        private Label lblKind;
        private ComboBox _kind;
        private Label lblType;
        private ComboBox _type;
        private Label lblAttribute;
        private ComboBox _attribute;
        private Label lblIcon;
        private ComboBox _icon;
        private Label lblLevel;
        private NumericUpDown _level;
        private Label lblAtk;
        private NumericUpDown _atk;
        private Label lblDef;
        private NumericUpDown _def;
        private Label lblLimitation;
        private ComboBox _limitation;
        private Label lblCopies;
        private NumericUpDown _copies;
        private Label lblArchetypes;
        private TableLayoutPanel archRow;
        private TextBox _archetypes;
        private Button btnArchetypes;
        private Label lblArt;
        private TableLayoutPanel artRow;
        private TextBox _artPath;
        private Button btnChooseArt;
        private PictureBox _preview;
        private Label _artInfo;
    }
}
