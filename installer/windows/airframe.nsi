; NSIS installer for airframe on Windows.
;
; Installs airframe.exe as an auto-start service configured from a settings
; page (observer location, radius, port), starts it, and optionally installs
; the screensaver and makes it the current user's active screensaver.
;
; Build with "make installer", or directly:
;   makensis -DVERSION=x.y.z installer/windows/airframe.nsi
;
; Needs the NScurl and nsJSON plugins in the NSIS x86-unicode plugin folder.
;
; The installer runs elevated, so HKCU is the account that approved the UAC
; prompt. That is the logged-on user when an administrator elevates their own
; session, but not when a standard user elevates with someone else's admin
; credentials: the screensaver is then set for the admin account.

Unicode true
RequestExecutionLevel admin
ManifestSupportedOS all

!include MUI2.nsh
!include LogicLib.nsh
!include x64.nsh
!include nsDialogs.nsh
!include FileFunc.nsh
!include WordFunc.nsh

!ifndef VERSION
  !define VERSION "0.0.0"
!endif

!define NAME        "Airframe"
!define PUBLISHER   "fireflycons"
!define SERVICE     "airframe"
!define BINDIR      "..\..\bin\windows-amd64"
!define SCR_SOURCE  "Webview2_WebPage_Screensaver.scr"
!define SCR_NAME    "Airframe.scr"
!define REGKEY      "Software\Airframe"
!define UNINSTKEY   "Software\Microsoft\Windows\CurrentVersion\Uninstall\Airframe"
!define SCR_REGKEY  "Software\Airframe-Screensaver"
!define DESKTOPKEY  "Control Panel\Desktop"
!define MAX_RADIUS  250 ; domain.MaxRadius

Name "${NAME}"
OutFile "${BINDIR}\airframe-setup.exe"
InstallDir "$PROGRAMFILES64\${NAME}"
InstallDirRegKey HKLM "${REGKEY}" "InstallDir"
BrandingText "${NAME} ${VERSION}"

Var Lat
Var Lon
Var Radius
Var Port
Var Screensaver
Var Place
Var Looked

Var hLat
Var hLon
Var hRadius
Var hPort
Var hScreensaver

!define MUI_ICON   "..\..\screensaver\windows\app.ico"
!define MUI_UNICON "..\..\screensaver\windows\app.ico"
!define MUI_ABORTWARNING

!define MUI_FINISHPAGE_LINK "Open the Airframe web page"
!define MUI_FINISHPAGE_LINK_LOCATION "http://localhost:$Port/"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
Page custom ConfigPageCreate ConfigPageLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "${NAME} requires 64-bit Windows."
    Abort
  ${EndIf}
  SetRegView 64
  StrCpy $Radius 5
  StrCpy $Port 7700
  StrCpy $Screensaver ${BST_CHECKED}
FunctionEnd

; Looks up the public IP's location at ipinfo.io, the service the geoip adapter
; uses. On any failure Lat, Lon and Place are left empty.
Function LookupLocation
  NScurl::http GET "https://ipinfo.io/json" "$PLUGINSDIR\ipinfo.json" /TIMEOUT 10s /SILENT /END
  Pop $0
  ${If} $0 != "OK"
    DetailPrint "Location lookup failed: $0"
    Return
  ${EndIf}

  nsJSON::Set /file "$PLUGINSDIR\ipinfo.json"
  ClearErrors
  nsJSON::Get "loc" /end
  ${If} ${Errors}
    Return
  ${EndIf}
  Pop $0
  ${WordFind} $0 "," "+1" $Lat
  ${WordFind} $0 "," "-1" $Lon

  StrCpy $Place ""
  ${ForEach} $1 0 2 + 1
    ${Select} $1
      ${Case} 0
        StrCpy $2 "city"
      ${Case} 1
        StrCpy $2 "region"
      ${Case} 2
        StrCpy $2 "country"
    ${EndSelect}
    ClearErrors
    nsJSON::Get $2 /end
    ${IfNot} ${Errors}
      Pop $3
      ${If} $3 != ""
        ${If} $Place != ""
          StrCpy $Place "$Place, "
        ${EndIf}
        StrCpy $Place "$Place$3"
      ${EndIf}
    ${EndIf}
  ${Next}
FunctionEnd

