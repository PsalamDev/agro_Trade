using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AgroTrade.Infrastructure.Data;

/// <summary>
/// Allocates human-readable sequence numbers (RFQ, order) that are computed as MAX(suffix) + 1.
/// </summary>
/// <remarks>
/// <para>
/// That computation is a read-then-write and therefore racy: two simultaneous creators can read
/// the same maximum and both try to insert the same number, and the loser gets an unhandled
/// unique-index violation surfacing as a 500. A test suite running classes in parallel against
/// one database hits this readily; production hits it whenever two buyers convert quotations in
/// the same moment.
/// </para>
/// <para>
/// The fix is to make the read block rather than race. The number is read inside a transaction
/// with <c>UPDLOCK, HOLDLOCK</c> over the indexed number column, so a second allocator waits for
/// the first to commit and then re-reads — and sees the row it just missed. This is the
/// documented SQL Server pattern for a max-plus-one sequence, and it needs no retry loop: a
/// plain retry cannot work here, because under READ COMMITTED every retry re-reads before the
/// winner commits and so recomputes the very number that just lost.
/// </para>
/// <para>
/// The lock is a range lock over one index range, not a global resource, so it serialises
/// allocation within a year-prefixed series and nothing else. An <c>sp_getapplock</c> over a
/// single shared name was tried and rejected: it queued every RFQ and order in the system behind
/// one resource, and because a queue needs a timeout, a contended request eventually failed with
/// an unhandled driver exception — trading one 500 for a rarer one.
/// </para>
/// </remarks>
public static class SequenceAllocator
{
    /// <summary>
    /// Reads the next free number for <paramref name="prefix"/> and persists the entity holding
    /// it, all under a range lock so a concurrent allocator cannot pick the same number.
    /// </summary>
    /// <param name="db">Caller-owned context. Must have no transaction open already.</param>
    /// <param name="table">Table holding the numbers, e.g. "Orders".</param>
    /// <param name="column">The unique, indexed number column on it, e.g. "OrderNumber".</param>
    /// <param name="prefix">Year-scoped prefix, e.g. "AG-2026-".</param>
    /// <param name="assign">Writes the chosen number onto the entity being inserted.</param>
    /// <param name="save">
    /// The caller's save, which must persist the entity <paramref name="assign"/> wrote to.
    /// </param>
    /// <remarks>
    /// Everything the caller already staged is written by <paramref name="save"/> inside this
    /// transaction, so the number, the row, and its side effects — reservations, notifications —
    /// all commit or roll back together. The transaction is serialisable so the range lock is
    /// held to commit; under a looser level the lock would be released early and the race return.
    /// </remarks>
    public static async Task AllocateAsync(
        AgroTradeDbContext db,
        string table,
        string column,
        string prefix,
        Action<string> assign,
        Func<CancellationToken, Task> save,
        CancellationToken ct = default)
    {
        IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            // EF1002 is suppressed deliberately. The table and column are compile-time
            // constants from the two call sites in this assembly, never request data, so there is
            // nothing to inject; the prefix, which does come from the clock, is still passed as
            // a parameter. The identifiers cannot be parameterised in T-SQL anyway, and the lock
            // hint cannot be expressed through the query provider, which is the only reason this
            // is raw SQL.
#pragma warning disable EF1002
            var existing = await db.Database
                .SqlQueryRaw<string>(
                    $"""
                    SELECT [{column}] AS [Value]
                    FROM [{table}] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [{column}] LIKE @prefix + '%'
                    """,
                    new SqlParameter("@prefix", prefix))
                .ToListAsync(ct);
#pragma warning restore EF1002

            assign(Next(prefix, existing));
            await save(ct);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    /// <summary>
    /// Allocates <paramref name="count"/> consecutive numbers in one lock, for a checkout that
    /// fans one cart out into several orders (Phase 4). A single lock acquires the whole run, so
    /// the numbers cannot be interleaved by another allocator; <paramref name="save"/> persists
    /// every order plus the checkout's side effects together.
    /// </summary>
    public static async Task AllocateRangeAsync(
        AgroTradeDbContext db,
        string table,
        string column,
        string prefix,
        int count,
        Action<IReadOnlyList<string>> assign,
        Func<CancellationToken, Task> save,
        CancellationToken ct = default)
    {
        IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
#pragma warning disable EF1002
            var existing = await db.Database
                .SqlQueryRaw<string>(
                    $"""
                    SELECT [{column}] AS [Value]
                    FROM [{table}] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [{column}] LIKE @prefix + '%'
                    """,
                    new SqlParameter("@prefix", prefix))
                .ToListAsync(ct);
#pragma warning restore EF1002

            assign(NextRange(prefix, existing, count));
            await save(ct);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    /// <summary>MAX(suffix) + 1 over the numbers already issued, zero-padded to six digits.</summary>
    private static string Next(string prefix, List<string> existing)
    {
        var next = existing.Count == 0
            ? 1
            : existing
                .Select(n => int.TryParse(n.AsSpan(prefix.Length), out var v) ? v : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

        return $"{prefix}{next:D6}";
    }

    /// <summary>
    /// The next <paramref name="count"/> consecutive numbers starting at MAX(suffix) + 1,
    /// so a fan-out checkout numbers its orders deterministically and in one gap-free block.
    /// </summary>
    private static List<string> NextRange(string prefix, List<string> existing, int count)
    {
        var first = existing.Count == 0
            ? 1
            : existing
                .Select(n => int.TryParse(n.AsSpan(prefix.Length), out var v) ? v : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

        return Enumerable.Range(first, count)
            .Select(n => $"{prefix}{n:D6}")
            .ToList();
    }
}