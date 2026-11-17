using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using RealEstateApp.Domain;

namespace RealEstateApp.WebApp.ViewModels;

public sealed class PropertyFilterViewModel : IValidatableObject
{
    [Display(Name = "Código"), RegularExpression("^[0-9]{6}$", ErrorMessage = "El código debe tener seis dígitos.")]
    public string? Code { get; set; }
    [Display(Name = "Tipo de propiedad"), Range(1, int.MaxValue, ErrorMessage = "Seleccione un tipo válido.")]
    public int? PropertyTypeId { get; set; }
    [Display(Name = "Precio mínimo (DOP)"), Range(typeof(decimal), "0", "9999999999999999", ErrorMessage = "El precio mínimo no puede ser negativo.")]
    public decimal? MinPrice { get; set; }
    [Display(Name = "Precio máximo (DOP)"), Range(typeof(decimal), "0", "9999999999999999", ErrorMessage = "El precio máximo no puede ser negativo.")]
    public decimal? MaxPrice { get; set; }
    [Display(Name = "Habitaciones"), Range(0, int.MaxValue, ErrorMessage = "Las habitaciones no pueden ser negativas.")]
    public int? Bedrooms { get; set; }
    [Display(Name = "Baños"), Range(0, int.MaxValue, ErrorMessage = "Los baños no pueden ser negativos.")]
    public int? Bathrooms { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice > MaxPrice)
            yield return new("El precio mínimo no puede superar el precio máximo.", [nameof(MaxPrice)]);
    }
}

public sealed class PropertyViewModel
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int PropertyTypeId { get; set; }
    public string PropertyType { get; set; } = "";
    public int SaleTypeId { get; set; }
    public string SaleType { get; set; } = "";
    public decimal Price { get; set; }
    public decimal Size { get; set; }
    public int Bedrooms { get; set; }
    public int Bathrooms { get; set; }
    public string Description { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string AgentName { get; set; } = "";
    public string? AgentEmail { get; set; }
    public string? AgentPhone { get; set; }
    public string? AgentPhoto { get; set; }
    public PropertyStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<ImageViewModel> Images { get; set; } = [];
    public List<CatalogViewModel> Improvements { get; set; } = [];
    public bool IsFavorite { get; set; }
    public bool Available => Status == PropertyStatus.Available;
}

public sealed class ImageViewModel
{
    public int Id { get; set; }
    public string FileKey { get; set; } = "";
    public int SortOrder { get; set; }
}

public sealed class PropertyListingViewModel
{
    public string Title { get; set; } = "Un lugar para su próxima etapa.";
    public string Subtitle { get; set; } = "Explore las propiedades disponibles y encuentre su espacio.";
    public string EmptyMessage { get; set; } = "No se encontraron propiedades disponibles con los filtros seleccionados.";
    public PropertyFilterViewModel Filter { get; set; } = new();
    public List<CatalogViewModel> PropertyTypes { get; set; } = [];
    public List<PropertyViewModel> Properties { get; set; } = [];
    public string ListingController { get; set; } = "Home";
    public string ListingAction { get; set; } = "Index";
    public string? AgentId { get; set; }
    public bool Maintenance { get; set; }
}

