using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeKit;
using RealEstateApp.Application;
using RealEstateApp.Infrastructure.Files;
using RealEstateApp.Infrastructure.Persistence;

namespace RealEstateApp.Infrastructure;

public sealed class BackgroundJobs(IServiceScopeFactory scopes, IConfiguration configuration,
    IHostEnvironment environment, ILogger<BackgroundJobs> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAsync(stoppingToken);
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background jobs failed; retrying in 10 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Both hosts may run workers. Only one owns this batch; a crash releases the transaction lock.
        var result = await db.Database.SqlQueryRaw<int>("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=N'RealEstate:Jobs', @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=0;
            SELECT @result AS [Value];
            """).ToListAsync(ct);
        if (result[0] < 0) return;
        var files = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        foreach (var job in await db.FileDeletionJobs.OrderBy(j => j.Attempts).ThenBy(j => j.Id).Take(20).ToListAsync(ct))
        {
            try { await files.DeleteAsync(job.FileKey, ct); db.FileDeletionJobs.Remove(job); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { job.Attempts++; job.LastError = ex.Message[..Math.Min(ex.Message.Length, 1000)]; }
        }
        foreach (var job in await db.EmailJobs.OrderBy(j => j.Attempts).ThenBy(j => j.Id).Take(10).ToListAsync(ct))
        {
            try
            {
                await DeliverAsync(job, scope.ServiceProvider.GetRequiredService<StoragePaths>(), ct);
                db.EmailJobs.Remove(job);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { job.Attempts++; job.LastError = ex.Message[..Math.Min(ex.Message.Length, 1000)]; }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task DeliverAsync(EmailJob job, StoragePaths paths, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(configuration["Email:From"] ?? "noreply@realestate.local"));
        message.To.Add(MailboxAddress.Parse(job.To));
        message.Subject = job.Subject;
        message.Body = new TextPart("html") { Text = job.Body };
        if (configuration["Email:Mode"] == "Pickup")
        {
            if (!environment.IsDevelopment()) throw new InvalidOperationException("Email pickup is development-only; configure SMTP.");
            await message.WriteToAsync(Path.Combine(paths.Mail, $"email-{job.Id}.eml"), ct);
            return;
        }
        var host = configuration["Email:SmtpHost"] ?? throw new InvalidOperationException("Configure Email:SmtpHost.");
        using var smtp = new SmtpClient();
        smtp.Timeout = 15000;
        await smtp.ConnectAsync(host, configuration.GetValue("Email:SmtpPort", 587), SecureSocketOptions.StartTls, ct);
        if (!string.IsNullOrEmpty(configuration["Email:User"]))
            await smtp.AuthenticateAsync(configuration["Email:User"]!, configuration["Email:Password"] ?? "", ct);
        await smtp.SendAsync(message, cancellationToken: ct);
        await smtp.DisconnectAsync(true, ct);
    }
}
