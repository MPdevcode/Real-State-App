using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using RealEstateApp.Infrastructure;
using RealEstateApp.Infrastructure.Identity;
using RealEstateApp.Infrastructure.Persistence;
using Xunit;

namespace RealEstateApp.Tests;

// Opt in from the parent PowerShell process:
// $env:REAL_ESTATE_SQL_TESTS = '1'; dotnet test tests/RealEstateApp.Tests/RealEstateApp.Tests.csproj
// Each test owns a new RealEstateApp_Test_<GUID> database on (localdb)\MSSQLLocalDB.
// EnsureCreated is intentional here only; migrations and all user databases are left untouched.
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("REAL_ESTATE_SQL_TESTS") != "1")
            Skip = "Set REAL_ESTATE_SQL_TESTS=1 in the parent process to enable isolated SQL Server LocalDB tests.";
    }
}

public sealed class SqlServerIntegrationTests
{
    [SqlFact]
    public async Task DatabaseCreatesSqlServerModelAndSeedsRealIdentityUsers()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var scope = database.CreateScope(Roles.Admin);
        var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
        Assert.True(db.Database.IsSqlServer());
        Assert.Equal(database.DatabaseName, db.Database.GetDbConnection().Database);
        Assert.Equal(3, await db.CatalogItems.CountAsync());
        Assert.Equal(4, await db.Roles.CountAsync());
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var role in Roles.All)
        {
            var user = Assert.Single(await users.GetUsersInRoleAsync(role));
            Assert.Equal(database.UserId(role), user.Id);
            Assert.True(user.IsActive);
            Assert.True(user.EmailConfirmed);
        }
        Assert.True(db.Model.FindEntityType(typeof(Property))!.FindProperty(nameof(Property.RowVersion))!.IsConcurrencyToken);
        var indexes = db.Model.FindEntityType(typeof(Offer))!.GetIndexes().Where(index => index.IsUnique).ToArray();
        Assert.Contains(indexes, index => index.GetFilter() == "[Status] = 1"
            && index.Properties.Select(property => property.Name).SequenceEqual([nameof(Offer.PropertyId), nameof(Offer.ClientId)]));
        Assert.Contains(indexes, index => index.GetFilter() == "[Status] = 3"
            && index.Properties.Select(property => property.Name).SequenceEqual([nameof(Offer.PropertyId)]));
        Assert.DoesNotContain(scope.ServiceProvider.GetServices<IHostedService>(), service => service is BackgroundJobs);
        Assert.IsType<OfferService>(scope.ServiceProvider.GetRequiredService<IOfferService>());
        Assert.IsType<AccountService>(scope.ServiceProvider.GetRequiredService<IAccountService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ITokenService>());
    }

    [SqlFact]
    public async Task PendingOfferIndexRejectsDuplicateClientButAllowsOtherClientsAndRejectedHistory()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var propertyId = await database.AddPropertyAsync();
        var otherClient = await database.AddUserAsync(Roles.Client);
        await using var scope = database.CreateScope(Roles.Client);
        var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
        var client = database.UserId(Roles.Client);
        db.Offers.AddRange(
            new Offer { PropertyId = propertyId, ClientId = client, Amount = 100 },
            new Offer { PropertyId = propertyId, ClientId = otherClient, Amount = 110 },
            new Offer { PropertyId = propertyId, ClientId = client, Amount = 90, Status = OfferStatus.Rejected },
            new Offer { PropertyId = propertyId, ClientId = client, Amount = 80, Status = OfferStatus.Rejected });
        await db.SaveChangesAsync();
        db.Offers.Add(new Offer { PropertyId = propertyId, ClientId = client, Amount = 120 });
        await AssertUniqueViolationAsync(db);
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Offers.CountAsync(offer => offer.Status == OfferStatus.Pending));
        Assert.Equal(2, await db.Offers.CountAsync(offer => offer.Status == OfferStatus.Rejected));
    }

    [SqlFact]
    public async Task AcceptedOfferIndexAllowsOnlyOneAcceptedOfferPerProperty()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var propertyId = await database.AddPropertyAsync();
        var otherClient = await database.AddUserAsync(Roles.Client);
        await using var scope = database.CreateScope(Roles.Agent);
        var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
        db.Offers.Add(new Offer { PropertyId = propertyId, ClientId = database.UserId(Roles.Client), Amount = 100, Status = OfferStatus.Accepted });
        await db.SaveChangesAsync();
        db.Offers.Add(new Offer { PropertyId = propertyId, ClientId = otherClient, Amount = 110, Status = OfferStatus.Accepted });
        await AssertUniqueViolationAsync(db);
        db.ChangeTracker.Clear();
        Assert.Single(await db.Offers.Where(offer => offer.Status == OfferStatus.Accepted).ToListAsync());
    }

    [SqlFact]
    public async Task FavoriteIndexRejectsDuplicatesButAllowsDifferentClients()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var propertyId = await database.AddPropertyAsync();
        var otherClient = await database.AddUserAsync(Roles.Client);
        await using var scope = database.CreateScope(Roles.Client);
        var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
        db.Favorites.AddRange(new Favorite { PropertyId = propertyId, ClientId = database.UserId(Roles.Client) },
            new Favorite { PropertyId = propertyId, ClientId = otherClient });
        await db.SaveChangesAsync();
        db.Favorites.Add(new Favorite { PropertyId = propertyId, ClientId = database.UserId(Roles.Client) });
        await AssertUniqueViolationAsync(db);
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Favorites.CountAsync());
    }

    [SqlFact]
    public async Task OfferServiceCreatesRejectsAndAcceptsUsingSeededParticipants()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var propertyId = await database.AddPropertyAsync();
        var otherClient = await database.AddUserAsync(Roles.Client);
        var first = await database.CreateOfferAsync(propertyId, 100);
        var competing = await database.CreateOfferAsync(propertyId, 110, otherClient);
        await using (var scope = database.CreateScope(Roles.Client))
        {
            var service = scope.ServiceProvider.GetRequiredService<IOfferService>();
            var visible = Assert.Single(await service.ListAsync(propertyId));
            Assert.Equal(first, visible.Id);
            Assert.NotEmpty(visible.ClientName);
            await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(propertyId, 120));
        }
        await using (var scope = database.CreateScope(Roles.Agent))
        {
            var service = scope.ServiceProvider.GetRequiredService<IOfferService>();
            Assert.Equal(2, (await service.ListAsync(propertyId)).Count);
            await service.RespondAsync(first, false);
        }
        await using (var scope = database.CreateScope(Roles.Agent))
        {
            var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
            Assert.Equal(PropertyStatus.Available, (await db.Properties.SingleAsync()).Status);
            Assert.Equal(OfferStatus.Pending, (await db.Offers.SingleAsync(offer => offer.Id == competing)).Status);
        }
        var replacement = await database.CreateOfferAsync(propertyId, 130);
        await using (var scope = database.CreateScope(Roles.Agent))
            await scope.ServiceProvider.GetRequiredService<IOfferService>().RespondAsync(replacement, true);
        await AssertSaleAsync(database, propertyId, accepted: 1, rejected: 2);
        await using (var scope = database.CreateScope(Roles.Client))
            await Assert.ThrowsAsync<BusinessException>(() => scope.ServiceProvider.GetRequiredService<IOfferService>().CreateAsync(propertyId, 140));
    }

    [SqlFact]
    public async Task ConcurrentAcceptsInIndependentScopesProduceOneWinnerAndRejectTheOtherOffer()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var propertyId = await database.AddPropertyAsync();
        var otherClient = await database.AddUserAsync(Roles.Client);
        var first = await database.CreateOfferAsync(propertyId, 100);
        var second = await database.CreateOfferAsync(propertyId, 110, otherClient);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;
        async Task<BusinessException?> AcceptAsync(int offerId)
        {
            await using var scope = database.CreateScope(Roles.Agent);
            var service = scope.ServiceProvider.GetRequiredService<IOfferService>();
            if (Interlocked.Increment(ref ready) == 2) start.SetResult();
            await start.Task;
            try { await service.RespondAsync(offerId, true); return null; }
            catch (BusinessException error) { return error; }
        }
        var outcomes = await Task.WhenAll(AcceptAsync(first), AcceptAsync(second)).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is not null);
        await AssertSaleAsync(database, propertyId, accepted: 1, rejected: 1);
    }

    [SqlFact]
    public async Task CreatingWhileAcceptingNeverLeavesPendingOffersOnASoldProperty()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var propertyId = await database.AddPropertyAsync();
        var otherClient = await database.AddUserAsync(Roles.Client);
        var offerId = await database.CreateOfferAsync(propertyId, 100);
        await using var agentScope = database.CreateScope(Roles.Agent);
        await using var clientScope = database.CreateScope(Roles.Client, otherClient);
        var agent = agentScope.ServiceProvider.GetRequiredService<IOfferService>();
        var client = clientScope.ServiceProvider.GetRequiredService<IOfferService>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<BusinessException?> RunAsync(Func<Task> action)
        {
            await start.Task;
            try { await action(); return null; }
            catch (BusinessException error) { return error; }
        }
        var accept = RunAsync(() => agent.RespondAsync(offerId, true));
        var create = RunAsync(() => client.CreateAsync(propertyId, 110));
        start.SetResult();
        await Task.WhenAll(accept, create).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Null(await accept);
        await AssertSaleAsync(database, propertyId, accepted: 1, rejected: await create is null ? 1 : 0);
    }

    [SqlFact]
    public async Task DeletingPropertyTypeRemovesAvailableAndSoldPropertiesAndAllChildren()
        => await AssertCatalogDeletionAsync(CatalogKind.PropertyTypes);

    [SqlFact]
    public async Task DeletingSaleTypeRemovesAvailableAndSoldPropertiesAndAllChildren()
        => await AssertCatalogDeletionAsync(CatalogKind.SaleTypes);

    [SqlFact]
    public async Task DeletingImprovementPreservesSoldPropertyWithNoRemainingImprovements()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var propertyId = await database.AddPropertyAsync(PropertyStatus.Sold);
        await using (var scope = database.CreateScope(Roles.Admin))
            await scope.ServiceProvider.GetRequiredService<ICatalogService>().For(CatalogKind.Improvements)
                .DeleteAsync(database.CatalogId(CatalogKind.Improvements));
        await using var verification = database.CreateScope(Roles.Admin);
        var db = verification.ServiceProvider.GetRequiredService<RealEstateDbContext>();
        var property = await db.Properties.Include(item => item.Improvements).Include(item => item.Images).SingleAsync();
        Assert.Equal(propertyId, property.Id);
        Assert.Equal(PropertyStatus.Sold, property.Status);
        Assert.Empty(property.Improvements);
        Assert.Empty(await db.PropertyImprovements.ToListAsync());
        Assert.Single(property.Images);
        Assert.Equal(2, await db.CatalogItems.CountAsync());
    }

    [SqlFact]
    public async Task AccountStatusChangesPersistAndCannotDeactivateSelfOrUseAFakeAdministrator()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var scope = database.CreateScope(Roles.Admin))
        {
            var service = scope.ServiceProvider.GetRequiredService<IAccountService>();
            await Assert.ThrowsAsync<ForbiddenException>(() => service.ChangeStatusAsync(database.UserId(Roles.Admin), false));
            await service.ChangeStatusAsync(database.UserId(Roles.Agent), false);
        }
        await using (var scope = database.CreateScope(Roles.Admin))
        {
            var service = scope.ServiceProvider.GetRequiredService<IAccountService>();
            Assert.False((await service.GetAsync(database.UserId(Roles.Agent))).IsActive);
            Assert.True((await service.GetAsync(database.UserId(Roles.Admin))).IsActive);
            await service.ChangeStatusAsync(database.UserId(Roles.Agent), true);
        }
        await using (var scope = database.CreateScope(Roles.Admin, Guid.NewGuid().ToString()))
            await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider.GetRequiredService<IAccountService>()
                .ChangeStatusAsync(database.UserId(Roles.Agent), false));
        await using var verification = database.CreateScope(Roles.Admin);
        Assert.True((await verification.ServiceProvider.GetRequiredService<IAccountService>().GetAsync(database.UserId(Roles.Agent))).IsActive);
    }

    [SqlFact]
    public async Task ConcurrentAdministratorsCannotDeactivateEachOtherAndLeaveNoActiveAdministrator()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var first = database.UserId(Roles.Admin);
        var second = await database.AddUserAsync(Roles.Admin);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<BusinessException?> DeactivateAsync(string actor, string target)
        {
            await using var scope = database.CreateScope(Roles.Admin, actor);
            var service = scope.ServiceProvider.GetRequiredService<IAccountService>();
            await start.Task;
            try { await service.ChangeStatusAsync(target, false); return null; }
            catch (BusinessException error) { return error; }
        }
        var firstAttempt = DeactivateAsync(first, second);
        var secondAttempt = DeactivateAsync(second, first);
        start.SetResult();
        var outcomes = await Task.WhenAll(firstAttempt, secondAttempt).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is not null);
        await using var verification = database.CreateScope(Roles.Admin);
        var accounts = await verification.ServiceProvider.GetRequiredService<IAccountService>().ListAsync(Roles.Admin, onlyActive: true);
        Assert.Single(accounts);
    }

    [SqlFact]
    public async Task ForcedFailureAfterOfferSaveRollsBackSaleAndAllOfferResponses()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var propertyId = await database.AddPropertyAsync();
        var otherClient = await database.AddUserAsync(Roles.Client);
        var offerId = await database.CreateOfferAsync(propertyId, 100);
        await database.CreateOfferAsync(propertyId, 110, otherClient);
        var forcedFailure = new InvalidOperationException("Forced failure after the SQL changes have been flushed.");
        await using (var scope = database.CreateScope(Roles.Agent))
        {
            var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var service = scope.ServiceProvider.GetRequiredService<IOfferService>();
            var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => work.ExecuteAsync(async () =>
            {
                await service.RespondAsync(offerId, true);
                await work.SaveChangesAsync();
                Assert.Equal(1, await db.Offers.CountAsync(offer => offer.Status == OfferStatus.Accepted));
                Assert.Equal(PropertyStatus.Sold, (await db.Properties.SingleAsync()).Status);
                throw forcedFailure;
            }));
            Assert.Same(forcedFailure, error);
            Assert.Empty(db.ChangeTracker.Entries());
        }
        await using var verification = database.CreateScope(Roles.Agent);
        var persisted = verification.ServiceProvider.GetRequiredService<RealEstateDbContext>();
        Assert.Equal(PropertyStatus.Available, (await persisted.Properties.SingleAsync()).Status);
        Assert.Equal(2, await persisted.Offers.CountAsync());
        Assert.All(await persisted.Offers.ToListAsync(), offer =>
        {
            Assert.Equal(OfferStatus.Pending, offer.Status);
            Assert.Null(offer.RespondedAt);
        });
    }

    private static async Task AssertUniqueViolationAsync(RealEstateDbContext db)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var sql = Assert.IsType<SqlException>(error.InnerException);
        Assert.Contains(sql.Number, new[] { 2601, 2627 });
    }

    private static async Task AssertSaleAsync(SqlTestDatabase database, int propertyId, int accepted, int rejected)
    {
        await using var scope = database.CreateScope(Roles.Agent);
        var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
        Assert.Equal(PropertyStatus.Sold, (await db.Properties.SingleAsync(property => property.Id == propertyId)).Status);
        var offers = await db.Offers.Where(offer => offer.PropertyId == propertyId).ToListAsync();
        Assert.Equal(accepted, offers.Count(offer => offer.Status == OfferStatus.Accepted));
        Assert.Equal(rejected, offers.Count(offer => offer.Status == OfferStatus.Rejected));
        Assert.DoesNotContain(offers, offer => offer.Status == OfferStatus.Pending);
        Assert.All(offers, offer => Assert.NotNull(offer.RespondedAt));
    }

    private static async Task AssertCatalogDeletionAsync(CatalogKind kind)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var available = await database.AddPropertyAsync();
        var sold = await database.AddPropertyAsync(PropertyStatus.Sold);
        await using (var scope = database.CreateScope(Roles.Admin))
        {
            var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
            foreach (var propertyId in new[] { available, sold })
            {
                db.Favorites.Add(new Favorite { PropertyId = propertyId, ClientId = database.UserId(Roles.Client) });
                db.Offers.Add(new Offer { PropertyId = propertyId, ClientId = database.UserId(Roles.Client), Amount = 100 });
                db.Conversations.Add(new Conversation
                {
                    PropertyId = propertyId, ClientId = database.UserId(Roles.Client), AgentId = database.UserId(Roles.Agent),
                    Messages = [new Message { SenderId = database.UserId(Roles.Client), Body = "Test message" }]
                });
            }
            await db.SaveChangesAsync();
        }
        // A new scope leaves the dependents untracked, so SQL Server must perform the cascades.
        await using (var scope = database.CreateScope(Roles.Admin))
            await scope.ServiceProvider.GetRequiredService<ICatalogService>().For(kind).DeleteAsync(database.CatalogId(kind));
        await using var verification = database.CreateScope(Roles.Admin);
        var persisted = verification.ServiceProvider.GetRequiredService<RealEstateDbContext>();
        Assert.Empty(await persisted.Properties.ToListAsync());
        Assert.Empty(await persisted.PropertyImages.ToListAsync());
        Assert.Empty(await persisted.PropertyImprovements.ToListAsync());
        Assert.Empty(await persisted.Favorites.ToListAsync());
        Assert.Empty(await persisted.Offers.ToListAsync());
        Assert.Empty(await persisted.Conversations.ToListAsync());
        Assert.Empty(await persisted.Messages.ToListAsync());
        Assert.False(await persisted.CatalogItems.AnyAsync(item => item.Id == database.CatalogId(kind)));
        Assert.Equal(2, await persisted.CatalogItems.CountAsync());
        Assert.Equal(4, await persisted.Users.CountAsync());
        Assert.Equal(2, await persisted.FileDeletionJobs.CountAsync());
    }
}

