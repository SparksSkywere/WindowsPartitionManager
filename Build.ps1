#Requires -Version 5.1
# Builds Partition Manager and Windows Installer packages:
#   - dist\PartitionManager.msi
#   - dist\PartitionManager-Setup.exe
#
# Requires: Visual Studio C++ tools, CMake, and .NET SDK 8+ (WiX is pulled from NuGet).

[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$Arch = 'x64',
    [switch]$SkipPackage,
    # Wipe the entire build\ tree before configuring (full clean).
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$Root = [System.IO.Path]::GetFullPath((Split-Path -Parent $MyInvocation.MyCommand.Path))
Set-Location -LiteralPath $Root

function Find-VisualStudio {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { return $null }

    $json = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json 2>$null
    if (-not $json) {
        $json = & $vswhere -latest -products * -format json 2>$null
    }
    if (-not $json) { return $null }

    $inst = ($json | ConvertFrom-Json) | Select-Object -First 1
    if (-not $inst) { return $null }

    $path = [string]$inst.installationPath
    $devCmd = Join-Path $path 'Common7\Tools\VsDevCmd.bat'
    if (-not (Test-Path -LiteralPath $devCmd)) { return $null }

    $line = [string]$inst.catalog.productLineVersion
    if (-not $line) {
        if ($path -match '\\18\\') { $line = '18' }
        elseif ($path -match '\\2022\\') { $line = '17' }
        elseif ($path -match '\\2019\\') { $line = '16' }
        else { $line = '17' }
    }

    $generator = switch ($line) {
        '18' { 'Visual Studio 18 2026' }
        '17' { 'Visual Studio 17 2022' }
        '16' { 'Visual Studio 16 2019' }
        default { 'Visual Studio 17 2022' }
    }

    [pscustomobject]@{
        InstallPath = $path
        DevCmd      = $devCmd
        LineVersion = $line
        Generator   = $generator
        DisplayName = [string]$inst.displayName
    }
}

function Find-Cmake([string]$VsInstallPath) {
    if (Get-Command cmake -ErrorAction SilentlyContinue) {
        return (Get-Command cmake).Source
    }
    $candidates = @(
        (Join-Path $VsInstallPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'),
        "${env:ProgramFiles}\CMake\bin\cmake.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path -LiteralPath $c)) { return $c }
    }
    return $null
}

function Normalize-CmakePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return '' }
    return ($Path.Trim() -replace '/', '\').TrimEnd('\').ToLowerInvariant()
}

function Get-CmakeCacheValue {
    param(
        [Parameter(Mandatory)][string]$CacheFile,
        [Parameter(Mandatory)][string]$Name
    )
    if (-not (Test-Path -LiteralPath $CacheFile)) { return $null }
    foreach ($line in Get-Content -LiteralPath $CacheFile) {
        if ($line -match "^${Name}:(?:STRING|PATH|INTERNAL|FILEPATH|BOOL)=(.*)$") {
            return $Matches[1].Trim()
        }
    }
    return $null
}

# CMake embeds absolute source/build paths in CMakeCache.txt. Remapping the same
# folder as Q: vs Y: (or moving the tree) makes configure fail until the cache is cleared.
function Clear-CmakeBuildCache {
    param(
        [Parameter(Mandatory)][string]$BuildDir,
        [Parameter(Mandatory)][string]$SourceDir,
        [switch]$Force
    )

    if ($Force -and (Test-Path -LiteralPath $BuildDir)) {
        Write-Host "Cleaning build directory: $BuildDir" -ForegroundColor Yellow
        Remove-Item -LiteralPath $BuildDir -Recurse -Force
        return
    }

    $cache = Join-Path $BuildDir 'CMakeCache.txt'
    if (-not (Test-Path -LiteralPath $cache)) { return }

    $cachedSource = Get-CmakeCacheValue -CacheFile $cache -Name 'CMAKE_HOME_DIRECTORY'
    if (-not $cachedSource) {
        $cachedSource = Get-CmakeCacheValue -CacheFile $cache -Name 'PartitionManager_SOURCE_DIR'
    }
    $cachedBuild = Get-CmakeCacheValue -CacheFile $cache -Name 'CMAKE_CACHEFILE_DIR'
    if (-not $cachedBuild) {
        $cachedBuild = Get-CmakeCacheValue -CacheFile $cache -Name 'PartitionManager_BINARY_DIR'
    }

    $wantSource = Normalize-CmakePath $SourceDir
    $wantBuild = Normalize-CmakePath $BuildDir
    $haveSource = Normalize-CmakePath $cachedSource
    $haveBuild = Normalize-CmakePath $cachedBuild

    $stale = $false
    if ($haveSource -and $haveSource -ne $wantSource) { $stale = $true }
    if ($haveBuild -and $haveBuild -ne $wantBuild) { $stale = $true }

    if (-not $stale) { return }

    Write-Host 'CMake cache path mismatch — clearing build cache...' -ForegroundColor Yellow
    if ($cachedSource) { Write-Host "  cached source: $cachedSource" }
    Write-Host "  current source: $SourceDir"
    if ($cachedBuild) { Write-Host "  cached build:  $cachedBuild" }
    Write-Host "  current build:  $BuildDir"

    # Full wipe is safest with the Visual Studio generator after a drive/path change.
    Remove-Item -LiteralPath $BuildDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host '=== Partition Manager — native build + Windows Installer ===' -ForegroundColor Cyan
Write-Host "Root: $Root"
Write-Host 'Publisher: Skywere Industries'

$vs = Find-VisualStudio
if (-not $vs) { throw 'Visual Studio C++ tools were not found (need Desktop development with C++).' }

$cmakeExe = Find-Cmake -VsInstallPath $vs.InstallPath
if (-not $cmakeExe) { throw 'CMake was not found.' }
$env:Path = "$(Split-Path $cmakeExe);$env:Path"

$cmakeGen = $vs.Generator
Write-Host "Visual Studio: $($vs.DisplayName)"
Write-Host "  $($vs.InstallPath)"
Write-Host "CMake: $cmakeExe"
Write-Host "Generator: $cmakeGen"

$versionFile = Join-Path $Root 'VERSION'
$AppVersion = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw).Trim() } else { '1.0.0' }
Write-Host "App version: $AppVersion" -ForegroundColor Green
foreach ($wxs in @(
    (Join-Path $Root 'installer\wix\PartitionManager.Installer\Package.wxs'),
    (Join-Path $Root 'installer\wix\PartitionManager.Bundle\Bundle.wxs')
)) {
    if (-not (Test-Path $wxs)) { continue }
    $text = Get-Content $wxs -Raw
    $updated = [regex]::Replace($text, 'Version="\d+\.\d+\.\d+"', "Version=`"$AppVersion`"", 1)
    if ($updated -ne $text) {
        Set-Content -Path $wxs -Value $updated -Encoding UTF8 -NoNewline
        Write-Host "  Synced version in $(Split-Path $wxs -Leaf)" -ForegroundColor Green
    }
}

$build = Join-Path $Root 'build'
Clear-CmakeBuildCache -BuildDir $build -SourceDir $Root -Force:$Clean

# Also drop the cache when the generator or VS instance changed.
$cacheFile = Join-Path $build 'CMakeCache.txt'
if (Test-Path -LiteralPath $cacheFile) {
    $cachedGen = Get-CmakeCacheValue -CacheFile $cacheFile -Name 'CMAKE_GENERATOR'
    $cachedInst = Get-CmakeCacheValue -CacheFile $cacheFile -Name 'CMAKE_GENERATOR_INSTANCE'
    $wantInst = Normalize-CmakePath $vs.InstallPath
    $haveInst = Normalize-CmakePath $cachedInst
    $genChanged = $cachedGen -and ($cachedGen -ne $cmakeGen)
    $instChanged = $haveInst -and ($haveInst -ne $wantInst)
    if ($genChanged -or $instChanged) {
        Write-Host 'CMake generator/instance changed - clearing build cache...' -ForegroundColor Yellow
        Remove-Item -LiteralPath $build -Recurse -Force -ErrorAction SilentlyContinue
    }
}

New-Item -ItemType Directory -Force -Path $build | Out-Null
$archArg = if ($Arch -eq 'x86') { 'x86' } else { 'x64' }
$genArch = if ($Arch -eq 'x86') { 'Win32' } else { 'x64' }
$vsInstance = $vs.InstallPath.Replace('\', '/')

Write-Host "`n[1/3] Building PartitionManager.exe..." -ForegroundColor Yellow
# Pin CMAKE_GENERATOR_INSTANCE so CMake finds this exact VS install.
$vsCmd = @"
"$($vs.DevCmd)" -arch=$archArg -host_arch=x64 && cmake -S "$Root" -B "$build" -G "$cmakeGen" -A $genArch "-DCMAKE_GENERATOR_INSTANCE=$vsInstance" && cmake --build "$build" --config $Configuration
"@
cmd /c $vsCmd
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Visual Studio generator failed - retrying with Ninja...' -ForegroundColor Yellow
    Remove-Item -LiteralPath $build -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $build | Out-Null
    $ninja = Join-Path $vs.InstallPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
    $ninjaOpt = if (Test-Path -LiteralPath $ninja) { " `"-DCMAKE_MAKE_PROGRAM=$($ninja.Replace('\','/'))`"" } else { '' }
    $ninjaCmd = @"
"$($vs.DevCmd)" -arch=$archArg -host_arch=x64 && cmake -S "$Root" -B "$build" -G Ninja -DCMAKE_BUILD_TYPE=$Configuration$ninjaOpt && cmake --build "$build"
"@
    cmd /c $ninjaCmd
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
}

$exe = Join-Path $build "bin\$Configuration\PartitionManager.exe"
if (-not (Test-Path $exe)) { $exe = Join-Path $build 'bin\PartitionManager.exe' }
if (-not (Test-Path $exe)) { throw 'PartitionManager.exe was not produced.' }
Write-Host "  EXE: $exe"

if ($SkipPackage) {
    Write-Host 'SkipPackage set — dist installers were not built.'
    return
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK (dotnet) is required to build the WiX MSI and Setup.exe.'
}

$PayloadDir = Join-Path $Root 'artifacts\payload'
$DistDir = Join-Path $Root 'dist'
$IconSrc = Join-Path $Root 'assets\app.ico'
$MsiProj = Join-Path $Root 'installer\wix\PartitionManager.Installer\PartitionManager.Installer.wixproj'
$BundleProj = Join-Path $Root 'installer\wix\PartitionManager.Bundle\PartitionManager.Bundle.wixproj'

if (-not (Test-Path $IconSrc)) { throw "Icon missing: $IconSrc" }

if (Test-Path $PayloadDir) { Remove-Item $PayloadDir -Recurse -Force }
New-Item -ItemType Directory -Path $PayloadDir -Force | Out-Null
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
Copy-Item $exe (Join-Path $PayloadDir 'PartitionManager.exe') -Force
Copy-Item $IconSrc (Join-Path $PayloadDir 'app.ico') -Force

Write-Host ("  Payload: {0:N1} MB" -f ((Get-ChildItem $PayloadDir -File | Measure-Object Length -Sum).Sum / 1MB))

$NugetOrg = 'https://api.nuget.org/v3/index.json'
Write-Host "`n[2/3] Building MSI (Windows Installer)..." -ForegroundColor Yellow
foreach ($proj in @($MsiProj, $BundleProj)) {
    Write-Host "    restore $(Split-Path $proj -Leaf)"
    dotnet restore $proj --source $NugetOrg
    if ($LASTEXITCODE -ne 0) { throw "NuGet restore failed for $(Split-Path $proj -Leaf)." }
}

Get-ChildItem (Join-Path $Root 'installer\wix\PartitionManager.Installer\bin') -Recurse -Filter '*.msi' -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

dotnet build $MsiProj -c $Configuration -p:PayloadDir="$PayloadDir" --no-incremental
if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }

$msi = Get-ChildItem (Join-Path $Root 'installer\wix\PartitionManager.Installer') -Recurse -Filter 'PartitionManager.msi' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $msi) { throw 'PartitionManager.msi was not produced.' }
Copy-Item $msi.FullName -Destination (Join-Path $DistDir 'PartitionManager.msi') -Force
Write-Host ("  MSI: {0:N1} MB -> dist\PartitionManager.msi" -f ($msi.Length / 1MB))

Write-Host "`n[3/3] Building Setup.exe (WiX Burn bootstrapper)..." -ForegroundColor Yellow
Get-ChildItem (Join-Path $Root 'installer\wix\PartitionManager.Bundle\bin') -Recurse -Filter '*.exe' -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

dotnet build $BundleProj -c $Configuration -p:PayloadDir="$PayloadDir" --no-incremental
if ($LASTEXITCODE -ne 0) { throw 'Bundle build failed.' }

$setup = Get-ChildItem (Join-Path $Root 'installer\wix\PartitionManager.Bundle') -Recurse -Filter 'PartitionManager-Setup.exe' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $setup) { throw 'PartitionManager-Setup.exe was not produced.' }
Copy-Item $setup.FullName -Destination (Join-Path $DistDir 'PartitionManager-Setup.exe') -Force

Write-Host ''
Write-Host '=== Build succeeded ===' -ForegroundColor Green
Write-Host "  dist\PartitionManager.msi"
Write-Host "  dist\PartitionManager-Setup.exe"
Write-Host ''
Write-Host 'Install (MSI wizard with folder Browse):'
Write-Host '  msiexec /i .\dist\PartitionManager.msi'
Write-Host ''
Write-Host 'Install (Setup.exe):'
Write-Host '  .\dist\PartitionManager-Setup.exe'
Write-Host ''
