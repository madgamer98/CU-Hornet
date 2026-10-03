# Reliably launch Casualties: Unknown into the tutorial sandbox for testing.
# Usage: powershell -ExecutionPolicy Bypass -File tools\load_sandbox.ps1 [-OutShot <path>]
param(
    [string]$OutShot = "%PROJECT_DIR%\shots\sandbox_now.png"
)

$um = "%TOOLKIT_DIR%\bin\um"
$proc = "CasualtiesUnknown"
$log = "%CU_GAME_DIR%\BepInEx\LogOutput.log"

# Kill an existing instance by exact PID.
$ps = & $um win ps 2>&1
$existing = $ps | Select-String $proc
if ($existing) {
    $procId = [int](($existing[0].Line -split '\s+')[1])
    $null = & $um win kill $procId 2>&1
    Start-Sleep -Seconds 4
}

$null = & $um win launch --steam 4576510 2>&1
Start-Sleep -Seconds 20

# Dismiss the content warning, load the tutorial world, then jump to the sandbox course.
$null = & $um win drive --proc $proc "focus" "key 0x11" 2>&1
Start-Sleep -Seconds 3
$null = & $um win drive --proc $proc "key 0x78" 2>&1
Start-Sleep -Seconds 9
$null = & $um win drive --proc $proc "key 0x77" 2>&1
Start-Sleep -Seconds 5

$null = & $um win shot --exe "CasualtiesUnknown.exe" $OutShot --scale 0.5 2>&1
Write-Output "sandbox shot: $OutShot"
Select-String -Path $log -Pattern "Hornet in Casualties" | Select-Object -Last 8 | ForEach-Object { $_.Line }
