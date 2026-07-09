using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;


namespace VoiceChatLauncher
{
    internal sealed class OpenWakeWordOptions
    {
        public string BaseDirectory;
        public string Models;
        public string MelspectrogramModelPath;
        public string EmbeddingModelPath;
        public float Threshold;
        public int CooldownMilliseconds;
        public string Device;
        public bool LogScores;
    }

    internal sealed class OpenWakeWordLogEventArgs : EventArgs
    {
        public OpenWakeWordLogEventArgs(string message)
        {
            Message = message;
        }

        public string Message { get; private set; }
    }

    internal sealed class WakeWordDetectedEventArgs : EventArgs
    {
        public WakeWordDetectedEventArgs(string name, float score)
        {
            Name = name;
            Score = score;
        }

        public string Name { get; private set; }
        public float Score { get; private set; }
    }

    internal sealed class OpenWakeWordListener : IDisposable
    {
        private readonly OpenWakeWordOptions _options;
        private OpenWakeWordRuntime _runtime;
        private WaveInAudioSource _audioSource;
        private DateTime _lastWake = DateTime.MinValue;
        private bool _disposed;

        public event EventHandler<OpenWakeWordLogEventArgs> LogMessage;
        public event EventHandler<WakeWordDetectedEventArgs> WakeWordDetected;

        public OpenWakeWordListener(OpenWakeWordOptions options)
        {
            _options = options;
        }

        public bool IsRunning
        {
            get { return _audioSource != null && _audioSource.IsRunning; }
        }

        public void Start()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("OpenWakeWordListener");
            }

            _runtime = new OpenWakeWordRuntime(_options, Log);
            int deviceId = WaveInAudioSource.ParseDeviceId(_options.Device);
            _audioSource = new WaveInAudioSource(deviceId, 16000, 1, 1280);
            _audioSource.AudioAvailable += OnAudioAvailable;
            _audioSource.Start();

