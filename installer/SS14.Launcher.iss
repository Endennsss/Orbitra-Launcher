#define AppName "Orbitra Launcher"
#define AppVersion "0.41.1"
#define AppPublisher "ChemHelper"
#define AppExeName "Orbitra Launcher.exe"

[Setup]
AppId={{A70F856B-1BF8-49DC-86A5-153281704560}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Установщик {#AppName}
DefaultDirName={localappdata}\Programs\Orbitra Launcher
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\bin\installer
OutputBaseFilename=Orbitra.Launcher.Setup.x64
SetupIconFile=..\SS14.Launcher\Assets\icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dark hidebevels
WizardBackColor=#101010
WizardBackColorDynamicDark=#101010
WizardSizePercent=115
DisableWelcomePage=no
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
ShowLanguageDialog=auto
ChangesAssociations=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные ярлыки:"; Flags: unchecked

[Files]
Source: "..\bin\publish\Windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{group}\Удалить {#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Запустить {#AppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
procedure InitializeWizard;
begin
  WizardForm.Color := $101010;
  WizardForm.WelcomeLabel1.Caption := 'ORBITRA LAUNCHER';
  WizardForm.WelcomeLabel2.Caption :=
    'Установщик подготовит лаунчер к работе и создаст необходимые ярлыки.' + #13#10 + #13#10 +
    'Настройки, аккаунты и избранные серверы сохраняются отдельно и не удаляются при обновлении.';
end;
