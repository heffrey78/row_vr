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
    /// The sounds are synthesised by SoundSynth (engine-free, tested): a "blup", splash and
    /// droplets at the catch, a swirl and drips at the finish, several takes of each so
    /// strokes do not repeat identically. Nothing to license, no binary asset. They play
    /// from either side of the rower, roughly where the blades are, so they are spatialised.
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

        RowingRig _rig;
        AudioSource _left, _right;
        AudioClip[] _catchClips, _finishClips;
        readonly System.Random _variety = new System.Random(11);
        bool _catchOpen;

        /// <summary>Sounds played since startup, for tests and diagnostics.</summary>
        public int CatchSounds { get; private set; }
        public int FinishSounds { get; private set; }

        void Awake()
        {
            _rig = GetComponent<RowingRig>();
            // Several takes of each, so no two strokes in a row sound identical.
            _catchClips = new AudioClip[4];
            _finishClips = new AudioClip[4];
            for (var i = 0; i < 4; i++)
            {
                _catchClips[i] = Clip("Catch " + i, SoundSynth.Catch(100 + i));
                _finishClips[i] = Clip("Finish " + i, SoundSynth.Finish(200 + i));
            }
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
            Play(_catchClips[_variety.Next(_catchClips.Length)], catchVolume);
            CatchSounds++;
            if (logSounds) Debug.Log($"[RowVR] sound: catch #{CatchSounds}");
        }

        void OnFinish(StrokeMetrics metrics)
        {
            if (!_catchOpen) return;
            _catchOpen = false;
            Play(_finishClips[_variety.Next(_finishClips.Length)], finishVolume);
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

        static AudioClip Clip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SoundSynth.SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
