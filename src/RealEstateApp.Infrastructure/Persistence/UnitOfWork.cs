using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RealEstateApp.Application;
using RealEstateApp.Domain;

namespace RealEstateApp.Infrastructure.Persistence;

public sealed class UnitOfWork(RealEstateDbContext db) : IUnitOfWork
{
    private readonly Dictionary<Type, object> repositories = [];
    public IRepository<T> Repository<T>() where T : Entity
    {
        if (!repositories.TryGetValue(typeof(T), out var value))
            repositories[typeof(T)] = value = new Repository<T>(db);
        return (IRepository<T>)value;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new BusinessException("El registro cambio. Actualice la página e intente nuevamente."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new BusinessException("Ya existe un registro con estos datos. Actualice la página e intente nuevamente."); }
    }

    public async Task ExecuteAsync(Func<Task> action, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await action();
            return;
        }
        // Never replay this delegate automatically: it may include file writes and Identity saves.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await action();
            await SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            db.ChangeTracker.Clear();
            throw new BusinessException("Otra operación modificó los mismos datos. Intente nuevamente.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task LockPropertyAsync(int propertyId, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Property locks require a transaction.");
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @id int; SELECT @id = [Id] FROM [Properties] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {propertyId};", ct);
        // A GET or offer-key lookup earlier in this request may have populated the identity map.
        foreach (var entry in db.ChangeTracker.Entries().Where(e =>
                     e.Entity is Property p && p.Id == propertyId || e.Entity is Offer o && o.PropertyId == propertyId).ToArray())
            await entry.ReloadAsync(ct);
    }

    public async Task DeletePropertiesAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return;
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Deletion requires a transaction.");
        foreach (var id in ids.Order()) await LockPropertyAsync(id, ct);
        var properties = await db.Properties.Where(p => ids.Contains(p.Id)).Include(p => p.Images)
            .Include(p => p.Improvements).AsSplitQuery().ToListAsync(ct);
        db.Properties.RemoveRange(properties);
        // Flush dependents before deleting a parent catalog/Identity user; all remain in the same transaction.
        await SaveChangesAsync(ct);
    }
}
