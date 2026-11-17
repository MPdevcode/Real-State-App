using Microsoft.Extensions.Configuration;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using SkiaSharp;

namespace RealEstateApp.Infrastructure.Files;

public sealed class StoragePaths
{
    public string Root { get; }
    public string Images => Path.Combine(Root, "images");
    public string Mail => Path.Combine(Root, "mail");
    public string Keys => Path.Combine(Root, "keys");
    public StoragePaths(IConfiguration configuration)
    {
        var configured = configuration["Storage:RootPath"];
        Root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RealEstateApp")
            : Path.GetFullPath(configured);
        Directory.CreateDirectory(Images);
        Directory.CreateDirectory(Mail);
        Directory.CreateDirectory(Keys);
    }
}

public sealed class LocalFileStorage(StoragePaths paths) : IFileStorage
{
    public async Task<string> SaveImageAsync(FileUpload upload, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(upload.Name).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png")) throw new BusinessException("Use imágenes JPG o PNG.");
        using var input = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await upload.Content.ReadAsync(buffer, ct)) != 0)
        {
            if (input.Length + count > 5 * 1024 * 1024) throw new BusinessException("Cada imagen debe pesar como máximo 5 MB.");
            await input.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        if (input.Length == 0) throw new BusinessException("La imagen esta vacía.");
        using var data = SKData.CreateCopy(input.ToArray());
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png))
            throw new BusinessException("El archivo no contiene una imagen JPG o PNG válida.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > 20_000_000
            || info.Width > 10000 || info.Height > 10000)
            throw new BusinessException("La imagen excede las dimensiones permitidas.");
        using var bitmap = new SKBitmap(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new BusinessException("La imagen esta corrupta o incompleta.");
        // Re-encode decoded pixels rather than preserving untrusted metadata or appended payloads.
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        var key = $"{Guid.NewGuid():N}.jpg";
        var destination = Path.Combine(paths.Images, key);
        try { await File.WriteAllBytesAsync(destination, encoded.ToArray(), ct); }
        catch { File.Delete(destination); throw; }
        return key;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsValidKey(key)) throw new InvalidOperationException("Invalid storage key.");
        File.Delete(Path.Combine(paths.Images, key));
        return Task.CompletedTask;
    }

    public static bool IsValidKey(string key) => key.Length == 36 && key.EndsWith(".jpg", StringComparison.Ordinal)
        && Guid.TryParseExact(key[..32], "N", out _);
}
