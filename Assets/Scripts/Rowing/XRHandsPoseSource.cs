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

        public bool TryGetPose(double timeSeconds, out HandSample sample)
        {
            sample = default;

            if (_subsystem == null || !_subsystem.running)
            {
                _subsystem = FindRunningSubsystem();
                if (_subsystem == null)
                {
                    IsTracking = false;
                    return false;
                }
            }

            if (!TryGetPalm(_subsystem.leftHand, out var left) ||
                !TryGetPalm(_subsystem.rightHand, out var right))
            {
                // Hands out of view or tracking dropped. Report no pose rather than a stale
                // one: the detector holds its state, where a repeated sample would read as
                // the handle having stopped dead.
                IsTracking = false;
                return false;
            }

            IsTracking = true;
            sample = new HandSample(
                timeSeconds,
                left.ToVec3(),
                right.ToVec3(),
                _head.position.ToVec3(),
                _head.forward.ToVec3());
            return true;
        }

        bool TryGetPalm(XRHand hand, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (!hand.isTracked) return false;

            var joint = hand.GetJoint(XRHandJointID.Palm);
            if (!joint.TryGetPose(out var pose)) return false;

            worldPosition = _trackingOrigin != null
                ? _trackingOrigin.TransformPoint(pose.position)
                : pose.position;
            return true;
        }

        static XRHandSubsystem FindRunningSubsystem()
        {
            SubsystemManager.GetSubsystems(Subsystems);
            foreach (var s in Subsystems)
            {
                if (s.running) return s;
            }
            return null;
        }
    }
}
