using MedRePar.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MedRePar
{
    public partial class MainForm : Form
    {
        private List<AIModelConfig> aiModels;
        private AIModelConfig selectedModel;
        private string dbPath = "medical_data.db";

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            try
            {
                // Initialize the database
                DatabaseService.InitializeDb(dbPath);
                LoggingService.LogInfo("Database initialized successfully.");

                // Load AI models from App.config
                aiModels = AIModelConfig.LoadAIModels();
                LoggingService.LogInfo("AI models loaded successfully.");

                // Populate modelComboBox with AI model names
                modelComboBox.Items.AddRange(aiModels.Select(m => m.Name).ToArray());
                if (modelComboBox.Items.Count > 0)
                {
                    modelComboBox.SelectedIndex = 0; // Select the first model by default
                    selectedModel = aiModels[0];
                    LoggingService.LogInfo($"Default AI model selected: {selectedModel.Name}");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error during MainForm_Load", ex);
                MessageBox.Show("An error occurred while loading the form. Please check the log file for more details.");
            }
        }

        private void modelComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                // Update the selected model based on ComboBox selection
                string selectedModelName = modelComboBox.SelectedItem.ToString();
                selectedModel = aiModels.FirstOrDefault(m => m.Name == selectedModelName);
                LoggingService.LogInfo($"Selected AI model: {selectedModel.Name}");
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error during modelComboBox_SelectedIndexChanged", ex);
                MessageBox.Show("An error occurred while selecting the AI model. Please check the log file for more details.");
            }
        }

        private async void uploadButton_Click(object sender, EventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*"
                };

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = openFileDialog.FileName;
                    string extractedText = PdfService.ExtractTextFromPdf(filePath);
                    LoggingService.LogInfo("PDF text extracted successfully.");

                    string responseText = await OpenAiService.NormalizeParametersUsingOpenAI(
                        selectedModel.ApiKey, selectedModel.Url, extractedText, selectedModel.Name);
                    var normalizedData = NormalizationService.ParseNormalizedData(responseText);
                    LoggingService.LogInfo("Parameters normalized successfully.");

                    DatabaseService.StoreData(dbPath, normalizedData, DateTime.Now.ToString("yyyy-MM-dd"));
                    MessageBox.Show("PDF data extracted and stored successfully.");
                    LoggingService.LogInfo("PDF data stored in database successfully.");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error during uploadButton_Click", ex);
                MessageBox.Show("An error occurred while processing the PDF. Please check the log file for more details.");
            }
        }

        private void trendButton_Click(object sender, EventArgs e)
        {
            try
            {
                ChartService.GenerateTrendChart(dbPath, "Total Cholesterol", chart);
                LoggingService.LogInfo("Trend chart generated successfully.");
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error during trendButton_Click", ex);
                MessageBox.Show("An error occurred while generating the trend chart. Please check the log file for more details.");
            }
        }
    }
}
