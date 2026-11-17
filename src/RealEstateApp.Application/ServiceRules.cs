using RealEstateApp.Domain;

namespace RealEstateApp.Application;

internal static class ServiceRules
{
    public static string RequireRole(ICurrentUser user, string role)
    {
        if (string.IsNullOrWhiteSpace(user.Id)) throw new AuthenticationException("Debe iniciar sesión.");
        user.RequireRole(role);
        return user.Id;
    }

    public static void RequireApi(ICurrentUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Id)) throw new AuthenticationException("Debe iniciar sesión.");
        if (!user.IsInRole(Roles.Admin) && !user.IsInRole(Roles.Developer))
            throw new ForbiddenException("Solo administradores y desarrolladores pueden usar esta operación.");
    }

    public static void ValidateCode(string code)
    {
        if (code is null || code.Length != 6 || code.Any(character => character is < '0' or > '9'))
            throw new BusinessException("El código debe tener seis dígitos.");
    }

    public static async Task<Property> PropertyAsync(IUnitOfWork work, int id, CancellationToken ct)
        => (await work.Repository<Property>().ListAsync(property => property.Id == id, ct)).SingleOrDefault()
            ?? throw new NotFoundException("La propiedad solicitada no existe.");

    public static async Task<CatalogItem> CatalogAsync(IUnitOfWork work, int id, CatalogKind kind, CancellationToken ct)
    {
        var item = await work.Repository<CatalogItem>().FindAsync(id, ct);
        if (item is null || item.Kind != kind) throw new BusinessException("El catálogo seleccionado no es válido.");
        return item;
    }

    public static async Task RequirePublicAsync(Property property, IAccountService accounts, CancellationToken ct,
        string? soldMessage = null)
    {
        property.EnsureAvailable(soldMessage);
        var agent = await accounts.GetAsync(property.AgentId, ct);
        if (!agent.IsActive || agent.Role != Roles.Agent)
            throw new NotFoundException("La propiedad solicitada no existe o no se encuentra disponible.");
    }

    // Try every key even if one deletion fails; callers distinguish rollback from post-commit cleanup.
    public static async Task<List<Exception>> DeleteFilesAsync(IFileStorage files, IEnumerable<string> keys)
    {
        var errors = new List<Exception>();
        foreach (var key in keys.Distinct())
        {
            try { await files.DeleteAsync(key, CancellationToken.None); }
            catch (Exception error) { errors.Add(new IOException($"No se pudo eliminar la imagen '{key}'.", error)); }
        }
        return errors;
    }
}
