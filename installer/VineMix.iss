#ifndef PayloadDir
  #error PayloadDir is required. Use scripts/build-installer.ps1.
#endif
#ifndef AppVersion
  #error AppVersion is required.
#endif
#ifndef NumericVersion
  #error NumericVersion is required.
#endif
#ifndef DisplayVersion
  #error DisplayVersion is required.
#endif
#ifndef DriverArchiveDir
  #error DriverArchiveDir is required.
#endif
#ifndef OutputDir
  #error OutputDir is required.
#endif
#ifndef VbArchiveHash
  #error Verified driver hashes are required.
#endif

[Setup]
AppId={code:ApplicationId}
AppName=VineMix
AppVersion={#DisplayVersion}
AppPublisher=acoyeen
AppComments=Local Windows audio mixing with a node workspace
DefaultDirName={localappdata}\Programs\VineMix
UsePreviousAppDir=yes
DefaultGroupName=VineMix
DisableProgramGroupPage=yes
DisableDirPage=no
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
SetupArchitecture=x64
MinVersion=10.0.22000
WizardStyle=modern dynamic
WizardSizePercent=120,140
SetupIconFile=..\desktop\VineMix.Desktop\Assets\Branding\app.ico
UninstallDisplayIcon={app}\VineMix.exe
LicenseFile=TERMS.txt
OutputDir={#OutputDir}
OutputBaseFilename=VineMix-{#AppVersion}-Setup-x64
Compression=lzma2/fast
SolidCompression=yes
ArchiveExtraction=full
CloseApplications=no
RestartApplications=no
SetupMutex=Local\VineMix.Setup
UsePreviousLanguage=no
UninstallDisplayName=VineMix
VersionInfoProductName=VineMix
VersionInfoProductVersion={#NumericVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoVersion={#NumericVersion}
VersionInfoCopyright=Copyright (c) 2026 acoyeen

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked; Check: NotTestMode

[Files]
; Early extraction keeps driver checks fast even with solid compression.
Source: "{#PayloadDir}\SetupSupport\VineMix.SetupHelper.exe"; Flags: dontcopy
Source: "{#DriverArchiveDir}\VBCABLE_Driver_Pack45.zip"; Flags: dontcopy
Source: "{#DriverArchiveDir}\vac471lite.zip"; Flags: dontcopy
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\VineMix"; Filename: "{app}\VineMix.exe"; WorkingDir: "{app}"; AppUserModelID: "acoyeen.VineMix"; Check: NotTestMode
Name: "{group}\Installation help"; Filename: "{app}\SetupSupport\INSTALLATION.txt"; Check: NotTestMode
Name: "{group}\Uninstall VineMix"; Filename: "{uninstallexe}"; Check: NotTestMode
Name: "{autodesktop}\VineMix"; Filename: "{app}\VineMix.exe"; WorkingDir: "{app}"; Tasks: desktopicon; Check: NotTestMode

[INI]
Filename: "{app}\installation.ini"; Section: "Installation"; Key: "Version"; String: "{#NumericVersion}"; Flags: uninsdeleteentry uninsdeletesectionifempty
Filename: "{app}\installation.ini"; Section: "Installation"; Key: "ApplicationId"; String: "{code:ApplicationId}"; Flags: uninsdeleteentry uninsdeletesectionifempty

[InstallDelete]
; Retire only binaries owned by the previous AppId after the process guard passed.
Type: files; Name: "{app}\AudioMixer.exe"
Type: files; Name: "{app}\AudioMixer.Engine.exe"

[UninstallDelete]
Type: files; Name: "{app}\installation.ini"
Type: files; Name: "{app}\SetupSupport\driver-install-result.txt"

[Run]
Filename: "{app}\VineMix.exe"; WorkingDir: "{app}"; Description: "Open VineMix"; Flags: nowait postinstall skipifsilent; Check: CanLaunch

[Code]
var
  DriversPage, DriverTermsPage: TWizardPage;
  VbCheck, VacCheck, DriverTermsCheck: TNewCheckBox;
  AppOnlyText, InputNotice, DriverHelp, StatusText, DriverNotice: TNewStaticText;
  RefreshButton, VbTermsButton, VacTermsButton: TNewButton;
  DetectionFile, HelperFile, DriverResults: String;
  IsTest, DetectionReady, VbInstalled, VacInstalled, VbKnown, VacKnown: Boolean;
  AppRunning, TargetRunning, ProcessesKnown, DestinationSafe, DriverRestart: Boolean;
  DetectionSequence: Integer;

function TestMode: Boolean;
begin
  Result := ExpandConstant('{param:TESTMODE|0}') = '1';
end;

function NotTestMode: Boolean;
begin
  Result := not TestMode;
end;

function ApplicationId(Param: String): String;
begin
  if TestMode then Result := '119d9882-c04b-4825-b61a-d50798c13ed8'
  else Result := 'bac29135-ce09-4029-af38-d22d1e177d4f';
end;

function Quote(const S: String): String;
begin
  Result := '"' + S + '"';
end;

function BoolIni(const Section, Key: String): Boolean;
begin
  Result := GetIniString(Section, Key, '0', DetectionFile) = '1';
end;

function ValidBoolIni(const Section, Key, FileName: String): Boolean;
var Value: String;
begin
  Value := GetIniString(Section, Key, '', FileName);
  Result := (Value = '0') or (Value = '1');
end;

function RefreshDetection: Boolean;
var
  Code: Integer;
  Params, TargetDirectory: String;
begin
  Result := False;
  AppRunning := True; TargetRunning := True; ProcessesKnown := False; DestinationSafe := False;
  VbKnown := False; VacKnown := False; VbInstalled := False; VacInstalled := False;
  if not FileExists(HelperFile) then Exit;
  DetectionSequence := DetectionSequence + 1;
  DetectionFile := ExpandConstant('{tmp}\driver-status-') + IntToStr(DetectionSequence) + '.ini';
  if FileExists(DetectionFile) then Exit;
  Params := 'detect --output ' + Quote(DetectionFile);
  // Inno initializes the application constant after InitializeWizard; the edit can be empty here.
  TargetDirectory := WizardForm.DirEdit.Text;
  if TargetDirectory <> '' then Params := Params + ' --install-dir ' + Quote(TargetDirectory);
  if Exec(HelperFile, Params,
      '', SW_HIDE, ewWaitUntilTerminated, Code) and ((Code = 0) or (Code = 2)) and FileExists(DetectionFile) then begin
    VbKnown := BoolIni('VBCable', 'DetectSucceeded') and ValidBoolIni('VBCable', 'Installed', DetectionFile);
    VacKnown := BoolIni('VAC', 'DetectSucceeded') and ValidBoolIni('VAC', 'Installed', DetectionFile);
    VbInstalled := BoolIni('VBCable', 'Installed');
    VacInstalled := BoolIni('VAC', 'Installed');
    ProcessesKnown := BoolIni('Application', 'DetectSucceeded') and
      ValidBoolIni('Application', 'Running', DetectionFile) and ValidBoolIni('Application', 'RunningInstalled', DetectionFile);
    if ProcessesKnown then begin
      AppRunning := BoolIni('Application', 'Running');
      TargetRunning := BoolIni('Application', 'RunningInstalled');
    end;
    DestinationSafe := BoolIni('Application', 'InstallPathSafe');
    Result := ProcessesKnown;
    Log(Format('Driver detection: VB installed=%d known=%d; VAC installed=%d known=%d; application running=%d; target running=%d', [
      Ord(VbInstalled), Ord(VbKnown), Ord(VacInstalled), Ord(VacKnown), Ord(AppRunning), Ord(TargetRunning)]));
  end;
end;

function DriverStatus(const Section: String; Known, Installed: Boolean): String;
begin
  if not Known then Result := 'Could not check safely - automatic installation is disabled'
  else if Installed then begin
    Result := 'Already installed';
    if GetIniString(Section, 'State', '', DetectionFile) <> 'DevicePresent' then
      Result := Result + ' - may need enabling, repair or a restart';
    if GetIniString(Section, 'Version', '', DetectionFile) <> '' then
      Result := Result + ' (' + GetIniString(Section, 'Version', '', DetectionFile) + ')';
  end else Result := 'Not installed';
end;

procedure UpdateDriverPage;
begin
  StatusText.Caption := 'VB-CABLE: ' + DriverStatus('VBCable', VbKnown, VbInstalled) + #13#10 +
    'VAC Lite: ' + DriverStatus('VAC', VacKnown, VacInstalled);
  StatusText.AdjustHeight;
  VbCheck.Enabled := VbKnown and not VbInstalled and not IsTest;
  VacCheck.Enabled := VacKnown and not VacInstalled and not IsTest;
  if not VbCheck.Enabled then VbCheck.Checked := False;
  if not VacCheck.Enabled then VacCheck.Checked := False;
  DriverTermsCheck.Enabled := VbCheck.Checked or VacCheck.Checked;
  if not DriverTermsCheck.Enabled then DriverTermsCheck.Checked := False;
end;

procedure RefreshClicked(Sender: TObject);
begin
  RefreshDetection;
  UpdateDriverPage;
end;

procedure DriverSelectionChanged(Sender: TObject);
begin
  { Consent belongs to the currently selected packages, not an earlier choice. }
  DriverTermsCheck.Checked := False;
  UpdateDriverPage;
end;

procedure OpenVbTerms(Sender: TObject);
var Code: Integer;
begin
  ShellExec('open', 'https://vb-audio.com/Services/licensing.htm', '', '', SW_SHOWNORMAL, ewNoWait, Code);
end;

procedure OpenVacTerms(Sender: TObject);
var Code: Integer;
begin
  ShellExec('open', ExpandConstant('{tmp}\vac-license.txt'), '', '', SW_SHOWNORMAL, ewNoWait, Code);
end;

function InitializeSetup: Boolean;
begin
  Result := False;
  IsTest := TestMode;
  if WizardSilent and (ExpandConstant('{param:ACCEPTTERMS|0}') <> '1') then begin
    Log('TERMS_NOT_ACCEPTED: silent setup requires /ACCEPTTERMS=1.');
    Exit;
  end;
  if WizardSilent and (Lowercase(ExpandConstant('{param:DRIVERS|none}')) <> 'none') then begin
    Log('Silent driver installation is not supported. Run interactively to review vendor terms.');
    Exit;
  end;
  if IsTest and ((ExpandConstant('{param:DIR|}') = '') or
      (Lowercase(ExpandConstant('{param:DRIVERS|none}')) <> 'none')) then begin
    Log('TESTMODE requires an explicit /DIR and /DRIVERS=none.');
    Exit;
  end;
  Result := True;
end;

procedure InitializeWizard;
begin
  WizardForm.ReadyMemo.WordWrap := True;
  WizardForm.ReadyMemo.ScrollBars := ssVertical;
  ExtractTemporaryFile('VineMix.SetupHelper.exe');
  HelperFile := ExpandConstant('{tmp}\VineMix.SetupHelper.exe');
  DetectionFile := ExpandConstant('{tmp}\driver-status.ini');
  ExtractTemporaryFile('vac471lite.zip');
  if CompareText(GetSHA256OfFile(ExpandConstant('{tmp}\vac471lite.zip')), '{#VacArchiveHash}') <> 0 then
    RaiseException('The VAC Lite package failed its integrity check.');
  { Preserve the original ZIP and all its files. Extract only to Setup's private temp tree. }
  ExtractArchive(ExpandConstant('{tmp}\vac471lite.zip'), ExpandConstant('{tmp}\drivers\vac-lite'), '', True, nil);
  FileCopy(ExpandConstant('{tmp}\drivers\vac-lite\license.txt'), ExpandConstant('{tmp}\vac-license.txt'), False);

  DriversPage := CreateCustomPage(wpSelectTasks, 'Choose optional audio features',
    'VineMix works with your existing microphones and listening devices.');
  AppOnlyText := TNewStaticText.Create(DriversPage);
  AppOnlyText.Parent := DriversPage.Surface;
  AppOnlyText.SetBounds(0, 0, DriversPage.SurfaceWidth, ScaleY(36));
  AppOnlyText.AutoSize := False;
  AppOnlyText.WordWrap := True;
  AppOnlyText.Font.Style := [fsBold];
  AppOnlyText.Caption := 'Install VineMix only: leave both options clear and click Next.';
  AppOnlyText.AdjustHeight;
  InputNotice := TNewStaticText.Create(DriversPage);
  InputNotice.Parent := DriversPage.Surface;
  InputNotice.SetBounds(0, AppOnlyText.Top + AppOnlyText.Height + ScaleY(8), DriversPage.SurfaceWidth, ScaleY(44));
  InputNotice.AutoSize := False;
  InputNotice.WordWrap := True;
  InputNotice.Caption := 'Using a headset or Virtual Desktop microphone? Connect it first, then choose its Windows input in VineMix. Neither optional driver below is required.';
  InputNotice.AdjustHeight;
  VbCheck := TNewCheckBox.Create(DriversPage);
  VbCheck.Parent := DriversPage.Surface;
  VbCheck.SetBounds(0, InputNotice.Top + InputNotice.Height + ScaleY(18), DriversPage.SurfaceWidth, ScaleY(22));
  VbCheck.Caption := 'Optional: audio from other apps (VB-CABLE)';
  VbCheck.Checked := False;
  VbCheck.OnClick := @DriverSelectionChanged;
  VacCheck := TNewCheckBox.Create(DriversPage);
  VacCheck.Parent := DriversPage.Surface;
  VacCheck.SetBounds(0, VbCheck.Top + VbCheck.Height + ScaleY(10), DriversPage.SurfaceWidth, ScaleY(22));
  VacCheck.Caption := 'Optional: your mix as an app microphone (VAC Lite)';
  VacCheck.Checked := False;
  VacCheck.OnClick := @DriverSelectionChanged;
  DriverHelp := TNewStaticText.Create(DriversPage);
  DriverHelp.Parent := DriversPage.Surface;
  DriverHelp.SetBounds(0, VacCheck.Top + VacCheck.Height + ScaleY(12), DriversPage.SurfaceWidth, ScaleY(42));
  DriverHelp.AutoSize := False;
  DriverHelp.WordWrap := True;
  DriverHelp.Caption := 'Choose only what you need. Drivers require separate terms, administrator approval and a restart. You can add them later by running Setup again.';
  DriverHelp.AdjustHeight;
  StatusText := TNewStaticText.Create(DriversPage);
  StatusText.Parent := DriversPage.Surface;
  StatusText.AutoSize := False;
  StatusText.WordWrap := True;
  RefreshButton := TNewButton.Create(DriversPage);
  RefreshButton.Parent := DriversPage.Surface;
  RefreshButton.SetBounds(0, DriversPage.SurfaceHeight - ScaleY(33), ScaleX(100), ScaleY(25));
  RefreshButton.Caption := 'Check again';
  RefreshButton.OnClick := @RefreshClicked;
  StatusText.SetBounds(0, DriverHelp.Top + DriverHelp.Height + ScaleY(10), DriversPage.SurfaceWidth,
    RefreshButton.Top - (DriverHelp.Top + DriverHelp.Height) - ScaleY(18));

  DriverTermsPage := CreateCustomPage(DriversPage.ID, 'Review selected driver terms',
    'Only the optional features you checked will be installed.');
  DriverNotice := TNewStaticText.Create(DriverTermsPage);
  DriverNotice.Parent := DriverTermsPage.Surface;
  DriverNotice.SetBounds(0, 0, DriverTermsPage.SurfaceWidth, ScaleY(122));
  DriverNotice.AutoSize := False;
  DriverNotice.WordWrap := True;
  DriverNotice.Caption := 'VB-CABLE by VB-Audio receives other apps into VineMix''s Virtual input. It is donationware; contributions are welcome.' + #13#10#13#10 +
    'VAC Lite sends VineMix''s Virtual output to another app''s microphone input. Its licence permits private home use unrelated to earning income.' + #13#10#13#10 +
    'Selected drivers open their original setup windows and require administrator approval. Restart Windows before using a newly installed driver.';
  DriverNotice.AdjustHeight;
  VbTermsButton := TNewButton.Create(DriverTermsPage);
  VbTermsButton.Parent := DriverTermsPage.Surface;
  VbTermsButton.SetBounds(0, DriverNotice.Top + DriverNotice.Height + ScaleY(14), ScaleX(155), ScaleY(25));
  VbTermsButton.Caption := 'VB-CABLE terms / donate';
  VbTermsButton.OnClick := @OpenVbTerms;
  VacTermsButton := TNewButton.Create(DriverTermsPage);
  VacTermsButton.Parent := DriverTermsPage.Surface;
  VacTermsButton.SetBounds(ScaleX(165), VbTermsButton.Top, ScaleX(135), ScaleY(25));
  VacTermsButton.Caption := 'Read VAC licence';
  VacTermsButton.OnClick := @OpenVacTerms;
  DriverTermsCheck := TNewCheckBox.Create(DriverTermsPage);
  DriverTermsCheck.Parent := DriverTermsPage.Surface;
  DriverTermsCheck.SetBounds(0, VbTermsButton.Top + VbTermsButton.Height + ScaleY(18), DriverTermsPage.SurfaceWidth, ScaleY(24));
  DriverTermsCheck.Caption := 'I accept these driver terms and their use limits.';
  RefreshDetection;
  { Drivers are always an explicit interactive opt-in, including on upgrade.
    Detection and command-line values never select missing packages for a user. }
  UpdateDriverPage;
  Log(Format('DRIVER_CHOICES_OPT_IN: VB=%d VAC=%d terms=%d; application-only setup is the default.', [
    Ord(VbCheck.Checked), Ord(VacCheck.Checked), Ord(DriverTermsCheck.Checked)]));
  Log(Format('DRIVER_PAGE_LAYOUT: surface=%dx%d; options bottom=%d; status top=%d height=%d; refresh top=%d bottom=%d; terms surface=%dx%d bottom=%d', [
    DriversPage.SurfaceWidth, DriversPage.SurfaceHeight, DriverHelp.Top + DriverHelp.Height,
    StatusText.Top, StatusText.Height, RefreshButton.Top, RefreshButton.Top + RefreshButton.Height,
    DriverTermsPage.SurfaceWidth, DriverTermsPage.SurfaceHeight, DriverTermsCheck.Top + DriverTermsCheck.Height]));
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = DriverTermsPage.ID) and not (VbCheck.Checked or VacCheck.Checked);
end;

function SafeDestination: Boolean;
var Folder, DataFolder, LegacyDataFolder, Root: String;
begin
  Folder := AddBackslash(ExpandFileName(ExpandConstant('{app}')));
  DataFolder := AddBackslash(ExpandFileName(ExpandConstant('{localappdata}\VineMix')));
  LegacyDataFolder := AddBackslash(ExpandFileName(ExpandConstant('{localappdata}\AudioMixer')));
  Root := AddBackslash(ExtractFileDrive(Folder));
  Result := (CompareText(Folder, Root) <> 0) and
    (Pos(Lowercase(DataFolder), Lowercase(Folder)) <> 1) and
    (Pos(Lowercase(LegacyDataFolder), Lowercase(Folder)) <> 1) and
    (CompareText(Folder, AddBackslash(GetEnv('USERPROFILE'))) <> 0) and
    (CompareText(Folder, AddBackslash(ExpandConstant('{localappdata}'))) <> 0) and
    (CompareText(Folder, AddBackslash(ExpandConstant('{userdocs}'))) <> 0) and
    (CompareText(Folder, AddBackslash(ExpandConstant('{win}'))) <> 0) and
    (CompareText(Folder, AddBackslash(ExpandConstant('{sys}'))) <> 0);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectDir) and not SafeDestination then begin
    SuppressibleMsgBox('Choose a dedicated application folder. Your saved-data folder and system roots cannot be used.', mbError, MB_OK, IDOK);
    Result := False;
  end;
  if (CurPageID = DriverTermsPage.ID) and (VbCheck.Checked or VacCheck.Checked) and not DriverTermsCheck.Checked then begin
    SuppressibleMsgBox('Read and accept the selected drivers'' terms, or go Back and clear their checkboxes to install VineMix only.', mbInformation, MB_OK, IDOK);
    Result := False;
  end;
end;

function CompareVersion(A, B: String): Integer;
var I, P, Left, Right: Integer; Part: String;
begin
  Result := 0;
  for I := 1 to 4 do begin
    P := Pos('.', A); if P = 0 then P := Length(A) + 1;
    Part := Copy(A, 1, P - 1); Left := StrToIntDef(Part, 0); Delete(A, 1, P);
    P := Pos('.', B); if P = 0 then P := Length(B) + 1;
    Part := Copy(B, 1, P - 1); Right := StrToIntDef(Part, 0); Delete(B, 1, P);
    if Left < Right then begin Result := -1; Exit; end;
    if Left > Right then begin Result := 1; Exit; end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExistingVersion: String;
begin
  Result := '';
  if (VbCheck.Checked or VacCheck.Checked) and not DriverTermsCheck.Checked then begin
    Result := 'Accept the selected driver terms, or go Back and clear the optional driver checkboxes.'; Exit;
  end;
  if not SafeDestination then begin Result := 'Choose a dedicated application folder.'; Exit; end;
  if not RefreshDetection then begin
    Result := 'Setup could not safely check running VineMix processes. Close other setup windows and try again.'; Exit;
  end;
  if not DestinationSafe then begin
    Result := 'Choose a dedicated application folder outside your saved-data folder and system roots.'; Exit;
  end;
  if TargetRunning then begin
    Log('TARGET_RUNNING: refusing to replace files used by the running target.');
    Result := 'VineMix is running from the selected installation folder. Save your work and choose Quit VineMix from its tray menu, then retry.'; Exit;
  end;
  if AppRunning and (VbCheck.Checked or VacCheck.Checked) then begin
    Result := 'Quit the running VineMix from its tray menu before installing audio drivers. You can also go back and deselect driver installation.'; Exit;
  end;
  ExistingVersion := GetIniString('Installation', 'Version', '', ExpandConstant('{app}\installation.ini'));
  if (ExistingVersion <> '') and (CompareVersion(ExistingVersion, '{#NumericVersion}') > 0) then begin
    Log('DOWNGRADE_BLOCKED: installed version is newer than this package.');
    Result := 'A newer version of VineMix is installed here. Use its installer rather than downgrading.';
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine + 'Saved workspaces and preferences:' + NewLine +
    Space + ExpandConstant('{localappdata}\VineMix') + NewLine +
    Space + 'Existing files are retained, including on uninstall.' + NewLine + NewLine + 'Optional virtual audio drivers:';
  if not VbCheck.Checked and not VacCheck.Checked then
    Result := Result + NewLine + Space + 'Install VineMix only. No additional audio drivers will be installed.';
  if VbCheck.Checked then Result := Result + NewLine + Space + 'Install original VB-CABLE package'
  else Result := Result + NewLine + Space + 'VB-CABLE: keep current installation / skip';
  if VacCheck.Checked then Result := Result + NewLine + Space + 'Install original VAC Lite package'
  else Result := Result + NewLine + Space + 'VAC: keep current installation / skip';
  if AppRunning then Result := Result + NewLine + NewLine + 'A copy of VineMix is already running. Setup will leave it running and will not open another copy.';
  if MemoTasksInfo <> '' then Result := Result + NewLine + NewLine + MemoTasksInfo;
end;

procedure InstallDriver(const Section, DisplayName, Archive, ExpectedHash, Subfolder, SetupExe: String);
var Code: Integer; Launched, WasInstalled: Boolean; Directory: String;
begin
  if IsTest or WizardSilent then RaiseException('Driver installation requires interactive setup.');
  RefreshDetection;
  WasInstalled := BoolIni(Section, 'Installed');
  if WasInstalled then begin Log(DisplayName + ' already registered; skipping.'); Exit; end;
  if not BoolIni(Section, 'DetectSucceeded') or not ProcessesKnown or AppRunning then begin
    DriverResults := DriverResults + #13#10 + DisplayName + ': skipped because installation could not be checked safely.';
    Exit;
  end;
  ExtractTemporaryFile(Archive);
  if CompareText(GetSHA256OfFile(ExpandConstant('{tmp}\') + Archive), ExpectedHash) <> 0 then
    RaiseException(DisplayName + ' package integrity check failed.');
  Directory := ExpandConstant('{tmp}\drivers\') + Subfolder;
  ExtractArchive(ExpandConstant('{tmp}\') + Archive, Directory, '', True, nil);
  WizardForm.StatusLabel.Caption := 'Complete the ' + DisplayName + ' installation window to continue.';
  Launched := ShellExec('runas', Directory + '\' + SetupExe, '', Directory, SW_SHOWNORMAL, ewWaitUntilTerminated, Code);
  Log(Format('%s installer launch=%d, result=%d', [DisplayName, Ord(Launched), Code]));
  RefreshDetection;
  if Launched and ((Code = 0) or (Code = 3010) or (Code = 1641)) and BoolIni(Section, 'Installed') then begin
    DriverResults := DriverResults + #13#10 + DisplayName + ': installed. Restart Windows before using the new driver.';
    DriverRestart := True;
  end else begin
    DriverResults := DriverResults + #13#10 + DisplayName + ': installation was cancelled, failed, or could not yet be confirmed. Run Setup again to retry.';
    if (Code = 3010) or (Code = 1641) then DriverRestart := True;
  end;
end;

procedure MigrateOwnedStartup;
var
  RunKey, ApprovalKey, OldCommand, NewCommand, Existing, Confirm: String;
  Approval, CurrentApproval: AnsiString;
  HadApproval, NewEntryExists: Boolean;
begin
  if IsTest then Exit;
  RunKey := 'Software\Microsoft\Windows\CurrentVersion\Run';
  ApprovalKey := 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run';
  OldCommand := Quote(ExpandConstant('{app}\AudioMixer.exe')) + ' --background';
  NewCommand := Quote(ExpandConstant('{app}\VineMix.exe')) + ' --background';
  if not RegQueryStringValue(HKCU, RunKey, 'AudioMixer', Existing) or
      (CompareText(Existing, OldCommand) <> 0) then Exit;
  NewEntryExists := RegValueExists(HKCU, RunKey, 'VineMix');
  if NewEntryExists then begin
    if not RegQueryStringValue(HKCU, RunKey, 'VineMix', Existing) or
        (CompareText(Existing, NewCommand) <> 0) then begin
      Log('STARTUP_MIGRATION_SKIPPED: another VineMix startup entry is already owned elsewhere.'); Exit;
    end;
    { The current entry already expresses the user's current choice. }
  end else begin
    HadApproval := RegValueExists(HKCU, ApprovalKey, 'AudioMixer');
    if HadApproval and not RegQueryBinaryValue(HKCU, ApprovalKey, 'AudioMixer', Approval) then begin
      Log('STARTUP_MIGRATION_SKIPPED: legacy startup approval state could not be preserved.'); Exit;
    end;
    if RegValueExists(HKCU, ApprovalKey, 'VineMix') then begin
      Log('STARTUP_MIGRATION_SKIPPED: an existing VineMix startup approval choice is retained.'); Exit;
    end;
    { Copy Windows' state verbatim; do not decode or reset disabled choices. }
    if HadApproval then begin
      if not RegWriteBinaryValue(HKCU, ApprovalKey, 'VineMix', Approval) or
          not RegQueryBinaryValue(HKCU, ApprovalKey, 'VineMix', CurrentApproval) or
          (CurrentApproval <> Approval) then begin
        Log('STARTUP_MIGRATION_SKIPPED: startup approval state could not be copied safely.'); Exit;
      end;
    end;
    if not RegWriteStringValue(HKCU, RunKey, 'VineMix', NewCommand) then begin
      Log('STARTUP_MIGRATION_SKIPPED: the current startup entry could not be written.'); Exit;
    end;
  end;
  if RegQueryStringValue(HKCU, RunKey, 'VineMix', Confirm) and
      (CompareText(Confirm, NewCommand) = 0) and
      RegQueryStringValue(HKCU, RunKey, 'AudioMixer', Existing) and
      (CompareText(Existing, OldCommand) = 0) then begin
    RegDeleteValue(HKCU, RunKey, 'AudioMixer');
    Log('STARTUP_MIGRATED: only the legacy startup command owned by this upgraded folder was moved.');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    MigrateOwnedStartup;
    DriverResults := '';
    if not VbCheck.Checked and not VacCheck.Checked then Log('DRIVER_INSTALLATION_SKIPPED');
    if VbCheck.Checked then InstallDriver('VBCable', 'VB-CABLE', 'VBCABLE_Driver_Pack45.zip', '{#VbArchiveHash}', 'vb-cable', 'VBCABLE_Setup_x64.exe');
    if VacCheck.Checked then InstallDriver('VAC', 'VAC Lite', 'vac471lite.zip', '{#VacArchiveHash}', 'vac-lite', 'setup64.exe');
    SaveStringToFile(ExpandConstant('{app}\SetupSupport\driver-install-result.txt'),
      'VineMix Setup '#13#10 + DriverResults + #13#10 + 'Use Setup to check or install missing drivers. Existing drivers are never uninstalled with VineMix.', False);
  end;
end;

function NeedRestart: Boolean;
begin
  Result := DriverRestart;
end;

function CanLaunch: Boolean;
begin
  Result := False;
  if TestMode or DriverRestart then Exit;
  if RefreshDetection then Result := not AppRunning;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = DriversPage.ID then begin RefreshDetection; UpdateDriverPage; end;
  if CurPageID = wpFinished then begin
    if AppRunning then WizardForm.FinishedLabel.Caption :=
      'VineMix is installed. Your existing copy is still running. Quit it from the tray before opening the installed copy. Your saved workspaces and preferences are shared and retained.';
    if DriverResults <> '' then WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10 + DriverResults;
  end;
end;

function InitializeUninstall: Boolean;
var Code: Integer; StatusFile, UninstallHelper: String;
begin
  Result := False;
  UninstallHelper := ExpandConstant('{app}\SetupSupport\VineMix.SetupHelper.exe');
  StatusFile := ExpandConstant('{tmp}\vinemix-uninstall-status.ini');
  if FileExists(StatusFile) and not DeleteFile(StatusFile) then Exit;
  if not FileExists(UninstallHelper) then begin
    SuppressibleMsgBox('Setup support is missing. Reinstall VineMix in this folder to repair it before uninstalling.', mbError, MB_OK, IDOK); Exit;
  end;
  if not Exec(UninstallHelper, 'detect --output ' + Quote(StatusFile) + ' --install-dir ' + Quote(ExpandConstant('{app}')),
      '', SW_HIDE, ewWaitUntilTerminated, Code) then Exit;
  if ((Code <> 0) and (Code <> 2)) or not FileExists(StatusFile) then Exit;
  if GetIniString('Application', 'DetectSucceeded', '0', StatusFile) <> '1' then Exit;
  if GetIniString('Application', 'InstallPathSafe', '0', StatusFile) <> '1' then Exit;
  if not ValidBoolIni('Application', 'RunningInstalled', StatusFile) then Exit;
  if GetIniString('Application', 'RunningInstalled', '1', StatusFile) = '1' then begin
    Log('UNINSTALL_TARGET_RUNNING: refusing to remove files used by the running target.');
    SuppressibleMsgBox('Save your work and choose Quit VineMix from its tray menu before uninstalling this copy.', mbInformation, MB_OK, IDOK); Exit;
  end;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var StartupValue: String;
begin
  if (CurUninstallStep = usUninstall) and not TestMode then begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'VineMix', StartupValue) and
      (CompareText(StartupValue, Quote(ExpandConstant('{app}\VineMix.exe')) + ' --background') = 0) then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'VineMix');
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AudioMixer', StartupValue) and
      (CompareText(StartupValue, Quote(ExpandConstant('{app}\AudioMixer.exe')) + ' --background') = 0) then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AudioMixer');
  end;
end;
