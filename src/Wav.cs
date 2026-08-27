using System;
using System.Buffers.Binary;
using System.IO;

namespace Arp
{
    /// <summary>
    /// Writes RF64 (RIFF64) WAV files, the same container libsndfile produces
    /// for the Python build's format='RF64'. RF64 is what allows a recording to
    /// pass 4 GB, which a 48 kHz/24-bit/stereo session reaches in about 4 hours.
    /// </summary>
    internal sealed class Rf64Writer : IDisposable
    {
        // Header layout, fixed so the offsets the repair routine seeks to are
        // exactly where the Python build expects them:
        //   0  'RF64'     4  0xFFFFFFFF   8  'WAVE'
        //   12 'ds64'    16  28
        //   20 riffSize(u64)   28 dataSize(u64)   36 sampleCount(u64)   44 table(u32)=0
        //   48 'fmt '    52  16     56 PCM fmt body (16 bytes)
        //   72 'data'    76  0xFFFFFFFF     80 samples
        private const int HeaderSize = 80;
        private const int Ds64RiffSizeOffset = 20;
        private const int Ds64DataSizeOffset = 28;
        private const int Ds64SampleCountOffset = 36;

        private FileStream _fs;
        private readonly int _channels;
        private readonly int _sampleRate;
        private readonly int _bitDepth;
        private readonly int _bytesPerSample;
        private byte[] _scratch = Array.Empty<byte>();

        public string Path { get; }
        public long Frames { get; private set; }
        public long DataBytes => Frames * _channels * _bytesPerSample;
        public bool IsClosed => _fs == null;

        public Rf64Writer(string path, int sampleRate, int channels, int bitDepth)
        {
            if (bitDepth != 16 && bitDepth != 24 && bitDepth != 32)
                throw new ArgumentException("Unsupported bit depth: " + bitDepth);

            Path = path;
            _sampleRate = sampleRate;
            _channels = channels;
            _bitDepth = bitDepth;
            _bytesPerSample = bitDepth / 8;

            _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
            WriteHeader();
        }

        private void WriteHeader()
        {
            var h = new byte[HeaderSize];
            Ascii(h, 0, "RF64");
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4), 0xFFFFFFFF);
            Ascii(h, 8, "WAVE");

