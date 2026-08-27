using System;
using System.IO;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;

namespace AudioRecorderPro
{
    public class AudioEngine : IDisposable
    {
        private WasapiCapture micCapture;
        private WasapiLoopbackCapture loopbackCapture;
        
        private BufferedWaveProvider micBuffer;
        private BufferedWaveProvider loopbackBuffer;
        
        private WaveFileWriter writer;
        private Thread writeThread;
        private bool isRecording;
        private bool isPaused;
        private bool isStopping;

        private string outputFolder;
        private string filePrefix;
        private string currentFilename;
        private int splitSeconds;
        private DateTime currentSplitStartTime;

        public AudioEngine(string micDeviceId, string loopbackDeviceId, string folder, string prefix, int bufferSize, int splitSecs)
        {
            outputFolder = folder;
            filePrefix = prefix;
            splitSeconds = splitSecs;

            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            var enumerator = new MMDeviceEnumerator();
            
            MMDevice micDevice = null;
            if (!string.IsNullOrEmpty(micDeviceId) && micDeviceId != "none")
            {
                micDevice = enumerator.GetDevice(micDeviceId);
            }
            else
            {
                micDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            }

            MMDevice loopbackDevice = null;
            if (!string.IsNullOrEmpty(loopbackDeviceId) && loopbackDeviceId != "none")
            {
                loopbackDevice = enumerator.GetDevice(loopbackDeviceId);
            }
            else
            {
                loopbackDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }

            if (micDevice != null)
            {
                micCapture = new WasapiCapture(micDevice, true, bufferSize);
                micCapture.DataAvailable += MicCapture_DataAvailable;
                micBuffer = new BufferedWaveProvider(micCapture.WaveFormat);
                micBuffer.DiscardOnBufferOverflow = true;
            }

            if (loopbackDevice != null)
            {
                loopbackCapture = new WasapiLoopbackCapture(loopbackDevice);
                loopbackCapture.DataAvailable += LoopbackCapture_DataAvailable;
                loopbackBuffer = new BufferedWaveProvider(loopbackCapture.WaveFormat);
                loopbackBuffer.DiscardOnBufferOverflow = true;
            }
        }

        private void MicCapture_DataAvailable(object sender, WaveInEventArgs e)
        {
            if (isRecording && !isPaused && micBuffer != null)
            {
                micBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            }
        }

        private void LoopbackCapture_DataAvailable(object sender, WaveInEventArgs e)
        {
            if (isRecording && !isPaused && loopbackBuffer != null)
            {
                loopbackBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            }
        }

        public void Start()
        {
            if (isRecording) return;
            isRecording = true;
            isPaused = false;
            isStopping = false;

            StartNewFile();

            micCapture?.StartRecording();
            loopbackCapture?.StartRecording();

            writeThread = new Thread(WriteLoop);
            writeThread.IsBackground = true;
            writeThread.Start();
        }

        public void Pause()
        {
            isPaused = true;
        }

        public void Resume()
        {
            isPaused = false;
        }

        public void Stop()
        {
            if (!isRecording) return;
            isStopping = true;
            isRecording = false;

            micCapture?.StopRecording();
            loopbackCapture?.StopRecording();

            if (writeThread != null && writeThread.IsAlive)
            {
                writeThread.Join(2000);
            }

            CloseFile();
        }

        private void StartNewFile()
        {
            CloseFile();
            
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            currentFilename = Path.Combine(outputFolder, $"{filePrefix}_{timestamp}.wav");
            
            // For mixing, we'll align everything to the loopback format or standard 48k 16-bit 2ch
            var format = loopbackCapture?.WaveFormat ?? micCapture?.WaveFormat ?? new WaveFormat(48000, 16, 2);
            writer = new WaveFileWriter(currentFilename, format);
            currentSplitStartTime = DateTime.Now;
        }

        private void CloseFile()
        {
            if (writer != null)
            {
                writer.Flush();
                writer.Dispose();
                writer = null;
            }
        }

        private void WriteLoop()
        {
            var format = loopbackCapture?.WaveFormat ?? micCapture?.WaveFormat ?? new WaveFormat(48000, 16, 2);
            
            ISampleProvider micSampleProvider = null;
            if (micBuffer != null)
            {
                micSampleProvider = micBuffer.ToSampleProvider();
                // Ensure channels match
                if (micSampleProvider.WaveFormat.Channels == 1 && format.Channels == 2)
                    micSampleProvider = new MonoToStereoSampleProvider(micSampleProvider);
                // Ensure sample rate matches
                if (micSampleProvider.WaveFormat.SampleRate != format.SampleRate)
                    micSampleProvider = new WdlResamplingSampleProvider(micSampleProvider, format.SampleRate);
            }

            ISampleProvider loopbackSampleProvider = null;
            if (loopbackBuffer != null)
            {
                loopbackSampleProvider = loopbackBuffer.ToSampleProvider();
                if (loopbackSampleProvider.WaveFormat.Channels == 1 && format.Channels == 2)
                    loopbackSampleProvider = new MonoToStereoSampleProvider(loopbackSampleProvider);
                if (loopbackSampleProvider.WaveFormat.SampleRate != format.SampleRate)
                    loopbackSampleProvider = new WdlResamplingSampleProvider(loopbackSampleProvider, format.SampleRate);
            }

            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels));
            if (micSampleProvider != null) mixer.AddMixerInput(micSampleProvider);
            if (loopbackSampleProvider != null) mixer.AddMixerInput(loopbackSampleProvider);

            int bufferSize = format.SampleRate * format.Channels;
            float[] mixBuffer = new float[bufferSize];

            while (isRecording || (isStopping && (micBuffer?.BufferedBytes > 0 || loopbackBuffer?.BufferedBytes > 0)))
            {
                if (isPaused && !isStopping)
                {
                    Thread.Sleep(50);
                    continue;
                }

                if (splitSeconds > 0 && (DateTime.Now - currentSplitStartTime).TotalSeconds >= splitSeconds)
                {
                    StartNewFile();
                }

                int samplesRead = mixer.Read(mixBuffer, 0, bufferSize);
                if (samplesRead > 0 && writer != null)
                {
                    writer.WriteSamples(mixBuffer, 0, samplesRead);
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }

        public void Dispose()
        {
            Stop();
            micCapture?.Dispose();
            loopbackCapture?.Dispose();
        }
    }
}
