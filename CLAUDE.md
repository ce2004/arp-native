AUDIO RECORDER PRO, NATIVE BUILD

The owner is blind and uses NVDA. Full detail is in AI_AGENT_GUIDE.txt; read the
relevant part of it before a release or a UI change. Its paths are out of date:
the repo is C:\Users\Conner\Documents\arp_csharp and the installed copy is in
C:\Users\Conner\Documents\arp-native-build.

WHAT IT IS

One exe per architecture (win-arm64, win-x64). Raw Win32 plus NativeAOT, no
WinForms, no NuGet packages. The main window is a modeless dialog driven by
IsDialogMessage. Recordings are RF64 wav. Settings, log and crash record live in
%LOCALAPPDATA%\Audio Recorder Pro, shared with the older Python build.

RULES

- No number sign or asterisk characters in any file or commit message. NVDA
  reads them aloud, and the commit message becomes the release notes.
- Never write into Documents\arp-native-build; it is the running install.
- Never touch Documents\apps\arp; those are the recordings.
- Never launch the GUI while a recording may be live.
- Never disable or hide the focused control without moving focus first, or the
  window stops taking keys until Alt+Tab. Use Disable() and RestoreFocus() in
  MainWindow.cs.

BUILD AND TEST

    dotnet build   (SDK is in %LOCALAPPDATA%\Microsoft\dotnet)
    bin\Debug\net8.0-windows\win-arm64\ArpRecorder.exe --selftest   then --uitest

These are windowed programs: launch with Start-Process -Wait -PassThru and read
%TEMP%\arp_selftest_report.txt or arp_uitest_report.txt.
The AOT exe cannot be built here (no MSVC linker; do not install one).

RELEASE

Bump CurrentVersion in src\Updater.cs, commit, push main, then push a tag equal
to it. The tag push makes GitHub build both exes and publish the release; the
app's updater finds it and picks the asset for its own architecture.

    git tag v2.2.1
    git push origin v2.2.1
    gh run watch <id> --exit-status

Repo: github.com/ce2004/arp-native (must stay public for the updater).

WHERE THINGS ARE

MainWindow.cs is the dashboard and recording state machine. Recorder.cs holds
capture threads and the writer, Wav.cs the RF64 writer and repair, Wasapi.cs the
audio COM interop, Updater.cs the self update, Speech.cs the NVDA client.
