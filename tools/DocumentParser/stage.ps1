param(
    [Parameter(Mandatory = $true)][string]$Destination
)
$ErrorActionPreference = "Stop"
$nodeVersion = "24.14.0"
$archiveName = "node-v$nodeVersion-win-x64.zip"
$baseUrl = "https://nodejs.org/dist/v$nodeVersion"
$cache = Join-Path $PSScriptRoot "..\..\artifacts\document-parser-build"
[IO.Directory]::CreateDirectory($cache) | Out-Null
$archive = Join-Path $cache $archiveName
$checksums = (Invoke-WebRequest "$baseUrl/SHASUMS256.txt" -UseBasicParsing).Content
$line = ($checksums -split "`n" | Where-Object { $_ -match ([regex]::Escape($archiveName) + '$') })
if (@($line).Count -ne 1) { throw "Node archive checksum not found." }
$expected = ($line.Trim() -split '\s+')[0]
if (-not (Test-Path $archive)) { Invoke-WebRequest "$baseUrl/$archiveName" -OutFile $archive -UseBasicParsing }
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected) { throw "Node archive checksum mismatch." }
$nodeRoot = Join-Path $cache "node-v$nodeVersion-win-x64"
if (-not (Test-Path (Join-Path $nodeRoot "node.exe"))) {
    Expand-Archive -LiteralPath $archive -DestinationPath $cache
}
Push-Location $PSScriptRoot
try {
    & npm.cmd ci --ignore-scripts --omit=optional
    if ($LASTEXITCODE -ne 0) { throw "Document parser dependency install failed." }
}
finally { Pop-Location }
$target = [IO.Path]::GetFullPath($Destination)
[IO.Directory]::CreateDirectory($target) | Out-Null
Copy-Item (Join-Path $nodeRoot "node.exe") (Join-Path $target "node.exe") -Force
Copy-Item (Join-Path $nodeRoot "LICENSE") (Join-Path $target "node-LICENSE") -Force
Copy-Item (Join-Path $PSScriptRoot "package.json") (Join-Path $target "package.json") -Force
Copy-Item (Join-Path $PSScriptRoot "node_modules") $target -Recurse -Force
$env:KORDOC_OFFLINE = "1"
& (Join-Path $target "node.exe") (Join-Path $target "node_modules\kordoc\dist\cli.js") --version
if ($LASTEXITCODE -ne 0) { throw "Bundled document parser cannot start." }
Write-Output "DOCUMENT_PARSER_STAGED $target"
