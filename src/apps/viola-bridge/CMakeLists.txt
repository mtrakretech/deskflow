# SPDX-FileCopyrightText: (C) 2026 Viola File Bridge contributors
# SPDX-License-Identifier: MIT
if(NOT WIN32)
  return()
endif()

option(BUILD_VIOLA_BRIDGE "Include Windows file transfer and clipboard history" ON)
if(NOT BUILD_VIOLA_BRIDGE)
  return()
endif()

find_program(VIOLA_POWERSHELL NAMES powershell.exe REQUIRED)
set(VIOLA_OUTPUT_DIR "${CMAKE_BINARY_DIR}/viola")
file(GLOB VIOLA_SOURCES CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/src/*.cs")
add_custom_command(
  OUTPUT "${VIOLA_OUTPUT_DIR}/ViolaFileBridge.exe" "${VIOLA_OUTPUT_DIR}/ViolaFileBridge-Setup.exe"
  COMMAND "${VIOLA_POWERSHELL}" -NoProfile -ExecutionPolicy Bypass
    -File "${CMAKE_CURRENT_SOURCE_DIR}/Build.ps1" -OutputDirectory "${VIOLA_OUTPUT_DIR}"
  DEPENDS ${VIOLA_SOURCES} Build.ps1 Firewall.ps1 NetworkSetup.ps1
    src/app.manifest src/ViolaFileBridge.exe.config README.md SECURITY.md CHANGELOG.md LICENSE
  VERBATIM
  COMMENT "Building lightweight Windows clipboard companion"
)
add_custom_target(viola-bridge ALL DEPENDS
  "${VIOLA_OUTPUT_DIR}/ViolaFileBridge.exe" "${VIOLA_OUTPUT_DIR}/ViolaFileBridge-Setup.exe")
install(FILES
  "${VIOLA_OUTPUT_DIR}/ViolaFileBridge.exe"
  "${VIOLA_OUTPUT_DIR}/ViolaFileBridge.exe.config"
  "${VIOLA_OUTPUT_DIR}/ViolaFileBridge-Setup.exe"
  Firewall.ps1 NetworkSetup.ps1
  DESTINATION "${CMAKE_INSTALL_BINDIR}")
install(FILES README.md SECURITY.md CHANGELOG.md LICENSE DESTINATION "${CMAKE_INSTALL_DOCDIR}/viola")
