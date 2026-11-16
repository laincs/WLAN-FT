Option Explicit

Dim shell, files, scriptPath, powershellPath, command, result, errorMessage
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")

scriptPath = files.BuildPath(files.GetParentFolderName(WScript.ScriptFullName), "WLAN-FT.ps1")
powershellPath = shell.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
command = """" & powershellPath & """ -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File """ & scriptPath & """"

On Error Resume Next
result = shell.Run(command, 0, True)
errorMessage = Err.Description
On Error GoTo 0

If errorMessage <> "" Then
    MsgBox "Could not start WLAN-FT: " & errorMessage, vbCritical, "WLAN-FT"
    WScript.Quit 1
ElseIf result <> 0 Then
    MsgBox "Could not start WLAN-FT. Run WLAN-FT.ps1 in PowerShell to see the error details.", vbCritical, "WLAN-FT"
    WScript.Quit result
End If
