using RealEstateApp.Domain;

namespace RealEstateApp.Application.Cqrs;

internal static class AgentIdentity
{
    public static string Parse(string id) => id.Length == 36 && Guid.TryParseExact(id, "D", out var parsed)
        ? parsed.ToString("D")
        : throw new BusinessException("El identificador del agente debe ser un GUID válido.");
}
