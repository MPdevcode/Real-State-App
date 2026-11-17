using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using RealEstateApp.Application;
using RealEstateApp.Infrastructure;
using RealEstateApp.Infrastructure.Files;
using RealEstateApp.WebApp.Mapping;
using RealEstateApp.WebApp.Support;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<WebMappingProfile>());
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add<MvcExceptionFilter>();
    options.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(name => $"El campo {name} debe ser numérico.");
    options.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((_, name) => $"Ingrese un valor válido para {name}.");
    options.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(_ => "Debe completar este campo.");
    options.ModelBindingMessageProvider.SetUnknownValueIsInvalidAccessor(_ => "El valor ingresado no es válido.");
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

var app = builder.Build();
app.UseExceptionHandler("/Home/StatusPage");
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(app.Services.GetRequiredService<StoragePaths>().Images),
    RequestPath = "/media"
});
app.UseStatusCodePagesWithReExecute("/Home/StatusPage", "?code={0}");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
await app.Services.InitializeInfrastructureAsync(app.Configuration);
await app.RunAsync();

public partial class Program;
