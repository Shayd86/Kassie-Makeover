#ifndef MyAppVersion
  #define MyAppVersion "0.3.0"
#endif
#define MyAppName "Kassie Makeover"
#define MyAppExeName "Kassie-Makeover.exe"

[Setup]
AppId={{B0EBAF87-B784-44A9-86A0-48F4894503AC}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Kassie
DefaultDirName=F:\Kassie Makeover
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=Kassie-Makeover-Setup-v{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
SetupIconFile=..\src\Kassie.Makeover\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
WizardStyle=modern

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "D:\Kassie\Makeover"
Name: "D:\Kassie\Makeover\models"
Name: "D:\Kassie\Makeover\runtimes"
Name: "D:\Kassie\Makeover\cache"
Name: "D:\Kassie\Makeover\temp"
Name: "D:\Kassie\Makeover\outputs"
Name: "D:\Kassie\Makeover\exports"
Name: "D:\Kassie\Makeover\wardrobe"
Name: "D:\Kassie\Makeover\config"
Name: "D:\Kassie\Makeover\logs"

[Icons]
Name: "{autoprograms}\Kassie Makeover"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Kassie Makeover"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Kassie Makeover"; Flags: nowait postinstall skipifsilent
