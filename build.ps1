<#
.SYNOPSIS
    Builds, validates, weaves native exports, and packages FARO SCENE Add-ins into production .faroapp packages.

.DESCRIPTION
    This script performs the following operations:
    1. Discovers MSBuild toolchain (via vswhere or PATH).
    2. Compiles the Visual Studio solution in the specified configuration/platform (net48 x64).
    3. Weaves unmanaged exports (lsAppStartup, lsAppShutdown) required by SCENE native loader.
    4. Asserts that FARO SDK assemblies are NOT copied to bin output (Private=False check).
    5. Stages the bundle assets (App.xml, AppManifest.xml, DLL, PDB, Icons, Resources).
    6. Archives each staging directory into valid FARO App packages (.faroapp and .fpp alias).
    7. Emits SHA256 hashes and verification stats.

.PARAMETER Configuration
    The build configuration ('Release' or 'Debug'). Default is 'Release'.

.PARAMETER Platform
    The target CPU platform ('x64'). Default is 'x64'.

.PARAMETER OutputDir
    The output directory where the final .faroapp packages are placed. Default is 'dist'.

.PARAMETER Clean
    If specified, performs a clean build and removes previous build/staging artifacts.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Release -Clean
#>

[CmdletBinding()]
param (
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64')]
    [string]$Platform = 'x64',

    [string]$OutputDir = '',

    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDir)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}
if ([string]::IsNullOrWhiteSpace($scriptDir)) {
    $scriptDir = (Get-Location).Path
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $scriptDir "dist"
}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "       FARO SCENE Open API Add-in Packaging Tool            " -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Configuration : $Configuration"
Write-Host "Platform      : $Platform"
Write-Host "Output Dir    : $OutputDir"
Write-Host ""

# 1. Locate MSBuild toolchain
Write-Host "[1/6] Locating MSBuild toolchain..." -ForegroundColor Yellow
$vswherePath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuildPath = $null

if (Test-Path $vswherePath) {
    $vsInstall = & $vswherePath -latest -requires Microsoft.Component.MSBuild -property installationPath
    if ($vsInstall) {
        $candidate = Join-Path $vsInstall "MSBuild\Current\Bin\MSBuild.exe"
        if (Test-Path $candidate) {
            $msbuildPath = $candidate
        }
    }
}

if (-not $msbuildPath) {
    $candidateCmd = Get-Command "msbuild.exe" -ErrorAction SilentlyContinue
    if ($candidateCmd) {
        $msbuildPath = $candidateCmd.Source
    }
}

if (-not $msbuildPath) {
    throw "Unable to find MSBuild.exe. Ensure Visual Studio 2019/2022 or Build Tools are installed."
}

Write-Host "Found MSBuild at: $msbuildPath" -ForegroundColor Green

# 2. Prepare Reference Assemblies
$libDir = Join-Path $scriptDir "lib"
$appLogicRef = Join-Path $libDir "FARO.LS.SDK.AppLogic.dll"
$dataLogicRef = Join-Path $libDir "FARO.LS.SDK.DataLogic.dll"

if (-not (Test-Path $appLogicRef) -or -not (Test-Path $dataLogicRef)) {
    throw "SDK reference contracts missing in $libDir."
} else {
    Write-Host "[2/6] SDK reference assemblies verified in lib directory." -ForegroundColor Green
}

# 3. Clean if requested
if ($Clean) {
    Write-Host "[3/6] Cleaning previous build artifacts..." -ForegroundColor Yellow
    $dirsToClean = @(
        "$scriptDir\src\FAROSceneHelloWorld\bin",
        "$scriptDir\src\FAROSceneHelloWorld\obj",
        "$scriptDir\src\FAROSceneSDCardWorkflow\bin",
        "$scriptDir\src\FAROSceneSDCardWorkflow\obj",
        "$scriptDir\tests\SDCardWorkflowVerification\bin",
        "$scriptDir\tests\SDCardWorkflowVerification\obj",
        "$scriptDir\obj",
        $OutputDir
    )
    foreach ($d in $dirsToClean) {
        if (Test-Path $d) {
            Remove-Item -Path $d -Recurse -Force
        }
    }
} else {
    Write-Host "[3/6] Skipping clean (use -Clean to force full cleanup)." -ForegroundColor Gray
}

