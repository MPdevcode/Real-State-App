using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using RealEstateApp.WebApp.Support;
using RealEstateApp.WebApp.ViewModels;

namespace RealEstateApp.WebApp.Controllers;

public sealed class AccountController(IAccountService accounts, IWebSessionService sessions, IMapper mapper, IConfiguration configuration,
    ILogger<AccountController> logger) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Start", "Home");
        return View(new LoginInputViewModel { ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
    }

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Login(LoginInputViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var login = await accounts.LoginAsync(model.Login, model.Password, api: false, ct);
                if (login.Role is not (Roles.Client or Roles.Agent or Roles.Admin))
                    throw new BusinessException("Su rol no tiene permisos para acceder a la WebApp.");
                await sessions.SignInAsync(login);
                if (Url.IsLocalUrl(model.ReturnUrl)) return LocalRedirect(model.ReturnUrl!);
                return RedirectToAction("Index", login.Role switch { Roles.Client => "Client", Roles.Agent => "Agent", _ => "Admin" });
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        model.Password = "";
        ModelState.SetModelValue(nameof(model.Password), null, null);
        return View(model);
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Register() => View(new RegisterInputViewModel());

    [AllowAnonymous, HttpPost, RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Register(RegisterInputViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var publicUrl = configuration["PublicUrl"];
                if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
                    throw new BusinessException("El registro no está disponible: el administrador debe configurar PublicUrl con una dirección HTTPS válida.");
                using var uploads = new UploadBatch(model.Photo is null ? [] : [model.Photo]);
                var confirmationUrl = uri.AbsoluteUri.TrimEnd('/') + "/Account/Confirm";
                await accounts.RegisterAsync(mapper.Map<RegisterInput>(model), uploads.Files.SingleOrDefault(), confirmationUrl, ct);
                TempData["Success"] = model.Role == Roles.Client
                    ? "Su cuenta ha sido creada correctamente. Revise su correo electrónico para activar su usuario."
                    : "Su cuenta de agente ha sido creada correctamente. Un administrador debe activar su usuario antes de que pueda iniciar sesión.";
                return RedirectToAction(nameof(Login));
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Registration failed");
                ModelState.AddModelError("", "No fue posible completar el registro. Intente nuevamente más tarde.");
            }
        }
        model.Password = model.ConfirmPassword = "";
        ModelState.SetModelValue(nameof(model.Password), null, null);
        ModelState.SetModelValue(nameof(model.ConfirmPassword), null, null);
        return View(model);
    }

    [AllowAnonymous, HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Confirm(string? userId, string? token, CancellationToken ct)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        try
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
                throw new BusinessException("El enlace de activación esta incompleto.");
            await accounts.ConfirmEmailAsync(userId, token, ct);
            TempData["Success"] = "Su cuenta fue activada correctamente. Ya puede iniciar sesión.";
        }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Login));
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Logout()
    {
        await sessions.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = 403;
        return View("Error", new ErrorViewModel { StatusCode = 403, Message = "No tiene permisos para acceder a esta sección." });
    }
}
