using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.XR.OpenXR;
#endif

namespace RowVR.Rowing
{
    /// <summary>
    /// Lets the headset's recenter (holding the Meta button, or the palm pinch) move this app's
    /// tracking space (#96). The XR Origin asks for Floor tracking; with recentering off, Unity's
    /// OpenXR maps Floor to the STAGE space, which the recenter is not expected to move, so it
    /// did nothing the app could see. With it on, Floor is LOCAL_FLOOR, the runtime reports each
    /// recenter, and BoatFrame re-seats the rower.
    ///
    /// Set before the first scene loads, so the space the XR Origin asks for is the right one.
    /// Device only: the Editor has no OpenXR runtime on Linux, and Play Mode uses the simulator.
    /// </summary>
    static class Recentering
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Enable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            OpenXRSettings.SetAllowRecentering(true);
            OpenXRSettings.RefreshRecenterSpace();
            Debug.Log("[RowVR] OpenXR recentering on: Floor tracking is LOCAL_FLOOR.");
#endif
        }
    }
}
