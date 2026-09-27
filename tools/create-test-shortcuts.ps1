param([string]$Folder,[string]$Game)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $Folder -Force | Out-Null
$shell=New-Object -ComObject WScript.Shell
foreach($item in @(@('normal.lnk','AMS2 Korean Launcher.exe'),@('vr.lnk','AMS2 Korean VR Launcher.exe'),@('unrelated.lnk','AMS2.exe'))) {
    $shortcut=$shell.CreateShortcut((Join-Path $Folder $item[0]))
    $shortcut.TargetPath=Join-Path $Game $item[1]
    $shortcut.Save()
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
}
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
