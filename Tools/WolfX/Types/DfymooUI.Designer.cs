using Types;

namespace WolfX.Types
{
    partial class DfymooUI
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        public List<Dfymoo_Item> DFY_Items { get; set; }
        public Dfymoo_Item DFY_Item { get; set; }


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
            DFY_Picture_Container = new Panel();
            DFY_Picture = new PictureBox();
            DFY_Picture_Container.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)DFY_Picture).BeginInit();
            SuspendLayout();
            // 
            // DFY_Picture_Container
            // 
            DFY_Picture_Container.AutoScroll = true;
            DFY_Picture_Container.BackColor = Color.Lime;
            DFY_Picture_Container.Controls.Add(DFY_Picture);
            DFY_Picture_Container.Dock = DockStyle.Fill;
            DFY_Picture_Container.Location = new Point(0, 0);
            DFY_Picture_Container.Name = "DFY_Picture_Container";
            DFY_Picture_Container.Size = new Size(1264, 681);
            DFY_Picture_Container.TabIndex = 0;
            // 
            // DFY_Picture
            // 
            DFY_Picture.Location = new Point(12, 12);
            DFY_Picture.Name = "DFY_Picture";
            DFY_Picture.Size = new Size(64, 64);
            DFY_Picture.SizeMode = PictureBoxSizeMode.AutoSize;
            DFY_Picture.TabIndex = 0;
            DFY_Picture.TabStop = false;
            DFY_Picture.Paint += DFY_Picture_Paint;
            // 
            // DfymooUI
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScroll = true;
            ClientSize = new Size(1264, 681);
            Controls.Add(DFY_Picture_Container);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "DfymooUI";
            Text = "Dfymoo Image Editor";
            DFY_Picture_Container.ResumeLayout(false);
            DFY_Picture_Container.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)DFY_Picture).EndInit();
            ResumeLayout(false);
        }

        #endregion

        public String _PicturePath;
        private Panel DFY_Picture_Container;
        public PictureBox DFY_Picture;
        private Rectangle _Rect;
        private Point _MovingStart;
        private Boolean _MovingBox;
    }
}