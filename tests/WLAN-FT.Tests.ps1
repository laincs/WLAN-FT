$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -File -Recurse |
    ForEach-Object { $_.FullName })
$sources += Join-Path $PSScriptRoot 'NetworkTests.cs'

Add-Type -Path $sources -ReferencedAssemblies @(
    'System.dll', 'System.Core.dll', 'System.Drawing.dll',
    'System.Windows.Forms.dll', 'System.Web.Extensions.dll'
)
Add-Type -AssemblyName System.Web.Extensions

function Assert-True($condition, $message) {
    if (-not $condition) {
        throw $message
    }
}

$directory = Join-Path ([System.IO.Path]::GetTempPath()) ('wlan-ft-test-' + [guid]::NewGuid().ToString('N'))
$service = $null
$client = $null
$udp = $null

try {
    [void][System.IO.Directory]::CreateDirectory($directory)
    $emptyPath = Join-Path $directory 'empty.bin'
    $largePath = Join-Path $directory 'large.bin'
    $destination = Join-Path $directory 'download.bin'
    [System.IO.File]::WriteAllBytes($emptyPath, [byte[]]@())
    $expected = New-Object byte[] 1200001
    (New-Object Random 42).NextBytes($expected)
    [System.IO.File]::WriteAllBytes($largePath, $expected)

    $service = [WlanFt.NetworkTests]::CreateIsolatedService()
    Assert-True ([WlanFt.NetworkTests]::IsAllowed('127.0.0.1')) 'Loopback should be allowed.'
    Assert-True (-not [WlanFt.NetworkTests]::IsAllowed('8.8.8.8')) 'A public Internet address must not be allowed.'
    Assert-True (-not [WlanFt.NetworkTests]::IsAllowed('::1')) 'IPv6 must not be allowed.'

    try {
        [void][WlanFt.NetworkTests]::Request('8.8.8.8', 80)
        throw 'A request to an Internet address was allowed.'
    }
    catch [System.Management.Automation.MethodInvocationException] {
        Assert-True ($_.Exception.InnerException -is [System.InvalidOperationException]) 'Unexpected error when rejecting a public address.'
    }

    $request = [WlanFt.NetworkTests]::Request('127.0.0.1', $service.Port)
    Assert-True (-not $request.AllowAutoRedirect) 'HTTP redirects must not be followed.'
    Assert-True ($null -eq $request.Proxy) 'HTTP proxies must not be used.'

    $empty = $service.Add($emptyPath)
    $large = $service.Add($largePath)
    $base = 'http://127.0.0.1:' + $service.Port
    $client = New-Object System.Net.WebClient
    $client.Proxy = $null
    $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $catalog = $serializer.DeserializeObject($client.DownloadString($base + '/catalog'))
    Assert-True ($catalog.Files.Count -eq 2) 'The catalog should contain both files.'

    $emptyData = $client.DownloadData($base + '/file/' + $empty.Id)
    Assert-True ($emptyData.Length -eq 0) 'The empty file was not transferred correctly.'

    $actual = $client.DownloadData($base + '/file/' + $large.Id)
    Assert-True ([Convert]::ToBase64String($actual) -eq [Convert]::ToBase64String($expected)) 'The transferred bytes differ.'

    $remoteFile = New-Object WlanFt.RemoteFile
    $remoteFile.Address = [System.Net.IPAddress]::Loopback
    $remoteFile.Port = $service.Port
    $remoteFile.File = $large
    [System.IO.File]::WriteAllText($destination, 'old content')
    $progress = New-Object 'System.Collections.Generic.List[int]'
    $downloadError = [WlanFt.NetworkTests]::Download($remoteFile, $destination, $progress)
    Assert-True ([string]::IsNullOrEmpty($downloadError)) "Download failed: $downloadError"
    Assert-True ($progress[0] -eq 0 -and $progress[$progress.Count - 1] -eq 100) 'Download progress should start at 0 and end at 100.'
    Assert-True (@($progress | Where-Object { $_ -gt 0 -and $_ -lt 100 }).Count -gt 0) 'A large file should report intermediate progress.'
    for ($i = 1; $i -lt $progress.Count; $i++) {
        Assert-True ($progress[$i] -gt $progress[$i - 1]) 'Download progress should increase.'
    }
    $actual = [System.IO.File]::ReadAllBytes($destination)
    Assert-True ([Convert]::ToBase64String($actual) -eq [Convert]::ToBase64String($expected)) 'The downloaded file differs.'

    foreach ($immediately in @($true, $false)) {
        $progress.Clear()
        $result = [WlanFt.NetworkTests]::CancelDownload($remoteFile, $destination, $progress, $immediately)
        Assert-True ($result -eq 'Download cancelled: download.bin') "Unexpected cancellation result: $result"
        Assert-True (-not $progress.Contains(100)) 'A cancelled download must not report completion.'
        if (-not $immediately) {
            Assert-True (@($progress | Where-Object { $_ -gt 0 }).Count -gt 0) 'Cancellation should occur during the transfer.'
        }
        $actual = [System.IO.File]::ReadAllBytes($destination)
        Assert-True ([Convert]::ToBase64String($actual) -eq [Convert]::ToBase64String($expected)) 'Cancellation changed the destination.'
        Assert-True (@(Get-ChildItem -LiteralPath $directory -Filter '*.wlanft' -Force).Count -eq 0) 'Cancellation left a temporary file.'
    }

    $invalidFile = New-Object WlanFt.FileRecord
    $invalidFile.Id = $large.Id
    $invalidFile.Name = $large.Name
    $invalidFile.Size = $large.Size + 1
    $remoteFile.File = $invalidFile
    $progress.Clear()
    $downloadError = [WlanFt.NetworkTests]::Download($remoteFile, $destination, $progress)
    Assert-True (-not [string]::IsNullOrEmpty($downloadError)) 'A changed file should not download.'
    Assert-True (-not $progress.Contains(100)) 'A failed download must not report completion.'
    $actual = [System.IO.File]::ReadAllBytes($destination)
    Assert-True ([Convert]::ToBase64String($actual) -eq [Convert]::ToBase64String($expected)) 'A failed download changed the destination.'
    Assert-True (@(Get-ChildItem -LiteralPath $directory -Filter '*.wlanft' -Force).Count -eq 0) 'A failed download left a temporary file.'

    $remoteFile.File = $empty
    $progress.Clear()
    $downloadError = [WlanFt.NetworkTests]::Download($remoteFile, $destination, $progress)
    Assert-True ([string]::IsNullOrEmpty($downloadError)) "Empty file download failed: $downloadError"
    Assert-True ($progress.Count -eq 2 -and $progress[0] -eq 0 -and $progress[1] -eq 100) 'An empty download should report 0 then 100.'
    Assert-True ((Get-Item -LiteralPath $destination).Length -eq 0) 'The downloaded empty file should be empty.'

    [WlanFt.NetworkTests]::TestConcurrentDownloads($directory)
    Assert-True (@(Get-ChildItem -LiteralPath $directory -Filter '*.wlanft' -Force).Count -eq 0) 'Concurrent downloads left temporary files.'

    $udp = New-Object System.Net.Sockets.UdpClient
    $message = 'WLAN-FT-1|' + [guid]::NewGuid().ToString('N') + '|' + $service.Port
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($message)
    [void]$udp.Send($bytes, $bytes.Length, '127.0.0.1', [WlanFt.NetworkTests]::DiscoveryPort($service))
    $deadline = [DateTime]::UtcNow.AddSeconds(10)

    do {
        Start-Sleep -Milliseconds 100
        $remote = @($service.GetRemoteFiles())
    } while ($remote.Count -lt 2 -and [DateTime]::UtcNow -lt $deadline)

    Assert-True ($remote.Count -eq 2) 'Discovery did not load the announced catalog.'

    $service.Remove($large.Id)
    $catalog = $serializer.DeserializeObject($client.DownloadString($base + '/catalog'))
    Assert-True ($catalog.Files.Count -eq 1) 'The removed file is still in the catalog.'

    try {
        [void]$client.DownloadData($base + '/file/' + $large.Id)
        throw 'A removed file was still downloadable.'
    }
    catch [System.Net.WebException] {
        Assert-True ([int]$_.Exception.Response.StatusCode -eq 404) 'A removed file did not return HTTP 404.'
        $_.Exception.Response.Close()
    }

    Write-Host 'All WLAN-FT tests passed.'
}
finally {
    if ($udp) {
        $udp.Close()
    }
    if ($client) {
        $client.Dispose()
    }
    if ($service) {
        $service.Dispose()
    }

    if (Test-Path -LiteralPath $emptyPath) {
        Remove-Item -LiteralPath $emptyPath
    }
    if (Test-Path -LiteralPath $largePath) {
        Remove-Item -LiteralPath $largePath
    }
    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination
    }
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory
    }
}
