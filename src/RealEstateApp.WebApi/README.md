# RealEstate API

.NET 10 controller host with 25 endpoints and typed, directly injected CQRS handlers. Application CQRS contracts live in `RealEstateApp.Application.Cqrs`; no mediator or HTTP dependency is used there.

## Configuration

- Set `Jwt__Key` through environment variables or a secret provider. It must contain at least 32 characters and 32 UTF-8 bytes. There is deliberately no checked-in signing key or development fallback.
- Configure `Jwt:Issuer`, `Jwt:Audience`, and `Jwt:LifetimeMinutes` consistently with the shared token service.
- The local default connection is `ConnectionStrings:DefaultConnection`, Windows-integrated LocalDB, database `RealEstateApp`. Override it for other environments.
- Initialization is opt-in through `Database:Initialize=true`. Startup then calls `app.Services.InitializeInfrastructureAsync(app.Configuration)`.
- `Storage:RootPath` is intentionally empty. Infrastructure owns the shared-root fallback. For deployment, configure the same absolute storage root in both hosts; do not configure different content-root-relative directories.
- The development launch profile listens on `http://localhost:5140`, with Swagger at `/swagger`. Swagger middleware is enabled only in Development.
- Terminate HTTPS at the deployment boundary or configure HTTPS hosting. This API never redirects authentication failures to cookie login pages. Override `AllowedHosts` for deployment.

## Endpoints

| Method | Route | Access |
| --- | --- | --- |
| POST | `/api/account/login` | Anonymous |
| POST | `/api/account/developers` | Administrador |
| POST | `/api/account/administrators` | Administrador |
| GET | `/api/properties` | Administrador, Desarrollador |
| GET | `/api/properties/{id}` | Administrador, Desarrollador |
| GET | `/api/properties/by-code/{code}` | Administrador, Desarrollador |
| GET | `/api/agents` | Administrador, Desarrollador |
| GET | `/api/agents/{id}` | Administrador, Desarrollador |
| GET | `/api/agents/{id}/properties` | Administrador, Desarrollador |
| PATCH | `/api/agents/{id}/status` | Administrador |
| GET | `/api/property-types`, `/api/sale-types`, `/api/improvements` | Administrador, Desarrollador |
| GET | Each catalog route + `/{id}` | Administrador, Desarrollador |
| POST | Each catalog route | Administrador |
| PUT | Each catalog route + `/{id}` | Administrador |
| DELETE | Each catalog route + `/{id}` | Administrador |

Log in with `{"login":"admin-user","password":"your-password"}`. The response contains `token`, `user`, `roles`, and `expiration`. Pass `Authorization: Bearer <token>` on protected requests. Cookie authentication is not accepted by this host.

Account creation accepts `firstName`, `lastName`, `userName`, `email`, `password`, `confirmPassword`, `nationalId` (5-30 characters, allowing formatted IDs), and optional `phone`. The role is fixed by the endpoint and cannot be selected in the request. The 201 response excludes passwords, national IDs, and photos, and does not invent a Location for an account detail endpoint that does not exist.

Catalog writes accept `{"name":"Apartamento","description":"Unidad residencial"}`. POST returns 201 with an actual GET Location; PUT returns 200 with the updated item; DELETE returns 204.

Agent status accepts `{"isActive":false}` or `{"isActive":true}`. An absent or null `isActive` produces 400. Agent identifiers must use the hyphenated GUID format. Valid identifiers belonging to missing users or non-agents produce 404, including on agent-property and status routes.

Property queries accept `code`, `propertyTypeId`, `minPrice`, `maxPrice`, `bedrooms`, and `bathrooms`. API list/detail queries use the shared API scope; agent-property queries also specify `agentId`. Property status is the string `Disponible` or `Vendida`, never a numeric enum. Property responses exclude agent email, phone, photo, and storage keys. Agent responses exclude national IDs, photos, usernames, and passwords.

Every list returns 204 without a body when empty. Missing details return 404. Numeric routes have no `:int` constraint so malformed, nonpositive, and overflowing IDs are model-validation 400 responses rather than routing 404s. Codes must contain exactly six ASCII digits. Expected errors, 401/403 authentication failures, unhandled exceptions, and otherwise empty HTTP errors use JSON Problem Details. Unexpected exception messages and stack traces are not returned.

## Integration Contract

The parent composition must provide `AddApplication()` in `RealEstateApp.Application`, `AddInfrastructure(IConfiguration)` and `InitializeInfrastructureAsync(IServiceProvider, IConfiguration)` in `RealEstateApp.Infrastructure`. The host calls `AddCqrs()` itself, registering all 14 typed handler interfaces as scoped services.

Shared dependencies are `IAccountService`, `ITokenService`, `IPropertyService`, and `ICatalogService`. JWT validation also resolves `RealEstateApp.Infrastructure.Security.UserAccessValidator`, whose `ValidateAsync(ClaimsPrincipal)` returns `Task<bool>`; validation checks the current account and access version on every authenticated request.

Package dependencies are already declared in the host project: `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.0 and `Swashbuckle.AspNetCore` 10.0.1. Swagger uses the v10 / Microsoft.OpenApi v2 namespace and security-reference API, not `Microsoft.OpenApi.Models`. Concurrency exception mapping uses the EF Core dependency supplied transitively by Infrastructure.

The namespaced public partial entry point is `RealEstateApp.WebApi.Program`, suitable for `WebApplicationFactory<RealEstateApp.WebApi.Program>` integration tests. Full build and integration verification belong to the parent once the shared framework and service implementations are ready.
