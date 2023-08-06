# Save the current directory
$initialDirectory = Get-Location

# Publish the dotnet application
& 'dotnet' publish -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true

# Move to the directory where the EXE file is
Set-Location -Path ".\bin\Release\net6.0-windows\win-x86\publish\"

# Create a timestamp
$timestamp = Get-Date -Format "yyyyMMddHHmmss"

# Create a copy of the EXE file with the timestamp in its name
$exeName = "NostalgiaAnticheat_" + $timestamp + ".exe"
Copy-Item -Path ".\NostalgiaAnticheat.exe" -Destination $exeName

# Create a ZIP file with the timestamp in its name in the initial directory
$zipName = Join-Path -Path $initialDirectory -ChildPath ("nostalgia_" + $timestamp + ".zip")
& '7z' a -tzip -mx9 $zipName $exeName
