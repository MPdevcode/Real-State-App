using System.ComponentModel.DataAnnotations;
using RealEstateApp.Domain;

namespace RealEstateApp.WebApp.ViewModels;

public sealed class CatalogViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int PropertyCount { get; set; }
}

public sealed class CatalogInputViewModel
{
    [Required(ErrorMessage = "El nombre es requerido."), StringLength(100, ErrorMessage = "El nombre admite hasta 100 caracteres."), Display(Name = "Nombre")]
    public string Name { get; set; } = "";
    [Required(ErrorMessage = "La descripción es requerida."), StringLength(2000, ErrorMessage = "La descripción admite hasta 2000 caracteres."), Display(Name = "Descripción")]
    public string Description { get; set; } = "";
}

public sealed class CatalogPageViewModel
{
    public CatalogKind Kind { get; set; }
    public string Title => Kind switch { CatalogKind.PropertyTypes => "Tipos de propiedades", CatalogKind.SaleTypes => "Tipos de ventas", _ => "Mejoras" };
    public string Singular => Kind switch { CatalogKind.PropertyTypes => "tipo de propiedad", CatalogKind.SaleTypes => "tipo de venta", _ => "mejora" };
    public string EmptyText => Kind switch { CatalogKind.PropertyTypes => "No existen tipos de propiedades registrados.", CatalogKind.SaleTypes => "No existen tipos de ventas registrados.", _ => "No existen mejoras registradas." };
    public List<CatalogViewModel> Items { get; set; } = [];
    public int? Id { get; set; }
    public CatalogInputViewModel Input { get; set; } = new();
}

public sealed class DashboardViewModel
{
    public int AvailableProperties { get; set; }
    public int SoldProperties { get; set; }
    public int ActiveAgents { get; set; }
    public int InactiveAgents { get; set; }
    public int ActiveClients { get; set; }
    public int InactiveClients { get; set; }
    public int ActiveDevelopers { get; set; }
    public int InactiveDevelopers { get; set; }
}

public sealed class ConfirmationViewModel
{
    public string Title { get; set; } = "Confirmar operación";
    public string Message { get; set; } = "";
    public string? Consequences { get; set; }
    public string PostAction { get; set; } = "";
    public string CancelAction { get; set; } = "Index";
    public string Controller { get; set; } = "";
    public string? Id { get; set; }
    public string? Role { get; set; }
    public CatalogKind? Kind { get; set; }
    public bool? Active { get; set; }
    public bool Destructive { get; set; }
}

public sealed class ErrorViewModel
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = "No fue posible procesar la solicitud. Intente nuevamente más tarde.";
    public string? RequestId { get; set; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class ImageUploadAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not IFormFile file) return ValidationResult.Success;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (file.Length is <= 0 or > 5 * 1024 * 1024)
            return new("La imagen debe tener contenido y no superar 5 MB.");
        if (extension is not (".jpg" or ".jpeg" or ".png") || file.ContentType is not ("image/jpeg" or "image/png"))
            return new("El archivo seleccionado no tiene un formato de imagen válido.");
        return ValidationResult.Success;
    }
}
