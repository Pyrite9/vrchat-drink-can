// Pyrite9 Drink Can — "preview in hand" (inspector button), after the wheelchair's sitting preview (WcSitPreview)
//   Runs the real build (NDMF ProcessAvatar — fit, size, materials) on a copy of the avatar, takes the can out in the spawn hand
//   and shows the copy in place of the original. The original is hidden in the Scene view only (SceneVisibilityManager — not saved).
//   ProcessAvatar marks the copy's scene as modified -> build in an empty temporary scene, move the copy home, close the temp scene.
//   No animator is evaluated: Hold is switched on and put on the grip point directly, constraints are turned off so they
//   cannot move it, and the holding hand takes the inspector's hand pose through HumanPoseHandler — also when the build turned
//   the pose off for this avatar (DrinkCanGripFit's safety; HandFits = false), so it can be adjusted here. Moving the
//   hand-pose sliders re-poses the hand at once (ApplyHandPose); where the can sits is refitted on Refresh. The fine-tuning
//   offsets / twist move the can at once too: the difference from the values the copy was built with, in the hand's frame
//   x the body scale the build used (DcBuildRecord) — the same as the build would place it. Descriptor and Pipeline Manager are removed from the copy (not in the SDK list).
//   Stops itself on script reload, Play, closing the scene and quitting Unity.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace Pyrite9.DrinkCan
{
    [InitializeOnLoad]
    public static class DcHoldPreview
    {
        const string Suffix = " (Drink Can Preview)";

        static GameObject clone, original;
        static Animator cloneAnim;
        static HumanPose cloneRest;
        // constraint-driven hand bones (Lapwing's skinned thumb), moved as the build's fit moves them (DrinkCanGripFit.Follow)
        static List<(Transform t, Transform src, Quaternion rot, Vector3 pos, bool parent)> cloneFollow;
        // the hand pose the build made (its Gesture grip clip) and the inspector pose it was built from: shown until the
        // sliders move (a fitted grip — the contact grip, 9-41 — is not the inspector's values)
        static Dictionary<string, float> builtMuscles;
        static string builtPoseKey;
        static string PoseKey(DrinkCan c) => $"{c.handPoseMode}{c.poseIndex}{c.poseMiddle}{c.poseRing}{c.poseLittle}{c.poseThumb}{c.gripHandPose}";
        static Transform cloneHold, cloneGrip;
        static bool cloneLeft, clonePose, cloneFits;
        static Vector3 builtOffset;
        static float builtTwist, builtBodyScale = 1f, builtCanHeight = 0.1f;
        static Vector2 builtTilt;
        static string settingsHash;
        public static event System.Action Changed;

        public static bool Busy { get; private set; }
        public static bool IsActiveFor(GameObject avatar) => clone != null && original == avatar;
        public static bool Stale(DrinkCan c) => clone != null && settingsHash != Hash(c);
        public static string LastError { get; private set; }

        static DcHoldPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingEditMode) Stop(); };
            EditorSceneManager.sceneClosing += (s, removing) => { if (original != null && original.scene == s) Stop(); };
            EditorApplication.wantsToQuit += () => { Stop(); return true; };
            // copies left over from before a reload (name suffix + DontSave)
            EditorApplication.delayCall += () =>
            {
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                    if (go != null && go.transform.parent == null && go.name.EndsWith(Suffix) && (go.hideFlags & HideFlags.DontSave) != 0 && go.scene.IsValid())
                        Object.DestroyImmediate(go);
            };
        }

        static string Hash(DrinkCan c) => c == null ? "" :
            $"{c.hand}|{c.sizePreset}|{c.heightMm}|{c.diameterMm}|{(c.labelTexture != null ? c.labelTexture.GetInstanceID() : 0)}|{c.labelFit}|{c.liquidColor}|{c.sizeScale}|{c.gripHandPose}"
            + $"|{c.handPoseMode}|{c.poseIndex}|{c.poseMiddle}|{c.poseRing}|{c.poseLittle}|{c.poseThumb}";

        public static void Start(DrinkCan can)
        {
            Stop();
            LastError = null;
            var desc = can != null ? can.GetComponentInParent<VRCAvatarDescriptor>(true) : null;
            if (desc == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var src = desc.gameObject;
            var home = src.scene;
            bool left = can.hand == DrinkCanHand.Left;
            Busy = true;
            Changed?.Invoke();
            Scene tmp = default;
            GameObject c = null;
            try
            {
                EditorUtility.DisplayProgressBar("Pyrite9 Drink Can", "Building the preview…", 0.4f);
                var active = SceneManager.GetActiveScene();
                tmp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(active);
                c = Object.Instantiate(src, src.transform.position, src.transform.rotation);
                c.transform.localScale = src.transform.lossyScale;
                c.name = src.name;   // so the build record (DcBuildRecord) keeps the original's name
                SceneManager.MoveGameObjectToScene(c, tmp);
                nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(c);
                foreach (var t in c.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
                SceneManager.MoveGameObjectToScene(c, home);
                c.name = src.name + Suffix;

                var all = c.GetComponentsInChildren<Transform>(true);
                var hold = all.FirstOrDefault(t => t.name == DrinkCanConst.Hold && t.GetComponent<VRCParentConstraint>() != null);
                var grip = all.FirstOrDefault(t => t.name.StartsWith(left ? DrinkCanConst.GripL : DrinkCanConst.GripR));
                if (hold == null || grip == null)
                    throw new System.Exception("The can was not built — see the Last build section and the build log.");
                foreach (var pc in c.GetComponentsInChildren<VRCParentConstraint>(true)) pc.IsActive = false;
                hold.SetPositionAndRotation(grip.position, grip.rotation);
                hold.gameObject.SetActive(true);
                foreach (var a in c.GetComponentsInChildren<Animator>(true)) a.enabled = false;
                // did the build give the holding hand a pose? (its Gesture grip layer)
                var gesture = c.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                    .FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.Gesture).animatorController as UnityEditor.Animations.AnimatorController;
                var gripLayer = gesture != null ? gesture.layers.FirstOrDefault(l => l.name == "Pyrite9 DrinkCan Grip " + (left ? "L" : "R")) : null;
                cloneFits = gripLayer != null;
                builtMuscles = null;
                var holdClip = gripLayer?.stateMachine.states.Select(st => st.state).FirstOrDefault(st => st.name == "Holding")?.motion as AnimationClip;
                if (holdClip != null)
                {
                    builtMuscles = new Dictionary<string, float>();
                    foreach (var b in AnimationUtility.GetCurveBindings(holdClip))   // "RightHand.Index.1 Stretched" -> "Right Index 1 Stretched"
                    {
                        int dot = b.propertyName.IndexOf('.');
                        if (dot < 0) continue;
                        var name = b.propertyName.Substring(0, dot).Replace("Hand", "") + " " + b.propertyName.Substring(dot + 1).Replace('.', ' ');
                        builtMuscles[name] = AnimationUtility.GetEditorCurve(holdClip, b).keys[0].value;
                    }
                }
                builtPoseKey = PoseKey(can);
                clonePose = true;
                cloneAnim = c.GetComponent<Animator>();
                cloneHold = hold; cloneGrip = grip; cloneLeft = left;
                if (cloneAnim != null && cloneAnim.isHuman)
                {
                    using (var hph = new HumanPoseHandler(cloneAnim.avatar, c.transform)) { cloneRest = new HumanPose(); hph.GetHumanPose(ref cloneRest); }
                    var hb = cloneAnim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                    cloneFollow = hb != null ? DrinkCanGripFit.ConstrainedUnder(c.transform, new HashSet<Transform>(hb.GetComponentsInChildren<Transform>(true))) : null;
                }
                else clonePose = false;
                foreach (var pm in c.GetComponents<Component>().Where(x => x != null && x.GetType().Name == "PipelineManager").ToArray()) Object.DestroyImmediate(pm);
                Object.DestroyImmediate(c.GetComponent<VRCAvatarDescriptor>());

                clone = c; original = src; settingsHash = Hash(can);
                builtOffset = can.gripOffset; builtTwist = can.gripTwist; builtTilt = can.gripTilt;
                var rec = DcBuildRecord.Get(src.name);
                builtBodyScale = rec != null && rec.bodyScale > 0f ? rec.bodyScale : 1f;
                builtCanHeight = DrinkCanFit.CanHeight(can, rec != null && rec.canScale > 0f ? rec.canScale : 1f);
                ApplyHandPose(can);
                SceneVisibilityManager.instance.DisablePicking(c, true);
                SceneVisibilityManager.instance.Hide(src, true);
                SceneView.RepaintAll();
            }
            catch (System.Exception e)
            {
                LastError = e.Message;
                Debug.LogException(e);
                if (c != null) Object.DestroyImmediate(c);
                clone = null; original = null;
            }
            finally
            {
                if (tmp.IsValid()) EditorSceneManager.CloseScene(tmp, true);
                EditorUtility.ClearProgressBar();
                Busy = false;
                Changed?.Invoke();
            }
        }

        public static bool HandPosed => clone != null && clonePose;
        // the last preview build gave the spawn hand its pose (false: the pose did not fit, or 'Hand pose' is off)
        public static bool HandFits => clone != null && cloneFits;

        // the last build's holding hand as the inspector's pose vectors (index, middle, ring, little, thumb) — what Auto found, so
        // switching to Manual starts from it
        public static bool BuiltPose(out Vector4[] pose)
        {
            pose = null;
            if (clone == null || builtMuscles == null || builtMuscles.Count == 0) return false;
            string side = cloneLeft ? "Left" : "Right";
            float M(string n) => builtMuscles.TryGetValue($"{side} {n}", out var v) ? v : 0f;
            Vector4 Fi(string f) => new Vector4(M(f + " 1 Stretched"), M(f + " 2 Stretched"), M(f + " 3 Stretched"), M(f + " Spread"));
            pose = new[] { Fi("Index"), Fi("Middle"), Fi("Ring"), Fi("Little"),
                new Vector4(M("Thumb Spread"), M("Thumb 1 Stretched"), M("Thumb 2 Stretched"), M("Thumb 3 Stretched")) };
            return true;
        }

        // the holding hand in the inspector's pose (muscles only; the can stays where the last build put it)
        public static void ApplyHandPose(DrinkCan can)
        {
            if (clone == null || cloneAnim == null || can == null) return;
            if (!clonePose) { PlaceHold(can); return; }
            var pose = cloneRest;
            pose.muscles = (float[])cloneRest.muscles.Clone();
            if (can.gripHandPose)
                foreach (var kv in builtMuscles != null && PoseKey(can) == builtPoseKey ? builtMuscles : DrinkCanGripFit.HandMuscles(can, cloneLeft, cloneAnim))
                {
                    int i = System.Array.IndexOf(HumanTrait.MuscleName, kv.Key);
                    if (i >= 0) pose.muscles[i] = kv.Value;
                }
            using (var hph = new HumanPoseHandler(cloneAnim.avatar, clone.transform)) hph.SetHumanPose(ref pose);
            if (cloneFollow != null) DrinkCanGripFit.Follow(cloneFollow);
            PlaceHold(can);
        }

        // the can on the grip point, moved by the offsets changed since the build (hand frame as DrinkCanFit.HandFrame)
        static void PlaceHold(DrinkCan can)
        {
            if (cloneHold == null || cloneGrip == null) return;
            var p = new Pose(cloneGrip.position, cloneGrip.rotation);
            var d = can.gripOffset - builtOffset;
            var fr = DrinkCanFit.HandFrame(clone.transform, cloneAnim, cloneLeft);
            // as the build: the built tilt off, the offsets / twist changed since, the current tilt on (DrinkCanFit.Tilt)
            p = DrinkCanFit.Tilt(p, fr, builtTilt, builtCanHeight, true);
            if (fr.bone != null) p.position += (fr.n * d.x + fr.f * d.y + fr.a * d.z) * builtBodyScale;
            p.rotation = Quaternion.AngleAxis(can.gripTwist - builtTwist, p.rotation * Vector3.up) * p.rotation;
            p = DrinkCanFit.Tilt(p, fr, can.gripTilt, builtCanHeight);
            cloneHold.SetPositionAndRotation(p.position, p.rotation);
            SceneView.RepaintAll();
        }

        public static void Stop()
        {
            bool had = clone != null || original != null;
            if (clone != null) Object.DestroyImmediate(clone);
            if (original != null) SceneVisibilityManager.instance.Show(original, true);
            clone = null; original = null; settingsHash = null; Busy = false;
            cloneAnim = null; cloneHold = null; cloneGrip = null; clonePose = false; cloneFits = false; cloneFollow = null; builtMuscles = null;
            if (had) { SceneView.RepaintAll(); Changed?.Invoke(); }
        }
    }
}
#endif
