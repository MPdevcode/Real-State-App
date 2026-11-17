using RealEstateApp.Domain;

namespace RealEstateApp.Application.Cqrs;

public sealed record AccountResponse(string Id, string FirstName, string LastName, string UserName,
    string Email, string? Phone, bool IsActive, string Role)
{
    public static AccountResponse From(UserDto user) => new(user.Id, user.FirstName, user.LastName,
        user.UserName, user.Email, user.Phone, user.IsActive, user.Role);
}

public sealed record AgentResponse(string Id, string FirstName, string LastName, int PropertyCount,
    string Email, string? Phone, bool IsActive)
{
    public static AgentResponse From(UserDto user) => new(user.Id, user.FirstName, user.LastName,
        user.PropertyCount, user.Email, user.Phone, user.IsActive);
}

public sealed record PropertyResponse(int Id, string Code, int PropertyTypeId, string PropertyType,
    int SaleTypeId, string SaleType, decimal Price, decimal Size, int Bedrooms, int Bathrooms,
    string Description, IReadOnlyList<CatalogDto> Improvements, string AgentId, string AgentName,
    string Status)
{
    public static PropertyResponse From(PropertyDto property) => new(property.Id, property.Code,
        property.PropertyTypeId, property.PropertyType, property.SaleTypeId, property.SaleType,
        property.Price, property.Size, property.Bedrooms, property.Bathrooms, property.Description,
        property.Improvements, property.AgentId, property.AgentName,
        property.Status switch
        {
            PropertyStatus.Available => "Disponible",
            PropertyStatus.Sold => "Vendida",
            _ => throw new InvalidOperationException("Unknown property status.")
        });
}
