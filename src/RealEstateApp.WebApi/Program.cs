using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using RealEstateApp.Application;
using RealEstateApp.Application.Cqrs;
using RealEstateApp.Infrastructure;
using RealEstateApp.Infrastructure.Security;
using RealEstateApp.WebApi.Errors;
using RealEstateApp.WebApi.Swagger;

namespace RealEstateApp.WebApi;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var jwtKey = builder.Configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32 || Encoding.UTF8.GetByteCount(jwtKey) < 32)
            throw new InvalidOperationException("Configure Jwt:Key with a secret of at least 32 characters (256 bits).");
        var issuer = builder.Configuration["Jwt:Issuer"];
        var audience = builder.Configuration["Jwt:Audience"];
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
            throw new InvalidOperationException("Configure Jwt:Issuer and Jwt:Audience.");

        builder.Services.AddApplication();
        builder.Services.AddCqrs();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddControllers();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();

        // AddIdentity registers cookie defaults. This host must authenticate and challenge with JWT only.
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "name",
                RoleClaimType = "role"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var validator = context.HttpContext.RequestServices.GetRequiredService<UserAccessValidator>();
                    if (context.Principal is null || !await validator.ValidateAsync(context.Principal))
                        context.Fail("La cuenta ya no tiene acceso.");
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await ApiProblem.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                        "No esta autorizado para acceder a este recurso.");
                },
                OnForbidden = context => ApiProblem.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden,
                    "Acceso denegado. No tiene permisos para realizar esta acción.")
            };
        });

        builder.Services.AddAuthorization(options =>
        {
            var authenticated = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser().Build();
            options.DefaultPolicy = authenticated;
            options.FallbackPolicy = authenticated;
        });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "RealEstate API", Version = "v1" });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Ingrese el token devuelto por POST /api/account/login."
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
            options.OperationFilter<AnonymousOperationFilter>();
        });

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseStatusCodePages(context => ApiProblem.WriteAsync(context.HttpContext, context.HttpContext.Response.StatusCode));
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "RealEstate API v1"));
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        if (app.Configuration.GetValue<bool>("Database:Initialize"))
            await app.Services.InitializeInfrastructureAsync(app.Configuration);

        await app.RunAsync();
    }
}
