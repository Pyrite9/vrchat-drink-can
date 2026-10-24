// Measures the avatar (rest pose) and places the grip and mouth points for the can.
//   hand frame: f = toward the middle-finger root, a = little -> index (thumb side), n = palm normal (out of the palm)
//   size     : body scale s = geometric mean of (spawn hand length (wrist -> middle proximal) / 0.09 m) and
//              (eye height / 1.55 m) — stylised small hands (Kipfel: hand x0.33, eyes x0.54) get a can between the two (9-19);
//              can scale = s * sizeScale
//   grip     : one pose per hand (GripR, GripL — the menu can switch hands); can axis along a (top toward the thumb), opening toward the wrist (-f, it faces the lips when drinking),
//              against the palm: axis at 80 % of the hand length toward the fingers, one can-radius + 12 mm out of the palm,
//              lid 20 mm (x s) above the index knuckle so the fingers wrap the body and the thumb rests by the lid (9-17 C1;
//              the old centre-of-palm fit left half the can above the hand and the thumb sank into it)
//   lips     : found on the face mesh — the vertices the lip-sync visemes aa and oh move most (VRC Avatar Descriptor viseme mesh),
//              their centre pushed out to the front-most one. The view position is no guide: Kipfel's sits on the forehead, its
//              mouth 126 mm below it (Milfy 36 mm) — the old view-based mouth put the desktop drink at the brow (9-24).
//              No visemes -> view position + (down 65 mm, forward 55 mm) * s as before
//   mouth    : the lips + 4 mm; desktop drink: can tilted 100 deg toward the face, opening at the lips
//   sip zone : VR sip contact, a capsule from where the player's real mouth is (player space: view + (down 75, forward 55 mm) * eye
//              scale — the headset maps the real head to the view position) to just in front of the avatar's lips, so either works
using System.Text;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Pyrite9.DrinkCan
{
    static class DrinkCanFit
    {
        const float RefHandLength = 0.09f;
        const float RefEyeHeight = 1.55f;
        const float GripAlongHand = 0.80f;      // can axis, fraction of wrist -> middle knuckle
        const float LidAboveIndex = 0.020f;     // m at body scale 1

        public struct Result
        {
            public bool ok;
            public float bodyScale, canScale, eyeScale;
            public float handLengthR, handLengthL;
            public Transform handR, handL, headBone;
            public Pose gripR, gripL, mouthCan;
            public Vector3 mouth, lips, sipTop, sipBottom;
            public bool lipsFound;

            public Pose Grip(bool left) => left ? gripL : gripR;
        }

        internal struct Hand
        {
            public Transform bone, index;
            public Vector3 f, a, n;
            public float length;
        }

        public static Result Measure(Transform avatar, DrinkCan can, StringBuilder log)
        {
            var r = new Result();
            var anim = avatar.GetComponent<Animator>();
            var desc = avatar.GetComponent<VRCAvatarDescriptor>();
            if (anim == null || !anim.isHuman || desc == null)
            {
                DcWarn.Add(log, DcWarn.Id.NotHumanoid);
                return r;
            }
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            var right = HandFrame(avatar, anim, false);
            var left = HandFrame(avatar, anim, true);
            if (right.bone == null || left.bone == null || head == null)
            {
                DcWarn.Add(log, DcWarn.Id.BoneMissing);
                return r;
            }

            // one can size for both hands: measured on the spawn hand
            var spawn = can.hand == DrinkCanHand.Left ? left : right;
            var view = avatar.TransformPoint(desc.ViewPosition);
            float handScale = spawn.length / RefHandLength;
            float eyeScale = Vector3.Dot(view - avatar.position, avatar.up) / RefEyeHeight;
            r.bodyScale = Mathf.Clamp(eyeScale > 0.05f ? Mathf.Sqrt(handScale * eyeScale) : handScale, 0.2f, 5f);
            r.eyeScale = eyeScale > 0.05f ? Mathf.Clamp(eyeScale, 0.2f, 5f) : r.bodyScale;
            r.canScale = r.bodyScale * Mathf.Clamp(can.sizeScale, 0.5f, 2f);
            r.handR = right.bone;
            r.handL = left.bone;
            r.handLengthR = right.length;
            r.handLengthL = left.length;
            r.headBone = head;

            var dims = DrinkCanMeshBuilder.GetDims(can.Shape);
            r.gripR = GripPose(right, can, dims, r);
            r.gripL = GripPose(left, can, dims, r);

            var fwd = avatar.forward;
            r.lipsFound = FindLips(avatar, desc, out r.lips);
            if (!r.lipsFound) r.lips = view + (-avatar.up * 0.065f + fwd * 0.051f) * r.bodyScale;
            r.mouth = r.lips + fwd * 0.004f * r.bodyScale;
            var sipPlayer = view + (-avatar.up * 0.075f + fwd * 0.055f) * r.eyeScale;
            var sipAvatar = r.lips + fwd * 0.02f * r.eyeScale;
            // upper end / lower end of the zone, the lower end reaching 30 mm (player scale) below the lower of the two
            bool playerHigher = Vector3.Dot(sipPlayer - sipAvatar, avatar.up) >= 0f;
            r.sipTop = playerHigher ? sipPlayer : sipAvatar;
            r.sipBottom = (playerHigher ? sipAvatar : sipPlayer) - avatar.up * 0.03f * r.eyeScale;
            // drinking: top toward the face, opening (+X) down at the lower lip
            var mUp = (-fwd * Mathf.Sin(100f * Mathf.Deg2Rad) + avatar.up * Mathf.Cos(100f * Mathf.Deg2Rad)).normalized;
            var mOpen = Vector3.ProjectOnPlane(-avatar.up, mUp).normalized;
            var mRot = Quaternion.LookRotation(Vector3.Cross(mOpen, mUp), mUp);
            var openingLocal = new Vector3(9f * dims.lidK, dims.H, 0f) * 0.001f * r.canScale;
            r.mouthCan = new Pose(r.mouth - mRot * openingLocal + fwd * 0.004f * r.bodyScale, mRot);
            r.ok = true;
            log.AppendLine($"fit: hand R {right.length:0.000} m / L {left.length:0.000} m, spawn {(can.hand == DrinkCanHand.Left ? "L" : "R")}, hand x{handScale:0.00} / eyes x{eyeScale:0.00} -> body x{r.bodyScale:0.00}, can x{r.canScale:0.00}");
            if (r.lipsFound)
                log.AppendLine($"fit: lips (visemes) {Vector3.Dot(view - r.lips, avatar.up) * 1000:0} mm below / {Vector3.Dot(r.lips - view, fwd) * 1000:0} mm in front of the view position");
            else
                DcWarn.Add(log, DcWarn.Id.NoVisemes);
            if (Mathf.Abs(right.length - left.length) > 0.1f * Mathf.Max(right.length, left.length))
                DcWarn.Add(log, DcWarn.Id.HandsDiffer, right.length.ToString("0.000"), left.length.ToString("0.000"));
            return r;
        }

        // the lips from the viseme blend shapes aa (10) and oh (13): vertices moved by more than 30 % of the largest delta,
        // centre of them, pushed forward to the front-most one (the lip surface)
        static bool FindLips(Transform avatar, VRCAvatarDescriptor desc, out Vector3 lips)
        {
            lips = Vector3.zero;
            var smr = desc.VisemeSkinnedMesh;
            var shapes = desc.VisemeBlendShapes;
            if (desc.lipSync != VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape || smr == null || smr.sharedMesh == null || shapes == null || shapes.Length < 14)
                return false;
            var mesh = smr.sharedMesh;
            int n = mesh.vertexCount;
            var delta = new float[n];
            var dv = new Vector3[n];
            var dn = new Vector3[n];
            var dt = new Vector3[n];
            foreach (var name in new[] { shapes[10], shapes[13] })
            {
                int bi = string.IsNullOrEmpty(name) ? -1 : mesh.GetBlendShapeIndex(name);
                if (bi < 0) continue;
                mesh.GetBlendShapeFrameVertices(bi, mesh.GetBlendShapeFrameCount(bi) - 1, dv, dn, dt);
                for (int i = 0; i < n; i++) delta[i] = Mathf.Max(delta[i], dv[i].magnitude);
            }
            float max = 0f;
            for (int i = 0; i < n; i++) max = Mathf.Max(max, delta[i]);
            if (max <= 0f) return false;
            var saved = new float[mesh.blendShapeCount];
            for (int i = 0; i < saved.Length; i++) { saved[i] = smr.GetBlendShapeWeight(i); smr.SetBlendShapeWeight(i, 0f); }
            var baked = new Mesh();
            try
            {
                smr.BakeMesh(baked, true);   // with scale + the renderer's TRS (same as DrinkCanGripFit)
                var v = baked.vertices;
                var fwd = avatar.forward;
                Vector3 sum = Vector3.zero;
                int count = 0;
                float front = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    if (delta[i] <= 0.3f * max) continue;
                    var w = smr.transform.TransformPoint(v[i]);
                    sum += w;
                    count++;
                    front = Mathf.Max(front, Vector3.Dot(w, fwd));
                }
                if (count == 0) return false;
                var centre = sum / count;
                lips = centre + fwd * (front - Vector3.Dot(centre, fwd));
                return true;
            }
            finally
            {
                Object.DestroyImmediate(baked);
                for (int i = 0; i < saved.Length; i++) smr.SetBlendShapeWeight(i, saved[i]);
            }
        }

        // f = toward the middle-finger root, a = little -> index (thumb side), n = out of the palm (both hands)
        internal static Hand HandFrame(Transform avatar, Animator anim, bool left)
        {
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            var h = new Hand { bone = B(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand) };
            if (h.bone == null) return h;
            var mid = B(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            var idx = B(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            var lit = B(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            var lower = B(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = h.bone;
            Vector3 f = mid != null ? (mid.position - hand.position) : (hand.position - (lower != null ? lower.position : hand.position - avatar.right));
            h.length = mid != null ? f.magnitude : RefHandLength;
            f.Normalize();
            Vector3 a;
            if (idx != null && lit != null) a = (idx.position - lit.position).normalized;
            else a = avatar.forward;                    // T-pose fallback: index side is forward
            a = (a - Vector3.Dot(a, f) * f).normalized;
            var n = Vector3.Cross(f, a);
            if (left) n = -n;                           // palm normal points out of the palm for both hands
            h.f = f; h.a = a; h.n = n;
            h.index = idx;
            return h;
        }

        // the inspector's tilt (DrinkCan.gripTilt, degrees): the can leans in the hand about its middle — x the top toward the
        // fingertips, y out of the palm. Built from the hand frame only (the untilted can axis is h.a), so the preview can take
        // the built tilt off and put a new one on (DcHoldPreview.PlaceHold). Applied after the grip fit and its offsets
        internal static Pose Tilt(Pose p, Hand h, Vector2 tilt, float height, bool undo = false)
        {
            if (tilt == Vector2.zero || h.bone == null) return p;
            var t = Quaternion.AngleAxis(tilt.x, Vector3.Cross(h.a, h.f)) * Quaternion.AngleAxis(tilt.y, Vector3.Cross(h.a, h.n));
            if (undo) t = Quaternion.Inverse(t);
            var center = p.position + p.rotation * Vector3.up * (height * 0.5f);
            return new Pose(center + t * (p.position - center), t * p.rotation);
        }

        internal static float CanHeight(DrinkCan can, float canScale) => DrinkCanMeshBuilder.GetDims(can.Shape).H * 0.001f * canScale;

        // can axis along a (top toward the thumb), opening toward the wrist; offsets in hand space so they mirror per hand
        static Pose GripPose(Hand h, DrinkCan can, DrinkCanMeshBuilder.Dims dims, Result r)
        {
            float radius = dims.R * 0.001f * r.canScale, height = dims.H * 0.001f * r.canScale;
            var up = h.a;
            var open = Vector3.ProjectOnPlane(-h.f, up).normalized;
            var rot = Quaternion.LookRotation(Vector3.Cross(open, up), up);
            rot = Quaternion.AngleAxis(can.gripTwist, up) * rot;
            var palm = h.bone.position + h.f * (GripAlongHand * h.length);
            float bottom = h.index != null
                ? Vector3.Dot(h.index.position - palm, up) + LidAboveIndex * r.bodyScale - height
                : -height * 0.5f;
            var pos = palm + h.n * (radius + 0.012f * r.bodyScale) + up * bottom;
            pos += (h.n * can.gripOffset.x + h.f * can.gripOffset.y + h.a * can.gripOffset.z) * r.bodyScale;
            return new Pose(pos, rot);
        }
    }
}
