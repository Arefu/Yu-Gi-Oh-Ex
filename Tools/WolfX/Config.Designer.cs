namespace WolfX.WolfX.File_Type_UI
{
    partial class Config
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
            groupBox1 = new GroupBox();
            UseJP_CheckBox = new CheckBox();
            AutoPause_CheckBox = new CheckBox();
            NoBan_CheckBox = new CheckBox();
            FreeStore_CheckBox = new CheckBox();
            groupBox2 = new GroupBox();
            AutoLoadPlugins_CheckBox = new CheckBox();
            label1 = new Label();
            PluginPath_TextBox = new TextBox();
            groupBox3 = new GroupBox();
            label2 = new Label();
            Archive_TextBox = new TextBox();
            BTN_CONFIG_OnSave = new Button();
            AllowMultiInstance_CB = new CheckBox();
            DisableJanken_CB = new CheckBox();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox3.SuspendLayout();
            SuspendLayout();
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(DisableJanken_CB);
            groupBox1.Controls.Add(UseJP_CheckBox);
            groupBox1.Controls.Add(AutoPause_CheckBox);
            groupBox1.Controls.Add(NoBan_CheckBox);
            groupBox1.Controls.Add(FreeStore_CheckBox);
            groupBox1.Location = new Point(12, 12);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(151, 160);
            groupBox1.TabIndex = 0;
            groupBox1.TabStop = false;
            groupBox1.Text = "Patch Me Out";
            // 
            // UseJP_CheckBox
            // 
            UseJP_CheckBox.AutoSize = true;
            UseJP_CheckBox.Location = new Point(6, 97);
            UseJP_CheckBox.Name = "UseJP_CheckBox";
            UseJP_CheckBox.Size = new Size(122, 19);
            UseJP_CheckBox.TabIndex = 3;
            UseJP_CheckBox.Text = "Use JP Title Screen";
            UseJP_CheckBox.UseVisualStyleBackColor = true;
            // 
            // AutoPause_CheckBox
            // 
            AutoPause_CheckBox.AutoSize = true;
            AutoPause_CheckBox.Location = new Point(6, 72);
            AutoPause_CheckBox.Name = "AutoPause_CheckBox";
            AutoPause_CheckBox.Size = new Size(86, 19);
            AutoPause_CheckBox.TabIndex = 2;
            AutoPause_CheckBox.Text = "Auto Pause";
            AutoPause_CheckBox.UseVisualStyleBackColor = true;
            // 
            // NoBan_CheckBox
            // 
            NoBan_CheckBox.AutoSize = true;
            NoBan_CheckBox.Location = new Point(6, 47);
            NoBan_CheckBox.Name = "NoBan_CheckBox";
            NoBan_CheckBox.Size = new Size(92, 19);
            NoBan_CheckBox.TabIndex = 1;
            NoBan_CheckBox.Text = "No PDLimits";
            NoBan_CheckBox.UseVisualStyleBackColor = true;
            // 
            // FreeStore_CheckBox
            // 
            FreeStore_CheckBox.AutoSize = true;
            FreeStore_CheckBox.Location = new Point(6, 22);
            FreeStore_CheckBox.Name = "FreeStore_CheckBox";
            FreeStore_CheckBox.Size = new Size(78, 19);
            FreeStore_CheckBox.TabIndex = 0;
            FreeStore_CheckBox.Text = "Free Store";
            FreeStore_CheckBox.UseVisualStyleBackColor = true;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(AutoLoadPlugins_CheckBox);
            groupBox2.Controls.Add(label1);
            groupBox2.Controls.Add(PluginPath_TextBox);
            groupBox2.Location = new Point(169, 12);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(151, 160);
            groupBox2.TabIndex = 1;
            groupBox2.TabStop = false;
            groupBox2.Text = "GUI";
            // 
            // AutoLoadPlugins_CheckBox
            // 
            AutoLoadPlugins_CheckBox.AutoSize = true;
            AutoLoadPlugins_CheckBox.Location = new Point(6, 72);
            AutoLoadPlugins_CheckBox.Name = "AutoLoadPlugins_CheckBox";
            AutoLoadPlugins_CheckBox.Size = new Size(123, 19);
            AutoLoadPlugins_CheckBox.TabIndex = 2;
            AutoLoadPlugins_CheckBox.Text = "Auto Load Plugins";
            AutoLoadPlugins_CheckBox.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(6, 19);
            label1.Name = "label1";
            label1.Size = new Size(71, 15);
            label1.TabIndex = 1;
            label1.Text = "Plugin Path:";
            // 
            // PluginPath_TextBox
            // 
            PluginPath_TextBox.Location = new Point(6, 37);
            PluginPath_TextBox.Name = "PluginPath_TextBox";
            PluginPath_TextBox.Size = new Size(100, 23);
            PluginPath_TextBox.TabIndex = 0;
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(AllowMultiInstance_CB);
            groupBox3.Controls.Add(label2);
            groupBox3.Controls.Add(Archive_TextBox);
            groupBox3.Location = new Point(326, 12);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(151, 160);
            groupBox3.TabIndex = 3;
            groupBox3.TabStop = false;
            groupBox3.Text = "Better Load";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(6, 19);
            label2.Name = "label2";
            label2.Size = new Size(82, 15);
            label2.TabIndex = 1;
            label2.Text = "Archive Name";
            // 
            // Archive_TextBox
            // 
            Archive_TextBox.Location = new Point(6, 37);
            Archive_TextBox.Name = "Archive_TextBox";
            Archive_TextBox.Size = new Size(100, 23);
            Archive_TextBox.TabIndex = 0;
            // 
            // BTN_CONFIG_OnSave
            // 
            BTN_CONFIG_OnSave.Location = new Point(398, 178);
            BTN_CONFIG_OnSave.Name = "BTN_CONFIG_OnSave";
            BTN_CONFIG_OnSave.Size = new Size(75, 23);
            BTN_CONFIG_OnSave.TabIndex = 4;
            BTN_CONFIG_OnSave.Text = "Save";
            BTN_CONFIG_OnSave.UseVisualStyleBackColor = true;
            BTN_CONFIG_OnSave.Click += BTN_CONFIG_OnSave_Click;
            // 
            // AllowMultiInstance_CB
            // 
            AllowMultiInstance_CB.AutoSize = true;
            AllowMultiInstance_CB.Location = new Point(6, 66);
            AllowMultiInstance_CB.Name = "AllowMultiInstance_CB";
            AllowMultiInstance_CB.Size = new Size(139, 19);
            AllowMultiInstance_CB.TabIndex = 2;
            AllowMultiInstance_CB.Text = "Allow Multi Instances";
            AllowMultiInstance_CB.UseVisualStyleBackColor = true;
            // 
            // DisableJanken_CB
            // 
            DisableJanken_CB.AutoSize = true;
            DisableJanken_CB.Location = new Point(6, 122);
            DisableJanken_CB.Name = "DisableJanken_CB";
            DisableJanken_CB.Size = new Size(103, 19);
            DisableJanken_CB.TabIndex = 4;
            DisableJanken_CB.Text = "Disable Janken";
            DisableJanken_CB.UseVisualStyleBackColor = true;
            // 
            // Config
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(485, 206);
            Controls.Add(BTN_CONFIG_OnSave);
            Controls.Add(groupBox3);
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "Config";
            Text = "Config Editor";
            Load += Config_Load;
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private GroupBox groupBox1;
        private CheckBox UseJP_CheckBox;
        private CheckBox AutoPause_CheckBox;
        private CheckBox NoBan_CheckBox;
        private CheckBox FreeStore_CheckBox;
        private GroupBox groupBox2;
        private Label label1;
        private TextBox PluginPath_TextBox;
        private CheckBox AutoLoadPlugins_CheckBox;
        private GroupBox groupBox3;
        private Label label2;
        private TextBox Archive_TextBox;
        private Button BTN_CONFIG_OnSave;
        private CheckBox DisableJanken_CB;
        private CheckBox AllowMultiInstance_CB;
    }
}