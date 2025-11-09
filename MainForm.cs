using MedRePar.Services;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MedRePar
{
    public partial class MainForm : Form
    {
        private List<AIModelConfig> aiModels;
        private AIModelConfig? selectedModel;
        private string dbPath = "medical_data.db";

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            try
            {
                LoggingService.LogInfo("=== Application Starting ===");

                // Initialize the database
                DatabaseService.InitializeDb(dbPath);
                LoggingService.LogInfo("Database initialized successfully.");

                // Print table structure and data for debugging
                DatabaseService.PrintTableStructure(dbPath);
                DatabaseService.PrintAllTableData(dbPath);

                // Load AI models from App.config
                aiModels = AIModelConfig.LoadAIModels();
                LoggingService.LogInfo($"Loaded {aiModels.Count} AI model(s) from configuration.");

                // Populate modelComboBox with AI model names
                if (aiModels.Count > 0)
                {
                    modelComboBox.Items.AddRange(aiModels.Select(m => m.Name).ToArray());
                    modelComboBox.SelectedIndex = 0; // Select the first model by default
                    selectedModel = aiModels[0];
                    LoggingService.LogInfo($"Default AI model selected: {selectedModel.Name}");
                }
                else
                {
                    LoggingService.LogWarning("No AI models configured in App.config!");
                    MessageBox.Show(
                        "No AI models are configured. Please check App.config and ensure at least one AI model is configured.",
                        "Configuration Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                // Initialize Azure Document Intelligence OCR client (optional)
                try
                {
                    var ocrEndpoint = ConfigurationManager.AppSettings["AzureDocumentIntelligenceEndpoint"];
                    var ocrApiKey = ConfigurationManager.AppSettings["AzureDocumentIntelligenceApiKey"];

                    if (!string.IsNullOrWhiteSpace(ocrEndpoint) && !string.IsNullOrWhiteSpace(ocrApiKey))
                    {
                        PdfService.InitializeOcrClient(ocrEndpoint, ocrApiKey);
                        LoggingService.LogInfo("Azure Document Intelligence OCR enabled.");
                    }
                    else
                    {
                        LoggingService.LogWarning("Azure Document Intelligence credentials not found in App.config. OCR will be limited to embedded text extraction.");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning($"Failed to initialize OCR client: {ex.Message}. Continuing with embedded text extraction only.");
                }

                LoggingService.LogInfo("=== Application initialized successfully ===");
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error during MainForm_Load", ex);
                MessageBox.Show(
                    $"An error occurred while loading the application:\n\n{ex.Message}\n\nPlease check the log file for more details.",
                    "Application Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void modelComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                // Update the selected model based on ComboBox selection
                if (modelComboBox.SelectedItem != null)
                {
                    string selectedModelName = modelComboBox.SelectedItem.ToString()!;
                    selectedModel = aiModels.FirstOrDefault(m => m.Name == selectedModelName);

                    if (selectedModel != null)
                    {
                        LoggingService.LogInfo($"Selected AI model: {selectedModel.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error during modelComboBox_SelectedIndexChanged", ex);
                MessageBox.Show(
                    "An error occurred while selecting the AI model. Please check the log file for more details.",
                    "Selection Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private async void uploadButton_Click(object sender, EventArgs e)
        {
            if (selectedModel == null)
            {
                MessageBox.Show(
                    "Please select an AI model before uploading PDFs.",
                    "No Model Selected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
                    Multiselect = true,
                    Title = "Select Medical Report PDFs"
                };

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    // Validate file selection
                    if (openFileDialog.FileNames.Length == 0)
                    {
                        return;
                    }

                    // Validate all files are PDFs and accessible
                    List<string> invalidFiles = new List<string>();
                    foreach (string filePath in openFileDialog.FileNames)
                    {
                        if (!PdfService.IsValidPdf(filePath))
                        {
                            invalidFiles.Add(Path.GetFileName(filePath));
                        }
                    }

                    if (invalidFiles.Count > 0)
                    {
                        MessageBox.Show(
                            $"The following files are not valid PDFs:\n\n{string.Join("\n", invalidFiles)}\n\nPlease select only valid PDF files.",
                            "Invalid Files",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    string runId = Guid.NewGuid().ToString(); // Generate a unique run ID for this upload session
                    LoggingService.LogInfo($"Starting new upload session with Run ID: {runId}");

                    // Disable upload button during processing
                    uploadButton.Enabled = false;
                    trendButton.Enabled = false;

                    loadingLabel.Visible = true;
                    loadingLabel.Text = "Processing PDFs...";
                    progressBar.Value = 0;

                    int fileCount = openFileDialog.FileNames.Length;
                    int currentFile = 0;
                    int successCount = 0;
                    List<string> failedFiles = new List<string>();

                    foreach (string filePath in openFileDialog.FileNames)
                    {
                        try
                        {
                            string fileName = Path.GetFileName(filePath);
                            loadingLabel.Text = $"Processing {currentFile + 1}/{fileCount}: {fileName}";
                            LoggingService.LogInfo($"Processing file {currentFile + 1}/{fileCount}: {filePath}");

                            // Extract text from PDF (with intelligent OCR fallback)
                            string extractedText = await PdfService.ExtractTextFromPdfAsync(filePath);

                            if (string.IsNullOrWhiteSpace(extractedText))
                            {
                                throw new InvalidOperationException("No text could be extracted from the PDF. The file may be empty or corrupted.");
                            }

                            LoggingService.LogInfo($"Extracted {extractedText.Length} characters from {fileName}");

                            // Normalize parameters using AI
                            Dictionary<string, string> normalizedData = await OpenAiService.NormalizeParametersUsingOpenAI(selectedModel, extractedText);
                            LoggingService.LogInfo("Parameters normalized successfully.");
                            LoggingService.LogDictionary("Normalized Data", normalizedData);

                            // Store in database
                            DatabaseService.StoreData(dbPath, normalizedData, DateTime.Now.ToString("yyyy-MM-dd"), runId);
                            LoggingService.LogInfo($"Data from {fileName} stored successfully.");

                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            string fileName = Path.GetFileName(filePath);
                            failedFiles.Add($"{fileName}: {ex.Message}");
                            LoggingService.LogError($"Failed to process {filePath}", ex);
                        }

                        currentFile++;
                        progressBar.Value = (int)((currentFile / (double)fileCount) * 100);
                    }

                    loadingLabel.Visible = false;
                    progressBar.Value = 100;

                    // Show results
                    if (successCount == fileCount)
                    {
                        MessageBox.Show(
                            $"Successfully processed all {fileCount} PDF(s).\n\nRun ID: {runId}\n\nYou can now generate trend charts.",
                            "Success",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    else if (successCount > 0)
                    {
                        MessageBox.Show(
                            $"Processed {successCount} out of {fileCount} PDF(s).\n\nFailed files:\n{string.Join("\n", failedFiles)}\n\nCheck the log file for details.",
                            "Partial Success",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    else
                    {
                        MessageBox.Show(
                            $"Failed to process any PDFs.\n\nErrors:\n{string.Join("\n", failedFiles)}\n\nPlease check the log file for details.",
                            "Processing Failed",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }

                    // Print table data after storing for verification
                    if (successCount > 0)
                    {
                        DatabaseService.PrintAllTableData(dbPath);
                    }

                    // Re-enable buttons
                    uploadButton.Enabled = true;
                    trendButton.Enabled = successCount > 0; // Only enable if we have data
                }
            }
            catch (Exception ex)
            {
                loadingLabel.Visible = false;
                uploadButton.Enabled = true;
                trendButton.Enabled = true;

                LoggingService.LogError("Error during uploadButton_Click", ex);
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{ex.Message}\n\nPlease check the log file for more details.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private async void trendButton_Click(object sender, EventArgs e)
        {
            try
            {
                // Get the latest run ID
                string? runId = GetLatestRunId();

                if (string.IsNullOrWhiteSpace(runId))
                {
                    MessageBox.Show(
                        "No data found in the database.\n\nPlease upload and process PDF files first.",
                        "No Data",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                // Get all parameters for the latest run
                List<string> parameters = GetParametersForRunId(runId);

                if (parameters.Count == 0)
                {
                    MessageBox.Show(
                        "No parameters found for the latest upload.\n\nPlease try uploading PDFs again.",
                        "No Parameters",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                LoggingService.LogInfo($"Generating charts for {parameters.Count} parameters from Run ID: {runId}");

                // Disable buttons during processing
                uploadButton.Enabled = false;
                trendButton.Enabled = false;

                loadingLabel.Visible = true;
                loadingLabel.Text = "Generating trend charts...";
                progressBar.Value = 0;

                List<string> imagePaths = new List<string>(); // List to store image paths

                await Task.Run(() =>
                {
                    List<string> generatedImagePaths = ChartService.GenerateTrendCharts(dbPath, parameters, chart, runId);
                    imagePaths.AddRange(generatedImagePaths);

                    // Update progress on UI thread
                    this.Invoke(new Action(() =>
                    {
                        progressBar.Value = 90; // Chart generation complete
                    }));
                });

                loadingLabel.Text = "Creating PDF...";

                // Save all images to a single PDF
                string pdfPath = ChartService.SaveImagesAsPdf(imagePaths);

                loadingLabel.Text = "Opening PDF...";

                // Open the PDF
                ChartService.OpenPdf(pdfPath);

                progressBar.Value = 100;
                loadingLabel.Visible = false;

                LoggingService.LogInfo($"Trend charts generated successfully. PDF saved to: {pdfPath}");

                MessageBox.Show(
                    $"Trend charts generated successfully!\n\nGenerated {imagePaths.Count} chart(s).\n\nPDF saved to:\n{pdfPath}",
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                // Re-enable buttons
                uploadButton.Enabled = true;
                trendButton.Enabled = true;
            }
            catch (Exception ex)
            {
                loadingLabel.Visible = false;
                uploadButton.Enabled = true;
                trendButton.Enabled = true;

                LoggingService.LogError("Error during trendButton_Click", ex);
                MessageBox.Show(
                    $"An error occurred while generating trend charts:\n\n{ex.Message}\n\nPlease check the log file for more details.",
                    "Chart Generation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private List<string> GetParametersForRunId(string runId)
        {
            List<string> parameters = new List<string>();

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    string sql = @"
                        SELECT DISTINCT parameters.name
                        FROM medical_data
                        INNER JOIN parameters ON medical_data.parameter_id = parameters.id
                        WHERE medical_data.run_id = @run_id
                        ORDER BY parameters.name";

                    SQLiteCommand command = new SQLiteCommand(sql, conn);
                    command.Parameters.AddWithValue("@run_id", runId);

                    SQLiteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        parameters.Add(reader["name"].ToString()!);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error retrieving parameters for run ID", ex);
                throw;
            }

            return parameters;
        }

        private string? GetLatestRunId()
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    string sql = "SELECT run_id FROM medical_data ORDER BY created_at DESC LIMIT 1";
                    SQLiteCommand command = new SQLiteCommand(sql, conn);
                    return command.ExecuteScalar()?.ToString();
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error retrieving latest run ID", ex);
                throw;
            }
        }
    }
}
