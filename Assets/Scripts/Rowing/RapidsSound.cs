using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The river's voice (#30): a roar that swells with the current, loudest in the
    /// rapids and a murmur in the pools, and spray splashing at the bow in rough water. The
    /// sound is what makes a rapid feel wild while the view stays steady (#90).
    /// Synthesised by SoundSynth; mixed under the stroke sounds, which keep priority.
    /// </summary>
    [RequireComponent(typeof(RapidsMotion), typeof(BoatFrame))]
    public sealed class RapidsSound : MonoBehaviour
    {
        [Range(0f, 1f)] [SerializeField] float roarInRapid = 0.75f;
        [Range(0f, 1f)] [SerializeField] float roarInPool = 0.12f;
        [Range(0f, 1f)] [SerializeField] float spray = 0.5f;

        RapidsMotion _rapids;
        BoatFrame _frame;
        AudioSource _roar, _spray;
        AudioClip[] _splashes;
        readonly System.Random _random = new System.Random(41);
        float _nextSplash;

        void Awake()
        {
            _rapids = GetComponent<RapidsMotion>();
            _frame = GetComponent<BoatFrame>();

            _roar = Source("River Roar", 0.4f);
            _roar.clip = Clip("Roar", SoundSynth.Rapids(6f, 7));
            _roar.loop = true;
            _roar.volume = roarInPool;
            _roar.timeSamples = _random.Next(_roar.clip.samples);
            _roar.Play();

            _spray = Source("Spray", 1f);
            _splashes = new AudioClip[3];
            for (var i = 0; i < _splashes.Length; i++) _splashes[i] = Clip("Splash " + i, SoundSynth.Finish(400 + i));
        }

        void OnDestroy()
        {
            if (_roar != null) Destroy(_roar.gameObject);
            if (_spray != null) Destroy(_spray.gameObject);
        }

        void LateUpdate()
        {
            var run = _rapids.Run;
            if (_frame.HasFrame)
            {
                _roar.transform.position = _frame.Point(4f, 0f, 0.5f);
                _spray.transform.position = _frame.Point(1.6f, 0f, 0.4f);
            }

            // The roar follows the (eased) current, so it swells into a rapid and fades after.
            var level = Mathf.InverseLerp(0.2f, 1.5f, run.Current);
            _roar.volume = Mathf.Lerp(roarInPool, roarInRapid, level);
            _roar.pitch = 0.9f + 0.15f * level;

            _nextSplash -= Time.deltaTime;
            if (run.InRapid && !run.OnLine && _nextSplash <= 0f)
            {
                _spray.pitch = 0.9f + (float)_random.NextDouble() * 0.3f;
                _spray.PlayOneShot(_splashes[_random.Next(_splashes.Length)], spray);
                _nextSplash = 0.5f + (float)_random.NextDouble() * 0.9f;
            }
        }

        static AudioSource Source(string name, float spatial)
        {
            var source = new GameObject(name).AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = spatial;
            source.minDistance = 2f;
            source.maxDistance = 40f;
            source.dopplerLevel = 0f;
            source.priority = 190;
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
