<#
.SYNOPSIS
    Configura, compila e testa o server. Resolve MSVC/CMake/Ninja do Visual Studio
    (que não estão no PATH) e entra no ambiente de desenvolvedor antes de chamar o CMake.

.EXAMPLE
    .\build.ps1                 # configure + build + test (Debug)
    .\build.ps1 -Preset release # RelWithDebInfo
    .\build.ps1 -NoTest
#>
param(
    [ValidateSet('debug', 'release')]
    [string]$Preset = 'debug',
    [switch]$NoTest,
    [switch]$Fresh
)

$ErrorActionPreference = 'Stop'

# Executa um .exe julgando APENAS o código de saída.
# Sem isto, qualquer linha que o CMake escreva em stderr — um warning (dev),
# por exemplo — vira NativeCommandError e, com ErrorActionPreference='Stop',
# aborta o script antes mesmo de olharmos o exit code.
function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$Exe,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$What
    )

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Exe @Arguments
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    if ($code -ne 0) {
        throw "$What falhou (exit $code)"
    }
}

if (-not $env:VCPKG_ROOT) {
    throw "VCPKG_ROOT não está definido. Aponte para a instalação do vcpkg (ex.: C:\vcpkg)."
}
if (-not (Test-Path "$env:VCPKG_ROOT\scripts\buildsystems\vcpkg.cmake")) {
    throw "vcpkg.cmake não encontrado em $env:VCPKG_ROOT. O vcpkg está bootstrapado?"
}

# --- Localiza a instalação do Visual Studio ---------------------------------
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe não encontrado; o Visual Studio está instalado?" }

$vsPath = & $vswhere -latest -prerelease -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) {
    $vsPath = & $vswhere -latest -prerelease -products * -property installationPath
}
if (-not $vsPath) { throw "Nenhuma instalação do Visual Studio com toolset C++ foi encontrada." }

# --- Importa o ambiente do vcvars64 para esta sessão ------------------------
# O CMake precisa achar `cl.exe`; o vcvars só existe como .bat, então capturamos
# as variáveis que ele define e aplicamos no processo atual.
$vcvars = Join-Path $vsPath 'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path $vcvars)) { throw "vcvars64.bat não encontrado em $vsPath" }

Write-Host "[build] ambiente MSVC: $vsPath" -ForegroundColor Cyan
& cmd.exe /c "`"$vcvars`" >nul 2>&1 && set" | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') {
        Set-Item -Path "env:$($Matches[1])" -Value $Matches[2] -ErrorAction SilentlyContinue
    }
}

# --- CMake e Ninja embutidos no Visual Studio -------------------------------
$cmakeExe = Get-Command cmake -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
$ctestExe = Get-Command ctest -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if (-not $cmakeExe) {
    $bundled = Join-Path $vsPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin'
    if (-not (Test-Path (Join-Path $bundled 'cmake.exe'))) { throw "cmake.exe não encontrado." }
    $cmakeExe = Join-Path $bundled 'cmake.exe'
    $ctestExe = Join-Path $bundled 'ctest.exe'
    $ninjaDir = Join-Path $vsPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja'
    $env:PATH = "$bundled;$ninjaDir;$env:PATH"
}

Push-Location $PSScriptRoot
try {
    $configureArgs = @('--preset', $Preset)
    if ($Fresh) { $configureArgs += '--fresh' }

    Write-Host "[build] configurando ($Preset)" -ForegroundColor Cyan
    Invoke-Native -Exe $cmakeExe -Arguments $configureArgs -What 'configure'

    Write-Host "[build] compilando" -ForegroundColor Cyan
    Invoke-Native -Exe $cmakeExe -Arguments @('--build', '--preset', $Preset) -What 'build'

    if (-not $NoTest -and $Preset -eq 'debug') {
        Write-Host "[build] testes" -ForegroundColor Cyan
        Invoke-Native -Exe $ctestExe -Arguments @('--preset', $Preset) -What 'testes'
    }

    Write-Host "[build] ok" -ForegroundColor Green
}
finally {
    Pop-Location
}
