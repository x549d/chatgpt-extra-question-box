#ifndef PayloadDir
  #error PayloadDir must point to the staged release payload
#endif
#ifndef ProductVersion
  #define ProductVersion "0.3.0"
#endif
[Setup]
AppId={{72EC0AA1-761D-4894-B2B8-A1FD53C06690}
AppName=Context Notes Native
AppVersion={#ProductVersion}
AppPublisher=x549d
DefaultDirName={localappdata}\Programs\ContextNotesNative
DefaultGroupName=Context Notes Native
PrivilegesRequired=lowest
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
OutputBaseFilename=ContextNotesNative-{#ProductVersion}-setup
OutputDir=..\artifacts
UninstallDisplayIcon={app}\versions\{#ProductVersion}\codex-plugin\plugins\context-notes\bin\SelectionProbe.exe
CloseApplications=no

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}\versions\{#ProductVersion}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Annotation History"; Filename: "{app}\versions\{#ProductVersion}\codex-plugin\plugins\context-notes\bin\SelectionProbe.exe"; Parameters: "--show-history"
Name: "{group}\User Guide"; Filename: "{app}\versions\{#ProductVersion}\docs\USER-GUIDE.md"
Name: "{group}\Uninstall"; Filename: "{uninstallexe}"

[Code]
var KeepHistory: Boolean;
function InitializeSetup(): Boolean;
var Index, ResultCode: Integer; FoundRuntime: Boolean; Captured: TExecOutput;
begin
  FoundRuntime := False;
  if ExecAndCaptureOutput(ExpandConstant('{pf64}\dotnet\dotnet.exe'), '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode, Captured) then
    if ResultCode = 0 then
      for Index := 0 to GetArrayLength(Captured.StdOut) - 1 do
        if Pos('Microsoft.WindowsDesktop.App 10.', Captured.StdOut[Index]) = 1 then FoundRuntime := True;
  Result := FoundRuntime;
  if not Result then SuppressibleMsgBox('Install .NET 10 Windows Desktop Runtime x64 before running this installer. Codex Desktop will also be checked before plugin deployment.', mbError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ResultCode: Integer; Binary: String;
begin
  Result := '';
  Binary := ExpandConstant('{app}\versions\{#ProductVersion}\codex-plugin\plugins\context-notes\bin\SelectionProbe.exe');
  if FileExists(Binary) then begin
    Exec(Binary, '--stop-watcher', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(Binary, '--stop', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1000);
  end;
end;

function RunManager(Action: String; Extra: String): Boolean;
var ResultCode: Integer; Params: String;
begin
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\versions\{#ProductVersion}\scripts\Manage-Installation.ps1') + '" -Action ' + Action + ' -PayloadRoot "' + ExpandConstant('{app}\versions\{#ProductVersion}') + '" ' + Extra;
  if ExpandConstant('{param:TESTROOT|}') <> '' then
    Params := Params + ' -SkipLaunch -ProfileRoot "' + ExpandConstant('{param:TESTROOT}') + '\profile" -DataRoot "' + ExpandConstant('{param:TESTROOT}') + '\data" -StartupRoot "' + ExpandConstant('{param:TESTROOT}') + '\startup"';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    if not RunManager('Install', '') then
      RaiseException('Plugin installation failed. The deployment manager attempted to restore the previous registration and history. See the troubleshooting guide.');
end;

function InitializeUninstall(): Boolean;
begin
  KeepHistory := True;
  if not UninstallSilent then
    KeepHistory := MsgBox('Keep local annotation history and backups? Yes is recommended. Choosing No permanently removes the local database; App Server history is managed separately by Codex.', mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDYES;
  if KeepHistory then Result := RunManager('Uninstall', '')
  else Result := RunManager('Uninstall', '-RemoveHistory');
  if not Result then MsgBox('Unable to stop or unregister the plugin. Application files and history have been preserved; retry after closing Context Notes.', mbError, MB_OK);
end;
