# Local test server and benchmark

These scripts exercise HyperDL's HTTP Range assumptions without changing the
application. Both run on Windows PowerShell 5.1 and PowerShell 7, bind only to
loopback, and use only in-box .NET types. No packages or downloads are needed.

## `test-server.ps1`

Serves one file at `http://127.0.0.1:PORT/file.bin` and changes its HTTP
behaviour based on the query string.

```powershell
# From a PowerShell prompt at the repository root (generates a 256 MiB file)
.\scripts\test-server.ps1

# Choose a port and size
.\scripts\test-server.ps1 -Port 18081 -SizeMB 64

# Serve an existing file instead; the server will not delete it
.\scripts\test-server.ps1 -Port 18081 -FilePath C:\temp\sample.bin
```

From `cmd.exe`, or when the execution policy blocks local scripts:

```bat
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-server.ps1 -Port 18080 -SizeMB 256
```

Parameters:

| Parameter | Default | Meaning |
| --- | ---: | --- |
| `-Port` | `18080` | Loopback TCP port. |
| `-SizeMB` | `256` | Size of the generated temp file, in MiB. Ignored when `-FilePath` is supplied. |
| `-FilePath` | empty | Existing file to serve. The file is never modified or deleted. |
| `-MaxConcurrency` | `96` | Maximum number of request runspaces. |

The generated file is created under `%TEMP%` and removed when the server stops.
Press `Ctrl+C` to stop. Request handlers run in a runspace pool so the server can
serve several ranges concurrently.

Every request is logged to the console with its method, path and query, `Range`
header, response status, and bytes sent:

```text
[12:34:56.789] GET  /file.bin?failafter=1000 Range=bytes=0-1048575 -> 206 1000 B (29 ms) [disconnect after 1000 bytes]
```

### Endpoint behaviour

All behaviours are query flags on `/file.bin`. Other paths return `404`.

| Query | Behaviour | Scenario it simulates |
| --- | --- | --- |
| none | `206` with correct `Content-Range`, `Content-Length`, `Accept-Ranges: bytes`, stable `ETag`, `Last-Modified`. | Normal single-range server. `bytes=0-99`, `bytes=100-`, and `bytes=-100` are supported. |
| `?norange=1` | Ignores `Range` and returns `200` with the full body. Does not advertise `Accept-Ranges`. | Server without byte-range support; the download engine should fall back to one stream. |
| `?failafter=N` | For a range request, writes `N` bytes, then aborts the connection while still advertising the full range length. | Mid-segment disconnect or truncated body. `N=0` drops after the headers. |
| `?throttle=KBPS` | Limits each connection to approximately `KBPS` KiB/s (`1024` bytes per KB). | Slow or congested link. The limit is per connection, not global. |
| `?delayms=N` | Sleeps `N` milliseconds before sending headers. | Slow response headers or a timing-out probe. |
| `?ratelimit=1` | Returns `429` with `Retry-After: 2` for the first two requests from a client, then serves normally. | Rate-limited host and retry handling. The counter resets when the server restarts. |
| `?changeful=1` | Alternates the `ETag` between `"hyperdl-change-a"` and `"hyperdl-change-b"` on successive requests and sends `Cache-Control: no-store`. | Source file changing between probe and download, or between resumes. |
| `?badlength=1` | Sends `Content-Length` one byte larger than the actual range and drops the connection after the body. | Broken server length; the client should detect a short read instead of hanging. |
| `?status=403` | Returns `403`. | Forbidden source. |
| `?status=404` | Returns `404`. | Missing source. |
| `?status=500` | Returns `500`. | Server failure. |

Additional notes:

- `HEAD` is supported on every endpoint. It returns the same status and headers
  as `GET`, with no body and zero bytes sent.
- An unsatisfiable range (for example a start offset beyond EOF) returns `416`
  with `Content-Range: bytes */SIZE`.
- Only one range per request is implemented. A malformed or multi-range header
  is ignored and the full file is returned with `200`.
