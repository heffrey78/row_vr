using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// Chooses what the rower sees in their hands: two sculling oars held by the handle ends,
    /// or a single rowing-machine handle. The two look wrong together, since the handle sits
    /// between the hands where the oar handles are, so exactly one is shown.
    ///
    /// Can be switched in the Inspector during Play Mode.
    /// </summary>
    [RequireComponent(typeof(OarsView), typeof(HandleView))]
    public sealed class GripDisplay : MonoBehaviour
    {
        public enum GripMode
        {
            /// <summary>Two sculling oars, one in each hand. The default.</summary>
            Oars,

            /// <summary>A single handle between the hands, as on the rowing machine.</summary>
            Handle,
        }

        [SerializeField] GripMode mode = GripMode.Oars;

        OarsView _oars;
        HandleView _handle;
        GripMode _applied;
        bool _hasApplied;

        public GripMode Mode
        {
            get => mode;
            set { mode = value; Apply(); }
        }

        void Awake()
        {
            _oars = GetComponent<OarsView>();
            _handle = GetComponent<HandleView>();
            Apply();
        }

        // Picks up an Inspector change during Play Mode.
        void Update()
        {
            if (!_hasApplied || mode != _applied) Apply();
        }

        void Apply()
        {
            if (_oars == null || _handle == null) return;
            _oars.enabled = mode == GripMode.Oars;
            _handle.enabled = mode == GripMode.Handle;
            _applied = mode;
            _hasApplied = true;
        }
    }
}
