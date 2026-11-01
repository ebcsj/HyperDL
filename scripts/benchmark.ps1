# Purpose: Benchmark local loopback HTTP Range throughput with a .NET HttpClient
#          harness against scripts/test-server.ps1. This does NOT launch HyperDL.exe
#          or invoke the WPF download engine headlessly.
# Requirements: Windows PowerShell 5.1 or PowerShell 7. No external packages.
#               -Build and -Test require the .NET 8 SDK at C:\Program Files\dotnet.
# Usage: powershell -ExecutionPolicy Bypass -File .\scripts\benchmark.ps1
#        powershell -ExecutionPolicy Bypass -File .\scripts\benchmark.ps1 -SizeMB 8 -Connections 1,4
# Output: Markdown table on the console and, when -OutFile is supplied, to that file.
[CmdletBinding()]
param(
    [ValidateRange(1, 262144)]
    [int]$SizeMB = 64,

    [ValidateRange(1, 65535)]
    [int]$Port = 0,

    [string[]]$Connections = @('1', '2', '4', '8', '16', '32', '64'),

    [switch]$Build,

    [switch]$Test,

    [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $root 'HyperDL.sln'
$serverScript = Join-Path $PSScriptRoot 'test-server.ps1'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$invariant = [System.Globalization.CultureInfo]::InvariantCulture

$serverProcess = $null
$serverOut = ''
$serverErr = ''
$tempFile = ''
$exitCode = 0

function Get-FreePort {
    $socket = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback, 0)
    try {
        $socket.Start()
        return ([System.Net.IPEndPoint]$socket.LocalEndpoint).Port
    }
    finally {
        $socket.Stop()
    }
}

function Get-InnermostMessage {
    param([System.Exception]$Exception)
    $current = $Exception
    while ($null -ne $current.InnerException) {
        $current = $current.InnerException
    }
    return $current.Message
}

function Get-LogTail {
    param([string]$Path, [int]$Count = 25)
    if ([string]::IsNullOrEmpty($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return @()
    }
    return @(Get-Content -LiteralPath $Path -Tail $Count -ErrorAction SilentlyContinue)
}

function New-BenchmarkFile {
    param([string]$Path, [long]$Length)
    $buffer = [byte[]]::new(1MB)
    ([System.Random]::new(20261026)).NextBytes($buffer)
    $stream = [System.IO.FileStream]::new(
        $Path,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None,
        1MB,
        [System.IO.FileOptions]::SequentialScan)
    try {
        $remaining = $Length
        while ($remaining -gt 0) {
            $count = [int][Math]::Min([long]$buffer.Length, $remaining)
            $stream.Write($buffer, 0, $count)
            $remaining -= $count
        }
        $stream.Flush($true)
    }
    finally {
        $stream.Dispose()
    }
}

function Get-RangeList {
    param([long]$Length, [int]$Count)
    $ranges = [System.Collections.Generic.List[object]]::new()
    $baseLength = [long][Math]::Floor([double]$Length / [double]$Count)
    $remainder = [int]($Length % $Count)
    $offset = [long]0

    for ($index = 0; $index -lt $Count; $index++) {
        $rangeLength = $baseLength
        if ($index -lt $remainder) {
            $rangeLength++
        }
        if ($rangeLength -le 0) {
            continue
        }

        $ranges.Add([pscustomobject]@{
                Index = $index
                Start = $offset
                End   = $offset + $rangeLength - 1
            })
        $offset += $rangeLength
    }

    return $ranges
}

function Invoke-RangeRun {
    param(
        [string]$Url,
        [long]$Length,
        [int]$ConnectionCount
    )

    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.MaxConnectionsPerServer = $ConnectionCount
    $handler.UseProxy = $false
    $handler.Proxy = $null
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromMinutes(10)

    $requests = [System.Collections.Generic.List[object]]::new()
    $responses = [System.Collections.Generic.List[object]]::new()
    $ranges = @(Get-RangeList -Length $Length -Count $ConnectionCount)

    try {
        $responseTasks = @()
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

        foreach ($range in $ranges) {
            $request = [System.Net.Http.HttpRequestMessage]::new(
                [System.Net.Http.HttpMethod]::Get, $Url)
            $request.Headers.Range = [System.Net.Http.Headers.RangeHeaderValue]::new(
                $range.Start, $range.End)
            $requests.Add($request)
            $responseTasks += $client.SendAsync(
                $request,
                [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead)
        }

        [System.Threading.Tasks.Task]::WaitAll(
            [System.Threading.Tasks.Task[]]$responseTasks)

        $copyTasks = @()
        $receivedBytes = [long]0
        for ($index = 0; $index -lt $responseTasks.Count; $index++) {
            $response = $responseTasks[$index].GetAwaiter().GetResult()
            $responses.Add($response)

            if ([int]$response.StatusCode -ne 206) {
                throw ('Connection {0}: expected HTTP 206, received {1}.' -f `
                    $ConnectionCount, [int]$response.StatusCode)
            }

            $expected = $ranges[$index]
            $contentRange = $response.Content.Headers.ContentRange
            if ($null -eq $contentRange -or
                -not [string]::Equals($contentRange.Unit, 'bytes',
                    [System.StringComparison]::OrdinalIgnoreCase) -or
                $contentRange.From -ne $expected.Start -or
                $contentRange.To -ne $expected.End -or
                $contentRange.Length -ne $Length) {
                throw ('Connection {0}: invalid Content-Range for bytes {1}-{2}.' -f `
                    $ConnectionCount, $expected.Start, $expected.End)
            }

            $expectedLength = $expected.End - $expected.Start + 1
            if ($null -ne $response.Content.Headers.ContentLength -and
                $response.Content.Headers.ContentLength -ne $expectedLength) {
                throw ('Connection {0}: Content-Length does not match the range.' -f `
                    $ConnectionCount)
            }

            $receivedBytes += $expectedLength
            $copyTasks += $response.Content.CopyToAsync([System.IO.Stream]::Null)
        }

        [System.Threading.Tasks.Task]::WaitAll(
            [System.Threading.Tasks.Task[]]$copyTasks)
        $stopwatch.Stop()

        if ($receivedBytes -ne $Length) {
            throw ('Connection {0}: received {1} bytes, expected {2}.' -f `
                $ConnectionCount, $receivedBytes, $Length)
        }

        $seconds = $stopwatch.Elapsed.TotalSeconds
        if ($seconds -le 0) {
            $seconds = 0.000001
        }

        return [pscustomobject]@{
            Connections = $ConnectionCount
            Seconds     = $seconds
            MegabytesPerSecond = ($receivedBytes / 1MB) / $seconds
            Bytes       = $receivedBytes
        }
    }
    finally {
        foreach ($response in $responses) {
            try {
                $response.Dispose()
            }
            catch {
            }
        }
        foreach ($request in $requests) {
            try {
                $request.Dispose()
            }
            catch {
            }
        }
        try {
            $client.Dispose()
        }
        catch {
        }
    }
}

try {
    if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
        throw "Run this script from a valid HyperDL source checkout; missing $solution"
    }
    if (-not (Test-Path -LiteralPath $serverScript -PathType Leaf)) {
        throw "Missing test server script: $serverScript"
    }

    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        Add-Type -AssemblyName System.Net.Http -ErrorAction Stop
    }

    if ($null -eq $Connections -or $Connections.Count -eq 0) {
        throw 'At least one connection count is required.'
    }
    $connectionCounts = [System.Collections.Generic.List[int]]::new()
    foreach ($entry in $Connections) {
        foreach ($part in ([string]$entry).Split(',')) {
            $text = $part.Trim()
            if ([string]::IsNullOrEmpty($text)) {
                continue
            }
            $count = 0
            if (-not [int]::TryParse($text, [ref]$count)) {
                throw "Invalid connection count: '$text'."
            }
            if ($count -lt 1 -or $count -gt 256) {
                throw "Connection counts must be between 1 and 256; received $count."
            }
            if (-not $connectionCounts.Contains($count)) {
                $connectionCounts.Add($count)
            }
        }
    }
    if ($connectionCounts.Count -eq 0) {
        throw 'At least one connection count is required.'
    }
    $connectionCounts = $connectionCounts.ToArray()

    if ($Build) {
        if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
            throw "The .NET SDK was not found at $dotnet"
        }
        Write-Output ''
        Write-Output 'Building HyperDL (Release)...'
        & $dotnet build $solution -c Release
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE"
        }
    }

    if ($Test) {
        if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
            throw "The .NET SDK was not found at $dotnet"
        }
        Write-Output ''
        Write-Output 'Running the HyperDL test suite (Release)...'
        $testArguments = @('test', $solution, '-c', 'Release')
        if ($Build) {
            $testArguments += '--no-build'
        }
        & $dotnet @testArguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet test failed with exit code $LASTEXITCODE"
        }
    }

    $benchmarkPort = if ($Port -gt 0) { $Port } else { Get-FreePort }
    $maxConnections = [int](($connectionCounts | Measure-Object -Maximum).Maximum)
    $serverConcurrency = [Math]::Max(64, $maxConnections + 8)
    $fileLength = [long]$SizeMB * 1MB
    $runId = [Guid]::NewGuid().ToString('N')
    $tempRoot = [System.IO.Path]::GetTempPath()
    $tempFile = Join-Path $tempRoot "hyperdl-benchmark-$runId.bin"
    $serverOut = Join-Path $tempRoot "hyperdl-benchmark-$runId.out.log"
    $serverErr = Join-Path $tempRoot "hyperdl-benchmark-$runId.err.log"

    Write-Output ''
    Write-Output ("Preparing {0} MiB test file: {1}" -f $SizeMB, $tempFile)
    New-BenchmarkFile -Path $tempFile -Length $fileLength

    $powerShellExe = if ($PSVersionTable.PSEdition -eq 'Core') {
        Join-Path $PSHOME 'pwsh.exe'
    }
    else {
        Join-Path $PSHOME 'powershell.exe'
    }
    if (-not (Test-Path -LiteralPath $powerShellExe -PathType Leaf)) {
        throw "Could not find the current PowerShell host at $powerShellExe"
    }

    $argumentList = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -Port {1} -FilePath "{2}" -MaxConcurrency {3}' -f `
        $serverScript, $benchmarkPort, $tempFile, $serverConcurrency

    Write-Output ("Starting test server on http://127.0.0.1:{0}/ ..." -f $benchmarkPort)
    $serverProcess = Start-Process `
        -FilePath $powerShellExe `
        -ArgumentList $argumentList `
        -WorkingDirectory $root `
        -WindowStyle Hidden `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError $serverErr `
        -PassThru

    $baseUrl = "http://127.0.0.1:$benchmarkPort/file.bin"
    $probeHandler = [System.Net.Http.HttpClientHandler]::new()
    $probeHandler.UseProxy = $false
    $probeHandler.Proxy = $null
    $probeClient = [System.Net.Http.HttpClient]::new($probeHandler)
    $probeClient.Timeout = [TimeSpan]::FromSeconds(2)

    try {
        $ready = $false
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        $lastProbeError = ''
        while (-not $ready -and [DateTime]::UtcNow -lt $deadline) {
            $serverProcess.Refresh()
            if ($serverProcess.HasExited) {
                throw ("Test server exited with code {0} before becoming ready." -f `
                    $serverProcess.ExitCode)
            }

            $probeRequest = $null
            $probeResponse = $null
            try {
                $probeRequest = [System.Net.Http.HttpRequestMessage]::new(
                    [System.Net.Http.HttpMethod]::Head, $baseUrl)
                $probeResponse = $probeClient.SendAsync(
                    $probeRequest,
                    [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
                if ([int]$probeResponse.StatusCode -eq 200) {
                    $ready = $true
                }
                else {
                    $lastProbeError = 'HTTP ' + [int]$probeResponse.StatusCode
                }
            }
            catch {
                $lastProbeError = Get-InnermostMessage $_.Exception
            }
            finally {
                if ($null -ne $probeResponse) {
                    $probeResponse.Dispose()
                }
                if ($null -ne $probeRequest) {
                    $probeRequest.Dispose()
                }
            }

            if (-not $ready) {
                [System.Threading.Thread]::Sleep(150)
            }
        }

        if (-not $ready) {
            $detail = if ([string]::IsNullOrEmpty($lastProbeError)) {
                'the readiness probe timed out'
            }
            else {
                $lastProbeError
            }
            throw "Test server did not become ready: $detail"
        }
    }
    finally {
        try {
            $probeClient.Dispose()
        }
        catch {
        }
    }

    Write-Output ("Running {0} connection-count case(s) against a {1} MiB file." -f `
        $connectionCounts.Count, $SizeMB)
    Write-Output 'Raw loopback HTTP throughput; the WPF download engine is not invoked.'
    Write-Output ''

    $results = [System.Collections.Generic.List[object]]::new()
    foreach ($count in $connectionCounts) {
        $serverProcess.Refresh()
        if ($serverProcess.HasExited) {
            throw ("Test server exited with code {0} during the benchmark." -f `
                $serverProcess.ExitCode)
        }

        Write-Output ("  connections={0}: running..." -f $count)
        $result = Invoke-RangeRun -Url $baseUrl -Length $fileLength -ConnectionCount $count
        $results.Add($result)
        Write-Output ("  connections={0}: {1} s, {2} MB/s" -f `
            $count,
            $result.Seconds.ToString('0.000', $invariant),
            $result.MegabytesPerSecond.ToString('0.00', $invariant))
    }

    $serverProcess.Refresh()
    if ($serverProcess.HasExited) {
        throw ("Test server exited with code {0} before the benchmark completed." -f `
            $serverProcess.ExitCode)
    }

    $timestamp = [DateTime]::UtcNow.ToString(
        'yyyy-MM-dd HH:mm:ss', [System.Globalization.CultureInfo]::InvariantCulture) + ' UTC'
    $markdown = [System.Collections.Generic.List[string]]::new()
    $markdown.Add('# HyperDL local range benchmark')
    $markdown.Add('')
    $markdown.Add("- Date: $timestamp")
    $markdown.Add("- Host PowerShell: $($PSVersionTable.PSVersion)")
    $markdown.Add("- OS: $([System.Environment]::OSVersion.VersionString)")
    $markdown.Add("- File size: $SizeMB MiB ($fileLength bytes)")
    $markdown.Add('- Transport: loopback HTTP/1.1 range GETs, .NET HttpClient, one equal segment per connection')
    $markdown.Add('- Scope: raw transport measurement only; HyperDL.exe and the WPF download engine are not invoked')
    $markdown.Add('')
    $markdown.Add('| Connections | Elapsed (s) | MB/s | Bytes |')
    $markdown.Add('| ---: | ---: | ---: | ---: |')
    foreach ($result in $results) {
        $markdown.Add(('| {0} | {1} | {2} | {3} |' -f `
            $result.Connections,
            $result.Seconds.ToString('0.000', $invariant),
            $result.MegabytesPerSecond.ToString('0.00', $invariant),
            $result.Bytes))
    }
    $markdown.Add('')
    $markdown.Add('MB/s uses 1 MB = 1,048,576 bytes. Single run per connection count; local loopback results are not end-user network throughput.')

    Write-Output ''
    foreach ($line in $markdown) {
        Write-Output $line
    }

    if (-not [string]::IsNullOrWhiteSpace($OutFile)) {
        if (-not [System.IO.Path]::IsPathRooted($OutFile)) {
            $OutFile = Join-Path $root $OutFile
        }
        $parent = Split-Path -Parent $OutFile
        if (-not [string]::IsNullOrEmpty($parent)) {
            [void][System.IO.Directory]::CreateDirectory($parent)
        }
        $content = ($markdown -join [Environment]::NewLine) + [Environment]::NewLine
        [System.IO.File]::WriteAllText(
            $OutFile, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Output ''
        Write-Output "Wrote markdown table: $OutFile"
    }
}
catch [System.Management.Automation.PipelineStoppedException] {
    # Ctrl+C stops the pipeline; the finally block still stops the server.
}
catch {
    $message = Get-InnermostMessage $_.Exception
    try {
        [Console]::Error.WriteLine("benchmark: $message")
        foreach ($logPath in @($serverOut, $serverErr)) {
            if (-not [string]::IsNullOrEmpty($logPath)) {
                $tail = Get-LogTail -Path $logPath
                if ($tail.Count -gt 0) {
                    [Console]::Error.WriteLine("--- $logPath (tail) ---")
                    foreach ($line in $tail) {
                        [Console]::Error.WriteLine($line)
                    }
                }
            }
        }
    }
    catch {
    }
    $exitCode = 1
}
finally {
    if ($null -ne $serverProcess) {
        try {
            $serverProcess.Refresh()
            if (-not $serverProcess.HasExited) {
                $serverProcess.Kill()
                [void]$serverProcess.WaitForExit(5000)
            }
        }
        catch {
        }
        try {
            $serverProcess.Dispose()
        }
        catch {
        }
    }

    if (-not [string]::IsNullOrEmpty($tempFile) -and
        (Test-Path -LiteralPath $tempFile)) {
        try {
            Remove-Item -LiteralPath $tempFile -Force -ErrorAction Stop
        }
        catch {
            try {
                [Console]::Error.WriteLine(
                    "benchmark: could not delete temp file: $tempFile")
            }
            catch {
            }
        }
    }

    foreach ($logPath in @($serverOut, $serverErr)) {
        if (-not [string]::IsNullOrEmpty($logPath) -and
            (Test-Path -LiteralPath $logPath)) {
            try {
                Remove-Item -LiteralPath $logPath -Force -ErrorAction Stop
            }
            catch {
            }
        }
    }
}

exit $exitCode
