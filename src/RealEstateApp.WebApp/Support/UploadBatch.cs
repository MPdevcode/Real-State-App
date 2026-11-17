using RealEstateApp.Application;

namespace RealEstateApp.WebApp.Support;

// The service consumes the streams during the awaited call; the MVC boundary owns their lifetime.
public sealed class UploadBatch : IDisposable
{
    public IReadOnlyList<FileUpload> Files { get; }
    public UploadBatch(IEnumerable<IFormFile> files)
    {
        var uploads = new List<FileUpload>();
        try
        {
            foreach (var file in files)
                uploads.Add(new(file.FileName, file.ContentType, file.OpenReadStream()));
            Files = uploads;
        }
        catch
        {
            foreach (var upload in uploads) upload.Content.Dispose();
            throw;
        }
    }
    public void Dispose()
    {
        foreach (var file in Files) file.Content.Dispose();
    }
}
