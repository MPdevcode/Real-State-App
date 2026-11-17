using AutoMapper;
using RealEstateApp.Domain;

namespace RealEstateApp.Application;

public sealed class ConversationService(IUnitOfWork work, ICurrentUser user, IAccountService accounts, IMapper mapper)
    : IConversationService
{
    public async Task<List<ConversationDto>> ListAsync(int propertyId, CancellationToken ct = default)
    {
        string? clientId = null;
        if (user.IsInRole(Roles.Client)) clientId = ServiceRules.RequireRole(user, Roles.Client);
        else
        {
            var agentId = ServiceRules.RequireRole(user, Roles.Agent);
            (await ServiceRules.PropertyAsync(work, propertyId, ct)).EnsureOwner(agentId);
        }
        var conversations = await work.Repository<Conversation>().ListAsync(conversation =>
            conversation.PropertyId == propertyId && (clientId == null ? conversation.AgentId == user.Id : conversation.ClientId == clientId), ct);
        var result = new List<ConversationDto>();
        foreach (var conversation in conversations.OrderBy(item => item.Id))
        {
            var client = await accounts.GetAsync(conversation.ClientId, ct);
            result.Add(new ConversationDto(conversation.Id, conversation.PropertyId, conversation.ClientId, client.FullName,
                conversation.AgentId, conversation.Messages.OrderBy(message => message.CreatedAt).ThenBy(message => message.Id)
                    .Select(mapper.Map<MessageDto>).ToArray()));
        }
        return result;
    }

    public async Task SendAsync(int propertyId, string? clientId, string body, CancellationToken ct = default)
    {
        var isClient = user.IsInRole(Roles.Client);
        var senderId = ServiceRules.RequireRole(user, isClient ? Roles.Client : Roles.Agent);
        if (string.IsNullOrWhiteSpace(body) || body.Length > 4000)
            throw new BusinessException("El mensaje es obligatorio y no puede exceder 4000 caracteres.");
        if (isClient)
        {
            if (clientId is not null && clientId != senderId) throw new ForbiddenException("No puede enviar mensajes como otro cliente.");
            clientId = senderId;
        }
        else if (string.IsNullOrWhiteSpace(clientId)) throw new BusinessException("Seleccione la conversación del cliente.");
        var participantId = clientId!;
        await work.ExecuteAsync(async () =>
        {
            await work.LockPropertyAsync(propertyId, ct);
            var property = await ServiceRules.PropertyAsync(work, propertyId, ct);
            if (isClient) await ServiceRules.RequirePublicAsync(property, accounts, ct, "Esta propiedad ya no se encuentra disponible para recibir mensajes.");
            else
            {
                property.EnsureOwner(senderId);
                property.EnsureAvailable();
            }
            var repository = work.Repository<Conversation>();
            var conversation = (await repository.ListAsync(item => item.PropertyId == propertyId &&
                item.ClientId == participantId, ct)).SingleOrDefault();
            if (conversation is null)
            {
                if (!isClient) throw new ForbiddenException("Solo el cliente puede iniciar una conversación.");
                conversation = new Conversation { PropertyId = propertyId, ClientId = senderId, AgentId = property.AgentId };
                repository.Add(conversation);
            }
            if (conversation.ClientId != participantId || conversation.AgentId != property.AgentId ||
                (isClient ? conversation.ClientId != senderId : conversation.AgentId != senderId))
                throw new ForbiddenException("No participa en esta conversación.");
            conversation.Messages.Add(new Message { ConversationId = conversation.Id, SenderId = senderId, Body = body.Trim() });
        }, ct);
    }
}
