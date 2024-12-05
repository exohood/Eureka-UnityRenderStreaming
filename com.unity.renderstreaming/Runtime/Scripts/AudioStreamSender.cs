using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.WebRTC;
using UnityEngine;

namespace Unity.RenderStreaming
{
    /// <summary>
    /// Specifies the source of the audio stream.
    /// </summary>
    public enum AudioStreamSource
    {
        /// <summary>
        /// Use the AudioListener component as the audio source.
        /// </summary>
        AudioListener = 0,
        /// <summary>
        /// Use the AudioSource component as the audio source.
        /// </summary>
        AudioSource = 1,
        /// <summary>
        /// Use the microphone as the audio source.
        /// </summary>
        Microphone = 2,
        /// <summary>
        /// Use only the API to provide audio data.
        /// </summary>
        APIOnly = 3
    }

    /// <summary>
    /// Component for sending audio streams.
    /// </summary>
    [AddComponentMenu("Render Streaming/Audio Stream Sender")]
    public class AudioStreamSender : StreamSenderBase
    {
        static readonly uint s_defaultMinBitrate = 0;
        static readonly uint s_defaultMaxBitrate = 200;

        internal const string SourcePropertyName = nameof(m_Source);
        internal const string AudioSourcePropertyName = nameof(m_AudioSource);
        internal const string AudioListenerPropertyName = nameof(m_AudioListener);
        internal const string MicrophoneDeviceIndexPropertyName = nameof(m_MicrophoneDeviceIndex);
        internal const string AutoRequestUserAuthorizationPropertyName = nameof(m_AutoRequestUserAuthorization);
        internal const string CodecPropertyName = nameof(m_Codec);
        internal const string BitratePropertyName = nameof(m_Bitrate);
        internal const string LoopbackPropertyName = nameof(m_Loopback);

        [SerializeField]
        private AudioStreamSource m_Source;

        [SerializeField]
        private AudioListener m_AudioListener;

        [SerializeField]
        private AudioSource m_AudioSource;

        [SerializeField]
        private int m_MicrophoneDeviceIndex;

        [SerializeField]
        private bool m_AutoRequestUserAuthorization = true;

        [SerializeField, Codec]
        private AudioCodecInfo m_Codec;

        [SerializeField, Bitrate(0, 1000)]
        private Range m_Bitrate = new Range(s_defaultMinBitrate, s_defaultMaxBitrate);

        [SerializeField]
        private bool m_Loopback = false;

        private int m_sampleRate = 0;

        private AudioStreamSourceImpl m_sourceImpl = null;

        private int m_frequency = 48000;

        /// <summary>
        /// Gets or sets the source of the audio stream.
        /// </summary>
        public AudioStreamSource source
        {
            get { return m_Source; }
            set
            {
                if (m_Source == value)
                    return;
                m_Source = value;

                if (!isPlaying)
                    return;

                var op = CreateTrack();
                StartCoroutineWithCallback(op, _ => ReplaceTrack(_.Track));
            }
        }

        /// <summary>
        /// Gets the codec used for the audio stream.
        /// </summary>
        public AudioCodecInfo codec
        {
            get { return m_Codec; }
        }

        /// <summary>
        /// Gets the minimum bitrate for the audio stream.
        /// </summary>
        public uint minBitrate
        {
            get { return m_Bitrate.min; }
        }

        /// <summary>
        /// Gets the maximum bitrate for the audio stream.
        /// </summary>
        public uint maxBitrate
        {
            get { return m_Bitrate.max; }
        }

