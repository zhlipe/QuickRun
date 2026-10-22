# QuickRun build script (Windows PowerShell 5.1, no .NET SDK needed)
# Usage: powershell -ExecutionPolicy Bypass -File .\build.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root 'QuickRun.cs'
$icon = Join-Path $root 'assets\quickrun.ico'
$manifest = Join-Path $root 'assets\app.manifest'
$output = Join-Path $root 'QuickRun.exe'

foreach ($file in @($source, $icon, $manifest)) {
    if (-not (Test-Path $file)) { throw "Missing file: $file" }
}

$code = [System.IO.File]::ReadAllText($source, [System.Text.Encoding]::UTF8)

$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateExecutable = $true
$cp.GenerateInMemory = $false
$cp.MainClass = 'QuickRun.Program'
$cp.OutputAssembly = $output
$cp.CompilerOptions = "/target:winexe /optimize+ /win32icon:`"$icon`" /win32manifest:`"$manifest`" /nowarn:1591"
foreach ($ref in @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll')) {
    [void]$cp.ReferencedAssemblies.Add($ref)
}

if (Test-Path $output) { Remove-Item $output -Force }

$results = $provider.CompileAssemblyFromSource($cp, @($code))
if ($results.Errors.Count -gt 0) {
    foreach ($err in $results.Errors) {
        $line = if ($err.Line -gt 0) { " (line $($err.Line))" } else { '' }
        Write-Host ("[{0}] {1}{2}" -f $err.ErrorNumber, $err.ErrorText, $line) -ForegroundColor Red
    }
    throw "Build failed: $($results.Errors.Count) error(s)"
}

$file = Get-Item $output
Write-Host ("Build OK: {0} ({1:N1} KB)" -f $file.FullName, ($file.Length / 1KB)) -ForegroundColor Green
