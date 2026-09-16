using AI.DocumentReader.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Services;

public interface IMedicalSoftwareWriter
{
    Task WriteAsync(IReadOnlyList<SelfUploadedReportData> rows, Func<IReadOnlyList<long>, Task> prepared, CancellationToken ct);
}

public sealed class ExportDeliveryException(bool safeToRetry) : Exception("Secondary export did not complete.")
{
    public bool SafeToRetry { get; } = safeToRetry;
}

public sealed class MedicalSoftwareWriter(MedicalSoftwareDbContext db) : IMedicalSoftwareWriter
{
    public async Task WriteAsync(IReadOnlyList<SelfUploadedReportData> rows, Func<IReadOnlyList<long>, Task> prepared, CancellationToken ct)
    {
        // Defense at the persistence boundary as well as in the mapper. Never round/clamp.
        foreach (var row in rows) row.Result = SelfUploadedReportMapper.SafeNumeric(row.Result);
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction;
        try { transaction = await db.Database.BeginTransactionAsync(ct); }
        catch { throw new ExportDeliveryException(safeToRetry: true); } // No row writes have started.
        await using (transaction)
        {
            var commitStarted = false;
            try
            {
                db.SelfUploadedReportData.AddRange(rows);
                await db.SaveChangesAsync(ct);
                // Keep generated IDs in the primary ledger before COMMIT for operator reconciliation.
                await prepared(rows.Select(x => x.Id).ToArray());
                commitStarted = true;
                await transaction.CommitAsync(ct);
            }
            catch
            {
                if (commitStarted) throw new ExportDeliveryException(safeToRetry: false);
                try { await transaction.RollbackAsync(CancellationToken.None); }
                catch { throw new ExportDeliveryException(safeToRetry: false); }
                throw new ExportDeliveryException(safeToRetry: true);
            }
            finally { db.ChangeTracker.Clear(); }
        }
    }
}
