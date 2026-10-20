#requires -Version 5.1
<#
.SYNOPSIS
Builds a self-contained Windows x64 release and creates out/CAERO.zip.
.DESCRIPTION
Run with: powershell -NoProfile -ExecutionPolicy Bypass -File .\Release.ps1
Requires the .NET SDK and access to NuGet packages.
The archive contains only CAERO/ at its root, with build output under CAERO/bin/.
The contents of doc/ are copied directly into CAERO/.
An existing archive is replaced only after publishing and compression succeed.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$solution = Join-Path $PSScriptRoot 'src\CAERO.sln'
$documents = Join-Path $PSScriptRoot 'doc'
$outputDirectory = Join-Path $PSScriptRoot 'out'
$archive = Join-Path $outputDirectory 'CAERO.zip'

try {
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
        throw "Solution not found: $solution"
    }
    if (-not (Test-Path -LiteralPath $documents -PathType Container)) {
        throw "Documentation directory not found: $documents"
    }

    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $workDirectory = Join-Path $outputDirectory ('.release-' + [guid]::NewGuid().ToString('N'))
    $packageDirectory = Join-Path $workDirectory 'CAERO'
    $publishDirectory = Join-Path $packageDirectory 'bin'
    $temporaryArchive = Join-Path $workDirectory 'CAERO.zip'

    try {
        New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
        # Publish restores packages and builds the solution in Release configuration.
        # Include .NET and native dependencies so the extracted executable can run.
        & $dotnet publish $solution --configuration Release --runtime win-x64 `
            --self-contained true "-p:PublishDir=$publishDirectory/" `
            '-p:UseAppHost=true' '-p:DebugType=None' '-p:DebugSymbols=false'
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE."
        }
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'CAERO.exe') -PathType Leaf)) {
            throw 'The published CAERO.exe was not found.'
        }

        Get-ChildItem -LiteralPath $documents -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $packageDirectory -Recurse -Force
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $packageDirectory,
            $temporaryArchive,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $true
        )
        Move-Item -LiteralPath $temporaryArchive -Destination $archive -Force
        Write-Host "Created: $archive"
    }
    finally {
        # Delete only the unique staging directory created inside this repository's out/.
        $resolvedWork = [System.IO.Path]::GetFullPath($workDirectory)
        $resolvedOutput = [System.IO.Path]::GetFullPath($outputDirectory).TrimEnd('\', '/')
        if ([System.IO.Path]::GetDirectoryName($resolvedWork) -ne $resolvedOutput -or
            [System.IO.Path]::GetFileName($resolvedWork) -notlike '.release-*') {
            throw "Refusing to remove staging directory outside out/: $resolvedWork"
        }
        if (Test-Path -LiteralPath $resolvedWork) {
            Remove-Item -LiteralPath $resolvedWork -Recurse -Force
        }
    }
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
