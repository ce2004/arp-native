using System;
using System.IO;
using System.Threading;

namespace Arp
{
    /// <summary>
    /// Watches the drive holding the output folder and reports the moment it
    /// goes away. Only non-system drives are watched, matching the Python build:
    /// C: disappearing is not a scenario worth polling for.
    /// </summary>
    internal sealed class DriveMonitor
    {
        private readonly Func<string> _getFolder;
        private readonly Action<string> _onDisconnected;
        private Thread _thread;
        private volatile bool _running;
        private bool _lastStatus;

        // Waiting on an event instead of sleeping means Stop returns at once.
        // Sleeping meant the shutdown had to sit out whatever remained of the
        // current second, which was most of the delay when closing the window.
        private readonly ManualResetEventSlim _wake = new(false);

        /// <summary>
        /// How often the drive is polled. Only a backstop: removal normally
        /// arrives instantly as a WM_DEVICECHANGE broadcast, so this can be
        /// slow and cheap rather than once a second.
        /// </summary>
        private const int PollMs = 5000;

        public DriveMonitor(Func<string> getFolder, Action<string> onDisconnected)
        {
            _getFolder = getFolder;
            _onDisconnected = onDisconnected;
            _lastStatus = CheckPresent(_getFolder());
        }

        private static bool CheckPresent(string folder)
        {
            string root = Recorder.DriveRootOf(folder);
            if (root == null || Recorder.IsSystemDrive(root)) return true;
            try { return Directory.Exists(root); }
            catch { return true; }
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _wake.Reset();
            _lastStatus = CheckPresent(_getFolder());
            _thread = new Thread(Run) { IsBackground = true, Name = "DriveMonitor" };
            _thread.Start();
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _wake.Set();
            try { _thread?.Join(1000); } catch { }
            _thread = null;
        }

        public bool IsRunning => _running;

        /// <summary>
        /// Re-checks immediately. Called when Windows broadcasts a volume
        /// change, so removal is noticed without waiting for the next poll.
        /// </summary>
        public void CheckNow()
        {
            if (!_running) return;
            _wake.Set();
        }

        private void Run()
        {
            while (_running)
            {
                try
                {
                    string folder = _getFolder();
                    string root = Recorder.DriveRootOf(folder);
                    if (root != null && !Recorder.IsSystemDrive(root))
                    {
                        bool exists = Directory.Exists(root);
                        if (!exists && _lastStatus)
                        {
                            _lastStatus = false;
                            _onDisconnected?.Invoke(root.TrimEnd('\\', '/'));
                        }
                        else if (exists)
                        {
                            _lastStatus = true;
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Warn("Drive monitor error: " + e.Message);
                }

                _wake.Wait(PollMs);
                _wake.Reset();
            }
        }
    }

    /// <summary>
    /// After an unattended failure, waits for the output drive and both
    /// configured inputs to come back, then signals a restart.
    /// </summary>
    internal sealed class AutoResumeWatcher
    {
        private readonly string _folder;
        private readonly string _mic1Id;
        private readonly string _mic2Id;
        private readonly Action<string> _onResume;
        private Thread _thread;
        private volatile bool _running;
        private readonly ManualResetEventSlim _wake = new(false);

        public string Missing { get; set; }
        public bool IsRunning => _running;

        public AutoResumeWatcher(string folder, string mic1Id, string mic2Id, string missing, Action<string> onResume)
        {
            _folder = folder;
            _mic1Id = mic1Id;
            _mic2Id = mic2Id;
            Missing = missing;
            _onResume = onResume;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Run) { IsBackground = true, Name = "AutoResume" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _wake.Set();
        }

        private void Run()
        {
            // A COM apartment is needed on this thread to enumerate endpoints.
            Wasapi.CoInitializeEx(IntPtr.Zero, Wasapi.COINIT_MULTITHREADED);
            try
            {
                while (_running)
                {
                    try
                    {
                        bool driveOk = true;
                        string root = Recorder.DriveRootOf(_folder);
                        if (root != null && !Recorder.IsSystemDrive(root))
                            driveOk = Directory.Exists(root);

                        bool mic1Ok = false;
                        bool mic2Ok = _mic2Id == "none";

                        if (driveOk)
                        {
                            try
                            {
                                var devices = Wasapi.EnumerateDevices();
                                foreach (var d in devices)
                                {
                                    if (d.Id == _mic1Id) mic1Ok = true;
                                    if (_mic2Id != "none" && d.Id == _mic2Id) mic2Ok = true;
                                }
                            }
                            catch
                            {
                            }
                        }

                        if (driveOk && mic1Ok && mic2Ok)
                        {
                            _running = false;
                            _onResume?.Invoke(Missing);
                            return;
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Warn("Auto-resume watcher error: " + e.Message);
                    }
                    _wake.Wait(1000);
                    _wake.Reset();
                }
            }
            finally
            {
                Wasapi.CoUninitialize();
            }
        }
    }
}
