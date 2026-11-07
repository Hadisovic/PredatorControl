#ifndef AppExe
  #error Build through packaging/Build.ps1
#endif
#ifndef LoaderDll
  #error Build through packaging/Build.ps1
#endif
[Setup]
AppId={{864743A4-42D1-4D15-A327-8CCEA718C2A1}
AppName=Predator Control
AppVersion=1.4.4
VersionInfoVersion=1.4.4.0
VersionInfoProductVersion=1.4.4
AppPublisher=Hadisovic
AppPublisherURL=https://github.com/Hadisovic/PredatorControl
DefaultDirName={autopf}\PredatorControl
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=PredatorControl-Setup-1.4.4
SetupIconFile=..\src\appicon.ico
UninstallDisplayIcon={app}\{code:ProductExe}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupMutex=PredatorControlSetup

[Tasks]
Name: startup; Description: "Start {code:ProductTitle} with Windows"; Flags: checkedonce

[Files]
Source: "{#AppExe}"; DestDir: "{app}"; DestName: "{code:ProductExe}"; Flags: ignoreversion
Source: "{#AppExe}"; DestName: "package-preflight.exe"; Flags: dontcopy
Source: "{#LoaderDll}"; DestDir: "{autopf}\PredatorControlData"; DestName: "WebView2Loader.dll"; Flags: ignoreversion
Source: "ThirdPartyNotices.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "dotnet-*.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{code:ProductTitle}"; Filename: "{app}\{code:ProductExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{code:ProductTitle}"; Filename: "{app}\{code:ProductExe}"; WorkingDir: "{app}"

[Run]
Filename: "{app}\{code:ProductExe}"; Description: "Launch {code:ProductTitle}"; Flags: postinstall nowait skipifsilent runascurrentuser

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
const
  InstalledKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{864743A4-42D1-4D15-A327-8CCEA718C2A1}_is1';

var
  NitroProduct: Boolean;
  PreviousUninstaller: String;
  ReplaceInstallation, PreviousStartup, PreviousRemoved, InstallCompleted: Boolean;
  CleanupWarning: Boolean;
  InstallAction: String;

function ProductExe(Param: String): String;
begin
  if NitroProduct then Result := 'NitroControl.exe' else Result := 'PredatorControl.exe';
end;

function ProductTitle(Param: String): String;
begin
  if NitroProduct then Result := 'Nitro Control' else Result := 'Predator Control';
end;

function CandidatePath(): String;
begin
  Result := AddBackslash(ExpandConstant('{autopf}')) + ChangeFileExt(ProductExe(''), '');
end;

function RunPreflight(const Args: String; var Code: Integer): Boolean;
begin
  Result := Exec(ExpandConstant('{tmp}\package-preflight.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code);
  Log(Args + ' result: ' + IntToStr(Code));
end;

function SetupError(): String;
var Contents: AnsiString;
begin
  Result := '';
  if LoadStringFromFile(ExpandConstant('{localappdata}\PredatorControl\last-setup-error.txt'), Contents) then
    Result := #13#10 + Copy(String(Contents), 1, 1000);
end;

function ValidatePrevious(var Code: Integer): Boolean;
var Registered: String;
begin
  Result := RegQueryStringValue(HKLM64, InstalledKey, 'UninstallString', Registered) and
    (Registered = '"' + PreviousUninstaller + '"') and
    RunPreflight('--package-check-uninstaller "' + PreviousUninstaller + '"', Code) and
    ((Code = 0) or (Code = 10));
end;

function RemovePrevious(Upgrade: Boolean): Boolean;
var Code, Attempt: Integer; Args: String;
begin
  Result := False;
  if not ValidatePrevious(Code) then Exit;
  if not RunPreflight('--package-stop', Code) or (Code <> 0) then Exit;
  Args := '/SUPPRESSMSGBOXES /NORESTART';
  if WizardSilent then Args := Args + ' /VERYSILENT' else Args := Args + ' /SILENT';
  if Upgrade then Args := Args + ' /PRESERVEPREFERENCES=1';
  if not Exec(PreviousUninstaller, Args,
      ExtractFileDir(PreviousUninstaller), SW_HIDE, ewWaitUntilTerminated, Code) then Exit;
  if Code <> 0 then Exit;
  { Inno's original uninstaller can exit before its temporary second phase. }
  for Attempt := 1 to 150 do begin
    if not RegKeyExists(HKLM64, InstalledKey) and not FileExists(PreviousUninstaller) then begin
      Result := True;
      Exit;
    end;
    Sleep(200);
  end;
end;

function ChooseMaintenance(): Boolean;
var Registered, Details, Action, OldExe: String; InstalledVersion, IncomingVersion: Int64;
    Code, Choice, Comparison: Integer;
begin
  Result := True;
  InstallAction := 'Install';
  if not RegKeyExists(HKLM64, InstalledKey) then Exit;
  Result := False;
  RegQueryStringValue(HKLM64, InstalledKey, 'UninstallString', Registered);
  if (Length(Registered) >= 3) and (Registered[1] = '"') and
    (Registered[Length(Registered)] = '"') then
    PreviousUninstaller := Copy(Registered, 2, Length(Registered) - 2);
  OldExe := 'PredatorControl.exe';
  if CompareText(ExtractFileDir(PreviousUninstaller), ExpandConstant('{autopf}\NitroControl')) = 0 then
    OldExe := 'NitroControl.exe';
  if (PreviousUninstaller = '') or (Pos('"', PreviousUninstaller) <> 0) or
    not ValidatePrevious(Code) or
    not GetPackedVersion(AddBackslash(ExtractFileDir(PreviousUninstaller)) + OldExe, InstalledVersion) or
    not GetPackedVersion(ExpandConstant('{tmp}\package-preflight.exe'), IncomingVersion) then begin
    SuppressibleMsgBox('The registered Predator Control installation could not be verified.'#13#10 +
      Registered + #13#10'Resolve the stale or unsafe registration before running setup again.',
      mbError, MB_OK, IDOK);
    Exit;
  end;
  PreviousStartup := Code = 10;
  Comparison := ComparePackedVersion(IncomingVersion, InstalledVersion);
  ReplaceInstallation := CompareText(ExtractFileDir(PreviousUninstaller), CandidatePath()) <> 0;
  Details := 'Installed version ' + VersionToStr(InstalledVersion) + #13#10 +
    'Installer version ' + VersionToStr(IncomingVersion);
  if WizardSilent then begin
    Result := (Comparison >= 0) and
      (not ReplaceInstallation or (ExpandConstant('{param:ALLOWUPDATE|0}') = '1'));
    if Result then begin
      if Comparison = 0 then InstallAction := 'Repair' else InstallAction := 'Update';
    end;
    if not Result then Log('Maintenance declined. Check version and /ALLOWUPDATE=1.');
    Exit;
  end;
  if Comparison < 0 then begin
    Choice := TaskDialogMsgBox('A newer Predator Control is installed', Details,
      mbConfirmation, MB_OKCANCEL, ['Uninstall'], IDOK);
    if Choice <> IDOK then Exit;
  end else begin
    Action := 'Update';
    if Comparison = 0 then Action := 'Repair';
    Choice := TaskDialogMsgBox('Predator Control is already installed', Details,
      mbConfirmation, MB_YESNOCANCEL, [Action, 'Uninstall'], IDYES);
    if Choice = IDYES then begin
      InstallAction := Action;
      Result := True;
      Exit;
    end;
    if Choice <> IDNO then Exit;
  end;
  if SuppressibleMsgBox('Uninstall Predator Control ' + VersionToStr(InstalledVersion) + '?',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) <> IDYES then Exit;
  if not RemovePrevious(False) then
    SuppressibleMsgBox('Uninstall did not complete. Check the installation at '#13#10 +
      ExtractFileDir(PreviousUninstaller), mbError, MB_OK, IDOK)
  else begin
    PreviousRemoved := True;
    SuppressibleMsgBox('The previous installation was removed successfully. Setup will continue with a fresh installation.',
      mbInformation, MB_OK, IDOK);
    if CompareText(ExtractFileDir(PreviousUninstaller), CandidatePath()) <> 0 then begin
      if RunPreflight('--package-clean-legacy-uninstall', Code) and (Code <> 0) then
        SuppressibleMsgBox('Some legacy data could not be removed. See LocalAppData\PredatorControl\cleanup-report.txt.',
          mbInformation, MB_OK, IDOK);
    end;
  end;
end;

function InitializeSetup(): Boolean;
var Code: Integer;
begin
  ExtractTemporaryFile('package-preflight.exe');
  Result := RunPreflight('--package-product', Code) and ((Code = 0) or (Code = 10));
  if not Result then Exit;
  NitroProduct := Code = 10;
  Result := ChooseMaintenance();
  if not Result then Exit;
  if not (DirExists(CandidatePath()) or FileExists(CandidatePath())) then Exit;
  Result := RunPreflight('--package-preflight', Code) and (Code = 0);
  if not Result then begin
    SuppressibleMsgBox('Setup could not verify the destination:'#13#10 + CandidatePath() + #13#10 +
      'Review and move or remove the conflicting files, then rerun setup.',
      mbError, MB_OK, IDOK);
    Exit;
  end;
  if WizardSilent then
    Result := ExpandConstant('{param:CONTINUEEXISTING|0}') = '1'
  else
    Result := TaskDialogMsgBox('Files already exist in the destination',
      CandidatePath() + #13#10'The files passed preflight.', mbConfirmation,
      MB_OKCANCEL, ['Continue'], IDOK) = IDOK;
end;

procedure InitializeWizard();
begin
  WizardForm.DirEdit.Text := CandidatePath();
  if PreviousUninstaller <> '' then begin
    if PreviousStartup then WizardSelectTasks('startup') else WizardSelectTasks('');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Result := '';
  if CompareText(ExpandConstant('{app}'), CandidatePath()) <> 0 then begin
    Result := 'This package requires its protected application folder. A custom installation path is not supported.';
    Exit;
  end;
  if not RunPreflight('--package-preflight', Code) or (Code <> 0) then begin
    Result := 'The installation folder could not be verified.' + SetupError();
    Exit;
  end;
  if not RunPreflight('--package-stop', Code) or (Code <> 0) then begin
    Result := 'Close all running copies of Predator Control and retry setup.';
    Exit;
  end;
  if ReplaceInstallation and not PreviousRemoved then begin
    if not WizardSilent then begin
      WizardForm.StatusLabel.Caption := 'Removing the previous installation. Setup will continue when removal is complete.';
      WizardForm.Update;
    end;
    if not RemovePrevious(True) then begin
      Result := 'The previous installation could not be removed. Resolve the uninstall error and retry setup.';
      Exit;
    end;
    PreviousRemoved := True;
    if not WizardSilent then begin
      WizardForm.StatusLabel.Caption := 'Previous installation removed. Continuing with the update.';
      WizardForm.Update;
    end;
  end;
  if not RunPreflight('--package-data-preflight', Code) or (Code <> 0) then begin
    Result := 'The separate native data folder could not be safely replaced or secured.' + SetupError();
    Exit;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID <> wpFinished then Exit;
  if InstallAction = 'Repair' then begin
    WizardForm.FinishedHeadingLabel.Caption := 'Repair completed';
    WizardForm.FinishedLabel.Caption := 'Predator Control was repaired successfully. The installed application was verified.';
  end else if InstallAction = 'Update' then begin
    WizardForm.FinishedHeadingLabel.Caption := 'Update completed';
    WizardForm.FinishedLabel.Caption := 'Predator Control was updated successfully. The installed application was verified.';
  end else if PreviousRemoved then begin
    WizardForm.FinishedHeadingLabel.Caption := 'Installation completed';
    WizardForm.FinishedLabel.Caption := 'The previous installation was removed and Predator Control was installed and verified successfully.';
  end else begin
    WizardForm.FinishedHeadingLabel.Caption := 'Installation completed';
    WizardForm.FinishedLabel.Caption := 'Predator Control was installed and verified successfully.';
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer; Args: String;
begin
  if CurStep = ssPostInstall then begin
    if CompareText(GetSHA256OfFile(AddBackslash(ExpandConstant('{app}')) + ProductExe('')), '{#AppHash}') <> 0 then
      RaiseException('The installed application did not match the setup package.');
    Args := '--package-install-manual';
    if WizardIsTaskSelected('startup') then Args := '--package-install-startup';
    if not Exec(AddBackslash(ExpandConstant('{app}')) + ProductExe(''), Args, '', SW_HIDE, ewWaitUntilTerminated, Code) then
      RaiseException('Setup could not launch the installed application to configure startup.');
    if Code = 21 then
      SuppressibleMsgBox('The app was installed. Start with Windows could not be configured.', mbError, MB_OK, IDOK)
    else if Code <> 0 then
      RaiseException('Setup could not verify the installed executable or folder.' + SetupError());
    if RunPreflight('--package-prune', Code) and (Code <> 0) then
      SuppressibleMsgBox('Some legacy data was retained. See LocalAppData\PredatorControl\cleanup-report.txt.',
        mbInformation, MB_OK, IDOK);
  end;
  if CurStep = ssDone then InstallCompleted := True;
end;

procedure DeinitializeSetup();
begin
  if PreviousRemoved and not InstallCompleted then
    SuppressibleMsgBox('The old installation was removed, but the update did not finish. ' +
      'Run this setup again, or reinstall the previous version from its original installer.',
      mbError, MB_OK, IDOK);
end;

procedure InitializeUninstallProgressForm();
begin
  UninstallProgressForm.StatusLabel.Caption := 'Removing Predator Control and its managed data...';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Code: Integer; Exe, Args: String;
begin
  if CurUninstallStep = usDone then begin
    if not UninstallSilent then begin
      if CleanupWarning then
        MsgBox('Predator Control was uninstalled. Some data could not be removed; see LocalAppData\PredatorControl\cleanup-report.txt.', mbInformation, MB_OK)
      else
        MsgBox('Predator Control was uninstalled successfully.', mbInformation, MB_OK);
    end;
    Exit;
  end;
  if CurUninstallStep <> usUninstall then Exit;
  UninstallProgressForm.StatusLabel.Caption := 'Removing Predator Control and checking its managed data...';
  Exe := AddBackslash(ExpandConstant('{app}')) + ExtractFileName(ExpandConstant('{app}')) + '.exe';
  Args := '--package-uninstall-full';
  if ExpandConstant('{param:PRESERVEPREFERENCES|0}') = '1' then Args := '--package-uninstall-check';
  if not Exec(Exe, Args, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    RaiseException('Could not start Predator Control cleanup.');
  Log('Predator Control cleanup exit code: ' + IntToStr(Code));
  if Code = 23 then
    CleanupWarning := True
  else if Code <> 0 then
    RaiseException('Startup cleanup failed. Close Predator Control and try uninstalling again.');
end;
