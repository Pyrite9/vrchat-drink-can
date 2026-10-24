using UnityEngine;

namespace Pyrite9.DrinkCan
{
    /// <summary>
    /// Builds the skinned can model from a generated mesh:
    ///   &lt;parent&gt;/CanModel (SkinnedMeshRenderer)
    ///       Can (root bone) / Tab, TearPanel, Liquid
    /// Bone names match DrinkCanMeshBuilder.BoneNames, so animation paths are fixed:
    ///   CanModel/Can/Tab, CanModel/Can/TearPanel, CanModel/Can/Liquid.
    /// </summary>
    public static class DrinkCanModel
    {
        public const string ModelName = "CanModel";

        public static SkinnedMeshRenderer Create(Transform parent, Mesh mesh, DrinkCanMeshBuilder.Rig rig, Material[] materials)
        {
            var go = new GameObject(ModelName);
            go.transform.SetParent(parent, false);
            var root = new GameObject(DrinkCanMeshBuilder.BoneNames[DrinkCanMeshBuilder.BoneCan]).transform;
            root.SetParent(go.transform, false);

            Transform Child(int index, Vector3 pivot)
            {
                var t = new GameObject(DrinkCanMeshBuilder.BoneNames[index]).transform;
                t.SetParent(root, false);
                t.localPosition = pivot;
                return t;
            }

            var bones = new[]
            {
                root,
                Child(DrinkCanMeshBuilder.BoneTab, rig.tabPivot),
                Child(DrinkCanMeshBuilder.BoneTear, rig.tearPivot),
                Child(DrinkCanMeshBuilder.BoneLiquid, rig.liquidFull),
            };
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = root;
            smr.sharedMaterials = materials;
            var b = mesh.bounds;
            b.Expand(0.02f);            // opened tab / folded panel stay inside
            smr.localBounds = b;
            smr.updateWhenOffscreen = false;
            return smr;
        }

        /// <summary>Poses the bones: tab and tear angles in degrees (negative = open), liquid level 0 (empty) .. 1 (full).</summary>
        public static void Pose(SkinnedMeshRenderer smr, DrinkCanMeshBuilder.Rig rig, float tabDeg, float tearDeg, float level)
        {
            var bones = smr.bones;
            bones[DrinkCanMeshBuilder.BoneTab].localRotation = Quaternion.Euler(0f, 0f, tabDeg);
            bones[DrinkCanMeshBuilder.BoneTear].localRotation = Quaternion.Euler(0f, 0f, tearDeg);
            bones[DrinkCanMeshBuilder.BoneLiquid].localPosition = Vector3.Lerp(rig.liquidEmpty, rig.liquidFull, Mathf.Clamp01(level));
        }
    }
}
