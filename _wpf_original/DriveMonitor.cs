using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ArpCSharp
{
    public class DriveMonitor
    {
        public event EventHandler<string> Disconnected;

        private readonly Func<string> _getFolderFunc;
        private bool _isRunning;
        private bool _lastDriveStatus;
        private Task _monitorTask;
        private CancellationTokenSource _cts;

        public DriveMonitor(Func<string> getFolderFunc)
        {
            _getFolderFunc = getFolderFunc;
            _isRunning = false;

            string folder = _getFolderFunc();
            if (!string.IsNullOrEmpty(folder))
            {
                string drive = Path.GetPathRoot(folder)?.TrimEnd('\\')?.ToUpper();
                if (!string.IsNullOrEmpty(drive) && drive != "C:")
                {
                    string drivePath = drive + "\\";
                    _lastDriveStatus = Directory.Exists(drivePath);
                }
                else
                {
                    _lastDriveStatus = true;
                }
            }
            else
            {
                _lastDriveStatus = true;
            }
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            _cts = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorLoop(_cts.Token));
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _cts?.Cancel();
            try { _monitorTask?.Wait(); } catch { }
            _cts?.Dispose();
        }

        private async Task MonitorLoop(CancellationToken token)
        {
            while (_isRunning && !token.IsCancellationRequested)
            {
                string folder = _getFolderFunc();
                if (!string.IsNullOrEmpty(folder))
                {
                    string drive = Path.GetPathRoot(folder)?.TrimEnd('\\')?.ToUpper();
                    if (!string.IsNullOrEmpty(drive) && drive != "C:")
                    {
                        string drivePath = drive + "\\";
                        bool exists = Directory.Exists(drivePath);
                        if (!exists && _lastDriveStatus)
                        {
                            Disconnected?.Invoke(this, drive);
                            _lastDriveStatus = false;
                        }
                        else if (exists)
                        {
                            _lastDriveStatus = true;
                        }
                    }
                }

                try
                {
                    await Task.Delay(1000, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
