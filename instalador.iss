; ============================================================================
;  Instalador de Coraza (Inno Setup 6)
;
;  Compila la carpeta publicar\Coraza en un unico CorazaInstalador.exe.
;  No se compila a mano: lo hace .\instalador.ps1, que primero publica la
;  aplicacion y despues invoca a ISCC con este guion.
;
;  Decisiones deliberadas:
;   - PrivilegesRequired=lowest  -> se instala para el usuario actual, asi que
;     Windows NO pide permisos de administrador. Es el momento en que mas gente
;     novata abandona una instalacion.
;   - MinVersion=10.0.19041      -> Coraza lee PDF con el motor de Windows, que
;     necesita Windows 10 2004 o superior. Mejor avisar aqui que fallar dentro.
;   - El desinstalador no toca Documentos\Coraza: las canciones, los servicios y
;     los respaldos del usuario sobreviven a desinstalar el programa.
; ============================================================================

#define Nombre      "Coraza"
#define Version     "1.1"
#define Autor       "John Elith"
#define Sitio       "https://john-elith.github.io/Coraza/"
#define Ejecutable  "Coraza.exe"

[Setup]
; Este AppId identifica al programa entre versiones. NO cambiarlo nunca:
; es lo que permite que una instalacion nueva reemplace a la anterior.
AppId={{B7C4E1A9-3F52-4D8B-9E06-2A7D5C81F4E3}
AppName={#Nombre}
AppVersion={#Version}
AppVerName={#Nombre} {#Version}
AppPublisher={#Autor}
AppPublisherURL={#Sitio}
AppSupportURL={#Sitio}
AppUpdatesURL={#Sitio}

DefaultDirName={autopf}\{#Nombre}
DefaultGroupName={#Nombre}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest

OutputDir=publicar
OutputBaseFilename=CorazaInstalador
SetupIconFile=src\Coraza.App\Recursos\coraza.ico
UninstallDisplayIcon={app}\{#Ejecutable}
UninstallDisplayName={#Nombre} · Proyeccion para iglesias

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; "x64compatible" en vez de "x64": incluye los Windows con ARM, que ejecutan
; aplicaciones x64 por emulacion. Coraza se publica como win-x64 autocontenido,
; asi que ahi tambien funciona.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el Escritorio"; GroupDescription: "Accesos directos:"

[Files]
; La carpeta completa que genero dotnet publish, incluidas Recursos\Biblias
; (el texto biblico y los avisos de licencia, que deben viajar con el programa).
Source: "publicar\Coraza\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#Nombre}";                Filename: "{app}\{#Ejecutable}"
Name: "{group}\Desinstalar {#Nombre}";    Filename: "{uninstallexe}"
Name: "{autodesktop}\{#Nombre}";          Filename: "{app}\{#Ejecutable}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#Ejecutable}"; Description: "Abrir Coraza ahora"; Flags: nowait postinstall skipifsilent
