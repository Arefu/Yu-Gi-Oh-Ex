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
            ANIMS_Picture_Container = new Panel();
            ANIMS_Picture = new PictureBox();
            ANIMS_Picture_Container.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)ANIMS_Picture).BeginInit();
            SuspendLayout();
            // 
            // ANIMS_Picture_Container
            // 
            ANIMS_Picture_Container.AutoScroll = true;
            ANIMS_Picture_Container.BackColor = Color.Lime;
            ANIMS_Picture_Container.Controls.Add(ANIMS_Picture);
            ANIMS_Picture_Container.Dock = DockStyle.Fill;
            ANIMS_Picture_Container.Location = new Point(0, 0);
            ANIMS_Picture_Container.Name = "ANIMS_Picture_Container";
            ANIMS_Picture_Container.Size = new Size(1264, 681);
            ANIMS_Picture_Container.TabIndex = 1;
            // 
            // ANIMS_Picture
            // 
            ANIMS_Picture.BackColor = Color.Transparent;
            ANIMS_Picture.Location = new Point(12, 12);
            ANIMS_Picture.Name = "ANIMS_Picture";
            ANIMS_Picture.Size = new Size(64, 64);
            ANIMS_Picture.SizeMode = PictureBoxSizeMode.AutoSize;
            ANIMS_Picture.TabIndex = 0;
            ANIMS_Picture.TabStop = false;
            ANIMS_Picture.Paint += ANIMS_Picture_Paint;
            // 
            // AnimlistUI
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScroll = true;
            AutoSize = true;
            ClientSize = new Size(1264, 681);
            Controls.Add(ANIMS_Picture_Container);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "AnimlistUI";
            ShowInTaskbar = false;
            Text = "Animlist Image Editor";
            ANIMS_Picture_Container.ResumeLayout(false);
            ANIMS_Picture_Container.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)ANIMS_Picture).EndInit();
            ResumeLayout(false);
        }

        #endregion
        private static string _SceneLocation;
        private Panel ANIMS_Picture_Container;
        public PictureBox ANIMS_Picture;

        public List<Animlist_Item> Anim_Items { get; set; }
        public string SceneLocation { get; set; }
    }
}