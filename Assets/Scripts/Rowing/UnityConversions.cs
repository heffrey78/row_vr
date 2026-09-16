using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The one place UnityEngine types meet the engine-free stroke library.
    ///
    /// RowVR.Stroke deliberately cannot see UnityEngine (its asmdef sets noEngineReferences),
    /// which is what lets its tests run under plain `dotnet test`. That boundary is only
    /// worth anything if the conversions stay here rather than leaking outward, so resist
    /// the temptation to add a Vector3 field to anything in RowVR.Stroke.
    /// </summary>
    public static class UnityConversions
    {
        public static Vec3 ToVec3(this Vector3 v) => new Vec3(v.x, v.y, v.z);

        public static Vector3 ToVector3(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
