using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using RealEstateApp.Domain;

namespace RealEstateApp.Application;

public interface IRepository<T> where T : Entity
{
    Task<T?> FindAsync(int id, CancellationToken ct = default);
    Task<List<T>> ListAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);
    void Add(T entity);
    void Remove(T entity);
}

public interface IUnitOfWork
{
    IRepository<T> Repository<T>() where T : Entity;
    Task SaveChangesAsync(CancellationToken ct = default);
    Task ExecuteAsync(Func<Task> action, CancellationToken ct = default);
    Task LockPropertyAsync(int propertyId, CancellationToken ct = default);
    Task DeletePropertiesAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);
}

public interface ICurrentUser
{
    string Id { get; }
    bool IsInRole(string role);
    void RequireRole(string role);
}

public sealed record FileUpload(string Name, string ContentType, Stream Content);
public interface IFileStorage
{
    Task<string> SaveImageAsync(FileUpload upload, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}

public sealed record UserDto(string Id, string FirstName, string LastName, string UserName,
    string Email, string? Phone, string? Photo, string? NationalId, bool IsActive, string Role, int PropertyCount = 0)
{
    public string FullName => $"{FirstName} {LastName}";
}
public sealed record LoginResult(string UserId, string UserName, string Role);
public sealed record TokenResult(string Token, string User, IReadOnlyList<string> Roles, DateTimeOffset Expiration);
public sealed record RegisterInput(string FirstName, string LastName, string UserName, string Email,
    string Password, string ConfirmPassword, string Role, string? Phone = null, string? NationalId = null);
public sealed record UpdateUserInput(string FirstName, string LastName, string UserName, string Email,
    string NationalId, string? Password, string? ConfirmPassword);
public sealed record ProfileInput(string FirstName, string LastName, string Phone);

public interface IAccountService
{
    Task<LoginResult> LoginAsync(string login, string password, bool api, CancellationToken ct = default);
    Task<string> RegisterAsync(RegisterInput input, FileUpload? photo = null, string? activationBaseUrl = null, CancellationToken ct = default);
    Task ConfirmEmailAsync(string userId, string token, CancellationToken ct = default);
    Task<List<UserDto>> ListAsync(string role, bool onlyActive = false, CancellationToken ct = default);
    Task<UserDto> GetAsync(string id, CancellationToken ct = default);
    Task UpdateAsync(string id, UpdateUserInput input, CancellationToken ct = default);
    Task ChangeStatusAsync(string id, bool active, CancellationToken ct = default);
    Task DeleteAgentAsync(string id, CancellationToken ct = default);
    Task UpdateProfileAsync(ProfileInput input, FileUpload? photo, CancellationToken ct = default);
}

public interface ITokenService
{
    Task<TokenResult> CreateAsync(LoginResult login, CancellationToken ct = default);
}

public interface IWebSessionService
{
    Task SignInAsync(LoginResult login);
    Task SignOutAsync();
}

public sealed class PropertyFilter
{
    [RegularExpression("^[0-9]{6}$", ErrorMessage = "El código debe tener seis dígitos.")]
    public string? Code { get; set; }
    public int? PropertyTypeId { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999")]
    public decimal? MinPrice { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999")]
    public decimal? MaxPrice { get; set; }
    [Range(0, int.MaxValue)] public int? Bedrooms { get; set; }
    [Range(0, int.MaxValue)] public int? Bathrooms { get; set; }
}

public sealed record CatalogDto(int Id, string Name, string Description, int PropertyCount = 0);
public sealed record CatalogInput(string Name, string Description);
public sealed record PropertyDto(int Id, string Code, int PropertyTypeId, string PropertyType,
    int SaleTypeId, string SaleType, decimal Price, decimal Size, int Bedrooms, int Bathrooms,
    string Description, string AgentId, string AgentName, string? AgentEmail, string? AgentPhone,
    string? AgentPhoto, PropertyStatus Status, DateTimeOffset CreatedAt,
    IReadOnlyList<ImageDto> Images, IReadOnlyList<CatalogDto> Improvements, bool IsFavorite = false);
public sealed record ImageDto(int Id, string FileKey, int SortOrder);
public sealed record PropertyInput(int PropertyTypeId, int SaleTypeId, decimal Price, decimal Size,
    int Bedrooms, int Bathrooms, string Description, IReadOnlyList<int> ImprovementIds,
    IReadOnlyList<int> RetainedImageIds);
public sealed record OfferDto(int Id, int PropertyId, string ClientId, string ClientName,
    decimal Amount, OfferStatus Status, DateTimeOffset CreatedAt);
public sealed record MessageDto(int Id, string SenderId, string Body, DateTimeOffset CreatedAt);
public sealed record ConversationDto(int Id, int PropertyId, string ClientId, string ClientName,
    string AgentId, IReadOnlyList<MessageDto> Messages);
public sealed record DashboardDto(int AvailableProperties, int SoldProperties, int ActiveAgents,
    int InactiveAgents, int ActiveClients, int InactiveClients, int ActiveDevelopers, int InactiveDevelopers);

public interface IGenericService<TRead, in TWrite>
{
    Task<List<TRead>> ListAsync(CancellationToken ct = default);
    Task<TRead> GetAsync(int id, CancellationToken ct = default);
    Task<TRead> CreateAsync(TWrite input, CancellationToken ct = default);
    Task<TRead> UpdateAsync(int id, TWrite input, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
public interface ICatalogService
{
    IGenericService<CatalogDto, CatalogInput> For(CatalogKind kind);
}
public interface IPropertyService
{
    Task<List<PropertyDto>> SearchAsync(PropertyFilter filter, string scope = "public", string? agentId = null, CancellationToken ct = default);
    Task<PropertyDto> GetAsync(int id, string scope = "public", CancellationToken ct = default);
    Task<PropertyDto> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<int> CreateAsync(PropertyInput input, IReadOnlyList<FileUpload> images, CancellationToken ct = default);
    Task UpdateAsync(int id, PropertyInput input, IReadOnlyList<FileUpload> images, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
public interface IFavoriteService
{
    Task SetAsync(int propertyId, bool favorite, CancellationToken ct = default);
}
public interface IOfferService
{
    Task<List<OfferDto>> ListAsync(int propertyId, string? clientId = null, CancellationToken ct = default);
    Task CreateAsync(int propertyId, decimal amount, CancellationToken ct = default);
    Task RespondAsync(int offerId, bool accept, CancellationToken ct = default);
}
public interface IConversationService
{
    Task<List<ConversationDto>> ListAsync(int propertyId, CancellationToken ct = default);
    Task SendAsync(int propertyId, string? clientId, string body, CancellationToken ct = default);
}
public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken ct = default);
}
