using System;

namespace RowVR.Stroke
{
    /// <summary>Tuning for <see cref="BoatModel"/>.</summary>
    public sealed class BoatSettings
    {
        /// <summary>
        /// Drag, as power = DragCoefficient * speed^3 in steady state. 2.8 is the constant rowing
        /// monitors use to turn watts into pace (a 2:00/500 m split is 203 W), so the paces
        /// shown read like a rowing machine's.
        /// </summary>
        public float DragCoefficient = 2.8f;

        /// <summary>
        /// Effective mass, kg: how fast the boat gains speed on the drive and loses it on the
        /// recovery. Only the feel of the glide depends on it, not the steady pace.
        /// </summary>
        public float MassKg = 80f;

        /// <summary>
        /// The machine's resistance dial, 1-10. The rower sets it to match their machine,
        /// since a low-end machine cannot report it (#26). 5 is the calibration point.
        /// </summary>
        public int ResistanceLevel = 5;

        /// <summary>
        /// Watts per (m/s)^3 of handle speed during the drive, at resistance 5. On a flywheel
        /// or magnet machine, resistance rises with speed, so power goes roughly with handle
        /// speed cubed. Calibrated so the steady rowing-machine fixture reads about 2:20/500 m.
        /// </summary>
        public float EffortCoefficient = 320f;

        /// <summary>
        /// Time constant, seconds, of each of the two smoothing stages behind the speed the
        /// world visibly moves at (#41). Two stages of 1.1 s lag about as much as one of
        /// 2 s, but cut the ~0.35 Hz per-stroke surge to under 5% where one stage of 1.5 s
        /// left 6%, measured on the steady fixture.
        /// </summary>
        public float VisibleSpeedSeconds = 1.1f;
    }

    /// <summary>
    /// Boat speed from the stroke (#26). There is no power measurement on this
    /// machine, so effort is inferred from handle speed during the drive (#41 context),
    /// and drag follows the same cube law rowing monitors use, so pace reads like a real
    /// monitor's.
    ///
    /// Two speeds come out. <see cref="Speed"/> is the true model speed, surge included; pace
    /// and distance come from it. <see cref="VisibleSpeed"/> is that speed smoothed over about
    /// a stroke, and is what the world should move at, so a harder stroke is seen over the
    /// next few strokes and the boat never lurches (#41).
    ///
    /// Only drives add speed. With no strokes, or no tracking, the boat glides to rest under
    /// drag and is never pushed by anything else.
    /// </summary>
    public sealed class BoatModel
    {
        readonly BoatSettings _settings;
        double _lastTime = double.NaN;
        float _visibleStage;

        // Below this speed, force = power / speed is capped, so the first drive from rest
        // does not divide by zero.
        const float MinForceSpeed = 0.5f;

        public BoatModel(BoatSettings settings = null)
        {
            _settings = settings ?? new BoatSettings();
        }

        public BoatSettings Settings => _settings;

        /// <summary>True speed, m/s, surge included.</summary>
        public float Speed { get; private set; }

        /// <summary>Speed smoothed for display and world motion, m/s.</summary>
        public float VisibleSpeed { get; private set; }

        /// <summary>Metres travelled, from the true speed.</summary>
        public double Distance { get; private set; }

        /// <summary>Propulsive power this update, W. Zero outside the drive.</summary>
        public float Power { get; private set; }

        public void Reset()
        {
            _lastTime = double.NaN;
            Speed = VisibleSpeed = Power = _visibleStage = 0f;
            Distance = 0d;
        }

        /// <summary>Advances the boat with the detector's output for this update.</summary>
        public void Update(double timeSeconds, StrokeState state)
        {
            // HandleVelocity is negative toward the finish, which is the drive's direction.
            var driving = state.Phase == StrokePhase.Drive || state.Phase == StrokePhase.Catch;
            Step(timeSeconds, driving ? Math.Max(0f, -state.HandleVelocity) : 0f);
        }

        /// <summary>Advances the boat with no stroke input: tracking lost, or no rower.</summary>
        public void Glide(double timeSeconds) => Step(timeSeconds, 0f);

        void Step(double timeSeconds, float handleSpeed)
        {
            var dt = double.IsNaN(_lastTime) ? 0f : (float)Math.Min(0.25d, Math.Max(0d, timeSeconds - _lastTime));
            _lastTime = timeSeconds;
            if (dt <= 0f) return;

            var resistance = 0.5f + Clamp(_settings.ResistanceLevel, 1, 10) / 10f;
            Power = _settings.EffortCoefficient * resistance * handleSpeed * handleSpeed * handleSpeed;

            var force = Power / Math.Max(Speed, MinForceSpeed);
            var drag = _settings.DragCoefficient * Speed * Speed;
            Speed = Math.Max(0f, Speed + (force - drag) / _settings.MassKg * dt);
            Distance += Speed * dt;

            var alpha = 1f - (float)Math.Exp(-dt / Math.Max(0.01f, _settings.VisibleSpeedSeconds));
            _visibleStage += (Speed - _visibleStage) * alpha;
            VisibleSpeed += (_visibleStage - VisibleSpeed) * alpha;
        }

        /// <summary>Seconds per 500 m at a speed, as rowing monitors show pace. Infinity at rest.</summary>
        public static float SplitSeconds(float speed) => speed > 0.05f ? 500f / speed : float.PositiveInfinity;

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
