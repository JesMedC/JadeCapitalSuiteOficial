using JadeCapital.Identity.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>identity.tenants</c> (Wave 6, slice 6c.1).
///
/// <para>
/// Mirrors migration 0024. The 2 CHECK constraints are also enforced in
/// the SQL migration; EF will skip emitting them (no equivalent primitive
/// in the EF model). The 1 UNIQUE index + 1 regular index + 2 CHECK
/// constraints together give us:
/// </para>
/// <list type="bullet">
///   <item><b>UNIQUE(slug)</b> — idempotency dedup at the DB level. The
///   handler maps the unique-violation exception to <c>tenant.slug_taken</c>.</item>
///   <item><b>INDEX(owner_user_id)</b> — accelerates <c>ListByOwnerAsync</c>.</item>
///   <item><b>CHECK(plan BETWEEN 0 AND 2)</b> — defense-in-depth against
///   out-of-range plan values.</item>
///   <item><b>CHECK(status BETWEEN 0 AND 2)</b> — same for status.</item>
/// </list>
/// </summary>
internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants");
        b.HasKey(t => t.Id);

        b.Property(t => t.Id).HasColumnName("id");
        b.Property(t => t.Name).HasColumnName("name").HasMaxLength(Tenant.MaxNameLength).IsRequired();
        b.Property(t => t.Slug).HasColumnName("slug").HasMaxLength(Tenant.MaxSlugLength).IsRequired();
        b.Property(t => t.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        b.Property(t => t.Plan).HasColumnName("plan").HasConversion<byte>().IsRequired();
        b.Property(t => t.Status).HasColumnName("status").HasConversion<byte>().IsRequired();
        b.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        b.Ignore(t => t.DomainEvents);

        // FK to identity.users(id). RESTRICT (mirrored in the SQL migration)
        // — cannot delete an owner user without transferring ownership first.
        b.HasOne<JadeCapital.Identity.Domain.Users.User>()
            .WithMany()
            .HasForeignKey(t => t.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // UNIQUE(slug) — mirrors migration 0024. The handler's slug-taken
        // check is a fast-path; this is the authoritative idempotency
        // guarantee at the DB level.
        b.HasIndex(t => t.Slug)
            .IsUnique()
            .HasDatabaseName("ux_tenants_slug");

        // Regular INDEX(owner_user_id) — accelerates ListByOwnerAsync.
        b.HasIndex(t => t.OwnerUserId)
            .HasDatabaseName("ix_tenants_owner_user_id");
    }
}
