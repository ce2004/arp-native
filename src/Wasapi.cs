using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Arp
{
    internal sealed class AudioDevice
    {
        public string Id;
        public string Name;
        public bool IsLoopback;

        // Matches the Python build's combo labels so the two look identical.
        public string DisplayName => (IsLoopback ? "Output (Loopback): " : "Input: ") + Name;
    }

    // WASAPI accessed through raw vtable calls.
    //
    // NativeAOT does not support classic [ComImport] built-in COM marshalling,
    // and NAudio is built on it, so every interface used here is called through
    // its vtable slot with unmanaged function pointers instead. That is also why
    // there is no NuGet dependency left in the project.
    internal static unsafe class Wasapi
    {
        private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
        private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
        private static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
        private static readonly Guid IID_IMMEndpoint = new("1BE09788-6894-4089-8586-9A2A6C265AC5");
        private static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        private static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT = new("00000003-0000-0010-8000-00AA00389B71");

        private const int CLSCTX_ALL = 23;
        private const int DEVICE_STATE_ACTIVE = 0x1;
        private const int STGM_READ = 0;
        private const int eRender = 0;
        private const int eCapture = 1;

        private const int AUDCLNT_SHAREMODE_SHARED = 0;
        private const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
        private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        private const uint AUDCLNT_STREAMFLAGS_NOPERSIST = 0x00080000;
        private const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
        private const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
        private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

        public const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);
        public const int AUDCLNT_E_UNSUPPORTED_FORMAT = unchecked((int)0x88890008);
        public const int AUDCLNT_S_BUFFER_EMPTY = 0x08890001;

        private const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(in Guid clsid, IntPtr outer, int ctx, in Guid iid, out IntPtr obj);

        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr reserved, int flags);

        [DllImport("ole32.dll")]
        public static extern void CoUninitialize();

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr p);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(void* pv);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEventW(IntPtr attrs, bool manualReset, bool initialState, IntPtr name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr h, uint ms);

        public const int COINIT_APARTMENTTHREADED = 0x2;
        public const int COINIT_MULTITHREADED = 0x0;

        private static void** Vt(IntPtr p) => *(void***)p;

        public static int Release(IntPtr p)
        {
            if (p == IntPtr.Zero) return 0;
            return ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vt(p)[2])(p);
        }

        private static int QueryInterface(IntPtr p, in Guid iid, out IntPtr result)
        {
            fixed (Guid* pIid = &iid)
            fixed (IntPtr* pOut = &result)
                return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Vt(p)[0])(p, pIid, pOut);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WAVEFORMATEXTENSIBLE
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
            public ushort wValidBitsPerSample;
            public uint dwChannelMask;
            public Guid SubFormat;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        private static IntPtr CreateEnumerator()
        {
            int hr = CoCreateInstance(CLSID_MMDeviceEnumerator, IntPtr.Zero, CLSCTX_ALL,
                IID_IMMDeviceEnumerator, out IntPtr enumerator);
            if (hr < 0) throw new COMException("Could not create MMDeviceEnumerator", hr);
            return enumerator;
        }

        // Mirrors soundcard.all_microphones(include_loopback=True): every active
        // capture endpoint, plus every active render endpoint offered as a
        // loopback source. Endpoint ids match the ones the Python build stores,
        // so an existing recorder_config.json selects the same hardware here.
        public static List<AudioDevice> EnumerateDevices()
        {
            var list = new List<AudioDevice>();
            IntPtr enumerator = IntPtr.Zero;
            try
            {
                enumerator = CreateEnumerator();
                CollectEndpoints(enumerator, eCapture, false, list);
                CollectEndpoints(enumerator, eRender, true, list);
            }
            finally
            {
                Release(enumerator);
            }
            return list;
        }

        private static void CollectEndpoints(IntPtr enumerator, int dataFlow, bool loopback, List<AudioDevice> into)
        {
            IntPtr collection = IntPtr.Zero;
            try
            {
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, int, int, IntPtr*, int>)Vt(enumerator)[3])(
                    enumerator, dataFlow, DEVICE_STATE_ACTIVE, &collection);
                if (hr < 0 || collection == IntPtr.Zero) return;

                uint count = 0;
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Vt(collection)[3])(collection, &count);
                if (hr < 0) return;

                for (uint i = 0; i < count; i++)
                {
                    IntPtr device = IntPtr.Zero;
                    try
                    {
                        hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vt(collection)[4])(collection, i, &device);
                        if (hr < 0 || device == IntPtr.Zero) continue;

                        string id = GetDeviceId(device);
                        string name = GetFriendlyName(device);
                        if (id == null) continue;

                        into.Add(new AudioDevice { Id = id, Name = name ?? "Unknown Device", IsLoopback = loopback });
                    }
                    finally { Release(device); }
                }
            }
            finally { Release(collection); }
        }

        private static string GetDeviceId(IntPtr device)
        {
            IntPtr pstr = IntPtr.Zero;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vt(device)[5])(device, &pstr);
            if (hr < 0 || pstr == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(pstr); }
            finally { CoTaskMemFree(pstr); }
        }

        private static string GetFriendlyName(IntPtr device)
        {
            IntPtr store = IntPtr.Zero;
            try
            {
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Vt(device)[4])(device, STGM_READ, &store);
                if (hr < 0 || store == IntPtr.Zero) return null;

                var key = new PROPERTYKEY
                {
                    fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
                    pid = 14,
                };

                // PROPVARIANT is 24 bytes on 64-bit; the value union starts at
                // offset 8 and holds an LPWSTR for VT_LPWSTR (31).
                byte* pv = stackalloc byte[32];
                for (int i = 0; i < 32; i++) pv[i] = 0;

                hr = ((delegate* unmanaged[Stdcall]<IntPtr, PROPERTYKEY*, void*, int>)Vt(store)[5])(store, &key, pv);
                if (hr < 0) return null;

                try
                {
                    ushort vt = *(ushort*)pv;
                    if (vt != 31) return null;
                    IntPtr sp = *(IntPtr*)(pv + 8);
                    return sp == IntPtr.Zero ? null : Marshal.PtrToStringUni(sp);
                }
                finally { PropVariantClear(pv); }
            }
            finally { Release(store); }
        }

        private static IntPtr GetDeviceById(IntPtr enumerator, string id)
        {
            IntPtr device = IntPtr.Zero;
            fixed (char* pid = id)
            {
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, char*, IntPtr*, int>)Vt(enumerator)[5])(enumerator, pid, &device);
                if (hr < 0) throw new COMException("Device not found: " + id, hr);
            }
            return device;
        }

        // A live capture stream. Always hands back interleaved 32-bit float at
        // the requested rate and channel count; Windows' own converter does any
        // resampling, which is what the "select the EXACT rate your device is
        // set to" warning in settings is about.
        internal sealed class CaptureStream : IDisposable
        {
            private IntPtr _device;
            private IntPtr _client;
            private IntPtr _capture;
            private IntPtr _dataReady;
            private bool _started;

            /// <summary>True when the audio engine signals us instead of being polled.</summary>
            public bool IsEventDriven => _dataReady != IntPtr.Zero;

            /// <summary>
            /// Blocks until the engine says a buffer is ready, or the timeout
            /// elapses. Returns false on timeout so the caller stays responsive
            /// to a stop request. Falls back to a plain sleep when the device
            /// would not accept event mode.
            /// </summary>
            public bool WaitForData(int timeoutMs)
            {
                if (_dataReady == IntPtr.Zero)
                {
                    System.Threading.Thread.Sleep(timeoutMs);
                    return true;
                }
                return WaitForSingleObject(_dataReady, (uint)timeoutMs) == 0;
            }

            public int SampleRate { get; private set; }
            public int Channels { get; private set; }
            public bool IsLoopback { get; private set; }

            public static CaptureStream Open(string deviceId, bool loopback, int sampleRate, int channels)
            {
                var s = new CaptureStream { IsLoopback = loopback, SampleRate = sampleRate, Channels = channels };
                IntPtr enumerator = IntPtr.Zero;
                try
                {
                    enumerator = CreateEnumerator();
                    s._device = GetDeviceById(enumerator, deviceId);

                    IntPtr client;
                    fixed (Guid* iid = &IID_IAudioClient)
                    {
                        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, int, void*, IntPtr*, int>)Vt(s._device)[3])(
                            s._device, iid, CLSCTX_ALL, null, &client);
                        if (hr < 0) throw new COMException("Activate(IAudioClient) failed", hr);
                    }
                    s._client = client;

                    var wfx = new WAVEFORMATEXTENSIBLE
                    {
                        wFormatTag = WAVE_FORMAT_EXTENSIBLE,
                        nChannels = (ushort)channels,
                        nSamplesPerSec = (uint)sampleRate,
                        wBitsPerSample = 32,
                        nBlockAlign = (ushort)(channels * 4),
                        nAvgBytesPerSec = (uint)(sampleRate * channels * 4),
                        cbSize = 22,
                        wValidBitsPerSample = 32,
                        dwChannelMask = channels == 1 ? 0x4u : 0x3u,
                        SubFormat = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT,
                    };

                    uint flags = AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY |
                                 AUDCLNT_STREAMFLAGS_NOPERSIST;
                    if (loopback) flags |= AUDCLNT_STREAMFLAGS_LOOPBACK;

                    // One second of ring buffer. Generous on purpose: an overrun
                    // would silently lose audio.
                    const long oneSecond = 10_000_000;

                    // Ask the audio engine to signal an event when a buffer is
                    // ready, so the reader sleeps until there is work instead of
                    // waking on a timer to ask. Not every device accepts this
                    // alongside the format converter, so a rejection falls back
                    // to polling rather than failing the recording.
                    int init = ((delegate* unmanaged[Stdcall]<IntPtr, int, uint, long, long, WAVEFORMATEXTENSIBLE*, Guid*, int>)Vt(s._client)[3])(
                        s._client, AUDCLNT_SHAREMODE_SHARED, flags | AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                        oneSecond, 0, &wfx, null);

                    if (init >= 0)
                    {
                        s._dataReady = CreateEventW(IntPtr.Zero, false, false, IntPtr.Zero);
                        int hr = s._dataReady == IntPtr.Zero
                            ? -1
                            : ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Vt(s._client)[13])(s._client, s._dataReady);
                        if (hr < 0)
                        {
                            Log.Info("SetEventHandle refused; falling back to polling.");
                            if (s._dataReady != IntPtr.Zero) { CloseHandle(s._dataReady); s._dataReady = IntPtr.Zero; }
                            init = -1;
                        }
                    }

                    if (init < 0)
                    {
                        // A client that failed Initialize cannot be reused, so
                        // activate a fresh one for the polling attempt.
                        Release(s._client);
                        s._client = IntPtr.Zero;
                        fixed (Guid* iid = &IID_IAudioClient)
                        {
                            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, int, void*, IntPtr*, int>)Vt(s._device)[3])(
                                s._device, iid, CLSCTX_ALL, null, &client);
                            if (hr < 0) throw new COMException("Activate(IAudioClient) failed", hr);
                        }
                        s._client = client;

                        init = ((delegate* unmanaged[Stdcall]<IntPtr, int, uint, long, long, WAVEFORMATEXTENSIBLE*, Guid*, int>)Vt(s._client)[3])(
                            s._client, AUDCLNT_SHAREMODE_SHARED, flags, oneSecond, 0, &wfx, null);
                    }

                    if (init < 0)
                        throw new COMException(
                            "IAudioClient.Initialize failed (0x" + init.ToString("X8") + "). The device may not support " +
                            sampleRate + " Hz. Set the sample rate in Settings to match the device's Windows format.", init);

                    IntPtr capture;
                    fixed (Guid* iid = &IID_IAudioCaptureClient)
                    {
                        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Vt(s._client)[14])(s._client, iid, &capture);
                        if (hr < 0) throw new COMException("GetService(IAudioCaptureClient) failed", hr);
                    }
                    s._capture = capture;

                    int start = ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vt(s._client)[10])(s._client);
                    if (start < 0) throw new COMException("IAudioClient.Start failed", start);
                    s._started = true;

                    return s;
                }
                catch
                {
                    s.Dispose();
                    throw;
                }
                finally
                {
                    Release(enumerator);
                }
            }

            public static bool IsRenderEndpoint(string deviceId)
            {
                IntPtr enumerator = IntPtr.Zero, device = IntPtr.Zero, endpoint = IntPtr.Zero;
                try
                {
                    enumerator = CreateEnumerator();
                    device = GetDeviceById(enumerator, deviceId);
                    if (QueryInterface(device, IID_IMMEndpoint, out endpoint) < 0) return false;
                    int flow = -1;
                    int hr = ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Vt(endpoint)[3])(endpoint, &flow);
                    return hr >= 0 && flow == eRender;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    Release(endpoint);
                    Release(device);
                    Release(enumerator);
                }
            }

            // WASAPI hands over whole packets (one device period, typically 480
            // frames at 48 kHz) and ReleaseBuffer is all-or-nothing: a partially
            // consumed packet cannot be handed back. Since the caller reads in
            // blocks that are not a whole number of packets, the tail of a
            // packet has to be held here until the next call, or that audio is
            // silently lost.
            private float[] _carry = Array.Empty<float>();
            private int _carryOffset;
            private int _carryLength;

            /// <summary>
            /// Drains whatever WASAPI has ready into <paramref name="dest"/>.
            /// Returns the number of floats written. Throws COMException with
            /// AUDCLNT_E_DEVICE_INVALIDATED if the device went away.
            /// </summary>
            public int Read(float[] dest, int destOffset, int maxFloats)
            {
                int written = 0;

                if (_carryLength > 0)
                {
                    int fromCarry = Math.Min(_carryLength, maxFloats);
                    Array.Copy(_carry, _carryOffset, dest, destOffset, fromCarry);
                    _carryOffset += fromCarry;
                    _carryLength -= fromCarry;
                    if (_carryLength == 0) _carryOffset = 0;
                    written += fromCarry;
                    if (written == maxFloats) return written;
                }

                while (written < maxFloats)
                {
                    uint packet = 0;
                    int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Vt(_capture)[5])(_capture, &packet);
                    if (hr < 0) throw new COMException("GetNextPacketSize failed", hr);
                    if (packet == 0) break;

                    byte* data;
                    uint frames = 0;
                    uint bufFlags = 0;
                    hr = ((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, ulong*, ulong*, int>)Vt(_capture)[3])(
                        _capture, &data, &frames, &bufFlags, null, null);
                    if (hr == AUDCLNT_S_BUFFER_EMPTY) break;
                    if (hr < 0) throw new COMException("GetBuffer failed", hr);

                    int floats = (int)frames * Channels;
                    int take = Math.Min(floats, maxFloats - written);
                    int rest = floats - take;
                    bool silent = (bufFlags & AUDCLNT_BUFFERFLAGS_SILENT) != 0;

                    if (take > 0)
                    {
                        if (silent) Array.Clear(dest, destOffset + written, take);
                        else
                            fixed (float* pd = &dest[destOffset + written])
                                Buffer.MemoryCopy(data, pd, (long)take * 4, (long)take * 4);
                        written += take;
                    }

                    if (rest > 0)
                    {
                        if (_carry.Length < rest) _carry = new float[Math.Max(rest, 4096)];
                        if (silent) Array.Clear(_carry, 0, rest);
                        else
                            fixed (float* pc = _carry)
                                Buffer.MemoryCopy(data + (long)take * 4, pc, (long)rest * 4, (long)rest * 4);
                        _carryOffset = 0;
                        _carryLength = rest;
                    }

                    hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, int>)Vt(_capture)[4])(_capture, frames);
                    if (hr < 0) throw new COMException("ReleaseBuffer failed", hr);

                    if (rest > 0) break; // dest is full; the tail is held over
                }
                return written;
            }

            public void Dispose()
            {
                try
                {
                    if (_started && _client != IntPtr.Zero)
                    {
                        ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vt(_client)[11])(_client);
                        _started = false;
                    }
                }
                catch
                {
                }
                Release(_capture); _capture = IntPtr.Zero;
                Release(_client); _client = IntPtr.Zero;
                Release(_device); _device = IntPtr.Zero;
                if (_dataReady != IntPtr.Zero) { CloseHandle(_dataReady); _dataReady = IntPtr.Zero; }
            }
        }
    }
}
