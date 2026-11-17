# Identity Integration

Call `services.AddIdentityInfrastructure(configuration)` from the main composition root after registering the shared, scoped `RealEstateDbContext`, `IUnitOfWork`, `IFileStorage`, and application `IEmailSender`. The extension lives in `RealEstateApp.Infrastructure.Identity`. No email transport or persistence implementation is registered here.

## Persistence

- `RealEstateApp.Infrastructure.Persistence.RealEstateDbContext` must derive from `IdentityDbContext<ApplicationUser>` and expose the domain DbSets and `FileDeletionJobs` described by the parent implementation.
- Configure filtered unique indexes on non-null `ApplicationUser.NormalizedNationalId` and `NormalizedEmail`, in addition to Identity's unique normalized username index. National IDs are normalized to uppercase letters/digits only, ignoring punctuation and whitespace.
- `IUnitOfWork.ExecuteAsync` must start a **serializable SQL Server transaction on the same scoped context** used by Identity stores. The account service and seeder check this explicitly. Identity's internal `SaveChanges` calls remain enlisted in that transaction. Clear rolled-back tracked state before any retry; do not independently replay a delegate after a possibly committed transaction.
- Administrator mutations and seeding additionally take the transaction-owned `RealEstate:Identity:Admins` SQL application lock, with a 10-second timeout. All other writers of administrator membership/status must use this same lock.
- `FileDeletionJobs.Add(new() { FileKey, Attempts, CreatedAt })` must accept the parent's durable job entity (`CreatedAt` is `DateTimeOffset`). Account deletion and photo replacement queue old files inside the SQL transaction. `DeletePropertiesAsync` must delete sold and available properties and queue their images atomically.
- Run the parent's durable deletion worker. Failed new-photo compensation falls back to an independently scoped deletion-job insert; if both storage deletion and SQL insertion fail, the orphan key is logged for operational cleanup.

## Configuration

- `Jwt:Key`: required external secret, at least 32 characters and 32 UTF-8 bytes. There is no fallback key. Use a high-entropy generated secret.
- `Jwt:Issuer` and `Jwt:Audience`: required nonempty values.
- `Jwt:LifetimeMinutes`: defaults to 15; permitted range is 1 through 1440.
- `Email:ActivationBaseUrl`: trusted absolute confirmation **endpoint** URL, such as `https://your-host/Account/ConfirmEmail`. Alternatively, MVC can pass its own configuration-derived URL to `RegisterAsync`. Never build this value from request host headers or accept it from public request input. The service appends URL-encoded `userId` and `token`; the controller must pass the query-bound token unchanged, with no second URL/Base64 decoding.
- Client confirmation emails are sent after commit. A transport error leaves an inactive, unconfirmed account persisted and surfaces the delivery failure; provide an operational recovery/resend path if required. Agent registration sends no email and remains inactive until administrator activation. Privileged registration requires an active administrator and is active/confirmed immediately.

## Authentication Pipelines

- Call `LoginAsync` first, then `ITokenService.CreateAsync` (API) or `IWebSessionService.SignInAsync` (MVC). Do not construct login results from untrusted request data. Password checking uses Identity lockout, never issues a cookie, and happens before active-state/role decisions. API permits administrators/developers only; MVC excludes developers.
- The web session service uses `SignInManager.SignInAsync` and the custom Identity principal factory, not a hand-built principal. Cookies are named `RealEstate.Identity`, HttpOnly, Secure, and SameSite Lax. Serve MVC over HTTPS, including local development. Login and forbidden paths are `/Account/Login` and `/Account/AccessDenied`.
- Cookie validation checks active state, exact current role membership, `auth_version`, and Identity security stamp **on every request**. Status changes and administrative updates increment the version and regenerate the stamp. A stale cookie is rejected and signed out.
- The parent API must register JWT bearer authentication; Infrastructure deliberately does not reference the JWT bearer package. Validate signature with the same UTF-8 HMAC key, require HS256, issuer, audience, expiration, and signed tokens. Prefer `MapInboundClaims = false`, `NameClaimType = "name"`, `RoleClaimType = "role"`, and a small/zero clock skew. Both `sub` and the standard nameidentifier URI claim carry the user ID.
- In API `OnTokenValidated`, resolve `UserAccessValidator` and call `await validator.ValidateAsync(context.Principal!)`; call `context.Fail(...)` when false. This database-backed check is mandatory for immediate token revocation and current roles. Do not catch database failures and treat them as success. An equivalent callback is required for any additional authentication schemes.
- If the parent changes authentication defaults after registering Identity, MVC must still authenticate/challenge the Identity application cookie scheme and API must select bearer explicitly. Never fall back from an invalid bearer token to a cookie on API endpoints.
- `ICurrentUser` trusts the authenticated principal produced by these pipelines. Administrative/profile mutations additionally reload and verify the acting account's active state and role in the database. Authentication exceptions map to HTTP 401, forbidden exceptions to HTTP 403, and not-found exceptions to HTTP 404.
- `ListAsync` and `GetAsync` intentionally have no internal role gate so application services can resolve dashboard counts and agent names. Controllers must guard administrative DTOs and project a public-safe subset for public endpoints. DTOs contain email, phone, and national ID but never password hashes, tokens, stamps, or auth versions.

## Seeding

Run `await IdentitySeeder.SeedAsync(app.Services, configuration, cancellationToken)` after schema creation/migration. The four domain roles are always ensured. Users are created only when `Seed:Enabled` is true.

For each of `Admin`, `Developer`, `Agent`, and `Client`, configure `Seed:<Kind>Password` in external configuration. An optional externally supplied development-only `Seed:Password` can be shared. There are no hardcoded passwords. Missing passwords fail with the specific configuration key and roll back the seed transaction.

Optional `Seed:<Kind>Email`, `UserName`, `FirstName`, `LastName`, `Phone`, and `Photo` keys customize seed identities. Emails default to `admin@realestate.local`, `developer@realestate.local`, `agent@realestate.local`, and `client@realestate.local`; usernames default to their email. Seed admin/developer national IDs are `SEED-ADMIN` and `SEED-DEVELOPER`. New seed accounts are active and confirmed. Existing accounts are never reactivated, renamed, assigned a new password, or silently promoted; conflicting identities cause an actionable failure. Photo keys may refer to seeded assets.
