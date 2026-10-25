using System.IO;
using System.Net;
using Microsoft.Data.Sqlite;

namespace HyperDL.Core;

public sealed class DownloadDatabase
{
    private readonly string _databasePath;
    private readonly string _connectionString;

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
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public string DatabasePath => _databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
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
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await using var recoverCommand = connection.CreateCommand();
        recoverCommand.CommandText = """
            UPDATE Downloads
            SET Status = $recoverable, UpdatedAt = $updatedAt
            WHERE Status IN ($downloading, $pausing);
            """;
        recoverCommand.Parameters.AddWithValue("$recoverable", DownloadSessionStatus.Recoverable.ToString());
        recoverCommand.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        recoverCommand.Parameters.AddWithValue("$downloading", DownloadSessionStatus.Downloading.ToString());
        recoverCommand.Parameters.AddWithValue("$pausing", DownloadSessionStatus.Pausing.ToString());
        await recoverCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveSessionAsync(
        DownloadSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        long downloadedSize;
        DownloadSessionStatus status;
        StoredSegmentSnapshot[] segmentSnapshots;
        lock (session.SyncRoot)
        {
            downloadedSize = session.Segments.Sum(segment => segment.BytesDownloaded);
            status = session.Status;
            segmentSnapshots = session.Segments
                .Select(segment => new StoredSegmentSnapshot(
                    segment.Index,
                    segment.StartOffset,
                    segment.EndOffset,
                    segment.CurrentOffset,
                    segment.Status))
                .ToArray();
        }

        await InitializeAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnableForeignKeysAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
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
            command.Parameters.AddWithValue(
                "$downloadedSize",
                downloadedSize);
            command.Parameters.AddWithValue("$status", status.ToString());
            command.Parameters.AddWithValue("$etag", (object?)session.Metadata.ETag ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$lastModified",
                session.Metadata.LastModified?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$connectionCount", session.ConnectionCount);
            command.Parameters.AddWithValue("$createdAt", now);
            command.Parameters.AddWithValue("$updatedAt", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteSegments = connection.CreateCommand())
        {
            deleteSegments.Transaction = (SqliteTransaction)transaction;
            deleteSegments.CommandText = "DELETE FROM Segments WHERE DownloadId = $id;";
            deleteSegments.Parameters.AddWithValue("$id", session.Id.ToString("D"));
            await deleteSegments.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var segment in segmentSnapshots)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO Segments
                    (DownloadId, SegmentIndex, StartOffset, EndOffset, CurrentOffset, Status)
                VALUES ($id, $index, $start, $end, $current, $status);
                """;
            command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
            command.Parameters.AddWithValue("$index", segment.Index);
            command.Parameters.AddWithValue("$start", segment.StartOffset);
            command.Parameters.AddWithValue("$end", segment.EndOffset);
            command.Parameters.AddWithValue("$current", segment.CurrentOffset);
            command.Parameters.AddWithValue("$status", segment.Status.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DownloadSession>> LoadSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnableForeignKeysAsync(connection, cancellationToken);
        var records = new List<StoredDownload>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Id, Url, FinalUrl, FileName, DestinationPath, PartPath, TotalSize, DownloadedSize,
                       Status, ETag, LastModified, ConnectionCount
                FROM Downloads
                ORDER BY UpdatedAt DESC;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
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
        foreach (var record in records)
        {
            var segments = await LoadSegmentsAsync(connection, record.Id, cancellationToken);
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
                record.Id)
            {
                Status = record.Status
            };

            if (session.Status == DownloadSessionStatus.Recoverable)
            {
                if (File.Exists(session.FinalPath))
                {
                    session.Status = new FileInfo(session.FinalPath).Length == record.TotalSize
                        ? DownloadSessionStatus.Completed
                        : DownloadSessionStatus.Failed;
                    session.RecoveryError = session.Status == DownloadSessionStatus.Failed
                        ? "目的檔案已存在但大小不符，未覆寫該檔案。"
                        : null;
                }
                else if (!File.Exists(session.PartPath))
                {
                    session.Status = DownloadSessionStatus.Failed;
                    session.RecoveryError = "資料庫記錄存在，但暫存檔不存在。";
                }
                else if (new FileInfo(session.PartPath).Length != record.TotalSize)
                {
                    session.Status = DownloadSessionStatus.Failed;
                    session.RecoveryError = "暫存檔大小不符，無法安全恢復。";
                }
            }
            else if (session.Status == DownloadSessionStatus.Completed &&
                     (!File.Exists(session.FinalPath) ||
                      new FileInfo(session.FinalPath).Length != record.TotalSize))
            {
                session.Status = DownloadSessionStatus.Failed;
                session.RecoveryError = "已完成檔案不存在或大小不符。";
            }

            sessions.Add(session);
            if (session.Status != record.Status)
            {
                await SaveSessionAsync(session, cancellationToken);
            }
        }

        return sessions;
    }

    public async Task DeleteSessionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnableForeignKeysAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "DELETE FROM Downloads WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task EnableForeignKeysAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
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
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
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
