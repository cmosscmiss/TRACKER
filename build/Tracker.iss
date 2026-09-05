; Instalador de TRACKER (Inno Setup 6).
;
; Empaqueta la carpeta de publish AUTOCONTENIDA (no hace falta .NET instalado) y, si el equipo no tiene el
; runtime de WebView2, lo instala antes con el bootstrapper Evergreen de Microsoft que va incluido.
;
; Compilar:  ISCC.exe build\Tracker.iss     (deja el .exe en Releases\<version>\)
; Requiere:  haber ejecutado antes build\build-release.ps1, que es quien deja la carpeta de publish COMPLETA
;            (el publish por si solo no incluye Assets, *.xbf ni Tracker.pri) y descarga el bootstrapper.

#define AppName        "TRACKER"
#define AppVersion     "1.0 RC 1"
#define AppPublisher   "Victor BLAZQUEZ (CMOSS)"
#define AppExeName     "Tracker.exe"
#define PublishDir     "..\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"
#define ReleaseDir     "..\Releases\v1.0-rc1"

[Setup]
AppId={{8E1F2A64-3B27-4C55-9E7D-1F6B0A2C4D31}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir={#ReleaseDir}
OutputBaseFilename=Tracker-v1.0-rc1-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; La app es x64: en un Windows de 32 bits no tiene sentido ofrecer la instalacion.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Requisito de la propia app (TargetPlatformMinVersion): Windows 10 1809.
MinVersion=10.0.17763
; Sin privilegios de administrador se instala en la carpeta del usuario; con ellos, en Archivos de programa.
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Toda la carpeta de publish (app + .NET + Windows App SDK).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Bootstrapper de WebView2: se copia a temporal y solo se ejecuta si falta el runtime (ver [Run] y [Code]).
Source: "..\Releases\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
; Notas para el usuario, junto a la app instalada.
Source: "LEEME.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; 1) Runtime de WebView2 (silencioso) solo si no esta ya presente.
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Instalando el runtime de WebView2..."; Check: NeedsWebView2; Flags: waituntilterminated
; 2) Arrancar la app al terminar (opcional para el usuario).
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Nada de datos de usuario: la BD, el .ini y el log viven en %LocalAppData%\Tracker y se conservan a proposito
; (asi una reinstalacion o una actualizacion no pierde los productos ni el historico de precios).

[Code]
{ El runtime Evergreen de WebView2 publica su version en el registro (por maquina, 64 y 32 bits, o por usuario).
  Si no aparece en ninguna de las tres claves, hay que instalarlo. }
function NeedsWebView2: Boolean;
var
  Version: String;
begin
  Result := True;

  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0') then
    Result := False
  else if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0') then
    Result := False
  else if RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0') then
    Result := False;
end;
