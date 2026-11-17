using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using RealEstateApp.Infrastructure.Persistence;
using Xunit;

namespace RealEstateApp.Tests;

public sealed class ApplicationServicesTests
{
    [Fact]
    public void RegistrationResolvesServicesAndValidatesMapping()
    {
        var context = new Context();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IUnitOfWork>(context.Work);
        services.AddSingleton<ICurrentUser>(context.User);
        services.AddSingleton<IAccountService>(context.Accounts);
        services.AddSingleton<IFileStorage>(context.Files);
        services.AddApplication();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        Assert.IsType<CatalogService>(scope.ServiceProvider.GetRequiredService<ICatalogService>());
        Assert.IsType<PropertyService>(scope.ServiceProvider.GetRequiredService<IPropertyService>());
        Assert.IsType<FavoriteService>(scope.ServiceProvider.GetRequiredService<IFavoriteService>());
        Assert.IsType<OfferService>(scope.ServiceProvider.GetRequiredService<IOfferService>());
        Assert.IsType<ConversationService>(scope.ServiceProvider.GetRequiredService<IConversationService>());
        Assert.IsType<DashboardService>(scope.ServiceProvider.GetRequiredService<IDashboardService>());
        provider.GetRequiredService<IMapper>().ConfigurationProvider.AssertConfigurationIsValid();
    }

