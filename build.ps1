$ErrorActionPreference = 'Stop'
$sourceFiles = @('AssemblyInfo.cs','Power.cs','Telemetry.cs','Engine.cs','Program.cs','WpfApp.cs') | ForEach-Object { Join-Path $PSScriptRoot "source\$_" }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$PSScriptRoot\BatteryPilot.exe" "/win32icon:$PSScriptRoot\BatteryPilot.ico" "/win32manifest:$PSScriptRoot\source\app.manifest" "/resource:$PSScriptRoot\source\MainWindow.xaml,MainWindow.xaml" "/reference:$wpf\PresentationFramework.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.Xml.dll /reference:System.Core.dll $sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
