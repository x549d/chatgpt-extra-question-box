#ifndef PayloadDir
  #error PayloadDir must point to the staged release payload
#endif
#ifndef ProductVersion
  #define ProductVersion "0.3.0"
#endif
[Setup]
AppId={code:ApplicationId}
UsePreviousLanguage=no
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

[Tasks]
Name: "startmenu"; Description: "Create Start Menu shortcuts"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}\versions\{#ProductVersion}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Annotation History"; Filename: "{app}\versions\{#ProductVersion}\codex-plugin\plugins\context-notes\bin\SelectionProbe.exe"; Parameters: "--show-history"; Tasks: startmenu
Name: "{group}\User Guide"; Filename: "{app}\versions\{#ProductVersion}\docs\USER-GUIDE.md"; Tasks: startmenu
Name: "{group}\Uninstall"; Filename: "{uninstallexe}"; Tasks: startmenu

[UninstallDelete]
Type: files; Name: "{app}\installation-context.ini"

[Code]
var
  KeepHistory: Boolean;
  DeploymentFailed: Boolean;
  WasFreshInstall: Boolean;
function ApplicationId(Param: String): String;
var TestRoot: String;
begin
  TestRoot := ExpandConstant('{param:TESTROOT|}');
  if TestRoot = '' then Result := '{72EC0AA1-761D-4894-B2B8-A1FD53C06690}'
  else Result := 'ContextNotesNative.Test.' + Copy(GetSHA256OfUnicodeString(Lowercase(ExpandFileName(TestRoot))), 1, 16);
end;

function InitializeSetup(): Boolean;
var Index, ResultCode: Integer; FoundRuntime, Existed, TempOk: Boolean; Captured: TExecOutput; Key, TempProbe: String;
begin
  FoundRuntime := False;
  if ExecAndCaptureOutput(ExpandConstant('{pf64}\dotnet\dotnet.exe'), '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode, Captured) then
    if ResultCode = 0 then
      for Index := 0 to GetArrayLength(Captured.StdOut) - 1 do
        if Pos('Microsoft.WindowsDesktop.App 10.', Captured.StdOut[Index]) = 1 then FoundRuntime := True;
  Result := FoundRuntime;
  if not Result then SuppressibleMsgBox('Install .NET 10 Windows Desktop Runtime x64 before running this installer. Codex Desktop will also be checked before plugin deployment.', mbError, MB_OK, IDOK);
  if not Result then exit;
  TempProbe := ExpandConstant('{tmp}\context-notes-preflight.tmp');
  TempOk := SaveStringToFile(TempProbe, 'metadata-only', False);
  if TempOk then DeleteFile(TempProbe);
  if TempOk then Log('Context Notes temp preflight dir=' + ExpandConstant('{tmp}') + ' write=ok')
  else begin
    Log('Context Notes temp preflight dir=' + ExpandConstant('{tmp}') + ' write=failed');
    SuppressibleMsgBox('This execution session cannot write to the temporary directory. Copy the installer to a normal user folder (such as Downloads or Desktop) and run again. Administrator permission is not required.', mbError, MB_OK, IDOK);
    Result := False;
    exit;
  end;
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + ApplicationId('') + '_is1';
  Log('Context Notes registry preflight target=HKCU64\' + Key + '\ContextNotesPreflight');
  Existed := RegKeyExists(HKCU64, Key);
  Result := RegWriteStringValue(HKCU64, Key + '\ContextNotesPreflight', 'Probe', 'metadata-only');
  if Result then Log('Context Notes current-user registry preflight write=ok')
  else Log('Context Notes current-user registry preflight write=failed; no machine-level registration attempted');
  RegDeleteValue(HKCU64, Key + '\ContextNotesPreflight', 'Probe');
  RegDeleteKeyIfEmpty(HKCU64, Key + '\ContextNotesPreflight');
  if not Existed then RegDeleteKeyIfEmpty(HKCU64, Key);
  if not Result then begin
    Log('Context Notes per-user uninstall registry preflight failed before file deployment. No elevation or ACL change attempted.');
    SuppressibleMsgBox('This execution session cannot write the current-user uninstall registration (for example, if launched from a Low Integrity / sandboxed directory). Copy the installer to a normal user folder such as Downloads or Desktop and run again, or use the portable package. Administrator permission is not required.', mbError, MB_OK, IDOK);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ResultCode: Integer; Binary: String;
begin
  Result := '';
  DeploymentFailed := False;
  WasFreshInstall := not FileExists(ExpandConstant('{app}\unins000.exe'));
  if ExpandConstant('{param:TESTROOT|}') <> '' then exit;
  Binary := ExpandConstant('{app}\versions\{#ProductVersion}\codex-plugin\plugins\context-notes\bin\SelectionProbe.exe');
  if FileExists(Binary) then begin
    Exec(Binary, '--stop-watcher', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(Binary, '--stop', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1000);
  end;
end;

function RunManager(Action: String; Extra: String): Boolean;
var ResultCode: Integer; Params, TestRoot: String;
begin
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\versions\{#ProductVersion}\scripts\Manage-Installation.ps1') + '" -Action ' + Action + ' -PayloadRoot "' + ExpandConstant('{app}\versions\{#ProductVersion}') + '" ' + Extra;
  TestRoot := ExpandConstant('{param:TESTROOT|}');
  if TestRoot = '' then TestRoot := GetIniString('Installation', 'TestRoot', '', ExpandConstant('{app}\installation-context.ini'));
  if (TestRoot = '') and (GetIniString('Installation', 'IsolatedTest', '0', ExpandConstant('{app}\installation-context.ini')) = '1') then begin
    Log('Context Notes refusing to fall back to production profile for an isolated test installation.');
    Result := False;
    exit;
  end;
  if TestRoot <> '' then
    Params := Params + ' -SkipLaunch -ProfileRoot "' + TestRoot + '\profile" -DataRoot "' + TestRoot + '\data" -StartupRoot "' + TestRoot + '\startup"';
  Log('Context Notes manager action=' + Action + ' test_isolated=' + IntToStr(Integer(TestRoot <> '')));
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  Log('Context Notes manager action=' + Action + ' exit_code=' + IntToStr(ResultCode));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var TestRoot, Extra, ContextIni, UninsExe: String; ResultCode: Integer;
begin
  if CurStep = ssPostInstall then begin
    TestRoot := ExpandConstant('{param:TESTROOT|}');
    Extra := '';
    ContextIni := ExpandConstant('{app}\installation-context.ini');
    if TestRoot <> '' then begin
      SetIniString('Installation', 'IsolatedTest', '1', ContextIni);
      SetIniString('Installation', 'TestRoot', TestRoot, ContextIni);
      if ExpandConstant('{param:INJECTFAILURE|}') = '1' then Extra := '-InjectFailure';
    end;
    if not RunManager('Install', Extra) then begin
      DeploymentFailed := True;
      Log('Context Notes deployment failed; WasFreshInstall=' + IntToStr(Integer(WasFreshInstall)));
      SuppressibleMsgBox('Plugin installation failed. The deployment manager attempted to restore the previous registration and history. See the troubleshooting guide.', mbError, MB_OK, IDOK);
      if WasFreshInstall then begin
        UninsExe := ExpandConstant('{uninstallexe}');
        if FileExists(UninsExe) then begin
          Log('Context Notes rolling back fresh installation via ' + UninsExe);
          Exec(UninsExe, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ROLLBACK=1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
          Log('Context Notes rollback uninstaller exit_code=' + IntToStr(ResultCode));
        end;
        if FileExists(ContextIni) then DeleteFile(ContextIni);
      end;
    end;
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  if DeploymentFailed then Result := 1
  else Result := 0;
end;

function InitializeUninstall(): Boolean;
begin
  if ExpandConstant('{param:ROLLBACK|}') = '1' then begin
    Log('Context Notes uninstall running in setup rollback mode; skipping manager uninstall');
    Result := True;
    exit;
  end;
  KeepHistory := True;
  if not UninstallSilent then
    KeepHistory := MsgBox('Keep local annotation history and backups? Yes is recommended. Choosing No permanently removes the local database; App Server history is managed separately by Codex.', mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDYES
  else if ExpandConstant('{param:REMOVEHISTORY|}') = '1' then
    KeepHistory := False;
  if KeepHistory then Result := RunManager('Uninstall', '')
  else Result := RunManager('Uninstall', '-RemoveHistory');
  if not Result then MsgBox('Unable to stop or unregister the plugin. Application files and history have been preserved; retry after closing Context Notes.', mbError, MB_OK);
end;
