using Microsoft.AspNetCore.Identity;

namespace RealEstateApp.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? NationalId { get; set; }
    public string? NormalizedNationalId { get; set; }
    public string? Photo { get; set; }
    public bool IsActive { get; set; }
    public int AuthVersion { get; set; } = 1;
}
