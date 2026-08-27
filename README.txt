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


BUILDING ON GITHUB - RUNBOOK FOR FUTURE AGENTS
----------------------------------------------

This is the normal way to produce binaries. Do not install the Visual Studio
C++ workload locally just to get a build; the hosted runners already have it,
including the ARM64 cross tools. A local "dotnet build" is still the right way
to check that the code compiles and to run the tests.

Repository: ce2004/arp-native  (private)

STEP 0 - TOOLS

Check first, because these may already be present:

    git --version
    gh --version

If either is missing:

    winget install --id Git.Git -e --accept-source-agreements --accept-package-agreements
    winget install --id GitHub.cli -e --accept-source-agreements --accept-package-agreements

After installing, the current shell will not have them on PATH yet. Either
start a new shell or refresh PATH in place:

    $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" +
                [System.Environment]::GetEnvironmentVariable("Path","User")

STEP 1 - AUTHENTICATION

You cannot do this step yourself. It needs a browser and a one time code.
Ask Conner to run it in the Claude Code prompt, where the leading exclamation
mark runs it in the session so the output is visible to you:

    ! gh auth login --hostname github.com --git-protocol https --web

Confirm before continuing:

    gh auth status

Never ask for, echo, or store a personal access token. The gh credential store
is the only place credentials should live.

STEP 2 - COMMIT

Commit messages become the release notes, and the application reads release
notes aloud through the screen reader. So, per AI_UPDATE_GUIDE.txt in the
Python repository: plain sentences, no "#" and no "*" characters anywhere.

    git add -A
    git commit -m "Fixed X. Added Y."
    git push

STEP 3 - RUN THE BUILD

For a plain build with downloadable artifacts and no public release:

    gh workflow run release.yml --ref main
    gh run list --workflow release.yml --limit 1
    gh run watch <run-id>

The matrix builds win-x64 and win-arm64 in parallel and runs --selftest on the
x64 build. The device tests need real audio endpoints and do not run in CI.

STEP 4 - DOWNLOAD

    gh run download <run-id> --dir dist

That yields dist\win-x64\ and dist\win-arm64\, each holding the zip and its
SHA-256 file. Verify a hash before handing anything over:

    Get-FileHash "dist\win-arm64\Audio Recorder Pro-win-arm64.zip" -Algorithm SHA256

STEP 5 - PUBLISH A RELEASE (only when asked)

Releases are cut by pushing a version tag, never by hand.

  1. Bump CurrentVersion in src\Updater.cs. It must start with a lowercase v.
  2. Commit that bump with the release notes as the message.
  3. Tag with exactly the same string and push the tag:

        git tag v2.0.1
        git push origin main
        git push origin v2.0.1

The release job then attaches both architecture zips plus sha256sums.txt and
uses the commit message as the release body.

TROUBLESHOOTING

  - "Platform linker not found" from a local publish means the MSVC toolchain
    is absent. That is expected on this machine. Build on GitHub instead.
  - A run that fails only on win-arm64 is usually a genuine AOT problem, since
    the x64 leg also executes the tests. Read the log with:
        gh run view <run-id> --log-failed
  - The workflow needs contents:write for the release job. It is already set.


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

NVDA only. Announcements go through the NVDA controller client and there is no
SAPI fallback: a second synthesiser talking over the top of NVDA is worse than
silence. If the controller client is missing, announcements are written to the
log and dropped, and NVDA still reads the whole interface normally because
every control is a real Win32 control.

The controller client must match the architecture of THIS process, not NVDA's.
Place the matching DLL in native\<rid>\ and the build copies it next to the
executable:

  native\win-x64\nvdaControllerClient64.dll      (present)
  native\win-arm64\nvdaControllerClientArm64.dll (needed for ARM64 speech)

The ARM64 client ships in NVDA's controllerClient package. Until it is dropped
in, the ARM64 build runs fine but makes no spoken status announcements of its
own.

READ-ONLY TEXT

Blocks of read-only text - the dashboard overview, the live statistics, and the
message in each prompt - are list boxes, not read-only edit controls. A
read-only edit announces itself as an editable text field, which is misleading;
a list reads as a list and gives each line its own item on arrow-down.

The live statistics are rewritten once a second. An update that arrives while
that list has focus is held back and applied when focus leaves, so the control
never re-announces or loses the reading position underneath you.

DURATIONS

Start delay, maximum length and auto-split are each a number plus a unit combo
box (Seconds, Minutes, Hours). A two hour split is "2" and "Hours" rather than
a very long press of the up arrow. Up, Down, Home and End still nudge the
number and speak the result. When a setting is loaded the largest unit that
divides it exactly is chosen, so 5400 seconds reads back as 90 minutes and
7200 as 2 hours.

NOTIFICATIONS

Notification titles follow the window title from settings, so renaming the
window to "ARP" renames what the notifications identify themselves as.

Bodies are deliberately one short sentence. The shell caps a balloon at 63
characters of title and 255 of body and clips the toast after two or three
lines regardless, so the full wording goes to the dialog and to the screen
reader instead, neither of which has that limit. If a notification ever does
have to be trimmed it breaks on a word boundary, ends with an ellipsis, and the
untruncated text is written to the log.


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
