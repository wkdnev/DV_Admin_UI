using DV.Admin.UI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DV.Admin.UI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentBlobController : ControllerBase
{
    private readonly DocumentUploadService _documentUploadService;
    private readonly ILogger<DocumentBlobController> _logger;

    public DocumentBlobController(DocumentUploadService documentUploadService, ILogger<DocumentBlobController> logger)
    {
        _documentUploadService = documentUploadService;
        _logger = logger;
    }

    [HttpGet("page/{pageId:int}")]
    public async Task<IActionResult> GetDocumentPage(int pageId, bool inline = true)
    {
        try
        {
            var content = await _documentUploadService.GetDocumentPageContentAsync(pageId);

            if (content == null)
            {
                _logger.LogWarning("Document page {PageId} not found", pageId);
                return NotFound("Document page not found");
            }

            var (fileContent, contentType, fileName) = content.Value;

            var contentDisposition = inline ? "inline" : "attachment";
            Response.Headers["Content-Disposition"] = $"{contentDisposition}; filename=\"{fileName}\"";
            Response.Headers["Cache-Control"] = "public, max-age=3600";

            return File(fileContent, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving document page {PageId}", pageId);
            return StatusCode(500, "Error retrieving document");
        }
    }

    [HttpGet("page/{pageId:int}/download")]
    public async Task<IActionResult> DownloadDocumentPage(int pageId)
    {
        return await GetDocumentPage(pageId, inline: false);
    }
}
