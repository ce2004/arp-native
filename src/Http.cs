using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Arp
{
    /// <summary>
    /// A minimal HTTPS GET over WinHTTP.
    ///
    /// HttpClient would be the obvious choice, but pulling System.Net.Http into
    /// a NativeAOT image costs roughly three megabytes - it more than doubled
    /// this executable, which exists to be small. WinHTTP is already part of
    /// Windows, follows redirects on its own (which the release download needs),
    /// and honours the system proxy, so the whole update path costs nothing in
    /// binary size.
    /// </summary>
    internal static class Http
    {
        private const uint WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY = 4;
        private const uint WINHTTP_FLAG_SECURE = 0x00800000;
        private const uint WINHTTP_QUERY_STATUS_CODE = 19;
        private const uint WINHTTP_QUERY_FLAG_NUMBER = 0x20000000;
        private const uint WINHTTP_ADDREQ_FLAG_ADD = 0x20000000;
        private const ushort INTERNET_DEFAULT_HTTPS_PORT = 443;

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr WinHttpOpen(string agent, uint accessType, string proxy,
            string proxyBypass, uint flags);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr WinHttpConnect(IntPtr session, string serverName, ushort port, uint reserved);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr WinHttpOpenRequest(IntPtr connect, string verb, string objectName,
            string version, string referrer, IntPtr acceptTypes, uint flags);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WinHttpAddRequestHeaders(IntPtr request, string headers, uint length, uint modifiers);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpSendRequest(IntPtr request, IntPtr headers, uint headersLength,
            IntPtr optional, uint optionalLength, uint totalLength, IntPtr context);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpReceiveResponse(IntPtr request, IntPtr reserved);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WinHttpQueryHeaders(IntPtr request, uint infoLevel, IntPtr name,
            ref uint buffer, ref uint bufferLength, IntPtr index);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpQueryDataAvailable(IntPtr request, out uint available);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpReadData(IntPtr request, byte[] buffer, uint toRead, out uint read);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpSetTimeouts(IntPtr handle, int resolve, int connect, int send, int receive);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpCloseHandle(IntPtr handle);

        public static string GetString(string url, int timeoutSeconds = 30) =>
            System.Text.Encoding.UTF8.GetString(Get(url, timeoutSeconds)).TrimStart('﻿');

        /// <summary>Performs an HTTPS GET and returns the whole body.</summary>
        public static byte[] Get(string url, int timeoutSeconds = 30)
        {
            var uri = new Uri(url);
            if (uri.Scheme != Uri.UriSchemeHttps)
                throw new NotSupportedException("Only https is supported: " + url);

            IntPtr session = IntPtr.Zero, connect = IntPtr.Zero, request = IntPtr.Zero;
            try
            {
                session = WinHttpOpen("AudioRecorderPro/" + Updater.CurrentVersion,
                    WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, null, null, 0);
                if (session == IntPtr.Zero) throw Fail("WinHttpOpen");

                int ms = Math.Max(1, timeoutSeconds) * 1000;
                WinHttpSetTimeouts(session, ms, ms, ms, ms);

                connect = WinHttpConnect(session, uri.Host, (ushort)(uri.IsDefaultPort
                    ? INTERNET_DEFAULT_HTTPS_PORT : uri.Port), 0);
                if (connect == IntPtr.Zero) throw Fail("WinHttpConnect");

                request = WinHttpOpenRequest(connect, "GET", uri.PathAndQuery, null, null,
                    IntPtr.Zero, WINHTTP_FLAG_SECURE);
                if (request == IntPtr.Zero) throw Fail("WinHttpOpenRequest");

                // GitHub rejects requests with no User-Agent, and asks for this
                // Accept header on its API.
                const string headers = "Accept: application/vnd.github+json\r\n";
                WinHttpAddRequestHeaders(request, headers, unchecked((uint)-1), WINHTTP_ADDREQ_FLAG_ADD);

                if (!WinHttpSendRequest(request, IntPtr.Zero, 0, IntPtr.Zero, 0, 0, IntPtr.Zero))
                    throw Fail("WinHttpSendRequest");
                if (!WinHttpReceiveResponse(request, IntPtr.Zero))
                    throw Fail("WinHttpReceiveResponse");

                uint status = 0, size = sizeof(uint);
                if (!WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                        IntPtr.Zero, ref status, ref size, IntPtr.Zero))
                    throw Fail("WinHttpQueryHeaders");

                if (status < 200 || status >= 300)
                    throw new IOException("HTTP " + status + " from " + uri.Host + uri.AbsolutePath);

                using var ms2 = new MemoryStream();
                var buffer = new byte[65536];
                while (true)
                {
                    if (!WinHttpQueryDataAvailable(request, out uint available)) throw Fail("WinHttpQueryDataAvailable");
                    if (available == 0) break;

                    uint want = Math.Min(available, (uint)buffer.Length);
                    if (!WinHttpReadData(request, buffer, want, out uint read)) throw Fail("WinHttpReadData");
                    if (read == 0) break;
                    ms2.Write(buffer, 0, (int)read);
                }
                return ms2.ToArray();
            }
            finally
            {
                if (request != IntPtr.Zero) WinHttpCloseHandle(request);
                if (connect != IntPtr.Zero) WinHttpCloseHandle(connect);
                if (session != IntPtr.Zero) WinHttpCloseHandle(session);
            }
        }

        private static IOException Fail(string what) =>
            new IOException(what + " failed (Win32 error " + Marshal.GetLastWin32Error() + ")");
    }
}
