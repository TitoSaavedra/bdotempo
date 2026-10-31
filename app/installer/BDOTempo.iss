; BDOTempo - installer (Inno Setup 6).
; Built by app/scripts/package.ps1 (mise run package-installer). The defines below are passed on the ISCC command line.
; Framework-dependent app: the .NET 10 Desktop Runtime (x64) must be on the PC; the check is in InitializeSetup.
; The installer embeds the app (publish output) and the 17 asset parts; the post-install step assembles assets.bdoa
; with the app's own command and deletes the parts. See app/installer/README.md.

#ifndef AppVersion
  #error AppVersion is required (pass /DAppVersion=<SemVer>; mise run package-installer does it)
#endif
#ifndef PublishDir
  #error PublishDir is required (pass /DPublishDir=<framework-dependent publish folder>)
#endif
#ifndef PartsDir
  #error PartsDir is required (pass /DPartsDir=<folder with assets.partNN.bdoa>)
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

[Setup]
; Fixed for the life of the product: an update is recognised by this id. Do not change it.
AppId={{4B848DC4-80D6-4C79-8365-A79F29EF2A21}
AppName=BDOTempo
AppVersion={#AppVersion}
AppVerName=BDOTempo {#AppVersion}
AppPublisher=TitoSaavedra
AppPublisherURL=https://github.com/TitoSaavedra/bdotempo
AppSupportURL=https://github.com/TitoSaavedra/bdotempo/issues
AppUpdatesURL=https://github.com/TitoSaavedra/bdotempo/releases
DefaultDirName={autopf}\BDOTempo
DefaultGroupName=BDOTempo
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=BDOTempo-Setup-{#AppVersion}
; The setup wizard and the Apps list use the app icon (the same .ico the exe embeds).
SetupIconFile=..\assets\icon\app-icon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Per user by default (no administrator prompt); an administrator can choose the whole PC. An update keeps the previous choice.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes
UninstallDisplayName=BDOTempo
UninstallDisplayIcon={app}\BDOTempo.exe
; An update over a running copy closes it first (the updater also exits the app before starting this installer).
CloseApplications=yes
CloseApplicationsFilter=BDOTempo.exe
RestartApplications=yes
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
en.DotNetMissing=The .NET 10 Desktop Runtime (x64) is needed and was not found on this PC.%n%nYes: install it now with winget (Windows will ask for permission).%nNo: open the download page.%nCancel: stop the installation.
es.DotNetMissing=Se necesita el .NET 10 Desktop Runtime (x64) y no está instalado en este equipo.%n%nSí: instalarlo ahora con winget (Windows pedirá permiso).%nNo: abrir la página de descarga.%nCancelar: detener la instalación.
en.DotNetInstallFailed=The runtime was not installed. The download page will open.
es.DotNetInstallFailed=No se instaló el runtime. Se abrirá la página de descarga.
en.AssetsFailed=The game data (assets.bdoa) could not be assembled (exit code %1). The app will show an error page until the installation is repeated. The parts are kept in %2.
es.AssetsFailed=No se pudo montar el archivo de datos (assets.bdoa) (código de salida %1). La aplicación mostrará una página de error hasta repetir la instalación. Las partes se conservan en %2.
en.LaunchApp=Launch BDOTempo
es.LaunchApp=Abrir BDOTempo

[Files]
; The framework-dependent app (exe, its DLLs and the Windows App SDK runtime files).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; The asset parts: joined into assets.bdoa in the post-install step, then deleted.
Source: "{#PartsDir}\assets.part*.bdoa"; DestDir: "{app}\assets\parts"; Flags: ignoreversion

[Icons]
Name: "{group}\BDOTempo"; Filename: "{app}\BDOTempo.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,BDOTempo}"; Filename: "{uninstallexe}"

[Run]
; Interactive installs offer to start the app on the last page. A silent install (the updater) does not start it.
Filename: "{app}\BDOTempo.exe"; Description: "{cm:LaunchApp}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\assets"
Type: files; Name: "{app}\assets.bdoa"

[Code]
const
  DotNetMajor = '10';
  DotNetPageUrl = 'https://dotnet.microsoft.com/download/dotnet/10.0';
  DotNetWingetId = 'Microsoft.DotNet.DesktopRuntime.10';

var
  AssetsFailed: Boolean;

{ True when the runtime folder of this name has a 10.x version folder (the Desktop Runtime installs both shared frameworks). }
function HasRuntimeVersion(const Framework: String): Boolean;
var
  FindRec: TFindRec;
  Base: String;
begin
  Result := False;
  Base := ExpandConstant('{commonpf64}') + '\dotnet\shared\' + Framework + '\';
  if FindFirst(Base + DotNetMajor + '.*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and $10) <> 0 then
          Result := True;
      until Result or (not FindNext(FindRec));
    finally
      FindClose(FindRec);
    end;
  end;
end;

function IsDotNetDesktopRuntimeInstalled(): Boolean;
begin
  Result := HasRuntimeVersion('Microsoft.WindowsDesktop.App') and HasRuntimeVersion('Microsoft.NETCore.App');
end;

function InitializeSetup(): Boolean;
var
  Choice: Integer;
  ResultCode: Integer;
begin
  Result := True;
  if IsDotNetDesktopRuntimeInstalled() then
    Exit;

  Log('.NET 10 Desktop Runtime (x64) not found.');
  if WizardSilent() then
  begin
    // A silent run has no window to ask in: refuse, and the exit code and the log tell the caller why.
    Result := False;
    Exit;
  end;

  Choice := MsgBox(ExpandConstant('{cm:DotNetMissing}'), mbError, MB_YESNOCANCEL);
  if Choice = IDYES then
  begin
    Exec(ExpandConstant('{cmd}'),
      '/C winget install --id ' + DotNetWingetId + ' -e --source winget --accept-package-agreements --accept-source-agreements',
      '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
    Result := IsDotNetDesktopRuntimeInstalled();
    if not Result then
    begin
      MsgBox(ExpandConstant('{cm:DotNetInstallFailed}'), mbError, MB_OK);
      ShellExec('open', DotNetPageUrl, '', '', SW_SHOW, ewNoWait, ResultCode);
    end;
  end
  else
  begin
    if Choice = IDNO then
      ShellExec('open', DotNetPageUrl, '', '', SW_SHOW, ewNoWait, ResultCode);
    Result := False;
  end;
end;

{ Runs after the files are copied. A failure cannot roll the copied files back (Inno commits them before this step), so it is
  reported through the exit code (GetCustomSetupExitCode) and the parts are kept for a repeat of the installation. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  AppDir, Exe, PartsDir, AssetsFile: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  AppDir := ExpandConstant('{app}');
  Exe := AppDir + '\BDOTempo.exe';
  PartsDir := AppDir + '\assets\parts';
  AssetsFile := AppDir + '\assets.bdoa';

  Log('Assembling assets.bdoa from the parts.');
  if (not Exec(Exe, '--assemble-assets "' + PartsDir + '" "' + AssetsFile + '"', AppDir, SW_HIDE, ewWaitUntilTerminated, ResultCode))
    or (ResultCode <> 0) then
  begin
    AssetsFailed := True;
    Log('The asset assembly failed with exit code ' + IntToStr(ResultCode) + '.');
    if not WizardSilent() then
      MsgBox(FmtMessage(ExpandConstant('{cm:AssetsFailed}'), [IntToStr(ResultCode), PartsDir]), mbCriticalError, MB_OK);
    Exit;
  end;

  // assets.bdoa is written and verified by the command: the parts are not needed any more.
  DelTree(PartsDir, True, True, True);
  RemoveDir(AppDir + '\assets');
  Log('Asset parts joined and removed.');
end;

function GetCustomSetupExitCode(): Integer;
begin
  if AssetsFailed then
    Result := 1
  else
    Result := 0;
end;
