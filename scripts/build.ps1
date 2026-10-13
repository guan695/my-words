param(
    [string]$DotnetPath,
    [switch]$SelfContained,
    [string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (-not $DotnetPath) {
    $taskDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $taskDotnet) { throw 'Install the .NET 8 SDK or specify -DotnetPath.' }
    $DotnetPath = $taskDotnet.Source
}
if (-not (Test-Path -LiteralPath $DotnetPath)) { throw 'The -DotnetPath executable does not exist.' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $taskRoot 'release' }
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$taskPackageName = if ($SelfContained) { 'MyWords-Windows-x64' } else { 'MyWords-Windows-x64-framework-dependent' }
$taskRelease = Join-Path $OutputRoot $taskPackageName
$taskSelfContained = if ($SelfContained) { 'true' } else { 'false' }
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& (Join-Path $PSScriptRoot 'make-assets.ps1')
& $DotnetPath publish (Join-Path $taskRoot 'src\MyWords\MyWords.csproj') -c Release --runtime win-x64 --self-contained $taskSelfContained --disable-build-servers --nologo --verbosity quiet --output $taskRelease -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Check that a .NET SDK is installed and the output app is closed.' }
# App data belongs in LocalAppData, never in a distributable build folder.
$taskPrivateFiles = Get-ChildItem -LiteralPath $taskRelease -Recurse -File | Where-Object { $_.Name -in @('words.json', 'settings.json') -or $_.Extension -in @('.bak', '.key', '.pfx') -or $_.Name -like '.env*' }
if ($taskPrivateFiles) { throw 'Possible private data found in the output directory. Choose a clean -OutputRoot.' }
if ($SelfContained) {
    # Preserve the licenses and notices shipped with the redistributed runtimes.
    $taskAssets = Get-Content -LiteralPath (Join-Path $taskRoot 'src\MyWords\obj\project.assets.json') -Raw | ConvertFrom-Json
    $taskRuntime = Get-Content -LiteralPath (Join-Path $taskRelease 'MyWords.runtimeconfig.json') -Raw | ConvertFrom-Json
    $taskNotices = Join-Path $taskRelease 'THIRD-PARTY-NOTICES'
    New-Item -ItemType Directory -Force -Path $taskNotices | Out-Null
    foreach ($taskFramework in $taskRuntime.runtimeOptions.includedFrameworks) {
        $taskPackageId = $taskFramework.name.ToLowerInvariant() + '.runtime.win-x64'
        $taskRuntimePackage = $taskAssets.packageFolders.PSObject.Properties.Name |
            ForEach-Object { Join-Path $_ ($taskPackageId + '/' + $taskFramework.version) } |
            Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $taskRuntimePackage) { throw ('Runtime license source not found: ' + $taskPackageId) }
        $taskLegalFiles = Get-ChildItem -LiteralPath $taskRuntimePackage -File |
            Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$' }
        if (-not $taskLegalFiles) { throw ('Runtime notices not found: ' + $taskPackageId) }
        foreach ($taskLegalFile in $taskLegalFiles) {
            Copy-Item -LiteralPath $taskLegalFile.FullName -Destination (Join-Path $taskNotices ($taskPackageId + '-' + $taskLegalFile.Name)) -Force
        }
    }
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskPlugin = Join-Path $taskRelease 'MyWords.mplugin'
$taskPluginStream = [System.IO.File]::Open($taskPlugin, [System.IO.FileMode]::Create)
$taskArchive = [System.IO.Compression.ZipArchive]::new($taskPluginStream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskFile in @('manggo.plugin.json', 'main.js', 'icon.png')) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, (Join-Path $taskRoot "manggo-plugin\$taskFile"), $taskFile) | Out-Null
    }
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, (Join-Path $taskRoot 'LICENSE'), 'LICENSE') | Out-Null
} finally { $taskArchive.Dispose() }
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\guide.md') -Destination (Join-Path $taskRelease 'USER-GUIDE.md') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination (Join-Path $taskRelease 'LICENSE') -Force
$taskZip = Join-Path $OutputRoot ($taskPackageName + '.zip')
Compress-Archive -Path (Join-Path $taskRelease '*') -DestinationPath $taskZip -Force
Copy-Item -LiteralPath $taskPlugin -Destination (Join-Path $OutputRoot 'MyWords.mplugin') -Force
Write-Output ('Package: ' + $taskZip)