    [Theory]
    [InlineData("public", 1, 2)]
    [InlineData("favorites", 1, 2)]
    [InlineData("agent", 3, 3)]
    [InlineData("maintenance", 1, 1)]
    [InlineData("api", 5, 5)]
    public async Task SearchEnforcesVisibility(string scope, int expected, int expectedMaterialized)
    {
        var context = new Context();
        context.User.As(scope switch { "agent" or "maintenance" => Roles.Agent, "api" => Roles.Developer, _ => Roles.Client });
        context.Property(2).Status = PropertyStatus.Sold;
        context.Property(3).AgentId = "inactive";
        context.Property(4).AgentId = "other";
        context.Property(4).Status = PropertyStatus.Sold;
        context.Property(5).Status = PropertyStatus.Sold;
        context.Work.Repository<Favorite>().Add(new Favorite { PropertyId = 1, ClientId = "client" });
        context.Work.Repository<Favorite>().Add(new Favorite { PropertyId = 3, ClientId = "client" });
        context.Work.Repository<Favorite>().Add(new Favorite { PropertyId = 2, ClientId = "client" });
        Assert.Equal(expected, (await context.Properties.SearchAsync(new PropertyFilter(), scope)).Count);
        var repository = (MemoryRepository<Property>)context.Work.Repository<Property>();
        Assert.NotNull(repository.LastFilter);
        Assert.Equal(expectedMaterialized, repository.Items.Count(repository.LastFilter.Compile()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public async Task BlankSearchCodeMeansNoCodeFilterWithoutMutatingCaller(string? code)
    {
        var context = new Context();
        var filter = new PropertyFilter { Code = code };
        Assert.Equal(4, (await context.Properties.SearchAsync(filter)).Count);
        Assert.Equal(code, filter.Code);
    }

    [Fact]
    public async Task SearchPushesAgentAndEveryOptionalFilterIntoRepository()
    {
        var context = new Context();
        context.User.As(Roles.Developer);
        var filter = new PropertyFilter { Code = "100001", PropertyTypeId = 1, MinPrice = 5, MaxPrice = 20, Bedrooms = 0, Bathrooms = 0 };
        Assert.Single(await context.Properties.SearchAsync(filter, "api", "agent"));
        var repository = (MemoryRepository<Property>)context.Work.Repository<Property>();
        Assert.NotNull(repository.LastFilter);
        var matches = repository.LastFilter.Compile();
        var property = context.Property(1);
        Assert.True(matches(property));
        property.AgentId = "other";
        Assert.False(matches(property));
        property.AgentId = "agent";
        property.Code = "999999";
        Assert.False(matches(property));
        property.Code = "100001";
        property.PropertyTypeId = 2;
        Assert.False(matches(property));
        property.PropertyTypeId = 1;
        property.Price = 4;
        Assert.False(matches(property));
        property.Price = 21;
        Assert.False(matches(property));
        property.Price = 10;
        property.Bedrooms = 1;
        Assert.False(matches(property));
        property.Bedrooms = 0;
        property.Bathrooms = 1;
        Assert.False(matches(property));
    }

    [Theory]
    [InlineData("public")]
    [InlineData("favorites")]
    [InlineData("agent")]
    [InlineData("maintenance")]
    [InlineData("api")]
    public async Task SearchPredicateTranslatesToSqlServerWithoutConnecting(string scope)
    {
        var context = new Context();
        context.User.As(scope switch { "agent" or "maintenance" => Roles.Agent, "api" => Roles.Developer, _ => Roles.Client });
        context.Work.Repository<Favorite>().Add(new Favorite { PropertyId = 1, ClientId = "client" });
        await context.Properties.SearchAsync(new PropertyFilter
        {
            Code = "100001", PropertyTypeId = 1, MinPrice = 5, MaxPrice = 20, Bedrooms = 0, Bathrooms = 0
        }, scope, "agent");
        var repository = (MemoryRepository<Property>)context.Work.Repository<Property>();
        Assert.NotNull(repository.LastFilter);
        await using var database = new RealEstateDbContext(new DbContextOptionsBuilder<RealEstateDbContext>()
            .UseSqlServer("Server=localhost;Database=QueryTranslationOnly;Integrated Security=True;TrustServerCertificate=True").Options);
        var sql = database.Properties.Where(repository.LastFilter).ToQueryString();
        Assert.Contains("WHERE", sql);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task ClientCanReadOwnSoldHistoryButOtherClientsAndPublicCannot(bool offerHistory, bool activeAgent)
    {
        var context = new Context();
        context.Property(1).Status = PropertyStatus.Sold;
        if (!activeAgent) context.Property(1).AgentId = "inactive";
        if (offerHistory)
            context.Work.Repository<Offer>().Add(new Offer { PropertyId = 1, ClientId = "client", Status = OfferStatus.Rejected });
        else
            context.Work.Repository<Conversation>().Add(new Conversation { PropertyId = 1, ClientId = "client", AgentId = context.Property(1).AgentId });
        Assert.Equal(PropertyStatus.Sold, (await context.Properties.GetAsync(1, "client")).Status);
        await Assert.ThrowsAsync<NotFoundException>(() => context.Properties.GetAsync(1, "public"));
        context.User.As(Roles.Client, "other-client");
        await Assert.ThrowsAsync<NotFoundException>(() => context.Properties.GetAsync(1, "client"));
    }

    [Fact]
    public async Task FavoritesAndHistoryOnAnotherPropertyDoNotGrantSoldAccess()
    {
        var context = new Context();
        context.Property(1).Status = PropertyStatus.Sold;
        context.Work.Repository<Favorite>().Add(new Favorite { PropertyId = 1, ClientId = "client" });
        context.Work.Repository<Offer>().Add(new Offer { PropertyId = 2, ClientId = "client" });
        context.Work.Repository<Conversation>().Add(new Conversation { PropertyId = 2, ClientId = "client", AgentId = "agent" });
        await Assert.ThrowsAsync<NotFoundException>(() => context.Properties.GetAsync(1, "client"));
    }

    [Fact]
    public async Task ClientScopeAllowsActiveAvailablePropertyButExcludesInactiveEvenWithHistory()
    {
        var context = new Context();
        Assert.Equal(1, (await context.Properties.GetAsync(1, "client")).Id);
        context.Work.Repository<Offer>().Add(new Offer { PropertyId = 3, ClientId = "client" });
        context.Work.Repository<Conversation>().Add(new Conversation { PropertyId = 3, ClientId = "client", AgentId = "inactive" });
        await Assert.ThrowsAsync<NotFoundException>(() => context.Properties.GetAsync(3, "client"));
    }

    [Theory]
    [InlineData(Roles.Agent)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Developer)]
    public async Task ClientDetailScopeRequiresClientRole(string role)
    {
        var context = new Context();
        context.User.As(role);
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Properties.GetAsync(1, "client"));
    }

    [Fact]
    public async Task ClientDetailScopeRequiresAuthenticatedIdentity()
    {
        var context = new Context();
        context.User.As(Roles.Client, string.Empty);
        await Assert.ThrowsAsync<AuthenticationException>(() => context.Properties.GetAsync(1, "client"));
    }

    [Fact]
    public async Task AgentCannotSpoofSearchOwner()
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Properties.SearchAsync(new PropertyFilter(), "agent", "other"));
        await Assert.ThrowsAsync<NotFoundException>(() => context.Properties.GetAsync(3, "agent"));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("\uFF11\uFF12\uFF13\uFF14\uFF15\uFF16")]
    public async Task ApiLookupValidatesSixAsciiDigits(string code)
    {
        var context = new Context();
        context.User.As(Roles.Developer);
        await Assert.ThrowsAsync<BusinessException>(() => context.Properties.GetByCodeAsync(code));
    }