public sealed class PropertyInputViewModel : IValidatableObject
{
    [Display(Name = "Tipo de propiedad"), Required(ErrorMessage = "Seleccione el tipo de propiedad."), Range(1, int.MaxValue, ErrorMessage = "Seleccione un tipo válido.")]
    public int? PropertyTypeId { get; set; }
    [Display(Name = "Tipo de venta"), Required(ErrorMessage = "Seleccione el tipo de venta."), Range(1, int.MaxValue, ErrorMessage = "Seleccione un tipo válido.")]
    public int? SaleTypeId { get; set; }
    [Display(Name = "Precio (DOP)"), Required(ErrorMessage = "Ingrese el precio."), Range(typeof(decimal), "0.01", "9999999999999999", ErrorMessage = "El precio debe ser mayor que cero.")]
    public decimal? Price { get; set; }
    [Display(Name = "Superficie (m2)"), Required(ErrorMessage = "Ingrese el tamaño."), Range(typeof(decimal), "0.01", "9999999999999999", ErrorMessage = "El tamaño debe ser mayor que cero.")]
    public decimal? Size { get; set; }
    [Display(Name = "Habitaciones"), Required(ErrorMessage = "Ingrese las habitaciones."), Range(0, int.MaxValue, ErrorMessage = "Las habitaciones no pueden ser negativas.")]
    public int? Bedrooms { get; set; }
    [Display(Name = "Baños"), Required(ErrorMessage = "Ingrese los baños."), Range(0, int.MaxValue, ErrorMessage = "Los baños no pueden ser negativos.")]
    public int? Bathrooms { get; set; }
    [Display(Name = "Descripción"), Required(ErrorMessage = "La descripción es requerida."), StringLength(4000, ErrorMessage = "La descripción admite hasta 4000 caracteres.")]
    public string Description { get; set; } = "";
    [Display(Name = "Mejoras"), MinLength(1, ErrorMessage = "Seleccione al menos una mejora.")]
    public List<int> ImprovementIds { get; set; } = [];
    public List<int> RetainedImageIds { get; set; } = [];
    [Display(Name = "Imagen 1"), ImageUpload] public IFormFile? File1 { get; set; }
    [Display(Name = "Imagen 2"), ImageUpload] public IFormFile? File2 { get; set; }
    [Display(Name = "Imagen 3"), ImageUpload] public IFormFile? File3 { get; set; }
    [Display(Name = "Imagen 4"), ImageUpload] public IFormFile? File4 { get; set; }
    [BindNever, ValidateNever] public int? Id { get; set; }
    [BindNever, ValidateNever] public string? Code { get; set; }
    [BindNever, ValidateNever] public List<ImageViewModel> ExistingImages { get; set; } = [];
    [BindNever, ValidateNever] public List<CatalogViewModel> PropertyTypes { get; set; } = [];
    [BindNever, ValidateNever] public List<CatalogViewModel> SaleTypes { get; set; } = [];
    [BindNever, ValidateNever] public List<CatalogViewModel> Improvements { get; set; } = [];
    public IEnumerable<IFormFile> Files => new[] { File1, File2, File3, File4 }.OfType<IFormFile>();
    public bool HasCatalogs => PropertyTypes.Count > 0 && SaleTypes.Count > 0 && Improvements.Count > 0;
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var count = RetainedImageIds.Distinct().Count() + Files.Count();
        if (count < 1)
            yield return new("Debe cargar al menos una imagen de la propiedad.", [nameof(File1)]);
        else if (count > 4)
            yield return new("Solo se permite registrar hasta 4 imágenes por propiedad.", [nameof(File1)]);
        if (ImprovementIds.Any(x => x < 1))
            yield return new("Seleccione mejoras válidas.", [nameof(ImprovementIds)]);
    }
}

public sealed class MessageInputViewModel
{
    [Required(ErrorMessage = "Debe escribir un mensaje antes de enviarlo."), StringLength(4000, ErrorMessage = "El mensaje admite hasta 4000 caracteres."), Display(Name = "Mensaje")]
    public string Body { get; set; } = "";
}

public sealed class OfferInputViewModel
{
    [Required(ErrorMessage = "Debe ingresar el monto de la oferta."), Range(typeof(decimal), "0.01", "9999999999999999", ErrorMessage = "El monto de la oferta debe ser un valor numérico mayor que cero."), Display(Name = "Monto ofertado (DOP)")]
    public decimal? Amount { get; set; }
}

public sealed class FavoriteInputViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione una propiedad válida.")] public int PropertyId { get; set; }
    [Required(ErrorMessage = "Indique el estado de favorito.")] public bool? Favorite { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class OfferResponseViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "La oferta no es válida.")] public int OfferId { get; set; }
    [Required(ErrorMessage = "Seleccione aceptar o rechazar.")] public bool? Accept { get; set; }
}

public sealed class MessageViewModel
{
    public string SenderId { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ConversationViewModel
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string AgentId { get; set; } = "";
    public List<MessageViewModel> Messages { get; set; } = [];
    public MessageViewModel? Latest => Messages.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
}

public sealed class OfferViewModel
{
    public int Id { get; set; }
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public decimal Amount { get; set; }
    public OfferStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string StatusLabel => Status switch { OfferStatus.Pending => "Pendiente", OfferStatus.Accepted => "Aceptada", _ => "Rechazada" };
}

public sealed class PropertyDetailsViewModel
{
    public PropertyViewModel Property { get; set; } = new();
    public List<ConversationViewModel> Conversations { get; set; } = [];
    public List<OfferViewModel> Offers { get; set; } = [];
    public string? SelectedClientId { get; set; }
    public ConversationViewModel? SelectedConversation => Conversations.FirstOrDefault(x => x.ClientId == SelectedClientId);
    public MessageInputViewModel Message { get; set; } = new();
    public OfferInputViewModel Offer { get; set; } = new();
    public bool CanOffer => Property.Available && !Offers.Any(x => x.Status is OfferStatus.Pending or OfferStatus.Accepted);
    public string? OfferBlockedReason => !Property.Available
        ? "Esta propiedad ya no se encuentra disponible para recibir ofertas."
        : Offers.Any(x => x.Status == OfferStatus.Accepted)
            ? "Esta propiedad ya tiene una oferta aceptada y no permite nuevas ofertas."
            : Offers.Any(x => x.Status == OfferStatus.Pending)
                ? "Ya tiene una oferta pendiente para esta propiedad."
                : null;
}