Function ConfigPageCreate
  ${If} $Looked != 1
    StrCpy $Looked 1
    InitPluginsDir
    Banner::show /set 76 "Detecting your location..." "Looking up the public IP address"
    Call LookupLocation
    Banner::destroy
  ${EndIf}

  !insertmacro MUI_HEADER_TEXT "Configuration" "Choose where to watch for aircraft."

  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}

  ${If} $Lat != ""
    ${NSD_CreateLabel} 0 0 100% 24u "Detected location: $Place$\r$\nChange the coordinates below if this isn't right."
  ${Else}
    ${NSD_CreateLabel} 0 0 100% 24u "Location could not be detected. Enter it, or leave it blank to auto-detect when the service starts."
  ${EndIf}
  Pop $0

  ${NSD_CreateLabel} 0 32u 30% 12u "Latitude:"
  Pop $0
  ${NSD_CreateText} 32% 30u 30% 12u $Lat
  Pop $hLat

  ${NSD_CreateLabel} 0 48u 30% 12u "Longitude:"
  Pop $0
  ${NSD_CreateText} 32% 46u 30% 12u $Lon
  Pop $hLon

  ${NSD_CreateLabel} 0 64u 30% 12u "Radius (NM, max ${MAX_RADIUS}):"
  Pop $0
  ${NSD_CreateText} 32% 62u 30% 12u $Radius
  Pop $hRadius

  ${NSD_CreateLabel} 0 80u 30% 12u "HTTP port:"
  Pop $0
  ${NSD_CreateNumber} 32% 78u 30% 12u $Port
  Pop $hPort
  ${NSD_SetTextLimit} $hPort 5

  ${NSD_CreateCheckbox} 0 100u 100% 12u "Install the Airframe screensaver and make it active"
  Pop $hScreensaver
  ${NSD_SetState} $hScreensaver $Screensaver

  nsDialogs::Show
FunctionEnd

; IsDecimal: replaces the string on top of the stack with 1 if it is a plain
; decimal number (optional leading '-', digits, at most one '.', at least one
; digit), otherwise 0.
Function IsDecimal
  Exch $0
  Push $1
  Push $2
  Push $3
  Push $4
  StrCpy $1 0 ; index
  StrCpy $3 0 ; digits seen
  StrCpy $4 0 ; '.' seen
  StrCpy $2 $0 1
  ${If} $2 == "-"
    IntOp $1 $1 + 1
  ${EndIf}
  ${Do}
    StrCpy $2 $0 1 $1
    ${If} $2 == ""
      ${ExitDo}
    ${ElseIf} $2 == "."
      ${If} $4 = 1
        StrCpy $3 -1
        ${ExitDo}
      ${EndIf}
      StrCpy $4 1
    ${ElseIf} $2 S>= "0"
    ${AndIf} $2 S<= "9"
      IntOp $3 $3 + 1
    ${Else}
      StrCpy $3 -1
      ${ExitDo}
    ${EndIf}
    IntOp $1 $1 + 1
  ${Loop}
  ${If} $3 > 0
    StrCpy $0 1
  ${Else}
    StrCpy $0 0
  ${EndIf}
  Pop $4
  Pop $3
  Pop $2
  Pop $1
  Exch $0
FunctionEnd

; InRange value lo hi: Abort with message unless value is a decimal number and
; lo <op_lo> value <= hi. op_lo is ">=" or ">".
!macro InRange value op_lo lo hi message
  Push "${value}"
  Call IsDecimal
  Pop $0
  ${If} $0 = 1
    StrCpy $R0 "${value}"
    Math::Script "a = f(R0); r0 = (a ${op_lo} ${lo}) && (a <= ${hi})"
  ${EndIf}
  ${If} $0 != 1
    MessageBox MB_ICONEXCLAMATION "${message}"
    Abort
  ${EndIf}
!macroend

; Pre-checks the input. "airframe install" validates it again and is the
; authority; this just catches mistakes before anything is installed.
Function ConfigPageLeave
  ${NSD_GetText} $hLat $Lat
  ${NSD_GetText} $hLon $Lon
  ${NSD_GetText} $hRadius $Radius
  ${NSD_GetText} $hPort $Port
  ${NSD_GetState} $hScreensaver $Screensaver

  ${If} $Lat != ""
  ${OrIf} $Lon != ""
    ${If} $Lat == ""
    ${OrIf} $Lon == ""
      MessageBox MB_ICONEXCLAMATION "Enter both latitude and longitude, or leave both blank to auto-detect."
      Abort
    ${EndIf}
    !insertmacro InRange $Lat ">=" -90 90 "Latitude must be a number from -90 to 90."
    !insertmacro InRange $Lon ">=" -180 180 "Longitude must be a number from -180 to 180."
  ${EndIf}

  !insertmacro InRange $Radius ">" 0 ${MAX_RADIUS} "Radius must be a number greater than 0 and at most ${MAX_RADIUS}."

  ${If} $Port == ""
  ${OrIf} $Port < 1
  ${OrIf} $Port > 65535
    MessageBox MB_ICONEXCLAMATION "Port must be a number from 1 to 65535."
    Abort
  ${EndIf}
FunctionEnd

