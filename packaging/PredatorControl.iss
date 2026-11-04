#ifndef AppExe
  #error Build through packaging/Build.ps1
#endif
[Setup]
AppId={{864743A4-42D1-4D15-A327-8CCEA718C2A1}
AppName=Predator Control
AppVersion=1.4.3
AppPublisher=Hadisovic
AppPublisherURL=https://github.com/Hadisovic/PredatorControl
DefaultDirName={autopf}\PredatorControl\1.4.3\{#AppHash}
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=PredatorControl-Setup-1.4.3
SetupIconFile=..\src\appicon.ico
UninstallDisplayIcon={app}\PredatorControl.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
LicenseFile=Runtime-Terms.txt

[Tasks]
Name: startup; Description: "Start Predator Control with Windows"; Flags: checkedonce

[Files]
Source: "{#AppExe}"; DestDir: "{app}"; DestName: "PredatorControl.exe"; Flags: ignoreversion
Source: "{#AppExe}"; DestName: "package-preflight.exe"; Flags: dontcopy
Source: "ThirdPartyNotices.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Predator Control"; Filename: "{app}\PredatorControl.exe"; WorkingDir: "{app}"

[Code]
function InitializeSetup(): Boolean;
var Code: Integer;
begin
  ExtractTemporaryFile('package-preflight.exe');
  Result := Exec(ExpandConstant('{tmp}\package-preflight.exe'), '--package-preflight', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
  if not Result then MsgBox('The installation folder could not be verified. No startup tasks were created.', mbError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer; Expected: String;
begin
  Expected := ExpandConstant('{autopf}\PredatorControl\1.4.3\{#AppHash}');
  if CompareText(ExpandConstant('{app}'), Expected) <> 0 then begin
    Result := 'This package requires its protected application folder. A custom installation path is not supported.';
    Exit;
  end;
  if not Exec(ExpandConstant('{tmp}\package-preflight.exe'), '--package-preflight', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    Result := 'The installation folder could not be verified. Existing permissions were not changed.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer; Args: String;
begin
  if CurStep = ssPostInstall then begin
    Args := '--package-install-manual';
    if WizardIsTaskSelected('startup') then Args := '--package-install-startup';
    if not Exec(ExpandConstant('{app}\PredatorControl.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException('Application preparation or startup configuration failed. Startup was not enabled. Run setup again or use the app cleanup option.');
  end;
end;

function InitializeUninstall(): Boolean;
var Code: Integer;
begin
  Result := Exec(ExpandConstant('{app}\PredatorControl.exe'), '--package-uninstall-check', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
  if not Result then MsgBox('Startup cleanup could not be verified. Installed files were retained.', mbError, MB_OK);
end;
