using System;
using System.Runtime.InteropServices;

namespace Arp
{
    internal sealed class UpdateInfo
    {
        public string Version;
        public string DownloadUrl;
        public string ReleaseNotes;
        public string Sha256Url;
    }

    /// <summary>
    /// The updater UI is complete — "Check for updates on startup" persists, the
    /// button reports a result, and <see cref="UpdateDialog"/> renders release
    /// notes as a navigable list. Only the network lookup is stubbed: the Python
    /// build pulls from ce2004/arp-audio-recorder-pro, whose release assets are
    /// the PyInstaller zip and would not install over this build. Point
    /// <see cref="Check"/> at a feed publishing per-architecture assets and the
    /// rest of the flow is already in place.
    /// </summary>
    internal static class Updater
    {
        public const string CurrentVersion = "v2.0.0";

        public static string ArchSuffix => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "win-arm64",
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            _ => "win-x64",
        };

        /// <summary>
        /// Returns the available update, or null when up to date.
        ///
        /// To enable: fetch https://api.github.com/repos/OWNER/REPO/releases,
        /// take the newest tag, compare it against CurrentVersion component by
        /// component, and select the asset whose name contains ArchSuffix along
        /// with the sha256 asset. Download to a temp path, verify the SHA-256
        /// against the published sum, reject any archive entry that escapes the
        /// extraction root, then swap the files and relaunch. The Python build's
        /// updater.py has a working version of exactly that sequence.
        /// </summary>
        private static UpdateInfo Check()
        {
            return null;
        }

        public static void CheckOnStartup(IntPtr owner)
        {
            try
            {
                var info = Check();
                if (info == null) return;
                Offer(owner, info);
            }
            catch (Exception e)
            {
                Log.Warn("Startup update check failed: " + e.Message);
            }
        }

        public static void CheckNow(IntPtr owner)
        {
            try
            {
                Log.Info("Manual update check triggered");
                var info = Check();
                if (info == null)
                {
                    Win32.MessageBoxW(owner,
                        "You are already on the latest version!\r\n\r\nInstalled version: " + CurrentVersion +
                        "\r\nBuild: " + ArchSuffix,
                        "Up to Date", Win32.MB_OK | Win32.MB_ICONINFORMATION);
                    return;
                }
                Offer(owner, info);
            }
            catch (Exception e)
            {
                Log.Error("Failed to check for updates: " + e.Message);
                Win32.MessageBoxW(owner, "Failed to check for updates: " + e.Message,
                    "Error", Win32.MB_OK | Win32.MB_ICONERROR);
            }
        }

        private static void Offer(IntPtr owner, UpdateInfo info)
        {
            var dlg = new UpdateDialog(CurrentVersion, info.Version, info.ReleaseNotes);
            if ((long)dlg.ShowModal(owner) != 1) return;
            Apply(owner, info);
        }

        private static void Apply(IntPtr owner, UpdateInfo info)
        {
            // Deliberately refuses rather than half-installing: without the
            // download-and-verify half of Check() implemented, there is nothing
            // safe to apply.
            Win32.MessageBoxW(owner,
                "This build has no release feed configured yet, so the update could not be installed.",
                "Update Unavailable", Win32.MB_OK | Win32.MB_ICONWARNING);
        }
    }
}
