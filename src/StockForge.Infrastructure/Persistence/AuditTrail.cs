using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Domain.Platform;

namespace StockForge.Infrastructure.Persistence;

/// <summary>Explicit audit entries for actions that are not plain entity changes (logins, approvals, security events).</summary>
internal sealed class AuditTrail(AppDbContext db, ScopeContext scope, TimeProvider clock) : IAuditTrail
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Record(string action, string entityType, string? entityId, object? details = null) =>
        RecordFor(scope.TenantId, scope.UserId, scope.Email, action, entityType, entityId, details);

    public void RecordFor(Guid? tenantId, Guid? userId, string? email, string action, string entityType, string? entityId, object? details = null) =>
        db.AuditLogs.Add(new AuditLog(
            tenantId,
            userId,
            email,
            action,
            entityType,
            entityId,
            null,
            details is null ? null : JsonSerializer.Serialize(details, Json),
            null,
            scope.IpAddress,
            scope.UserAgent,
            scope.CorrelationId,
            clock.GetUtcNow()));
}

/// <summary>Stores binaries in PostgreSQL (tenant-scoped, RLS-protected). Swap for S3 by implementing <see cref="IFileStorage"/>.</summary>
internal sealed class DatabaseFileStorage(AppDbContext db, ICurrentUser currentUser, TimeProvider clock) : IFileStorage
{
    public Guid Stage(string fileName, string contentType, byte[] content, string category)
    {
        var file = new StoredFile(fileName, contentType, content.LongLength, Convert.ToHexStringLower(SHA256.HashData(content)),
            category, currentUser.UserId, clock.GetUtcNow());
        db.StoredFiles.Add(file);
        db.StoredFileContents.Add(new StoredFileContent(file.Id, content));
        return file.Id;
    }

    public async Task<StoredFileData?> GetAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null)
        {
            return null;
        }

        var content = await db.StoredFileContents.AsNoTracking()
            .Where(c => c.FileId == fileId)
            .Select(c => c.Data)
            .FirstOrDefaultAsync(cancellationToken);
        return content is null ? null : new StoredFileData(file.Id, file.FileName, file.ContentType, content);
    }

    public async Task DeleteAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is not null)
        {
            db.StoredFiles.Remove(file);
            var content = await db.StoredFileContents.FirstOrDefaultAsync(c => c.FileId == fileId, cancellationToken);
            if (content is not null)
            {
                db.StoredFileContents.Remove(content);
            }
        }
    }
}