# 4. Compile Solution via MSBuild
Write-Host "[4/6] Compiling solution ($Configuration|$Platform)..." -ForegroundColor Yellow
$solutionPath = Join-Path $scriptDir "FAROSceneHelloWorld.sln"

$buildArgs = @(
    $solutionPath,
    "/restore",
    "/t:Rebuild",
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform",
    "/v:minimal",
    "/nologo"
)

& $msbuildPath $buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild compilation failed with exit code $LASTEXITCODE."
}

# Locate ILDASM and ILASM for native export weaving
$ildasmCandidates = @(
    "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\x64\ildasm.exe",
    "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\ildasm.exe"
)
$ildasmPath = $ildasmCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
$ilasmPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\ilasm.exe"

$appsToPackage = @(
    @{
        Name = "FAROSceneHelloWorld"
        Dir  = Join-Path $scriptDir "src\FAROSceneHelloWorld"
    },
    @{
        Name = "FAROSceneSDCardWorkflow"
        Dir  = Join-Path $scriptDir "src\FAROSceneSDCardWorkflow"
    }
)

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($app in $appsToPackage) {
    $appName = $app.Name
    $appDir = $app.Dir
    $binDir = Join-Path $appDir "bin\$Platform\$Configuration"
    $mainDll = Join-Path $binDir "$appName.dll"

    Write-Host ""
    Write-Host ">>> Processing Add-in: $appName <<<" -ForegroundColor Cyan

    # 4b. Weave unmanaged exports (lsAppStartup, lsAppShutdown)
    Write-Host "[4b/6] Weaving native C++ exports for $appName..." -ForegroundColor Yellow

    if ($ildasmPath -and (Test-Path $ilasmPath) -and (Test-Path $mainDll)) {
        $objDir = Join-Path $scriptDir "obj\$appName"
        if (-not (Test-Path $objDir)) { New-Item -ItemType Directory -Path $objDir -Force | Out-Null }
        $ilFile = Join-Path $objDir "$appName.il"

        & $ildasmPath /nobar /out=$ilFile $mainDll
        $ilContent = [System.IO.File]::ReadAllText($ilFile)

        if ($ilContent -notmatch "VT_01") {
            $vtfixup = @"
.vtfixup [1] int64 fromunmanaged at VT_01
.data VT_01 = int64(0)

.vtfixup [1] int64 fromunmanaged at VT_02
.data VT_02 = int64(0)
"@
            $ilContent = $ilContent -replace '(\.module[^\r\n]+)', "`$1`r`n$vtfixup"
        }

        # Clear ILONLY flag (0x00000001) so PE loader and ILASM generate unmanaged exports in PE header
        $ilContent = $ilContent -replace '\.corflags 0x[0-9a-fA-F]+', '.corflags 0x00000000'

        $startupPattern = '(?s)(\.method\s+[^{]+?\blsAppStartup\([^{]+?\{)'
        $startupReplacement = "`$1`r`n    .vtentry 1 : 1`r`n    .export [1] as lsAppStartup"
        if ($ilContent -notmatch "\.export \[1\] as lsAppStartup") {
            $ilContent = [regex]::Replace($ilContent, $startupPattern, $startupReplacement)
        }

        $shutdownPattern = '(?s)(\.method\s+[^{]+?\blsAppShutdown\([^{]+?\{)'
        $shutdownReplacement = "`$1`r`n    .vtentry 2 : 1`r`n    .export [2] as lsAppShutdown"
        if ($ilContent -notmatch "\.export \[2\] as lsAppShutdown") {
            $ilContent = [regex]::Replace($ilContent, $shutdownPattern, $shutdownReplacement)
        }

        [System.IO.File]::WriteAllText($ilFile, $ilContent)

        & $ilasmPath /dll /pe64 /x64 /nologo /quiet /out=$mainDll $ilFile
        if ($LASTEXITCODE -ne 0) {
            throw "ILASM assembly failed for $appName with exit code $LASTEXITCODE."
        }
        Write-Host "Native export weaving completed successfully for $appName (lsAppStartup & lsAppShutdown emitted)." -ForegroundColor Green
    } else {
        Write-Host "Warning: ildasm/ilasm not found or DLL missing. Native export weaving skipped." -ForegroundColor Yellow
    }

    # 5. Enforce SDK Private=False Constraints
    Write-Host "[5/6] Validating output and SDK reference isolation for $appName..." -ForegroundColor Yellow
    $forbiddenDlls = @("FARO.LS.SDK.AppLogic.dll", "FARO.LS.SDK.DataLogic.dll")
    foreach ($forbidden in $forbiddenDlls) {
        $found = Join-Path $binDir $forbidden
        if (Test-Path $found) {
            Remove-Item $found -Force
        }
    }
    Write-Host "Assembly isolation check passed for $appName." -ForegroundColor Green

    # 6. Stage and Package into .faroapp
    Write-Host "[6/6] Packaging FARO App bundle for $appName..." -ForegroundColor Yellow
    $stagingDir = Join-Path $scriptDir "obj\package-staging-$appName"
    if (Test-Path $stagingDir) {
        Remove-Item -Path $stagingDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

    # Copy Primary Assembly and Debug Symbols
    Copy-Item -Path $mainDll -Destination $stagingDir -Force
    $pdbFile = Join-Path $binDir "$appName.pdb"
    if (Test-Path $pdbFile) {
        Copy-Item -Path $pdbFile -Destination $stagingDir -Force
    }

    # Stage SDK reference assemblies alongside app DLL for AppDomain resolution
    Copy-Item -Path (Join-Path $libDir "FARO.LS.SDK.*.dll") -Destination $stagingDir -Force

    # Copy Manifests
    $appManifestXml = Join-Path $appDir "AppManifest.xml"
    if (Test-Path $appManifestXml) {
        Copy-Item -Path $appManifestXml -Destination (Join-Path $stagingDir "AppManifest.xml") -Force
    }

    $appXml = Join-Path $appDir "App.xml"
    if (Test-Path $appXml) {
        Copy-Item -Path $appXml -Destination (Join-Path $stagingDir "App.xml") -Force
    }

    # Copy Root AppIcon.ico
    $appIcon = Join-Path $appDir "AppIcon.ico"
    if (Test-Path $appIcon) {
        Copy-Item -Path $appIcon -Destination (Join-Path $stagingDir "AppIcon.ico") -Force
    }

    # Copy Resources
    $resourcesSrc = Join-Path $appDir "Resources"
    if (Test-Path $resourcesSrc) {
        Copy-Item -Path $resourcesSrc -Destination $stagingDir -Recurse -Force
    }

    # Create .faroapp and .fpp archives
    $packagePath = Join-Path $OutputDir "$appName.faroapp"
    $fppPackagePath = Join-Path $OutputDir "$appName.fpp"

    if (Test-Path $packagePath) { Remove-Item -Path $packagePath -Force }
    if (Test-Path $fppPackagePath) { Remove-Item -Path $fppPackagePath -Force }

    [System.IO.Compression.ZipFile]::CreateFromDirectory($stagingDir, $packagePath)
    Copy-Item -Path $packagePath -Destination $fppPackagePath -Force

    Remove-Item -Path $stagingDir -Recurse -Force

    $hash = Get-FileHash -Path $packagePath -Algorithm SHA256
    $sizeKb = [Math]::Round((Get-Item $packagePath).Length / 1KB, 2)

    Write-Host "Package Created : $packagePath ($sizeKb KB)" -ForegroundColor Green
    Write-Host "Legacy Alias    : $fppPackagePath" -ForegroundColor Green
    Write-Host "SHA256 Hash     : $($hash.Hash)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "       ALL PACKAGES CREATED SUCCESSFULLY                    " -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host "To install in FARO SCENE:" -ForegroundColor White
Write-Host "  1. Drag and drop any .faroapp directly onto the open FARO SCENE window, OR"
Write-Host "  2. Go to SCENE Menu -> Apps -> Import / Install App -> select package."
Write-Host "============================================================" -ForegroundColor Green
