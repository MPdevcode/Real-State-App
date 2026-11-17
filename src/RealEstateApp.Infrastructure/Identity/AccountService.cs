using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using RealEstateApp.Infrastructure.Persistence;

namespace RealEstateApp.Infrastructure.Identity;

public sealed class AccountService(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    RealEstateDbContext db,
    IUnitOfWork unitOfWork,
    ICurrentUser current,
    IFileStorage files,
    IEmailSender email,
    IConfiguration configuration,
    IServiceScopeFactory scopes,
    ILogger<AccountService> logger) : IAccountService
{
    public async Task<LoginResult> LoginAsync(string login, string password, bool api, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password))
            throw new AuthenticationException("Debe ingresar su correo o nombre de usuario y contraseña.");
        var user = await users.FindByNameAsync(login.Trim()) ?? await users.FindByEmailAsync(login.Trim());
        if (user is null || !(await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)).Succeeded)
            throw new AuthenticationException("Los datos de acceso son inválidos.");
        if (!user.IsActive)
            throw new AuthenticationException(api
                ? "El usuario se encuentra inactivo y no puede autenticarse."
                : "El usuario se encuentra inactivo y no puede iniciar sesión.");

        var roles = await users.GetRolesAsync(user);
        string? role;
        if (api)
            role = roles.Contains(Roles.Admin) ? Roles.Admin : roles.Contains(Roles.Developer) ? Roles.Developer : null;
        else
        {
            if (roles.Contains(Roles.Developer)) throw new ForbiddenException("El usuario desarrollador no tiene permisos para acceder a la aplicación web. Solo puede utilizar la API.");
            role = new[] { Roles.Admin, Roles.Agent, Roles.Client }.FirstOrDefault(roles.Contains);
        }
        if (role is null)
            throw new ForbiddenException(api && (roles.Contains(Roles.Client) || roles.Contains(Roles.Agent))
                ? "Los usuarios con rol Cliente o Agente no tienen permisos para acceder a la API."
                : "El usuario no tiene un rol válido asignado. Póngase en contacto con un administrador.");
        return new LoginResult(user.Id, user.UserName!, role);
    }

    public async Task<string> RegisterAsync(RegisterInput input, FileUpload? photo = null,
        string? activationBaseUrl = null, CancellationToken ct = default)
    {
        if (!Roles.All.Contains(input.Role)) throw new BusinessException("Rol inválido.");
        var privileged = input.Role == Roles.Admin || input.Role == Roles.Developer;
        if (privileged) await RequireActorAsync(Roles.Admin, ct);
        ValidateNames(input.FirstName, input.LastName);
        ValidateAccount(input.UserName, input.Email);
        if (string.IsNullOrEmpty(input.Password) || input.Password != input.ConfirmPassword)
            throw new BusinessException("La contraseña y la confirmación de contraseña no coinciden.");
        if (!privileged && (photo is null || string.IsNullOrWhiteSpace(input.Phone)))
            throw new BusinessException("El cliente o agente debe proporcionar teléfono y foto.");
        var nationalId = NormalizeNationalId(input.NationalId);
        if (privileged && nationalId is null) throw new BusinessException("La cédula es requerida.");
        string? activationUrl = null;
        if (input.Role == Roles.Client)
        {
            activationUrl = activationBaseUrl ?? configuration["Email:ActivationBaseUrl"];
            if (!Uri.TryCreate(activationUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException("Configure Email:ActivationBaseUrl as a trusted absolute confirmation URL.");
        }

        string? savedPhoto = null;
        var user = new ApplicationUser
        {
            FirstName = input.FirstName.Trim(), LastName = input.LastName.Trim(),
            UserName = input.UserName.Trim(), Email = input.Email.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim(),
            NationalId = nationalId is null ? null : input.NationalId!.Trim(), NormalizedNationalId = nationalId,
            IsActive = privileged, EmailConfirmed = privileged
        };
        try
        {
            if (photo is not null) user.Photo = savedPhoto = await files.SaveImageAsync(photo, ct);
            await ExecuteAsync(async () =>
            {
                if (privileged)
                {
                    await LockAdminsAsync(ct);
                    await RequireActorAsync(Roles.Admin, ct);
                }
                await EnsureUniqueAsync(user, ct);
                EnsureSuccess(await users.CreateAsync(user, input.Password));
                EnsureSuccess(await users.AddToRoleAsync(user, input.Role));
                if (input.Role == Roles.Client)
                {
                    var token = await users.GenerateEmailConfirmationTokenAsync(user);
                    var link = QueryHelpers.AddQueryString(activationUrl!, new Dictionary<string, string?>
                    {
                        ["userId"] = user.Id, ["token"] = token
                    });
                    // IEmailSender queues delivery in this SQL transaction; SMTP runs after commit.
                    await email.SendAsync(user.Email!, "Activación de cuenta en RealEstateApp",
                        $"<p>Hola {WebUtility.HtmlEncode(user.FirstName)},</p>" +
                        "<p>Su cuenta ha sido registrada correctamente en RealEstateApp.</p>" +
                        "<p>Para activar su usuario y poder iniciar sesión, utilice el siguiente enlace de activación:</p>" +
                        $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Activar mi cuenta</a></p>" +
                        "<p>Si usted no realizó este registro, puede ignorar este mensaje.</p>", ct);
                }
                await unitOfWork.SaveChangesAsync(ct);
            }, ct);
        }
        catch
        {
            if (savedPhoto is not null) await CompensatePhotoAsync(savedPhoto);
            throw;
        }

        return user.Id;
    }

    public Task ConfirmEmailAsync(string userId, string token, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        var user = await FindUserAsync(userId, ct);
        if (!await users.IsInRoleAsync(user, Roles.Client) || user.EmailConfirmed)
            throw new BusinessException("El enlace no es válido o la cuenta ya fue confirmada.");
        if (string.IsNullOrWhiteSpace(token)) throw new BusinessException("El token de confirmación es requerido.");
        EnsureSuccess(await users.ConfirmEmailAsync(user, token));
        user.IsActive = true;
        await InvalidateAsync(user);
    }, ct);

    public async Task<List<UserDto>> ListAsync(string role, bool onlyActive = false, CancellationToken ct = default)
    {
        if (!Roles.All.Contains(role)) throw new BusinessException("Rol inválido.");
        ct.ThrowIfCancellationRequested();
        var accounts = await users.GetUsersInRoleAsync(role);
        var counts = await db.Properties.AsNoTracking().GroupBy(p => p.AgentId)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return accounts.Where(u => !onlyActive || u.IsActive).OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => ToDto(u, role, counts.GetValueOrDefault(u.Id))).ToList();
    }

    public async Task<UserDto> GetAsync(string id, CancellationToken ct = default)
    {
        var user = await FindUserAsync(id, ct);
        var roles = await users.GetRolesAsync(user);
        var count = await db.Properties.CountAsync(p => p.AgentId == id, ct);
        return ToDto(user, roles.FirstOrDefault() ?? "", count);
    }

    public Task UpdateAsync(string id, UpdateUserInput input, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        await LockAdminsAsync(ct);
        await RequireActorAsync(Roles.Admin, ct);
        if (id == current.Id) throw new ForbiddenException("No puede editar su propio usuario desde este mantenimiento.");
        var user = await FindUserAsync(id, ct);
        if (!await users.IsInRoleAsync(user, Roles.Admin) && !await users.IsInRoleAsync(user, Roles.Developer))
            throw new ForbiddenException("Solo se pueden editar administradores y desarrolladores.");
        ValidateNames(input.FirstName, input.LastName);
        ValidateAccount(input.UserName, input.Email);
        var nationalId = NormalizeNationalId(input.NationalId)
            ?? throw new BusinessException("La cédula es requerida.");
        var changePassword = !string.IsNullOrEmpty(input.Password) || !string.IsNullOrEmpty(input.ConfirmPassword);
        if (changePassword && (string.IsNullOrEmpty(input.Password) || input.Password != input.ConfirmPassword))
            throw new BusinessException("La contraseña y la confirmación de contraseña no coinciden.");

        user.FirstName = input.FirstName.Trim();
        user.LastName = input.LastName.Trim();
        user.UserName = input.UserName.Trim();
        user.Email = input.Email.Trim();
        user.NationalId = input.NationalId.Trim();
        user.NormalizedNationalId = nationalId;
        await EnsureUniqueAsync(user, ct);
        if (changePassword)
        {
            var resetToken = await users.GeneratePasswordResetTokenAsync(user);
            EnsureSuccess(await users.ResetPasswordAsync(user, resetToken, input.Password!));
        }
        await InvalidateAsync(user);
    }, ct);

    public Task ChangeStatusAsync(string id, bool active, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        // Serialize the entire administrator set before reading actor, target, or active count.
        await LockAdminsAsync(ct);
        await RequireActorAsync(Roles.Admin, ct);
        var user = await FindUserAsync(id, ct);
        var roles = await users.GetRolesAsync(user);
        if (!roles.Any(r => r == Roles.Agent || r == Roles.Admin || r == Roles.Developer) || roles.Contains(Roles.Client))
            throw new ForbiddenException("No se puede cambiar el estado de este usuario.");
        if (!active && roles.Contains(Roles.Admin))
        {
            if (id == current.Id) throw new ForbiddenException("No puede inactivar a su propio usuario.");
            if (user.IsActive)
            {
                var normalizedRole = users.NormalizeName(Roles.Admin);
                var activeAdmins = await (from account in db.Users.AsNoTracking()
                    join membership in db.UserRoles on account.Id equals membership.UserId
                    join role in db.Roles on membership.RoleId equals role.Id
                    where account.IsActive && role.NormalizedName == normalizedRole
                    select account.Id).CountAsync(ct);
                if (activeAdmins <= 1) throw new BusinessException("Debe existir al menos un administrador activo en el sistema.");
            }
        }
        if (user.IsActive == active) return;
        user.IsActive = active;
        await InvalidateAsync(user);
    }, ct);

    public Task DeleteAgentAsync(string id, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        await LockAdminsAsync(ct);
        await RequireActorAsync(Roles.Admin, ct);
        var user = await FindUserAsync(id, ct);
        var roles = await users.GetRolesAsync(user);
        if (roles.Count != 1 || !roles.Contains(Roles.Agent)) throw new ForbiddenException("Solo se pueden eliminar agentes.");
        var propertyIds = await db.Properties.Where(p => p.AgentId == id).Select(p => p.Id).ToListAsync(ct);
        await unitOfWork.DeletePropertiesAsync(propertyIds, ct);
        if (!string.IsNullOrWhiteSpace(user.Photo)) QueueDeletion(user.Photo);
        EnsureSuccess(await users.DeleteAsync(user));
        await unitOfWork.SaveChangesAsync(ct);
    }, ct);

    public async Task UpdateProfileAsync(ProfileInput input, FileUpload? photo, CancellationToken ct = default)
    {
        await RequireActorAsync(Roles.Agent, ct);
        ValidateNames(input.FirstName, input.LastName);
        if (string.IsNullOrWhiteSpace(input.Phone)) throw new BusinessException("El teléfono es requerido.");
        string? savedPhoto = null;
        try
        {
            if (photo is not null) savedPhoto = await files.SaveImageAsync(photo, ct);
            await ExecuteAsync(async () =>
            {
                var user = await RequireActorAsync(Roles.Agent, ct);
                user.FirstName = input.FirstName.Trim();
                user.LastName = input.LastName.Trim();
                user.PhoneNumber = input.Phone.Trim();
                if (savedPhoto is not null)
                {
                    if (!string.IsNullOrWhiteSpace(user.Photo) && user.Photo != savedPhoto) QueueDeletion(user.Photo);
                    user.Photo = savedPhoto;
                }
                EnsureSuccess(await users.UpdateAsync(user));
                await unitOfWork.SaveChangesAsync(ct);
            }, ct);
        }
        catch
        {
            if (savedPhoto is not null) await CompensatePhotoAsync(savedPhoto);
            throw;
        }
    }

    private async Task ExecuteAsync(Func<Task> action, CancellationToken ct)
    {
        await unitOfWork.ExecuteAsync(async () =>
        {
            if (db.Database.IsSqlServer() && db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("Identity requires IUnitOfWork.ExecuteAsync to use a serializable transaction on the shared RealEstateDbContext.");
            await action();
        }, ct);
    }

    private Task LockAdminsAsync(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource = N'RealEstate:Identity:Admins',
            @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @result < 0 THROW 51000, 'Could not acquire the administrator account lock.', 1;
        """, ct);

    private async Task<ApplicationUser> FindUserAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(id) ?? throw new NotFoundException("Usuario no encontrado.");
        // Cookie validation may already have tracked the user before this transaction started.
        await db.Entry(user).ReloadAsync(ct);
        if (db.Entry(user).State == EntityState.Detached) throw new NotFoundException("Usuario no encontrado.");
        return user;
    }

    private async Task<ApplicationUser> RequireActorAsync(string role, CancellationToken ct)
    {
        current.RequireRole(role);
        var actor = await FindUserAsync(current.Id, ct);
        if (!actor.IsActive || !await users.IsInRoleAsync(actor, role))
            throw new ForbiddenException("Su cuenta no está activa o no tiene permisos.");
        return actor;
    }

    private async Task EnsureUniqueAsync(ApplicationUser user, CancellationToken ct)
    {
        var name = users.NormalizeName(user.UserName!);
        var address = users.NormalizeEmail(user.Email!);
        if (await db.Users.AnyAsync(u => u.Id != user.Id && u.NormalizedUserName == name, ct))
            throw new BusinessException("Ya existe un usuario registrado con este nombre de usuario.");
        if (await db.Users.AnyAsync(u => u.Id != user.Id && u.NormalizedEmail == address, ct))
            throw new BusinessException("Ya existe un usuario registrado con este correo electrónico.");
        if (user.NormalizedNationalId is not null && await db.Users.AnyAsync(
                u => u.Id != user.Id && u.NormalizedNationalId == user.NormalizedNationalId, ct))
            throw new BusinessException("Ya existe un usuario registrado con esta cédula.");
    }

    private static string? NormalizeNationalId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
        if (normalized.Length == 0) throw new BusinessException("La cédula no es válida.");
        return normalized;
    }

    private async Task InvalidateAsync(ApplicationUser user)
    {
        user.AuthVersion = checked(user.AuthVersion + 1);
        EnsureSuccess(await users.UpdateSecurityStampAsync(user));
    }

    private void QueueDeletion(string key) => db.FileDeletionJobs.Add(new()
    {
        FileKey = key, Attempts = 0, CreatedAt = DateTimeOffset.UtcNow
    });

    private async Task CompensatePhotoAsync(string key)
    {
        try { await files.DeleteAsync(key, CancellationToken.None); }
        catch (Exception deleteError)
        {
            try
            {
                // Do not flush tracked entities from a rolled-back account transaction.
                await using var scope = scopes.CreateAsyncScope();
                var cleanupDb = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
                cleanupDb.FileDeletionJobs.Add(new() { FileKey = key, CreatedAt = DateTimeOffset.UtcNow });
                await cleanupDb.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception queueError)
            {
                logger.LogError(queueError, "Could not queue cleanup for uncommitted profile image {FileKey}. Original deletion error: {Error}",
                    key, deleteError.Message);
            }
        }
    }

    private static void ValidateNames(string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            throw new BusinessException("El nombre y apellido son requeridos.");
    }

    private static void ValidateAccount(string userName, string address)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(address)
            || !new EmailAddressAttribute().IsValid(address.Trim()))
            throw new BusinessException("Debe ingresar un correo electrónico válido y un nombre de usuario.");
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded) throw new BusinessException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    private static UserDto ToDto(ApplicationUser u, string role, int count) => new(u.Id, u.FirstName, u.LastName,
        u.UserName ?? "", u.Email ?? "", u.PhoneNumber, u.Photo, u.NationalId, u.IsActive, role, count);
}
