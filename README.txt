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

Run "ArpRecorder.exe --config" to see exactly how this build reads the file.

BOTH BUILDS SHARE ONE PROGRESS RECORD

The crash-recovery record, active_recording.json, lives at the same path for
both builds. A session running in either leaves one sitting there, so the other
must not treat it as evidence of a crash. Before offering to repair anything,
this build checks whether the audio file is still open for writing and skips it
if so. Without that check, starting this build while the Python one is
recording produces an offer to repair the file being written at that moment.


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

    ArpRecorder.exe --speech [say]

      Reports which speech backend loaded, whether the architecture-matched
      controller client was found, and whether NVDA is running. Exit code 0
      means speech is working, 1 means no client was found, 2 means the client
      loaded but NVDA is not running. Add "say" to hear a test phrase.

    ArpRecorder.exe --config

      Prints how this build reads the shared settings file, including which
      physical device each configured id resolves to and any keys it does not
      recognise but will preserve. Read-only; it never writes.

    ArpRecorder.exe --checkupdate

      Reports the installed version, what the release feed offers, the download
      it would pick for this architecture and the checksum it would verify
      against. Exit code 0 means up to date, 10 means an update is available,
      1 means the check failed.

    ArpRecorder.exe --update

      Checks and installs without opening a window, and relaunches headless.
      Used to exercise the update path unattended.

Each mode writes its transcript to %TEMP%\arp_<mode>_report.txt and exits
non-zero on failure.


SCREEN READER OUTPUT
--------------------

NVDA only. Announcements go through the NVDA controller client and there is no
SAPI fallback: a second synthesiser talking over the top of NVDA is worse than
silence. If the controller client is missing, announcements are written to the
log and dropped, and NVDA still reads the whole interface normally because
every control is a real Win32 control.

The controller client must match the architecture of THIS process, not NVDA's,
which is a real trap here: NVDA itself is an x64 build running under emulation
on ARM64, but a native ARM64 recorder still needs the ARM64 client.

The client is EMBEDDED IN THE EXECUTABLE. It is a native DLL and cannot be
linked in, so the matching one is carried as a resource and unpacked on first
run to:

  %LOCALAPPDATA%\Audio Recorder Pro\nvdaControllerClient<arch>.dll

An existing copy of the right size is reused, and a copy locked by another
running instance is loaded as it stands. That is what lets the whole
application ship as one file with nothing beside it.

A DLL placed next to the executable still wins, so a newer NVDA client can be
dropped in without rebuilding. Sources for the build:

  native\win-x64\nvdaControllerClient64.dll      (embedded into the x64 build)
  native\win-arm64\nvdaControllerClientArm64.dll (embedded into the ARM64 build)

Both came from NVDA 2026.1's official controllerClient package and are LGPL.
The licence is embedded too and written out beside the unpacked DLL. To update
them, download nvda_<version>_controllerClient.zip from nvaccess.org and copy
the arm64 and x64 DLLs into those folders under the names above.

To check speech is working:

    ArpRecorder.exe --speech        reports the backend and whether NVDA is up
    ArpRecorder.exe --speech say    also sends a test phrase to NVDA

PROMPTS AND READ-ONLY TEXT

A prompt - the repair question, the external drive warning, the update offer -
is a real dialog whose message is plain static text. The screen reader speaks
the caption and the whole message as the dialog opens, and Tab then moves only
between the buttons. The message is not focusable and is not a tab stop, so
there is nothing to arrow through before the question can be heard.

The dashboard overview and the live statistics are different: they are status
you go and read, so they are list boxes you can tab to, with one line per item.
They are not read-only edit controls, because an edit announces itself as an
editable text field, which is misleading.

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

UPDATING

Releases publish the executables directly, not archives, so there is nothing to
unpack:

  https://github.com/ce2004/arp-native/releases

The application updates itself in place. Because it is a single file, an update
is a file swap:

  1. Read the latest release and pick the asset whose name carries this
     process's architecture.
  2. Download it and check its SHA-256 against the published sha256sums.txt.
     A file whose hash does not match is NOT installed, and neither is one from
     a release that published no checksum at all. The running version is left
     untouched in both cases.
  3. Rename the running executable aside. Windows will not let a running
     executable be deleted, but it will let it be renamed.
  4. Move the new one into its place and relaunch it.
  5. The new process waits for the old one to exit and deletes the renamed
     file.

Nothing is left in the folder afterwards: no second executable, no download
temporary. Every startup also sweeps for leftovers, so an update interrupted by
a crash or a power cut is cleared the next time the program runs rather than
leaving a stray executable behind.

