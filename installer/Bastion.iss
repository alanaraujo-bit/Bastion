; Bastion — Inno Setup 6 installer
; Build:  iscc installer\Bastion.iss   (after running tools\publish.ps1)

#define AppName "Bastion"
#define AppVersion "1.0.0"
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
const
  ATTR_DIRECTORY = $10;

// Returns True if any Microsoft.WindowsDesktop.App 8.x shared framework is present.
function DotNetDesktop8Present(): Boolean;
var
  rec: TFindRec;
  base: String;
  found: Boolean;
begin
  found := False;
  base := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if DirExists(base) then
  begin
    if FindFirst(base + '\8.*', rec) then
    try
      repeat
        if (rec.Attributes and ATTR_DIRECTORY) <> 0 then
          if (rec.Name <> '.') and (rec.Name <> '..') then
            found := True;
      until not FindNext(rec);
    finally
      FindClose(rec);
    end;
  end;
  Result := found;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not DotNetDesktop8Present() then
  begin
    if MsgBox('Bastion needs the .NET Desktop Runtime 8 (x64), which was not found.'
      + #13#10#13#10 + 'Install it from https://dotnet.microsoft.com/download/dotnet/8.0 (Desktop Runtime), then run this setup again.'
      + #13#10#13#10 + 'Continue anyway?', mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;

procedure RunSc(Params: String);
var
  code: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, code);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  bin: String;
begin
  if CurStep = ssPostInstall then
  begin
    bin := ExpandConstant('{app}\Bastion.Service.exe');
    // Remove any prior instance, then (re)create the service as LocalSystem, auto-start.
    RunSc('stop {#ServiceName}');
    RunSc('delete {#ServiceName}');
    RunSc('create {#ServiceName} binPath= "\"' + bin + '\"" start= auto obj= LocalSystem DisplayName= "{#ServiceDisplay}"');
    RunSc('description {#ServiceName} "Enforces Bastion application protection. Stopping this service leaves protected apps locked."');
    // Restart automatically if it ever crashes.
    RunSc('failure {#ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/10000');
    RunSc('start {#ServiceName}');
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
