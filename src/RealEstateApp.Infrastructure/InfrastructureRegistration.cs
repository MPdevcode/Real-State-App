using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RealEstateApp.Application;
using RealEstateApp.Infrastructure.Email;
using RealEstateApp.Infrastructure.Files;
using RealEstateApp.Infrastructure.Identity;
using RealEstateApp.Infrastructure.Persistence;

namespace RealEstateApp.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RealEstateDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection.")));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddSingleton<StoragePaths>();
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<IEmailSender, QueuedEmailSender>();
        services.AddIdentityInfrastructure(configuration);
        services.AddDataProtection().SetApplicationName("RealEstateApp");
        services.AddOptions<KeyManagementOptions>().Configure<StoragePaths>((options, paths) =>
        {
            options.XmlRepository = new Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository(
                new DirectoryInfo(paths.Keys), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        });
        if (configuration.GetValue("Jobs:Enabled", true)) services.AddHostedService<BackgroundJobs>();
        return services;
    }

    public static async Task InitializeInfrastructureAsync(this IServiceProvider services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Database:Initialize")) return;
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
        await db.Database.MigrateAsync();
        await IdentitySeeder.SeedAsync(services, configuration);
    }
}
