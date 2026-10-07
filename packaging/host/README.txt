WRApperture - HOST package (the PC whose apps will be used remotely)
====================================================================

Requirements: Windows 10/11 Pro, Enterprise or Education (x64). No .NET needed.

INSTALL (or update)
  1. Extract this .zip to a folder.
  2. Double-click Instalar.cmd and accept the administrator prompt.
     Answer whether you want a desktop shortcut (Enter = yes).
  3. Write down the ADDRESS, PORT and TOKEN shown at the end.

PUBLISH APPS
  Start menu > "WRApperture (Host)": drag shortcuts or programs onto the window,
  review and click Publish. "From the Start menu..." publishes several at once.
  Same window: prepare the PC, generate a new token (with Copy), restart the agent.

  Command line (optional): wrahost list / publish / unpublish / inspect

UNINSTALL
  Double-click Desinstalar.cmd (in this package or in C:\Program Files\WRApperture).
  It asks for administrator and whether to undo everything (including the token).

LANGUAGE BAR FLOATING OVER THE APPS
  The installer already hides the bar for the signed-in users. For another user,
  that user runs (no admin needed): wrahost langbar hide
  It applies from the next session: sign out of the remote session and open the app again.

NOTE
  - Windows Pro allows only one session at a time: opening a remote app disconnects
    whoever is using this PC.
  - By default the agent only accepts connections on domain/private networks.
  - The executables are not signed; SmartScreen may show a warning.

Logs: %ProgramData%\WRApperture\logs
