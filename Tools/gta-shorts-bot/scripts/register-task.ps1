param([string]$At = '18:00', [switch]$Enable)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (Get-ScheduledTask -TaskName 'GTA Shorts Bot' -ErrorAction SilentlyContinue) { throw 'GTA Shorts Bot task already exists; inspect or remove it before registering again.' }
if (-not (Test-Path "$projectRoot\.venv\Scripts\python.exe")) { throw 'Run setup first' }
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$projectRoot\scripts\run-daily.ps1`"" -WorkingDirectory $projectRoot
$trigger = New-ScheduledTaskTrigger -Daily -At $At
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 4)
$principal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
$task = New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings -Principal $principal
if (-not $Enable) { $task.Settings.Enabled = $false }
Register-ScheduledTask -TaskName 'GTA Shorts Bot' -InputObject $task | Out-Null
Write-Output "Registered GTA Shorts Bot at $At local time. Enabled: $Enable"