            Ascii(h, 12, "ds64");
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(16), 28);
            // riffSize / dataSize / sampleCount are filled in by UpdateSizes.
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(44), 0); // table length

            Ascii(h, 48, "fmt ");
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(52), 16);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(56), 1); // WAVE_FORMAT_PCM
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(58), (ushort)_channels);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(60), (uint)_sampleRate);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(64), (uint)(_sampleRate * _channels * _bytesPerSample));
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(68), (ushort)(_channels * _bytesPerSample));
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(70), (ushort)_bitDepth);

            Ascii(h, 72, "data");
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(76), 0xFFFFFFFF);

            _fs.Write(h, 0, h.Length);
        }

        private static void Ascii(byte[] buf, int offset, string s)
        {
            for (int i = 0; i < s.Length; i++) buf[offset + i] = (byte)s[i];
        }

        /// <summary>Writes interleaved normalised floats, clipped to [-1, 1].</summary>
        public void Write(float[] samples, int offset, int count)
        {
            if (_fs == null) throw new ObjectDisposedException(nameof(Rf64Writer));
            if (count <= 0) return;

            int needed = count * _bytesPerSample;
            if (_scratch.Length < needed) _scratch = new byte[Math.Max(needed, 1 << 16)];

            int p = 0;
            switch (_bitDepth)
            {
                case 16:
                    for (int i = 0; i < count; i++)
                    {
                        int v = (int)MathF.Round(Clamp(samples[offset + i]) * 32767f);
                        _scratch[p++] = (byte)v;
                        _scratch[p++] = (byte)(v >> 8);
                    }
                    break;
                case 24:
                    for (int i = 0; i < count; i++)
                    {
                        int v = (int)MathF.Round(Clamp(samples[offset + i]) * 8388607f);
                        _scratch[p++] = (byte)v;
                        _scratch[p++] = (byte)(v >> 8);
                        _scratch[p++] = (byte)(v >> 16);
                    }
                    break;
                default:
                    for (int i = 0; i < count; i++)
                    {
                        // 2147483647 is not representable as a float, so scale by
                        // 2^31 and clamp the positive edge down by one LSB.
                        double d = Math.Round((double)Clamp(samples[offset + i]) * 2147483647.0);
                        int v = d >= 2147483647.0 ? int.MaxValue : d <= -2147483648.0 ? int.MinValue : (int)d;
                        _scratch[p++] = (byte)v;
                        _scratch[p++] = (byte)(v >> 8);
                        _scratch[p++] = (byte)(v >> 16);
                        _scratch[p++] = (byte)(v >> 24);
                    }
                    break;
            }

            _fs.Write(_scratch, 0, p);
            Frames += count / _channels;
        }

        private static float Clamp(float v) => v > 1f ? 1f : v < -1f ? -1f : v;

        /// <summary>
        /// Pushes buffered bytes to the OS and rewrites the ds64 sizes so a file
        /// left behind by a crash is already playable. The Python build only
        /// fixes sizes on close, which is why it needs the repair prompt; doing
        /// it here on every flush makes that prompt a fallback rather than the
        /// primary recovery path.
        /// </summary>
        public void Flush(bool toDisk = false)
        {
            if (_fs == null) return;
            _fs.Flush();
            UpdateSizes();
            _fs.Flush();
            if (toDisk) _fs.Flush(true);
        }

        private void UpdateSizes()
        {
            long dataBytes = DataBytes;
            long pos = _fs.Position;
            long riffSize = HeaderSize + dataBytes - 8;

            Span<byte> u64 = stackalloc byte[8];

            _fs.Seek(Ds64RiffSizeOffset, SeekOrigin.Begin);
            BinaryPrimitives.WriteUInt64LittleEndian(u64, (ulong)riffSize);
            _fs.Write(u64);

            _fs.Seek(Ds64DataSizeOffset, SeekOrigin.Begin);
            BinaryPrimitives.WriteUInt64LittleEndian(u64, (ulong)dataBytes);
            _fs.Write(u64);

            _fs.Seek(Ds64SampleCountOffset, SeekOrigin.Begin);
            BinaryPrimitives.WriteUInt64LittleEndian(u64, (ulong)Frames);
            _fs.Write(u64);

            _fs.Seek(pos, SeekOrigin.Begin);
        }

        public void Close()
        {
            if (_fs == null) return;
            try
            {
                _fs.Flush();
                // RIFF chunks are word aligned; 24-bit mono can leave an odd
                // data length, so pad before recording the final sizes.
                if (DataBytes % 2 != 0)
                {
                    _fs.Seek(0, SeekOrigin.End);
                    _fs.WriteByte(0);
                }
                UpdateSizes();
                _fs.Flush(true);
            }
            finally
            {
                _fs.Dispose();
                _fs = null;
            }
        }

        public void Dispose() => Close();
    }

    internal static class WavFile
    {
        /// <summary>
        /// Port of the Python build's repair_wav_file: rebuilds the size fields
        /// of a RIFF or RF64 file that was never closed, keeping a .backup
        /// alongside until the result verifies.
        /// </summary>
        public static bool Repair(string filepath)
        {
            string backupPath = filepath + ".backup";
            try
            {
                long filesize = new FileInfo(filepath).Length;
                if (filesize < 44) return false;

                File.Copy(filepath, backupPath, true);

                using (var fs = new FileStream(filepath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var head = new byte[Math.Min(filesize, 8192)];
                    int read = fs.Read(head, 0, head.Length);
                    if (read < 16) throw new Exception("File too short");

                    string magic = System.Text.Encoding.ASCII.GetString(head, 0, 4);
                    if (magic != "RIFF" && magic != "RF64") return false;

                    int dataOffset = IndexOf(head, read, "data");
                    if (dataOffset < 0) throw new Exception("No data chunk found");

                    Span<byte> u32 = stackalloc byte[4];
                    Span<byte> u64 = stackalloc byte[8];

                    if (magic == "RIFF")
                    {
                        fs.Seek(4, SeekOrigin.Begin);
                        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)(filesize - 8));
                        fs.Write(u32);

                        int sizeOffset = dataOffset + 4;
                        fs.Seek(sizeOffset, SeekOrigin.Begin);
                        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)(filesize - (sizeOffset + 4)));
                        fs.Write(u32);
                    }
                    else
                    {
                        if (System.Text.Encoding.ASCII.GetString(head, 8, 4) != "WAVE")
                            throw new Exception("No WAVE signature");
                        if (System.Text.Encoding.ASCII.GetString(head, 12, 4) != "ds64")
                            throw new Exception("No ds64 chunk");

                        fs.Seek(4, SeekOrigin.Begin);
                        BinaryPrimitives.WriteUInt32LittleEndian(u32, 0xFFFFFFFF);
                        fs.Write(u32);

                        fs.Seek(20, SeekOrigin.Begin);
                        BinaryPrimitives.WriteUInt64LittleEndian(u64, (ulong)(filesize - 8));
                        fs.Write(u64);

                        fs.Seek(dataOffset + 4, SeekOrigin.Begin);
                        BinaryPrimitives.WriteUInt32LittleEndian(u32, 0xFFFFFFFF);
                        fs.Write(u32);

                        long actualDataSize = filesize - (dataOffset + 8);
                        fs.Seek(28, SeekOrigin.Begin);
                        BinaryPrimitives.WriteUInt64LittleEndian(u64, (ulong)actualDataSize);
                        fs.Write(u64);

                        // The Python version stops here and leaves sampleCount
                        // stale; filling it in keeps players that trust it from
                        // reporting the wrong duration.
                        var fmt = ReadFormat(head, read);
                        if (fmt.blockAlign > 0)
                        {
                            fs.Seek(36, SeekOrigin.Begin);
                            BinaryPrimitives.WriteUInt64LittleEndian(u64, (ulong)(actualDataSize / fmt.blockAlign));
                            fs.Write(u64);
                        }
                    }

                    fs.Flush(true);
                }

                if (!Verify(filepath)) throw new Exception("Validation failed");

                try { File.Delete(backupPath); } catch { }
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Error repairing WAV: " + e.Message);
                try
                {
                    if (File.Exists(backupPath)) File.Copy(backupPath, filepath, true);
                }
                catch
                {
                }
                return false;
            }
        }

        private static (int channels, int blockAlign, int sampleRate, int bits) ReadFormat(byte[] head, int len)
        {
            int f = IndexOf(head, len, "fmt ");
            if (f < 0 || f + 24 > len) return (0, 0, 0, 0);
            int body = f + 8;
            int channels = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(body + 2));
            int rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(body + 4));
            int blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(body + 12));
            int bits = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(body + 14));
            return (channels, blockAlign, rate, bits);
        }

        /// <summary>
        /// Stands in for the Python build's soundfile round-trip check: confirms
        /// the header parses and reports a positive frame count.
        /// </summary>
        public static bool Verify(string path, int expectedChannels = 0, int expectedRate = 0)
        {
            try
            {
                long size = new FileInfo(path).Length;
                if (size < 44) return false;

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var head = new byte[(int)Math.Min(size, 8192)];
                int read = fs.Read(head, 0, head.Length);
                if (read < 16) return false;

                string magic = System.Text.Encoding.ASCII.GetString(head, 0, 4);
                if (magic != "RIFF" && magic != "RF64") return false;
                if (System.Text.Encoding.ASCII.GetString(head, 8, 4) != "WAVE") return false;

                var fmt = ReadFormat(head, read);
                if (fmt.blockAlign <= 0) return false;

                int dataOffset = IndexOf(head, read, "data");
                if (dataOffset < 0) return false;

                long dataBytes;
                if (magic == "RF64")
                {
                    if (System.Text.Encoding.ASCII.GetString(head, 12, 4) != "ds64") return false;
                    dataBytes = (long)BinaryPrimitives.ReadUInt64LittleEndian(head.AsSpan(28));
                }
                else
                {
                    dataBytes = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(dataOffset + 4));
                }

                if (dataBytes <= 0) return false;
                if (dataBytes / fmt.blockAlign <= 0) return false;
                if (expectedChannels > 0 && fmt.channels != expectedChannels) return false;
                if (expectedRate > 0 && fmt.sampleRate != expectedRate) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int IndexOf(byte[] hay, int len, string needle)
        {
            for (int i = 0; i + needle.Length <= len; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (hay[i + j] != (byte)needle[j]) { ok = false; break; }
                }
                if (ok) return i;
            }
            return -1;
        }
    }
}
