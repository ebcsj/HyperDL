using System.Net;
using HyperDL.Core;
using Microsoft.Data.Sqlite;

namespace HyperDL.Tests;

public class DownloadDatabaseTests
{
    [Fact]
    public async Task LoadSessions_MarksInterruptedDownloadRecoverableAndRestoresOffsets()
    {
        using var context = new DatabaseTestContext();
        var database = new DownloadDatabase(Path.Combine(context.DirectoryPath, "hyperdl.db"));
        await database.InitializeAsync();
        var session = context.Engine.CreateSession(CreateMetadata(), 2);
        Directory.CreateDirectory(Path.GetDirectoryName(session.PartPath)!);
        await using (var part = new FileStream(
                         session.PartPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None))
        {
            part.SetLength(session.Metadata.ContentLength!.Value);
        }

        await database.SaveSessionAsync(session);
        await SetStoredStatusAsync(database, session.Id);
        await using (var connection = new SqliteConnection(
                         new SqliteConnectionStringBuilder { DataSource = database.DatabasePath }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE Segments
                SET CurrentOffset = StartOffset + 123, Status = $status
                WHERE DownloadId = $id AND SegmentIndex = 1;
                """;
            command.Parameters.AddWithValue("$status", DownloadSegmentStatus.Downloading.ToString());
            command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        var recovered = Assert.Single(await database.LoadSessionsAsync());

        Assert.Equal(DownloadSessionStatus.Recoverable, recovered.Status);
        Assert.Equal(123, recovered.Segments[0].BytesDownloaded);
        Assert.Equal(session.Id, recovered.Id);
        Assert.True(File.Exists(recovered.PartPath));
    }

    [Fact]
    public async Task LoadSessions_MissingPartMarksInterruptedDownloadFailed()
    {
        using var context = new DatabaseTestContext();
        var database = new DownloadDatabase(Path.Combine(context.DirectoryPath, "hyperdl.db"));
        var session = context.Engine.CreateSession(CreateMetadata(), 2);
        await database.SaveSessionAsync(session);
        await SetStoredStatusAsync(database, session.Id);

        var recovered = Assert.Single(await database.LoadSessionsAsync());

        Assert.Equal(DownloadSessionStatus.Failed, recovered.Status);
        Assert.Equal(
            "The database record exists, but the partial file is missing.",
            recovered.RecoveryError);
    }

    [Fact]
    public async Task SaveSession_UsesTransactionForDownloadAndSegmentRows()
    {
        using var context = new DatabaseTestContext();
        var database = new DownloadDatabase(Path.Combine(context.DirectoryPath, "hyperdl.db"));
        var session = context.Engine.CreateSession(CreateMetadata(), 2);
        await database.SaveSessionAsync(session);

        var loaded = Assert.Single(await database.LoadSessionsAsync());

        Assert.Equal(session.Segments.Count, loaded.Segments.Count);
        Assert.Equal(session.Segments[0].StartOffset, loaded.Segments[0].StartOffset);
        Assert.Equal(session.Segments[0].EndOffset, loaded.Segments[0].EndOffset);
    }

    [Fact]
    public async Task Initialize_MigratesPendingDownloadsTableToStartAfter()
    {
        using var context = new DatabaseTestContext();
        var dbPath = Path.Combine(context.DirectoryPath, "hyperdl.db");
        await using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE PendingDownloads (
                    Id TEXT PRIMARY KEY,
                    Url TEXT NOT NULL,
                    FileName TEXT NOT NULL,
                    RequestedConnections INTEGER NOT NULL,
                    BufferMode TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                INSERT INTO PendingDownloads VALUES
                    ('bf58e5a1-1111-4a6b-9a54-000000000001',
                     'https://unit.test/a.bin', 'a.bin', 2, 'Auto',
                     '2026-01-01T00:00:00.0000000Z');
                PRAGMA user_version = 2;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var database = new DownloadDatabase(dbPath);
        await database.InitializeAsync();

        var pending = Assert.Single(await database.LoadPendingAsync());
        Assert.Equal(2, pending.RequestedConnections);
        Assert.Null(pending.StartAfter);

        await using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var version = (long)(await command.ExecuteScalarAsync())!;
            Assert.Equal(3, version);
        }
    }

    [Fact]
    public async Task SavePending_PersistsScheduledStartAfter()
    {
        using var context = new DatabaseTestContext();
        var database = new DownloadDatabase(Path.Combine(context.DirectoryPath, "hyperdl.db"));
        var start = new DateTime(2026, 12, 1, 8, 30, 0, DateTimeKind.Local);

        await database.SavePendingAsync(new PendingDownload(
            Guid.NewGuid(),
            new Uri("https://unit.test/a.bin"),
            "a.bin",
            4,
            BufferMode.Auto,
            start));

        var pending = Assert.Single(await database.LoadPendingAsync());
        Assert.Equal(4, pending.RequestedConnections);
        Assert.NotNull(pending.StartAfter);
        Assert.Equal(start, pending.StartAfter!.Value, TimeSpan.FromSeconds(1));
    }

    private static DownloadMetadata CreateMetadata() =>
        new(
            new Uri("https://example.test/archive.bin"),
            HttpStatusCode.PartialContent,
            "archive.bin",
            "application/octet-stream",
            2 * DownloadEngine.MinimumSegmentSize,
            "\"stable-etag\"",
            DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
            true,
            ByteRangeSupport.Supported);

    private static async Task SetStoredStatusAsync(DownloadDatabase database, Guid id)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = database.DatabasePath }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Downloads SET Status = 'Downloading' WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync();
    }

    private sealed class DatabaseTestContext : IDisposable
    {
        public DatabaseTestContext()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"HyperDL.DatabaseTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            Engine = new DownloadEngine(new HttpClient(new NoRequestHandler()), DirectoryPath);
        }

        public string DirectoryPath { get; }
        public DownloadEngine Engine { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }

    private sealed class NoRequestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Database tests must not send network requests.");
    }
}
