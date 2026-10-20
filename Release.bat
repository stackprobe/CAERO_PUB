CALL Clean.bat

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Release.ps1"

C:\app\Annex\SubTools\Dev\RevisionCodeGen.exe * out

TIMEOUT 2
