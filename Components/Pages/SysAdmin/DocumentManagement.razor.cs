using DV.Admin.UI.Data;
using DV.Admin.UI.Services;
using DV.Shared.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace DV.Admin.UI.Components.Pages.SysAdmin;

public partial class DocumentManagement
{
    [Inject] private DocumentRepository DocumentRepository { get; set; } = default!;
    [Inject] private DocumentUploadService DocumentUploadService { get; set; } = default!;
    [Inject] private AppDbContext DbContext { get; set; } = default!;
    [Inject] private SecurityDbContext SecurityDbContext { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    // ====== Constants ======
    private const string FieldLabelStyle = "display: block; margin-bottom: 0.375rem; color: #1e293b; font-weight: 500; font-size: 0.8125rem;";
    private const string FieldInputStyle = "width: 100%; padding: 0.5rem; border: 1px solid #e2e8f0; border-radius: 0.375rem; font-size: 0.875rem; background: white";

    // ====== State ======
    private List<Project>? projects;
    private Project? selectedProject;
    private bool isLoading;
    private string? errorMessage;

    // Data
    private List<Document> documents = new();
    private DocumentSchemaStats? stats;
    private int totalCount;

    // Filters
    private string? searchTerm;
    private string? filterStatus;
    private string? filterType;
    private string? filterClassification;
    private DateTime? filterDateFrom;
    private DateTime? filterDateTo;

    // Filter dropdown values
    private List<string> distinctStatuses = new();
    private List<string> distinctTypes = new();
    private List<string> distinctClassifications = new();

    // Sorting
    private string sortColumn = "CreatedOn";
    private bool sortAscending = false;

    // Pagination
    private int currentPage = 1;
    private int pageSize = 50;

    // Selection
    private HashSet<int> selectedDocumentIds = new();
    private bool isAllSelected;
    private string? bulkStatusValue;

    // View dialog
    private bool showViewDialog;
    private Document? viewDocument;

    // Edit / Create dialog
    private bool showEditDialog;
    private bool isCreating;
    private int editTab;
    private EditableDocument? editDoc;
    private bool isSaving;
    private string? editSuccessMessage;
    private string? editErrorMessage;

    // Pages dialog
    private bool showPagesDialog;
    private Document? pagesDocument;
    private List<DocumentPage>? documentPages;
    private bool isLoadingPages;
    private int newPageNumber = 1;
    private string? newPageReference;
    private IBrowserFile? newPageFile;
    private bool isUploadingPage;
    private string? pagesSuccessMessage;
    private string? pagesErrorMessage;

    // ====== Lifecycle ======
    protected override async Task OnInitializedAsync()
    {
        try
        {
            var allProjects = await DocumentRepository.GetProjectsAsync();
            projects = allProjects.Where(p => !string.IsNullOrEmpty(p.SchemaName)).OrderBy(p => p.ProjectName).ToList();
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to load projects: {ex.Message}";
        }
    }

    // ====== Project Selection ======
    private async Task OnProjectChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var projectId))
        {
            selectedProject = projects?.FirstOrDefault(p => p.ProjectId == projectId);
            if (selectedProject != null)
            {
                currentPage = 1;
                selectedDocumentIds.Clear();
                await LoadFilterOptions();
                await LoadData();
            }
        }
        else
        {
            selectedProject = null;
            documents = new();
            stats = null;
            totalCount = 0;
        }
    }

    private async Task LoadFilterOptions()
    {
        if (selectedProject == null) return;
        try
        {
            distinctStatuses = await DocumentRepository.GetDistinctValuesAsync(selectedProject.SchemaName, "Status");
            distinctTypes = await DocumentRepository.GetDistinctValuesAsync(selectedProject.SchemaName, "DocumentType");
            distinctClassifications = await DocumentRepository.GetDistinctValuesAsync(selectedProject.SchemaName, "Classification");
        }
        catch { /* ignore if schema doesn't have data */ }
    }

    private async Task LoadData()
    {
        if (selectedProject == null) return;

        isLoading = true;
        errorMessage = null;
        StateHasChanged();

        try
        {
            stats = await DocumentRepository.GetSchemaStatsAsync(selectedProject.SchemaName);
            totalCount = await DocumentRepository.CountInSchemaAsync(selectedProject.SchemaName, searchTerm, filterStatus, filterType, filterClassification, filterDateFrom, filterDateTo);
            documents = (await DocumentRepository.SearchInSchemaAdvancedAsync(selectedProject.SchemaName, searchTerm, currentPage, pageSize, sortColumn, sortAscending, filterStatus, filterType, filterClassification, filterDateFrom, filterDateTo)).ToList();
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to load documents: {ex.Message}";
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task RefreshData()
    {
        await LoadFilterOptions();
        await LoadData();
    }

    // ====== Filtering ======
    private async Task ApplyFilters()
    {
        currentPage = 1;
        selectedDocumentIds.Clear();
        await LoadData();
    }

    private async Task ClearFilters()
    {
        searchTerm = null;
        filterStatus = null;
        filterType = null;
        filterClassification = null;
        filterDateFrom = null;
        filterDateTo = null;
        currentPage = 1;
        selectedDocumentIds.Clear();
        await LoadData();
    }

    // ====== Sorting ======
    private async Task ToggleSort(string column)
    {
        if (sortColumn == column)
        {
            sortAscending = !sortAscending;
        }
        else
        {
            sortColumn = column;
            sortAscending = true;
        }
        currentPage = 1;
        await LoadData();
    }

    private string SortIcon(string column)
    {
        if (sortColumn != column) return "";
        return sortAscending ? " \u25B2" : " \u25BC";
    }

    // ====== Pagination ======
    private async Task FirstPage() { currentPage = 1; await LoadData(); }
    private async Task PreviousPage() { if (currentPage > 1) { currentPage--; await LoadData(); } }
    private async Task NextPage() { var tp = (int)Math.Ceiling((double)totalCount / pageSize); if (currentPage < tp) { currentPage++; await LoadData(); } }
    private async Task LastPage() { currentPage = (int)Math.Ceiling((double)totalCount / pageSize); await LoadData(); }
    private async Task GoToPage(int page) { currentPage = page; await LoadData(); }

    // ====== Selection ======
    private void ToggleDocumentSelection(int docId, bool selected)
    {
        if (selected) selectedDocumentIds.Add(docId);
        else selectedDocumentIds.Remove(docId);
        isAllSelected = documents.Count > 0 && documents.All(d => selectedDocumentIds.Contains(d.DocumentId));
    }

    private void ToggleSelectAll(ChangeEventArgs e)
    {
        isAllSelected = (bool)(e.Value ?? false);
        if (isAllSelected)
        {
            foreach (var d in documents) selectedDocumentIds.Add(d.DocumentId);
        }
        else
        {
            foreach (var d in documents) selectedDocumentIds.Remove(d.DocumentId);
        }
    }

    private void ClearSelection()
    {
        selectedDocumentIds.Clear();
        isAllSelected = false;
        bulkStatusValue = null;
    }

    // ====== Bulk Operations ======
    private async Task BulkDelete()
    {
        if (selectedProject == null || selectedDocumentIds.Count == 0) return;
        var confirmed = await JSRuntime.InvokeAsync<bool>("confirm",
            $"Are you sure you want to permanently delete {selectedDocumentIds.Count} document(s) and all their pages? This cannot be undone.");
        if (!confirmed) return;

        try
        {
            await DocumentRepository.DeleteDocumentsBulkAsync(selectedProject.SchemaName, selectedDocumentIds.ToList());
            selectedDocumentIds.Clear();
            isAllSelected = false;
            await LoadData();
        }
        catch (Exception ex)
        {
            errorMessage = $"Bulk delete failed: {ex.Message}";
        }
    }

    private async Task BulkUpdateStatus()
    {
        if (selectedProject == null || selectedDocumentIds.Count == 0 || string.IsNullOrEmpty(bulkStatusValue)) return;
        var confirmed = await JSRuntime.InvokeAsync<bool>("confirm",
            $"Set status to '{bulkStatusValue}' for {selectedDocumentIds.Count} document(s)?");
        if (!confirmed) return;

        try
        {
            var userId = await ResolveCurrentUserIdAsync();
            await DocumentRepository.UpdateDocumentStatusBulkAsync(selectedProject.SchemaName, selectedDocumentIds.ToList(), bulkStatusValue, userId);
            selectedDocumentIds.Clear();
            isAllSelected = false;
            bulkStatusValue = null;
            await LoadData();
        }
        catch (Exception ex)
        {
            errorMessage = $"Bulk status update failed: {ex.Message}";
        }
    }

    // ====== View Document ======
    private void ViewDocument(Document doc)
    {
        viewDocument = doc;
        showViewDialog = true;
    }

    private void CloseViewDialog()
    {
        showViewDialog = false;
        viewDocument = null;
    }

    // ====== Create / Edit Document ======
    private void OpenCreateDialog()
    {
        isCreating = true;
        editTab = 0;
        editDoc = new EditableDocument { DocumentNumber = $"DOC_{DateTime.UtcNow:yyyyMMdd}_{Guid.NewGuid().ToString("N")[..8]}" };
        editSuccessMessage = null;
        editErrorMessage = null;
        showEditDialog = true;
    }

    private void OpenEditDialog(Document doc)
    {
        isCreating = false;
        editTab = 0;
        editDoc = EditableDocument.FromDocument(doc);
        editSuccessMessage = null;
        editErrorMessage = null;
        showEditDialog = true;
    }

    private void CloseEditDialog()
    {
        showEditDialog = false;
        editDoc = null;
    }

    private async Task SaveDocument()
    {
        if (editDoc == null || selectedProject == null) return;
        if (string.IsNullOrWhiteSpace(editDoc.DocumentNumber))
        {
            editErrorMessage = "Document Number is required.";
            return;
        }

        isSaving = true;
        editErrorMessage = null;
        editSuccessMessage = null;
        StateHasChanged();

        try
        {
            var userId = await ResolveCurrentUserIdAsync();
            var document = editDoc.ToDocument(selectedProject.ProjectId);

            if (isCreating)
            {
                var newId = await DocumentRepository.CreateDocumentAsync(selectedProject.SchemaName, document, userId);
                editSuccessMessage = $"Document created successfully (ID: {newId}).";
            }
            else
            {
                await DocumentRepository.UpdateDocumentAsync(selectedProject.SchemaName, document, userId);
                editSuccessMessage = "Document updated successfully.";
            }

            await LoadData();
            await Task.Delay(1500);
            CloseEditDialog();
        }
        catch (Exception ex)
        {
            editErrorMessage = $"Failed to save: {ex.Message}";
        }
        finally
        {
            isSaving = false;
        }
    }

    // ====== Delete Document ======
    private async Task DeleteDocument(Document doc)
    {
        if (selectedProject == null) return;
        var confirmed = await JSRuntime.InvokeAsync<bool>("confirm",
            $"Permanently delete document #{doc.DocumentId} ({doc.DocumentNumber}) and all its pages? This cannot be undone.");
        if (!confirmed) return;

        try
        {
            await DocumentRepository.DeleteDocumentAsync(selectedProject.SchemaName, doc.DocumentId);
            selectedDocumentIds.Remove(doc.DocumentId);
            await LoadData();
        }
        catch (Exception ex)
        {
            errorMessage = $"Delete failed: {ex.Message}";
        }
    }

    // ====== Pages Dialog ======
    private async Task OpenPagesDialog(Document doc)
    {
        pagesDocument = doc;
        pagesSuccessMessage = null;
        pagesErrorMessage = null;
        newPageNumber = 1;
        newPageReference = null;
        newPageFile = null;
        showPagesDialog = true;
        await LoadPages();
    }

    private void ClosePagesDialog()
    {
        showPagesDialog = false;
        pagesDocument = null;
        documentPages = null;
    }

    private async Task LoadPages()
    {
        if (selectedProject == null || pagesDocument == null) return;
        isLoadingPages = true;
        StateHasChanged();

        try
        {
            documentPages = await DocumentRepository.GetPagesByDocumentIdAsync(selectedProject.SchemaName, pagesDocument.DocumentId);
            if (documentPages.Count > 0)
                newPageNumber = documentPages.Max(p => p.PageNumber) + 1;
        }
        catch (Exception ex)
        {
            pagesErrorMessage = $"Failed to load pages: {ex.Message}";
        }
        finally
        {
            isLoadingPages = false;
        }
    }

    private void OnNewPageFileSelected(InputFileChangeEventArgs e)
    {
        newPageFile = e.File;
        pagesErrorMessage = null;
    }

    private async Task UploadNewPage()
    {
        if (selectedProject == null || pagesDocument == null || newPageFile == null) return;

        const long maxSize = 50 * 1024 * 1024;
        if (newPageFile.Size > maxSize)
        {
            pagesErrorMessage = "File exceeds 50 MB limit.";
            return;
        }

        isUploadingPage = true;
        pagesErrorMessage = null;
        pagesSuccessMessage = null;
        StateHasChanged();

        try
        {
            var wrapper = new FormFileWrapper(newPageFile);
            var userId = await ResolveCurrentUserIdAsync();
            var uploadedPage = await DocumentUploadService.UploadFileAsync(
                pagesDocument.DocumentId, newPageNumber, wrapper, newPageReference, userId);

            pagesSuccessMessage = $"Page {newPageNumber} uploaded successfully.";
            newPageFile = null;
            newPageReference = null;
            await LoadPages();
        }
        catch (Exception ex)
        {
            pagesErrorMessage = $"Upload error: {ex.Message}";
        }
        finally
        {
            isUploadingPage = false;
        }
    }

    private async Task DeletePage(DocumentPage pg)
    {
        if (selectedProject == null) return;
        var confirmed = await JSRuntime.InvokeAsync<bool>("confirm",
            $"Delete page {pg.PageNumber}? This cannot be undone.");
        if (!confirmed) return;

        try
        {
            await DocumentRepository.DeletePageAsync(selectedProject.SchemaName, pg.PageId);
            pagesSuccessMessage = $"Page {pg.PageNumber} deleted.";
            await LoadPages();
        }
        catch (Exception ex)
        {
            pagesErrorMessage = $"Failed to delete page: {ex.Message}";
        }
    }

    // ====== CSV Export ======
    private async Task ExportCsv()
    {
        if (selectedProject == null) return;

        try
        {
            var allDocs = await DocumentRepository.SearchInSchemaAdvancedAsync(
                selectedProject.SchemaName, searchTerm, 1, 100000,
                sortColumn, sortAscending, filterStatus, filterType, filterClassification, filterDateFrom, filterDateTo);

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("DocumentId,DocumentNumber,Title,Author,DocumentDate,DocumentType,Status,Classification,Keywords,DocumentIndex,Issue,Version,Memo,Text01,Text02,Text03,Text04,Text05,Text06,Text07,Text08,Text09,Text10,Text11,Text12,Date01,Date02,Date03,Date04,Boolean01,Boolean02,Boolean03,Number01,Number02,Number03,OldDM,CM,GM,EM,CreatedOn,ModifiedOn");

            foreach (var d in allDocs)
            {
                csv.AppendLine(
                    $"{d.DocumentId},{Esc(d.DocumentNumber)},{Esc(d.Title)},{Esc(d.Author)},{d.DocumentDate:yyyy-MM-dd},{Esc(d.DocumentType)},{Esc(d.Status)},{Esc(d.Classification)},{Esc(d.Keywords)},{Esc(d.DocumentIndex)},{Esc(d.Issue)},{Esc(d.Version)},{Esc(d.Memo)},{Esc(d.Text01)},{Esc(d.Text02)},{Esc(d.Text03)},{Esc(d.Text04)},{Esc(d.Text05)},{Esc(d.Text06)},{Esc(d.Text07)},{Esc(d.Text08)},{Esc(d.Text09)},{Esc(d.Text10)},{Esc(d.Text11)},{Esc(d.Text12)},{d.Date01:yyyy-MM-dd},{d.Date02:yyyy-MM-dd},{d.Date03:yyyy-MM-dd},{d.Date04:yyyy-MM-dd},{d.Boolean01},{d.Boolean02},{d.Boolean03},{d.Number01},{d.Number02},{d.Number03},{d.OldDM},{d.CM},{d.GM},{d.EM},{d.CreatedOn:yyyy-MM-dd HH:mm:ss},{d.ModifiedOn:yyyy-MM-dd HH:mm:ss}");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            var base64 = Convert.ToBase64String(bytes);
            var fileName = $"documents_{selectedProject.SchemaName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            await JSRuntime.InvokeVoidAsync("eval",
                $"var a=document.createElement('a');a.href='data:text/csv;base64,{base64}';a.download='{fileName}';document.body.appendChild(a);a.click();a.remove();");
        }
        catch (Exception ex)
        {
            errorMessage = $"Export failed: {ex.Message}";
        }
    }

    // ====== Helpers ======
    private async Task<int> ResolveCurrentUserIdAsync()
    {
        try
        {
            var user = await SecurityDbContext.Users.FirstOrDefaultAsync();
            return user?.UserId ?? 1;
        }
        catch { return 1; }
    }

    private string GetStatusBadgeStyle(string? status)
    {
        var bg = status?.ToLower() switch
        {
            "active" or "approved" or "published" => "#dcfce7",
            "draft" or "pending" => "#fef3c7",
            "archived" or "obsolete" or "superseded" => "#f1f5f9",
            "rejected" or "failed" or "error" => "#fee2e2",
            "review" or "in review" => "#dbeafe",
            _ => "#f1f5f9"
        };
        var fg = status?.ToLower() switch
        {
            "active" or "approved" or "published" => "#166534",
            "draft" or "pending" => "#92400e",
            "archived" or "obsolete" or "superseded" => "#475569",
            "rejected" or "failed" or "error" => "#991b1b",
            "review" or "in review" => "#1e40af",
            _ => "#475569"
        };
        return $"padding: 0.125rem 0.5rem; background: {bg}; color: {fg}; border-radius: 0.25rem; font-size: 0.75rem; font-weight: 600;";
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "\u2014" : (s.Length > max ? s[..max] + "\u2026" : s);

    private static string FormatFileSize(long? bytes)
    {
        if (!bytes.HasValue || bytes <= 0) return "\u2014";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }

    private static string Esc(string? val) =>
        string.IsNullOrEmpty(val) ? "" : "\"" + val.Replace("\"", "\"\"") + "\"";

    private void GoBackToDashboard() => Navigation.NavigateTo("/sysadmin");

    private List<DocumentFieldView> GetDocumentFieldsForView(Document doc)
    {
        var fields = new List<DocumentFieldView>
        {
            new("Document ID", doc.DocumentId.ToString(), false),
            new("Document Number", doc.DocumentNumber, false),
            new("Title", doc.Title, true),
            new("Author", doc.Author, false),
            new("Document Date", doc.DocumentDate?.ToString("yyyy-MM-dd"), false),
            new("Document Type", doc.DocumentType, false),
            new("Status", doc.Status, false),
            new("Classification", doc.Classification, false),
            new("Version", doc.Version, false),
            new("Document Index", doc.DocumentIndex, false),
            new("Issue", doc.Issue, false),
            new("Keywords", doc.Keywords, true),
            new("Memo", doc.Memo, true),
            new("File Path", doc.FilePath, true),
        };

        // Custom text fields (only show non-empty)
        for (int i = 1; i <= 12; i++)
        {
            var val = typeof(Document).GetProperty($"Text{i:D2}")?.GetValue(doc)?.ToString();
            if (!string.IsNullOrEmpty(val))
                fields.Add(new DocumentFieldView($"Text {i:D2}", val, false));
        }

        // Custom dates
        for (int i = 1; i <= 4; i++)
        {
            var val = (DateTime?)typeof(Document).GetProperty($"Date{i:D2}")?.GetValue(doc);
            if (val.HasValue)
                fields.Add(new DocumentFieldView($"Date {i:D2}", val.Value.ToString("yyyy-MM-dd"), false));
        }

        // Booleans
        for (int i = 1; i <= 3; i++)
        {
            var val = (bool?)typeof(Document).GetProperty($"Boolean{i:D2}")?.GetValue(doc);
            if (val.HasValue)
                fields.Add(new DocumentFieldView($"Boolean {i:D2}", val.Value ? "Yes" : "No", false));
        }

        // Numbers
        for (int i = 1; i <= 3; i++)
        {
            var val = (double?)typeof(Document).GetProperty($"Number{i:D2}")?.GetValue(doc);
            if (val.HasValue)
                fields.Add(new DocumentFieldView($"Number {i:D2}", val.Value.ToString("F2"), false));
        }

        // Related IDs
        if (doc.OldDM.HasValue) fields.Add(new DocumentFieldView("OldDM", doc.OldDM.ToString()!, false));
        if (doc.CM.HasValue) fields.Add(new DocumentFieldView("CM", doc.CM.ToString()!, false));
        if (doc.GM.HasValue) fields.Add(new DocumentFieldView("GM", doc.GM.ToString()!, false));
        if (doc.EM.HasValue) fields.Add(new DocumentFieldView("EM", doc.EM.ToString()!, false));

        // Audit
        fields.Add(new DocumentFieldView("Created On", doc.CreatedOn.ToString("yyyy-MM-dd HH:mm:ss"), false));
        fields.Add(new DocumentFieldView("Created By", doc.CreatedBy.ToString(), false));
        fields.Add(new DocumentFieldView("Modified On", doc.ModifiedOn?.ToString("yyyy-MM-dd HH:mm:ss"), false));
        fields.Add(new DocumentFieldView("Modified By", doc.ModifiedBy?.ToString(), false));

        return fields;
    }

    // ====== Inner Classes ======
    private class DocumentFieldView
    {
        public string Label { get; set; }
        public string? Value { get; set; }
        public bool Wide { get; set; }
        public DocumentFieldView(string label, string? value, bool wide) { Label = label; Value = value; Wide = wide; }
    }

    private class EditableDocument
    {
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Author { get; set; }
        public DateTime? DocumentDate { get; set; }
        public string? DocumentType { get; set; }
        public string? Status { get; set; }
        public string? Classification { get; set; }
        public string? Keywords { get; set; }
        public string? Memo { get; set; }
        public string? DocumentIndex { get; set; }
        public string? Issue { get; set; }
        public string? Version { get; set; }
        public string? FilePath { get; set; }

        public string? Text01 { get; set; }
        public string? Text02 { get; set; }
        public string? Text03 { get; set; }
        public string? Text04 { get; set; }
        public string? Text05 { get; set; }
        public string? Text06 { get; set; }
        public string? Text07 { get; set; }
        public string? Text08 { get; set; }
        public string? Text09 { get; set; }
        public string? Text10 { get; set; }
        public string? Text11 { get; set; }
        public string? Text12 { get; set; }

        public DateTime? Date01 { get; set; }
        public DateTime? Date02 { get; set; }
        public DateTime? Date03 { get; set; }
        public DateTime? Date04 { get; set; }

        public bool? Boolean01 { get; set; }
        public bool? Boolean02 { get; set; }
        public bool? Boolean03 { get; set; }

        public double? Number01 { get; set; }
        public double? Number02 { get; set; }
        public double? Number03 { get; set; }

        public int? OldDM { get; set; }
        public int? CM { get; set; }
        public int? GM { get; set; }
        public int? EM { get; set; }

        public DateTime CreatedOn { get; set; }
        public int CreatedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public int? ModifiedBy { get; set; }

        public static EditableDocument FromDocument(Document d) => new()
        {
            DocumentId = d.DocumentId,
            DocumentNumber = d.DocumentNumber,
            Title = d.Title, Author = d.Author, DocumentDate = d.DocumentDate,
            DocumentType = d.DocumentType, Status = d.Status, Classification = d.Classification,
            Keywords = d.Keywords, Memo = d.Memo, DocumentIndex = d.DocumentIndex,
            Issue = d.Issue, Version = d.Version, FilePath = d.FilePath,
            Text01 = d.Text01, Text02 = d.Text02, Text03 = d.Text03, Text04 = d.Text04,
            Text05 = d.Text05, Text06 = d.Text06, Text07 = d.Text07, Text08 = d.Text08,
            Text09 = d.Text09, Text10 = d.Text10, Text11 = d.Text11, Text12 = d.Text12,
            Date01 = d.Date01, Date02 = d.Date02, Date03 = d.Date03, Date04 = d.Date04,
            Boolean01 = d.Boolean01, Boolean02 = d.Boolean02, Boolean03 = d.Boolean03,
            Number01 = d.Number01, Number02 = d.Number02, Number03 = d.Number03,
            OldDM = d.OldDM, CM = d.CM, GM = d.GM, EM = d.EM,
            CreatedOn = d.CreatedOn, CreatedBy = d.CreatedBy,
            ModifiedOn = d.ModifiedOn, ModifiedBy = d.ModifiedBy
        };

        public Document ToDocument(int projectId) => new()
        {
            DocumentId = DocumentId,
            ProjectId = projectId,
            DocumentNumber = DocumentNumber,
            Title = Title, Author = Author, DocumentDate = DocumentDate,
            DocumentType = DocumentType, Status = Status, Classification = Classification,
            Keywords = Keywords, Memo = Memo, DocumentIndex = DocumentIndex,
            Issue = Issue, Version = Version, FilePath = FilePath,
            Text01 = Text01, Text02 = Text02, Text03 = Text03, Text04 = Text04,
            Text05 = Text05, Text06 = Text06, Text07 = Text07, Text08 = Text08,
            Text09 = Text09, Text10 = Text10, Text11 = Text11, Text12 = Text12,
            Date01 = Date01, Date02 = Date02, Date03 = Date03, Date04 = Date04,
            Boolean01 = Boolean01, Boolean02 = Boolean02, Boolean03 = Boolean03,
            Number01 = Number01, Number02 = Number02, Number03 = Number03,
            OldDM = OldDM, CM = CM, GM = GM, EM = EM
        };
    }
}
