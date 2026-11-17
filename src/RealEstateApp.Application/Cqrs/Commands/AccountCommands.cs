using RealEstateApp.Domain;

namespace RealEstateApp.Application.Cqrs.Commands;

public sealed record LoginCommand(string Login, string Password) : ICommand<TokenResult>;

public sealed class LoginCommandHandler(IAccountService accounts, ITokenService tokens)
    : ICommandHandler<LoginCommand, TokenResult>
{
    public async Task<TokenResult> HandleAsync(LoginCommand command, CancellationToken ct = default)
    {
        var login = await accounts.LoginAsync(command.Login, command.Password, api: true, ct: ct);
        return await tokens.CreateAsync(login, ct);
    }
}

public sealed record CreateAccountCommand(RegisterInput Input) : ICommand<AccountResponse>;

public sealed class CreateAccountCommandHandler(IAccountService accounts)
    : ICommandHandler<CreateAccountCommand, AccountResponse>
{
    public async Task<AccountResponse> HandleAsync(CreateAccountCommand command, CancellationToken ct = default)
    {
        if (command.Input.Role is not (Roles.Admin or Roles.Developer))
            throw new BusinessException("Solo puede crear administradores o desarrolladores por esta vía.");

        var id = await accounts.RegisterAsync(command.Input, ct: ct);
        return AccountResponse.From(await accounts.GetAsync(id, ct));
    }
}

public sealed record ChangeAgentStatusCommand(string Id, bool IsActive) : ICommand<Unit>;

public sealed class ChangeAgentStatusCommandHandler(IAccountService accounts)
    : ICommandHandler<ChangeAgentStatusCommand, Unit>
{
    public async Task<Unit> HandleAsync(ChangeAgentStatusCommand command, CancellationToken ct = default)
    {
        var id = AgentIdentity.Parse(command.Id);
        var user = await accounts.GetAsync(id, ct);
        if (user.Role != Roles.Agent) throw new NotFoundException("El agente solicitado no existe.");

        await accounts.ChangeStatusAsync(id, command.IsActive, ct);
        return default;
    }
}