Section "Install"
  ; Remove an existing service first, so its exe is unlocked and "install"
  ; doesn't fail because the service exists. The new exe does this from the
  ; plugins dir, so it works wherever the old one was installed.
  ClearErrors
  ReadRegStr $0 HKLM "SYSTEM\CurrentControlSet\Services\${SERVICE}" "ImagePath"
  ${IfNot} ${Errors}
    InitPluginsDir
    SetOutPath $PLUGINSDIR
    File "${BINDIR}\airframe.exe"
    DetailPrint "Removing the existing ${SERVICE} service"
    nsExec::ExecToLog '"$PLUGINSDIR\airframe.exe" uninstall'
    Pop $0
    ${If} $0 != 0
      MessageBox MB_ICONSTOP "Could not remove the existing ${SERVICE} service (exit code $0)."
      Abort
    ${EndIf}
    Delete "$PLUGINSDIR\airframe.exe"
  ${EndIf}

  SetOutPath $INSTDIR
  File "${BINDIR}\airframe.exe"
  WriteUninstaller "$INSTDIR\uninstall.exe"

  StrCpy $1 '--radius $Radius --listen :$Port'
  ${If} $Lat != ""
    StrCpy $1 '$1 --location $Lat,$Lon'
  ${EndIf}
  DetailPrint "Installing the ${SERVICE} service: $1"
  nsExec::ExecToLog '"$INSTDIR\airframe.exe" install $1'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Could not install the ${SERVICE} service (exit code $0). See the details for the error."
    Abort
  ${EndIf}

  DetailPrint "Starting the ${SERVICE} service"
  nsExec::ExecToLog 'sc.exe start ${SERVICE}'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_ICONEXCLAMATION "The ${SERVICE} service was installed but did not start (exit code $0). It will start when Windows next starts."
  ${EndIf}

  ${If} $Screensaver == ${BST_CHECKED}
    File "/oname=${SCR_NAME}" "${BINDIR}\${SCR_SOURCE}"

    ; SCRNSAVE.EXE is unreliable with spaces in the path, so prefer the 8.3
    ; name. GetFullPathName /SHORT returns the long path if the volume has no
    ; 8.3 names.
    GetFullPathName /SHORT $0 "$INSTDIR\${SCR_NAME}"
    WriteRegStr HKCU "${DESKTOPKEY}" "SCRNSAVE.EXE" $0
    WriteRegStr HKCU "${DESKTOPKEY}" "ScreenSaveActive" "1"
    ; SPI_SETSCREENSAVEACTIVE, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
    System::Call 'user32::SystemParametersInfoW(i 17, i 1, p 0, i 3)'

    ; Point the screensaver at the chosen port. A plain "Url" is moved to the
    ; primary screen's "UrlScreen0" when the screensaver first loads. If
    ; UrlScreen0 exists, only replace it when it is a localhost URL (one we
    ; set), so the user's own pages are kept.
    ClearErrors
    ReadRegStr $0 HKCU "${SCR_REGKEY}" "UrlScreen0"
    ${If} ${Errors}
      WriteRegStr HKCU "${SCR_REGKEY}" "Url" "http://localhost:$Port/"
    ${Else}
      StrCpy $1 $0 17
      ${If} $1 == "http://localhost:"
        WriteRegStr HKCU "${SCR_REGKEY}" "UrlScreen0" "http://localhost:$Port/"
      ${EndIf}
    ${EndIf}
  ${EndIf}

  WriteRegStr HKLM "${REGKEY}" "InstallDir" $INSTDIR
  WriteRegDWORD HKLM "${REGKEY}" "Screensaver" $Screensaver

  WriteRegStr HKLM "${UNINSTKEY}" "DisplayName" "${NAME}"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTKEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\airframe.exe"
  WriteRegStr HKLM "${UNINSTKEY}" "InstallLocation" $INSTDIR
  WriteRegStr HKLM "${UNINSTKEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoRepair" 1
  ${GetSize} $INSTDIR "/S=0K" $0 $1 $2
  WriteRegDWORD HKLM "${UNINSTKEY}" "EstimatedSize" $0
SectionEnd

Function un.onInit
  SetRegView 64
FunctionEnd

Section "Uninstall"
  DetailPrint "Removing the ${SERVICE} service"
  nsExec::ExecToLog '"$INSTDIR\airframe.exe" uninstall'
  Pop $0

  ; Clear the active screensaver only if it is still ours. The screensaver's
  ; own settings under ${SCR_REGKEY} are the user's and are kept.
  ReadRegDWORD $0 HKLM "${REGKEY}" "Screensaver"
  ${If} $0 = ${BST_CHECKED}
    ReadRegStr $0 HKCU "${DESKTOPKEY}" "SCRNSAVE.EXE"
    GetFullPathName /SHORT $1 "$INSTDIR\${SCR_NAME}"
    ${If} $0 == $1
    ${OrIf} $0 == "$INSTDIR\${SCR_NAME}"
      DeleteRegValue HKCU "${DESKTOPKEY}" "SCRNSAVE.EXE"
      WriteRegStr HKCU "${DESKTOPKEY}" "ScreenSaveActive" "0"
      ; SPI_SETSCREENSAVEACTIVE, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
      System::Call 'user32::SystemParametersInfoW(i 17, i 0, p 0, i 3)'
    ${EndIf}
  ${EndIf}

  Delete "$INSTDIR\airframe.exe"
  Delete "$INSTDIR\${SCR_NAME}"
  Delete "$INSTDIR\uninstall.exe"
  RMDir $INSTDIR

  DeleteRegKey HKLM "${UNINSTKEY}"
  DeleteRegKey HKLM "${REGKEY}"
SectionEnd
