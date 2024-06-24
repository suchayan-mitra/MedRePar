namespace MedRePar
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.ComboBox modelComboBox;
        private System.Windows.Forms.Button uploadButton;
        private System.Windows.Forms.Button trendButton;
        private System.Windows.Forms.DataVisualization.Charting.Chart chart;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.modelComboBox = new System.Windows.Forms.ComboBox();
            this.uploadButton = new System.Windows.Forms.Button();
            this.trendButton = new System.Windows.Forms.Button();
            this.chart = new System.Windows.Forms.DataVisualization.Charting.Chart();
            ((System.ComponentModel.ISupportInitialize)(this.chart)).BeginInit();
            this.SuspendLayout();
            // 
            // modelComboBox
            // 
            this.modelComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.modelComboBox.FormattingEnabled = true;
            this.modelComboBox.Location = new System.Drawing.Point(12, 12);
            this.modelComboBox.Name = "modelComboBox";
            this.modelComboBox.Size = new System.Drawing.Size(300, 21);
            this.modelComboBox.TabIndex = 0;
            this.modelComboBox.SelectedIndexChanged += new System.EventHandler(this.modelComboBox_SelectedIndexChanged);
            // 
            // uploadButton
            // 
            this.uploadButton.Location = new System.Drawing.Point(12, 39);
            this.uploadButton.Name = "uploadButton";
            this.uploadButton.Size = new System.Drawing.Size(100, 23);
            this.uploadButton.TabIndex = 1;
            this.uploadButton.Text = "Upload PDF";
            this.uploadButton.UseVisualStyleBackColor = true;
            this.uploadButton.Click += new System.EventHandler(this.uploadButton_Click);
            // 
            // trendButton
            // 
            this.trendButton.Location = new System.Drawing.Point(12, 68);
            this.trendButton.Name = "trendButton";
            this.trendButton.Size = new System.Drawing.Size(150, 23);
            this.trendButton.TabIndex = 2;
            this.trendButton.Text = "Generate Trend Chart";
            this.trendButton.UseVisualStyleBackColor = true;
            this.trendButton.Click += new System.EventHandler(this.trendButton_Click);
            // 
            // chart
            // 
            this.chart.Anchor = System.Windows.Forms.AnchorStyles.Top |
                                System.Windows.Forms.AnchorStyles.Bottom |
                                System.Windows.Forms.AnchorStyles.Left |
                                System.Windows.Forms.AnchorStyles.Right;

            this.chart.Location = new System.Drawing.Point(12, 97);
            this.chart.Name = "chart";
            this.chart.Size = new System.Drawing.Size(760, 453);
            this.chart.TabIndex = 3;
            this.chart.Text = "chart";
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 561);
            this.Controls.Add(this.chart);
            this.Controls.Add(this.trendButton);
            this.Controls.Add(this.uploadButton);
            this.Controls.Add(this.modelComboBox);
            this.Name = "MainForm";
            this.Text = "Medical Report Parser";
            this.Load += new System.EventHandler(this.MainForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.chart)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
