using RealEstateApp.Application;
using RealEstateApp.Infrastructure.Persistence;

namespace RealEstateApp.Infrastructure.Email;

public sealed class QueuedEmailSender(RealEstateDbContext db) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        db.EmailJobs.Add(new EmailJob { To = to, Subject = subject, Body = body });
        await db.SaveChangesAsync(ct);
    }
}
