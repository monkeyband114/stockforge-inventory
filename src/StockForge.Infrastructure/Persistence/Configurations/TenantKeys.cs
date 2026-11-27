using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockForge.Domain.Common;

namespace StockForge.Infrastructure.Persistence.Configurations;

/// <summary>
/// Helpers for tenant-composite relationships. Principals expose a unique <c>(tenant_id, id)</c> key and
/// dependents reference it with <c>(tenant_id, fk)</c>, so the database itself rejects any row that points at
/// another tenant's record, regardless of application code.
/// </summary>
internal static class TenantKeys
{
    public static void HasTenantKey<T>(this EntityTypeBuilder<T> builder) where T : Entity, ITenantOwned =>
        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

    public static ReferenceCollectionBuilder<TPrincipal, TDependent> TenantReference<TDependent, TPrincipal>(
        this EntityTypeBuilder<TDependent> builder,
        Expression<Func<TDependent, TPrincipal?>>? navigation,
        Expression<Func<TDependent, object?>> foreignKey,
        DeleteBehavior onDelete = DeleteBehavior.Restrict)
        where TDependent : class, ITenantOwned
        where TPrincipal : Entity, ITenantOwned =>
        builder.HasOne(navigation)
            .WithMany()
            .HasForeignKey(foreignKey)
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(onDelete);
}
