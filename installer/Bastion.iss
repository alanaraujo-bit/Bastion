; Bastion — Inno Setup 6 installer
; Build:  iscc installer\Bastion.iss   (after running tools\publish.ps1)

#define AppName "Bastion"
#define AppVersion "1.0.1"
#define AppPublisher "Bastion"
#define ServiceName "BastionProtection"
#define ServiceDisplay "Bastion Protection"

[Setup]
AppId={{7C2B9E14-3F5A-4E2B-9C1D-BA5710A0F001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\Bastion.App.exe
UninstallDisplayName={#AppName}
OutputDir=..\dist
OutputBaseFilename=BastionSetup-{#AppVersion}
SetupIconFile=..\assets\bastion.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Dirs]
; Shared, machine-wide config store: SYSTEM/Admins full control, standard users read-only.
Name: "{commonappdata}\{#AppName}"; Permissions: system-full admins-full users-readexec

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Bastion.App.exe"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Bastion.App.exe"; Tasks: desktopicon

[Run]
; Launch the console after install so the user can complete onboarding.
Filename: "{app}\Bastion.App.exe"; Description: "Open {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
// The app is published self-contained, so no .NET runtime check is needed.

function RunSc(Params: String): Integer;
var
  code: Integer;
begin
  if not Exec(ExpandConstant('{sys}\sc.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, code) then
    code := -1;
  Result := code;
end;

// True if `sc query` reports the service in the given state (e.g. 'RUNNING', 'STOPPED').
function ServiceInState(State: String): Boolean;
var
  code: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'),
    '/C ""' + ExpandConstant('{sys}\sc.exe') + '" query {#ServiceName} | "' + ExpandConstant('{sys}\find.exe') + '" "' + State + '""',
    '', SW_HIDE, ewWaitUntilTerminated, code) and (code = 0);
end;

// True if the service exists at all (sc query fails with 1060 otherwise).
function ServiceExists(): Boolean;
begin
  Result := RunSc('query {#ServiceName}') = 0;
end;

// Stops the service (if present) and waits for it to actually stop, so its
// files are unlocked before we overwrite them.
procedure StopServiceAndWait();
var
  i: Integer;
begin
  if not ServiceExists() then Exit;
  RunSc('stop {#ServiceName}');
  for i := 1 to 30 do
  begin
    if ServiceInState('STOPPED') then Exit;
    Sleep(500);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopServiceAndWait();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  bin: String;
  i, code: Integer;
  running: Boolean;
begin
  if CurStep = ssPostInstall then
  begin
    bin := ExpandConstant('{app}\Bastion.Service.exe');
    // Remove any prior instance, then (re)create the service as LocalSystem, auto-start.
    StopServiceAndWait();
    RunSc('delete {#ServiceName}');
    // A deleted service lingers ("marked for deletion", 1072) until its handles close; retry briefly.
    for i := 1 to 20 do
    begin
      code := RunSc('create {#ServiceName} binPath= "\"' + bin + '\"" start= auto obj= LocalSystem DisplayName= "{#ServiceDisplay}"');
      if code <> 1072 then Break;
      Sleep(500);
    end;
    RunSc('description {#ServiceName} "Enforces Bastion application protection. Stopping this service leaves protected apps locked."');
    // Restart automatically if it ever crashes.
    RunSc('failure {#ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/10000');
    RunSc('start {#ServiceName}');

    // Confirm it really came up instead of assuming it did.
    running := False;
    for i := 1 to 30 do
    begin
      if ServiceInState('RUNNING') then
      begin
        running := True;
        Break;
      end;
      Sleep(500);
    end;
    if not running then
      SuppressibleMsgBox('The Bastion protection service could not be started.'
        + #13#10#13#10 + 'Restart the PC and open Bastion again. If the problem continues, run this setup again.'
        + #13#10#13#10 + 'Details are logged in ' + ExpandConstant('{commonappdata}\{#AppName}\service.log') + '.',
        mbError, MB_OK, IDOK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  bin: String;
  code: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    bin := ExpandConstant('{app}\Bastion.Service.exe');
    // 1) stop the service so it can't re-apply hooks
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, code);
    // 2) remove every IFEO hook we created, so protected apps open normally again
    if FileExists(bin) then
      Exec(bin, '--cleanup-hooks', '', SW_HIDE, ewWaitUntilTerminated, code);
    // 3) delete the service
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, code);
  end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\{#AppName}"
