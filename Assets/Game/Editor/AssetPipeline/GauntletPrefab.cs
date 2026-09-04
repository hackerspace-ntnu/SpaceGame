using System;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The pieces every worn gauntlet's prefab needs, in one place.
    ///
    /// <para>
    /// Since 2026-09-02 the gauntlets are one family: their models are authored against
    /// <c>components/props/gauntlet_base.blend</c>'s hardpoint deck in the gauntlet frame (origin
    /// at the wrist joint, the arm down the model's -Z, the back of the arm along +Y) at TRUE suit
    /// scale, and <c>BodyEquipmentController.WearOnForearm</c> seats them on the forearm bone.
    /// Since 2026-09-04 they no longer CONTAIN that base: the bracer is worn permanently by
    /// <c>ForearmBracers</c>, and a gauntlet is only the device standing on it. Everything
    /// that follows from that — the fit component, the sizes, the marker adoption — was being
    /// written a second and a third time as each builder was reworked, which is exactly the
    /// duplication the family was supposed to remove.
    /// </para>
    /// </summary>
    public static class GauntletPrefab
    {
        /// <summary>
        /// What every gauntlet's <c>ItemGrip.holdSize</c> is: nothing.
        ///
        /// <para>
        /// Zero does not mean "unset" to <see cref="ItemGrip"/>; it means "keep the size the
        /// artist built", and that is the right answer for a family authored against the rig's own
        /// forearm. A number here would be a second opinion about how big an arm is.
        /// </para>
        /// </summary>
        public const float HoldSize = 0f;

        /// <summary>
        /// What every gauntlet is drawn at on the pack mat: nothing, which means its true size.
        ///
        /// <para>
        /// This was 0.54 m — a deliberate shrink, because a gauntlet was a device wrapped in a
        /// bracer and the bracer's girth round a whole forearm made the pair 0.77 m long and absurd
        /// lying on a mat. Since 2026-09-04 the bracer is worn permanently and is not part of the
        /// item, so what goes on the mat is the device alone: 0.39 m for the flashlight, 0.60 for
        /// the grappling hook. Those are gadget-sized already, and <see cref="ItemGrip.PackSize"/>
        /// is explicit that a size here is only for items whose true size reads as absurd. The
        /// reason for the shrink went away with the bracer, so the shrink went with it.
        /// </para>
        /// </summary>
        public const float PackSize = 0f;

        /// <summary>
        /// Give a prefab root the components that make it a worn forearm gauntlet, and size it.
        ///
        /// <para>
        /// No <c>rotationOffset</c>: the offset exists to turn an item in the HAND frame, and two
        /// of these gauntlets carried a -90 for exactly that reason while they were seated over
        /// the glove. On the forearm the model's own axes are the frame, so any offset here is a
        /// tilt nobody asked for.
        /// </para>
        /// </summary>
        public static void MakeWorn(GameObject root, Transform gripPoint, Transform sizeReference)
        {
            var grip = root.GetComponent<ItemGrip>() ?? root.AddComponent<ItemGrip>();
            SetPrivate(grip, "gripPoint", gripPoint);
            SetPrivate(grip, "holdSize", HoldSize);
            SetPrivate(grip, "packSize", PackSize);
            SetPrivate(grip, "rotationOffset", Vector3.zero);
            SetPrivate(grip, "positionOffset", Vector3.zero);
            SetPrivate(grip, "sizeReference", sizeReference);

            // Written, not left to the field initialisers. A GauntletFit that is already on the
            // prefab keeps whatever was SERIALIZED on it, and changing the constants in the
            // component does nothing to it — which is how four gauntlets came back from a reseat
            // still carrying the arm cuff's 2.3 across and 1.9 along, and would have been worn at
            // more than twice their size with nothing in the console.
            var fit = root.GetComponent<GauntletFit>() ?? root.AddComponent<GauntletFit>();
            SetPrivate(fit, "cuffScale", GauntletFit.DefaultCuffScale);
            SetPrivate(fit, "lengthScale", GauntletFit.DefaultLengthScale);
            SetPrivate(fit, "wristGap", GauntletFit.DefaultWristGap);
            SetPrivate(fit, "rollDegrees", 0f);
        }

        /// <summary>
        /// Lift a marker out of the imported model onto the prefab root, keeping its place.
        ///
        /// <para>
        /// The marker itself is deactivated rather than deleted: it belongs to the FBX, and
        /// deleting a child of an imported model instance is an override that the next reimport
        /// re-creates. Copied from <c>SuckerPuncherBuilder</c>, which had the only version of it.
        /// </para>
        /// </summary>
        public static Transform AdoptMarker(Transform root, Transform model, string markerName,
                                            string wantedName, string logTag)
        {
            var adopted = new GameObject(wantedName);
            adopted.transform.SetParent(root, false);

            Transform marker = FindDeep(model, markerName);
            if (marker == null)
            {
                Debug.LogWarning($"[{logTag}] No {markerName} in the FBX; {wantedName} left at the origin.");
                return adopted.transform;
            }

            adopted.transform.localPosition = root.InverseTransformPoint(marker.position);
            marker.gameObject.SetActive(false);
            return adopted.transform;
        }

        /// <summary>Hide every marker nothing adopted, so no stray empty ships in the model.</summary>
        public static void HideRemainingMarkers(Transform model)
        {
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                if (child.name.StartsWith("Marker_", StringComparison.Ordinal))
                    child.gameObject.SetActive(false);
        }

        /// <summary>The first descendant with this name, active or not.</summary>
        public static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        /// <summary>Write a private serialized field, the way every builder here does.</summary>
        public static void SetPrivate(object target, string field, object value)
        {
            System.Reflection.FieldInfo info = null;
            for (Type t = target.GetType(); t != null && info == null; t = t.BaseType)
                info = t.GetField(field, System.Reflection.BindingFlags.Instance |
                                         System.Reflection.BindingFlags.NonPublic |
                                         System.Reflection.BindingFlags.Public);

            if (info == null)
                throw new MissingFieldException($"{target.GetType().Name} has no field '{field}'");

            info.SetValue(target, value);
            if (target is UnityEngine.Object o) EditorUtility.SetDirty(o);
        }
    }
}
