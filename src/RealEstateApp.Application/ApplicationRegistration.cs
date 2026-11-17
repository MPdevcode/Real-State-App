using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using RealEstateApp.Domain;

namespace RealEstateApp.Application;

public static class ApplicationRegistration
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAutoMapper(configuration => configuration.AddProfile<ApplicationMappingProfile>());
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IPropertyService, PropertyService>();
        services.AddScoped<IFavoriteService, FavoriteService>();
        services.AddScoped<IOfferService, OfferService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IDashboardService, DashboardService>();
        return services;
    }
}

public sealed class ApplicationMappingProfile : Profile
{
    public ApplicationMappingProfile()
    {
        CreateMap<CatalogItem, CatalogDto>()
            .ForCtorParam(nameof(CatalogDto.PropertyCount), option => option.MapFrom(_ => 0));
        CreateMap<PropertyImage, ImageDto>();
        CreateMap<Message, MessageDto>();
        CreateMap<Offer, OfferDto>()
            .ForCtorParam(nameof(OfferDto.ClientName), option => option.MapFrom(_ => string.Empty));
    }
}
