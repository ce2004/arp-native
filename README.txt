AUDIO RECORDER PRO - NATIVE BUILD
=================================

A rewrite of the Python/PyQt6 recorder as a native Windows application that
runs on ARM64 and x64 with no runtime to install. Every setting the Python
build had is present, reading from and writing to the same configuration file.


WHAT IT IS BUILT ON
-------------------

Raw Win32 with NativeAOT. No WPF, no WinForms, no NuGet packages. Every piece
of interop is hand written:

  - Dialogs are built as in-memory DLGTEMPLATE structures. Going through a real
    dialog template gives correct tab order, arrow-key group navigation,
    Alt-mnemonics, Enter and Escape, and the MSAA dialog semantics NVDA reads
    best, all from the operating system rather than from a UI framework.
  - Audio uses WASAPI through raw COM vtable calls. NativeAOT does not support
    the classic ComImport marshalling that NAudio is built on, so the interfaces
    are invoked through unmanaged function pointers instead.
  - Configuration uses a purpose-built JSON reader. It is smaller than a
    serializer and, more importantly, it preserves keys it does not recognise,
    so this build and the Python build can share one config file.

The result is a single self-contained executable per architecture with no
framework dependency.


SETTINGS COMPATIBILITY
----------------------

The app reads and writes the same file as the Python build:

  %LOCALAPPDATA%\Audio Recorder Pro\recorder_config.json

Device ids are Windows endpoint id strings, identical to the ones the Python
build stores, so an existing configuration selects the same hardware with no
changes. Unknown keys, including split_silence_sec and split_threshold_db, are
carried through untouched on save.

The diagnostic log stays at:

  %LOCALAPPDATA%\Audio Recorder Pro\arp_diagnostic.log

Recordings are written as RF64, the same container libsndfile produces for the
Python build's format='RF64'. Files verified with soundfile report
"format=RF64 subtype=PCM_24", so the two builds' output is interchangeable.


BUILDING
--------

Requirements for a normal build (compile and run the tests):

  .NET 8 SDK

    dotnet build

Requirements for the final NativeAOT publish, which additionally needs the
Microsoft C++ linker:

  Visual Studio 2022 Build Tools with the Desktop development with C++
  workload, plus the C++ ARM64 build tools.

    winget install --id Microsoft.VisualStudio.2022.BuildTools --override ^
      "--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools ^
       --add Microsoft.VisualStudio.Component.VC.Tools.ARM64 ^
       --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 ^
       --add Microsoft.VisualStudio.Component.Windows11SDK.22621"

Then:

    dotnet publish -r win-arm64 -c Release -o publish/win-arm64
    dotnet publish -r win-x64   -c Release -o publish/win-x64

Without a local C++ toolchain, the GitHub Actions workflow in
.github/workflows/release.yml produces both binaries; the hosted Windows
runners already carry the MSVC toolchain including the ARM64 cross tools.


TESTING
-------

The executable carries its own test modes, so there is no test framework
dependency and the tests run against the shipping code.

    ArpRecorder.exe --selftest [referenceSoundsDir]

      Headless checks: JSON round-tripping and unknown-key preservation, the
      config migration from auto_split_mins, the duration grammar in both
      directions, percentage and filename handling, channel routing maths, the
      RF64 container byte by byte, the crash-repair path for both RIFF and
      RF64, sound-cue generation, and every dialog template.

      Pass the Python build's sounds folder as an argument to additionally
      compare the regenerated cues sample for sample against the originals.

    ArpRecorder.exe --uitest

      Creates every dialog for real, hidden, runs its initialisation, and
      checks that the controls exist and were populated from config. This is
      what catches a mistake in the hand-assembled dialog templates.

    ArpRecorder.exe --captest [seconds] [--device id] [--device2 id]
                              [--split n] [--rate n] [--bits n]
                              [--channels n] [--buffer n] [--keep]

      Runs the real capture pipeline into a temporary folder and reports
      captured duration, dropped blocks, substituted silence blocks, stall
      warnings, split behaviour and file validity. Defaults to a loopback
      endpoint so no microphone is opened.

    ArpRecorder.exe --signaltest [--cable "Line 1"]

      Plays a known tone into a virtual audio cable, captures that cable's
      loopback through the normal pipeline, reads the file back and checks
      amplitude, frequency and channel placement. Catches a wrong conversion
      scale, swapped channels or a stream running at the wrong rate. Needs a
      virtual audio cable so that nothing is played aloud; skips cleanly if
      none is present.

    ArpRecorder.exe --devices

      Prints the endpoint list the settings dialog would show, with ids.

Each mode writes its transcript to %TEMP%\arp_<mode>_report.txt and exits
non-zero on failure.


SCREEN READER OUTPUT
--------------------

Speech goes to the NVDA controller client when it is available and falls back
to SAPI otherwise, standing in for accessible_output2's Auto backend.

The controller client must match the architecture of THIS process, not NVDA's.
Place the matching DLL in native\<rid>\ and the build copies it next to the
executable:

  native\win-x64\nvdaControllerClient64.dll      (present)
  native\win-arm64\nvdaControllerClientArm64.dll (needed for ARM64 speech)

The ARM64 client ships in NVDA's controllerClient package. Without it, an ARM64
build still runs and NVDA still reads the interface normally, because every
control is a real Win32 control; only the app's own spoken status announcements
fall back to SAPI.

Duration and volume fields display and accept spoken text ("1 hour, 30 minutes",
"15 percent") rather than bare numbers, and Up, Down, Home and End adjust them,
speaking the new value.


LAYOUT
------

  src\Program.cs        entry point, message loop, diagnostic switches
  src\MainWindow.cs     dashboard, recording state machine, failure handling
  src\SettingsDialog.cs the settings window
  src\Dialogs.cs        notifications, sounds, channels, repair, update
  src\UiCore.cs         dialog base class, spin edits, folder picker
  src\DialogBuilder.cs  in-memory DLGTEMPLATE construction
  src\Win32.cs          user32, kernel32, comctl32 interop
  src\Wasapi.cs         device enumeration and capture via COM vtables
  src\Recorder.cs       reader threads, mixing writer loop, split, journal
  src\Wav.cs            RF64 writer and the crash-repair routine
  src\Monitors.cs       drive monitor and auto-resume watcher
  src\Config.cs         settings, shared with the Python build
  src\Json.cs           minimal order-preserving JSON
  src\Speech.cs         NVDA controller client with SAPI fallback
  src\Sounds.cs         cue generation and waveOut playback
  src\Notifier.cs       shell tray balloon notifications
  src\Log.cs            rotating diagnostic log
  src\Updater.cs        update UI; the release feed is not yet wired up
  src\SelfTest.cs       headless checks
  src\UiTest.cs         dialog checks
  src\CaptureTest.cs    capture pipeline checks
  src\SignalTest.cs     end-to-end signal fidelity check

An earlier WPF and NAudio attempt lived in _wpf_original\ and was deleted once
this replaced it. It never compiled: three mismatched namespaces, a settings
window that saved device friendly names where the audio engine expected
endpoint ids, and no implementation behind most of the buttons. It is in this
repository's first commit if it is ever needed.
