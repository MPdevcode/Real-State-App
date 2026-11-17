using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RealEstateApp.Application;
using RealEstateApp.Domain;

namespace RealEstateApp.Infrastructure.Persistence;

public sealed class Repository<T>(RealEstateDbContext db) : IRepository<T> where T : Entity
{
    private IQueryable<T> Query()
    {
        if (typeof(T) == typeof(Property))
            return (IQueryable<T>)db.Properties.Include(p => p.PropertyType).Include(p => p.SaleType)
                .Include(p => p.Images).Include(p => p.Improvements).ThenInclude(i => i.Improvement).AsSplitQuery();
        if (typeof(T) == typeof(Conversation))
            return (IQueryable<T>)db.Conversations.Include(c => c.Messages).AsSplitQuery();
        return db.Set<T>();
    }

    public Task<T?> FindAsync(int id, CancellationToken ct = default) => Query().SingleOrDefaultAsync(e => e.Id == id, ct);
    public Task<List<T>> ListAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        => (filter is null ? Query() : Query().Where(filter)).ToListAsync(ct);
    public void Add(T entity) => db.Set<T>().Add(entity);
    public void Remove(T entity) => db.Set<T>().Remove(entity);
}
