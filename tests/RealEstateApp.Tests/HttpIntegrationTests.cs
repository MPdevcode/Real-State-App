using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RealEstateApp.Application;
using RealEstateApp.Application.Cqrs;
using RealEstateApp.Domain;
using RealEstateApp.Infrastructure;
using RealEstateApp.Infrastructure.Identity;
using RealEstateApp.Infrastructure.Persistence;
using Xunit;

namespace RealEstateApp.Tests;

// Opt in with REAL_ESTATE_SQL_TESTS=1. Each test owns its database, files, keys, and random credentials.
public sealed class HttpIntegrationTests
{
    // A real, one-pixel PNG fixture. Storage must decode and re-encode it, not just accept its MIME type.
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY0iZ+vY/AAYtAuY09dZ/AAAAAElFTkSuQmCC";

    [SqlFact]
    public async Task ApiUsesRealCredentialsRolesAndRevokesExistingDeveloperTokensAfterAdministratorStatusChange()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        using var anonymous = fixture.ApiClient();
        using (var response = await anonymous.GetAsync("/api/agents"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        }
        foreach (var role in new[] { Roles.Client, Roles.Agent })
        {
            using var response = await anonymous.PostAsJsonAsync("/api/account/login", new { Login = fixture.Email(role), fixture.Password });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
        using (var response = await anonymous.PostAsJsonAsync("/api/account/login", new { Login = fixture.Email(Roles.Admin), Password = fixture.Password + "incorrect" }))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var admin = await fixture.AuthenticatedApiAsync(Roles.Admin);
        using var developer = await fixture.AuthenticatedApiAsync(Roles.Developer);
        using (var response = await developer.GetAsync("/api/agents")) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var mvcAdmin = fixture.MvcClient();
        await LoginMvcAsync(mvcAdmin, fixture.Email(Roles.Admin), fixture.Password, "/Admin");
        var statusUrl = "/AdminUsers/Status?role=" + Uri.EscapeDataString(Roles.Developer) + "&id=" + fixture.UserId(Roles.Developer);
        using (var response = await PostFormAsync(mvcAdmin, statusUrl, statusUrl, new() { ["Active"] = "false" }))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var response = await developer.GetAsync("/api/agents")) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using (var response = await anonymous.PostAsJsonAsync("/api/account/login", new { Login = fixture.Email(Roles.Developer), fixture.Password }))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using (var response = await PostFormAsync(mvcAdmin, statusUrl, statusUrl, new() { ["Active"] = "true" }))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // Reactivation does not restore an old, correctly signed token.
        using (var response = await developer.GetAsync("/api/agents")) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var freshDeveloper = await fixture.AuthenticatedApiAsync(Roles.Developer);
        using (var response = await freshDeveloper.GetAsync("/api/agents")) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await admin.GetAsync("/api/agents")) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [SqlFact]
    public async Task ApiCatalogsExerciseAllCrudStatusesValidationRoleGuardsAndSwaggerOperations()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        using var admin = await fixture.AuthenticatedApiAsync(Roles.Admin);
        using var developer = await fixture.AuthenticatedApiAsync(Roles.Developer);
        foreach (var path in new[] { "/api/property-types", "/api/sale-types", "/api/improvements" })
        {
            using (var response = await admin.GetAsync(path)) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            using (var response = await developer.PostAsJsonAsync(path, new { Name = "Forbidden", Description = "Developer cannot write" }))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await admin.PostAsJsonAsync(path, new { Name = "", Description = "x" }))
                await AssertProblemAsync(response, HttpStatusCode.BadRequest);
            using var created = await admin.PostAsJsonAsync(path, new { Name = "HTTP catalog", Description = "Created through the actual API" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var item = (await created.Content.ReadFromJsonAsync<CatalogDto>())!;
            Assert.True(item.Id > 0);
            Assert.EndsWith(path + "/" + item.Id, created.Headers.Location!.OriginalString, StringComparison.Ordinal);
            using (var response = await developer.GetAsync(path + "/" + item.Id))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(item, await response.Content.ReadFromJsonAsync<CatalogDto>());
            }
            using (var response = await developer.GetAsync(path))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(item.Id, Assert.Single((await response.Content.ReadFromJsonAsync<List<CatalogDto>>())!).Id);
            }
            using (var response = await admin.PostAsJsonAsync(path, new { Name = " HTTP CATALOG ", Description = "Duplicate normalized name" }))
                await AssertProblemAsync(response, HttpStatusCode.BadRequest);
            using (var response = await developer.PutAsJsonAsync(path + "/" + item.Id, new { Name = "Forbidden", Description = "Cannot update this item" }))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await developer.DeleteAsync(path + "/" + item.Id)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await admin.PutAsJsonAsync(path + "/" + item.Id, new { Name = "Updated catalog", Description = "Updated through the actual API" }))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("Updated catalog", (await response.Content.ReadFromJsonAsync<CatalogDto>())!.Name);
            }
            using (var response = await admin.GetAsync(path + "/0")) await AssertProblemAsync(response, HttpStatusCode.BadRequest);
            using (var response = await admin.DeleteAsync(path + "/" + item.Id)) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            using (var response = await admin.GetAsync(path + "/" + item.Id)) await AssertProblemAsync(response, HttpStatusCode.NotFound);
            using (var response = await admin.PutAsJsonAsync(path + "/" + item.Id, new { Name = "Missing catalog", Description = "This item no longer exists" }))
                await AssertProblemAsync(response, HttpStatusCode.NotFound);
            using (var response = await admin.DeleteAsync(path + "/" + item.Id)) await AssertProblemAsync(response, HttpStatusCode.NotFound);
        }

        using var anonymous = fixture.ApiClient();
        using var swagger = await anonymous.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        using var document = await JsonDocument.ParseAsync(await swagger.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal(16, paths.EnumerateObject().Count());
        var verbs = new[] { "get", "post", "put", "delete", "patch" };
        Assert.Equal(25, paths.EnumerateObject().Sum(path => path.Value.EnumerateObject().Count(operation => verbs.Contains(operation.Name))));
        Assert.True(paths.GetProperty("/api/account/login").GetProperty("post").TryGetProperty("responses", out _));
        Assert.Equal("bearer", document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
    }

    [SqlFact]
    public async Task ApiPropertiesIncludeSoldAndInactiveAgentsAndValidateCodesIdsAndFilters()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        using var admin = await fixture.AuthenticatedApiAsync(Roles.Admin);
        using var developer = await fixture.AuthenticatedApiAsync(Roles.Developer);
        var agentId = fixture.UserId(Roles.Agent);
        using (var response = await admin.GetAsync("/api/properties")) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await developer.GetAsync($"/api/agents/{agentId}/properties")) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var available = await fixture.AddPropertyAsync(PropertyStatus.Available);
        var sold = await fixture.AddPropertyAsync(PropertyStatus.Sold);
        using (var response = await developer.GetAsync("/api/properties"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var items = (await response.Content.ReadFromJsonAsync<List<PropertyResponse>>())!;
            Assert.Equal(2, items.Count);
            Assert.Contains(items, property => property.Id == available.Id && property.Status == "Disponible");
            Assert.Contains(items, property => property.Id == sold.Id && property.Status == "Vendida");
            Assert.All(items, property => Assert.False(string.IsNullOrWhiteSpace(property.AgentName)));
        }
        using (var response = await developer.PatchAsJsonAsync($"/api/agents/{agentId}/status", new { IsActive = false }))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await admin.PatchAsJsonAsync($"/api/agents/{agentId}/status", new { IsActive = false }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await developer.GetAsync($"/api/agents/{agentId}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var agent = (await response.Content.ReadFromJsonAsync<AgentResponse>())!;
            Assert.False(agent.IsActive);
            Assert.Equal(2, agent.PropertyCount);
        }
        using (var response = await developer.GetAsync($"/api/agents/{agentId}/properties"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, (await response.Content.ReadFromJsonAsync<List<PropertyResponse>>())!.Count);
        }
        using (var response = await developer.GetAsync("/api/properties"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, (await response.Content.ReadFromJsonAsync<List<PropertyResponse>>())!.Count);
        }
        foreach (var property in new[] { available, sold })
        {
            foreach (var path in new[] { $"/api/properties/{property.Id}", $"/api/properties/by-code/{property.Code}" })
            {
                using var response = await developer.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(property.Id, (await response.Content.ReadFromJsonAsync<PropertyResponse>())!.Id);
            }
        }
        using (var response = await developer.GetAsync("/api/properties?Code=" + sold.Code))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(sold.Id, Assert.Single((await response.Content.ReadFromJsonAsync<List<PropertyResponse>>())!).Id);
        }
        foreach (var path in new[]
        {
            "/api/properties/0", "/api/properties/not-an-id", "/api/properties/by-code/12345",
            "/api/properties/by-code/abcdef", "/api/properties?MinPrice=5&MaxPrice=1", "/api/properties?Bedrooms=-1",
            "/api/properties?PropertyTypeId=0", "/api/agents/not-a-guid", "/api/agents/not-a-guid/properties"
        })
        {
            using var response = await developer.GetAsync(path);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        }
        foreach (var path in new[] { "/api/properties/2147483647", "/api/properties/by-code/999999", "/api/agents/" + Guid.NewGuid() })
        {
            using var response = await developer.GetAsync(path);
            await AssertProblemAsync(response, HttpStatusCode.NotFound);
        }
        using (var response = await admin.PatchAsJsonAsync($"/api/agents/{agentId}/status", new { }))
            await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        using (var response = await admin.PatchAsJsonAsync($"/api/agents/{Guid.NewGuid()}/status", new { IsActive = false }))
            await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [SqlFact]
    public async Task ApiPrivilegedRegistrationRequiresAdministratorAndEnforcesIdentityUniquenessAndPasswordPolicy()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        using var anonymous = fixture.ApiClient();
        using var admin = await fixture.AuthenticatedApiAsync(Roles.Admin);
        using var developer = await fixture.AuthenticatedApiAsync(Roles.Developer);
        foreach (var definition in new[] { (Path: "administrators", Role: Roles.Admin), (Path: "developers", Role: Roles.Developer) })
        {
            var input = new RegisterInput("HTTP", "Privileged", Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N") + "@test.invalid",
                fixture.Password, fixture.Password, definition.Role, NationalId: "HTTP-" + Guid.NewGuid().ToString("N")[..20]);
            var path = "/api/account/" + definition.Path;
            using (var response = await anonymous.PostAsJsonAsync(path, input)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using (var response = await developer.PostAsJsonAsync(path, input)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var created = await admin.PostAsJsonAsync(path, input);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var account = (await created.Content.ReadFromJsonAsync<AccountResponse>())!;
            Assert.Equal(definition.Role, account.Role);
            Assert.True(account.IsActive);
            Assert.Equal(input.Email, account.Email);
            using (var response = await anonymous.PostAsJsonAsync("/api/account/login", new { Login = input.UserName, fixture.Password }))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var token = (await response.Content.ReadFromJsonAsync<TokenResult>())!;
                Assert.Contains(definition.Role, token.Roles);
                Assert.False(string.IsNullOrWhiteSpace(token.Token));
            }
            using (var response = await admin.PostAsJsonAsync(path, input)) await AssertProblemAsync(response, HttpStatusCode.BadRequest);
            var duplicateNationalId = input with
            {
                UserName = Guid.NewGuid().ToString("N"), Email = Guid.NewGuid().ToString("N") + "@test.invalid",
                NationalId = input.NationalId!.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant()
            };
            using (var response = await admin.PostAsJsonAsync(path, duplicateNationalId)) await AssertProblemAsync(response, HttpStatusCode.BadRequest);
            var weakPassword = duplicateNationalId with { NationalId = "NEW-" + Guid.NewGuid().ToString("N")[..20], Password = "abcdefgh", ConfirmPassword = "abcdefgh" };
            using (var response = await admin.PostAsJsonAsync(path, weakPassword)) await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        }
    }

    [SqlFact]
    public async Task MvcRendersRolePagesValidatesFormsEnforcesAntiforgeryAndRevokesRealCookies()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        using var anonymous = fixture.MvcClient();
        using (var response = await anonymous.GetAsync("/Client")) AssertRedirect(response, "/Account/Login");
        using (var response = await anonymous.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Login"] = fixture.Email(Roles.Admin), ["Password"] = fixture.Password })))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await PostFormAsync(anonymous, "/Account/Login", "/Account/Login", new()))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Ingrese su", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            AssertNoIdentityCookie(response);
        }
        using (var response = await PostFormAsync(anonymous, "/Account/Register", "/Account/Register", new()))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("El nombre es requerido", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        using (var response = await PostFormAsync(anonymous, "/Account/Login", "/Account/Login", new()
            { ["Login"] = fixture.Email(Roles.Developer), ["Password"] = fixture.Password }))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("desarrolladores", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            AssertNoIdentityCookie(response);
        }

        foreach (var definition in new[] { (Role: Roles.Client, Page: "/Client"), (Role: Roles.Agent, Page: "/Agent"), (Role: Roles.Admin, Page: "/Admin") })
        {
            using var client = fixture.MvcClient();
            await LoginMvcAsync(client, fixture.Email(definition.Role), fixture.Password, definition.Page);
            var html = await GetHtmlAsync(client, definition.Page);
            Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
            var forbiddenPage = definition.Role == Roles.Admin ? "/Agent" : "/Admin";
            using var denied = await client.GetAsync(forbiddenPage);
            AssertRedirect(denied, "/Account/AccessDenied");
            using var error = await client.GetAsync(denied.Headers.Location);
            Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
            using var logout = await PostFormAsync(client, definition.Page, "/Account/Logout", new());
            Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
            using var afterLogout = await client.GetAsync(definition.Page);
            AssertRedirect(afterLogout, "/Account/Login");
        }

        using var agent = fixture.MvcClient();
        await LoginMvcAsync(agent, fixture.Email(Roles.Agent), fixture.Password, "/Agent");
        using var adminApi = await fixture.AuthenticatedApiAsync(Roles.Admin);
        using (var response = await adminApi.PatchAsJsonAsync($"/api/agents/{fixture.UserId(Roles.Agent)}/status", new { IsActive = false }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await agent.GetAsync("/Agent"))
        {
            AssertRedirect(response, "/Account/Login");
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("RealEstate.Identity=;", StringComparison.Ordinal));
        }
        using (var response = await adminApi.PatchAsJsonAsync($"/api/agents/{fixture.UserId(Roles.Agent)}/status", new { IsActive = true }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await agent.GetAsync("/Agent")) AssertRedirect(response, "/Account/Login");
    }

    [SqlFact]
    public async Task MvcPublicRegistrationStoresRealPhotosQueuesClientConfirmationAndActivatesAgentOnlyThroughAdministrator()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        using var client = fixture.MvcClient();
        using var admin = await fixture.AuthenticatedApiAsync(Roles.Admin);
        foreach (var role in new[] { Roles.Agent, Roles.Client })
        {
            var address = Guid.NewGuid().ToString("N") + "@test.invalid";
            var token = Antiforgery(await GetHtmlAsync(client, "/Account/Register"));
            using var input = Multipart(new()
            {
                ["__RequestVerificationToken"] = token, ["FirstName"] = "Registered", ["LastName"] = "Through HTTP",
                ["UserName"] = address, ["Email"] = address, ["Phone"] = "8095551234", ["Role"] = role,
                ["Password"] = fixture.Password, ["ConfirmPassword"] = fixture.Password
            }, "Photo");
            using (var response = await client.PostAsync("/Account/Register", input)) await AssertFormRedirectAsync(response, "/Account/Login");
            string userId;
            string photo;
            string? confirmation = null;
            await using (var scope = fixture.Api.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
                var user = await db.Users.SingleAsync(user => user.Email == address);
                userId = user.Id;
                photo = Assert.IsType<string>(user.Photo);
                Assert.False(user.IsActive);
                Assert.False(user.EmailConfirmed);
                var jobs = await db.EmailJobs.Where(job => job.To == address).ToListAsync();
                if (role == Roles.Client)
                {
                    var body = Assert.Single(jobs).Body;
                    var match = Regex.Match(body, "href=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1));
                    Assert.True(match.Success);
                    confirmation = WebUtility.HtmlDecode(match.Groups[1].Value);
                    Assert.Equal("localhost", new Uri(confirmation).Host);
                }
                else Assert.Empty(jobs);
            }
            using (var response = await client.GetAsync("/media/" + photo))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
                var bytes = await response.Content.ReadAsByteArrayAsync();
                Assert.True(bytes.Length > 2 && bytes[0] == 0xff && bytes[1] == 0xd8);
            }
            using (var response = await PostFormAsync(client, "/Account/Login", "/Account/Login", new() { ["Login"] = address, ["Password"] = fixture.Password }))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("inactivo", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
                AssertNoIdentityCookie(response);
            }
            if (role == Roles.Client)
            {
                using (var response = await client.GetAsync(confirmation)) AssertRedirect(response, "/Account/Login");
                await LoginMvcAsync(client, address, fixture.Password, "/Client");
                await GetHtmlAsync(client, "/Client");
                using (var response = await PostFormAsync(client, "/Client", "/Account/Logout", new())) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                await using (var scope = fixture.Api.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
                    var user = await db.Users.SingleAsync(user => user.Id == userId);
                    Assert.True(user.EmailConfirmed);
                    Assert.True(user.IsActive);
                    user.IsActive = false;
                    await db.SaveChangesAsync();
                }
                using (var response = await client.GetAsync(confirmation)) AssertRedirect(response, "/Account/Login");
                await using var verification = fixture.Api.Services.CreateAsyncScope();
                Assert.False((await verification.ServiceProvider.GetRequiredService<RealEstateDbContext>().Users.SingleAsync(user => user.Id == userId)).IsActive);
            }
            else
            {
                using (var response = await admin.PatchAsJsonAsync($"/api/agents/{userId}/status", new { IsActive = true }))
                    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                await LoginMvcAsync(client, address, fixture.Password, "/Agent");
                await GetHtmlAsync(client, "/Agent");
                using var logout = await PostFormAsync(client, "/Agent", "/Account/Logout", new());
                Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
            }
        }
    }

    [SqlFact]
    public async Task MvcCreatesPropertyWithDecodedImageAndPersistsFavoritesMessagesOffersAndSale()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        var catalogs = await fixture.AddCatalogsAsync();
        using var agent = fixture.MvcClient();
        using var client = fixture.MvcClient();
        await LoginMvcAsync(agent, fixture.Email(Roles.Agent), fixture.Password, "/Agent");
        await LoginMvcAsync(client, fixture.Email(Roles.Client), fixture.Password, "/Client");
        using (var response = await PostFormAsync(agent, "/Agent/Create", "/Agent/Create", new()))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("La descripción es requerida", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        var token = Antiforgery(await GetHtmlAsync(agent, "/Agent/Create"));
        using var input = Multipart(new()
        {
            ["__RequestVerificationToken"] = token,
            ["PropertyTypeId"] = catalogs[CatalogKind.PropertyTypes].ToString(), ["SaleTypeId"] = catalogs[CatalogKind.SaleTypes].ToString(),
            ["ImprovementIds"] = catalogs[CatalogKind.Improvements].ToString(), ["Price"] = "250000", ["Size"] = "125",
            ["Bedrooms"] = "3", ["Bathrooms"] = "2", ["Description"] = "Property created through real MVC multipart binding"
        }, "File1");
        using (var response = await agent.PostAsync("/Agent/Create", input)) await AssertFormRedirectAsync(response, "/Agent/Maintenance");
        int propertyId;
        string photo;
        await using (var scope = fixture.Api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
            var property = await db.Properties.Include(property => property.Images).Include(property => property.Improvements).SingleAsync();
            propertyId = property.Id;
            photo = Assert.Single(property.Images).FileKey;
            Assert.Single(property.Improvements);
            Assert.Equal(fixture.UserId(Roles.Agent), property.AgentId);
            Assert.Matches("^[0-9]{6}$", property.Code);
        }
        using (var response = await client.GetAsync("/media/" + photo)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = "/Client/Details/" + propertyId;
        using (var response = await PostFormAsync(client, details, "/Client/SetFavorite", new() { ["PropertyId"] = propertyId.ToString(), ["Favorite"] = "true" }))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var favorites = await GetHtmlAsync(client, "/Client/Favorites");
        Assert.Contains("/Client/Details/" + propertyId, favorites, StringComparison.Ordinal);
        Assert.Contains("Quitar favorita", favorites, StringComparison.Ordinal);
        using (var response = await PostFormAsync(client, details, "/Client/SendMessage/" + propertyId, new() { ["Message.Body"] = "Can I visit this property?" }))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var response = await PostFormAsync(client, details, "/Client/SendOffer/" + propertyId, new() { ["Offer.Amount"] = "240000" }))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        int offerId;
        await using (var scope = fixture.Api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
            Assert.Equal(fixture.UserId(Roles.Client), (await db.Favorites.SingleAsync()).ClientId);
            Assert.Equal("Can I visit this property?", (await db.Messages.SingleAsync()).Body);
            var offer = await db.Offers.SingleAsync();
            Assert.Equal(240000, offer.Amount);
            Assert.Equal(OfferStatus.Pending, offer.Status);
            offerId = offer.Id;
        }
        var agentDetails = "/Agent/Details/" + propertyId + "?clientId=" + fixture.UserId(Roles.Client);
        using (var response = await PostFormAsync(agent, agentDetails, "/Agent/Reply/" + propertyId, new()
            { ["clientId"] = fixture.UserId(Roles.Client), ["Message.Body"] = "Yes, please visit tomorrow." }))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var response = await PostFormAsync(agent, agentDetails, "/Agent/Respond/" + propertyId, new()
            { ["clientId"] = fixture.UserId(Roles.Client), ["OfferId"] = offerId.ToString(), ["Accept"] = "true" }))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var verification = fixture.Api.Services.CreateAsyncScope();
        var persisted = verification.ServiceProvider.GetRequiredService<RealEstateDbContext>();
        Assert.Equal(PropertyStatus.Sold, (await persisted.Properties.SingleAsync()).Status);
        Assert.Equal(OfferStatus.Accepted, (await persisted.Offers.SingleAsync()).Status);
        Assert.Equal(2, await persisted.Messages.CountAsync());
        Assert.Contains("Yes, please visit tomorrow.", await GetHtmlAsync(client, details), StringComparison.Ordinal);
        using var admin = await fixture.AuthenticatedApiAsync(Roles.Admin);
        using var apiProperty = await admin.GetAsync("/api/properties/" + propertyId);
        Assert.Equal(HttpStatusCode.OK, apiProperty.StatusCode);
        Assert.Equal("Vendida", (await apiProperty.Content.ReadFromJsonAsync<PropertyResponse>())!.Status);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal((int)expected, json.RootElement.GetProperty("status").GetInt32());
    }

    private static void AssertRedirect(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        var actualPath = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0];
        Assert.Equal(path, actualPath);
    }

    private static async Task AssertFormRedirectAsync(HttpResponseMessage response, string path)
    {
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var html = await response.Content.ReadAsStringAsync();
            var errors = Regex.Matches(html, "(?:<li>|<span[^>]*field-validation-error[^>]*>)(.*?)</(?:li|span)>",
                RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Select(match => WebUtility.HtmlDecode(match.Groups[1].Value));
            Assert.Fail("Form returned a validation page: " + string.Join("; ", errors));
        }
        AssertRedirect(response, path);
    }

    private static void AssertNoIdentityCookie(HttpResponseMessage response) => Assert.DoesNotContain(
        response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
        cookie => cookie.StartsWith("RealEstate.Identity=", StringComparison.Ordinal));

    private static async Task<string> GetHtmlAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        return await response.Content.ReadAsStringAsync();
    }

    private static string Antiforgery(string html)
    {
        var input = Regex.Match(html, "<input\\b[^>]*\\bname=\"__RequestVerificationToken\"[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        Assert.True(input.Success, "The rendered Razor form must contain an antiforgery input.");
        var value = Regex.Match(input.Value, "\\bvalue=\"([^\"]+)\"", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        Assert.True(value.Success);
        return WebUtility.HtmlDecode(value.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string formPage, string action, Dictionary<string, string> values)
    {
        values["__RequestVerificationToken"] = Antiforgery(await GetHtmlAsync(client, formPage));
        using var content = new FormUrlEncodedContent(values);
        return await client.PostAsync(action, content);
    }

    private static async Task LoginMvcAsync(HttpClient client, string login, string password, string target)
    {
        using var response = await PostFormAsync(client, "/Account/Login", "/Account/Login", new() { ["Login"] = login, ["Password"] = password });
        AssertRedirect(response, target);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("RealEstate.Identity=", StringComparison.Ordinal));
    }

    private static MultipartFormDataContent Multipart(Dictionary<string, string> values, string imageField)
    {
        var content = new MultipartFormDataContent();
        foreach (var value in values) content.Add(new StringContent(value.Value), value.Key);
        var image = new ByteArrayContent(Convert.FromBase64String(Png));
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(image, imageField, "fixture.png");
        return content;
    }

    private sealed class HttpFixture : IAsyncDisposable
    {
        private const string Prefix = "RealEstateApp_HttpTest_";
        private readonly string databaseName = Prefix + Guid.NewGuid().ToString("N");
        private readonly string connectionString;
        private readonly string storageRoot;
        private readonly Dictionary<string, string> userIds = [];
        private readonly Dictionary<string, string?> settings;
        public string Password { get; } = "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        public HttpFactory<RealEstateApp.WebApi.Program> Api { get; }
        private HttpFactory<global::Program> Mvc { get; }

        private HttpFixture()
        {
            storageRoot = Path.Combine(Path.GetTempPath(), "opencode", databaseName);
            connectionString = new SqlConnectionStringBuilder
            {
                DataSource = @"(localdb)\MSSQLLocalDB", InitialCatalog = databaseName,
                IntegratedSecurity = true, TrustServerCertificate = true, ConnectTimeout = 15, Pooling = false
            }.ConnectionString;
            settings = new()
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Jobs:Enabled"] = "false", ["Database:Initialize"] = "false", ["Seed:Enabled"] = "true",
                ["Seed:Password"] = Password, ["Jwt:Key"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                ["Jwt:Issuer"] = "RealEstateApp.HttpTests", ["Jwt:Audience"] = "RealEstateApp.HttpTests",
                ["Jwt:LifetimeMinutes"] = "5", ["PublicUrl"] = "https://localhost", ["Email:Mode"] = "Pickup",
                ["Email:ActivationBaseUrl"] = "https://localhost/Account/Confirm", ["Storage:RootPath"] = storageRoot
            };
            foreach (var definition in new[] { (Key: "Admin", Role: Roles.Admin), (Key: "Developer", Role: Roles.Developer), (Key: "Agent", Role: Roles.Agent), (Key: "Client", Role: Roles.Client) })
            {
                // Override per-role external settings too; no developer credentials may leak into this fixture.
                settings[$"Seed:{definition.Key}Password"] = Password;
                settings[$"Seed:{definition.Key}Email"] = Email(definition.Role);
                settings[$"Seed:{definition.Key}UserName"] = Email(definition.Role);
                settings[$"Seed:{definition.Key}Photo"] = null;
            }
            Api = new(settings);
            Mvc = new(settings);
        }

        public static async Task<HttpFixture> CreateAsync()
        {
            var fixture = new HttpFixture();
            try
            {
                await using (var db = fixture.OwnedContext()) Assert.True(await db.Database.EnsureCreatedAsync());
                var configuration = fixture.Api.Services.GetRequiredService<IConfiguration>();
                Assert.Equal(fixture.connectionString, configuration.GetConnectionString("DefaultConnection"));
                Assert.False(configuration.GetValue<bool>("Database:Initialize"));
                Assert.DoesNotContain(fixture.Api.Services.GetServices<IHostedService>(), service => service is BackgroundJobs);
                await IdentitySeeder.SeedAsync(fixture.Api.Services, configuration);
                await using var scope = fixture.Api.Services.CreateAsyncScope();
                var connection = new SqlConnectionStringBuilder(scope.ServiceProvider.GetRequiredService<RealEstateDbContext>().Database.GetConnectionString());
                Assert.Equal(fixture.databaseName, connection.InitialCatalog);
                Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource, ignoreCase: true);
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                foreach (var role in Roles.All) fixture.userIds[role] = Assert.Single(await manager.GetUsersInRoleAsync(role)).Id;
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public string UserId(string role) => userIds[role];
        public string Email(string role) => role switch
        {
            Roles.Admin => "admin@realestate.local", Roles.Developer => "developer@realestate.local",
            Roles.Agent => "agent@realestate.local", Roles.Client => "client@realestate.local",
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };

        public HttpClient ApiClient() => Api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false
        });

        public HttpClient MvcClient() => Mvc.CreateDefaultClient(new Uri("https://localhost"), new TestCookies());

        public async Task<HttpClient> AuthenticatedApiAsync(string role)
        {
            var client = ApiClient();
            try
            {
                using var response = await client.PostAsJsonAsync("/api/account/login", new { Login = Email(role), Password });
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.False(response.Headers.Contains("Set-Cookie"));
                var token = (await response.Content.ReadFromJsonAsync<TokenResult>())!;
                Assert.Contains(role, token.Roles);
                Assert.InRange(token.Expiration, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(6));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                return client;
            }
            catch { client.Dispose(); throw; }
        }

        public async Task<Dictionary<CatalogKind, int>> AddCatalogsAsync()
        {
            await using var scope = Api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
            foreach (var kind in Enum.GetValues<CatalogKind>())
                if (!await db.CatalogItems.AnyAsync(item => item.Kind == kind))
                    db.CatalogItems.Add(new CatalogItem { Kind = kind, Name = kind.ToString(), NormalizedName = kind.ToString().ToUpperInvariant(), Description = "HTTP fixture catalog" });
            await db.SaveChangesAsync();
            return await db.CatalogItems.ToDictionaryAsync(item => item.Kind, item => item.Id);
        }

        public async Task<Property> AddPropertyAsync(PropertyStatus status)
        {
            var catalogs = await AddCatalogsAsync();
            await using var scope = Api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
            var property = new Property
            {
                Code = (100001 + await db.Properties.CountAsync()).ToString(System.Globalization.CultureInfo.InvariantCulture),
                AgentId = UserId(Roles.Agent), PropertyTypeId = catalogs[CatalogKind.PropertyTypes], SaleTypeId = catalogs[CatalogKind.SaleTypes],
                Price = 100000, Size = 100, Bedrooms = 2, Bathrooms = 1, Description = "HTTP fixture property", Status = status,
                Improvements = [new PropertyImprovement { ImprovementId = catalogs[CatalogKind.Improvements] }]
            };
            db.Properties.Add(property);
            await db.SaveChangesAsync();
            return property;
        }

        private RealEstateDbContext OwnedContext()
        {
            var connection = new SqlConnectionStringBuilder(connectionString);
            if (!databaseName.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(databaseName[Prefix.Length..], "N", out _)
                || connection.InitialCatalog != databaseName || !string.Equals(connection.DataSource, @"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to create/delete a database not owned by this HTTP fixture.");
            return new RealEstateDbContext(new DbContextOptionsBuilder<RealEstateDbContext>().UseSqlServer(connectionString).Options);
        }

        public async ValueTask DisposeAsync()
        {
            try { await Mvc.DisposeAsync(); }
            finally
            {
                try { await Api.DisposeAsync(); }
                finally
                {
                    try
                    {
                        await using var db = OwnedContext();
                        await db.Database.EnsureDeletedAsync();
                    }
                    finally
                    {
                        if (Directory.Exists(storageRoot)) Directory.Delete(storageRoot, recursive: true);
                    }
                }
            }
        }
    }

    private sealed class HttpFactory<TProgram>(Dictionary<string, string?> settings) : WebApplicationFactory<TProgram> where TProgram : class
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Deferred minimal hosting turns host configuration into entry-point arguments. This
            // supplies Jwt:Key before Program reads it, without mutating process environment variables.
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(settings));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }

    private sealed class TestCookies : DelegatingHandler
    {
        private readonly CookieContainer cookies = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var header = cookies.GetCookieHeader(uri);
            if (header.Length > 0) request.Headers.Add("Cookie", header);
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (var value in values) cookies.SetCookies(uri, value);
            return response;
        }
    }
}
