using Types;

namespace WolfX.Types
{
    partial class AnimlistUI
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
            PNL_AnimList_Container = new Panel();
            label1 = new Label();
            numericUpDown1 = new NumericUpDown();
            label2 = new Label();
            numericUpDown3 = new NumericUpDown();
            label3 = new Label();
            numericUpDown2 = new NumericUpDown();
            groupBox1 = new GroupBox();
            groupBox2 = new GroupBox();
            button2 = new Button();
            label4 = new Label();
            button1 = new Button();
            radioButton2 = new RadioButton();
            radioButton1 = new RadioButton();
            ((System.ComponentModel.ISupportInitialize)numericUpDown1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown3).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown2).BeginInit();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            SuspendLayout();
            // 
            // PNL_AnimList_Container
            // 
            PNL_AnimList_Container.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            PNL_AnimList_Container.AutoScroll = true;
            PNL_AnimList_Container.BackColor = Color.Transparent;
            PNL_AnimList_Container.Location = new Point(12, 12);
            PNL_AnimList_Container.Name = "PNL_AnimList_Container";
            PNL_AnimList_Container.Size = new Size(1560, 720);
            PNL_AnimList_Container.TabIndex = 3;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(6, 19);
            label1.Name = "label1";
            label1.Size = new Size(55, 15);
            label1.TabIndex = 0;
            label1.Text = "Object X:";
            // 
            // numericUpDown1
            // 
            numericUpDown1.Location = new Point(85, 46);
            numericUpDown1.Maximum = new decimal(new int[] { 1080, 0, 0, 0 });
            numericUpDown1.Minimum = new decimal(new int[] { 1080, 0, 0, int.MinValue });
            numericUpDown1.Name = "numericUpDown1";
            numericUpDown1.Size = new Size(48, 23);
            numericUpDown1.TabIndex = 0;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(6, 48);
            label2.Name = "label2";
            label2.Size = new Size(55, 15);
            label2.TabIndex = 1;
            label2.Text = "Object Y:";
            // 
            // numericUpDown3
            // 
            numericUpDown3.Location = new Point(85, 17);
            numericUpDown3.Maximum = new decimal(new int[] { 1920, 0, 0, 0 });
            numericUpDown3.Minimum = new decimal(new int[] { 1920, 0, 0, int.MinValue });
            numericUpDown3.Name = "numericUpDown3";
            numericUpDown3.Size = new Size(48, 23);
            numericUpDown3.TabIndex = 2;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(6, 77);
            label3.Name = "label3";
            label3.Size = new Size(73, 15);
            label3.TabIndex = 2;
            label3.Text = "Object Slide:";
            // 
            // numericUpDown2
            // 
            numericUpDown2.Location = new Point(85, 75);
            numericUpDown2.Maximum = new decimal(new int[] { 2160, 0, 0, 0 });
            numericUpDown2.Minimum = new decimal(new int[] { 2160, 0, 0, int.MinValue });
            numericUpDown2.Name = "numericUpDown2";
            numericUpDown2.Size = new Size(48, 23);
            numericUpDown2.TabIndex = 1;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(numericUpDown2);
            groupBox1.Controls.Add(label3);
            groupBox1.Controls.Add(numericUpDown3);
            groupBox1.Controls.Add(label2);
            groupBox1.Controls.Add(numericUpDown1);
            groupBox1.Controls.Add(label1);
            groupBox1.Location = new Point(12, 738);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(172, 111);
            groupBox1.TabIndex = 4;
            groupBox1.TabStop = false;
            groupBox1.Text = "Scene Object Information";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(button2);
            groupBox2.Controls.Add(label4);
            groupBox2.Controls.Add(button1);
            groupBox2.Controls.Add(radioButton2);
            groupBox2.Controls.Add(radioButton1);
            groupBox2.Location = new Point(190, 738);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(172, 111);
            groupBox2.TabIndex = 5;
            groupBox2.TabStop = false;
            groupBox2.Text = "Scene Information";
            // 
            // button2
            // 
            button2.Location = new Point(6, 75);
            button2.Name = "button2";
            button2.Size = new Size(106, 23);
            button2.TabIndex = 7;
            button2.Text = "Remove Object";
            button2.UseVisualStyleBackColor = true;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(6, 19);
            label4.Name = "label4";
            label4.Size = new Size(58, 15);
            label4.TabIndex = 2;
            label4.Text = "Title Side:";
            // 
            // button1
            // 
            button1.Location = new Point(6, 46);
            button1.Name = "button1";
            button1.Size = new Size(106, 23);
            button1.TabIndex = 6;
            button1.Text = "Add Object";
            button1.UseVisualStyleBackColor = true;
            // 
            // radioButton2
            // 
            radioButton2.AutoSize = true;
            radioButton2.Location = new Point(118, 17);
            radioButton2.Name = "radioButton2";
            radioButton2.Size = new Size(53, 19);
            radioButton2.TabIndex = 1;
            radioButton2.TabStop = true;
            radioButton2.Text = "Right";
            radioButton2.UseVisualStyleBackColor = true;
            // 
            // radioButton1
            // 
            radioButton1.AutoSize = true;
            radioButton1.Location = new Point(67, 17);
            radioButton1.Name = "radioButton1";
            radioButton1.Size = new Size(45, 19);
            radioButton1.TabIndex = 0;
            radioButton1.TabStop = true;
            radioButton1.Text = "Left";
            radioButton1.UseVisualStyleBackColor = true;
            // 
            // AnimlistUI
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScroll = true;
            AutoSize = true;
            BackColor = SystemColors.Control;
            ClientSize = new Size(1584, 861);
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            Controls.Add(PNL_AnimList_Container);
            DoubleBuffered = true;
            MinimizeBox = false;
            Name = "AnimlistUI";
            ShowInTaskbar = false;
            Text = "Animlist Image Editor";
            ((System.ComponentModel.ISupportInitialize)numericUpDown1).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown3).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown2).EndInit();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ResumeLayout(false);
        }

        #endregion
        private static string _SceneLocation;
        private Panel PNL_AnimList_Container;
        private Label label1;
        private NumericUpDown numericUpDown1;
        private Label label2;
        private NumericUpDown numericUpDown3;
        private Label label3;
        private NumericUpDown numericUpDown2;
        private GroupBox groupBox1;
        private GroupBox groupBox2;
        private Label label4;
        private RadioButton radioButton2;
        private RadioButton radioButton1;
        private Button button2;
        private Button button1;

        public List<Animlist_Item> Anim_Items { get; set; }
        public string SceneLocation { get; set; }
    }
}