The repository has to stay public for this to work. The updater reads the
release feed anonymously, exactly as the Python build does; against a private
repository GitHub answers 404 and no update is ever found.

SOUNDS

Sixty five cues, all generated in code on first use. There is no sounds folder
and nothing to ship beside the executable. The library is in SoundLibrary.cs
and groups into melodies, notification tones, alerts, and short subtle ones:

  Melodies         Major and Minor Triad Up and Down, Perfect Fifth,
                   Octave Leap, Fanfare, Little Fanfare, Pentatonic Run Up
                   and Down, Question, Answer, Music Box, Lullaby, Waltz,
                   Skip Step, Cascade, Staircase

  Notifications    Gentle Ping, Bright Ping, Soft Pop, Bubble, Marimba,
                   Wood Block, Glass Tap, Crystal, Bell Ding, Doorbell,
                   Elevator Chime, Submarine Ping, Radar Blip, Harp Pluck,
                   Kalimba, Celesta

  Alerts           Gentle Alert, Urgent Alert, Siren Sweep, Warning Trill,
                   Error Low, Error Double, Attention Rise, Attention Fall,
                   Klaxon, Buzz

  Subtle           Tick, Tock, Soft Click, Whoosh Up, Whoosh Down, Air Puff,
                   Heartbeat, Pulse, Drip, Ripple

  Original cues    Rising Sweep, Falling Sweep, Low Double Beep,
                   High Double Beep, Two Tone Up, Two Tone Down, Soft Chime,
                   Short Blip, Triple Blip, Low Thud, Alert Warble

Plus None, which plays nothing.

Each of the four events - start, stop, pause, unpause - has its own checkbox
and its own sound combo in Configure Sounds. Choosing a sound plays it
immediately at the configured volume, because picking a cue by name is
meaningless without hearing it. "ArpRecorder.exe --sounds" lists them all with
their duration and level.

Notes are built from decaying partials mixed onto a floating point canvas and
normalised once at the end, so cues can overlap and ring into each other
without clipping and every one lands at a comparable loudness. Bell voices use
the classic inharmonic 2.76 and 5.4 partials; noise for the whooshes comes from
a fixed seed so a cue sounds identical every time.

The first four sounds reproduce the original cues sample for sample and remain
the defaults, so an existing install sounds exactly as it did. The choices are
stored as snd_<event>_sound; an unrecognised name falls back to the default
rather than silently playing nothing.

The gen_sounds.py script that produced the old WAV files has been deleted from
the Python repository; nothing referenced it at runtime and the sounds folder
that build still reads was left alone.

POWER AND RESPONSIVENESS

While idle the application does no polling at all. The drive watcher only runs
during a recording, because a drive going missing only matters while something
is being written to it; leaving it running woke the machine once a second for
as long as the window was open. Removal is noticed instantly anyway through the
WM_DEVICECHANGE broadcast Windows sends to every top level window, which costs
nothing, so the watcher's own poll is a slow five second backstop.

While recording, the capture thread is event driven: WASAPI signals it when a
buffer is ready rather than being asked on a timer. A device that will not
accept event mode alongside the format converter falls back to polling at about
half a block, which is still far less often than a fixed short poll. The log
records which mode each input negotiated.

Startup does no blocking work. The update check runs on its own thread, so the
window appears immediately instead of waiting on a network round trip, and cues
are built the first time they are played rather than all at once up front.

Closing is immediate. The watcher threads wait on an event rather than sleeping,
so stopping them returns at once instead of sitting out the remainder of a
one second sleep.

Measured with "ArpRecorder.exe --timing".

INPUT STALLS

If an input stops delivering audio for two seconds, the recording KEEPS GOING.
It is announced, a notification is raised, silence is written for the quiet
input, and recovery is announced when data returns.

This is a deliberate departure from the Python build, which routed the same
condition into its error path and stopped the recording behind a modal dialog.
That turned a transient driver hiccup, or simply using a loopback input while
nothing was playing, into the permanent end of an unattended session. Note that
a genuinely dead device is a different path: the reader thread ends, which is
handled as a disconnect and does honour auto-resume.

Silence in the room is not a stall. A working microphone in a quiet room still
sends a continuous stream of zeros, which is data.

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
  src\Speech.cs         NVDA controller client, embedded and unpacked on demand
  src\SoundLibrary.cs   the built-in cue set, generated in code
  src\Sounds.cs         cue playback through waveOut
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