- Delay applies before all responses, including forced statuses.
- `failafter` applies to range requests only. If `N` is greater than or equal to
  the range length, the response completes normally.
- Port conflicts or a blocked URL prefix produce a startup error. Loopback
  prefixes normally work without an URL reservation. If Windows returns an
  access-denied error, pick another port or reserve the prefix for the current
  user with `netsh http add urlacl` (administrator required).

## `benchmark.ps1`

Starts `test-server.ps1` on a free loopback port with a generated temp file, then
measures raw HTTP Range throughput with a .NET `HttpClient` harness.

```powershell
# Default: 64 MiB file, connection counts 1, 2, 4, 8, 16, 32, 64
.\scripts\benchmark.ps1

# Short local check
.\scripts\benchmark.ps1 -SizeMB 8 -Connections 1,4

# Also build and run the tests before benchmarking
.\scripts\benchmark.ps1 -Build -Test

# Write the observed markdown table to a file (relative to the repo root)
.\scripts\benchmark.ps1 -OutFile artifacts\benchmark\baseline.md
```

From `cmd.exe`:

```bat
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\benchmark.ps1 -SizeMB 8 -Connections 1,4
```

Parameters:

| Parameter | Default | Meaning |
| --- | ---: | --- |
| `-SizeMB` | `64` | Temp file size in MiB. |
| `-Port` | `0` | Use a specific port; `0` selects a free port. |
| `-Connections` | `1,2,4,8,16,32,64` | Comma-separated connection-count cases, for example `-Connections 1,4`. |
| `-Build` | off | Runs `C:\Program Files\dotnet\dotnet.exe build HyperDL.sln -c Release`. |
| `-Test` | off | Runs `dotnet test HyperDL.sln -c Release`. If `-Build` is also present, `--no-build` is used. |
| `-OutFile` | empty | Writes the markdown table to this path, creating parent directories. |

### What the benchmark measures

For each connection count, the harness:

1. Splits the file into that many contiguous byte ranges.
2. Starts one `HttpClient` Range request per range concurrently, using a handler
   whose `MaxConnectionsPerServer` matches the connection count.
3. Validates every response is `206`, that `Content-Range` matches the requested
   range and total length, and that `Content-Length` matches.
4. Streams every body to `Stream.Null` and verifies that the received byte count
   equals the file size.
5. Reports wall-clock elapsed seconds and MB/s for that complete run.

MB/s means 1 MB = 1,048,576 bytes. Each connection count is run once (no warm-up
and no repeated trials) and the server and client are on the same machine. The
results are raw loopback transport measurements and are not HyperDL download
throughput or end-user network throughput.

The script deliberately does not launch `HyperDL.exe` or invoke the WPF download
engine headlessly. It measures the HTTP layer that the engine depends on.

### Output and cleanup

The console receives progress lines and a markdown table:

```markdown
| Connections | Elapsed (s) | MB/s | Bytes |
| ---: | ---: | ---: | ---: |
| 1 | <measured> | <measured> | 8388608 |
| 4 | <measured> | <measured> | 8388608 |
```

With `-OutFile`, the same table, timestamp, host PowerShell version, OS, file
size, and scope notes are written as UTF-8 without a BOM. The file is not written
unless `-OutFile` is supplied.

The benchmark's `finally` block stops the server process, waits for it to exit,
and deletes the generated temp file and temporary server logs. If startup or a
run fails, the tail of the server log is printed to stderr for diagnosis.

## Build and test

`-Build` and `-Test` are optional; the benchmark itself needs only Windows
PowerShell and the .NET Framework/.NET runtime that hosts it. When either switch
is used, the script checks for the .NET 8 SDK at:

```text
C:\Program Files\dotnet\dotnet.exe
```

Build and test failures stop the script with a non-zero exit code before the
benchmark starts. The scripts do not modify C# or XAML files, `.github`, or the
installer.
