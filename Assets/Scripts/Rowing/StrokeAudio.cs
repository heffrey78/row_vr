using System;
using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The blade going in and coming out, as sound (REQ-0002-FUNC). Hands-only means no
    /// controller haptics, so this is what gives each stroke a physical beat.
    ///
    /// Plays on <see cref="RowingRig.Catch"/> and <see cref="RowingRig.Finish"/> on the frame
    /// the detector reports them. Android's audio output latency comes on top of that and is
    /// measured on the device (TASK-0020); the build asks for the smallest DSP buffer
    /// (ProjectBootstrap) to keep it down.
    ///
    /// A catch can be withdrawn up to ~1.5 s later when it turns out the hands let go of the
    /// handle (ADR-0007). Its catch sound has already played by then, and delaying every
    /// catch to wait would ruin the timing, so that is accepted. What is suppressed is the
    /// finish: one only sounds for a drive whose catch was heard and not taken back.
    ///
    /// The sounds are placeholders synthesised in code: no licensing question and no binary
    /// asset in LFS until real recordings replace them. They play from either side of the
    /// rower, roughly where the blades are, so they are spatialised.
    /// </summary>
    [RequireComponent(typeof(RowingRig))]
    public sealed class StrokeAudio : MonoBehaviour
    {
        [Range(0f, 1f)] [SerializeField] float catchVolume = 0.9f;
        [Range(0f, 1f)] [SerializeField] float finishVolume = 0.45f;

        [Tooltip("Where the sounds come from, relative to the head: metres to each side, and below.")]
        [SerializeField] float sideOffset = 1.2f;
        [SerializeField] float belowHead = 0.6f;

        [Tooltip("Log each sound, for checking sound counts against the recordings in Play Mode.")]
        [SerializeField] bool logSounds;

        const int SampleRate = 48000;

        RowingRig _rig;
        AudioSource _left, _right;
        AudioClip _catchClip, _finishClip;
        bool _catchOpen;

        /// <summary>Sounds played since startup, for tests and diagnostics.</summary>
        public int CatchSounds { get; private set; }
        public int FinishSounds { get; private set; }

        void Awake()
        {
            _rig = GetComponent<RowingRig>();
            _catchClip = SynthesiseCatch();
            _finishClip = SynthesiseFinish();
            _left = CreateSource("Stroke Audio Left");
            _right = CreateSource("Stroke Audio Right");
        }

        void OnEnable()
        {
            _rig.Catch += OnCatch;
            _rig.Finish += OnFinish;
            _rig.CatchWithdrawn += OnCatchWithdrawn;
        }

        void OnDisable()
        {
            _rig.Catch -= OnCatch;
            _rig.Finish -= OnFinish;
            _rig.CatchWithdrawn -= OnCatchWithdrawn;
        }

        void OnDestroy()
        {
            if (_left != null) Destroy(_left.gameObject);
            if (_right != null) Destroy(_right.gameObject);
        }

        void LateUpdate()
        {
            if (!_rig.IsTracking) return;
            var sample = _rig.LastSample;
            var head = sample.HeadPosition.ToVector3();
            var forward = sample.HeadForward.ToVector3();
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return;
            var right = Vector3.Cross(Vector3.up, forward.normalized);
            var low = head + Vector3.down * belowHead;
            _left.transform.position = low - right * sideOffset;
            _right.transform.position = low + right * sideOffset;
        }

        void OnCatch(StrokeMetrics metrics)
        {
            _catchOpen = true;
            Play(_catchClip, catchVolume);
            CatchSounds++;
            if (logSounds) Debug.Log($"[RowVR] sound: catch #{CatchSounds}");
        }

        void OnFinish(StrokeMetrics metrics)
        {
            if (!_catchOpen) return;
            _catchOpen = false;
            Play(_finishClip, finishVolume);
            FinishSounds++;
            if (logSounds) Debug.Log($"[RowVR] sound: finish #{FinishSounds}");
        }

        void OnCatchWithdrawn(StrokeMetrics metrics)
        {
            _catchOpen = false;
            if (logSounds) Debug.Log("[RowVR] sound: catch withdrawn, no finish will sound");
        }

        void Play(AudioClip clip, float volume)
        {
            _left.PlayOneShot(clip, volume);
            _right.PlayOneShot(clip, volume);
        }

        static AudioSource CreateSource(string name)
        {
            var source = new GameObject(name).AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 1f;
            source.maxDistance = 20f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            return source;
        }

        /// <summary>
        /// A splash: noise through a low-pass filter whose cutoff falls from bright to dull,
        /// over a short low thump, with a sharp attack and a fast decay.
        /// </summary>
        static AudioClip SynthesiseCatch()
        {
            const float seconds = 0.18f;
            var data = new float[(int)(SampleRate * seconds)];
            var random = new System.Random(1);
            var lowPassed = 0f;
            for (var i = 0; i < data.Length; i++)
            {
                var t = (float)i / SampleRate;
                var cutoff = Mathf.Lerp(2500f, 500f, t / seconds);
                lowPassed += OnePoleCoefficient(cutoff) * (Noise(random) - lowPassed);
                var splash = lowPassed * 2.5f * Envelope(t, 0.003f, 0.05f);
                var thump = Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.5f * Envelope(t, 0.002f, 0.025f);
                data[i] = Mathf.Clamp(splash + thump, -1f, 1f);
            }
            return Clip("Catch (synthesised)", data);
        }

        /// <summary>A swish: the high part of the noise, softer and shorter.</summary>
        static AudioClip SynthesiseFinish()
        {
            const float seconds = 0.12f;
            var data = new float[(int)(SampleRate * seconds)];
            var random = new System.Random(2);
            var lowPassed = 0f;
            var a = OnePoleCoefficient(1500f);
            for (var i = 0; i < data.Length; i++)
            {
                var t = (float)i / SampleRate;
                var noise = Noise(random);
                lowPassed += a * (noise - lowPassed);
                data[i] = Mathf.Clamp((noise - lowPassed) * 0.6f * Envelope(t, 0.01f, 0.035f), -1f, 1f);
            }
            return Clip("Finish (synthesised)", data);
        }

        static float Noise(System.Random random) => (float)(random.NextDouble() * 2d - 1d);

        /// <summary>Linear attack, then exponential decay with time constant <paramref name="decay"/>.</summary>
        static float Envelope(float t, float attack, float decay) =>
            t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);

        static float OnePoleCoefficient(float cutoffHz) =>
            1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz / SampleRate);

        static AudioClip Clip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
