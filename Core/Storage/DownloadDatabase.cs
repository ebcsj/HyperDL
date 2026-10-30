using System.IO;
using System.Net;
using Microsoft.Data.Sqlite;

namespace HyperDL.Core;

public sealed class DownloadDatabase
{
    private const int SchemaVersion = 2;
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private volatile bool _initialized;

    public DownloadDatabase(string? databasePath = null)
    {
        _databasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HyperDL",
            "hyperdl.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 30,
            Pooling = true
        }.ToString();
    }

    public string DatabasePath => _databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);

            await using (var wal = connection.CreateCommand())
            {
                wal.CommandText = "PRAGMA journal_mode = WAL;";
                await wal.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var schema = connection.CreateCommand())
            {
                schema.CommandText = """
                    CREATE TABLE IF NOT EXISTS Downloads (
                        Id TEXT PRIMARY KEY,
                        Url TEXT NOT NULL,
                        FinalUrl TEXT NOT NULL,
                        FileName TEXT NOT NULL,
                        DestinationPath TEXT NOT NULL,
                        PartPath TEXT NOT NULL,
                        TotalSize INTEGER NOT NULL,
                        DownloadedSize INTEGER NOT NULL,
                        Status TEXT NOT NULL,
                        ETag TEXT NULL,
                        LastModified TEXT NULL,
                        ConnectionCount INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS Segments (
                        DownloadId TEXT NOT NULL,
                        SegmentIndex INTEGER NOT NULL,
                        StartOffset INTEGER NOT NULL,
                        EndOffset INTEGER NOT NULL,
                        CurrentOffset INTEGER NOT NULL,
                        Status TEXT NOT NULL,
                        PRIMARY KEY (DownloadId, SegmentIndex),
                        FOREIGN KEY (DownloadId) REFERENCES Downloads(Id) ON DELETE CASCADE
                    );
                    CREATE INDEX IF NOT EXISTS IX_Segments_DownloadId
                        ON Segments (DownloadId);
                    CREATE TABLE IF NOT EXISTS PendingDownloads (
                        Id TEXT PRIMARY KEY,
                        Url TEXT NOT NULL,
                        FileName TEXT NOT NULL,
                        RequestedConnections INTEGER NOT NULL,
                        BufferMode TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL
                    );
                    """;
                await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var version = connection.CreateCommand())
            {
                version.CommandText = $"PRAGMA user_version = {SchemaVersion};";
                await version.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task SaveSessionAsync(
        DownloadSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        long downloadedSize;
        DownloadSessionStatus status;
        StoredSegmentSnapshot[] segmentSnapshots;
        lock (session.SyncRoot)
        {
            var segments = session.Segments;
            downloadedSize = segments.Sum(segment => segment.BytesDownloaded);
            status = session.Status;
            segmentSnapshots = segments
                .Select(segment => new StoredSegmentSnapshot(
                    segment.Index,
                    segment.StartOffset,
                    segment.EndOffset,
                    segment.CurrentOffset,
                    segment.Status))
                .OrderBy(segment => segment.Index)
                .ToArray();
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO Downloads
                    (Id, Url, FinalUrl, FileName, DestinationPath, PartPath, TotalSize,
                     DownloadedSize, Status, ETag, LastModified, ConnectionCount, CreatedAt, UpdatedAt)
                VALUES
                    ($id, $url, $finalUrl, $fileName, $destinationPath, $partPath, $totalSize,
                     $downloadedSize, $status, $etag, $lastModified, $connectionCount, $createdAt, $updatedAt)
                ON CONFLICT(Id) DO UPDATE SET
                    Url = excluded.Url,
                    FinalUrl = excluded.FinalUrl,
                    FileName = excluded.FileName,
                    DestinationPath = excluded.DestinationPath,
                    PartPath = excluded.PartPath,
                    TotalSize = excluded.TotalSize,
                    DownloadedSize = excluded.DownloadedSize,
                    Status = excluded.Status,
                    ETag = excluded.ETag,
                    LastModified = excluded.LastModified,
                    ConnectionCount = excluded.ConnectionCount,
                    UpdatedAt = excluded.UpdatedAt;
                """;
            command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
            command.Parameters.AddWithValue(
                "$url",
                (session.Metadata.OriginalUri ?? session.Metadata.FinalUri).ToString());
            command.Parameters.AddWithValue("$finalUrl", session.Metadata.FinalUri.ToString());
            command.Parameters.AddWithValue("$fileName", session.FileName);
            command.Parameters.AddWithValue("$destinationPath", Path.GetDirectoryName(session.FinalPath)!);
            command.Parameters.AddWithValue("$partPath", session.PartPath);
            command.Parameters.AddWithValue("$totalSize", session.Metadata.ContentLength!.Value);
            command.Parameters.AddWithValue("$downloadedSize", downloadedSize);
            command.Parameters.AddWithValue("$status", status.ToString());
            command.Parameters.AddWithValue("$etag", (object?)session.Metadata.ETag ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$lastModified",
                session.Metadata.LastModified?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$connectionCount", session.ConnectionCount);
            command.Parameters.AddWithValue("$createdAt", now);
            command.Parameters.AddWithValue("$updatedAt", now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var segment in segmentSnapshots)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO Segments
                    (DownloadId, SegmentIndex, StartOffset, EndOffset, CurrentOffset, Status)
                VALUES ($id, $index, $start, $end, $current, $status)
                ON CONFLICT(DownloadId, SegmentIndex) DO UPDATE SET
                    StartOffset = excluded.StartOffset,
                    EndOffset = excluded.EndOffset,
                    CurrentOffset = excluded.CurrentOffset,
                    Status = excluded.Status;
                """;
            command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
            command.Parameters.AddWithValue("$index", segment.Index);
            command.Parameters.AddWithValue("$start", segment.StartOffset);
            command.Parameters.AddWithValue("$end", segment.EndOffset);
            command.Parameters.AddWithValue("$current", segment.CurrentOffset);
            command.Parameters.AddWithValue("$status", segment.Status.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var liveIndices = segmentSnapshots.Select(segment => segment.Index).ToHashSet();
        var storedIndices = new List<int>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "SELECT SegmentIndex FROM Segments WHERE DownloadId = $id;";
            command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                storedIndices.Add(reader.GetInt32(0));
            }
        }

        foreach (var staleIndex in storedIndices.Where(index => !liveIndices.Contains(index)))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                DELETE FROM Segments
                WHERE DownloadId = $id AND SegmentIndex = $index;
                """;
            command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
            command.Parameters.AddWithValue("$index", staleIndex);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DownloadSession>> LoadSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var records = new List<StoredDownload>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Id, Url, FinalUrl, FileName, DestinationPath, PartPath, TotalSize, DownloadedSize,
                       Status, ETag, LastModified, ConnectionCount
                FROM Downloads
                ORDER BY UpdatedAt DESC;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                records.Add(new StoredDownload(
                    Guid.Parse(reader.GetString(0)),
                    new Uri(reader.GetString(1), UriKind.Absolute),
                    new Uri(reader.GetString(2), UriKind.Absolute),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7),
                    Enum.TryParse<DownloadSessionStatus>(reader.GetString(8), out var status)
                        ? status
                        : DownloadSessionStatus.Failed,
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10)
                        ? null
                        : DateTimeOffset.Parse(reader.GetString(10)),
                    reader.GetInt32(11)));
            }
        }

        var sessions = new List<DownloadSession>(records.Count);
        var changedSessions = new List<DownloadSession>();
        foreach (var record in records)
        {
            var segments = await LoadSegmentsAsync(connection, record.Id, cancellationToken)
                .ConfigureAwait(false);
            var metadata = new DownloadMetadata(
                record.FinalUri,
                HttpStatusCode.PartialContent,
                record.FileName,
                null,
                record.TotalSize,
                record.ETag,
                record.LastModified,
                true,
                ByteRangeSupport.Supported)
            {
                OriginalUri = record.Url
            };
            var session = new DownloadSession(
                metadata,
                record.FileName,
                Path.Combine(record.DestinationPath, record.FileName),
                record.ConnectionCount,
                segments,
                record.Id,
                record.PartPath)
            {
                Status = record.Status
            };

            RecoverSession(session, record);
            sessions.Add(session);
            if (session.Status != record.Status)
            {
                changedSessions.Add(session);
            }
        }

        foreach (var session in changedSessions)
        {
            await SaveSessionAsync(session, cancellationToken).ConfigureAwait(false);
        }

        return sessions;
    }

    public async Task DeleteSessionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            DELETE FROM Downloads WHERE Id = $id;
            DELETE FROM PendingDownloads WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Persists a URL that has been queued but not probed yet. Without this the
    /// queue would lose every job added after the last session checkpoint.
    /// </summary>
    public async Task SavePendingAsync(
        PendingDownload pending,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pending);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PendingDownloads
                (Id, Url, FileName, RequestedConnections, BufferMode, CreatedAt)
            VALUES ($id, $url, $fileName, $connections, $bufferMode, $createdAt)
            ON CONFLICT(Id) DO UPDATE SET
                Url = excluded.Url,
                FileName = excluded.FileName,
                RequestedConnections = excluded.RequestedConnections,
                BufferMode = excluded.BufferMode;
            """;
        command.Parameters.AddWithValue("$id", pending.Id.ToString("D"));
        command.Parameters.AddWithValue("$url", pending.Url.ToString());
        command.Parameters.AddWithValue("$fileName", pending.FileName);
        command.Parameters.AddWithValue("$connections", pending.RequestedConnections);
        command.Parameters.AddWithValue("$bufferMode", pending.BufferMode.ToString());
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeletePendingAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PendingDownloads WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingDownload>> LoadPendingAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var pending = new List<PendingDownload>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Url, FileName, RequestedConnections, BufferMode
            FROM PendingDownloads
            ORDER BY CreatedAt;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!Guid.TryParse(reader.GetString(0), out var id) ||
                !Uri.TryCreate(reader.GetString(1), UriKind.Absolute, out var uri) ||
                !DownloadUrlValidator.IsSupported(uri))
            {
                continue;
            }

            var bufferMode = Enum.TryParse<BufferMode>(reader.GetString(4), out var parsed)
                ? parsed
                : BufferMode.Auto;
            pending.Add(new PendingDownload(
                id,
                uri,
                reader.GetString(2),
                reader.GetInt32(3),
                bufferMode));
        }

        return pending;
    }

    private static void RecoverSession(DownloadSession session, StoredDownload record)
    {
        var partValid = File.Exists(session.PartPath) &&
                        new FileInfo(session.PartPath).Length == record.TotalSize;

        switch (record.Status)
        {
            case DownloadSessionStatus.Downloading:
            case DownloadSessionStatus.Pausing:
                if (partValid)
                {
                    session.Status = DownloadSessionStatus.Recoverable;
                }
                else
                {
                    session.Status = DownloadSessionStatus.Failed;
                    session.RecoveryError = File.Exists(session.PartPath)
                        ? "暫存檔大小不符，無法安全恢復。"
                        : "資料庫記錄存在，但暫存檔不存在。";
                }

                break;

            case DownloadSessionStatus.Paused:
            case DownloadSessionStatus.Recoverable:
                if (partValid)
                {
                    session.Status = record.Status;
                }
                else
                {
                    session.Status = DownloadSessionStatus.Failed;
                    session.RecoveryError = File.Exists(session.PartPath)
                        ? "暫存檔大小不符，無法安全恢復。"
                        : "資料庫記錄存在，但暫存檔不存在。";
                }

                break;

            case DownloadSessionStatus.Completed:
                if (!File.Exists(session.FinalPath) ||
                    new FileInfo(session.FinalPath).Length != record.TotalSize)
                {
                    session.Status = DownloadSessionStatus.Failed;
                    session.RecoveryError = "已完成檔案不存在或大小不符。";
                }

                break;

            case DownloadSessionStatus.Waiting:
                if (!File.Exists(session.PartPath) &&
                    !File.Exists(session.FinalPath))
                {
                    session.Status = DownloadSessionStatus.Failed;
                    session.RecoveryError = "下載尚未開始，且找不到暫存檔。";
                }

                break;
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 30000;
            PRAGMA synchronous = NORMAL;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<DownloadSegment>> LoadSegmentsAsync(
        SqliteConnection connection,
        Guid downloadId,
        CancellationToken cancellationToken)
    {
        var segments = new List<DownloadSegment>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SegmentIndex, StartOffset, EndOffset, CurrentOffset, Status
            FROM Segments
            WHERE DownloadId = $id
            ORDER BY SegmentIndex;
            """;
        command.Parameters.AddWithValue("$id", downloadId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var status = Enum.TryParse<DownloadSegmentStatus>(reader.GetString(4), out var parsed)
                ? parsed
                : DownloadSegmentStatus.Failed;
            segments.Add(new DownloadSegment(
                reader.GetInt32(0),
                new ByteRange(reader.GetInt64(1), reader.GetInt64(2)),
                reader.GetInt64(3),
                status));
        }

        return segments;
    }

    private sealed record StoredDownload(
        Guid Id,
        Uri Url,
        Uri FinalUri,
        string FileName,
        string DestinationPath,
        string PartPath,
        long TotalSize,
        long DownloadedSize,
        DownloadSessionStatus Status,
        string? ETag,
        DateTimeOffset? LastModified,
        int ConnectionCount);

    private sealed record StoredSegmentSnapshot(
        int Index,
        long StartOffset,
        long EndOffset,
        long CurrentOffset,
        DownloadSegmentStatus Status);
}

/// <summary>
/// A queued URL that has not been probed yet. It is deliberately stored
/// separately from <see cref="DownloadSession"/> so an app restart can rebuild
/// the queue without inventing fake segment metadata.
/// </summary>
public sealed record PendingDownload(
    Guid Id,
    Uri Url,
    string FileName,
    int RequestedConnections,
    BufferMode BufferMode);
