# Purpose: Serve a generated or supplied file over loopback HTTP with controllable
#          Range, throttling, delay, rate-limit, and failure behaviours.
# Requirements: Windows PowerShell 5.1 or PowerShell 7 on Windows. No external packages.
# Usage: powershell -ExecutionPolicy Bypass -File .\scripts\test-server.ps1 -Port 18080 -SizeMB 256
# Output: Console request log. Press Ctrl+C to stop; generated temp files are removed.
[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 18080,

    [ValidateRange(1, 262144)]
    [int]$SizeMB = 256,

    [string]$FilePath = '',

    [ValidateRange(1, 512)]
    [int]$MaxConcurrency = 96
)

$ErrorActionPreference = 'Stop'

$listener = $null
$pool = $null
$pending = $null
$generatedFile = $null

try {
    if ($PSVersionTable.PSEdition -ne 'Core') {
        # HttpListener lives in System.Net. Explicit registration keeps Windows
        # PowerShell 5.1 reliable on machines with a minimal assembly load set.
        Add-Type -AssemblyName System.Net -ErrorAction Stop
    }

    if ([string]::IsNullOrWhiteSpace($FilePath)) {
        $fileLength = [long]$SizeMB * 1MB
        $generatedFile = Join-Path ([System.IO.Path]::GetTempPath()) (
            'hyperdl-test-server-{0}.bin' -f [Guid]::NewGuid().ToString('N'))
        $FilePath = $generatedFile

        Write-Output ("Generating {0} MiB test file: {1}" -f $SizeMB, $FilePath)
        $buffer = [byte[]]::new(1MB)
        ([System.Random]::new(20261026)).NextBytes($buffer)
        $stream = [System.IO.FileStream]::new(
            $FilePath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None,
            1MB,
            [System.IO.FileOptions]::SequentialScan)
        try {
            $remaining = $fileLength
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
    else {
        $resolved = Resolve-Path -LiteralPath $FilePath -ErrorAction Stop
        $FilePath = $resolved.Path
        if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
            throw "File was not found: $FilePath"
        }
        $fileLength = (Get-Item -LiteralPath $FilePath).Length
        if ($fileLength -le 0) {
            throw "The test file must not be empty: $FilePath"
        }
    }

    $state = [hashtable]::Synchronized(@{
        RateCounts  = @{}
        EtagCounter = 0
    })

    # The handler runs inside a runspace pool. Keep it self-contained: runspaces
    # do not share the caller's functions or local variables.
    $handler = {
        param($Context, $HandlerFilePath, $HandlerFileLength, $HandlerState)

        function Get-QueryFlag {
            param([hashtable]$Values, [string]$Name)
            if (-not $Values.ContainsKey($Name)) {
                return $false
            }
            $value = [string]$Values[$Name]
            return -not ($value -eq '0' -or $value -ieq 'false')
        }

        function Send-TextResponse {
            param(
                [System.Net.HttpListenerResponse]$Response,
                [int]$StatusCode,
                [string]$Text,
                [bool]$IsHead,
                [hashtable]$ExtraHeaders
            )
            $body = [System.Text.Encoding]::UTF8.GetBytes($Text)
            $Response.StatusCode = $StatusCode
            $Response.ContentType = 'text/plain; charset=utf-8'
            $Response.ContentLength64 = $body.Length
            if ($null -ne $ExtraHeaders) {
                foreach ($key in $ExtraHeaders.Keys) {
                    $Response.Headers[[string]$key] = [string]$ExtraHeaders[$key]
                }
            }
            if (-not $IsHead) {
                $Response.OutputStream.Write($body, 0, $body.Length)
            }
            if ($IsHead) {
                return [long]0
            }
            return [long]$body.Length
        }

        function Write-RequestLog {
            param(
                [string]$Method,
                [string]$Target,
                [string]$RangeHeader,
                [int]$StatusCode,
                [long]$BytesSent,
                [long]$Milliseconds,
                [string]$Note
            )
            $rangeText = if ([string]::IsNullOrWhiteSpace($RangeHeader)) { '-' } else { $RangeHeader }
            $noteText = if ([string]::IsNullOrWhiteSpace($Note)) { '' } else { ' [' + $Note + ']' }
            $line = '[{0:HH:mm:ss.fff}] {1,-4} {2} Range={3} -> {4} {5} B ({6} ms){7}' -f `
                [DateTime]::Now, $Method, $Target, $rangeText, $StatusCode, $BytesSent,
                $Milliseconds, $noteText
            try {
                [Console]::WriteLine($line)
            }
            catch {
                # Logging must never take down a request.
            }
        }

        $request = $Context.Request
        $response = $Context.Response
        $method = [string]$request.HttpMethod
        $isHead = $method -ieq 'HEAD'
        $target = if ($null -ne $request.Url) { $request.Url.PathAndQuery } else { '/' }
        $rangeHeader = [string]$request.Headers['Range']
        $statusCode = 500
        $bytesSent = [long]0
        $bodyStarted = $false
        $aborted = $false
        $note = ''
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

        try {
            # --- Path selection ---------------------------------------------------
            if ($null -eq $request.Url -or
                -not [string]::Equals(
                    $request.Url.AbsolutePath,
                    '/file.bin',
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                $statusCode = 404
                $bodyStarted = $true
                $bytesSent = Send-TextResponse $response 404 (
                    "Not found. This test server only serves /file.bin.`n") $isHead $null
                return
            }

            # --- Query parameters -------------------------------------------------
            $parameters = @{}
            if ($null -ne $request.Url -and $request.Url.Query.Length -gt 1) {
                foreach ($pair in $request.Url.Query.Substring(1).Split('&')) {
                    if ([string]::IsNullOrEmpty($pair)) {
                        continue
                    }
                    $separator = $pair.IndexOf('=')
                    if ($separator -lt 0) {
                        $key = $pair
                        $value = ''
                    }
                    else {
                        $key = $pair.Substring(0, $separator)
                        $value = $pair.Substring($separator + 1)
                    }
                    $key = [Uri]::UnescapeDataString($key).ToLowerInvariant()
                    $parameters[$key] = [Uri]::UnescapeDataString($value)
                }
            }

            $delayMs = 0
            if ($parameters.ContainsKey('delayms')) {
                if (-not [int]::TryParse($parameters['delayms'], [ref]$delayMs) -or $delayMs -lt 0) {
                    $statusCode = 400
                    $bodyStarted = $true
                    $bytesSent = Send-TextResponse $response 400 "delayms must be a non-negative integer.`n" $isHead $null
                    return
                }
            }

            $throttleKbps = 0
            if ($parameters.ContainsKey('throttle')) {
                if (-not [int]::TryParse($parameters['throttle'], [ref]$throttleKbps) -or $throttleKbps -lt 0) {
                    $statusCode = 400
                    $bodyStarted = $true
                    $bytesSent = Send-TextResponse $response 400 "throttle must be a non-negative integer (KB/s).`n" $isHead $null
                    return
                }
            }

            $failAfter = [long]-1
            if ($parameters.ContainsKey('failafter')) {
                if (-not [long]::TryParse($parameters['failafter'], [ref]$failAfter) -or $failAfter -lt 0) {
                    $statusCode = 400
                    $bodyStarted = $true
                    $bytesSent = Send-TextResponse $response 400 "failafter must be a non-negative integer.`n" $isHead $null
                    return
                }
            }

            $noRange = Get-QueryFlag $parameters 'norange'
            $badLength = Get-QueryFlag $parameters 'badlength'
            $changeful = Get-QueryFlag $parameters 'changeful'
            $rateLimit = Get-QueryFlag $parameters 'ratelimit'

            # --- Simulated server delay ------------------------------------------
            if ($delayMs -gt 0) {
                [System.Threading.Thread]::Sleep($delayMs)
            }

            # --- Forced status ----------------------------------------------------
            if ($parameters.ContainsKey('status')) {
                $statusText = [string]$parameters['status']
                if ($statusText -ne '403' -and $statusText -ne '404' -and $statusText -ne '500') {
                    $statusCode = 400
                    $bodyStarted = $true
                    $bytesSent = Send-TextResponse $response 400 "status must be 403, 404, or 500.`n" $isHead $null
                    return
                }

                $forcedStatus = [int]$statusText
                $statusCode = $forcedStatus
                $bodyStarted = $true
                $bytesSent = Send-TextResponse $response $forcedStatus (
                    "Simulated HTTP $forcedStatus response.`n") $isHead $null
                return
            }

            # --- Per-client rate limiting ----------------------------------------
            if ($rateLimit) {
                $clientKey = 'unknown'
                if ($null -ne $request.RemoteEndPoint) {
                    $clientKey = [string]$request.RemoteEndPoint.Address
                }
                $requestNumber = 0
                [System.Threading.Monitor]::Enter($HandlerState.SyncRoot)
                try {
                    if ($HandlerState.RateCounts.ContainsKey($clientKey)) {
                        $requestNumber = [int]$HandlerState.RateCounts[$clientKey]
                    }
                    $requestNumber++
                    $HandlerState.RateCounts[$clientKey] = $requestNumber
                }
                finally {
                    [System.Threading.Monitor]::Exit($HandlerState.SyncRoot)
                }

                if ($requestNumber -le 2) {
                    $statusCode = 429
                    $bodyStarted = $true
                    $note = "rate limited request $requestNumber"
                    $bytesSent = Send-TextResponse $response 429 (
                        "Too Many Requests. Retry after 2 seconds.`n") $isHead @{ 'Retry-After' = '2' }
                    return
                }
            }

            # --- Range selection --------------------------------------------------
            $range = $null
            if (-not $noRange -and -not [string]::IsNullOrWhiteSpace($rangeHeader)) {
                if ($rangeHeader -match '^bytes\s*=\s*(\d*)\s*-\s*(\d*)\s*$') {
                    $startText = $Matches[1]
                    $endText = $Matches[2]
                    if ([string]::IsNullOrEmpty($startText) -and [string]::IsNullOrEmpty($endText)) {
                        $range = $null
                    }
                    elseif ([string]::IsNullOrEmpty($startText)) {
                        $suffixLength = [long]0
                        if (-not [long]::TryParse($endText, [ref]$suffixLength) -or $suffixLength -le 0) {
                            $range = @{ Invalid = $true }
                        }
                        else {
                            $count = [Math]::Min($suffixLength, $HandlerFileLength)
                            $range = @{
                                Invalid = $false
                                Start   = [long]($HandlerFileLength - $count)
                                End     = [long]($HandlerFileLength - 1)
                            }
                        }
                    }
                    else {
                        $start = [long]0
                        if (-not [long]::TryParse($startText, [ref]$start) -or
                            $start -ge $HandlerFileLength) {
                            $range = @{ Invalid = $true }
                        }
                        else {
                            $end = [long]($HandlerFileLength - 1)
                            if (-not [string]::IsNullOrEmpty($endText)) {
                                if (-not [long]::TryParse($endText, [ref]$end) -or $end -lt $start) {
                                    $range = @{ Invalid = $true }
                                }
                                elseif ($end -ge $HandlerFileLength) {
                                    $end = [long]($HandlerFileLength - 1)
                                }
                            }
                            if ($null -eq $range) {
                                $range = @{ Invalid = $false; Start = $start; End = $end }
                            }
                        }
                    }
                }
            }

            if ($null -ne $range -and $range.Invalid) {
                $response.Headers['Accept-Ranges'] = 'bytes'
                $response.Headers['Content-Range'] = 'bytes */' + $HandlerFileLength
                $statusCode = 416
                $bodyStarted = $true
                $bytesSent = Send-TextResponse $response 416 "Requested range is not satisfiable.`n" $isHead $null
                return
            }

            $isRange = $null -ne $range
            $start = [long]0
            $end = [long]($HandlerFileLength - 1)
            if ($isRange) {
                $start = [long]$range.Start
                $end = [long]$range.End
            }
            $bodyLength = [long]($end - $start + 1)

            $lastWrite = [System.IO.File]::GetLastWriteTimeUtc($HandlerFilePath)
            $etag = '"hyperdl-{0}-{1}"' -f $HandlerFileLength, $lastWrite.Ticks
            if ($changeful) {
                $counter = 0
                [System.Threading.Monitor]::Enter($HandlerState.SyncRoot)
                try {
                    $HandlerState.EtagCounter = [int]$HandlerState.EtagCounter + 1
                    $counter = [int]$HandlerState.EtagCounter
                }
                finally {
                    [System.Threading.Monitor]::Exit($HandlerState.SyncRoot)
                }
                $etag = if (($counter % 2) -eq 1) {
                    '"hyperdl-change-a"'
                }
                else {
                    '"hyperdl-change-b"'
                }
                $response.Headers['Cache-Control'] = 'no-store'
            }

            if ($isRange) {
                $statusCode = 206
                $response.StatusCode = 206
                $response.Headers['Content-Range'] = 'bytes {0}-{1}/{2}' -f `
                    $start, $end, $HandlerFileLength
            }
            else {
                $statusCode = 200
                $response.StatusCode = 200
            }

            if (-not $noRange) {
                $response.Headers['Accept-Ranges'] = 'bytes'
            }
            $response.Headers['ETag'] = $etag
            $response.Headers['Last-Modified'] = $lastWrite.ToString(
                'R', [System.Globalization.CultureInfo]::InvariantCulture)
            $response.Headers['Content-Disposition'] = 'attachment; filename="file.bin"'
            $response.ContentType = 'application/octet-stream'

            $declaredLength = $bodyLength
            if ($badLength) {
                $declaredLength = $bodyLength + 1
                $response.KeepAlive = $false
                $note = 'Content-Length is one byte too large'
            }
            $response.ContentLength64 = $declaredLength

            if ($isHead) {
                return
            }

            $truncateAt = [long]-1
            if ($isRange -and $failAfter -ge 0 -and $failAfter -lt $bodyLength) {
                $truncateAt = $failAfter
                $response.KeepAlive = $false
                $note = "disconnect after $failAfter bytes"
            }

            $limit = if ($truncateAt -ge 0) { $truncateAt } else { $bodyLength }
            if ($limit -eq 0 -and $truncateAt -ge 0) {
                $aborted = $true
                try {
                    $response.OutputStream.Flush()
                    $response.Abort()
                }
                catch {
                }
                return
            }

            $chunkSize = 65536
            $bytesPerSecond = 0.0
            if ($throttleKbps -gt 0) {
                $bytesPerSecond = [double]$throttleKbps * 1024.0
                $chunkSize = [int][Math]::Max(
                    1024,
                    [Math]::Min(262144, [Math]::Floor($bytesPerSecond / 16.0)))
            }

            $fileStream = [System.IO.FileStream]::new(
                $HandlerFilePath,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Read,
                [System.IO.FileShare]::ReadWrite,
                $chunkSize,
                [System.IO.FileOptions]::SequentialScan)
            try {
                $fileStream.Seek($start, [System.IO.SeekOrigin]::Begin) | Out-Null
                $buffer = [byte[]]::new($chunkSize)
                $written = [long]0
                $pace = if ($throttleKbps -gt 0) {
                    [System.Diagnostics.Stopwatch]::StartNew()
                }
                else {
                    $null
                }

                while ($written -lt $limit) {
                    $wanted = [int][Math]::Min([long]$buffer.Length, $limit - $written)
                    $read = $fileStream.Read($buffer, 0, $wanted)
                    if ($read -le 0) {
                        throw 'Unexpected end of test file while serving the response.'
                    }
                    $bodyStarted = $true
                    $response.OutputStream.Write($buffer, 0, $read)
                    $written += $read
                    $bytesSent += $read

                    if ($throttleKbps -gt 0) {
                        $targetSeconds = [double]$written / $bytesPerSecond
                        $deficit = $targetSeconds - $pace.Elapsed.TotalSeconds
                        if ($deficit -gt 0.001) {
                            [System.Threading.Thread]::Sleep(
                                [int][Math]::Round($deficit * 1000.0))
                        }
                    }
                }

                $response.OutputStream.Flush()

                if ($truncateAt -ge 0) {
                    $aborted = $true
                    try {
                        $response.Abort()
                    }
                    catch {
                    }
                    return
                }

                if ($badLength) {
                    # Keep the advertised length larger than the bytes actually
                    # written, then drop the connection instead of hanging.
                    $aborted = $true
                    try {
                        $response.Abort()
                    }
                    catch {
                    }
                }
            }
            finally {
                $fileStream.Dispose()
            }
        }
        catch {
            $note = 'error: ' + $_.Exception.Message
            if (-not $bodyStarted -and -not $aborted) {
                try {
                    $statusCode = 500
                    $bodyStarted = $true
                    $bytesSent = Send-TextResponse $response 500 (
                        "Internal test server error: $($_.Exception.Message)`n") $isHead $null
                }
                catch {
                }
            }
        }
        finally {
            if (-not $aborted) {
                try {
                    $response.Close()
                }
                catch {
                    if ([string]::IsNullOrEmpty($note)) {
                        $note = 'close error: ' + $_.Exception.Message
                    }
                }
            }

            $stopwatch.Stop()
            Write-RequestLog $method $target $rangeHeader $statusCode $bytesSent `
                ([long]$stopwatch.ElapsedMilliseconds) $note
        }
    }

    $handlerText = $handler.ToString()

    $listener = [System.Net.HttpListener]::new()
    $listener.Prefixes.Add("http://127.0.0.1:$Port/")
    $listener.Start()

    $pool = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspacePool(
        1, $MaxConcurrency)
    $pool.Open()
    $pending = [System.Collections.ArrayList]::new()

    $url = "http://127.0.0.1:$Port/file.bin"
    Write-Output ''
    Write-Output 'HyperDL HTTP range test server'
    Write-Output "  URL:        $url"
    Write-Output "  File:       $FilePath"
    Write-Output ("  Size:       {0:N1} MiB ({1} bytes)" -f ($fileLength / 1MB), $fileLength)
    Write-Output "  Concurrency: up to $MaxConcurrency request runspaces"
    Write-Output '  Endpoints:  /file.bin plus query flags; see scripts\README.md'
    Write-Output '  Press Ctrl+C to stop.'
    Write-Output ''

    function Complete-FinishedRequest {
        param([System.Collections.ArrayList]$Pending)
        for ($index = $Pending.Count - 1; $index -ge 0; $index--) {
            $item = $Pending[$index]
            if (-not $item.Async.IsCompleted) {
                continue
            }

            try {
                $null = $item.PowerShell.EndInvoke($item.Async)
            }
            catch {
                try {
                    [Console]::Error.WriteLine(
                        '[test-server] request handler failed: ' + $_.Exception.Message)
                }
                catch {
                }
            }

            if ($item.PowerShell.Streams.Error.Count -gt 0) {
                foreach ($errorRecord in $item.PowerShell.Streams.Error) {
                    try {
                        [Console]::Error.WriteLine(
                            '[test-server] handler: ' + $errorRecord.ToString())
                    }
                    catch {
                    }
                }
            }

            try {
                $item.PowerShell.Dispose()
            }
            catch {
            }
            $Pending.RemoveAt($index)
        }
    }

    $contextTask = $listener.GetContextAsync()
    while ($true) {
        $completed = $false
        try {
            $completed = $contextTask.Wait(200)
        }
        catch {
            if ($listener.IsListening) {
                throw
            }
            break
        }

        Complete-FinishedRequest -Pending $pending

        if (-not $completed) {
            continue
        }

        try {
            $context = $contextTask.GetAwaiter().GetResult()
        }
        catch {
            if ($listener.IsListening) {
                throw
            }
            break
        }
        $contextTask = $listener.GetContextAsync()

        $powerShell = [PowerShell]::Create()
        $null = $powerShell.AddScript($handlerText)
        $null = $powerShell.AddArgument($context)
        $null = $powerShell.AddArgument($FilePath)
        $null = $powerShell.AddArgument([long]$fileLength)
        $null = $powerShell.AddArgument($state)

        try {
            $async = $powerShell.BeginInvoke()
            [void]$pending.Add([pscustomobject]@{
                    PowerShell = $powerShell
                    Async      = $async
                })
        }
        catch {
            try {
                [Console]::Error.WriteLine(
                    '[test-server] could not schedule request: ' + $_.Exception.Message)
                $context.Response.StatusCode = 503
                $context.Response.Close()
            }
            catch {
            }
            try {
                $powerShell.Dispose()
            }
            catch {
            }
        }
    }
}
catch [System.Management.Automation.PipelineStoppedException] {
    # Ctrl+C stops the pipeline; cleanup runs in finally.
}
catch {
    try {
        [Console]::Error.WriteLine('[test-server] ' + $_.Exception.Message)
    }
    catch {
    }
    exit 1
}
finally {
    if ($null -ne $listener) {
        try {
            $listener.Stop()
        }
        catch {
        }
        try {
            $listener.Close()
        }
        catch {
        }
    }

    if ($null -ne $pending) {
        foreach ($item in @($pending)) {
            try {
                $item.PowerShell.Stop()
            }
            catch {
            }
            try {
                $item.PowerShell.Dispose()
            }
            catch {
            }
        }
        $pending.Clear()
    }

    if ($null -ne $pool) {
        try {
            $pool.Close()
        }
        catch {
        }
        try {
            $pool.Dispose()
        }
        catch {
        }
    }

    if ($null -ne $generatedFile -and (Test-Path -LiteralPath $generatedFile)) {
        try {
            Remove-Item -LiteralPath $generatedFile -Force -ErrorAction Stop
        }
        catch {
            try {
                [Console]::Error.WriteLine(
                    '[test-server] could not remove temp file: ' + $generatedFile)
            }
            catch {
            }
        }
    }

    try {
        [Console]::WriteLine('Test server stopped.')
    }
    catch {
    }
}
