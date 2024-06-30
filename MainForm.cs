using MedRePar.Services;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
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

                // Print table structure and data for debugging
                DatabaseService.PrintTableStructure(dbPath);
                DatabaseService.PrintAllTableData(dbPath);

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
                    Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
                    Multiselect = true
                };

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string runId = Guid.NewGuid().ToString(); // Generate a unique run ID for this upload session

                    loadingLabel.Visible = true;
                    progressBar.Value = 0;

                    int fileCount = openFileDialog.FileNames.Length;
                    int currentFile = 0;
                    foreach (string filePath in openFileDialog.FileNames)
                    {
                        string extractedText = PdfService.ExtractTextFromPdf(filePath);
                        LoggingService.LogInfo($"PDF text extracted successfully from {filePath}. And the content is: \n {extractedText}");

                        Dictionary<string, string> normalizedData = await OpenAiService.NormalizeParametersUsingOpenAI(selectedModel, extractedText);
                        LoggingService.LogInfo("Parameters normalized successfully.");
                        LoggingService.LogDictionary("Normalized Data", normalizedData);

                        DatabaseService.StoreData(dbPath, normalizedData, DateTime.Now.ToString("yyyy-MM-dd"), runId);
                        LoggingService.LogInfo($"PDF data from {filePath} stored in database successfully.");

                        currentFile++;
                        progressBar.Value = (int)((currentFile / (double)fileCount) * 100);
                    }

                    loadingLabel.Visible = false;
                    MessageBox.Show("All PDF data extracted and stored successfully.");

                    // Print table data after storing for verification
                    DatabaseService.PrintAllTableData(dbPath);
                }
            }
            catch (Exception ex)
            {
                loadingLabel.Visible = false;
                LoggingService.LogError("Error during uploadButton_Click", ex);
                MessageBox.Show("An error occurred while processing the PDFs. Please check the log file for more details.");
            }
        }

        private async void trendButton_Click(object sender, EventArgs e)
        {
            try
            {
                loadingLabel.Visible = true;
                progressBar.Value = 0;

                // Get the latest run ID
                string runId = GetLatestRunId();

                // Get all parameters for the latest run
                List<string> parameters = GetParametersForRunId(runId);

                // Update progress bar increment value based on the number of parameters
                int progressIncrement = 100 / parameters.Count;

                List<string> imagePaths = new List<string>(); // List to store image paths

                await Task.Run(() =>
                {
                    foreach (var parameter in parameters)
                    {
                        string imagePath = ChartService.GenerateTrendChart(dbPath, parameter, chart, runId);
                        if (!string.IsNullOrEmpty(imagePath))
                        {
                            imagePaths.Add(imagePath);
                        }
                        this.Invoke(new Action(() =>
                        {
                            progressBar.Value += progressIncrement;
                        }));
                    }
                });

                // Save all images to a single PDF
                string pdfPath = ChartService.SaveImagesAsPdf(imagePaths);
                ChartService.OpenPdf(pdfPath);

                progressBar.Value = 100;
                loadingLabel.Visible = false;
                LoggingService.LogInfo("Trend charts generated and saved successfully.");
            }
            catch (Exception ex)
            {
                loadingLabel.Visible = false;
                LoggingService.LogError("Error during trendButton_Click", ex);
                MessageBox.Show("An error occurred while generating the trend charts. Please check the log file for more details.");
            }
        }

        private List<string> GetParametersForRunId(string runId)
        {
            List<string> parameters = new List<string>();

            using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
            {
                conn.Open();
                string sql = @"
            SELECT DISTINCT parameters.name
            FROM medical_data
            INNER JOIN parameters ON medical_data.parameter_id = parameters.id
            WHERE medical_data.run_id = @run_id";

                SQLiteCommand command = new SQLiteCommand(sql, conn);
                command.Parameters.AddWithValue("@run_id", runId);

                SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    parameters.Add(reader["name"].ToString());
                }
            }

            return parameters;
        }

        private string GetLatestRunId()
        {
            using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
            {
                conn.Open();
                string sql = "SELECT run_id FROM medical_data ORDER BY date DESC LIMIT 1";
                SQLiteCommand command = new SQLiteCommand(sql, conn);
                return command.ExecuteScalar()?.ToString();
            }
        }
    }
}