            Log("READY models=" + _options.Models +
                " threshold=" + _options.Threshold.ToString("0.###", CultureInfo.InvariantCulture) +
                " cooldown_ms=" + _options.CooldownMilliseconds.ToString(CultureInfo.InvariantCulture) +
                " device=" + _audioSource.DeviceDescription);
        }

        private void OnAudioAvailable(object sender, AudioFrameEventArgs e)
        {
            try
            {
                WakeWordPrediction prediction = _runtime.Predict(e.Samples);
                if (_options.LogScores && !string.IsNullOrEmpty(prediction.Name))
                {
                    Log("SCORE " + prediction.Name + " " + prediction.Score.ToString("0.000", CultureInfo.InvariantCulture));
                }

                if (!string.IsNullOrEmpty(prediction.Name) && prediction.Score >= _options.Threshold)
                {
                    DateTime now = DateTime.Now;
                    if ((now - _lastWake).TotalMilliseconds >= _options.CooldownMilliseconds)
                    {
                        _lastWake = now;
                        EventHandler<WakeWordDetectedEventArgs> handler = WakeWordDetected;
                        if (handler != null)
                        {
                            handler(this, new WakeWordDetectedEventArgs(prediction.Name, prediction.Score));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ERROR " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void Log(string message)
        {
            EventHandler<OpenWakeWordLogEventArgs> handler = LogMessage;
            if (handler != null)
            {
                handler(this, new OpenWakeWordLogEventArgs(message));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_audioSource != null)
            {
                _audioSource.AudioAvailable -= OnAudioAvailable;
                _audioSource.Dispose();
                _audioSource = null;
            }

            if (_runtime != null)
            {
                _runtime.Dispose();
                _runtime = null;
            }
        }
    }

    internal struct WakeWordPrediction
    {
        public string Name;
        public float Score;
    }

    internal sealed class OpenWakeWordRuntime : IDisposable
    {
        private readonly AudioFeatureExtractor _features;
        private readonly List<WakeWordModel> _models = new List<WakeWordModel>();
        private readonly Dictionary<string, Queue<float>> _predictionBuffers = new Dictionary<string, Queue<float>>();

        public OpenWakeWordRuntime(OpenWakeWordOptions options, Action<string> log)
        {
            if (!File.Exists(options.MelspectrogramModelPath))
            {
                throw new FileNotFoundException("OpenWakeWord melspectrogram model が見つかりません。setup_openwakeword.ps1 を実行してください。", options.MelspectrogramModelPath);
            }

            if (!File.Exists(options.EmbeddingModelPath))
            {
                throw new FileNotFoundException("OpenWakeWord embedding model が見つかりません。setup_openwakeword.ps1 を実行してください。", options.EmbeddingModelPath);
            }

            _features = new AudioFeatureExtractor(options.MelspectrogramModelPath, options.EmbeddingModelPath);

            foreach (string item in SplitModelList(options.Models))
            {
                string modelPath = ResolveModelPath(options.BaseDirectory, item);
                if (!File.Exists(modelPath))
                {
                    throw new FileNotFoundException("OpenWakeWord model が見つかりません。", modelPath);
                }

                var model = new WakeWordModel(item, modelPath);
                _models.Add(model);
                if (!_predictionBuffers.ContainsKey(model.Name))
                {
                    _predictionBuffers.Add(model.Name, new Queue<float>());
                }

                if (log != null)
                {
                    log("Loaded model " + model.Name + " from " + modelPath);
                }
            }

            if (_models.Count == 0)
            {
                throw new InvalidOperationException("OpenWakeWordModels が空です。");
            }
        }

        public WakeWordPrediction Predict(short[] audio)
        {
            int preparedSamples = _features.Process(audio);
            var best = new WakeWordPrediction();
            best.Name = string.Empty;
            best.Score = 0;

            foreach (WakeWordModel model in _models)
            {
                float[] input = _features.GetFeatures(model.InputFrames);
                float score = model.Predict(input);
                Queue<float> buffer = _predictionBuffers[model.Name];

                if (buffer.Count < 5)
                {
                    score = 0;
                }

                buffer.Enqueue(score);
                while (buffer.Count > 30)
                {
                    buffer.Dequeue();
                }

                if (preparedSamples >= 1280 && score > best.Score)
                {
                    best.Name = model.Name;
                    best.Score = score;
                }
            }

            return best;
        }

        public void Dispose()
        {
            foreach (WakeWordModel model in _models)
            {
                model.Dispose();
            }

            _models.Clear();
            if (_features != null)
            {
                _features.Dispose();
            }
        }

        private static IEnumerable<string> SplitModelList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield break;
            }

            string[] parts = value.Replace(",", "|").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    yield return trimmed;
                }
            }
        }

        private static string ResolveModelPath(string baseDirectory, string model)
        {
            if (model.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(Path.IsPathRooted(model) ? model : Path.Combine(baseDirectory, model));
            }

            string fileName = model.Replace(" ", "_") + "_v0.1.onnx";
            return Path.GetFullPath(Path.Combine(baseDirectory, "..", "models", fileName));
        }
    }

    internal sealed class WakeWordModel : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly string _inputName;

        public WakeWordModel(string configuredName, string path)
        {
            Name = CreateModelName(configuredName, path);
            _session = CreateSession(path);
            _inputName = _session.InputMetadata.Keys.First();
            InputFrames = 16;
            int[] dimensions = _session.InputMetadata[_inputName].Dimensions;
            if (dimensions != null && dimensions.Length >= 2 && dimensions[1] > 0)
            {
                InputFrames = dimensions[1];
            }
        }

        public string Name { get; private set; }
        public int InputFrames { get; private set; }

        public float Predict(float[] features)
        {
            var tensor = new DenseTensor<float>(features, new[] { 1, InputFrames, 96 });
            using (var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) }))
            {
                return results.First().AsEnumerable<float>().First();
            }
        }

        public void Dispose()
        {
            _session.Dispose();
        }

        private static InferenceSession CreateSession(string path)
        {
            var options = new SessionOptions();
            options.InterOpNumThreads = 1;
            options.IntraOpNumThreads = 1;
            return new InferenceSession(path, options);
        }

        private static string CreateModelName(string configuredName, string path)
        {
            if (!configuredName.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            {
                return configuredName.Replace(" ", "_");
            }

            return Path.GetFileNameWithoutExtension(path);
        }
    }

    internal sealed class AudioFeatureExtractor : IDisposable
    {
        private readonly InferenceSession _melspectrogramSession;
        private readonly InferenceSession _embeddingSession;
        private readonly string _melspectrogramInputName;
        private readonly string _embeddingInputName;
        private readonly RingBuffer<short> _rawDataBuffer = new RingBuffer<short>(16000 * 10);
        private readonly List<float[]> _melspectrogramBuffer = new List<float[]>();
        private readonly List<float[]> _featureBuffer = new List<float[]>();
        private readonly List<short> _rawRemainder = new List<short>();
        private int _accumulatedSamples;

        public AudioFeatureExtractor(string melspectrogramModelPath, string embeddingModelPath)
        {
            _melspectrogramSession = CreateSession(melspectrogramModelPath);
            _embeddingSession = CreateSession(embeddingModelPath);
            _melspectrogramInputName = _melspectrogramSession.InputMetadata.Keys.First();
            _embeddingInputName = _embeddingSession.InputMetadata.Keys.First();
            Reset();
        }

        public void Reset()
        {
            _rawDataBuffer.Clear();
            _melspectrogramBuffer.Clear();
            _featureBuffer.Clear();
            _rawRemainder.Clear();
            _accumulatedSamples = 0;

            for (int i = 0; i < 76; i++)
            {
                float[] row = new float[32];
                for (int j = 0; j < row.Length; j++)
                {
                    row[j] = 1f;
                }

                _melspectrogramBuffer.Add(row);
            }

            short[] initialAudio = new short[16000 * 4];
            var random = new Random(1);
            for (int i = 0; i < initialAudio.Length; i++)
            {
                initialAudio[i] = (short)random.Next(-1000, 1000);
            }

            foreach (float[] embedding in GetEmbeddings(initialAudio))
            {
                _featureBuffer.Add(embedding);
            }

            TrimFeatureBuffer();
        }

        public int Process(short[] input)
        {
            int processedSamples = 0;
            var audio = new List<short>();
            if (_rawRemainder.Count > 0)
            {
                audio.AddRange(_rawRemainder);
                _rawRemainder.Clear();
            }

            audio.AddRange(input);

            if (_accumulatedSamples + audio.Count >= 1280)
            {
                int remainder = (_accumulatedSamples + audio.Count) % 1280;
                int evenLength = audio.Count - remainder;
                if (evenLength > 0)
                {
                    short[] even = audio.GetRange(0, evenLength).ToArray();
                    _rawDataBuffer.AddRange(even);
                    _accumulatedSamples += even.Length;
                }

                if (remainder > 0)
                {
                    _rawRemainder.AddRange(audio.GetRange(evenLength, remainder));
                }
            }
            else
            {
                _rawDataBuffer.AddRange(audio);
                _accumulatedSamples += audio.Count;
            }

            if (_accumulatedSamples >= 1280 && _accumulatedSamples % 1280 == 0)
            {
                StreamingMelspectrogram(_accumulatedSamples);

                int chunks = _accumulatedSamples / 1280;
                for (int i = chunks - 1; i >= 0; i--)
                {
                    int ndx = -8 * i;
                    int end = ndx != 0 ? _melspectrogramBuffer.Count + ndx : _melspectrogramBuffer.Count;
                    int start = end - 76;
                    if (start >= 0 && end <= _melspectrogramBuffer.Count)
                    {
                        _featureBuffer.Add(GetEmbeddingFromMelspectrogram(start));
                    }
                }

                processedSamples = _accumulatedSamples;
                _accumulatedSamples = 0;
            }

            TrimFeatureBuffer();
            return processedSamples != 0 ? processedSamples : _accumulatedSamples;
        }

        public float[] GetFeatures(int nFeatureFrames)
        {
            float[] result = new float[nFeatureFrames * 96];
            int start = Math.Max(0, _featureBuffer.Count - nFeatureFrames);
            int outputRow = 0;
            for (int i = start; i < _featureBuffer.Count && outputRow < nFeatureFrames; i++, outputRow++)
            {
                Array.Copy(_featureBuffer[i], 0, result, outputRow * 96, 96);
            }

            return result;
        }

        public void Dispose()
        {
            _melspectrogramSession.Dispose();
            _embeddingSession.Dispose();
        }

        private void StreamingMelspectrogram(int nSamples)
        {
            short[] raw = _rawDataBuffer.GetLast(nSamples + 160 * 3);
            foreach (float[] row in GetMelspectrogram(raw))
            {
                _melspectrogramBuffer.Add(row);
            }

            while (_melspectrogramBuffer.Count > 10 * 97)
            {
                _melspectrogramBuffer.RemoveAt(0);
            }
        }

        private List<float[]> GetMelspectrogram(short[] audio)
        {
            float[] input = new float[audio.Length];
            for (int i = 0; i < audio.Length; i++)
            {
                input[i] = audio[i];
            }

            var tensor = new DenseTensor<float>(input, new[] { 1, audio.Length });
            using (var results = _melspectrogramSession.Run(new[] { NamedOnnxValue.CreateFromTensor(_melspectrogramInputName, tensor) }))
            {
                Tensor<float> output = results.First().AsTensor<float>();
                float[] values = output.ToArray();
                int rows = values.Length / 32;
                var spec = new List<float[]>();
                for (int row = 0; row < rows; row++)
                {
                    float[] item = new float[32];
                    for (int col = 0; col < 32; col++)
                    {
                        item[col] = values[row * 32 + col] / 10f + 2f;
                    }

                    spec.Add(item);
                }

                return spec;
            }
        }

        private List<float[]> GetEmbeddings(short[] audio)
        {
            List<float[]> spec = GetMelspectrogram(audio);
            var embeddings = new List<float[]>();
            for (int start = 0; start + 76 <= spec.Count; start += 8)
            {
                embeddings.Add(GetEmbeddingFromMelspectrogram(spec, start));
            }

            return embeddings;
        }

        private float[] GetEmbeddingFromMelspectrogram(int start)
        {
            return GetEmbeddingFromMelspectrogram(_melspectrogramBuffer, start);
        }

        private float[] GetEmbeddingFromMelspectrogram(List<float[]> spec, int start)
        {
            float[] input = new float[76 * 32];
            for (int row = 0; row < 76; row++)
            {
                Array.Copy(spec[start + row], 0, input, row * 32, 32);
            }

            var tensor = new DenseTensor<float>(input, new[] { 1, 76, 32, 1 });
            using (var results = _embeddingSession.Run(new[] { NamedOnnxValue.CreateFromTensor(_embeddingInputName, tensor) }))
            {
                return results.First().AsEnumerable<float>().ToArray();
            }
        }

        private void TrimFeatureBuffer()
        {
            while (_featureBuffer.Count > 120)
            {
                _featureBuffer.RemoveAt(0);
            }
        }

        private static InferenceSession CreateSession(string path)
        {
            var options = new SessionOptions();
            options.InterOpNumThreads = 1;
            options.IntraOpNumThreads = 1;
            return new InferenceSession(path, options);
        }
    }

    internal sealed class RingBuffer<T>
    {
        private readonly T[] _buffer;
        private int _start;
        private int _count;

        public RingBuffer(int capacity)
        {
            _buffer = new T[capacity];
        }

        public void Clear()
        {
            _start = 0;
            _count = 0;
        }

        public void AddRange(IEnumerable<T> values)
        {
            foreach (T value in values)
            {
                Add(value);
            }
        }

        public T[] GetLast(int count)
        {
            int actual = Math.Min(count, _count);
            var result = new T[actual];
            int start = (_start + _count - actual) % _buffer.Length;
            for (int i = 0; i < actual; i++)
            {
                result[i] = _buffer[(start + i) % _buffer.Length];
            }

            return result;
        }

        private void Add(T value)
        {
            if (_count < _buffer.Length)
            {
                _buffer[(_start + _count) % _buffer.Length] = value;
                _count++;
                return;
            }

            _buffer[_start] = value;
            _start = (_start + 1) % _buffer.Length;
        }
    }

    internal sealed class AudioFrameEventArgs : EventArgs
    {
        public AudioFrameEventArgs(short[] samples)
        {
            Samples = samples;
        }

        public short[] Samples { get; private set; }
    }

    internal sealed class WaveInAudioSource : IDisposable
    {
        private const int WaveMapper = -1;
        private const int BufferCount = 4;
        private readonly int _deviceId;
        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly int _samplesPerBuffer;
        private readonly List<WaveBuffer> _buffers = new List<WaveBuffer>();
        private readonly object _sync = new object();
        private WaveNative.WaveInProc _callback;
        private IntPtr _handle;
        private bool _running;
        private bool _disposed;

        public event EventHandler<AudioFrameEventArgs> AudioAvailable;

        public WaveInAudioSource(int deviceId, int sampleRate, int channels, int samplesPerBuffer)
        {
            _deviceId = deviceId;
            _sampleRate = sampleRate;
            _channels = channels;
            _samplesPerBuffer = samplesPerBuffer;
            DeviceDescription = DescribeDevice(deviceId);
        }

        public bool IsRunning
        {
            get { return _running; }
        }

        public string DeviceDescription { get; private set; }

        public void Start()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("WaveInAudioSource");
            }

            _callback = OnWaveIn;
            var format = new WaveNative.WaveFormatEx();
            format.wFormatTag = 1;
            format.nChannels = (short)_channels;
            format.nSamplesPerSec = _sampleRate;
            format.wBitsPerSample = 16;
            format.nBlockAlign = (short)(_channels * format.wBitsPerSample / 8);
            format.nAvgBytesPerSec = format.nSamplesPerSec * format.nBlockAlign;
            format.cbSize = 0;

            int result = WaveNative.waveInOpen(out _handle, _deviceId, ref format, _callback, IntPtr.Zero, WaveNative.CallbackFunction);
            Check(result, "waveInOpen");

            int bufferBytes = _samplesPerBuffer * format.nBlockAlign;
            for (int i = 0; i < BufferCount; i++)
            {
                var buffer = new WaveBuffer(bufferBytes);
                buffer.Prepare(_handle);
                buffer.Add(_handle);
                _buffers.Add(buffer);
            }

            Check(WaveNative.waveInStart(_handle), "waveInStart");
            _running = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (_sync)
            {
                _running = false;
            }

            if (_handle != IntPtr.Zero)
            {
                WaveNative.waveInReset(_handle);
                foreach (WaveBuffer buffer in _buffers)
                {
                    buffer.Unprepare(_handle);
                    buffer.Dispose();
                }

                _buffers.Clear();
                WaveNative.waveInClose(_handle);
                _handle = IntPtr.Zero;
            }
        }

        public static int ParseDeviceId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return WaveMapper;
            }

            int parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return WaveMapper;
        }

        private void OnWaveIn(IntPtr hwi, int message, IntPtr instance, IntPtr waveHeader, IntPtr reserved)
        {
            if (message != WaveNative.WimData || waveHeader == IntPtr.Zero)
            {
                return;
            }

            bool shouldRequeue;
            lock (_sync)
            {
                shouldRequeue = _running && !_disposed;
            }

            if (!shouldRequeue)
            {
                return;
            }

            WaveNative.WaveHdr header = (WaveNative.WaveHdr)Marshal.PtrToStructure(waveHeader, typeof(WaveNative.WaveHdr));
            if (header.dwBytesRecorded > 0)
            {
                byte[] bytes = new byte[header.dwBytesRecorded];
                Marshal.Copy(header.lpData, bytes, 0, bytes.Length);
                short[] samples = new short[bytes.Length / 2];
                Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);

                EventHandler<AudioFrameEventArgs> handler = AudioAvailable;
                if (handler != null)
                {
                    handler(this, new AudioFrameEventArgs(samples));
                }
            }

            if (shouldRequeue)
            {
                WaveNative.waveInAddBuffer(_handle, waveHeader, Marshal.SizeOf(typeof(WaveNative.WaveHdr)));
            }
        }

        private static string DescribeDevice(int deviceId)
        {
            if (deviceId == WaveMapper)
            {
                return "default";
            }

            var caps = new WaveNative.WaveInCaps();
            int result = WaveNative.waveInGetDevCaps((IntPtr)deviceId, ref caps, Marshal.SizeOf(typeof(WaveNative.WaveInCaps)));
            return result == 0 ? deviceId + ":" + caps.szPname : deviceId.ToString(CultureInfo.InvariantCulture);
        }

        private static void Check(int result, string operation)
        {
            if (result != 0)
            {
                throw new InvalidOperationException(operation + " failed with code " + result.ToString(CultureInfo.InvariantCulture));
            }
        }

        private sealed class WaveBuffer : IDisposable
        {
            private readonly int _headerSize = Marshal.SizeOf(typeof(WaveNative.WaveHdr));
            private IntPtr _header;
            private IntPtr _data;
            private bool _prepared;

            public WaveBuffer(int byteLength)
            {
                _data = Marshal.AllocHGlobal(byteLength);
                _header = Marshal.AllocHGlobal(_headerSize);
                var header = new WaveNative.WaveHdr();
                header.lpData = _data;
                header.dwBufferLength = byteLength;
                header.dwBytesRecorded = 0;
                header.dwUser = IntPtr.Zero;
                header.dwFlags = 0;
                header.dwLoops = 0;
                header.lpNext = IntPtr.Zero;
                header.reserved = IntPtr.Zero;
                Marshal.StructureToPtr(header, _header, false);
            }

            public void Prepare(IntPtr handle)
            {
                Check(WaveNative.waveInPrepareHeader(handle, _header, _headerSize), "waveInPrepareHeader");
                _prepared = true;
            }

            public void Add(IntPtr handle)
            {
                Check(WaveNative.waveInAddBuffer(handle, _header, _headerSize), "waveInAddBuffer");
            }

            public void Unprepare(IntPtr handle)
            {
                if (_prepared)
                {
                    WaveNative.waveInUnprepareHeader(handle, _header, _headerSize);
                    _prepared = false;
                }
            }

            public void Dispose()
            {
                if (_header != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_header);
                    _header = IntPtr.Zero;
                }

                if (_data != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_data);
                    _data = IntPtr.Zero;
                }
            }
        }
    }
}
