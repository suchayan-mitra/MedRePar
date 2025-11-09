# MedRePar - Medical Report Parser

A production-ready Windows desktop application for parsing medical reports (PDFs) and extracting health parameters using AI, with intelligent OCR support for scanned documents.

## Features

### Core Capabilities
- **Intelligent PDF Text Extraction**: Automatically detects digital vs scanned PDFs
- **Azure Document Intelligence OCR**: Handles scanned medical reports with high accuracy
- **AI-Powered Parameter Extraction**: Uses Azure OpenAI or OpenAI to extract structured medical data
- **Multi-Report Support**: Process multiple PDFs in a single batch
- **Trend Visualization**: Generate charts showing parameter trends over time
- **SQLite Database**: Local storage with transactional integrity

### Production-Ready Features
- **Retry Logic**: Exponential backoff for API failures (4 retries)
- **Comprehensive Error Handling**: Graceful degradation and informative error messages
- **Input Validation**: PDF validation before processing
- **Transaction Support**: Database rollback on errors
- **Structured Logging**: NLog integration with detailed audit trails
- **Progress Tracking**: Real-time UI feedback during processing
- **Partial Failure Handling**: Continue processing even if some PDFs fail

### Intelligent OCR
- **Digital PDFs**: Fast embedded text extraction (milliseconds)
- **Scanned PDFs**: Automatic fallback to Azure Document Intelligence OCR
- **Text Quality Detection**: Smart detection of meaningful vs garbage text
- **Layout Preservation**: Maintains document structure during extraction

## Prerequisites

