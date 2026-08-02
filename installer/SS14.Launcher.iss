#define AppName "Orbitra Launcher"
#ifndef AppVersion
  #define AppVersion "0.41.1"
#endif
#define AppPublisher "Orbitra Launcher"
#define AppURL "https://endennsss.github.io/Orbitra-Launcher/"
#define AppSupportURL "https://github.com/Endennsss/Orbitra-Launcher/issues"
#define AppUpdatesURL "https://github.com/Endennsss/Orbitra-Launcher/releases/latest"
#define AppExeName "Orbitra Launcher.exe"

[Setup]
AppId={{A70F856B-1BF8-49DC-86A5-153281704560}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppSupportURL}
AppUpdatesURL={#AppUpdatesURL}
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
VersionInfoCopyright=Orbitra Launcher project
DefaultDirName={localappdata}\Programs\Orbitra Launcher
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\bin\installer
OutputBaseFilename=Orbitra_Launcher_Setup_x64
SetupIconFile=..\SS14.Launcher\Assets\icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
WizardImageFile=assets\orbitra-wizard.bmp
WizardSmallImageFile=assets\orbitra-small.bmp
WizardStyle=modern dark hidebevels
WizardBackColor=#101010
WizardBackColorDynamicDark=#101010
WizardSizePercent=115
DisableWelcomePage=no
DisableProgramGroupPage=auto
AllowNoIcons=yes
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
ShowLanguageDialog=yes
SetupLogging=yes
MinVersion=10.0.17763
ChangesEnvironment=no
ChangesAssociations=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.TypeFull=Полная установка
russian.TypeFullDesc=Установить Orbitra Launcher и рекомендуемые ярлыки.
russian.TypeCompact=Компактная установка
russian.TypeCompactDesc=Установить только файлы лаунчера.
russian.TypeCustom=Выборочная установка
russian.AdditionalIcons=Ярлыки и запуск:
russian.DesktopIcon=Создать ярлык на рабочем столе
russian.AutoStart=Запускать Orbitra вместе с Windows
russian.LaunchAfter=Запустить Orbitra Launcher
russian.WelcomeTitle=ДОБРО ПОЖАЛОВАТЬ В ORBITRA
russian.WelcomeText=Мастер установит Orbitra Launcher на этот компьютер.%n%nПри обновлении аккаунты, избранные серверы, темы и настройки останутся без изменений.
english.TypeFull=Full installation
english.TypeFullDesc=Install Orbitra Launcher with recommended shortcuts.
english.TypeCompact=Compact installation
english.TypeCompactDesc=Install launcher files only.
english.TypeCustom=Custom installation
english.AdditionalIcons=Shortcuts and startup:
english.DesktopIcon=Create a desktop shortcut
english.AutoStart=Start Orbitra with Windows
english.LaunchAfter=Launch Orbitra Launcher
english.WelcomeTitle=WELCOME TO ORBITRA
english.WelcomeText=Setup will install Orbitra Launcher on this computer.%n%nAccounts, favorite servers, themes and settings are preserved when updating.

[Types]
Name: "full"; Description: "{cm:TypeFull}"
Name: "compact"; Description: "{cm:TypeCompact}"
Name: "custom"; Description: "{cm:TypeCustom}"; Flags: iscustom

[Components]
Name: "launcher"; Description: "Orbitra Launcher"; Types: full compact custom; Flags: fixed

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\publish\Windows\*"; DestDir: "{app}"; Components: launcher; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Orbitra Launcher"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchAfter}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
procedure InitializeWizard;
begin
  WizardForm.Color := $101010;
  WizardForm.WelcomeLabel1.Caption := CustomMessage('WelcomeTitle');
  WizardForm.WelcomeLabel2.Caption := CustomMessage('WelcomeText');
  WizardForm.WelcomeLabel1.Font.Color := $F2F2F2;
  WizardForm.WelcomeLabel2.Font.Color := $A8A8A8;
end;
