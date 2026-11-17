namespace RealEstateApp.Domain;

public abstract class Entity
{
    public int Id { get; set; }
}

public enum PropertyStatus { Available = 1, Sold = 2 }
public enum OfferStatus { Pending = 1, Rejected = 2, Accepted = 3 }
public enum CatalogKind { PropertyTypes = 1, SaleTypes = 2, Improvements = 3 }

public static class Roles
{
    public const string Client = "Cliente";
    public const string Agent = "Agente";
    public const string Admin = "Administrador";
    public const string Developer = "Desarrollador";
    public static readonly string[] All = [Client, Agent, Admin, Developer];
}

public sealed class Property : Entity
{
    public string Code { get; set; } = "";
    public string AgentId { get; set; } = "";
    public int PropertyTypeId { get; set; }
    public int SaleTypeId { get; set; }
    public decimal Price { get; set; }
    public decimal Size { get; set; }
    public int Bedrooms { get; set; }
    public int Bathrooms { get; set; }
    public string Description { get; set; } = "";
    public PropertyStatus Status { get; set; } = PropertyStatus.Available;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public byte[] RowVersion { get; set; } = [];
    public CatalogItem PropertyType { get; set; } = null!;
    public CatalogItem SaleType { get; set; } = null!;
    public List<PropertyImage> Images { get; set; } = [];
    public List<PropertyImprovement> Improvements { get; set; } = [];

    public void EnsureAvailable(string? message = null)
    {
        if (Status != PropertyStatus.Available)
            throw new BusinessException(message ?? "La propiedad ya fue vendida y no admite esta operación.");
    }

    public void EnsureOwner(string agentId, string? message = null)
    {
        if (AgentId != agentId) throw new ForbiddenException(message ?? "No tiene permisos sobre esta propiedad.");
    }
}

// A discriminator keeps the three identical catalog structures together; services enforce their kind.
public sealed class CatalogItem : Entity
{
    public CatalogKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class PropertyImage : Entity
{
    public int PropertyId { get; set; }
    public string FileKey { get; set; } = "";
    public int SortOrder { get; set; }
}

public sealed class PropertyImprovement
{
    public int PropertyId { get; set; }
    public int ImprovementId { get; set; }
    public CatalogItem Improvement { get; set; } = null!;
}

public sealed class Favorite : Entity
{
    public string ClientId { get; set; } = "";
    public int PropertyId { get; set; }
}

public sealed class Conversation : Entity
{
    public int PropertyId { get; set; }
    public string ClientId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public List<Message> Messages { get; set; } = [];
}

public sealed class Message : Entity
{
    public int ConversationId { get; set; }
    public string SenderId { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Offer : Entity
{
    public int PropertyId { get; set; }
    public string ClientId { get; set; } = "";
    public decimal Amount { get; set; }
    public OfferStatus Status { get; set; } = OfferStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }

    public void Respond(bool accept)
    {
        if (Status != OfferStatus.Pending) throw new BusinessException("Esta oferta ya fue respondida.");
        Status = accept ? OfferStatus.Accepted : OfferStatus.Rejected;
        RespondedAt = DateTimeOffset.UtcNow;
    }
}

public class BusinessException(string message) : Exception(message);
public sealed class ForbiddenException(string message) : BusinessException(message);
public sealed class NotFoundException(string message) : BusinessException(message);
public sealed class AuthenticationException(string message) : BusinessException(message);
