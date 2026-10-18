using Atlas.Onboarding.Api.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.Api.Workflow;

/// <summary>
/// Claims due applications for this replica in one atomic statement: rows locked by another replica are skipped
/// (READPAST) and the claimed rows get <c>NextAttemptAt</c> pushed out by the lease, which hides them from everyone
/// else. No long-running transaction is held while providers are called.
/// </summary>
public sealed class WorkClaims(OnboardingDbContext db)
{
    public async Task<IReadOnlyList<string>> ClaimDueAsync(DateTimeOffset now, TimeSpan lease, int batchSize, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH due AS (
                SELECT TOP (@batch) Id, NextAttemptAt
                FROM onboarding.Applications WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE NextAttemptAt <= @now
                ORDER BY NextAttemptAt)
            UPDATE due SET NextAttemptAt = @leaseUntil
            OUTPUT inserted.Id AS [Value];
            """;

        return await db.Database
            .SqlQueryRaw<string>(
                sql,
                new SqlParameter("@batch", batchSize),
                new SqlParameter("@now", now),
                new SqlParameter("@leaseUntil", now + lease))
            .ToListAsync(cancellationToken);
    }
}
