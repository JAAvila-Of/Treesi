Unicode true
RequestExecutionLevel user
ManifestSupportedOS all
SetCompressor zlib
SetDatablockOptimize on
Name "Treesi Preview"
BrandingText "Treesi"
ShowInstDetails show
ShowUninstDetails show
!include "MUI2.nsh"
!include "x64.nsh"
!include "payload.nsh"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\TreesiPreview"
!define MUI_WELCOMEPAGE_TEXT "Review local Git changes with Treesi.$\r$\n$\r$\nThis experimental build installs for your Windows account and adds a Start menu shortcut. Git must already be available on PATH.$\r$\n$\r$\nClose Treesi before uninstalling. Your repositories and saved preferences are kept."
!define MUI_FINISHPAGE_TEXT "Treesi Preview is installed. Open Treesi Preview from the Start menu.$\r$\n$\r$\nGit is a separate requirement. This experimental installer has no OS publisher signature."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  SetShellVarContext current
  SetRegView 64
  StrCpy $INSTDIR "$LOCALAPPDATA\Programs\Treesi Preview"
  ${IfNot} ${IsNativeAMD64}
    MessageBox MB_OK|MB_ICONSTOP "This candidate requires Windows x64." /SD IDOK
    SetErrorLevel 2
    Quit
  ${EndIf}
  Call CheckInstallTarget
FunctionEnd

Function CheckInstallTarget
  System::Call 'kernel32::GetFileAttributesW(w "$SMPROGRAMS\Treesi Preview.lnk") i.r0'
  ${If} $0 != -1
    MessageBox MB_OK|MB_ICONSTOP "The Treesi Preview Start menu shortcut already exists. Remove or rename it before installing." /SD IDOK
    SetErrorLevel 2
    Quit
  ${EndIf}
  System::Call 'kernel32::GetFileAttributesW(w "$LOCALAPPDATA\Programs") i.r0'
  ${If} $0 != -1
    IntOp $0 $0 & 0x400
    ${If} $0 != 0
      MessageBox MB_OK|MB_ICONSTOP "The installation parent is a link. Use a portable candidate in a regular directory." /SD IDOK
      SetErrorLevel 2
      Quit
    ${EndIf}
  ${EndIf}
  System::Call 'kernel32::GetFileAttributesW(w "$INSTDIR") i.r0'
  ${If} $0 != -1
    MessageBox MB_OK|MB_ICONSTOP "The Treesi Preview folder already exists. Uninstall the previous candidate or use a portable build in a separate directory." /SD IDOK
    SetErrorLevel 2
    Quit
  ${EndIf}
  ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "InstallLocation"
  ${If} $0 != ""
    MessageBox MB_OK|MB_ICONSTOP "A Treesi Preview installation is already registered. Uninstall it before installing another candidate." /SD IDOK
    SetErrorLevel 2
    Quit
  ${EndIf}
FunctionEnd

Section "Treesi Preview"
  Call CheckInstallTarget
  CreateDirectory "$LOCALAPPDATA\Programs"
  System::Call 'kernel32::CreateDirectoryW(w "$INSTDIR", p 0) i.r0'
  ${If} $0 == 0
    MessageBox MB_OK|MB_ICONSTOP "The installation directory could not be created. No existing installation will be overwritten." /SD IDOK
    SetErrorLevel 2
    Quit
  ${EndIf}
  ClearErrors
  WriteINIStr "$INSTDIR\installation.ini" "Identity" "Product" "TreesiPreview"
  WriteINIStr "$INSTDIR\installation.ini" "Identity" "Commit" "${SOURCE_COMMIT}"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONSTOP "Installation metadata could not be written. No previous installation was overwritten." /SD IDOK
    SetErrorLevel 3
    Quit
  ${EndIf}
  !insertmacro InstallPayload
  SetOutPath "$INSTDIR"
  CreateShortcut "$SMPROGRAMS\Treesi Preview.lnk" "$INSTDIR\Treesi.Desktop.exe" "" "$INSTDIR\Treesi.Desktop.exe" 0 SW_SHOWNORMAL
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "Treesi Preview"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "Treesi contributors"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\Treesi.Desktop.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Function un.onInit
  SetShellVarContext current
  SetRegView 64
  StrCpy $INSTDIR "$LOCALAPPDATA\Programs\Treesi Preview"
  System::Call 'kernel32::GetFileAttributesW(w "$LOCALAPPDATA\Programs") i.r0'
  ${If} $0 != -1
    IntOp $0 $0 & 0x400
    ${If} $0 != 0
      MessageBox MB_OK|MB_ICONSTOP "The installation parent is a link. Nothing was removed." /SD IDOK
      SetErrorLevel 2
      Quit
    ${EndIf}
  ${EndIf}
  !insertmacro CheckOwnedDirectories
  ReadINIStr $0 "$INSTDIR\installation.ini" "Identity" "Product"
  ReadINIStr $1 "$INSTDIR\installation.ini" "Identity" "Commit"
  ${If} $0 != "TreesiPreview"
  ${OrIf} $1 != "${SOURCE_COMMIT}"
    MessageBox MB_OK|MB_ICONSTOP "This installation does not match this uninstaller. Nothing was removed." /SD IDOK
    SetErrorLevel 2
    Quit
  ${EndIf}
  !insertmacro CheckPayloadNotInUse
FunctionEnd

Section "Uninstall"
  !insertmacro CheckOwnedDirectories
  !insertmacro RemovePayload
  Delete "$INSTDIR\installation.ini"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\Treesi Preview.lnk"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
SectionEnd
