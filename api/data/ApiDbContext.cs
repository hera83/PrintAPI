using api.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Data;

public class ApiDbContext(DbContextOptions<ApiDbContext> options) : DbContext(options)
{
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<HpPrinter> HpPrinters => Set<HpPrinter>();
    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasIndex(k => k.KeyHash).IsUnique();
            entity.Property(k => k.Name).HasMaxLength(200);
            entity.Property(k => k.ContactName).HasMaxLength(200);
            entity.Property(k => k.ContactNote).HasMaxLength(1000);
        });

        modelBuilder.Entity<HpPrinter>(entity =>
        {
            entity.HasIndex(p => p.Uuid).IsUnique();
            entity.HasIndex(p => p.Host);
            entity.Property(p => p.Name).HasMaxLength(200);
            entity.Property(p => p.Note).HasMaxLength(1000);
            entity.Property(p => p.Host).HasMaxLength(255);
            entity.Property(p => p.ResourcePath).HasMaxLength(200);
        });

        modelBuilder.Entity<PrintJob>(entity =>
        {
            entity.HasOne(j => j.Printer).WithMany().HasForeignKey(j => j.PrinterId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(j => j.Status);
            entity.HasIndex(j => j.SubmittedByKeyId);
            entity.HasIndex(j => j.CreatedAt);
            entity.Property(j => j.Pages).HasMaxLength(200);
            // Stored as text, and used as a concurrency token so the worker and the Cancel endpoint can't overwrite each other's transitions.
            entity.Property(j => j.Status).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
        });
    }
}
