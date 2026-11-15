$ErrorActionPreference = 'Stop'

try {
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File -Recurse |
        ForEach-Object { $_.FullName })
    if ($sources.Count -eq 0) {
        throw 'No source files were found in src.'
    }

    Add-Type -Path $sources -ReferencedAssemblies @(
        'System.dll',
        'System.Core.dll',
        'System.Drawing.dll',
        'System.Windows.Forms.dll',
        'System.Web.Extensions.dll'
    )
    [WlanFt.Program]::Run()
}
catch {
    Write-Error $_
    exit 1
}