    [Fact]
    public async Task ApiLookupDoesNotAllowClients()
    {
        var context = new Context();
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Properties.GetByCodeAsync("123456"));
    }

    [Fact]
    public async Task FiltersRejectInvalidRangesAndWrongCatalogKind()
    {
        var context = new Context();
        foreach (var filter in new[]
        {
            new PropertyFilter { MinPrice = -1 }, new PropertyFilter { MaxPrice = -1 },
            new PropertyFilter { MinPrice = 2, MaxPrice = 1 }, new PropertyFilter { Bedrooms = -1 },
            new PropertyFilter { Bathrooms = -1 }, new PropertyFilter { PropertyTypeId = 2 },
            new PropertyFilter { PropertyTypeId = 999 }
        })
            await Assert.ThrowsAsync<BusinessException>(() => context.Properties.SearchAsync(filter));
    }

    [Fact]
    public async Task PropertyCreationSetsOwnerCodeAndMapsGraph()
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        var id = await context.Properties.CreateAsync(Context.Input(), [Context.Upload()]);
        var created = await context.Properties.GetAsync(id, "agent");
        Assert.Equal("agent", created.AgentId);
        Assert.Matches("^[1-9][0-9]{5}$", created.Code);
        Assert.Equal("House", created.PropertyType);
        Assert.Single(created.Images);
        Assert.Single(created.Improvements);
    }

    [Fact]
    public async Task PropertyInputValidationRejectsInvalidFields()
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        var valid = Context.Input();
        foreach (var input in new[]
        {
            valid with { Price = 0 }, valid with { Size = -1 }, valid with { Bedrooms = -1 },
            valid with { Bathrooms = -1 }, valid with { Description = " " }, valid with { Description = new string('x', 4001) },
            valid with { ImprovementIds = [] }, valid with { ImprovementIds = [3, 3] },
            valid with { ImprovementIds = [1] }, valid with { PropertyTypeId = 2 }, valid with { SaleTypeId = 1 },
            valid with { PropertyTypeId = 0 }, valid with { SaleTypeId = -1 },
            valid with { RetainedImageIds = [1] }
        })
            await Assert.ThrowsAsync<BusinessException>(() => context.Properties.CreateAsync(input, [Context.Upload()]));
        await Assert.ThrowsAsync<BusinessException>(() => context.Properties.CreateAsync(valid, []));
        await Assert.ThrowsAsync<BusinessException>(() => context.Properties.CreateAsync(valid,
            [Context.Upload(), Context.Upload(), Context.Upload(), Context.Upload(), Context.Upload()]));
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("1.234")]
    [InlineData("10000000000000000")]
    [InlineData("79228162514264337593543950335")]
    public async Task PropertyPriceAndSizeMustFitDecimal18Scale2OnCreateAndUpdate(string text)
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        foreach (var input in new[] { Context.Input() with { Price = value }, Context.Input() with { Size = value } })
        {
            await Assert.ThrowsAsync<BusinessException>(() => context.Properties.CreateAsync(input, [Context.Upload()]));
            await Assert.ThrowsAsync<BusinessException>(() => context.Properties.UpdateAsync(1, input, [Context.Upload()]));
        }
        Assert.Empty(context.Files.Saved);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("9999999999999999.99")]
    [InlineData("1.2300")]
    public async Task PropertyPriceAndSizeAcceptExactStorageBoundaries(string text)
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var input = Context.Input() with { Price = value, Size = value };
        var id = await context.Properties.CreateAsync(input, [Context.Upload()]);
        await context.Properties.UpdateAsync(id, input, [Context.Upload()]);
        Assert.Equal(value, context.Property(id).Price);
        Assert.Equal(value, context.Property(id).Size);
    }

    [Fact]
    public async Task CreationCompensatesFilesWhenCommitFails()
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        context.Work.CommitError = new InvalidOperationException("commit failed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Properties.CreateAsync(Context.Input(), [Context.Upload()]));
        Assert.Equal(context.Files.Saved, context.Files.Deleted);
    }

    [Fact]
    public async Task CreationCompensatesEarlierFilesWhenLaterUploadFails()
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        context.Files.FailSaveNumber = 2;
        await Assert.ThrowsAsync<IOException>(() => context.Properties.CreateAsync(Context.Input(), [Context.Upload(), Context.Upload()]));
        Assert.Single(context.Files.Deleted);
        Assert.Equal(context.Files.Saved, context.Files.Deleted);
    }

    [Fact]
    public async Task FailedCompensationPreservesOriginalFailure()
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        context.Work.CommitError = new InvalidOperationException("commit failed");
        context.Files.FailDelete = true;
        var error = await Assert.ThrowsAsync<AggregateException>(() => context.Properties.CreateAsync(Context.Input(), [Context.Upload()]));
        Assert.Contains(error.InnerExceptions, item => item == context.Work.CommitError);
        Assert.Contains(error.InnerExceptions, item => item is IOException);
    }

    [Fact]
    public async Task UpdateChecksImageOwnershipAfterLock()
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        context.Work.RequirePropertyLock = true;
        await Assert.ThrowsAsync<BusinessException>(() => context.Properties.UpdateAsync(1,
            Context.Input() with { RetainedImageIds = [999] }, []));
        Assert.Equal(1, context.Work.LockCount);
        Assert.Empty(context.Files.Saved);
    }

    [Fact]
    public async Task UpdateRemovesOldImageFromGraphWithoutDirectFileDeletion()
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        context.Work.RequirePropertyLock = true;
        await context.Properties.UpdateAsync(1, Context.Input(), [Context.Upload()]);
        Assert.DoesNotContain(context.Property(1).Images, image => image.FileKey == "old-1");
        Assert.Equal(context.Files.Saved.Single(), Assert.Single(context.Property(1).Images).FileKey);
        Assert.Empty(context.Files.Deleted);
    }

    [Fact]
    public async Task UpdateSucceedsWithoutCallingFileDeletionWhenStorageDeletionWouldFail()
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        context.Files.FailDelete = true;
        await context.Properties.UpdateAsync(1, Context.Input(), [Context.Upload()]);
        Assert.Empty(context.Files.Deleted);
        Assert.Single(context.Files.Saved);
    }

    [Fact]
    public async Task FailedUpdateKeepsOldFilesAndCompensatesNewFiles()
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        context.Work.CommitError = new InvalidOperationException("commit failed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Properties.UpdateAsync(1, Context.Input(), [Context.Upload()]));
        Assert.Equal(context.Files.Saved, context.Files.Deleted);
        Assert.DoesNotContain("old-1", context.Files.Deleted);
    }

    [Fact]
    public async Task CodeGenerationStopsAfterTenCollisionsWithoutSavingFiles()
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        context.Work.ForceCodeCollision = true;
        await Assert.ThrowsAsync<BusinessException>(() => context.Properties.CreateAsync(Context.Input(), [Context.Upload()]));
        Assert.Equal(10, context.Work.PropertyQueryCount);
        Assert.Empty(context.Files.Saved);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SoldPropertyCannotBeUpdatedOrDeleted(bool update)
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        context.Property(1).Status = PropertyStatus.Sold;
        context.Work.RequirePropertyLock = true;
        await Assert.ThrowsAsync<BusinessException>(() => update
            ? context.Properties.UpdateAsync(1, Context.Input(), [Context.Upload()])
            : context.Properties.DeleteAsync(1));
        Assert.Empty(context.Files.Saved);
    }

    [Fact]
    public async Task PropertyMutationRejectsOtherOwner()
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Properties.UpdateAsync(3, Context.Input(), [Context.Upload()]));
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Properties.DeleteAsync(3));
    }

    [Fact]
    public async Task FavoritesAreIdempotentAndScopedToCurrentClient()
    {
        var context = new Context();
        context.Work.RequirePropertyLock = true;
        context.Work.Repository<Favorite>().Add(new Favorite { PropertyId = 1, ClientId = "another-client" });
        var service = new FavoriteService(context.Work, context.User, context.Accounts);
        await service.SetAsync(1, true);
        await service.SetAsync(1, true);
        Assert.Equal(2, (await context.Work.Repository<Favorite>().ListAsync()).Count);
        await service.SetAsync(1, false);
        await service.SetAsync(1, false);
        Assert.Equal("another-client", (await context.Work.Repository<Favorite>().ListAsync()).Single().ClientId);
        await Assert.ThrowsAsync<NotFoundException>(() => service.SetAsync(3, true));
    }

    [Fact]
    public async Task OfferCreationRequiresAvailablePropertyAndNoPendingDuplicate()
    {
        var context = new Context();
        context.Work.RequirePropertyLock = true;
        await context.Offers.CreateAsync(1, 10);
        await Assert.ThrowsAsync<BusinessException>(() => context.Offers.CreateAsync(1, 20));
        await Assert.ThrowsAsync<BusinessException>(() => context.Offers.CreateAsync(1, 0));
        await Assert.ThrowsAsync<NotFoundException>(() => context.Offers.CreateAsync(3, 10));
        context.Property(1).Status = PropertyStatus.Sold;
        await Assert.ThrowsAsync<BusinessException>(() => context.Offers.CreateAsync(1, 20));
    }

    [Fact]
    public async Task AcceptingOfferRejectsOtherPendingAndSellsInsideOneTransaction()
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        context.Work.RequirePropertyLock = true;
        var target = new Offer { Id = 1, PropertyId = 1, ClientId = "client", Amount = 10 };
        var other = new Offer { Id = 2, PropertyId = 1, ClientId = "another-client", Amount = 20 };
        var rejected = new Offer { Id = 3, PropertyId = 1, ClientId = "client", Amount = 30, Status = OfferStatus.Rejected };
        context.Work.Repository<Offer>().Add(target);
        context.Work.Repository<Offer>().Add(other);
        context.Work.Repository<Offer>().Add(rejected);
        await context.Offers.RespondAsync(1, true);
        Assert.Equal(OfferStatus.Accepted, target.Status);
        Assert.Equal(OfferStatus.Rejected, other.Status);
        Assert.Equal(PropertyStatus.Sold, context.Property(1).Status);
        Assert.NotNull(target.RespondedAt);
        Assert.NotNull(other.RespondedAt);
        Assert.Null(rejected.RespondedAt);
        Assert.Equal(1, context.Work.TransactionCount);
        await Assert.ThrowsAsync<BusinessException>(() => context.Offers.RespondAsync(2, true));
    }

    [Fact]
    public async Task RejectingOfferDoesNotSellOrRejectOthers()
    {
        var context = new Context();
        context.User.As(Roles.Agent);
        context.Work.Repository<Offer>().Add(new Offer { Id = 1, PropertyId = 1, ClientId = "client" });
        context.Work.Repository<Offer>().Add(new Offer { Id = 2, PropertyId = 1, ClientId = "other-client" });
        await context.Offers.RespondAsync(1, false);
        Assert.Equal(OfferStatus.Pending, (await context.Work.Repository<Offer>().FindAsync(2))!.Status);
        Assert.Equal(PropertyStatus.Available, context.Property(1).Status);
        await Assert.ThrowsAsync<BusinessException>(() => context.Offers.RespondAsync(1, false));
    }

    [Fact]
    public async Task OffersAreVisibleOnlyToParticipants()
    {
        var context = new Context();
        context.Work.Repository<Offer>().Add(new Offer { Id = 1, PropertyId = 1, ClientId = "client" });
        context.Work.Repository<Offer>().Add(new Offer { Id = 2, PropertyId = 1, ClientId = "other-client" });
        Assert.Single(await context.Offers.ListAsync(1));
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Offers.ListAsync(1, "other-client"));
        context.User.As(Roles.Agent);
        Assert.Equal(2, (await context.Offers.ListAsync(1)).Count);
        context.Property(1).AgentId = "other";
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Offers.RespondAsync(1, true));
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Offers.ListAsync(1));
    }

    [Fact]
    public async Task OnlyClientStartsConversationAndBothParticipantsCanReplyUntilSold()
    {
        var context = new Context();
        context.Work.RequirePropertyLock = true;
        context.User.As(Roles.Agent);
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Conversations.SendAsync(1, "client", "hello"));
        context.User.As(Roles.Client);
        await context.Conversations.SendAsync(1, null, " hello ");
        context.User.As(Roles.Agent);
        await context.Conversations.SendAsync(1, "client", "reply");
        context.Work.RequirePropertyLock = false;
        var conversation = Assert.Single(await context.Conversations.ListAsync(1));
        Assert.Equal(2, conversation.Messages.Count);
        Assert.Equal("hello", conversation.Messages[0].Body);
        context.Property(1).Status = PropertyStatus.Sold;
        context.Work.RequirePropertyLock = true;
        await Assert.ThrowsAsync<BusinessException>(() => context.Conversations.SendAsync(1, "client", "sold"));
        context.User.As(Roles.Client);
        await Assert.ThrowsAsync<BusinessException>(() => context.Conversations.SendAsync(1, null, "sold"));
    }

    [Fact]
    public async Task ConversationsRejectSpoofedParticipantsAndInvalidBodies()
    {
        var context = new Context();
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Conversations.SendAsync(1, "other-client", "hello"));
        await Assert.ThrowsAsync<BusinessException>(() => context.Conversations.SendAsync(1, null, " "));
        await Assert.ThrowsAsync<BusinessException>(() => context.Conversations.SendAsync(1, null, new string('x', 4001)));
        context.Work.Repository<Conversation>().Add(new Conversation { PropertyId = 1, AgentId = "agent", ClientId = "other-client" });
        Assert.Empty(await context.Conversations.ListAsync(1));
        context.User.As(Roles.Agent);
        Assert.Single(await context.Conversations.ListAsync(1));
        context.Property(1).AgentId = "other";
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Conversations.ListAsync(1));
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Conversations.SendAsync(1, "other-client", "hello"));
    }

    [Fact]
    public async Task CatalogWritesRequireAdminAndNormalizeNamesPerKind()
    {
        var context = new Context();
        var catalog = context.Catalogs.For(CatalogKind.PropertyTypes);
        Assert.NotEmpty(await catalog.ListAsync());
        await Assert.ThrowsAsync<ForbiddenException>(() => catalog.CreateAsync(new CatalogInput("New", "Description")));
        context.User.As(Roles.Admin);
        var created = await catalog.CreateAsync(new CatalogInput("  Condo  ", " description "));
        Assert.Equal("Condo", created.Name);
        Assert.Equal("description", created.Description);
        await Assert.ThrowsAsync<BusinessException>(() => catalog.CreateAsync(new CatalogInput("CONDO", "Description")));
        await context.Catalogs.For(CatalogKind.SaleTypes).CreateAsync(new CatalogInput("condo", "Description"));
        await Assert.ThrowsAsync<NotFoundException>(() => catalog.GetAsync(2));
    }

    [Theory]
    [InlineData(CatalogKind.PropertyTypes, 1)]
    [InlineData(CatalogKind.SaleTypes, 2)]
    [InlineData(CatalogKind.Improvements, 3)]
    public async Task CatalogNamesAndDescriptionsAreRequiredAndBounded(CatalogKind kind, int id)
    {
        var context = new Context();
        context.User.As(Roles.Admin);
        var catalog = context.Catalogs.For(kind);
        foreach (var input in new[]
        {
            new CatalogInput(" ", "Description"), new CatalogInput(new string('n', 101), "Description"),
            new CatalogInput("Name", ""), new CatalogInput("Name", " \t\r\n"),
            new CatalogInput("Name", new string('d', 2001))
        })
        {
            await Assert.ThrowsAsync<BusinessException>(() => catalog.CreateAsync(input));
            await Assert.ThrowsAsync<BusinessException>(() => catalog.UpdateAsync(id, input));
        }
        var boundary = new CatalogInput(new string('n', 100), new string('d', 2000));
        var created = await catalog.CreateAsync(boundary);
        Assert.Equal(100, created.Name.Length);
        Assert.Equal(2000, created.Description.Length);
        Assert.Equal(created, await catalog.UpdateAsync(created.Id, boundary));
    }

    [Theory]
    [InlineData(CatalogKind.PropertyTypes, 1)]
    [InlineData(CatalogKind.SaleTypes, 2)]
    public async Task CatalogDeletionIncludesSoldProperties(CatalogKind kind, int id)
    {
        var context = new Context();
        context.User.As(Roles.Admin);
        context.Property(1).Status = PropertyStatus.Sold;
        await context.Catalogs.For(kind).DeleteAsync(id);
        Assert.Equal(5, context.Work.DeletedPropertyIds.Count);
        Assert.Contains(1, context.Work.DeletedPropertyIds);
        Assert.True(context.Work.DeletePropertiesWasTransactional);
    }

    [Fact]
    public async Task ImprovementDeletionDoesNotDeleteProperties()
    {
        var context = new Context();
        context.User.As(Roles.Admin);
        await context.Catalogs.For(CatalogKind.Improvements).DeleteAsync(3);
        Assert.Empty(context.Work.DeletedPropertyIds);
    }

    [Fact]
    public async Task DashboardRequiresAdminAndReturnsEightCounts()
    {
        var context = new Context();
        var service = new DashboardService(context.Work, context.User, context.Accounts);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync());
        context.User.As(Roles.Admin);
        context.Property(1).Status = PropertyStatus.Sold;
        context.Accounts.Users.Add(Context.Account("inactive-client", Roles.Client, false));
        context.Accounts.Users.Add(Context.Account("developer", Roles.Developer));
        context.Accounts.Users.Add(Context.Account("inactive-developer", Roles.Developer, false));
        Assert.Equal(new DashboardDto(4, 1, 2, 1, 2, 1, 1, 1), await service.GetAsync());
    }

    private sealed class Context
    {
        public MemoryWork Work { get; } = new();
        public CurrentUser User { get; } = new();
        public Accounts Accounts { get; } = new();
        public Files Files { get; } = new();
        public IMapper Mapper { get; }
        public PropertyService Properties => new(Work, User, Accounts, Files, Mapper);
        public OfferService Offers => new(Work, User, Accounts, Mapper);
        public ConversationService Conversations => new(Work, User, Accounts, Mapper);
        public CatalogService Catalogs => new(Work, User, Mapper);

        public Context()
        {
            var configuration = new MapperConfiguration(config => config.AddProfile<ApplicationMappingProfile>(),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
            Mapper = configuration.CreateMapper();
            Accounts.Users.AddRange([Account("agent", Roles.Agent), Account("inactive", Roles.Agent, false),
                Account("other", Roles.Agent), Account("client", Roles.Client), Account("other-client", Roles.Client)]);
            Work.Repository<CatalogItem>().Add(new CatalogItem { Id = 1, Kind = CatalogKind.PropertyTypes, Name = "House", NormalizedName = "HOUSE" });
            Work.Repository<CatalogItem>().Add(new CatalogItem { Id = 2, Kind = CatalogKind.SaleTypes, Name = "Sale", NormalizedName = "SALE" });
            Work.Repository<CatalogItem>().Add(new CatalogItem { Id = 3, Kind = CatalogKind.Improvements, Name = "Pool", NormalizedName = "POOL" });
            for (var id = 1; id <= 5; id++)
                Work.Repository<Property>().Add(new Property
                {
                    Id = id, Code = (100000 + id).ToString(), AgentId = id == 3 ? "inactive" : "agent",
                    PropertyTypeId = 1, SaleTypeId = 2, Price = 10, Size = 10, Description = "House",
                    PropertyType = Work.Items<CatalogItem>().Single(item => item.Id == 1),
                    SaleType = Work.Items<CatalogItem>().Single(item => item.Id == 2),
                    Images = [new PropertyImage { Id = id, PropertyId = id, FileKey = $"old-{id}" }],
                    Improvements = [new PropertyImprovement { PropertyId = id, ImprovementId = 3,
                        Improvement = Work.Items<CatalogItem>().Single(item => item.Id == 3) }]
                });
        }

        public Property Property(int id) => Work.Items<Property>().Single(item => item.Id == id);
        public static PropertyInput Input() => new(1, 2, 10, 10, 1, 1, " description ", [3], []);
        public static FileUpload Upload() => new("photo.png", "image/png", Stream.Null);
        public static UserDto Account(string id, string role, bool active = true)
            => new(id, id, "Last", id, id + "@test.invalid", null, null, null, active, role);
    }

    private sealed class CurrentUser : ICurrentUser
    {
        public string Id { get; private set; } = "client";
        private string role = Roles.Client;
        public void As(string value, string? id = null) { role = value; Id = id ?? (value == Roles.Agent ? "agent" : value == Roles.Client ? "client" : "admin"); }
        public bool IsInRole(string value) => role == value;
        public void RequireRole(string value) { if (!IsInRole(value)) throw new ForbiddenException("Role denied."); }
    }

    private sealed class MemoryWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> repositories = [];
        public bool Executing { get; private set; }
        public bool RequirePropertyLock { get; set; }
        private bool locked;
        public int LockCount { get; private set; }
        public int TransactionCount { get; private set; }
        public int PropertyQueryCount { get; private set; }
        public bool ForceCodeCollision { get; set; }
        public Exception? CommitError { get; set; }
        public List<int> DeletedPropertyIds { get; } = [];
        public bool DeletePropertiesWasTransactional { get; private set; }
        public List<T> Items<T>() where T : Entity => ((MemoryRepository<T>)Repository<T>()).Items;
        public IRepository<T> Repository<T>() where T : Entity
        {
            if (!repositories.TryGetValue(typeof(T), out var repository))
            {
                repository = new MemoryRepository<T>(this);
                repositories.Add(typeof(T), repository);
            }
            return (IRepository<T>)repository;
        }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public async Task ExecuteAsync(Func<Task> action, CancellationToken ct = default)
        {
            TransactionCount++;
            Executing = true;
            locked = false;
            try
            {
                await action();
                if (CommitError is not null) throw CommitError;
                foreach (var property in Items<Property>())
                {
                    property.PropertyType = Items<CatalogItem>().Single(item => item.Id == property.PropertyTypeId);
                    property.SaleType = Items<CatalogItem>().Single(item => item.Id == property.SaleTypeId);
                }
            }
            finally { Executing = false; locked = false; }
        }
        public Task LockPropertyAsync(int propertyId, CancellationToken ct = default)
        {
            Assert.True(Executing);
            LockCount++;
            locked = true;
            return Task.CompletedTask;
        }
        public Task DeletePropertiesAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
        {
            DeletePropertiesWasTransactional = Executing;
            DeletedPropertyIds.AddRange(ids);
            Items<Property>().RemoveAll(item => ids.Contains(item.Id));
            return Task.CompletedTask;
        }
        public void CheckRead<T>() where T : Entity
        {
            if (typeof(T) == typeof(Property)) PropertyQueryCount++;
            if (typeof(T) == typeof(Property) && RequirePropertyLock) Assert.True(Executing && locked, "Property read must follow its transactional lock.");
        }
    }

    private sealed class MemoryRepository<T>(MemoryWork work) : IRepository<T> where T : Entity
    {
        public List<T> Items { get; } = [];
        public Expression<Func<T, bool>>? LastFilter { get; private set; }
        public Task<T?> FindAsync(int id, CancellationToken ct = default)
        {
            work.CheckRead<T>();
            return Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
        }
        public Task<List<T>> ListAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            work.CheckRead<T>();
            LastFilter = filter;
            if (typeof(T) == typeof(Property) && work.ForceCodeCollision) return Task.FromResult(Items.Take(1).ToList());
            return Task.FromResult(filter is null ? Items.ToList() : Items.Where(filter.Compile()).ToList());
        }
        public void Add(T entity)
        {
            if (entity.Id == 0) entity.Id = Items.Select(item => item.Id).DefaultIfEmpty().Max() + 1;
            Items.Add(entity);
        }
        public void Remove(T entity) => Items.Remove(entity);
    }

    private sealed class Files : IFileStorage
    {
        public List<string> Saved { get; } = [];
        public List<string> Deleted { get; } = [];
        public int FailSaveNumber { get; set; }
        public bool FailDelete { get; set; }
        public Task<string> SaveImageAsync(FileUpload upload, CancellationToken ct = default)
        {
            if (FailSaveNumber == Saved.Count + 1) throw new IOException("upload failed");
            var key = "new-" + Saved.Count;
            Saved.Add(key);
            return Task.FromResult(key);
        }
        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            Deleted.Add(key);
            if (FailDelete) throw new IOException("delete failed");
            return Task.CompletedTask;
        }
    }

    private sealed class Accounts : IAccountService
    {
        public List<UserDto> Users { get; } = [];
        public Task<UserDto> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(Users.Single(user => user.Id == id));
        public Task<List<UserDto>> ListAsync(string role, bool onlyActive = false, CancellationToken ct = default)
            => Task.FromResult(Users.Where(user => user.Role == role && (!onlyActive || user.IsActive)).ToList());
        public Task<LoginResult> LoginAsync(string login, string password, bool api, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> RegisterAsync(RegisterInput input, FileUpload? photo = null, string? activationBaseUrl = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ConfirmEmailAsync(string userId, string token, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(string id, UpdateUserInput input, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ChangeStatusAsync(string id, bool active, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAgentAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateProfileAsync(ProfileInput input, FileUpload? photo, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
