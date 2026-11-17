using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RealEstateApp.Domain;
using RealEstateApp.Infrastructure.Identity;

namespace RealEstateApp.Infrastructure.Persistence;

public sealed class RealEstateDbContext(DbContextOptions<RealEstateDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<PropertyImage> PropertyImages => Set<PropertyImage>();
    public DbSet<PropertyImprovement> PropertyImprovements => Set<PropertyImprovement>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<FileDeletionJob> FileDeletionJobs => Set<FileDeletionJob>();
    public DbSet<EmailJob> EmailJobs => Set<EmailJob>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<ApplicationUser>(b =>
        {
            b.Property(u => u.FirstName).HasMaxLength(100);
            b.Property(u => u.LastName).HasMaxLength(100);
            b.Property(u => u.NationalId).HasMaxLength(32);
            b.Property(u => u.NormalizedNationalId).HasMaxLength(32);
            b.Property(u => u.Photo).HasMaxLength(100);
            b.HasIndex(u => u.NormalizedNationalId).IsUnique().HasFilter("[NormalizedNationalId] IS NOT NULL");
            b.HasIndex(u => u.NormalizedEmail).IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
        });
        model.Entity<CatalogItem>(b =>
        {
            b.Property(c => c.Name).HasMaxLength(100).IsRequired();
            b.Property(c => c.NormalizedName).HasMaxLength(100).IsRequired();
            b.Property(c => c.Description).HasMaxLength(2000).IsRequired();
            b.HasIndex(c => new { c.Kind, c.NormalizedName }).IsUnique();
            b.ToTable(t => t.HasCheckConstraint("CK_Catalog_Kind", "[Kind] IN (1,2,3)"));
        });
        model.Entity<Property>(b =>
        {
            b.Property(p => p.Code).HasColumnType("char(6)").IsRequired();
            b.HasIndex(p => p.Code).IsUnique();
            b.Property(p => p.Description).HasMaxLength(4000).IsRequired();
            b.Property(p => p.Price).HasPrecision(18, 2);
            b.Property(p => p.Size).HasPrecision(18, 2);
            b.Property(p => p.RowVersion).IsRowVersion();
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.AgentId).OnDelete(DeleteBehavior.NoAction);
            b.HasOne(p => p.PropertyType).WithMany().HasForeignKey(p => p.PropertyTypeId).OnDelete(DeleteBehavior.NoAction);
            b.HasOne(p => p.SaleType).WithMany().HasForeignKey(p => p.SaleTypeId).OnDelete(DeleteBehavior.NoAction);
            b.HasMany(p => p.Images).WithOne().HasForeignKey(i => i.PropertyId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(p => p.Improvements).WithOne().HasForeignKey(i => i.PropertyId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(p => new { p.AgentId, p.Status });
            b.HasIndex(p => new { p.Status, p.CreatedAt });
            b.HasIndex(p => new { p.PropertyTypeId, p.Price });
            b.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Property_Code", "[Code] NOT LIKE '%[^0-9]%' AND LEN([Code]) = 6");
                t.HasCheckConstraint("CK_Property_Amounts", "[Price] > 0 AND [Size] > 0 AND [Bedrooms] >= 0 AND [Bathrooms] >= 0");
                t.HasCheckConstraint("CK_Property_Status", "[Status] IN (1,2)");
            });
        });
        model.Entity<PropertyImage>(b =>
        {
            b.Property(i => i.FileKey).HasMaxLength(100).IsRequired();
            b.HasIndex(i => new { i.PropertyId, i.SortOrder });
        });
        model.Entity<PropertyImprovement>(b =>
        {
            b.HasKey(i => new { i.PropertyId, i.ImprovementId });
            b.HasOne(i => i.Improvement).WithMany().HasForeignKey(i => i.ImprovementId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Favorite>(b =>
        {
            b.HasIndex(f => new { f.ClientId, f.PropertyId }).IsUnique();
            b.HasOne<Property>().WithMany().HasForeignKey(f => f.PropertyId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(f => f.ClientId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<Conversation>(b =>
        {
            b.HasIndex(c => new { c.PropertyId, c.ClientId }).IsUnique();
            b.HasOne<Property>().WithMany().HasForeignKey(c => c.PropertyId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.ClientId).OnDelete(DeleteBehavior.NoAction);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.AgentId).OnDelete(DeleteBehavior.NoAction);
            b.HasMany(c => c.Messages).WithOne().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Message>(b =>
        {
            b.Property(m => m.Body).HasMaxLength(4000).IsRequired();
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.NoAction);
            b.HasIndex(m => new { m.ConversationId, m.CreatedAt });
        });
        model.Entity<Offer>(b =>
        {
            b.Property(o => o.Amount).HasPrecision(18, 2);
            b.HasOne<Property>().WithMany().HasForeignKey(o => o.PropertyId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(o => o.ClientId).OnDelete(DeleteBehavior.NoAction);
            b.HasIndex(o => new { o.PropertyId, o.ClientId }).IsUnique().HasFilter("[Status] = 1");
            b.HasIndex(o => o.PropertyId).IsUnique().HasFilter("[Status] = 3");
            b.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Offer_Amount", "[Amount] > 0");
                t.HasCheckConstraint("CK_Offer_Status", "[Status] IN (1,2,3)");
            });
        });
        model.Entity<FileDeletionJob>(b =>
        {
            b.Property(j => j.FileKey).HasMaxLength(100).IsRequired();
            b.Property(j => j.LastError).HasMaxLength(1000);
        });
        model.Entity<EmailJob>(b =>
        {
            b.Property(j => j.To).HasMaxLength(256).IsRequired();
            b.Property(j => j.Subject).HasMaxLength(256).IsRequired();
            b.Property(j => j.Body).IsRequired();
            b.Property(j => j.LastError).HasMaxLength(1000);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        var deletedKeys = ChangeTracker.Entries<PropertyImage>().Where(e => e.State == EntityState.Deleted)
            .Select(e => e.Entity.FileKey).Distinct().ToArray();
        foreach (var key in deletedKeys)
            if (!FileDeletionJobs.Local.Any(j => j.FileKey == key))
                FileDeletionJobs.Add(new FileDeletionJob { FileKey = key });
        return base.SaveChangesAsync(cancellationToken);
    }
}

public sealed class FileDeletionJob
{
    public int Id { get; set; }
    public string FileKey { get; set; } = "";
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LastError { get; set; }
}

public sealed class EmailJob
{
    public int Id { get; set; }
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LastError { get; set; }
}
