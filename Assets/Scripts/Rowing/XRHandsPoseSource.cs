using System.Collections.Generic;
using RowVR.Stroke;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace RowVR.Rowing
{
    /// <summary>
    /// Live hand tracking as an <see cref="IHandPoseSource"/>, so the detector cannot tell
    /// the headset from a recorded clip.
    ///
    /// Uses the palm joint rather than a fingertip: the palm is the most stable joint under
    /// Quest hand tracking and is where a real oar handle would sit. Fingertips are noisier
    /// and, at the catch, are the joints most likely to be occluded by the hand itself.
    /// </summary>
    public sealed class XRHandsPoseSource : IHandPoseSource
    {
        static readonly List<XRHandSubsystem> Subsystems = new List<XRHandSubsystem>();

        readonly Transform _trackingOrigin;
        readonly Transform _head;
        XRHandSubsystem _subsystem;

        // Diagnostics for the "subsystem running, hands never tracked" case. The subsystem
        // is pumped from the PlayerLoop, and what each pump returns separates two very
        // different failures: no updates at all means the Unity-side update is not running;
        // updates arriving with no success flags means the runtime is declining to locate
        // hands for this app.
        XRHandSubsystem _subscribed;
        int _updates;
        XRHandSubsystem.UpdateSuccessFlags _lastFlags;

        /// <param name="trackingOrigin">
        /// The XR Origin's transform. XR Hands reports joint poses in tracking space, so
        /// they must be taken through this to reach world space. If the rig is ever moved,
        /// rotated or scaled (a boat that travels, say) and this is skipped, the handle will
        /// appear stuck at the world origin - check this first if tracking looks wrong.
        /// </param>
        /// <param name="head">The camera transform, used for the drive axis and seat reference.</param>
        public XRHandsPoseSource(Transform trackingOrigin, Transform head)
        {
            _trackingOrigin = trackingOrigin;
            _head = head;
        }

        public bool IsTracking { get; private set; }

        /// <summary>
        /// Why there is no pose, in a few words.
        ///
        /// "No hands" collapses five unrelated failures into one symptom - no subsystem
        /// registered, one registered but not running, a hand not tracked, a palm joint with
        /// no pose - and on device each guess costs putting the headset back on. Naming the
        /// failure turns that into one look at the readout.
        /// </summary>
        public string Status { get; private set; } = "starting";

        public bool TryGetPose(double timeSeconds, out HandSample sample)
        {
            sample = default;

            if (_subsystem == null || !_subsystem.running)
            {
                _subsystem = FindRunningSubsystem();
                Subscribe(_subsystem);
                if (_subsystem == null)
                {
                    SubsystemManager.GetSubsystems(Subsystems);
                    Status = Subsystems.Count == 0
                        ? "no hand subsystem registered"
                        : $"{Subsystems.Count} hand subsystem(s), none running";
                    IsTracking = false;
                    return false;
                }
            }

            // Cleared each frame so a palm-specific reason from an earlier frame cannot
            // outlive the condition that produced it.
            Status = string.Empty;
            var leftOk = TryGetPalm(_subsystem.leftHand, out var left);
            var rightOk = TryGetPalm(_subsystem.rightHand, out var right);
            if (!leftOk || !rightOk)
            {
                // Hands out of view or tracking dropped. Report no pose rather than a stale
                // one: the detector holds its state, where a repeated sample would read as
                // the handle having stopped dead.
                var which = !leftOk && !rightOk ? "both hands untracked"
                          : !leftOk ? "left hand untracked"
                          : "right hand untracked";
                // Keep the palm-specific reason if TryGetPalm set one.
                if (!Status.StartsWith("tracked, but", System.StringComparison.Ordinal))
                    Status = $"{which} ({SubsystemId}, updates {_updates}, flags {_lastFlags})";
                IsTracking = false;
                return false;
            }

            Status = "ok";
            IsTracking = true;
            sample = new HandSample(
                timeSeconds,
                left.ToVec3(),
                right.ToVec3(),
                _head.position.ToVec3(),
                _head.forward.ToVec3());
            return true;
        }

        /// <summary>
        /// The index fingertip of one hand, in world space, for pressing menu buttons (ADR-0013).
        /// Uses whichever subsystem the palms came from; false if that hand is not tracked.
        /// </summary>
        public bool TryGetFingertip(bool left, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (_subsystem == null || !_subsystem.running) return false;
            var hand = left ? _subsystem.leftHand : _subsystem.rightHand;
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var pose)) return false;
            worldPosition = _trackingOrigin != null ? _trackingOrigin.TransformPoint(pose.position) : pose.position;
            return true;
        }

        /// <summary>
        /// One hand's palm pose in world space, for the wrist menu (ADR-0015). In XR Hands the
        /// palm joint's up points out of the back of the hand, so the palm faces -up.
        /// </summary>
        public bool TryGetPalmPose(bool left, out Pose worldPose)
        {
            worldPose = default;
            if (_subsystem == null || !_subsystem.running) return false;
            var hand = left ? _subsystem.leftHand : _subsystem.rightHand;
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var pose)) return false;
            worldPose = _trackingOrigin != null
                ? new Pose(_trackingOrigin.TransformPoint(pose.position), _trackingOrigin.rotation * pose.rotation)
                : pose;
            return true;
        }

        bool TryGetPalm(XRHand hand, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (!hand.isTracked) return false;

            var joint = hand.GetJoint(XRHandJointID.Palm);
            if (!joint.TryGetPose(out var pose))
            {
                // Distinct from "not tracked": the hand is being tracked but this particular
                // joint has no pose, which would point at the joint choice rather than at
                // tracking. Worth telling apart before blaming the runtime.
                Status = "tracked, but palm joint has no pose";
                return false;
            }

            worldPosition = _trackingOrigin != null
                ? _trackingOrigin.TransformPoint(pose.position)
                : pose.position;
            return true;
        }

        void Subscribe(XRHandSubsystem subsystem)
        {
            if (subsystem == _subscribed) return;
            if (_subscribed != null) _subscribed.updatedHands -= OnUpdatedHands;
            _subscribed = subsystem;
            if (_subscribed != null) _subscribed.updatedHands += OnUpdatedHands;
        }

        void OnUpdatedHands(XRHandSubsystem subsystem,
                            XRHandSubsystem.UpdateSuccessFlags flags,
                            XRHandSubsystem.UpdateType type)
        {
            _updates++;
            _lastFlags = flags;
        }

        /// <summary>
        /// Prefers a real hand subsystem over a simulated one. The XR Interaction Simulator
        /// registers its own XRHandSubsystem, and if one ever runs on device alongside the
        /// OpenXR one, "first running" is a coin toss that silently picks hands that are
        /// never tracked. Simulated subsystems are only used when nothing else is running,
        /// which is exactly the Play Mode case they exist for.
        /// </summary>
        static XRHandSubsystem FindRunningSubsystem()
        {
            SubsystemManager.GetSubsystems(Subsystems);
            XRHandSubsystem simulated = null;
            foreach (var s in Subsystems)
            {
                if (!s.running) continue;
                if (IsSimulated(s)) { simulated ??= s; continue; }
                return s;
            }
            return simulated;
        }

        static bool IsSimulated(XRHandSubsystem subsystem)
        {
            var id = subsystem.subsystemDescriptor?.id ?? string.Empty;
            return id.IndexOf("simulat", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public string SubsystemId => _subsystem?.subsystemDescriptor?.id ?? "none";
    }
}