internal sealed class SqlTestDatabase : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly IConfiguration configuration;
    private readonly string storageRoot;
    private readonly Dictionary<string, string> users = [];
    private readonly Dictionary<CatalogKind, int> catalogs = [];
    public string DatabaseName { get; } = "RealEstateApp_Test_" + Guid.NewGuid().ToString("N");

    private SqlTestDatabase()
    {
        storageRoot = Path.Combine(Path.GetTempPath(), "opencode", DatabaseName);
        var connection = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB", InitialCatalog = DatabaseName,
            IntegratedSecurity = true, TrustServerCertificate = true, ConnectTimeout = 15, Pooling = false
        };
        configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection.ConnectionString,
            ["Jobs:Enabled"] = "false", ["Database:Initialize"] = "false", ["Seed:Enabled"] = "true",
            ["Seed:Password"] = "Aa1!" + Guid.NewGuid().ToString("N"),
            ["Jwt:Key"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ["Jwt:Issuer"] = "RealEstateApp.Tests", ["Jwt:Audience"] = "RealEstateApp.Tests",
            ["Jwt:LifetimeMinutes"] = "5", ["Email:Mode"] = "Pickup",
            ["Email:ActivationBaseUrl"] = "https://test.invalid/account/confirm-email", ["Storage:RootPath"] = storageRoot
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.RemoveAll<ICurrentUser>();
        services.AddScoped<MutableCurrentUser>();
        services.AddScoped<ICurrentUser>(serviceProvider => serviceProvider.GetRequiredService<MutableCurrentUser>());
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public static async Task<SqlTestDatabase> CreateAsync()
    {
        var database = new SqlTestDatabase();
        try
        {
            await using (var scope = database.provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
                database.AssertOwnedDatabase(db);
                Assert.True(await db.Database.EnsureCreatedAsync(), "Every SQL test must create a fresh, uniquely named database.");
            }
            await IdentitySeeder.SeedAsync(database.provider, database.configuration);
            await using (var scope = database.provider.CreateAsyncScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                foreach (var role in Roles.All)
                    database.users[role] = Assert.Single(await manager.GetUsersInRoleAsync(role)).Id;
                var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
                foreach (var kind in Enum.GetValues<CatalogKind>())
                    db.CatalogItems.Add(new CatalogItem { Kind = kind, Name = kind.ToString(), NormalizedName = kind.ToString().ToUpperInvariant() });
                await db.SaveChangesAsync();
                foreach (var item in await db.CatalogItems.ToListAsync()) database.catalogs[item.Kind] = item.Id;
            }
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public string UserId(string role) => users[role];
    public int CatalogId(CatalogKind kind) => catalogs[kind];

    public AsyncServiceScope CreateScope(string role, string? userId = null)
    {
        var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().As(userId ?? UserId(role), role);
        return scope;
    }

    public async Task<int> AddPropertyAsync(PropertyStatus status = PropertyStatus.Available)
    {
        await using var scope = CreateScope(Roles.Agent);
        var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
        var number = await db.Properties.CountAsync() + 100001;
        var property = new Property
        {
            Code = number.ToString(System.Globalization.CultureInfo.InvariantCulture), AgentId = UserId(Roles.Agent),
            PropertyTypeId = CatalogId(CatalogKind.PropertyTypes), SaleTypeId = CatalogId(CatalogKind.SaleTypes),
            Price = 1000, Size = 100, Bedrooms = 2, Bathrooms = 1, Description = "SQL test property", Status = status,
            Images = [new PropertyImage { FileKey = Guid.NewGuid().ToString("N") + ".jpg" }],
            Improvements = [new PropertyImprovement { ImprovementId = CatalogId(CatalogKind.Improvements) }]
        };
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        return property.Id;
    }

    public async Task<string> AddUserAsync(string role)
    {
        await using var scope = CreateScope(Roles.Admin);
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var address = Guid.NewGuid().ToString("N") + "@test.invalid";
        var user = new ApplicationUser { UserName = address, Email = address, FirstName = "Test", LastName = "User", IsActive = true, EmailConfirmed = true };
        var created = await manager.CreateAsync(user, "Aa1!" + Guid.NewGuid().ToString("N"));
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));
        var membership = await manager.AddToRoleAsync(user, role);
        Assert.True(membership.Succeeded, string.Join("; ", membership.Errors.Select(error => error.Description)));
        return user.Id;
    }

    public async Task<int> CreateOfferAsync(int propertyId, decimal amount, string? clientId = null)
    {
        await using var scope = CreateScope(Roles.Client, clientId);
        await scope.ServiceProvider.GetRequiredService<IOfferService>().CreateAsync(propertyId, amount);
        var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
        return (await db.Offers.SingleAsync(offer => offer.PropertyId == propertyId
            && offer.ClientId == (clientId ?? UserId(Roles.Client)) && offer.Status == OfferStatus.Pending)).Id;
    }

    private void AssertOwnedDatabase(RealEstateDbContext db)
    {
        const string prefix = "RealEstateApp_Test_";
        var connection = new SqlConnectionStringBuilder(db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string is missing."));
        if (!DatabaseName.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(DatabaseName[prefix.Length..], "N", out _)
            || connection.InitialCatalog != DatabaseName
            || !string.Equals(connection.DataSource, @"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to create or delete a database not owned by this test.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
            AssertOwnedDatabase(db);
            await db.Database.EnsureDeletedAsync();
        }
        finally
        {
            await provider.DisposeAsync();
            if (Directory.Exists(storageRoot)) Directory.Delete(storageRoot, recursive: true);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = typeof(SqlTestDatabase).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class MutableCurrentUser : ICurrentUser
    {
        private string role = "";
        public string Id { get; private set; } = "";
        public void As(string id, string roleName) { Id = id; role = roleName; }
        public bool IsInRole(string roleName) => role == roleName;
        public void RequireRole(string roleName)
        {
            if (string.IsNullOrWhiteSpace(Id) || !IsInRole(roleName)) throw new ForbiddenException("Test identity does not have the required role.");
        }
    }
}
