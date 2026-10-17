using System;

namespace RowVR.Stroke
{
    /// <summary>
    /// How the raft rides the water (#90): it heaves, pitches and rolls over the waves under
    /// its hull, and settles still on flat water. Only the raft moves; the rower's view never
    /// does.
    ///
    /// Each step samples the water's height on a grid under the hull and fits a plane to it:
    /// the plane's mean is where the raft wants to float, its slope along the raft the pitch,
    /// across it the roll. The pose then follows that target on a damped spring, so a crest
    /// lifts the bow, rolls under the rower and drops the stern, rather than snapping. The
    /// comfort limits are enforced here, not left to the scene: no angle or height goes past
    /// its limit, and no angle changes faster than the rate limit.
    /// </summary>
    public sealed class RaftRocking
    {
        readonly RaftRockingSettings _settings;
        double _lastTime = double.NaN;
        float _heaveVelocity, _pitchVelocity, _rollVelocity;

        public RaftRocking(RaftRockingSettings settings = null)
        {
            _settings = settings ?? new RaftRockingSettings();
        }

        public RaftRockingSettings Settings => _settings;

        /// <summary>Height of the raft above flat water, metres.</summary>
        public float Heave { get; private set; }

        /// <summary>Bow up, degrees.</summary>
        public float Pitch { get; private set; }

        /// <summary>Right side up, degrees (the right as the rower faces the bow).</summary>
        public float Roll { get; private set; }

        /// <summary>Fastest the pitch or roll changed in the last step, degrees per second.</summary>
        public float AngularRate { get; private set; }

        /// <summary>
        /// Advances the raft to <paramref name="timeSeconds"/>. <paramref name="heightAt"/> gives the
        /// water's height above flat at a point under the raft: metres to the right of its centre,
        /// then metres toward its bow.
        /// </summary>
        public void Update(double timeSeconds, Func<float, float, float> heightAt)
        {
            if (double.IsNaN(_lastTime)) { _lastTime = timeSeconds; return; }
            var dt = (float)(timeSeconds - _lastTime);
            _lastTime = timeSeconds;
            if (dt <= 0f) return;
            dt = Math.Min(dt, 0.25f);  // after a hitch, catch up gently rather than in one leap

            var (heave, pitch, roll) = Target(heightAt);
            var amount = Clamp(_settings.Amount, 0f, 1f);
            heave *= amount;
            pitch *= amount;
            roll *= amount;

            var startPitch = Pitch;
            var startRoll = Roll;
            var steps = (int)Math.Ceiling(dt / MaxStep);
            var h = dt / steps;
            for (var i = 0; i < steps; i++)
            {
                Heave = Spring(Heave, ref _heaveVelocity, heave, h, float.MaxValue, _settings.MaxHeaveMetres);
                Pitch = Spring(Pitch, ref _pitchVelocity, pitch, h, _settings.MaxDegreesPerSecond, _settings.MaxPitchDegrees);
                Roll = Spring(Roll, ref _rollVelocity, roll, h, _settings.MaxDegreesPerSecond, _settings.MaxRollDegrees);
            }
            AngularRate = Math.Max(Math.Abs(Pitch - startPitch), Math.Abs(Roll - startRoll)) / dt;
        }

        const float MaxStep = 1f / 120f;

        /// <summary>The plane fitted to the water under the hull: mean height, bow-up and right-up slopes in degrees.</summary>
        (float Heave, float Pitch, float Roll) Target(Func<float, float, float> heightAt)
        {
            var halfLength = _settings.LengthMetres * 0.5f * 0.9f;
            var halfWidth = _settings.WidthMetres * 0.5f * 0.85f;
            float sum = 0f, alongMoment = 0f, acrossMoment = 0f, alongSquares = 0f, acrossSquares = 0f;
            var count = 0;
            for (var a = 0; a < AlongSamples; a++)
            {
                var along = halfLength * (2f * a / (AlongSamples - 1) - 1f);
                for (var c = 0; c < AcrossSamples; c++)
                {
                    var across = halfWidth * (2f * c / (AcrossSamples - 1) - 1f);
                    var height = heightAt(across, along);
                    sum += height;
                    alongMoment += height * along;
                    acrossMoment += height * across;
                    alongSquares += along * along;
                    acrossSquares += across * across;
                    count++;
                }
            }
            // The grid is symmetric about the centre, so the plane's three terms separate. A
            // crest is narrower than the raft, and a light raft rides up its face rather than
            // averaging it out, so the slopes are scaled by Response.
            var pitch = (float)(Math.Atan(_settings.Response * alongMoment / alongSquares) * 180d / Math.PI);
            var roll = (float)(Math.Atan(_settings.Response * acrossMoment / acrossSquares) * 180d / Math.PI);
            return (sum / count, pitch, roll);
        }

        const int AlongSamples = 7;
        const int AcrossSamples = 3;

        float Spring(float value, ref float velocity, float target, float dt, float maxRate, float limit)
        {
            var omega = 2f * (float)Math.PI * _settings.Frequency;
            var acceleration = omega * omega * (target - value) - 2f * _settings.Damping * omega * velocity;
            velocity = Clamp(velocity + acceleration * dt, -maxRate, maxRate);
            value += velocity * dt;
            if (value > limit || value < -limit)
            {
                value = Clamp(value, -limit, limit);
                velocity = 0f;
            }
            return value;
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>Tuning for <see cref="RaftRocking"/>. The limits come from #90.</summary>
    public sealed class RaftRockingSettings
    {
        /// <summary>The raft's hull, metres: where the water is sampled.</summary>
        public float LengthMetres = 4.3f;
        public float WidthMetres = 2f;

        /// <summary>How much more the raft tips than the plane fitted under it: it rides up a crest's face.</summary>
        public float Response = 2.5f;

        /// <summary>How much of the water's motion the raft shows, 0 (still) to 1. A comfort setting.</summary>
        public float Amount = 1f;

        /// <summary>Most the raft tips bow or stern, degrees (#90: at most 5).</summary>
        public float MaxPitchDegrees = 5f;

        /// <summary>Most the raft rolls, degrees (#90: at most 6).</summary>
        public float MaxRollDegrees = 6f;

        /// <summary>Most the raft rises or sinks, metres (#90: at most 0.15).</summary>
        public float MaxHeaveMetres = 0.15f;

        /// <summary>Fastest pitch or roll may change, degrees per second (#90: at most 20).</summary>
        public float MaxDegreesPerSecond = 20f;

        /// <summary>The raft's natural bob, Hz: an inflatable on a short spring.</summary>
        public float Frequency = 0.9f;

        /// <summary>Damping ratio: below 1, so it overshoots a little and bobs once.</summary>
        public float Damping = 0.55f;
    }
}
