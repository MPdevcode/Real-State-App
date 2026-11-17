using AutoMapper;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using RealEstateApp.WebApp.ViewModels;

namespace RealEstateApp.WebApp.Controllers;

[Authorize(Roles = Roles.Admin), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminUsersController(IAccountService accounts, IMapper mapper) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string role, CancellationToken ct)
    {
        RequireRole(role);
        return View(new UserListingViewModel { Role = role, Users = mapper.Map<List<UserViewModel>>(await accounts.ListAsync(role, ct: ct)) });
    }

    [HttpGet]
    public IActionResult Create(string role)
    {
        RequireRole(role);
        return View("UserForm", new AdminUserCreateInputViewModel { Role = role });
    }

    [HttpPost]
    public async Task<IActionResult> Create(string role, AdminUserCreateInputViewModel model, CancellationToken ct)
    {
        RequireRole(role);
        model.Role = role;
        if (ModelState.IsValid)
        {
            try
            {
                await accounts.RegisterAsync(mapper.Map<RegisterInput>(model), ct: ct);
                TempData["Success"] = $"{Noun(role)} fue creado correctamente.";
                return RedirectToAction(nameof(Index), new { role });
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        ClearPasswords(model);
        return View("UserForm", model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string role, string id, CancellationToken ct)
    {
        var user = await UserAsync(role, id, ct);
        if (role == Roles.Admin && id == User.FindFirstValue(ClaimTypes.NameIdentifier))
            throw new ForbiddenException("No puede editar su propio usuario desde este mantenimiento.");
        return View("UserForm", mapper.Map<AdminUserInputViewModel>(user));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(string role, string id, AdminUserInputViewModel model, CancellationToken ct)
    {
        await UserAsync(role, id, ct);
        model.Role = role;
        model.Id = id;
        if (ModelState.IsValid)
        {
            try
            {
                await accounts.UpdateAsync(id, mapper.Map<UpdateUserInput>(model), ct);
                TempData["Success"] = $"{Noun(role)} fue actualizado correctamente.";
                return RedirectToAction(nameof(Index), new { role });
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        ClearPasswords(model);
        return View("UserForm", model);
    }

    [HttpGet]
    public async Task<IActionResult> Status(string role, string id, CancellationToken ct)
    {
        var user = await UserAsync(role, id, ct);
        return View("Confirm", new ConfirmationViewModel
        {
            Title = user.IsActive ? "Inactivar usuario" : "Activar usuario",
            Message = $"¿Está seguro que desea {(user.IsActive ? "inactivar" : "activar")} este {Singular(role)}?",
            Consequences = "Los usuarios inactivos no pueden autenticarse ni acceder a las funciones protegidas.",
            Controller = "AdminUsers", Id = id, Role = role, Active = !user.IsActive, PostAction = nameof(Status)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Status(string role, string id, StatusInputViewModel input, CancellationToken ct)
    {
        RequireRole(role);
        if (ModelState.IsValid)
        {
            try
            {
                await UserAsync(role, id, ct);
                await accounts.ChangeStatusAsync(id, input.Active!.Value, ct);
                TempData["Success"] = $"{Noun(role)} fue {(input.Active.Value ? "activado" : "inactivado")} correctamente.";
            }
            catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        }
        else TempData["Error"] = "Debe indicar un estado válido.";
        return RedirectToAction(nameof(Index), new { role });
    }

    private static string Singular(string role) => role == Roles.Admin ? "administrador" : "desarrollador";

    private static string Noun(string role) => role == Roles.Admin ? "El administrador" : "El desarrollador";

    private static void RequireRole(string role)
    {
        if (role is not (Roles.Admin or Roles.Developer)) throw new NotFoundException("El mantenimiento solicitado no existe.");
    }

    private async Task<UserDto> UserAsync(string role, string id, CancellationToken ct)
    {
        RequireRole(role);
        var user = await accounts.GetAsync(id, ct);
        if (user.Role != role) throw new NotFoundException("El usuario no pertenece a este mantenimiento.");
        return user;
    }

    private void ClearPasswords(AdminUserInputViewModel model)
    {
        model.Password = model.ConfirmPassword = null;
        ModelState.SetModelValue(nameof(model.Password), null, null);
        ModelState.SetModelValue(nameof(model.ConfirmPassword), null, null);
    }
}
