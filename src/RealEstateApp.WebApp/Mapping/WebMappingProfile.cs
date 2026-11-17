using AutoMapper;
using RealEstateApp.Application;
using RealEstateApp.WebApp.ViewModels;

namespace RealEstateApp.WebApp.Mapping;

public sealed class WebMappingProfile : Profile
{
    public WebMappingProfile()
    {
        CreateMap<PropertyDto, PropertyViewModel>();
        CreateMap<ImageDto, ImageViewModel>();
        CreateMap<CatalogDto, CatalogViewModel>();
        CreateMap<UserDto, UserViewModel>();
        CreateMap<OfferDto, OfferViewModel>();
        CreateMap<MessageDto, MessageViewModel>();
        CreateMap<ConversationDto, ConversationViewModel>();
        CreateMap<DashboardDto, DashboardViewModel>();
        CreateMap<PropertyFilterViewModel, PropertyFilter>();
        CreateMap<PropertyInputViewModel, PropertyInput>();
        CreateMap<CatalogInputViewModel, CatalogInput>();
        CreateMap<CatalogDto, CatalogInputViewModel>();
        CreateMap<RegisterInputViewModel, RegisterInput>();
        CreateMap<AdminUserInputViewModel, RegisterInput>()
            .ForCtorParam("Phone", o => o.MapFrom(_ => (string?)null));
        CreateMap<AdminUserCreateInputViewModel, RegisterInput>()
            .IncludeBase<AdminUserInputViewModel, RegisterInput>();
        CreateMap<AdminUserInputViewModel, UpdateUserInput>();
        CreateMap<ProfileInputViewModel, ProfileInput>();
        CreateMap<UserDto, ProfileInputViewModel>(MemberList.None)
            .ForMember(d => d.Photo, o => o.Ignore())
            .ForMember(d => d.CurrentPhoto, o => o.MapFrom(s => s.Photo));
        CreateMap<UserDto, AdminUserInputViewModel>(MemberList.None);
        CreateMap<PropertyDto, PropertyInputViewModel>(MemberList.None)
            .ForMember(d => d.ImprovementIds, o => o.MapFrom(s => s.Improvements.Select(x => x.Id)))
            .ForMember(d => d.RetainedImageIds, o => o.MapFrom(s => s.Images.Select(x => x.Id)))
            .ForMember(d => d.ExistingImages, o => o.MapFrom(s => s.Images))
            .ForMember(d => d.Improvements, o => o.Ignore());
    }
}
