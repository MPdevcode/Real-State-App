using System.ComponentModel.DataAnnotations;
using System.Reflection;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using RealEstateApp.WebApp.Mapping;
using RealEstateApp.WebApp.ViewModels;
using Xunit;

namespace RealEstateApp.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void DomainDoesNotReferenceOuterLayersOrFrameworkInfrastructure()
    {
        var forbidden = new[] { "RealEstateApp.Application", "RealEstateApp.Infrastructure", "RealEstateApp.Web",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions.Identity", "System.Net.Http" };
        Assert.All(typeof(Property).Assembly.GetReferencedAssemblies(), reference =>
            Assert.False(forbidden.Any(prefix => reference.Name!.StartsWith(prefix, StringComparison.Ordinal)), reference.FullName));
    }

    [Fact]
    public void ApplicationDoesNotReferenceInfrastructureWebEntityFrameworkOrIdentity()
    {
        var forbidden = new[] { "RealEstateApp.Infrastructure", "RealEstateApp.Web", "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore", "Microsoft.Extensions.Identity" };
        Assert.All(typeof(IPropertyService).Assembly.GetReferencedAssemblies(), reference =>
            Assert.False(forbidden.Any(prefix => reference.Name!.StartsWith(prefix, StringComparison.Ordinal)), reference.FullName));
    }

    [Fact]
    public void WebControllerConstructorsDoNotDependOnInfrastructureRepositoriesOrDatabaseContexts()
    {
        var controllers = new[] { typeof(WebMappingProfile).Assembly, typeof(WebApi.Controllers.PropertiesController).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && type.IsClass).ToArray();
        Assert.NotEmpty(controllers);
        foreach (var controller in controllers)
        foreach (var parameter in controller.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     .SelectMany(constructor => constructor.GetParameters()))
            AssertPresentationDependency(parameter.ParameterType, $"{controller.FullName}({parameter.Name})");
    }

    [Fact]
    public async Task ApiExposesExactlyTheExpected25RoutesWithEffectiveAuthorizationIncludingInheritedActions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            manager.ApplicationParts.Clear();
            manager.ApplicationParts.Add(new AssemblyPart(typeof(WebApi.Controllers.PropertiesController).Assembly));
        });
        using var provider = services.BuildServiceProvider();
        var actions = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>().ToArray();
        var expected = new List<string>
        {
            "POST api/account/login", "POST api/account/developers", "POST api/account/administrators",
            "GET api/agents", "GET api/agents/{id}", "GET api/agents/{id}/properties", "PATCH api/agents/{id}/status",
            "GET api/properties", "GET api/properties/{id}", "GET api/properties/by-code/{code}"
        };
        foreach (var catalog in new[] { "property-types", "sale-types", "improvements" })
            expected.AddRange([$"GET api/{catalog}", $"GET api/{catalog}/{{id}}", $"POST api/{catalog}",
                $"PUT api/{catalog}/{{id}}", $"DELETE api/{catalog}/{{id}}"]);

        Assert.Equal(25, actions.Length);
        var actual = actions.Select(action =>
        {
            var constraint = Assert.Single(action.ActionConstraints!.OfType<HttpMethodActionConstraint>());
            return $"{Assert.Single(constraint.HttpMethods)} {action.AttributeRouteInfo!.Template}";
        }).ToArray();
        Assert.Equal(expected.Order(), actual.Order());

        var policyProvider = new DefaultAuthorizationPolicyProvider(Options.Create(new AuthorizationOptions()));
        foreach (var action in actions)
        {
            var attributes = action.ControllerTypeInfo.GetCustomAttributes(true)
                .Concat(action.MethodInfo.GetCustomAttributes(true)).ToArray();
            var route = action.AttributeRouteInfo!.Template;
            var anonymous = attributes.OfType<IAllowAnonymous>().ToArray();
            if (route == "api/account/login")
            {
                Assert.Single(anonymous);
                continue;
            }
            Assert.Empty(anonymous);
            var authorization = attributes.OfType<IAuthorizeData>().ToArray();
            Assert.NotEmpty(authorization);
            var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorization);
            Assert.NotNull(policy);
            var roleRequirements = policy.Requirements.OfType<RolesAuthorizationRequirement>().ToArray();
            Assert.NotEmpty(roleRequirements);
            Assert.All(roleRequirements, requirement => Assert.Contains(Roles.Admin, requirement.AllowedRoles));
            Assert.All(new[] { Roles.Client, Roles.Agent }, role =>
                Assert.Contains(roleRequirements, requirement => !requirement.AllowedRoles.Contains(role)));
            var method = Assert.Single(action.ActionConstraints!.OfType<HttpMethodActionConstraint>()).HttpMethods.Single();
            var developerAllowed = roleRequirements.All(requirement => requirement.AllowedRoles.Contains(Roles.Developer));
            Assert.Equal(method == "GET", developerAllowed);
        }
    }

    [Fact]
    public void FullApplicationAndWebMappingRegistrationIsValid()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddAutoMapper(configuration => configuration.AddProfile<WebMappingProfile>());
        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        mapper.ConfigurationProvider.AssertConfigurationIsValid();
        Assert.Equal("House", mapper.Map<CatalogViewModel>(mapper.Map<CatalogDto>(new CatalogItem { Name = "House" })).Name);
        var input = mapper.Map<PropertyInput>(ValidPropertyInput());
        Assert.Equal(1, input.PropertyTypeId);
        Assert.Equal([3], input.ImprovementIds);
    }

    [Theory]
    [InlineData(nameof(PropertyInputViewModel.PropertyTypeId), 0)]
    [InlineData(nameof(PropertyInputViewModel.SaleTypeId), -1)]
    [InlineData(nameof(PropertyInputViewModel.Price), 0)]
    [InlineData(nameof(PropertyInputViewModel.Size), 0)]
    [InlineData(nameof(PropertyInputViewModel.Bedrooms), -1)]
    [InlineData(nameof(PropertyInputViewModel.Bathrooms), -1)]
    public void PropertyInputRejectsOutOfRangeValues(string member, int value)
    {
        var model = ValidPropertyInput();
        var property = typeof(PropertyInputViewModel).GetProperty(member)!;
        property.SetValue(model, property.PropertyType == typeof(decimal?) ? (object)(decimal)value : value);
        Assert.Contains(Validate(model), result => result.MemberNames.Contains(member));
    }

    [Fact]
    public void PropertyInputRequiresFieldsAndAtLeastOneValidImprovement()
    {
        var empty = Validate(new PropertyInputViewModel());
        foreach (var member in new[] { "PropertyTypeId", "SaleTypeId", "Price", "Size", "Bedrooms", "Bathrooms", "Description", "ImprovementIds" })
            Assert.Contains(empty, result => result.MemberNames.Contains(member));
        var model = ValidPropertyInput();
        model.ImprovementIds = [0];
        Assert.Contains(Validate(model), result => result.MemberNames.Contains(nameof(model.ImprovementIds)));
        model.ImprovementIds = [3];
        model.Description = new string('x', 4001);
        Assert.Contains(Validate(model), result => result.MemberNames.Contains(nameof(model.Description)));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, true)]
    [InlineData(0, 4, true)]
    [InlineData(1, 3, true)]
    [InlineData(2, 3, false)]
    [InlineData(4, 0, true)]
    [InlineData(5, 0, false)]
    public void PropertyPhotoCountCombinesDistinctRetainedImagesAndNewUploads(int retained, int uploaded, bool valid)
    {
        var model = ValidPropertyInput();
        model.RetainedImageIds = Enumerable.Range(1, retained).ToList();
        using var content = new MemoryStream([1]);
        IFormFile Photo() => new FormFile(content, 0, 1, "photo", "photo.png")
            { Headers = new HeaderDictionary(), ContentType = "image/png" };
        if (uploaded > 0) model.File1 = Photo();
        if (uploaded > 1) model.File2 = Photo();
        if (uploaded > 2) model.File3 = Photo();
        if (uploaded > 3) model.File4 = Photo();
        Assert.Equal(valid, Validate(model).Count == 0);
        model.RetainedImageIds.AddRange(model.RetainedImageIds.ToArray());
        Assert.Equal(valid, Validate(model).Count == 0);
    }

    [Theory]
    [InlineData("photo.png", "image/png", 1, true)]
    [InlineData("photo.JPG", "image/jpeg", 5242880, true)]
    [InlineData("photo.png", "image/png", 0, false)]
    [InlineData("photo.png", "image/png", 5242881, false)]
    [InlineData("photo.exe", "image/png", 1, false)]
    [InlineData("photo.png", "application/octet-stream", 1, false)]
    public void PhotoValidationChecksSizeExtensionAndContentType(string name, string type, int size, bool valid)
    {
        using var content = new MemoryStream();
        var photo = new FormFile(content, 0, size, "photo", name) { Headers = new HeaderDictionary(), ContentType = type };
        Assert.Equal(valid, new ImageUploadAttribute().IsValid(photo));
    }

    [Fact]
    public void RegistrationRequiresPhotoButProfilePhotoIsOptional()
    {
        var registration = new RegisterInputViewModel
        {
            FirstName = "Test", LastName = "User", Phone = "8095550100", UserName = "test-user",
            Email = "user@test.invalid", Password = "Aa1!" + Guid.NewGuid().ToString("N"), Role = Roles.Client
        };
        registration.ConfirmPassword = registration.Password;
        Assert.Contains(Validate(registration), result => result.MemberNames.Contains(nameof(registration.Photo)));
        Assert.Empty(Validate(new ProfileInputViewModel { FirstName = "Test", LastName = "User", Phone = "8095550100" }));
    }

    [Fact]
    public void FiltersAndOffersRejectInvalidRangesAndAcceptBoundaryValues()
    {
        Assert.Empty(Validate(new PropertyFilterViewModel { MinPrice = 0, MaxPrice = 0, Bedrooms = 0, Bathrooms = 0, Code = "123456" }));
        foreach (var filter in new[]
        {
            new PropertyFilterViewModel { MinPrice = -1 }, new PropertyFilterViewModel { MaxPrice = -1 },
            new PropertyFilterViewModel { MinPrice = 2, MaxPrice = 1 }, new PropertyFilterViewModel { Bedrooms = -1 },
            new PropertyFilterViewModel { Bathrooms = -1 }, new PropertyFilterViewModel { PropertyTypeId = 0 },
            new PropertyFilterViewModel { Code = "12345" }, new PropertyFilterViewModel { Code = "12a456" }
        }) Assert.NotEmpty(Validate(filter));
        Assert.NotEmpty(Validate(new OfferInputViewModel()));
        Assert.NotEmpty(Validate(new OfferInputViewModel { Amount = 0 }));
        Assert.NotEmpty(Validate(new OfferInputViewModel { Amount = -1 }));
        Assert.Empty(Validate(new OfferInputViewModel { Amount = 0.01m }));
    }

    private static void AssertPresentationDependency(Type type, string location)
    {
        var name = type.FullName ?? type.Name;
        Assert.False(name.StartsWith("RealEstateApp.Infrastructure", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || type == typeof(IUnitOfWork)
            || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRepository<>), $"{location}: {name}");
        foreach (var argument in type.GenericTypeArguments) AssertPresentationDependency(argument, location);
        if (type.HasElementType) AssertPresentationDependency(type.GetElementType()!, location);
    }

    private static PropertyInputViewModel ValidPropertyInput() => new()
    {
        PropertyTypeId = 1, SaleTypeId = 2, Price = 0.01m, Size = 0.01m, Bedrooms = 0, Bathrooms = 0,
        Description = "Test property", ImprovementIds = [3], RetainedImageIds = [1]
    };

    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}