- **Windows OS**: Windows 10/11 (Windows Forms application)
- **.NET 8 SDK**: [Download here](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Visual Studio 2022** (recommended) or VS Code with C# extension
- **Azure Account** (for Azure OpenAI and optional OCR)
- **OpenAI API Key** (alternative to Azure OpenAI)

## Quick Start

### 1. Clone the Repository

```bash
git clone https://github.com/yourusername/MedRePar.git
cd MedRePar
```

### 2. Configure API Credentials

```bash
# Copy the template to create your config file
cp App.config.template App.config
```

Edit `App.config` and add your credentials:

**For Azure OpenAI:**
```xml
<aiModelsSection>
  <add key="AzureOpenAI" value="https://YOUR-RESOURCE.openai.azure.com/;YOUR-API-KEY;YOUR-DEPLOYMENT;2024-05-01" />
</aiModelsSection>
```

**For Standard OpenAI:**
```xml
<aiModelsSection>
  <add key="OpenAI" value="https://api.openai.com/v1/chat/completions;sk-YOUR-API-KEY" />
</aiModelsSection>
```

**For OCR Support (Optional):**
```xml
<appSettings>
  <add key="AzureDocumentIntelligenceEndpoint" value="https://YOUR-RESOURCE.cognitiveservices.azure.com/" />
  <add key="AzureDocumentIntelligenceApiKey" value="YOUR-API-KEY" />
</appSettings>
```

### 3. Restore Dependencies

```bash
dotnet restore
```

### 4. Build the Application

```bash
dotnet build
```

### 5. Run the Application

```bash
dotnet run
```

Or open `MedRePar.sln` in Visual Studio and press F5.

## Usage Guide

### Processing Medical Reports

1. **Launch the Application**: Run MedRePar.exe
2. **Select AI Model**: Choose from configured models in the dropdown
3. **Upload PDFs**: Click "Upload PDF" and select one or more medical report PDFs
4. **View Progress**: Monitor the progress bar and status messages
5. **Generate Charts**: Click "Generate Trend Chart" to visualize parameter trends
6. **Review Results**: Charts are saved as PDF and automatically opened

### Supported PDF Formats

- **Digital PDFs**: PDFs with embedded text (most modern lab reports)
- **Scanned PDFs**: Image-based PDFs (requires Azure Document Intelligence)
- **Multi-page Reports**: Automatically processes all pages
- **Various Layouts**: Handles different medical report formats

### What Gets Extracted

The AI model extracts:
- **Report Date**: Automatically detected
- **Medical Categories**: Lipid Profile, Complete Blood Count, Liver Function, etc.
- **Parameter Names**: LDL Cholesterol, Hemoglobin, etc.
- **Values with Units**: "150 mg/dL", "14.5 g/dL", etc.
- **Reference Ranges**: "(70-100)", ">40", etc.

## Configuration Details

### AI Model Configuration

The application supports two AI providers:

#### Azure OpenAI (Recommended)
- **Pros**: Enterprise security, HIPAA compliant, better rate limits
- **Cons**: Requires Azure account
- **Setup**: Create Azure OpenAI resource, deploy a model (gpt-4 recommended)
- **Format**: `endpoint;api-key;deployment-name;api-version`

#### Standard OpenAI
- **Pros**: Easy to set up, pay-as-you-go
- **Cons**: Data leaves your environment
- **Setup**: Get API key from platform.openai.com
- **Format**: `api-url;api-key`

### OCR Configuration (Optional)

Azure Document Intelligence provides:
- **Medical-specific OCR**: Trained on healthcare documents
- **Handwriting Recognition**: Handles doctor's notes
- **Table Detection**: Extracts structured lab results
- **Cost**: ~$1.50 per 1000 pages
- **HIPAA Compliance**: Healthcare-ready

**When to enable OCR:**
- Processing scanned documents
- Handling image-based PDFs
- Dealing with faxed reports
- Working with photocopied documents

**When NOT needed:**
- All PDFs are digital (selectable text)
- Budget constraints (OCR adds cost)
- Only modern electronic lab reports

## Architecture

### Data Flow

```
PDF Upload
    ↓
PDF Validation
    ↓
Text Extraction (iTextSharp or Azure Document Intelligence)
    ↓
AI Processing (Azure OpenAI / OpenAI)
    ↓
JSON Parsing & Validation
    ↓
SQLite Storage (with transactions)
    ↓
Chart Generation
    ↓
PDF Export
```

### Database Schema

```sql
categories
├── id (PRIMARY KEY)
└── name (UNIQUE)

parameters
├── id (PRIMARY KEY)
├── category_id (FOREIGN KEY)
├── name (normalized)
└── alias (original name)

medical_data
├── id (PRIMARY KEY)
├── parameter_id (FOREIGN KEY)
├── value
├── date
├── run_id
└── created_at
```

### Key Services

- **PdfService**: Text extraction with intelligent OCR fallback
- **OpenAiService**: AI-powered parameter normalization
- **DatabaseService**: SQLite operations with transactions
- **ChartService**: Trend visualization
- **LoggingService**: NLog wrapper

## Error Handling

### Automatic Retry
- API failures: 4 retries with exponential backoff (2s, 4s, 8s, 16s)
- Network errors: Automatic retry
- Rate limits: Detected and retried

### Graceful Degradation
- OCR not configured: Falls back to embedded text extraction
- Some PDFs fail: Continues processing others
- Database errors: Transaction rollback

### Logging
All operations logged to `logs/logfile.log`:
- Info: Normal operations
- Warning: Recoverable issues
- Error: Failures with stack traces

## Troubleshooting

### "No text could be extracted from PDF"
- **Cause**: Scanned PDF without OCR configured
- **Solution**: Configure Azure Document Intelligence or convert PDF to searchable format

### "AI model returned invalid JSON"
- **Cause**: LLM prompt issue or malformed report
- **Solution**: Check logs for response, may need to adjust prompt

### "Rate limit exceeded"
- **Cause**: Too many requests to Azure/OpenAI
- **Solution**: Wait and retry, or upgrade to higher tier

### Application won't start
- **Cause**: Missing App.config
- **Solution**: Copy App.config.template to App.config and add credentials

## Development

### Project Structure

```
MedRePar/
├── Services/
│   ├── PdfService.cs           # PDF text extraction + OCR
│   ├── OpenAiService.cs        # AI integration
│   ├── DatabaseService.cs      # SQLite operations
│   ├── ChartService.cs         # Visualization
│   ├── LoggingService.cs       # NLog wrapper
│   └── AIModelConfig.cs        # Configuration models
├── MainForm.cs                 # UI logic
├── MainForm.Designer.cs        # UI definition
├── Program.cs                  # Entry point
├── NLog.config                 # Logging config
├── App.config.template         # Config template
└── README.md                   # This file
```

### Adding New AI Providers

1. Create new config class extending `AIModelConfig`
2. Implement credential parsing
3. Add handler in `OpenAiService.NormalizeParametersUsingOpenAI()`
4. Update App.config.template

### Building for Release

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

Output: `bin/Release/net8.0-windows/win-x64/publish/`

## Security Considerations

### Credentials
- **Never commit** `App.config` (in .gitignore)
- Use environment variables for CI/CD
- Rotate API keys regularly

### PHI/PII Data
- SQLite database contains medical data
- Encrypt database file if sharing devices
- Consider HIPAA compliance requirements
- Use Azure services with BAA for production

### Logging
- Logs may contain extracted text
- Review log retention policies
- Secure log file access

## Cost Estimation

### Azure OpenAI
- **Input**: ~$0.03 per 1000 tokens (~750 words)
- **Output**: ~$0.06 per 1000 tokens
- **Typical Report**: $0.05 - $0.10 per report

### Azure Document Intelligence
- **OCR**: $1.50 per 1000 pages
- **Typical Report**: $0.001 - $0.005 per page

### Total
- **Digital PDFs**: ~$0.05 - $0.10 per report
- **Scanned PDFs**: ~$0.10 - $0.15 per report

## Roadmap

### Planned Features
- [ ] Export to CSV/Excel
- [ ] Custom parameter mappings
- [ ] Multi-user support
- [ ] Cloud sync (Azure SQL)
- [ ] PDF report generation with insights
- [ ] Anomaly detection
- [ ] Web API version

### Known Limitations
- Windows only (Windows Forms)
- Single-user application
- No built-in authentication
- Limited chart customization

## Contributing

Contributions welcome! Please:
1. Fork the repository
2. Create a feature branch
3. Make changes with tests
4. Submit pull request

## License

This project is licensed under the MIT License - see [LICENSE.txt](LICENSE.txt) for details.

## Support

- **Issues**: [GitHub Issues](https://github.com/yourusername/MedRePar/issues)
- **Discussions**: [GitHub Discussions](https://github.com/yourusername/MedRePar/discussions)
- **Logs**: Check `logs/logfile.log` for detailed error information

## Acknowledgments

- **Azure OpenAI**: AI-powered extraction
- **Azure Document Intelligence**: OCR capabilities
- **iTextSharp**: PDF text extraction
- **NLog**: Logging framework
- **Polly**: Resilience library

---

**Version**: 2.0.0
**Last Updated**: 2025-01-09
**Status**: Production-Ready
