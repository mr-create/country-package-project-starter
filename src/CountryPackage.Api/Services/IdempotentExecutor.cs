using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CountryPackage.Api.Domain;
using CountryPackage.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CountryPackage.Api.Services;

public sealed record CommandResult<T>(T Value, int StatusCode, bool IsReplay);

public sealed class IdempotentExecutor(AppDbContext db, TimeProvider clock)
{
    public async Task<CommandResult<T>> ExecuteAsync<T>(
        string actorUserId,
        string operation,
        string key,
        string requestHash,
        int successStatusCode,
        Func<Task<(T Value, Guid PackageId)>> action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            throw new ApiException(400, "idempotency.invalid_key", "A valid Idempotency-Key header is required.");

        var existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(
            x => x.ActorUserId == actorUserId && x.Operation == operation && x.Key == key,
            cancellationToken);

        if (existing is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(existing.RequestHash), Encoding.UTF8.GetBytes(requestHash)))
                throw new ApiException(409, "idempotency.key_reused", "The idempotency key was already used for a different request.");

            var replay = JsonSerializer.Deserialize<T>(existing.ResponseJson, JsonDefaults.Options)
                         ?? throw new InvalidOperationException("Stored idempotency response could not be read.");
            return new(replay, existing.StatusCode, true);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var (value, packageId) = await action();
            db.IdempotencyRecords.Add(new IdempotencyRecordEntity
            {
                Id = Guid.NewGuid(),
                CountryPackageId = packageId,
                ActorUserId = actorUserId,
                Operation = operation,
                Key = key,
                RequestHash = requestHash,
                ResponseJson = JsonSerializer.Serialize(value, JsonDefaults.Options),
                StatusCode = successStatusCode,
                CreatedAt = clock.GetUtcNow()
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(value, successStatusCode, false);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ApiException(409, "workflow.concurrent_update", "The package changed while this request was being processed.");
        }
        catch (DbUpdateException exception) when (exception.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ApiException(409, "idempotency.concurrent_request", "Another request with this idempotency key is being processed.");
        }
        catch (DbUpdateException exception) when (exception.InnerException?.Message.Contains("locked", StringComparison.OrdinalIgnoreCase) == true)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ApiException(409, "workflow.concurrent_update", "The package is being updated by another request.");
        }
    }

    public static string Hash(params string?[] values)
    {
        var joined = string.Join('\n', values.Select(x => x ?? "<null>"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined))).ToLowerInvariant();
    }
}
