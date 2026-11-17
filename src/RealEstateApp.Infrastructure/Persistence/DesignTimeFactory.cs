using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RealEstateApp.Infrastructure.Persistence;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<RealEstateDbContext>
{
    public RealEstateDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=RealEstateApp;Trusted_Connection=True;MultipleActiveResultSets=False;TrustServerCertificate=True";
        return new(new DbContextOptionsBuilder<RealEstateDbContext>().UseSqlServer(connection).Options);
    }
}
