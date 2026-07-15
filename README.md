# OcrSearch

Search by text on local screenshots and other pictures for Windows 11: OCR-based index service with full-text search component.

## OCR quality spike

Process a directory with screenshots to process and generate HTML report 
```powershell
dotnet run --project tools/OcrSearch.OcrSpike -- "C:\path\to\screenshots" --out report.html
```