        /// <summary>
        /// Gets or sets whether to play the audio locally while sending it to the remote peer.
        /// </summary>
        public bool loopback
        {
            get
            {
                return m_Loopback;
            }
            set
            {
                if (m_Loopback == value)
                {
                    return;
                }

                m_Loopback = value;

                if (Track is AudioStreamTrack audioTrack)
                {
                    audioTrack.Loopback = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the index of the microphone device used as the audio source.
        /// </summary>
        public int sourceDeviceIndex
        {
            get { return m_MicrophoneDeviceIndex; }
            set
            {
                if (m_MicrophoneDeviceIndex == value)
                    return;
                m_MicrophoneDeviceIndex = value;

                if (!isPlaying || m_Source != AudioStreamSource.Microphone)
                    return;

                var op = CreateTrack();
                StartCoroutineWithCallback(op, _ => ReplaceTrack(_.Track));
            }
        }

        /// <summary>
        /// Gets or sets the AudioSource component used as the audio source.
        /// </summary>
        public AudioSource audioSource
        {
            get { return m_AudioSource; }
            set
            {
                if (m_AudioSource == value)
                    return;
                m_AudioSource = value;

                if (!isPlaying || m_Source != AudioStreamSource.AudioSource)
                    return;

                var op = CreateTrack();
                StartCoroutineWithCallback(op, _ => ReplaceTrack(_.Track));
            }
        }

        /// <summary>
        /// Gets or sets the AudioListener component used as the audio source.
        /// </summary>
        public AudioListener audioListener
        {
            get { return m_AudioListener; }
            set
            {
                if (m_AudioListener == value)
                    return;
                m_AudioListener = value;

                if (!isPlaying || m_Source != AudioStreamSource.AudioListener)
                    return;

                var op = CreateTrack();
                StartCoroutineWithCallback(op, _ => ReplaceTrack(_.Track));
            }
        }

        /// <summary>
        /// Gets the available video codecs.
        /// </summary>
        /// <code>
        /// var codecs = VideoStreamSender.GetAvailableCodecs();
        /// foreach (var codec in codecs)
        ///     Debug.Log(codec.name);
        /// </code>
        /// </example>
        /// <returns>A list of available codecs.</returns>
        static public IEnumerable<AudioCodecInfo> GetAvailableCodecs()
        {
            var excludeCodecMimeType = new[] { "audio/CN", "audio/telephone-event" };
            var capabilities = RTCRtpSender.GetCapabilities(TrackKind.Audio);
            return capabilities.codecs.Where(codec => !excludeCodecMimeType.Contains(codec.mimeType)).Select(codec => AudioCodecInfo.Create(codec));
        }

        /// <summary>
        /// Sets the bitrate range for the audio stream.
        /// </summary>
        /// <example>
        /// <code>
        /// audioStreamSender.SetBitrate(128, 256);
        /// </code>
        /// </example>
        /// <param name="minBitrate">The minimum bitrate in kbps. Must be greater than zero.</param>
        /// <param name="maxBitrate">The maximum bitrate in kbps. Must be greater than or equal to the minimum bitrate.</param>
        /// <exception cref="ArgumentException">Thrown when the maximum bitrate is less than the minimum bitrate.</exception>
        public void SetBitrate(uint minBitrate, uint maxBitrate)
        {
            if (minBitrate > maxBitrate)
                throw new ArgumentException("The maxBitrate must be greater than minBitrate.", "maxBitrate");
            m_Bitrate.min = minBitrate;
            m_Bitrate.max = maxBitrate;
            foreach (var transceiver in Transceivers.Values)
            {
                RTCError error = transceiver.Sender.SetBitrate(m_Bitrate.min, m_Bitrate.max);
                if (error.errorType != RTCErrorType.None)
                    RenderStreaming.Logger.Log(LogType.Error, error.message);
            }
        }

        /// <summary>
        /// Sets the codec for the audio stream.
        /// </summary>
        /// <example>
        /// <code>
        /// <![CDATA[
        /// var codec = AudioStreamSender.GetAvailableCodecs().First(x => x.mimeType.Contains("opus"));
        /// audioStreamSender.SetCodec(codec);
        /// ]]>
        ///</code>
        /// </example>
        /// <param name="codec">The codec information to set.</param>
        public void SetCodec(AudioCodecInfo codec)
        {
            m_Codec = codec;
            foreach (var transceiver in Transceivers.Values)
            {
                if (!string.IsNullOrEmpty(transceiver.Mid))
                    continue;
                if (transceiver.Sender.Track.ReadyState == TrackState.Ended)
                    continue;

                var codecs = new AudioCodecInfo[] { m_Codec };
                RTCErrorType error = transceiver.SetCodecPreferences(SelectCodecCapabilities(codecs).ToArray());
                if (error != RTCErrorType.None)
                    throw new InvalidOperationException($"Set codec is failed. errorCode={error}");
            }
        }

        internal IEnumerable<RTCRtpCodecCapability> SelectCodecCapabilities(IEnumerable<AudioCodecInfo> codecs)
        {
            return RTCRtpSender.GetCapabilities(TrackKind.Audio).SelectCodecCapabilities(codecs);
        }

        private protected virtual void Awake()
        {
            OnStartedStream += _OnStartedStream;
            OnStoppedStream += _OnStoppedStream;
        }

        private protected override void OnDestroy()
        {
            base.OnDestroy();

            m_sourceImpl?.Dispose();
            m_sourceImpl = null;
        }

        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            m_sampleRate = AudioSettings.outputSampleRate;
        }

        void _OnStartedStream(string connectionId)
        {
        }

        void _OnStoppedStream(string connectionId)
        {
            m_sourceImpl?.Dispose();
            m_sourceImpl = null;
        }

        internal override WaitForCreateTrack CreateTrack()
        {
            m_sourceImpl?.Dispose();
            m_sourceImpl = CreateAudioStreamSource();
            return m_sourceImpl.CreateTrack();
        }

        AudioStreamSourceImpl CreateAudioStreamSource()
        {
            switch (m_Source)
            {
                case AudioStreamSource.AudioListener:
                    return new AudioStreamSourceAudioListener(this);
                case AudioStreamSource.AudioSource:
                    return new AudioStreamSourceAudioSource(this);
                case AudioStreamSource.Microphone:
                    return new AudioStreamSourceMicrophone(this);
                case AudioStreamSource.APIOnly:
                    return new AudioStreamSourceAPIOnly(this);
            }
            throw new InvalidOperationException("");
        }

        private protected override void OnEnable()
        {
            OnAudioConfigurationChanged(false);
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            base.OnEnable();
        }

        private protected override void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            base.OnDisable();
        }

        /// <summary>
        /// Sets the audio data for the stream.
        /// </summary>
        /// <example>
        /// <code>
        /// <![CDATA[
        /// int sampleRate = AudioSettings.outputSampleRate;
        /// int frequency = 440;
        /// int bufferSize = sampleRate; // 1 second buffer
        /// var audioData = new NativeArray<float>(bufferSize, Allocator.Temp);
        /// for (int i = 0; i < bufferSize; i++)
        /// {
        ///     audioData[i] = Mathf.Sin(2 * Mathf.PI * frequency * i / sampleRate);
        /// }
        /// audioStreamSender.SetData(audioData.AsReadOnly(), 1);
        /// audioData.Dispose();
        /// ]]>
        /// </code>
        /// </example>
        /// <param name="nativeArray">The native array containing the audio data.</param>
        /// <param name="channels">The number of audio channels.</param>
        /// <exception cref="InvalidOperationException">Thrown when the source property is not set to AudioStreamSource.APIOnly.</exception>
        public void SetData(NativeArray<float>.ReadOnly nativeArray, int channels)
        {
            if (m_Source != AudioStreamSource.APIOnly)
                throw new InvalidOperationException("To use this method, please set AudioStreamSource.APIOnly to source property");
            if (!isPlaying)
                return;
            (m_sourceImpl as AudioStreamSourceAPIOnly)?.SetData(nativeArray, channels, m_sampleRate);
        }

        abstract class AudioStreamSourceImpl : IDisposable
        {
            protected AudioStreamSourceImpl(AudioStreamSender parent)
            {
            }

            public abstract WaitForCreateTrack CreateTrack();
            public abstract void Dispose();
        }

        class AudioStreamSourceAudioListener : AudioStreamSourceImpl
        {
            private AudioListener m_audioListener;

            public AudioStreamSourceAudioListener(AudioStreamSender parent) : base(parent)
            {
                m_audioListener = parent.m_AudioListener;
                if (m_audioListener == null)
                    throw new InvalidOperationException("The audioListener is not assigned.");
            }

            public override WaitForCreateTrack CreateTrack()
            {
                var instruction = new WaitForCreateTrack();
                instruction.Done(new AudioStreamTrack(m_audioListener));
                return instruction;
            }

            public override void Dispose()
            {
                GC.SuppressFinalize(this);
            }

            ~AudioStreamSourceAudioListener()
            {
                Dispose();
            }
        }

        class AudioStreamSourceAudioSource : AudioStreamSourceImpl
        {
            private AudioSource m_audioSource;
            public AudioStreamSourceAudioSource(AudioStreamSender parent) : base(parent)
            {
                m_audioSource = parent.m_AudioSource;
                if (m_audioSource == null)
                    throw new InvalidOperationException("The audioSource is not assigned.");

            }

            public override WaitForCreateTrack CreateTrack()
            {
                var instruction = new WaitForCreateTrack();
                instruction.Done(new AudioStreamTrack(m_audioSource));
                return instruction;
            }

            public override void Dispose()
            {
                GC.SuppressFinalize(this);
            }

            ~AudioStreamSourceAudioSource()
            {
                Dispose();
            }
        }
        class AudioStreamSourceMicrophone : AudioStreamSourceImpl
        {
            int m_deviceIndex;
            bool m_autoRequestUserAuthorization;
            int m_frequency;
            string m_deviceName;
            AudioSource m_audioSource;
            GameObject m_audioSourceObj;
            AudioStreamSender m_parent;

            public AudioStreamSourceMicrophone(AudioStreamSender parent) : base(parent)
            {
                int deviceIndex = parent.m_MicrophoneDeviceIndex;
                if (deviceIndex < 0 || Microphone.devices.Length <= deviceIndex)
                    throw new ArgumentOutOfRangeException("deviceIndex", deviceIndex, "The deviceIndex is out of range");
                m_parent = parent;
                m_deviceIndex = deviceIndex;
                m_frequency = parent.m_frequency;
                m_autoRequestUserAuthorization = parent.m_AutoRequestUserAuthorization;
            }

            public override WaitForCreateTrack CreateTrack()
            {
                var instruction = new WaitForCreateTrack();
                m_parent.StartCoroutine(CreateTrackCoroutine(instruction));
                return instruction;
            }

            IEnumerator CreateTrackCoroutine(WaitForCreateTrack instruction)
            {
                if (m_autoRequestUserAuthorization)
                {
                    AsyncOperation op = Application.RequestUserAuthorization(UserAuthorization.Microphone);
                    yield return op;
                }
                if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
                    throw new InvalidOperationException("Call Application.RequestUserAuthorization before creating track with Microphone.");

                m_deviceName = Microphone.devices[m_deviceIndex];
                Microphone.GetDeviceCaps(m_deviceName, out int minFreq, out int maxFreq);
                var micClip = Microphone.Start(m_deviceName, true, 1, m_frequency);

                // set the latency to “0” samples before the audio starts to play.
                yield return new WaitUntil(() => Microphone.GetPosition(m_deviceName) > 0);

                m_audioSourceObj = new GameObject("Audio");
                m_audioSourceObj.hideFlags = HideFlags.HideInHierarchy;
                DontDestroyOnLoad(m_audioSourceObj);
                m_audioSource = m_audioSourceObj.AddComponent<AudioSource>();
                m_audioSource.clip = micClip;
                m_audioSource.loop = true;
                m_audioSource.Play();

                instruction.Done(new AudioStreamTrack(m_audioSource));
            }

            public override void Dispose()
            {
                if (m_audioSourceObj != null)
                {
                    m_audioSource.Stop();
                    var clip = m_audioSource.clip;
                    if (clip != null)
                    {
                        Destroy(clip);
                    }
                    m_audioSource.clip = null;

                    Destroy(m_audioSourceObj);
                    m_audioSourceObj = null;
                    m_audioSource = null;
                }
                if (Microphone.IsRecording(m_deviceName))
                    Microphone.End(m_deviceName);
                GC.SuppressFinalize(this);
            }

            ~AudioStreamSourceMicrophone()
            {
                Dispose();
            }
        }

        class AudioStreamSourceAPIOnly : AudioStreamSourceImpl
        {
            AudioStreamTrack m_audioTrack;

            public AudioStreamSourceAPIOnly(AudioStreamSender parent) : base(parent)
            {

            }

            public override WaitForCreateTrack CreateTrack()
            {
                var instruction = new WaitForCreateTrack();
                m_audioTrack = new AudioStreamTrack();
                instruction.Done(m_audioTrack);
                return instruction;
            }

            public void SetData(NativeArray<float>.ReadOnly nativeArray, int channels, int sampleRate)
            {
                m_audioTrack?.SetData(nativeArray, channels, sampleRate);
            }

            public override void Dispose()
            {
                GC.SuppressFinalize(this);
            }

            ~AudioStreamSourceAPIOnly()
            {
                Dispose();
            }
        }
    }
}
