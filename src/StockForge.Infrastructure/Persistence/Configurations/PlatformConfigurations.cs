using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockForge.Domain.Platform;
using StockForge.Infrastructure.Identity;

namespace StockForge.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.Property(a => a.UserEmail).HasMaxLength(256);
        b.Property(a => a.Action).HasMaxLength(80);
        b.Property(a => a.EntityType).HasMaxLength(80);
        b.Property(a => a.EntityId).HasMaxLength(80);
        b.Property(a => a.OldValues).HasColumnType("jsonb");
        b.Property(a => a.NewValues).HasColumnType("jsonb");
        b.PrimitiveCollection(a => a.ChangedProperties).HasColumnType("text[]");
        b.Property(a => a.IpAddress).HasMaxLength(64);
        b.Property(a => a.UserAgent).HasMaxLength(512);
        b.Property(a => a.CorrelationId).HasMaxLength(64);
        b.HasIndex(a => new { a.TenantId, a.Timestamp });
        b.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
        b.HasIndex(a => new { a.TenantId, a.UserId });
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.Property(m => m.Type).HasMaxLength(100);
        b.Property(m => m.Payload).HasColumnType("text");
        b.Property(m => m.LockedBy).HasMaxLength(100);
        b.Property(m => m.LastError).HasMaxLength(4000);
        b.Property(m => m.CorrelationId).HasMaxLength(64);
        b.Property(m => m.DeduplicationKey).HasMaxLength(200);
        b.HasIndex(m => new { m.Status, m.NextAttemptAt });
        b.HasIndex(m => new { m.TenantId, m.CreatedAt });
        b.HasIndex(m => m.DeduplicationKey).IsUnique().HasFilter("deduplication_key IS NOT NULL");
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("idempotency_records");
        b.Property(r => r.Key).HasMaxLength(100);
        b.Property(r => r.Method).HasMaxLength(10);
        b.Property(r => r.Path).HasMaxLength(400);
        b.Property(r => r.RequestHash).HasMaxLength(64);
        b.Property(r => r.ResponseBody).HasColumnType("text");
        b.Property(r => r.ResponseContentType).HasMaxLength(100);
        b.HasIndex(r => new { r.TenantId, r.Key }).IsUnique();
        b.HasIndex(r => r.ExpiresAt);
    }
}

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> b)
    {
        b.ToTable("stored_files");
        b.HasAlternateKey(f => new { f.TenantId, f.Id });
        b.Property(f => f.FileName).HasMaxLength(255);
        b.Property(f => f.ContentType).HasMaxLength(100);
        b.Property(f => f.Sha256).HasMaxLength(64);
        b.Property(f => f.Category).HasMaxLength(40);
        b.HasIndex(f => new { f.TenantId, f.Category });
    }
}

internal sealed class StoredFileContentConfiguration : IEntityTypeConfiguration<StoredFileContent>
{
    public void Configure(EntityTypeBuilder<StoredFileContent> b)
    {
        b.ToTable("stored_file_contents");
        b.HasKey(c => c.FileId);
        b.HasOne<StoredFile>()
            .WithOne()
            .HasForeignKey<StoredFileContent>(c => new { c.TenantId, c.FileId })
            .HasPrincipalKey<StoredFile>(f => new { f.TenantId, f.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TenantSequenceConfiguration : IEntityTypeConfiguration<TenantSequence>
{
    public void Configure(EntityTypeBuilder<TenantSequence> b)
    {
        b.ToTable("tenant_sequences");
        b.HasKey(s => new { s.TenantId, s.Name });
        b.Property(s => s.Name).HasMaxLength(40);
    }
}

internal sealed class ScheduledJobRunConfiguration : IEntityTypeConfiguration<ScheduledJobRun>
{
    public void Configure(EntityTypeBuilder<ScheduledJobRun> b)
    {
        b.ToTable("scheduled_job_runs");
        b.Property(r => r.JobName).HasMaxLength(100);
        b.Property(r => r.RunKey).HasMaxLength(100);
        b.Property(r => r.InstanceId).HasMaxLength(100);
        b.Property(r => r.Error).HasMaxLength(4000);
        b.HasIndex(r => new { r.JobName, r.RunKey }).IsUnique();
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> b)
    {
        b.ToTable("user_sessions");
        b.Property(s => s.IpAddress).HasMaxLength(64);
        b.Property(s => s.UserAgent).HasMaxLength(512);
        b.Property(s => s.RevokedReason).HasMaxLength(100);
        b.HasIndex(s => new { s.UserId, s.RevokedAt });
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.Property(t => t.TokenHash).HasMaxLength(64);
        b.Property(t => t.CreatedByIp).HasMaxLength(64);
        b.Property(t => t.RevokedReason).HasMaxLength(100);
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.HasIndex(t => t.SessionId);
        b.HasOne<UserSession>().WithMany().HasForeignKey(t => t.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class IdentityTableNames :
    IEntityTypeConfiguration<ApplicationUser>,
    IEntityTypeConfiguration<ApplicationRole>,
    IEntityTypeConfiguration<IdentityUserRole<Guid>>,
    IEntityTypeConfiguration<IdentityUserClaim<Guid>>,
    IEntityTypeConfiguration<IdentityUserLogin<Guid>>,
    IEntityTypeConfiguration<IdentityUserToken<Guid>>,
    IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.ToTable("users");
        b.Property(u => u.FirstName).HasMaxLength(100);
        b.Property(u => u.LastName).HasMaxLength(100);
        b.Ignore(u => u.DisplayName);
    }

    public void Configure(EntityTypeBuilder<ApplicationRole> b)
    {
        b.ToTable("roles");
        b.Property(r => r.Description).HasMaxLength(300);
    }

    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> b) => b.ToTable("user_roles");

    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> b) => b.ToTable("user_claims");

    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> b) => b.ToTable("user_logins");

    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> b) => b.ToTable("user_tokens");

    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> b) => b.ToTable("role_claims");
}
