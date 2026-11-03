param(
    [Parameter(Mandatory=$true)][string]$GameFolder,
    [string]$NexusExecutable,
    [string]$RepairDll
)
$ErrorActionPreference = 'Stop'
if (!$NexusExecutable) { $NexusExecutable = Join-Path $PSScriptRoot '..\dist\Next\AONexus.exe' }
if (!$RepairDll) { $RepairDll = Join-Path $PSScriptRoot '..\runtime\AOVKPackage\version-rebirth.dll' }
if ([IntPtr]::Size -ne 4) { throw 'Run this script with Windows SysWOW64\WindowsPowerShell\v1.0\powershell.exe (x86).' }
$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($NexusExecutable))
$graphics = $assembly.GetType('AOLauncher.AovkGraphics', $true)
$method = $graphics.GetMethod('InstallStartupRepair', [Reflection.BindingFlags]'Static,NonPublic')
try {
    $method.Invoke($null, [object[]]@([IO.Path]::GetFullPath($GameFolder), [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($RepairDll)), $null)) | Out-Null
} catch [Reflection.TargetInvocationException] {
    throw $_.Exception.InnerException
}
Write-Host 'Startup repair installed. Renderer and preferences preserved; rollback copy retained in NexusGraphicsBackups.'
