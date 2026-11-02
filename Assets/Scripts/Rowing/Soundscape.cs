using System.Collections.Generic;
using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The lake's ambience (#83): water lapping round the boat, a soft breeze, water
    /// running under the hull that rises with speed, and birds calling from the banks now and
    /// then. All of it synthesised by SoundSynth (engine-free, tested), so nothing is
    /// licensed or stored as audio.
    ///
    /// Mixed under the stroke sounds: the catch is the rower's beat and must never be masked.
    /// Lapping and the hull sit around the boat (BoatFrame); birds call from 60-160 m away,
    /// out on the banks, spatialised so they can be placed by ear.
    /// </summary>
    [RequireComponent(typeof(BoatFrame), typeof(BoatMotion))]
    public sealed class Soundscape : MonoBehaviour
    {
        [Range(0f, 1f)] [SerializeField] float lapping = 0.28f;
        [Range(0f, 1f)] [SerializeField] float wind = 0.12f;
        [Range(0f, 1f)] [SerializeField] float hullAtSpeed = 0.35f;
        [Range(0f, 1f)] [SerializeField] float birds = 0.45f;

        [Tooltip("Seconds between birdcalls, at random within this range.")]
        [SerializeField] Vector2 birdInterval = new Vector2(5f, 14f);

        BoatFrame _frame;
        BoatMotion _motion;
        AudioSource _lapLeft, _lapRight, _wind, _hull;
        readonly List<AudioSource> _birdSources = new List<AudioSource>();
        AudioClip[] _birdCalls;
        readonly System.Random _random = new System.Random(31);
        float _nextBird;
        int _nextBirdSource;

        void Awake()
        {
            _frame = GetComponent<BoatFrame>();
            _motion = GetComponent<BoatMotion>();

            _lapLeft = Loop("Lapping Left", SoundSynth.Lapping(7f, 1), lapping, 1f);
            _lapRight = Loop("Lapping Right", SoundSynth.Lapping(7.6f, 2), lapping, 1f);
            _wind = Loop("Wind", SoundSynth.Wind(13f, 3), wind, 0f);
            _hull = Loop("Hull", SoundSynth.HullRush(4f, 4), 0f, 0.6f);

            _birdCalls = new AudioClip[SoundSynth.BirdSpecies * 2];
            for (var i = 0; i < _birdCalls.Length; i++)
                _birdCalls[i] = Clip("Bird " + i, SoundSynth.Bird(i % SoundSynth.BirdSpecies, 300 + i));
            for (var i = 0; i < 3; i++)
            {
                var source = Source("Bird Source " + i, 1f);
                source.minDistance = 25f;
                source.maxDistance = 250f;
                _birdSources.Add(source);
            }
            _nextBird = 3f;
        }

        void OnDestroy()
        {
            foreach (var s in new[] { _lapLeft, _lapRight, _wind, _hull })
                if (s != null) Destroy(s.gameObject);
            foreach (var s in _birdSources)
                if (s != null) Destroy(s.gameObject);
        }

        void LateUpdate()
        {
            if (_frame.HasFrame)
            {
                _lapLeft.transform.position = _frame.Point(-0.5f, -1.8f, 0f);
                _lapRight.transform.position = _frame.Point(0.5f, 1.8f, 0f);
                _hull.transform.position = _frame.Point(-0.8f, 0f, 0f);
            }

            // Water under the hull: silent at rest, full at a brisk 4.5 m/s.
            var speed = _motion.Boat != null ? _motion.Boat.VisibleSpeed : 0f;
            var run = Mathf.Clamp01(speed / 4.5f);
            _hull.volume = hullAtSpeed * run * run;
            _hull.pitch = 0.85f + 0.3f * run;

            _nextBird -= Time.deltaTime;
            if (_nextBird <= 0f && _frame.HasFrame)
            {
                CallBird();
                _nextBird = Mathf.Lerp(birdInterval.x, birdInterval.y, (float)_random.NextDouble());
            }
        }

        void CallBird()
        {
            var source = _birdSources[_nextBirdSource++ % _birdSources.Count];
            var side = _random.NextDouble() < 0.5 ? -1f : 1f;
            var along = ((float)_random.NextDouble() - 0.3f) * 160f;
            var across = side * (60f + (float)_random.NextDouble() * 100f);
            source.transform.position = _frame.Point(along, across, 4f + (float)_random.NextDouble() * 8f);
            source.pitch = 0.95f + (float)_random.NextDouble() * 0.1f;
            var call = _birdCalls[_random.Next(_birdCalls.Length)];
            source.clip = call;
            source.volume = birds;
            source.Play();
            // Songbirds often call twice.
            if (_random.NextDouble() < 0.4) source.PlayScheduled(AudioSettings.dspTime + call.length + 0.25);
        }

        AudioSource Loop(string name, float[] samples, float volume, float spatial)
        {
            var source = Source(name, spatial);
            source.clip = Clip(name, samples);
            source.loop = true;
            source.volume = volume;
            // Start each loop at a different point, so layers never line up.
            source.timeSamples = _random.Next(samples.Length);
            source.Play();
            return source;
        }

        AudioSource Source(string name, float spatial)
        {
            var source = new GameObject(name).AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = spatial;
            source.minDistance = 1.5f;
            source.maxDistance = 30f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.dopplerLevel = 0f;
            // Lower priority than the stroke sounds (default 128), so they are never the ones dropped.
            source.priority = 200;
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
