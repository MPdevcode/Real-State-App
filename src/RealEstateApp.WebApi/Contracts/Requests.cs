using System.ComponentModel.DataAnnotations;
using RealEstateApp.Application;

namespace RealEstateApp.WebApi.Contracts;

public sealed class LoginRequest
{
    [Required, StringLength(254, MinimumLength = 3)]
    public string Login { get; init; } = "";

    [Required, StringLength(128, MinimumLength = 6)]
    public string Password { get; init; } = "";
}

public sealed class CreateAccountRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string FirstName { get; init; } = "";

    [Required, StringLength(100, MinimumLength = 2)]
    public string LastName { get; init; } = "";

    [Required, StringLength(100, MinimumLength = 3)]
    public string UserName { get; init; } = "";

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = "";

    [Required, StringLength(128, MinimumLength = 6)]
    public string Password { get; init; } = "";

    [Required, StringLength(128, MinimumLength = 6), Compare(nameof(Password))]
    public string ConfirmPassword { get; init; } = "";

    [Phone, StringLength(30, MinimumLength = 7)]
    public string? Phone { get; init; }

    [Required, StringLength(30, MinimumLength = 5)]
    public string NationalId { get; init; } = "";

    public RegisterInput ToInput(string role) => new(FirstName.Trim(), LastName.Trim(), UserName.Trim(),
        Email.Trim(), Password, ConfirmPassword, role, Phone?.Trim(), NationalId);
}

public sealed class ChangeAgentStatusRequest
{
    [Required(ErrorMessage = "El estado enviado no es válido.")]
    public bool? IsActive { get; init; }
}

public sealed class CatalogRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = "";

    [Required, StringLength(1000, MinimumLength = 5)]
    public string Description { get; init; } = "";

    public CatalogInput ToInput() => new(Name.Trim(), Description.Trim());
}

public sealed class PropertySearchRequest : IValidatableObject
{
    [StringLength(6, MinimumLength = 6), RegularExpression("^[0-9]{6}$")]
    public string? Code { get; init; }

    [Range(1, int.MaxValue)]
    public int? PropertyTypeId { get; init; }

    [Range(typeof(decimal), "0", "9999999999999999")]
    public decimal? MinPrice { get; init; }

    [Range(typeof(decimal), "0", "9999999999999999")]
    public decimal? MaxPrice { get; init; }

    [Range(0, int.MaxValue)]
    public int? Bedrooms { get; init; }

    [Range(0, int.MaxValue)]
    public int? Bathrooms { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice.HasValue && MaxPrice.HasValue && MinPrice.Value > MaxPrice.Value)
            yield return new ValidationResult("MaxPrice debe ser mayor o igual a MinPrice.", [nameof(MaxPrice)]);
    }

    public PropertyFilter ToFilter() => new()
    {
        Code = Code,
        PropertyTypeId = PropertyTypeId,
        MinPrice = MinPrice,
        MaxPrice = MaxPrice,
        Bedrooms = Bedrooms,
        Bathrooms = Bathrooms
    };
}

public static class RouteValidation
{
    public const string AgentIdPattern = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
}
