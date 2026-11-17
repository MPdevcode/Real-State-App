using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApp.Application;
using RealEstateApp.Application.Cqrs;
using RealEstateApp.Application.Cqrs.Commands;
using RealEstateApp.Domain;
using RealEstateApp.WebApi.Contracts;

namespace RealEstateApp.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/account")]
public sealed class AccountController(
    ICommandHandler<LoginCommand, TokenResult> login,
    ICommandHandler<CreateAccountCommand, AccountResponse> create) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<TokenResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TokenResult>> Login([FromBody] LoginRequest request, CancellationToken ct) =>
        Ok(await login.HandleAsync(new LoginCommand(request.Login, request.Password), ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("developers")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AccountResponse>> CreateDeveloper([FromBody] CreateAccountRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created,
            await create.HandleAsync(new CreateAccountCommand(request.ToInput(Roles.Developer)), ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("administrators")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AccountResponse>> CreateAdministrator([FromBody] CreateAccountRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created,
            await create.HandleAsync(new CreateAccountCommand(request.ToInput(Roles.Admin)), ct));
}
