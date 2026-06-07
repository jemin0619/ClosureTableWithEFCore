using Microsoft.EntityFrameworkCore;
using Project.Infrastructure.Entities;

namespace Project.Infrastructure;

public class SpecDbContext(DbContextOptions<SpecDbContext> options) : DbContext(options)
{
    public DbSet<SpecNodeEntity> SpecNodes => Set<SpecNodeEntity>();
    public DbSet<SpecClosureEntity> SpecClosures => Set<SpecClosureEntity>();
    public DbSet<SpecSerialRootEntity> SpecSerialRoots => Set<SpecSerialRootEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SpecNodeEntity>(builder =>
        {
            builder.ToTable("spec_node");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(100).IsRequired();
            builder.Property(x => x.Value).HasColumnName("value").HasColumnType("text");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            builder.HasIndex(x => x.Key).HasDatabaseName("ix_spec_node_key");
        });

        modelBuilder.Entity<SpecClosureEntity>(builder =>
        {
            builder.ToTable("spec_closure");
            builder.HasKey(x => new { x.AncestorId, x.DescendantId });
            builder.Property(x => x.AncestorId).HasColumnName("ancestor_id");
            builder.Property(x => x.DescendantId).HasColumnName("descendant_id");
            builder.Property(x => x.Depth).HasColumnName("depth");

            builder.HasOne(x => x.Ancestor)
                .WithMany()
                .HasForeignKey(x => x.AncestorId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Descendant)
                .WithMany()
                .HasForeignKey(x => x.DescendantId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => new { x.AncestorId, x.Depth }).HasDatabaseName("ix_spec_closure_ancestor_depth");
            builder.HasIndex(x => new { x.DescendantId, x.Depth }).HasDatabaseName("ix_spec_closure_descendant_depth");
        });

        modelBuilder.Entity<SpecSerialRootEntity>(builder =>
        {
            builder.ToTable("spec_serial_root");
            builder.HasKey(x => x.SerialCode);
            builder.Property(x => x.SerialCode).HasColumnName("serial_code").HasMaxLength(50);
            builder.Property(x => x.RootNodeId).HasColumnName("root_node_id").IsRequired();

            builder.HasOne(x => x.RootNode)
                .WithMany()
                .HasForeignKey(x => x.RootNodeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.RootNodeId).HasDatabaseName("ix_spec_serial_root_root_node_id");
        });
    }
}
