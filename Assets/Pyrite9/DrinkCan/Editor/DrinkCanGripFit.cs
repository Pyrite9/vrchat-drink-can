// Fits the grip at build time: the holding hand from the inspector, and the can placed in it.
//   The hand = humanoid muscles on the DrinkCan component (inspector 'Hand pose'); the defaults are the author's own pose
//   (2026-10-22, 9-32), shaped by eye on Milfy: fingers nearly straight along the can's side, thumb spread far out (past the
//   humanoid limit) holding the front. Muscles only — the thumb included — so no rotation constraints (the IK + constraint
//   thumb of 9-27..9-29 broke the hand in VR) and nothing taken from anyone else's pose (the gimmick is distributed).
//   The can goes where the author put it, as fractions of the hand (along wrist -> middle knuckle) and of the can (lid above the
//   index knuckle), then out of the palm as far as it takes for no skin to sink in — so another avatar's hand keeps the shape.
//   Safety: the same muscles land differently on another rig (Lapwing's thumb comes across the palm, right where the can sits,
//   9-33). When the can cannot sit against the palm's skin, that hand gets no pose (follows the controller) and the default
//   grip. Bones that follow the humanoid ones through constraints (Lapwing's skinned thumb) are moved with them (9-36).
//   The avatar is posed through HumanPoseHandler, and every transform is restored afterwards.
//   Auto (DrinkCan.handPoseMode, the default since 2026-10-23): the contact grip below fits the fingers, thumb and can to the
//   hand's own skin instead (ContactFit); it falls back to the fixed pose when it finds no grip.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace Pyrite9.DrinkCan
{
    static class DrinkCanGripFit
    {
        static readonly string[] Fingers = { "Index", "Middle", "Ring", "Little" };
        internal static float AlongHand = 0.908f;        // can axis, fraction of wrist -> middle knuckle (a Profile may differ)
        internal static float LidAboveIndex = 0.384f;    // lid above the index knuckle, x can height (a Profile may differ)
        const float OutMax = 0.12f, OutStep = 0.0005f;   // m x body scale — can axis out of the hand bone's plane
        const float Touch = 0.0005f;           // m x body scale
        const float MaxSink = 0.0015f;         // m x body scale — skin allowed into the can
        // m x body scale — seated: can side this close to the palm's skin (9-36; was 30 mm from the hand bone's plane, which
        // counted the palm's own flesh). Can side to palm skin: Milfy -0.8 mm (pressed), Kipfel +0.7, Lapwing 11.3 (thumb root)
        const float SeatGap = 0.015f;
        // off = fingers and thumb follow the controller and only the fitted grip position is used
        internal static bool HandPoseEnabled = true;
        // dev (shaping a pose): log what touches the can at the palm even when it fits
        internal static bool LogContact;
        // the contact grip (9-40..9-47, DrinkCan.handPoseMode Auto): can against the palm skin, each finger joint curled until its
        // own segment touches the can, the thumb hugged onto it; a result that is no grip falls back to the fixed pose
        const float ContactCenter = 0.45f;  // first guess: the knuckle line's middle at this fraction of the can's height
        // the can placements the contact grip tries (fraction of the hand / tilt in degrees), 9-42
        internal static float[] ContactAlongs = { 0.74f, 0.82f, 0.9f };
        internal static float[] ContactTilts = { -10f, 0f, 10f, 20f, 30f };   // pivot at the knuckles' middle (round 5)
        internal static float[] ContactTiltOuts = { 0f, 15f };   // can top out of the palm (room for the thumb's root), 9-44
        // the thumb's search: spread toward the palm only, past the humanoid limit as the author's poses do (Milfy default -1.74,
        // Lapwing -3); +1 splayed Kipfel's thumb backward (round 4). Root a little past the limit
        internal static float ThumbSpreadMin = -2f, ThumbSpreadMax = 0f, ThumbRootMax = 1.25f;   // -1.25 (round 7 try) left Milfy's and Kipfel's thumb roots 12-13 mm off; ThumbBad's 60 deg cap is the rig-independent limit
        // the index finger's upper edge at this fraction of the can's height — a real grip, just under the lid's shoulder
        // (round 5's hands sat at 57-70 %)
        internal static float IndexTop = 0.83f;
        // the can's straight side, as a fraction of its height (the neck tapers above it; round 6 Kipfel's thumb sat on the taper)
        const float CylTop = 0.86f;
        // the right hand's chosen placement, reused for the left hand of the same avatar (its search came out the same, round 6
        // logs) — one placement instead of ~44
        static Transform lastAvatar;
        static float lastAlong, lastTilt, lastOut, lastScore;

        // a rig the default pose does not fit, recognised by its bones: its own pose and can placement (9-37). Numbers only,
        // nothing from the avatar. The pose is used while the component still has the default pose (the inspector writes it
        // into the component so the sliders show it); the placement whenever the rig matches — it follows the hand's shape
        internal sealed class Profile
        {
            public string name;
            public float alongHand, lidAboveIndex;
            public Vector4 index, middle, ring, little, thumb;
        }

        // Lapwing: the humanoid thumb is a weightless dummy chain (Thumb1Dummy_R..) the skinned thumb follows by constraints,
        // and its root sits where the default seat puts the can. Shaped on Lapwing 1.0.10 from the dev renders (2026-10-22,
        // 9-37/9-38): can 90 % along the hand, lid a quarter of its height above the index knuckle — the hand on the middle of
        // the can (index knuckle at 0.75 of its height, little fingertip 0.12; at 85 % / 50 % the little finger went past the
        // bottom), fingertips 0-1 mm off the can, thumb root on it, fingers spread together (no gaps)
        static readonly Profile LapwingProfile = new Profile
        {
            name = "Lapwing", alongHand = 0.9f, lidAboveIndex = 0.25f,
            index = new Vector4(-0.13f, -0.13f, -0.13f, 0.3f), middle = new Vector4(-0.15f, -0.15f, -0.15f, 0.2f),
            ring = new Vector4(0f, 0f, 0f, -0.3f), little = new Vector4(0.2f, 0.2f, 0.2f, -0.5f),
            thumb = new Vector4(-3f, 1f, 0.11f, -1f),
        };

        internal static Profile ProfileFor(Animator anim)
        {
            if (anim == null || !anim.isHuman) return null;
            var t = anim.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            if (t != null && t.name == "Thumb1Dummy_R" && t.parent != null && t.parent.Find("Thumb1_R") != null) return LapwingProfile;
            return null;
        }

        internal static bool IsDefaultPose(DrinkCan c) =>
            c.poseIndex == DrinkCan.DefaultPoseIndex && c.poseMiddle == DrinkCan.DefaultPoseMiddle && c.poseRing == DrinkCan.DefaultPoseRing
            && c.poseLittle == DrinkCan.DefaultPoseLittle && c.poseThumb == DrinkCan.DefaultPoseThumb;

        // HumanTrait muscle name -> value for one hand, from the component (also the inspector preview); a matching rig's
        // Profile pose while the component has the default pose
        internal static Dictionary<string, float> HandMuscles(DrinkCan can, bool left, Animator anim = null)
        {
            var side = left ? "Left" : "Right";
            var m = new Dictionary<string, float>();
            var prof = ProfileFor(anim);
            bool useProf = prof != null && IsDefaultPose(can);
            var fingers = useProf ? new[] { prof.index, prof.middle, prof.ring, prof.little } : new[] { can.poseIndex, can.poseMiddle, can.poseRing, can.poseLittle };
            var thumb = useProf ? prof.thumb : can.poseThumb;
            for (int f = 0; f < Fingers.Length; f++)
            {
                m[$"{side} {Fingers[f]} 1 Stretched"] = fingers[f].x;
                m[$"{side} {Fingers[f]} 2 Stretched"] = fingers[f].y;
                m[$"{side} {Fingers[f]} 3 Stretched"] = fingers[f].z;
                m[$"{side} {Fingers[f]} Spread"] = fingers[f].w;
            }
            m[$"{side} Thumb Spread"] = thumb.x;
            m[$"{side} Thumb 1 Stretched"] = thumb.y;
            m[$"{side} Thumb 2 Stretched"] = thumb.z;
            m[$"{side} Thumb 3 Stretched"] = thumb.w;
            return m;
        }

        public struct Result
        {
            public bool ok;
            public Pose grip;                           // fitted grip (world, rest pose)
            public Dictionary<string, float> muscles;   // hand muscles (HumanTrait name -> value), fingers and thumb
        }

        // signed distance from p to a solid cylinder (base centre c, unit axis ax, radius R, height H): < 0 inside
        static float CylSD(Vector3 p, Vector3 c, Vector3 ax, float R, float H)
        {
            var d = p - c;
            float h = Vector3.Dot(d, ax);
            float dr = (d - ax * h).magnitude - R;
            float dh = Mathf.Max(-h, h - H);
            if (dr <= 0f && dh <= 0f) return Mathf.Max(dr, dh);
            return Mathf.Sqrt(Mathf.Max(dr, 0f) * Mathf.Max(dr, 0f) + Mathf.Max(dh, 0f) * Mathf.Max(dh, 0f));
        }

        // bones under the hand that rotation / parent constraints (Unity or VRC) drive from another bone under the hand, with
        // how they sit on that source now (at rest), parents first. Lapwing's humanoid thumb is a dummy chain (Thumb1Dummy_R..)
        // and the skinned thumb (Thumb1_R..) copies it through rotation constraints — constraints do not run between
        // SetHumanPose and BakeMesh, so without this the fit saw Lapwing's thumb at rest whatever the values (9-36)
        internal static List<(Transform t, Transform src, Quaternion rot, Vector3 pos, bool parent)> ConstrainedUnder(Transform avatar, HashSet<Transform> handBones)
        {
            var found = new List<(Transform t, Transform src, bool parent)>();
            foreach (var t in handBones)
            {
                if (t.GetComponent<IConstraint>() is IConstraint uc && (uc is RotationConstraint || uc is ParentConstraint) && uc.constraintActive && uc.sourceCount > 0)
                {
                    int best = 0;
                    for (int i = 1; i < uc.sourceCount; i++) if (uc.GetSource(i).weight > uc.GetSource(best).weight) best = i;
                    found.Add((t, uc.GetSource(best).sourceTransform, uc is ParentConstraint));
                    // a one-source Unity rotation constraint is followed as Unity evaluates it: weight, axes, offset, at rest
                    if (uc is RotationConstraint rc && rc.sourceCount == 1)
                        rotSpecs[t] = new RotSpec
                        {
                            offset = Quaternion.Euler(rc.rotationOffset), rest = Quaternion.Euler(rc.rotationAtRest),
                            weight = rc.weight * rc.GetSource(0).weight, axes = rc.rotationAxis, restEuler = t.localEulerAngles,
                        };
                    else rotSpecs.Remove(t);
                }
            }
            foreach (var dead in rotSpecs.Keys.Where(k => k == null).ToList()) rotSpecs.Remove(dead);
            foreach (var vc in avatar.GetComponentsInChildren<VRCConstraintBase>(true))
            {
                if (!vc.IsActive || !(vc is VRCRotationConstraint || vc is VRCParentConstraint) || vc.Sources.Count == 0) continue;
                var t = vc.GetEffectiveTargetTransform();
                if (t == null || !handBones.Contains(t)) continue;
                int best = 0;
                for (int i = 1; i < vc.Sources.Count; i++) if (vc.Sources[i].Weight > vc.Sources[best].Weight) best = i;
                found.Add((t, vc.Sources[best].SourceTransform, vc is VRCParentConstraint));
            }
            int Depth(Transform x) { int d = 0; for (; x != null; x = x.parent) d++; return d; }
            return found.Where(f => f.src != null && handBones.Contains(f.src) && f.src != f.t)
                .OrderBy(f => Depth(f.t))
                .Select(f => (f.t, f.src, Quaternion.Inverse(f.src.rotation) * f.t.rotation, f.src.InverseTransformPoint(f.t.position), f.parent))
                .ToList();
        }

        // a Unity RotationConstraint as it evaluates (checked against Unity on Lapwing's thumb, 0.00 deg, 2026-10-23): the
        // local rotation = parent^-1 x source x offset, the axes it does not drive kept as they rest, then weighed against
        // 'at rest'. Lapwing's skinned thumb root follows its dummy at weight 0.41 and the two outer joints on X only — copied
        // whole (9-36) the fit saw a thumb that turned much further than in game, and its thumb went into the can (9-47)
        sealed class RotSpec
        {
            public Quaternion offset, rest;
            public float weight;
            public Axis axes;
            public Vector3 restEuler;
        }
        static readonly Dictionary<Transform, RotSpec> rotSpecs = new Dictionary<Transform, RotSpec>();

        // put the constrained bones back on their sources (after SetHumanPose; parents first, as ConstrainedUnder orders them)
        internal static void Follow(List<(Transform t, Transform src, Quaternion rot, Vector3 pos, bool parent)> follow)
        {
            foreach (var f in follow)
            {
                if (!f.parent && f.t.parent != null && rotSpecs.TryGetValue(f.t, out var sp))
                {
                    var e = (Quaternion.Inverse(f.t.parent.rotation) * f.src.rotation * sp.offset).eulerAngles;
                    var r = sp.restEuler;
                    var m = Quaternion.Euler((sp.axes & Axis.X) != 0 ? e.x : r.x, (sp.axes & Axis.Y) != 0 ? e.y : r.y, (sp.axes & Axis.Z) != 0 ? e.z : r.z);
                    f.t.localRotation = Quaternion.Slerp(sp.rest, m, sp.weight);
                    continue;
                }
                f.t.rotation = f.src.rotation * f.rot;
                if (f.parent) f.t.position = f.src.TransformPoint(f.pos);
            }
        }

        static float DistSeg(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-12f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        // the mesh's vertices `idx` with its blend shapes as the renderer has them set (frames interpolated as Unity does), in
        // bind space — what skinning starts from
        static Vector3[] SkinRest(SkinnedMeshRenderer smr, int[] idx)
        {
            var mesh = smr.sharedMesh;
            var v = mesh.vertices;
            var r = idx.Select(i => v[i]).ToArray();
            var d0 = new Vector3[v.Length];
            var d1 = new Vector3[v.Length];
            for (int b = 0; b < mesh.blendShapeCount; b++)
            {
                float wt = smr.GetBlendShapeWeight(b);
                if (Mathf.Abs(wt) < 1e-4f) continue;
                int fc = mesh.GetBlendShapeFrameCount(b);
                int hi = 0;
                while (hi < fc - 1 && mesh.GetBlendShapeFrameWeight(b, hi) < wt) hi++;
                float wHi = mesh.GetBlendShapeFrameWeight(b, hi);
                mesh.GetBlendShapeFrameVertices(b, hi, d1, null, null);
                if (hi == 0) { float k = wt / wHi; for (int j = 0; j < idx.Length; j++) r[j] += d1[idx[j]] * k; continue; }
                float wLo = mesh.GetBlendShapeFrameWeight(b, hi - 1);
                mesh.GetBlendShapeFrameVertices(b, hi - 1, d0, null, null);
                float u = (wt - wLo) / Mathf.Max(1e-6f, wHi - wLo);
                for (int j = 0; j < idx.Length; j++) r[j] += Vector3.LerpUnclamped(d0[idx[j]], d1[idx[j]], u);
            }
            return r;
        }

        public static Result Fit(Transform avatar, DrinkCanFit.Result fit, DrinkCan can, bool left, StringBuilder log)
        {
            var r = new Result { grip = fit.Grip(left) };
            var anim = avatar.GetComponent<Animator>();
            var side = left ? "Left" : "Right";
            Transform B(string n) => anim.GetBoneTransform((HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), side + n));
            var hand = B("Hand");
            if (hand == null || B("IndexProximal") == null) { DcWarn.Add(log, DcWarn.Id.NoHandBones, side); return r; }
            // the skin: the mesh with finger vertices (dominant bone under the hand, not the hand itself — a sleeve cuff only
            // reaches the hand: Milfy's cardigan), a mesh named Body.. first (Kipfel's shirt sleeve carries thumb vertices too).
            // Not by the humanoid thumb tip: Lapwing's is a dummy end bone with no weights (2026-10-22)
            var handBones = new HashSet<Transform>(hand.GetComponentsInChildren<Transform>(true));
            int FingerCount(SkinnedMeshRenderer x) { var bs = x.bones; return x.sharedMesh.boneWeights.Count(bw0 => bw0.boneIndex0 < bs.Length && bs[bw0.boneIndex0] != hand && handBones.Contains(bs[bw0.boneIndex0])); }
            var cands = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(x => x.sharedMesh != null && x.bones != null && x.bones.Contains(hand) && FingerCount(x) > 0)
                .ToList();
            var skin = cands.Where(x => x.name.StartsWith("Body", System.StringComparison.OrdinalIgnoreCase)).OrderByDescending(FingerCount).FirstOrDefault()
                ?? cands.OrderByDescending(FingerCount).FirstOrDefault();
            if (skin == null) { DcWarn.Add(log, DcWarn.Id.NoSkin, side); return r; }

            // every vertex of the hand (by dominant bone: the hand and everything under it)
            var bw = skin.sharedMesh.boneWeights;
            var bones = skin.bones;
            var handV = Enumerable.Range(0, bw.Length).Where(i => bw[i].boneIndex0 < bones.Length && handBones.Contains(bones[bw[i].boneIndex0])).ToArray();
            if (handV.Length == 0) { DcWarn.Add(log, DcWarn.Id.NoHandVerts, side, skin.name); return r; }

            var names = HumanTrait.MuscleName;
            var all = avatar.GetComponentsInChildren<Transform>(true);
            var saved = all.Select(t => (t.localPosition, t.localRotation, t.localScale)).ToArray();
            var baked = new Mesh();
            var hph = new HumanPoseHandler(anim.avatar, avatar);
            try
            {
                var pose = new HumanPose();
                hph.GetHumanPose(ref pose);
                var frame = DrinkCanFit.HandFrame(avatar, anim, left);
                var dims = DrinkCanMeshBuilder.GetDims(can.Shape);
                float R = dims.R * 0.001f * fit.canScale, H = dims.H * 0.001f * fit.canScale, s = fit.bodyScale;
                var handPos0 = hand.position;
                var handRot0 = hand.rotation;
                var inv0 = Quaternion.Inverse(handRot0);
                var posL0 = inv0 * (r.grip.position - handPos0);
                var rotL = inv0 * r.grip.rotation;
                var nL = inv0 * frame.n;
                var fL = inv0 * frame.f;

                var muscles = HandMuscles(can, left, anim);
                var prof = ProfileFor(anim);
                float along = prof != null ? prof.alongHand : AlongHand, lidK = prof != null ? prof.lidAboveIndex : LidAboveIndex;
                var follow = ConstrainedUnder(avatar, handBones);
                Vector3[] w = null;
                var muscleIdx = new Dictionary<string, int>();
                for (int i = 0; i < names.Length; i++) muscleIdx[names[i]] = i;
                void Bake()
                {
                    skin.BakeMesh(baked, true);   // with scale + the renderer's TRS: checked on Kipfel (root x1.12) and Milfy
                    var vtx = baked.vertices;
                    var m = skin.transform.localToWorldMatrix;
                    w = handV.Select(i => m.MultiplyPoint3x4(vtx[i])).ToArray();
                }
                // contact grip: the hand's vertices skinned here (bind pose + blend shapes as set, linear blend) instead of
                // BakeMesh of the whole body per pose (16-29 s per avatar, round 4). Checked against BakeMesh on poses 1, 20 and
                // 200; a rig where they differ by more than 0.1 mm goes back to BakeMesh
                bool auto = can.HandPoseModeNow == DrinkCanHandPoseMode.Auto;
                bool lbs = auto;
                var bindPos = lbs ? SkinRest(skin, handV) : null;
                var bindposes = skin.sharedMesh.bindposes;
                var usedB = lbs ? handV.SelectMany(i => new[] { bw[i].boneIndex0, bw[i].boneIndex1, bw[i].boneIndex2, bw[i].boneIndex3 })
                    .Where(b => b >= 0 && b < bones.Length && b < bindposes.Length && bones[b] != null).Distinct().ToArray() : null;
                var boneM = new Matrix4x4[bones.Length];
                int poses = 0;
                int[] only = null;   // contact grip: skin just these hand vertices (the finger being shaped), the rest keep theirs
                void PoseHand()
                {
                    foreach (var kv in muscles) { if (muscleIdx.TryGetValue(kv.Key, out var i)) pose.muscles[i] = kv.Value; }
                    hph.SetHumanPose(ref pose);
                    Follow(follow);
                    if (!lbs) { Bake(); return; }
                    foreach (var b in usedB) boneM[b] = bones[b].localToWorldMatrix * bindposes[b];
                    if (only != null && w != null && w.Length == handV.Length)
                    {
                        foreach (var k in only) w[k] = Skin(k);
                        return;
                    }
                    var lw = new Vector3[handV.Length];
                    for (int k = 0; k < handV.Length; k++) lw[k] = Skin(k);
                    poses++;
                    if (poses == 1 || poses == 20 || poses == 200)
                    {
                        Bake();
                        float err = 0f;
                        for (int k = 0; k < handV.Length; k++) err = Mathf.Max(err, (w[k] - lw[k]).magnitude);
                        if (err > 0.0001f * fit.bodyScale) { lbs = false; log.AppendLine($"contact grip {side}: direct skinning off ({skin.name}: {err * 1000f:0.00} mm from BakeMesh at pose {poses})"); return; }
                    }
                    w = lw;
                }
                Vector3 Skin(int k)
                {
                    var x = bw[handV[k]];
                    var v = bindPos[k];
                    var p = boneM[x.boneIndex0].MultiplyPoint3x4(v) * x.weight0;
                    if (x.weight1 > 0f) p += boneM[x.boneIndex1].MultiplyPoint3x4(v) * x.weight1;
                    if (x.weight2 > 0f) p += boneM[x.boneIndex2].MultiplyPoint3x4(v) * x.weight2;
                    if (x.weight3 > 0f) p += boneM[x.boneIndex3].MultiplyPoint3x4(v) * x.weight3;
                    return p;
                }

                if (auto)
                {
                    var keep = new Dictionary<string, float>(muscles);
                    if (ContactFit()) return r;
                    foreach (var kv in keep) muscles[kv.Key] = kv.Value;   // no grip found: the fixed pose below
                }

                // the contact grip (DrinkCan.handPoseMode Auto, 9-40..9-47). Searched over can placements (ContactAlongs, widened
                // until the best is inside, x ContactTilts x ContactTiltOuts, then a finer look round the best), each scored after
                // the whole hand is posed:
                //  1. the can's axis along the knuckle line (on the palm), leaned by the tilt about the knuckles' middle, `along` of
                //     the wrist -> middle-knuckle length; depth solved so the palm (1.5 mm) and the fingers' roots (2 mm) press in no
                //     further; the can slid along its axis so the index finger's upper edge is at IndexTop of its height
                //  2. fingers: spread so they run parallel (AlignAll), then each hugs the can root first — for each root bend the
                //     middle and tip close until a pad would sink, then the tip alone (capped by the curve and ~0.8 x the middle);
                //     scored by segments touching, tip angle round the can, the curve's bends and how a hand shares them. Gather
                //     closes the gaps between fingertips; the height is set again with the fingers as they lie
                //  3. thumb: grid over spread (toward the palm) x root x middle, the best few hugged onto the can (middle, then the
                //     tip to its pad) and judged as they end up: band 70..120 deg round the other way at the index knuckle's height,
                //     crossing the axis, on the can's straight side, no joint bent back, not into the index finger
                //  4. Settle: the depth again with the thumb's root as it sits; if it moved, the fingers and the thumb again
                //  5. score: fingertips short of their reach, roots/palm off the can, skin past the can's ends, thumb cost, sinking,
                //     height error, a weak pull toward 90 % upright. The left hand tries the right hand's placement first.
                // A result that is no grip (skin deep in the can, a finger or the thumb's tip off it, the palm far from it, a thumb
                // joint bent back) returns false: the fixed pose below
                bool ContactFit()
                {
                    string[] fn = { "Index", "Middle", "Ring", "Little", "Thumb" };
                    string[] jn = { "Proximal", "Intermediate", "Distal" };
                    var segOfBone = new Dictionary<Transform, int>();   // f * 3 + j
                    var segBone = new Transform[15];                   // the bone that carries the skin (Lapwing's thumb: the copy)
                    for (int f = 0; f < 5; f++)
                        for (int j = 0; j < 3; j++) { var t = B(fn[f] + jn[j]); if (t != null) { segOfBone[t] = f * 3 + j; segBone[f * 3 + j] = t; } }
                    foreach (var fl in follow) if (segOfBone.TryGetValue(fl.src, out var sj)) { segOfBone[fl.t] = sj; segBone[sj] = fl.t; }
                    // humanoid rigs may leave out finger bones: such a hand gets the fixed pose (the solver walks all 15)
                    for (int sg = 0; sg < 15; sg++)
                        if (segBone[sg] == null) { log.AppendLine($"contact grip {side}: no {fn[sg / 3]} {jn[sg % 3]} bone — fixed pose"); return false; }
                    var segV = new List<int>[15];
                    for (int i = 0; i < 15; i++) segV[i] = new List<int>();
                    var palmV = new List<int>();
                    var fingerK = new int[5][];
                    for (int k = 0; k < handV.Length; k++)
                    {
                        var t = bones[bw[handV[k]].boneIndex0];
                        while (t != null && t != hand && !segOfBone.ContainsKey(t)) t = t.parent;
                        if (t != null && segOfBone.TryGetValue(t, out var sk)) segV[sk].Add(k);
                        // the palm: the hand bone's skin, or a helper bone's under it that is no finger's (a rig with metacarpal
                        // or palm bones would otherwise have no palm, and the can would go through it)
                        else if (t == hand) palmV.Add(k);
                    }
                    for (int f = 0; f < 4; f++) fingerK[f] = segV[f * 3].Concat(segV[f * 3 + 1]).Concat(segV[f * 3 + 2]).ToArray();
                    fingerK[4] = segV[12].Concat(segV[13]).Concat(segV[14]).Concat(palmV).ToArray();
                    string Mus(int f, int j) => $"{side} {fn[f]} {j + 1} Stretched";
                    string Spr(int f) => $"{side} {fn[f]} Spread";
                    string tS = $"{side} Thumb Spread";
                    var inspector = new Dictionary<string, float>(muscles);
                    float tch = Touch * s, mm = 0.001f * s;
                    var nW = hand.rotation * nL;
                    var fW = hand.rotation * fL;
                    var rot0 = hand.rotation * rotL;
                    var ax0 = rot0 * Vector3.up;
                    Vector3 at = Vector3.zero, ax = ax0;
                    Quaternion rotW = rot0;
                    float palm = 0f, o = 0f;
                    float SD(int k) => CylSD(w[k], at, ax, R, H);
                    float Min(List<int> ks) { float m = float.MaxValue; foreach (var k in ks) m = Mathf.Min(m, SD(k)); return m; }

                    // rest hand (all finger and thumb muscles 0): pads = each segment's skin past its first quarter, base = before it
                    foreach (var key in muscles.Keys.ToList()) muscles[key] = 0f;
                    PoseHand();
                    var padV = new List<int>[15];
                    var baseV = new List<int>();   // the fingers' roots by the knuckles (they meet the can before any bending)
                    var tipK = new int[15];
                    for (int sg = 0; sg < 15; sg++)
                    {
                        padV[sg] = new List<int>();
                        tipK[sg] = -1;
                        if (segBone[sg] == null || segV[sg].Count == 0) continue;
                        var a = segBone[sg].position;
                        int j = sg % 3;
                        Vector3 b = j < 2 && segBone[sg + 1] != null ? segBone[sg + 1].position
                            : a + (a - (j > 0 && segBone[sg - 1] != null ? segBone[sg - 1].position : hand.position)) * 0.8f;
                        var d = b - a;
                        float best = float.MinValue;
                        foreach (var k in segV[sg])
                        {
                            float p = Vector3.Dot(w[k] - a, d) / Mathf.Max(1e-8f, d.sqrMagnitude);
                            if (p >= 0.25f) padV[sg].Add(k); else if (j == 0 && sg < 12) baseV.Add(k);
                            if (p > best) { best = p; tipK[sg] = k; }
                        }
                        if (padV[sg].Count < 8) padV[sg] = new List<int>(segV[sg]);
                    }
                    int Tip(int f) => tipK[f * 3 + 2] >= 0 ? tipK[f * 3 + 2] : tipK[f * 3 + 1];
                    float Pad(int sg) => padV[sg].Count > 0 ? Min(padV[sg]) : float.MaxValue;
                    float FingerMin(int f, int fromJ = 0) { float m = float.MaxValue; for (int j = fromJ; j < 3; j++) m = Mathf.Min(m, Min(segV[f * 3 + j])); return m; }
                    // the bones' own bend at joint j of finger f (deg, + toward the palm); the tip end is the farthest skin
                    float Flex(int f, int j)
                    {
                        var p0 = j == 0 ? hand.position : segBone[f * 3 + j - 1].position;
                        var p1 = segBone[f * 3 + j].position;
                        var p2 = j < 2 && segBone[f * 3 + j + 1] != null ? segBone[f * 3 + j + 1].position : (Tip(f) >= 0 ? w[Tip(f)] : p1);
                        var from = p1 - p0; var to = p2 - p1;
                        return -Vector3.SignedAngle(from, to, Vector3.Cross(from, -nW));
                    }

                    // the thumb bends in its own plane (turned ~90 deg from the fingers'), so its joints are measured about their own
                    // curl axis, found at rest by curling each joint a little (in its parent's frame; + = curl). The root against
                    // its rest direction, the middle and tip against the segment before. Round 4 measured them in the fingers' plane
                    // and ruled nothing out: Kipfel's thumb came out bent back at the root and tip (-42/+45/-33)
                    Vector3 SegDir(int sg)
                    {
                        var a = segBone[sg].position;
                        int j = sg % 3;
                        if (j < 2 && segBone[sg + 1] != null) return segBone[sg + 1].position - a;
                        if (tipK[sg] >= 0) return w[tipK[sg]] - a;
                        return j > 0 && segBone[sg - 1] != null ? a - segBone[sg - 1].position : a - hand.position;
                    }
                    Transform ThumbParent(int j) => j == 0 ? hand : segBone[12 + j - 1];
                    bool thumbOk = segBone[12] != null && segBone[13] != null && segBone[14] != null;
                    var thumbAxis = new Vector3[3];
                    var thumbRest0 = Vector3.zero;   // the root segment at rest, in the hand's frame
                    if (thumbOk)
                    {
                        thumbRest0 = Quaternion.Inverse(hand.rotation) * SegDir(12);
                        for (int j = 0; j < 3; j++)
                        {
                            var par = ThumbParent(j);
                            var d0 = Quaternion.Inverse(par.rotation) * SegDir(12 + j);
                            muscles[Mus(4, j)] = -0.5f; PoseHand();
                            var d1 = Quaternion.Inverse(par.rotation) * SegDir(12 + j);
                            muscles[Mus(4, j)] = 0f; PoseHand();
                            var c = Vector3.Cross(d0, d1);
                            thumbAxis[j] = c.sqrMagnitude > 1e-14f ? c.normalized : Vector3.zero;
                        }
                    }
                    float ThumbFlex(int j)
                    {
                        if (!thumbOk || thumbAxis[j] == Vector3.zero) return 0f;
                        var axW = ThumbParent(j).rotation * thumbAxis[j];
                        var from = Vector3.ProjectOnPlane(j == 0 ? hand.rotation * thumbRest0 : SegDir(12 + j - 1), axW);
                        var to = Vector3.ProjectOnPlane(SegDir(12 + j), axW);
                        return Vector3.SignedAngle(from, to, axW);
                    }
                    // no thumb joint bent back past -5 deg (or 5 deg past its rest, if it rests bent back); the root opened at most
                    // 25 deg past its rest
                    var thumbMin = new float[3];
                    for (int j = 1; j < 3; j++) thumbMin[j] = Mathf.Min(-5f, ThumbFlex(j) - 5f);

                    // the index finger as capsules (bone line, radius from its skin at rest) — the thumb must not go into it
                    var capBone = new Transform[3]; var capEnd = new Vector3[3]; var capR = new float[3];
                    for (int j = 0; j < 3; j++)
                    {
                        var t = segBone[j];
                        if (t == null || segV[j].Count == 0) continue;
                        var a = t.position; var e = a + SegDir(j);
                        if ((e - a).sqrMagnitude < 1e-10f) continue;
                        var ds = segV[j].Select(k => DistSeg(w[k], a, e)).OrderBy(x => x).ToList();
                        capBone[j] = t; capEnd[j] = t.InverseTransformPoint(e); capR[j] = ds[ds.Count / 2] * 0.85f;
                    }
                    float IndexOverlap()
                    {
                        float worst = 0f;
                        for (int j = 0; j < 3; j++)
                        {
                            if (capBone[j] == null) continue;
                            var a = capBone[j].position; var e = capBone[j].TransformPoint(capEnd[j]);
                            for (int sg = 13; sg <= 14; sg++) foreach (var k in segV[sg]) worst = Mathf.Max(worst, capR[j] - 1.5f * mm - DistSeg(w[k], a, e));
                        }
                        return worst;
                    }
                    float ThumbBad()
                    {
                        float bad = Mathf.Max(0f, IndexOverlap()) / mm;
                        if (thumbOk)
                        {
                            // the root: not swung back toward the back of the hand by more than ~25 deg, nor more than 60 deg from its
                            // rest direction whatever the muscles (spread is in muscle units — a different angle on every rig — and
                            // the root's flex about an axis found at spread 0 misread at -2, round 5)
                            var d = SegDir(12).normalized; var r0 = (hand.rotation * thumbRest0).normalized;
                            bad += 100f * Mathf.Max(0f, Vector3.Dot(r0, nW) - Vector3.Dot(d, nW) - 0.42f);
                            bad += Mathf.Max(0f, Vector3.Angle(d, r0) - 60f);
                            bad += Mathf.Max(0f, thumbMin[1] - ThumbFlex(1)) + Mathf.Max(0f, thumbMin[2] - ThumbFlex(2));
                        }
                        return bad;
                    }

                    Vector3 e1 = Vector3.right, e2 = Vector3.up;
                    void Frame()
                    {
                        e1 = (-nW - ax * Vector3.Dot(-nW, ax)).normalized;
                        e2 = Vector3.Cross(ax, e1);
                        if (Vector3.Dot(e2, fW) < 0f) e2 = -e2;
                    }
                    // angle round the can from the palm side, + the way the fingers wrap; a little past 180 still counts as round
                    float Around(Vector3 p)
                    {
                        var d = p - at; d -= ax * Vector3.Dot(d, ax);
                        float a = Mathf.Atan2(Vector3.Dot(d, e2), Vector3.Dot(d, e1)) * Mathf.Rad2Deg;
                        return a < -30f ? a + 360f : a;
                    }

                    // can placed with the hand at rest; the thumb's root no longer sets the depth here (round 4: at rest it pushed
                    // Lapwing's whole hand off the can, roots 3-10 mm off) — Settle takes it in once the thumb has its pose. The axis
                    // runs along the knuckle line (on the palm), the tilt pivots at the knuckles' middle (at the index knuckle the
                    // ring and little fingers slid 15-26 mm off at 30 deg, round 4); tilt = top toward the fingertips, tiltOut = top
                    // out of the palm
                    Vector3 cAt = Vector3.zero;
                    // depth solved, not stepped (stepping out from a start already clear left Kipfel's palm 3.7 mm off): for each
                    // skin point beside the can, the push along nW that puts it just at the can's side less what may press in. Only
                    // skin behind the axis (`upTo`, the push the axis is at now): not fingers already wrapped round the far side
                    float Depth(Vector3 c, bool thumbRoot, float upTo)
                    {
                        var nP = nW - ax * Vector3.Dot(nW, ax);
                        float aa = nP.sqrMagnitude, res = float.MinValue;
                        void Need(List<int> ks, float allow)
                        {
                            float r2 = (R - allow) * (R - allow);
                            foreach (var k in ks)
                            {
                                var d = w[k] - c;
                                float h = Vector3.Dot(d, ax);
                                if (h < 0f || h > H) continue;
                                var rp = d - ax * h;
                                if (Vector3.Dot(rp, nP) / aa > upTo) continue;
                                float bb = -2f * Vector3.Dot(rp, nP), cc = rp.sqrMagnitude - r2;
                                float disc = bb * bb - 4f * aa * cc;
                                if (disc < 0f) continue;
                                res = Mathf.Max(res, (-bb + Mathf.Sqrt(disc)) / (2f * aa));
                            }
                        }
                        Need(palmV, MaxSink * s);
                        Need(baseV, 2f * mm);
                        if (thumbRoot) Need(segV[12], 3f * mm);
                        return res;
                    }
                    void PlaceCan(float along, float tilt, float tiltOut)
                    {
                        foreach (var key in muscles.Keys.ToList()) muscles[key] = 0f;
                        PoseHand();
                        nW = hand.rotation * nL; fW = hand.rotation * fL; rot0 = hand.rotation * rotL; ax0 = rot0 * Vector3.up;
                        var idxK = B("IndexProximal").position;
                        var litK = B("LittleProximal") != null ? B("LittleProximal").position : idxK;
                        var kMid = (idxK + litK) * 0.5f;
                        var kl = idxK - litK;
                        kl -= nW * Vector3.Dot(kl, nW);
                        if (kl.sqrMagnitude > 1e-10f)
                        {
                            if (Vector3.Dot(kl, ax0) < 0f) kl = -kl;
                            rot0 = Quaternion.FromToRotation(ax0, kl.normalized) * rot0;
                            ax0 = rot0 * Vector3.up;
                        }
                        rotW = Quaternion.AngleAxis(tiltOut, Vector3.Cross(ax0, nW)) * Quaternion.AngleAxis(tilt, Vector3.Cross(ax0, fW)) * rot0;
                        ax = rotW * Vector3.up;
                        // the knuckle line's middle at ContactCenter of the can's height, `along` down the hand
                        var pK = hand.position + fW * (along * frame.length) + ax0 * Vector3.Dot(kMid - hand.position, ax0);
                        var c = pK - ax * (ContactCenter * H);
                        var palmD = new List<float>();
                        foreach (var k in palmV)
                        {
                            var d = w[k] - c;
                            float h = Vector3.Dot(d, ax);
                            if (h < 0f || h > H || Mathf.Abs(Vector3.Dot(d, fW)) > R) continue;
                            palmD.Add(Vector3.Dot(d, nW));
                        }
                        palmD.Sort();
                        palm = palmD.Count > 0 ? palmD[(int)(0.9f * (palmD.Count - 1))] : 0f;
                        cAt = c;
                        o = Depth(c, false, float.MaxValue);
                        if (o == float.MinValue) o = palm + R;
                        at = c + nW * o;
                        // the hand's height on the can (the fingers curl square to the axis, so the rest pose shows it): the index
                        // finger's upper edge at IndexTop, the little finger kept above the bottom and the index under the lid
                        float top = float.MinValue, bot = float.MaxValue;
                        for (int sg = 0; sg < 3; sg++) foreach (var k in segV[sg]) top = Mathf.Max(top, Vector3.Dot(w[k] - at, ax));
                        for (int sg = 9; sg < 12; sg++) foreach (var k in segV[sg]) bot = Mathf.Min(bot, Vector3.Dot(w[k] - at, ax));
                        if (top > float.MinValue)
                        {
                            float sh = top - IndexTop * H;
                            if (bot < float.MaxValue) sh = Mathf.Min(sh, bot - 0.03f * H);
                            sh = Mathf.Max(sh, top - CylTop * H);
                            c += ax * sh;
                            cAt = c;
                            o = Depth(c, false, float.MaxValue);
                            if (o == float.MinValue) o = palm + R;
                            at = c + nW * o;
                        }
                        Frame();
                    }

                    // a muscle value found by bisection between `open` (allowed) and `closed`, as far toward closed as ok() allows
                    float Furthest(string m, float open, float closed, System.Func<bool> ok)
                    {
                        muscles[m] = open; PoseHand();
                        if (!ok()) return open;
                        muscles[m] = closed; PoseHand();
                        if (ok()) return closed;
                        float a = open, b = closed;
                        for (int it = 0; it < 6; it++) { float mid = (a + b) * 0.5f; muscles[m] = mid; PoseHand(); if (ok()) a = mid; else b = mid; }
                        muscles[m] = a; PoseHand();
                        return a;
                    }
                    // the most open muscle value of joint j that still leaves the bones bent (>= 0 deg)
                    float Straight(int f, int j)
                    {
                        string m = Mus(f, j);
                        float keep = muscles[m];
                        float v = Furthest(m, 0f, 1f, () => Flex(f, j) >= 0f);
                        muscles[m] = keep; PoseHand();
                        return v;
                    }

                    // the bend each joint needs to lay its two segments round a can of radius R + half a finger (a polygon round
                    // the circle): about (half of each neighbouring segment) / radius
                    float SegLen(int f, int j)
                    {
                        var a = segBone[f * 3 + j]; if (a == null) return 0f;
                        // a fingertip without skin of its own (Kipfel): the middle segment's skin runs to the tip
                        if (j == 1 && segV[f * 3 + 2].Count == 0 && tipK[f * 3 + 1] >= 0) return (w[tipK[f * 3 + 1]] - a.position).magnitude;
                        if (j < 2 && segBone[f * 3 + j + 1] != null) return (segBone[f * 3 + j + 1].position - a.position).magnitude;
                        return Tip(f) >= 0 ? (w[Tip(f)] - a.position).magnitude : 0f;
                    }
                    float Ideal(int f, int j) => Mathf.Rad2Deg * (0.5f * (SegLen(f, j - 1) + SegLen(f, j))) / (R + 6f * mm);
                    // contact, not reach: each segment lying on the can counts; the fingertip's angle round it only when the tip
                    // touches; middle/tip bends near what the can's curve asks for (9-44)
                    float FingerScore(int f, out string why)
                    {
                        float gap = 0f; int touch = 0;
                        for (int j = 0; j < 3; j++)
                            if (padV[f * 3 + j].Count > 0) { float g = Pad(f * 3 + j); gap += Mathf.Clamp(g / mm, 0f, 10f); if (g <= 1f * mm) touch++; }
                        bool tipSkin = padV[f * 3 + 2].Count > 0;
                        bool tipOn = tipSkin ? Pad(f * 3 + 2) <= 1f * mm : Pad(f * 3 + 1) <= 1f * mm;
                        float tip = TipAround(f);
                        float pip = Flex(f, 1), dip = tipSkin ? Flex(f, 2) : 0f, mcp = Flex(f, 0);
                        float i1 = Ideal(f, 1), i2 = tipSkin ? Ideal(f, 2) : 0f;
                        why = $"{tip:0} deg, flex {mcp:0}/{pip:0}/{(tipSkin ? dip.ToString("0") : "-")} (curve asks ~{i1:0}/{(tipSkin ? i2.ToString("0") : "-")}), touching {touch}";
                        // a fingertip without skin has no tip joint to shape: the middle bend alone follows the curve, and reaching
                        // round the can counts half (round 4 Kipfel: index middle 71 deg for a curve asking 41 — a claw)
                        float shape = tipSkin ? (pip - i1) * (pip - i1) + (dip - i2) * (dip - i2) : 2f * (pip - i1) * (pip - i1);
                        // how a hand shares the bend: the root at least ~3/4 of the middle, the tip at most ~4/5 of it; the little
                        // finger's root not less than the ring's (round 6 Lapwing: index 30/57/39, middle tip 49 over 36, little 15)
                        float share = Sq(Mathf.Max(0f, 0.75f * pip - 5f - mcp)) + (tipSkin ? Sq(Mathf.Max(0f, dip - 0.8f * pip - 5f)) : 0f)
                            + (f == 3 ? Sq(Mathf.Max(0f, Flex(2, 0) - 5f - mcp)) : 0f);
                        return 15f * touch + (tipOn ? (tipSkin ? 0.3f : 0.15f) * tip : 0f) - 0.02f * shape - 0.1f * share - gap - 0.5f * Mathf.Max(0f, -mcp);
                    }
                    float Sq(float x) => x * x;
                    // how far round the can a finger can reach: from its knuckle, its length laid round a cylinder of R + half a
                    // finger, less 15 deg; at most 110 (round 6 Kipfel: 110 for every finger pulled its short hand off the palm)
                    float Reach(int f)
                    {
                        float len = SegLen(f, 0) + SegLen(f, 1) + SegLen(f, 2);
                        return Mathf.Clamp(Around(segBone[f * 3].position) + Mathf.Rad2Deg * len / (R + 6f * mm) - 15f, 60f, 110f);
                    }
                    bool FingerOk(int f)
                    {
                        if (FingerMin(f) < -3f * mm) return false;
                        for (int j = 0; j < 3; j++) if (padV[f * 3 + j].Count > 0 && Pad(f * 3 + j) < -0.5f * mm) return false;
                        // no bent-back joints, and no L-shaped finger (middle bends at least ~0.8 x the root)
                        float m0f = Flex(f, 0), m1f = Flex(f, 1);
                        return m0f >= -2f && m1f >= -2f && (padV[f * 3 + 2].Count == 0 || Flex(f, 2) >= -2f) && m1f >= 0.8f * m0f - 5f;
                    }
                    void Hug(int f)
                    {
                        only = fingerK[f];
                        try { HugIn(f); } finally { only = null; }
                    }
                    void HugIn(int f)
                    {
                        string m0 = Mus(f, 0), m1 = Mus(f, 1), m2 = Mus(f, 2);
                        muscles[m0] = 0f; muscles[m1] = 0f; muscles[m2] = 0f; PoseHand();
                        float o1 = Straight(f, 1), o2 = Straight(f, 2);
                        float bestS = float.MinValue; float b0 = 0f, b1 = 0f, b2 = 0f;
                        int nBack = 0, nRoot = 0, nFail = 0; float rootSink = 0f;
                        // nothing passing: the least wrong pose tried, not the rest curl (which can sit through the can)
                        float leastV = float.MaxValue, l0 = 0f, l1 = 0f, l2 = 0f;
                        void Consider(float v0, float v1, float v2)
                        {
                            muscles[m0] = v0; muscles[m1] = v1; muscles[m2] = v2; PoseHand();
                            if (!FingerOk(f))
                            {
                                nFail++;
                                float v = Mathf.Max(0f, -FingerMin(f)) / mm + Mathf.Max(0f, -Flex(f, 0)) + Mathf.Max(0f, -Flex(f, 1));
                                if (v < leastV) { leastV = v; l0 = v0; l1 = v1; l2 = v2; }
                                return;
                            }
                            float sc = FingerScore(f, out _);
                            if (sc > bestS) { bestS = sc; b0 = v0; b1 = v1; b2 = v2; }
                        }
                        for (float v0 = 0.6f; v0 >= -1.001f; v0 -= 0.1f)
                        {
                            muscles[m0] = v0; muscles[m1] = o1; muscles[m2] = o2; PoseHand();
                            if (Flex(f, 0) < -2f) { nBack++; continue; }
                            // the root segment alone (the straightened middle/tip held out over the can used to reject every useful root
                            // bend, leaving only the L-shaped and the floating ones, 9-44)
                            float rootMin = Min(segV[f * 3]);
                            if (rootMin < -3f * mm || (padV[f * 3].Count > 0 && Pad(f * 3) < -0.5f * mm))
                            {
                                nRoot++; rootSink = Mathf.Max(rootSink, -Mathf.Min(rootMin, Pad(f * 3)));
                                float v = 50f + Mathf.Max(0f, -rootMin) / mm;
                                if (v < leastV) { leastV = v; l0 = v0; l1 = o1; l2 = o2; }
                                continue;
                            }
                            // middle and tip close together (tip ~0.7 of the middle's travel) until a pad is about to sink, then the
                            // tip alone until its pad touches
                            float lo = 0f, hi = 1f;
                            bool LinkedOk(float c)
                            {
                                muscles[m1] = Mathf.Lerp(o1, -1f, c); muscles[m2] = Mathf.Lerp(o2, -1f, 0.7f * c); PoseHand();
                                return FingerMin(f, 1) >= -3f * mm && (padV[f * 3 + 1].Count == 0 || Pad(f * 3 + 1) >= -0.5f * mm) && (padV[f * 3 + 2].Count == 0 || Pad(f * 3 + 2) >= -0.5f * mm);
                            }
                            if (LinkedOk(1f)) lo = 1f;
                            else if (LinkedOk(0f)) for (int it = 0; it < 6; it++) { float mid = (lo + hi) * 0.5f; if (LinkedOk(mid)) lo = mid; else hi = mid; }
                            float v1 = Mathf.Lerp(o1, -1f, lo), v2s = Mathf.Lerp(o2, -1f, 0.7f * lo);
                            muscles[m1] = v1; muscles[m2] = v2s; PoseHand();
                            // the tip no more than 12 deg past what the curve asks (round 5 Lapwing middle: 59 for 37, a hook)
                            // and no more than ~4/5 of the middle joint's bend
                            float v2 = padV[f * 3 + 2].Count > 0 ? Furthest(m2, v2s, -1f, () => FingerMin(f, 2) >= -3f * mm && Pad(f * 3 + 2) >= -0.5f * mm && Flex(f, 2) <= Mathf.Min(Ideal(f, 2) + 12f, 0.8f * Flex(f, 1) + 5f)) : v2s;
                            Consider(v0, v1, v2);
                        }
                        // the inspector's pose too, if it actually lies on the can (it floated 20-30 mm on Milfy/Kipfel, round 3)
                        muscles[m0] = inspector[m0]; muscles[m1] = inspector[m1]; muscles[m2] = inspector[m2]; PoseHand();
                        float inspGap = 0f;
                        for (int j = 0; j < 3; j++) if (padV[f * 3 + j].Count > 0) inspGap += Mathf.Max(0f, Pad(f * 3 + j)) / mm;
                        if (inspGap <= 10f) Consider(inspector[m0], inspector[m1], inspector[m2]);
                        if (bestS == float.MinValue) { if (leastV < float.MaxValue) { b0 = l0; b1 = l1; b2 = l2; } else { b0 = 0.6f; b1 = o1; b2 = o2; } }
                        if (bestS == float.MinValue && LogContact)
                            log.AppendLine($"contact grip {side}: {fn[f]} found no pose — root bent back {nBack}, root segment sunk {nRoot} (up to {rootSink / mm:0.0} mm), rejected after bending {nFail}");
                        muscles[m0] = b0; muscles[m1] = b1; muscles[m2] = b2; PoseHand();
                    }

                    // spread so the fingers run parallel: each finger's root segment toward the four fingers' mean direction against
                    // the can's axis (the mean itself is the tilt's to fix — round 6 pinned the middle finger's spread at -1 on all
                    // three trying to square every finger to the axis), little spread, from the rest curl, before they hug
                    void AlignAll()
                    {
                        only = new int[0];   // bones only
                        try
                        {
                            float Ang(int f) => Mathf.Asin(Mathf.Clamp(Vector3.Dot(SegDir(f * 3).normalized, ax), -1f, 1f)) * Mathf.Rad2Deg;
                            float mean = 0f;
                            for (int f = 0; f < 4; f++) mean += Ang(f) / 4f;
                            for (int f = 0; f < 4; f++)
                            {
                                string sp = Spr(f);
                                float best = float.MaxValue, bv = muscles[sp];
                                for (float v = -1f; v <= 1.001f; v += 0.125f)
                                {
                                    muscles[sp] = v; PoseHand();
                                    float a = Mathf.Abs(Ang(f) - mean) + 10f * Mathf.Max(0f, Mathf.Abs(v) - 0.5f) + 0.5f * Mathf.Abs(v);
                                    if (a < best) { best = a; bv = v; }
                                }
                                muscles[sp] = bv; PoseHand();
                            }
                        }
                        finally { only = null; }
                        PoseHand();
                    }
                    // the fingertip's angle round the can — only while it is on the side of the can (one that slid under the bottom
                    // read as wrapped all the way round, round 5 log)
                    float TipAround(int f)
                    {
                        int k = Tip(f); if (k < 0) return 0f;
                        float h = Vector3.Dot(w[k] - at, ax);
                        // on the side: the tip segment's pad near the can (not the nail end, which curls off it even when the pad
                        // lies on the can), the tip within the can's height
                        float pad = padV[f * 3 + 2].Count > 0 ? Pad(f * 3 + 2) : Pad(f * 3 + 1);
                        if (h < -5f * mm || h > H + 5f * mm || pad > 6f * mm) return 0f;
                        // fading out from 3 to 6 mm off (a cliff at 3 mm swung placements by ~4 points a finger, round 6)
                        return Mathf.Min(Around(w[k]), 180f) * Mathf.Clamp01((6f * mm - pad) / (3f * mm));
                    }

                    float TipGap(int a, int b)
                    {
                        float m = float.MaxValue;
                        foreach (var ka in segV[a * 3 + 2].Count > 0 ? segV[a * 3 + 2] : segV[a * 3 + 1])
                            foreach (var kb in segV[b * 3 + 2].Count > 0 ? segV[b * 3 + 2] : segV[b * 3 + 1]) m = Mathf.Min(m, (w[ka] - w[kb]).sqrMagnitude);
                        return Mathf.Sqrt(m);
                    }
                    void Gather(int f, int nb, float dir)
                    {
                        // the finger already lies on the can; it may slide round it, but stops if it sinks in further than it started,
                        // lifts off, or the gap to its neighbour stops closing (the spread's sign is the rig's: the other way is tried
                        // if the first step opens the gap)
                        float start = Mathf.Min(FingerMin(f), 0f);
                        float gap = TipGap(f, nb);
                        for (int i = 0; i < 12; i++)
                        {
                            if (gap <= 1.5f * mm) return;
                            float v0 = muscles[Spr(f)], v = Mathf.Clamp(v0 + dir * 0.1f, -1f, 1f);
                            if (v == v0) return;
                            muscles[Spr(f)] = v; PoseHand();
                            float now = FingerMin(f), g = TipGap(f, nb);
                            if (now < start - 0.4f * mm || now > Mathf.Max(start, 0f) + 2f * mm || g >= gap)
                            {
                                muscles[Spr(f)] = v0; PoseHand();
                                if (i == 0 && g >= gap && dir != 0f) { dir = -dir; continue; }
                                return;
                            }
                            gap = g;
                        }
                    }

                    // thumb band: angles -120..-70 deg (round the other way), height th +- 8 mm, on the can's surface
                    float thH = 0f;
                    float ThumbCost(out float dist, out float pen, out float cross)
                    {
                        dist = float.MaxValue;
                        foreach (var k in padV[14].Count > 0 ? padV[14] : padV[13])
                        {
                            var d = w[k] - at;
                            float h = Mathf.Clamp(Vector3.Dot(d, ax), thH - 8f * mm, thH + 8f * mm);
                            float th = Mathf.Clamp(Around(w[k]) > 180f ? Around(w[k]) - 360f : Around(w[k]), -120f, -70f) * Mathf.Deg2Rad;
                            var q = at + ax * h + (e1 * Mathf.Cos(th) + e2 * Mathf.Sin(th)) * R;
                            dist = Mathf.Min(dist, (w[k] - q).magnitude);
                        }
                        float root = Min(segV[12]), outer = Mathf.Min(Min(segV[13]), Min(segV[14])), pl = Min(palmV);
                        pen = Mathf.Max(0f, -root - 3f * mm) + Mathf.Max(0f, -outer - 1f * mm) + Mathf.Max(0f, -pl - 2.5f * mm);
                        cross = 90f;
                        int tk = Tip(4);
                        if (tk >= 0)
                        {
                            // the last segment, along the can's surface (pointing into the can is not crossing it)
                            var lastA = (tipK[14] >= 0 ? segBone[14] : segBone[13]).position;
                            var v = w[tk] - lastA;
                            var rad = lastA - at; rad -= ax * Vector3.Dot(rad, ax);
                            if (rad.sqrMagnitude > 1e-12f) { rad.Normalize(); v -= rad * Vector3.Dot(v, rad); }
                            if (v.sqrMagnitude > 1e-12f) cross = Vector3.Angle(v, ax);
                        }
                        if (cross > 90f) cross = 180f - cross;
                        float cost = dist / (6f * mm) + pen / mm;
                        // its last segment across the can's axis, continuously toward 72+ (round 4 only penalised under 55: every
                        // thumb sat at 60-64, pointing at the lid)
                        cost += Mathf.Max(0f, 72f - cross) / 6f;
                        // not over the lid's shoulder; the middle segment and the tip pad on the can
                        if (tk >= 0) cost += 2f * Mathf.Max(0f, Vector3.Dot(w[tk] - at, ax) - (CylTop * H - 3f * mm)) / mm;
                        cost += 0.3f * Mathf.Clamp(Pad(13) / mm, 0f, 10f) + 0.5f * Mathf.Clamp(Pad(14) / mm, 0f, 10f);
                        // its root's flesh near the can too (round 5 Milfy: 13.7 mm off, a straight thumb touching with the tip only)
                        // light: a thumb holding with its tip alone is a real grip too (the author, round 7) — at 0.3/mm this term pulled
                        // Kipfel's can toward the wrist and left its finger roots 18 mm off
                        cost += 0.1f * Mathf.Clamp((Pad(12) - 6f * mm) / mm, 0f, 10f);
                        return cost;
                    }
                    float thSp = 0f, thRt = 0f, thCc = 0f;
                    // a thumb hug: for each middle bend, the tip curled until its pad meets the can; the pair that leaves both pads
                    // nearest the can, the middle joint not past ~30 deg and the tip 15-30 (round 5: the old close stopped as soon as
                    // the tip touched — Milfy's thumb a straight bar). Returns how far it is from that
                    float ThumbClose()
                    {
                        string t1 = Mus(4, 1), t2 = Mus(4, 2);
                        muscles[tS] = thSp; muscles[Mus(4, 0)] = thRt;
                        float bestC = float.MaxValue, b1 = thCc, b2 = 0.8f * thCc;
                        for (float v1 = Mathf.Min(0.3f, thCc + 0.3f); v1 >= -1.001f; v1 -= 0.15f)
                        {
                            muscles[t1] = v1; muscles[t2] = 0.3f; PoseHand();
                            if (Min(segV[13]) < -1f * mm || IndexOverlap() > 0f) continue;
                            Furthest(t2, 0.3f, -1f, () => Pad(14) >= -0.5f * mm && Min(segV[14]) >= -1f * mm && IndexOverlap() <= 0f);
                            if (Min(segV[13]) < -1f * mm || Pad(14) < -0.5f * mm || IndexOverlap() > 0f) continue;
                            float f1 = ThumbFlex(1), f2 = ThumbFlex(2);
                            if (thumbOk && (f1 < thumbMin[1] || f2 < thumbMin[2])) continue;
                            float c = Mathf.Max(0f, Pad(13)) / mm + Mathf.Max(0f, Pad(14)) / mm
                                + (thumbOk ? 0.3f * Mathf.Max(0f, f1 - 30f) + 0.1f * (Mathf.Max(0f, 15f - f2) + Mathf.Max(0f, f2 - 30f)) : 0f);
                            if (c < bestC) { bestC = c; b1 = v1; b2 = muscles[t2]; }
                        }
                        muscles[t1] = b1; muscles[t2] = b2; PoseHand();
                        return bestC;
                    }
                    // the thumb's whole cost as it ends up (band, crossing, gaps, wrong joints)
                    float ThumbTotal() { float bad = ThumbBad(); return ThumbCost(out _, out _, out _) + (bad > 0f ? 100f + bad : 0f); }
                    // grid over spread (toward the palm) x root x middle bend; then the best few root/spread pairs are each hugged
                    // (root +-0.3 too) and judged as they end up — round 6 picked the root with the thumb held straight and only
                    // then curled the middle and tip, leaving its middle segment 7 mm off (Lapwing) and its root 13 mm (Milfy)
                    void Thumb()
                    {
                        only = fingerK[4];
                        try { ThumbIn(); } finally { only = null; }
                        PoseHand();
                    }
                    void ThumbIn()
                    {
                        string t0 = Mus(4, 0), t1 = Mus(4, 1), t2 = Mus(4, 2);
                        var cand = new List<(float c, float sp, float rt, float cc)>();
                        float bc = float.MaxValue, bsp = 0f, brt = 0f, bcc = 0f;
                        void Try(float sp, float rt, float cc)
                        {
                            muscles[tS] = sp; muscles[t0] = rt; muscles[t1] = cc; muscles[t2] = 0.8f * cc; PoseHand();
                            float cost = ThumbTotal();
                            cand.Add((cost, sp, rt, cc));
                            if (cost < bc) { bc = cost; bsp = sp; brt = rt; bcc = cc; }
                        }
                        for (float sp = ThumbSpreadMin; sp <= ThumbSpreadMax + 0.001f; sp += 0.25f)
                            for (float rt = -1f; rt <= 1.001f; rt += 0.5f)
                                foreach (var cc in new[] { 0f, -0.4f }) Try(sp, rt, cc);
                        Try(Mathf.Clamp(inspector[tS], ThumbSpreadMin, ThumbSpreadMax), Mathf.Clamp(inspector[t0], -1f, ThumbRootMax), Mathf.Clamp(inspector[t1], -1f, 0.3f));
                        for (float st = 0.25f; st > 0.06f; st *= 0.5f)
                            foreach (var dv in new[] { -st, st })
                            {
                                Try(Mathf.Clamp(bsp + dv, ThumbSpreadMin, ThumbSpreadMax), brt, bcc);
                                Try(bsp, Mathf.Clamp(brt + dv, -1f, ThumbRootMax), bcc);
                            }
                        // the best few distinct spread/root pairs, each hugged with its root as found and 0.3 either way
                        var picks = new List<(float sp, float rt, float cc)>();
                        foreach (var c in cand.OrderBy(x => x.c))
                        {
                            if (picks.Any(p => Mathf.Abs(p.sp - c.sp) < 0.2f && Mathf.Abs(p.rt - c.rt) < 0.2f)) continue;
                            picks.Add((c.sp, c.rt, c.cc));
                            if (picks.Count == 3) break;
                        }
                        float best = float.MaxValue; float fsp = bsp, frt = brt, fcc = bcc;
                        foreach (var p in picks)
                            foreach (var drt in new[] { 0f, -0.3f, 0.3f })
                            {
                                thSp = p.sp; thRt = Mathf.Clamp(p.rt + drt, -1f, ThumbRootMax); thCc = p.cc;
                                ThumbClose();
                                float c = ThumbTotal();
                                if (c < best) { best = c; fsp = thSp; frt = thRt; fcc = thCc; }
                            }
                        thSp = fsp; thRt = frt; thCc = fcc;
                        ThumbClose();
                    }
                    // the can in (or out) to what the posed hand allows — palm, finger roots, and the thumb's root as it now sits
                    // — then the fingers hug again and the thumb closes again
                    void Settle()
                    {
                        for (int it = 0; it < 2; it++)
                        {
                            float oN = Depth(cAt, true, o);
                            if (oN == float.MinValue || Mathf.Abs(oN - o) < 0.3f * mm) return;
                            o = oN; at = cAt + nW * o; Frame();
                            for (int f = 0; f < 4; f++) Hug(f);
                            PoseHand();
                            Gather(0, 1, 1f); Gather(2, 1, -1f); Gather(3, 2, -1f);
                            // the thumb searched again where the can now is (round 5 only re-closed it: a sunk thumb stayed sunk)
                            Thumb();
                        }
                    }

                    var alongs = ContactAlongs.ToList(); var tilts = ContactTilts.ToList();
                    var outs = ContactTiltOuts.ToList();
                    float bestScore = float.MaxValue;
                    Dictionary<string, float> bestM = null;
                    Vector3 bestAt = at, bestAx = ax; Quaternion bestRot = rotW; float bestPalm = 0f, bestO = 0f, bestAlong = 0f, bestTilt = 0f;
                    string bestWhy = "";
                    var tried = new List<string>();
                    var done = new HashSet<(float, float, float)>();
                    float bestOut = 0f;
                    void Evaluate(float al, float tl, float to)
                    {
                        if (!done.Add((al, tl, to))) return;
                        PlaceCan(al, tl, to);
                        foreach (var key in muscles.Keys.ToList()) muscles[key] = 0f;
                        muscles[tS] = 1f; muscles[Mus(4, 0)] = 1f;   // thumb out of the way while the fingers close
                        PoseHand();
                        AlignAll();
                        for (int f = 0; f < 4; f++) Hug(f);
                        PoseHand();
                        Gather(0, 1, 1f); Gather(2, 1, -1f); Gather(3, 2, -1f);
                        // the height again with the fingers as they now lie (the spread moves the index's edge: 83 % at rest came
                        // out 77-79 %, round 6); along the axis the can's side is the same, so the fingers stay as they are
                        {
                            float top = float.MinValue, bot = float.MaxValue;
                            for (int sg = 0; sg < 3; sg++) foreach (var k in segV[sg]) top = Mathf.Max(top, Vector3.Dot(w[k] - at, ax));
                            for (int sg = 9; sg < 12; sg++) foreach (var k in segV[sg]) bot = Mathf.Min(bot, Vector3.Dot(w[k] - at, ax));
                            float sh = top - IndexTop * H;
                            sh = Mathf.Min(sh, bot - 0.03f * H);
                            sh = Mathf.Max(sh, top - CylTop * H);
                            if (Mathf.Abs(sh) > 1f * mm) { at += ax * sh; cAt += ax * sh; }
                        }
                        // the thumb's height from the index knuckle (not the fingertips, which rise with the tilt), under the lid's
                        // shoulder
                        thH = Mathf.Clamp(Vector3.Dot(B("IndexProximal").position - at, ax) - 10f * mm, 0.35f * H, CylTop * H - 10f * mm);
                        Thumb();
                        float oPlaced = o;
                        Settle();
                        var tipA = new float[4];
                        var whyF = new string[4];
                        float wrapShort = 0f, rootGap = 0f, outside = 0f;
                        for (int f = 0; f < 4; f++)
                        {
                            FingerScore(f, out whyF[f]); tipA[f] = TipAround(f);
                            wrapShort += Mathf.Max(0f, Reach(f) - tipA[f]) / 30f;
                            rootGap += Mathf.Clamp(Pad(f * 3) / mm, 0f, 10f);
                            // past the can's bottom or lid (round 5 Kipfel: the little finger half under the bottom)
                            float ext = 0f;
                            for (int j = 0; j < 3; j++) foreach (var k in segV[f * 3 + j]) { float h = Vector3.Dot(w[k] - at, ax); ext = Mathf.Max(ext, Mathf.Max(-h, h - H)); }
                            outside += Mathf.Clamp(ext / mm, 0f, 15f);
                        }
                        float tBad = ThumbBad();
                        float tc = ThumbCost(out var tDist, out var tPen, out var tCross) + (tBad > 0f ? 5f + tBad : 0f);
                        float worst = 0f;
                        for (int k = 0; k < w.Length; k++) worst = Mathf.Max(worst, -SD(k));
                        float palmGap = Mathf.Clamp(Min(palmV) / mm, 0f, 10f);
                        // contact counts too: the fingers' roots and the palm on the can (round 4 Lapwing: only the tips touched)
                        float sc = wrapShort + tc + Mathf.Max(0f, worst - 3f * mm) / mm * 2f + 0.15f * rootGap + 0.3f * palmGap + 0.3f * outside
                            + Mathf.Abs(al - 0.9f) * 1f + 0.002f * (tl * tl + to * to);
                        float idxTop = float.MinValue;
                        for (int j = 0; j < 3; j++) foreach (var k in segV[j]) idxTop = Mathf.Max(idxTop, Vector3.Dot(w[k] - at, ax));
                        sc += 0.1f * Mathf.Max(0f, Mathf.Abs(idxTop - IndexTop * H) / mm - 3f);
                        string why = $"tips {string.Join("/", tipA.Select(x => x.ToString("0")))} deg, roots off {rootGap:0} mm, index top {idxTop / H * 100f:0} %, outside the can {outside:0} mm, thumb {tDist / mm:0} mm off its band (crosses the axis {tCross:0} deg, flex {ThumbFlex(0):0}/{ThumbFlex(1):0}/{ThumbFlex(2):0}, root {Pad(12) / mm:+0.0;-0.0} mm, wrong {tBad:0.0}), settled {(o - oPlaced) / mm:+0.0;-0.0} mm, deepest {worst / mm:0.0} mm, palm {Min(palmV) / mm:+0.0;-0.0}";
                        tried.Add($"{al * 100:0}%/{tl:0}/{to:0}deg {sc:0.00} ({why})");
                        if (sc < bestScore) { bestScore = sc; bestM = new Dictionary<string, float>(muscles); bestAt = at; bestAx = ax; bestRot = rotW; bestPalm = palm; bestO = o; bestAlong = al; bestTilt = tl; bestOut = to; bestWhy = why + " | " + string.Join(" · ", Enumerable.Range(0, 4).Select(f => fn[f] + " " + whyF[f])); }
                    }
                    // the left hand of the avatar whose right hand was just fitted: that placement only
                    // (unless it fits this hand clearly worse — Lapwing's left hand scored 7.1 on its right's 3.4, a fingertip 5 mm
                    // off: the rig is not mirror-exact)
                    bool reuse = left && lastAvatar == avatar;
                    if (reuse)
                    {
                        Evaluate(lastAlong, lastTilt, lastOut);
                        if (bestScore > lastScore + 1.5f)
                        {
                            log.AppendLine($"contact grip {side}: the right hand's placement scores {bestScore:0.00} here (right {lastScore:0.00}) — searching");
                            reuse = false;
                        }
                    }
                    if (!reuse)
                    {
                    // the grid, widened along the hand until the best is inside it (round 5 Lapwing stopped on the edge again at 98 %),
                    // then a finer look round the best
                    for (int round = 0; round < 4; round++)
                    {
                        foreach (var al in alongs.ToList())
                            foreach (var tl in tilts.ToList())
                                foreach (var to in outs) Evaluate(al, tl, to);
                        bool more = false;
                        if (bestAlong <= alongs.Min() + 0.001f && bestAlong - 0.08f >= 0.5f) { alongs.Add(bestAlong - 0.08f); more = true; }
                        if (bestAlong >= alongs.Max() - 0.001f && bestAlong + 0.08f <= 1.1f) { alongs.Add(bestAlong + 0.08f); more = true; }
                        if (!more) break;
                    }
                    {
                        float a0 = bestAlong, t0 = bestTilt, u0 = bestOut;
                        Evaluate(a0 - 0.04f, t0, u0); Evaluate(a0 + 0.04f, t0, u0);
                        if (t0 - 5f >= -10f) Evaluate(a0, t0 - 5f, u0);
                        if (t0 + 5f <= 30f) Evaluate(a0, t0 + 5f, u0);
                    }
                    if (!left) { lastAvatar = avatar; lastAlong = bestAlong; lastTilt = bestTilt; lastOut = bestOut; lastScore = bestScore; }
                    }
                    at = bestAt; ax = bestAx; rotW = bestRot; palm = bestPalm; o = bestO;
                    Frame();
                    foreach (var kv in bestM) muscles[kv.Key] = kv.Value;
                    PoseHand();

                    var gaps = new float[15];
                    for (int i = 0; i < 15; i++) gaps[i] = segV[i].Count > 0 ? Min(segV[i]) : float.MaxValue;
                    float deepest = 0f;
                    for (int k = 0; k < w.Length; k++) deepest = Mathf.Min(deepest, SD(k));
                    log.AppendLine($"contact grip {side} ({skin.name}): tried {string.Join(" | ", tried)}");
                    log.AppendLine($"contact grip {side}: chose {bestAlong * 100:0} % / tilt {bestTilt:0} deg / out {bestOut:0} deg — {bestWhy}; axis {o / mm:0.0} mm out of the hand bone (palm skin {palm / mm:0.0}), deepest skin {-deepest / mm:0.0} mm in");
                    log.AppendLine($"contact grip {side}: palm {Min(palmV) / mm:+0.0;-0.0} | " + string.Join(" | ", Enumerable.Range(0, 5).Select(f =>
                        fn[f] + " " + string.Join("/", Enumerable.Range(0, 3).Select(j => gaps[f * 3 + j] == float.MaxValue ? "-" : (gaps[f * 3 + j] / mm).ToString("+0.0;-0.0")))
                        + " (" + string.Join(", ", Enumerable.Range(0, 3).Select(j => muscles[Mus(f, j)].ToString("0.00"))) + "; spread " + muscles[f < 4 ? Spr(f) : tS].ToString("0.00")
                        + "; flex " + string.Join("/", Enumerable.Range(0, 3).Select(j => segBone[f * 3 + j] != null ? (f == 4 ? ThumbFlex(j) : Flex(f, j)).ToString("0") : "-")) + ")")));
                    // it has to be a grip: nothing deep in the can, every finger and the thumb's tip on it, the palm near it, no
                    // thumb joint bent back — else the fixed pose (a rig the search could not fit)
                    string bad = null;
                    if (-deepest > 4f * mm) bad = $"skin {-deepest / mm:0.0} mm into the can";
                    for (int f = 0; f < 4 && bad == null; f++)
                    {
                        float g = float.MaxValue;
                        for (int j = 0; j < 3; j++) if (padV[f * 3 + j].Count > 0) g = Mathf.Min(g, Pad(f * 3 + j));
                        if (g > 4f * mm) bad = $"{fn[f]} finger {(g == float.MaxValue ? "has no skin" : (g / mm).ToString("0.0") + " mm off the can")}";
                    }
                    float thumbTip = padV[14].Count > 0 ? Pad(14) : Pad(13);
                    if (bad == null && thumbTip > 8f * mm) bad = $"thumb {(thumbTip == float.MaxValue ? "has no skin" : (thumbTip / mm).ToString("0.0") + " mm off the can")}";
                    if (bad == null && Min(palmV) > 10f * mm) bad = $"palm {Min(palmV) / mm:0.0} mm off the can";
                    if (bad == null && ThumbBad() > 0f) bad = "thumb bent back or into the index finger";
                    if (bad != null)
                    {
                        log.AppendLine($"contact grip {side}: no grip found ({bad}) — the fixed pose instead");
                        return false;
                    }
                    var seatC = at + hand.rotation * (nL * can.gripOffset.x + fL * can.gripOffset.y) * s + ax * (can.gripOffset.z * s);
                    r.grip = new Pose(handPos0 + handRot0 * (inv0 * (seatC - hand.position)), handRot0 * (Quaternion.Inverse(hand.rotation) * rotW));
                    r.muscles = muscles;
                    r.ok = true;
                    return true;
                }

                // the can: along the hand, lid above the index knuckle, then along the palm normal (SetHumanPose can nudge the
                // wrist, so everything from the hand as it is now). From the palm outward: the first spot where no skin sinks in
                // (pushing out from wherever the can started overshot the thumb, 9-32); if there is none, the least-pressed spot
                // before the thumb starts closing in again (the palm itself may stay pressed a little: the author's own placement on
                // Milfy had it 2.9 mm in). Seated = the can's side within SeatGap of the palm's skin
                float outO = 0f, sinkAt = 0f;
                Vector3 seat = Vector3.zero, scanFrom = Vector3.zero, scanAx = Vector3.up, scanN = Vector3.forward;
                float palm90 = 0f;
                bool Place()
                {
                    var ax = hand.rotation * rotL * Vector3.up;
                    var nW = hand.rotation * nL;
                    var fW = hand.rotation * fL;
                    var c = hand.position + hand.rotation * posL0;
                    c += fW * (along * frame.length - Vector3.Dot(c - hand.position, fW));
                    float lid = Vector3.Dot(B("IndexProximal").position, ax) + lidK * H;
                    c += ax * (lid - (Vector3.Dot(c, ax) + H));
                    c -= nW * Vector3.Dot(c - hand.position, nW);   // on the hand bone's plane
                    scanFrom = c; scanAx = ax; scanN = nW;
                    // the palm's skin under the can: hand-bone vertices (not fingers, not thumb) inside the can's footprint
                    // (along its height, within its radius across the hand), how far out of the hand bone's plane
                    var palmD = new List<float>();
                    for (int k = 0; k < w.Length; k++)
                    {
                        if (bones[bw[handV[k]].boneIndex0] != hand) continue;
                        var d = w[k] - c;
                        float h = Vector3.Dot(d, ax);
                        if (h < 0f || h > H || Mathf.Abs(Vector3.Dot(d, fW)) > R) continue;
                        palmD.Add(Vector3.Dot(d, nW));
                    }
                    palmD.Sort();   // 90th percentile, not the max: one stray vertex (Milfy's max 23 vs 12) is not the palm
                    palm90 = palmD.Count > 0 ? palmD[(int)(0.9f * (palmD.Count - 1))] : 0f;
                    float MinGap(Vector3 at) { float mn = float.MaxValue; foreach (var p in w) mn = Mathf.Min(mn, CylSD(p, at, ax, R, H)); return mn; }
                    float found = float.NaN, leastO = 0f, leastSink = float.MaxValue;
                    for (float o = 0f; o <= OutMax * s + 1e-6f; o += OutStep * s)
                    {
                        float sink = Mathf.Max(0f, -MinGap(c + nW * o));
                        if (sink < leastSink) { leastSink = sink; leastO = o; }
                        if (sink <= MaxSink * s) { found = o; break; }
                        if (leastSink < 0.006f * s && sink > leastSink + 0.0005f * s) break;   // past the palm's hollow, toward the thumb (Kipfel: 1.5 mm in at 25 mm, clear only past 38)
                    }
                    outO = float.IsNaN(found) ? leastO : found;
                    sinkAt = Mathf.Max(0f, -MinGap(c + nW * outO));
                    seat = c + nW * outO;
                    return outO - R <= palm90 + SeatGap * s && sinkAt <= 0.004f * s;
                }
                PoseHand();
                // what is in the way with the can against the palm's skin (palm90 out of the bone's plane): skin vertices inside
                // the can there, by their bone, palm excluded — the build log's hint for which slider to move. (Checked at the
                // bone's plane instead, every avatar's palm is 10-15 mm "in the way": Milfy 12, Lapwing 15)
                string InWay()
                {
                    var inWay = Enumerable.Range(0, w.Length)
                        .Where(k => bones[bw[handV[k]].boneIndex0] != hand)
                        .Select(k => (bone: bones[bw[handV[k]].boneIndex0].name, d: CylSD(w[k], scanFrom + scanN * (palm90 + R), scanAx, R, H)))
                        .Where(x => x.d < 0f).GroupBy(x => x.bone)
                        .Select(g => $"{g.Key} {g.Count()} ({-g.Min(x => x.d) * 1000:0.0} mm)").ToList();
                    return $"grip fit {side}: palm skin {palm90 * 1000:0.0} mm out of the hand bone; with the can against it, in the way — "
                        + (inWay.Count > 0 ? string.Join(", ", inWay) : "nothing");
                }
                if (!Place())
                {
                    DcWarn.Add(log, DcWarn.Id.PoseNotFit, side, (outO * 1000f).ToString("0.0"));
                    log.AppendLine(InWay());
                    return r;   // ok = false: default grip, no hand pose
                }
                if (LogContact)
                {
                    log.AppendLine(InWay());
                    // each bone's closest skin to the can where it sits (+ gap, - into it)
                    var near = Enumerable.Range(0, w.Length).Where(k => bones[bw[handV[k]].boneIndex0] != hand)
                        .GroupBy(k => bones[bw[handV[k]].boneIndex0].name)
                        .Select(g => $"{g.Key} {g.Min(k => CylSD(w[k], seat, scanAx, R, H)) * 1000:+0.0;-0.0}").OrderBy(x => x);
                    log.AppendLine($"grip fit {side}: at the seat — " + string.Join(", ", near));
                }
                // the inspector's fine tuning moves the can from that seat (the seat itself is found without it — reseating after
                // the offsets jumped the can 20 mm on Milfy and the preview, which moves the built can, no longer matched)
                seat += hand.rotation * (nL * can.gripOffset.x + fL * can.gripOffset.y) * s + hand.rotation * rotL * Vector3.up * (can.gripOffset.z * s);
                var posL = inv0 * (seat - hand.position);
                r.grip = new Pose(handPos0 + handRot0 * posL, handRot0 * rotL);
                r.muscles = muscles;
                r.ok = true;
                log.AppendLine($"grip fit {side} ({skin.name}{(prof != null ? ", " + prof.name + " hand" + (IsDefaultPose(can) ? " and pose" : "") : "")}): can at {along * 100:0} % of the hand, lid {lidK * 100:0} % of its height above the index knuckle, axis {outO * 1000:0.0} mm out of the palm, skin in {sinkAt * 1000:0.0} mm");
                if (sinkAt > 0.004f * s) DcWarn.Add(log, DcWarn.Id.HandSinks, side, (sinkAt * 1000f).ToString("0.0"));
            }
            finally
            {
                hph.Dispose();
                Object.DestroyImmediate(baked);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    all[i].localPosition = saved[i].localPosition;
                    all[i].localRotation = saved[i].localRotation;
                    all[i].localScale = saved[i].localScale;
                }
            }
            return r;
        }
    }
}
