using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// Every colour in the scene, in one place (#42), so the whole look can be re-keyed:
    /// a morning lake now, dusk or the rapids later. Colours are authored here as seen, in
    /// sRGB; materials convert them for the Linear-space project.
    ///
    /// The defaults are a soft morning: cream and peach at the horizon, a teal lake, sage and
    /// blue-grey hills fading into haze, and warm ink.
    /// </summary>
    public sealed class ScenePalette : MonoBehaviour
    {
        [Header("Sky")]
        public Color skyZenith = new Color(0.55f, 0.7f, 0.84f);
        public Color skyHorizon = new Color(0.96f, 0.91f, 0.8f);
        public Color horizonGlow = new Color(0.98f, 0.8f, 0.62f);
        public Color cloud = new Color(1f, 0.98f, 0.94f);
        public Color cloudShade = new Color(0.86f, 0.84f, 0.88f);
        public Color sun = new Color(1f, 0.93f, 0.72f);

        [Header("Water")]
        public Color waterDeep = new Color(0.24f, 0.46f, 0.52f);
        public Color waterLight = new Color(0.44f, 0.64f, 0.66f);
        public Color waterDash = new Color(0.9f, 0.94f, 0.9f);
        public Color reflection = new Color(0.2f, 0.34f, 0.34f);

        [Header("Land")]
        public Color hillFar = new Color(0.66f, 0.72f, 0.8f);
        public Color hillMiddle = new Color(0.5f, 0.6f, 0.62f);
        public Color hillNear = new Color(0.4f, 0.52f, 0.44f);
        public Color bank = new Color(0.52f, 0.6f, 0.38f);
        public Color treeDark = new Color(0.22f, 0.36f, 0.3f);
        public Color treeLight = new Color(0.4f, 0.54f, 0.36f);
        public Color reeds = new Color(0.62f, 0.64f, 0.38f);

        [Header("Boat")]
        public Color hull = new Color(0.93f, 0.9f, 0.82f);
        public Color deck = new Color(0.72f, 0.52f, 0.34f);
        public Color rigger = new Color(0.36f, 0.38f, 0.42f);
        public Color oarShaft = new Color(0.92f, 0.88f, 0.78f);
        public Color blade = new Color(0.84f, 0.36f, 0.26f);
        public Color buoyA = new Color(0.95f, 0.55f, 0.28f);
        public Color buoyB = new Color(0.97f, 0.95f, 0.88f);

        [Header("River (rapids)")]
        public Color rock = new Color(0.46f, 0.47f, 0.5f);
        public Color rockShade = new Color(0.32f, 0.33f, 0.38f);
        public Color foam = new Color(0.95f, 0.97f, 0.96f);
        public Color raft = new Color(0.96f, 0.62f, 0.2f);
        public Color raftFloor = new Color(0.3f, 0.33f, 0.38f);

        [Header("Ink and paper")]
        public Color ink = new Color(0.17f, 0.14f, 0.16f);
        public Color shadeTint = new Color(0.66f, 0.68f, 0.82f);
        public Color paper = new Color(0.97f, 0.94f, 0.86f);
    }
}
