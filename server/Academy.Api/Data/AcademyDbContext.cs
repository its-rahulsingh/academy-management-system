using Academy.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Data;

public sealed class AcademyDbContext(DbContextOptions<AcademyDbContext> options) : DbContext(options)
{
    public DbSet<Batch> Batches => Set<Batch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Batch>(entity =>
        {
            entity.ToTable("batches");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Name)
                .HasColumnName("name")
                .HasMaxLength(120)
                .IsRequired();
            entity.Property(x => x.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .HasDefaultValueSql("now()")
                .IsRequired();
        });
    }
}
