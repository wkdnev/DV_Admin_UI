using Microsoft.AspNetCore.Components.Forms;

namespace DV.Admin.UI.Services;

/// <summary>
/// Wrapper to convert IBrowserFile to IFormFile for DocumentUploadService
/// </summary>
public class FormFileWrapper : IFormFile
{
    private readonly IBrowserFile _browserFile;

    public FormFileWrapper(IBrowserFile browserFile)
    {
        _browserFile = browserFile;
    }

    public string ContentType => _browserFile.ContentType;
    public string ContentDisposition => string.Empty;
    public IHeaderDictionary Headers => new HeaderDictionary();
    public long Length => _browserFile.Size;
    public string Name => _browserFile.Name;
    public string FileName => _browserFile.Name;

    public void CopyTo(Stream target) => throw new NotImplementedException("Use CopyToAsync instead");

    public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
    {
        using var stream = _browserFile.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024); // 50MB max
        await stream.CopyToAsync(target, cancellationToken);
    }

    public Stream OpenReadStream() => _browserFile.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024);
}
