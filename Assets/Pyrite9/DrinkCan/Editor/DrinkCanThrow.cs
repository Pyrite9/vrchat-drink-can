// Throwing (F5, D2 = mesh particle): letting go of the can (Take out off) hides the held can and emits one mesh particle
// in its place — it inherits the hand's velocity, falls, bounces and rolls on world colliders, shrinks away after a while.
//   Every client simulates its own copy (no sync beyond Out).
//   The particle mesh is a low-poly copy of the generated can (VRChat ranks mesh particle polygons: <= 1000 Excellent,
//   the full can is ~22k): outer revolve rings picked by profile simplification, every 2nd vertex around, attributes
//   copied exactly from the source (label seam and UVs unchanged), the lid a flat fan on the panel edge (planar UVs ->
//   the centre is the rim average). No tab, no opening, no inside — the same mesh for a closed and an opened can.
//   A particle mesh must have exactly one submesh (Unity: "Particle system meshes will only work with exactly one sub
//   mesh"; the renderer's 2nd material slot is the trail), so metal and label are two emitters, Throw and ThrowLabel,
//   side by side with the same settings and the same fixed random seed, always simulated (not culled): they emit,
//   fly, bounce and roll identically — one can.
//   The emitters hang under HandPoint at the can's centre (the FX Out layer sets their emission rate): the particle system
//   knows the hand's velocity when the can is let go. Taking a new can out clears the old one by switching the emitters
//   off for a moment (no random draws are left to restart, 9-56; the earlier one frame at simulation speed 2000 would now
//   set off the resting copy, see Rest).
//   Coming to rest (9-58): the rolling particle is velocity-aligned, and near a stop the gravity of one frame (before the
//   floor takes it back) outweighs what is left sideways — the can flipped about its axis frame to frame, and at exactly 0
//   it snapped its heading to world X. So below KillSpeed the floor kills it and a Death sub-emitter (Rest, one per
//   emitter) puts a copy in its place: same position and roll (inherit rotation), no gravity (floor collision only), the parent's
//   velocity with its vertical part clamped off, rolling exactly (ω = v / r — no gravity step to correct for) and slowed
//   by a drag that grows (quadratic in speed, RestDrag): ~7 cm over ~0.5 s, then a creep that never reaches 0 (0.3 mm/s
//   at the end), so the velocity alignment keeps the heading until it shrinks away (9-61; until then it stopped dead).
//   The swap costs two frames without a can (Play mode: a particle is not drawn on the frame it dies, and a new
//   sub-emitted particle not on its first — not a bounds matter: a placeholder particle keeping them did not help) — seen
//   in game as a blink just before it stops; kept (the author, 9-62 / 9-64). Tried twice and dropped: an unseen third emitter
//   in lockstep dying a little earlier than the drawn two, to cover the gap. The floor contact's frame-to-frame jitter
//   makes the hand-over frame vary, so no blank frame needs 2-6 frames of both cans (up to 52 deg of roll apart) — in game
//   two cans at once looked worse than the blink (9-64). Collision quality Medium / Low, to calm the jitter, let the can
//   fall through the floor.
//   Custom vertex streams put the mesh's UV1 in TEXCOORD1 where lilToon reads its detail atlas (Main 2nd / Normal 2nd).
// Landing sound: the animator can't see the particle land. A VRC Raycast straight down from the hand keeps
//   DropPoint on the floor below it and writes the distance; on letting go the Drop layer picks a delay bucket from that
//   distance (free fall, √(2h/g)) and plays the drop sound at DropPoint. A real throw lands later / further — approximate.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;
using static Pyrite9.DrinkCan.DrinkCanConst;

namespace Pyrite9.DrinkCan
{
    static class DrinkCanThrow
    {
        public const float Lifetime = 8f;          // seconds the resting copy lies on the floor (last 5 % shrinks); also the rolling one's cap
        // m/s after a floor hit: below it the rolling can is replaced by its resting copy (9-58). Play mode: from ~0.2 down the
        // flip about the axis grew (-14 deg at 0.21, -60 at 0.03); 0.2 then -> 0.35 with the copy rolling on (9-61): the swap
        // comes before the flip (the copy has no gravity, so none of its own)
        const float KillSpeed = 0.35f;
        // the resting copy's drag x speed² (Unity's drag x particle velocity): RestDrag0 at once, RestDrag after RestDragRamp s —
        // gentle at first (~0.5 m/s² at 0.35), then holding it near 0 without reaching it (Play mode fit: v = v0 / (1 + k v0 t))
        const float RestDrag0 = 4f, RestDrag = 300f, RestDragRamp = 1f;
        // m: the rolling can's collision radius is this much bigger than the can — it rolled 2-3 mm into the floor (each frame's
        // gravity before the floor takes it back), now about ±1 mm (Play mode 30-120 fps, 9-63)
        const float RideLift = 0.002f;
        // m: the copy is put this much above where the rolling can died (it dies a frame's fall into the floor). Play mode, 45-120
        // fps: copy - last drawn height -0.5 mm mean (-3.6..+1.5; VR 72-120 fps -1.4..+1.5) — before, 1-5 mm lower. 3 mm: pops up
        const float CopyLift = 0.002f;
        public const float RayDistance = 6f;       // floor further than this below the hand: no drop sound
        // per hit: speed kept along the normal / lost overall. Dampen counts every frame the can touches the floor, so 0.35 stopped
        // it within ~0.7 s where it landed — in game it read as stuck in the floor, no rolling (the author 2026-10-23, 9-53). Play
        // mode: 0.05 rolls ~2 s, <= 0.003 never stops. Bounce 0: resting on the floor, 0.3 kept re-bouncing it (vertical speed
        // +-0.05 m/s every frame against 0.004 sideways), so the velocity alignment flipped up/down each frame and the can
        // twitched about its axis (the author 2026-10-23, 9-56); with 0 the speed left is sideways only and steady (Play mode)
        const float Bounce = 0f, Dampen = 0.03f;
        // a nudge on release, Kick m/s along the avatar's forward: a can let go from a still hand (menu drop) fell straight down
        // and lay dead; now it rolls off a little. The emitters copy the avatar root's rotation (VRC Rotation Constraint) and
        // emit along their +Z. Not random: the two emitters do NOT draw the same random numbers despite the same seed (Play
        // mode: a random world kick put metal and label 47 cm apart, a random roll phase 20-100 deg apart, 9-56)
        const float Kick = 0.7f;   // 0.4 left a menu drop under Rest (it slid instead of rolling, Play mode)
        // m/s: ω = v / r up to RollFull (was capped at 1). The speed rotation-by-speed sees includes the gravity a frame adds before
        // the floor takes it back (g·dt: 0.11 m/s at 90 fps, 0.33 at 30), so ω = sqrt(v² - Rest²) / r. Rest was 0.33 to keep a
        // resting can from turning in place (9-56) — it slid below 0.33; the rolling can no longer rests (KillSpeed), so 0.1
        const float Rest = 0.1f, RollFull = 6f;
        const float InheritHand = 1.2f;               // x the hand's velocity when let go
        static readonly int CollideWith = (1 << 0) | (1 << 11);   // Default, Environment (not players, not pickups)

        // Orientation: the particle mesh lies along +X and the renderer aligns the particle to its velocity (+Z = direction of
        // travel), so the can's axis is always across its path; speed only rolls it about that axis (rotation by speed X) —
        // so it flies and lands on its side, rests exactly on its radius and rolls instead of sliding. A random heading with
        // the axis along +Z (until 9-54) left the axis along the path about as often as across it: it slid on the floor
        // (the author 2026-10-23). Play mode: stopped, it stays lying across its last path. (A free tumble left it standing or
        // tilted ~1.6 cm into the floor.) The cost: it turns from the held pose to lying flat the moment it is let go.
        public struct Result
        {
            public float collisionRadius;   // world metres, the can lying on its side
            public float centreDrop;        // world metres from the grip point (can bottom) up to the can's centre
        }

        // null when the particle mesh can't be made (the throw is then left out)
        public static Result? Build(Transform avatarRoot, Transform handPoint, Mesh mesh, DrinkCanMeshBuilder.Dims dims, float canScale,
            Material metal, Material label, List<Object> made)
        {
            float parentScale = Mathf.Max(1e-4f, handPoint.lossyScale.x);
            float half = dims.H * 0.5f * 0.001f;
            var res = new Result { collisionRadius = dims.R * 0.001f * canScale, centreDrop = half * canScale };
            var low = LowPoly(mesh, half, canScale);
            if (low == null) return null;
            made.Add(low.Value.metal);
            made.Add(low.Value.label);
            Emitter(avatarRoot, handPoint, Throw, low.Value.metal, metal, res, parentScale, half * canScale);
            Emitter(avatarRoot, handPoint, ThrowLabel, low.Value.label, label, res, parentScale, half * canScale);
            return res;
        }

        const uint Seed = 0x50795239;             // same in both emitters -> identical particles

        // the landing-sound probe: DropRay follows the hand (position only, upright with the avatar), DropPoint is its hit
        // flick sensor: two short PhysBones on the hand (along the can's axis and across it — a bone does not bend for motion
        // along itself), no gravity, a stiff pull back: a sharp swing of the hand makes the tip lag, <param>_Angle rises. Every
        // client simulates it; only the owner acts on it (FX Flick layer, IsLocal)
        public const float FlickBone = 0.08f;   // m (world, at build)

        public static void BuildFlickSensor(Transform handPoint)
        {
            var sensor = new GameObject(FlickSensor).transform;
            sensor.SetParent(handPoint, false);
            float s = Mathf.Max(1e-4f, handPoint.lossyScale.x);
            Bone(sensor, "A", Vector3.up * (FlickBone / s), FlickA);
            Bone(sensor, "B", Vector3.forward * (FlickBone / s), FlickB);

            void Bone(Transform parent, string name, Vector3 tip, string param)
            {
                var root = new GameObject(name).transform;
                root.SetParent(parent, false);
                var end = new GameObject(name + "Tip").transform;
                end.SetParent(root, false);
                end.localPosition = tip;
                var pb = root.gameObject.AddComponent<VRCPhysBone>();
                pb.rootTransform = root;
                pb.integrationType = VRCPhysBoneBase.IntegrationType.Simplified;
                pb.pull = FlickPull;
                pb.spring = FlickSpring;
                pb.stiffness = 0f;
                pb.gravity = 0f;
                // all motion counts (as measured): World immobile 1 — meant to drop walking — never fired in game (2026-10-23)
                pb.immobileType = VRCPhysBoneBase.ImmobileType.AllMotion;
                pb.immobile = 0f;
                pb.allowCollision = VRCPhysBoneBase.AdvancedBool.False;
                pb.allowGrabbing = VRCPhysBoneBase.AdvancedBool.False;
                pb.allowPosing = VRCPhysBoneBase.AdvancedBool.False;
                pb.maxStretch = 0f;
                pb.maxSquish = 0f;
                pb.limitType = VRCPhysBoneBase.LimitType.Angle;   // no flipping over (pushed along itself it could turn 180)
                pb.maxAngleX = 90f;
                pb.radius = 0f;
                pb.parameter = param;
            }
        }
        // measured in Play mode (2026-10-23, 0.3 s strokes, sin speed profile, 142 fps): with pull 1 / spring 0.05 the larger
        // _Angle of the two is 0.006 at 0.3 m/s, <= 0.056 up to 1.5 m/s, 0.066-0.092 at 2.5, 0.087-0.116 at 3-4 m/s — smooth
        // in speed. Softer bones (pull 0.05-0.85) swung to 0.2-1.0 already at 0.5-1 m/s and were erratic
        // in game nothing a person does reached 0.05 at pull 1 (the author: "throw it on even a slight swing", 9-51) -> pull 0.85 /
        // spring 0.1: 0.3 m/s 0-0.03, 0.6 m/s 0.03-0.15, 1 m/s 0.1-0.19 (same Play mode measurement)
        public const float FlickPull = 0.85f, FlickSpring = 0.1f;

        public static AudioSource BuildDropProbe(Transform root, Transform handPoint, float gain, float near, float far)
        {
            var ray = new GameObject(DropRay).transform;
            ray.SetParent(root, false);
            ray.position = handPoint.position;
            var pc = ray.gameObject.AddComponent<VRCPositionConstraint>();
            pc.Sources.Add(new VRCConstraintSource(handPoint, 1f));
            pc.IsActive = true;
            pc.Locked = true;

            var point = new GameObject(DropPoint).transform;   // outside the ray's own hierarchy
            point.SetParent(root, false);
            point.position = handPoint.position;

            var rc = ray.gameObject.AddComponent<VRCRaycast>();
            rc.RootTransform = ray;
            rc.RaycastDirection = Vector3.down;
            rc.Distance = RayDistance;
            rc.ApplyTransformScale = false;
            rc.RaycastCollisionMode = VRCRaycast.CollisionMode.HitWorlds;
            rc.ResultTransform = point;
            rc.ApplyRotation = false;
            rc.BehaviorOnMiss = VRCRaycast.MissBehavior.DoNothing;
            rc.Parameter = Floor;

            var src = point.gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 1f;
            src.dopplerLevel = 0f;
            var spatial = point.gameObject.AddComponent<VRCSpatialAudioSource>();
            spatial.Gain = gain;
            spatial.Near = near;
            spatial.Far = far;
            spatial.EnableSpatialization = true;
            return src;
        }

        // free-fall delay buckets for the drop sound: (floor distance below the grip up to, delay). The ray starts at the
        // grip (can bottom); the particle starts at the can's centre and lands when its centre is one radius above the floor
        public static List<(float maxDistance, float delay)> DelayBuckets(Result r)
        {
            var edges = new[] { 0.3f, 0.45f, 0.6f, 0.8f, 1.0f, 1.25f, 1.5f, 1.8f, 2.2f, 2.7f, 3.3f, 4f, 5f, RayDistance };
            var list = new List<(float, float)>();
            float lo = 0f;
            foreach (var hi in edges)
            {
                float h = Mathf.Max(0f, (lo + hi) * 0.5f + r.centreDrop - r.collisionRadius);
                list.Add((hi, Mathf.Sqrt(2f * h / 9.81f)));
                lo = hi;
            }
            return list;
        }

        public const int Segments = 64;                       // the source mesh's revolve segments (DrinkCan.BuildMesh)
        const int Step = 2;                                   // every 2nd vertex around: 32-gon
        const float ProfileTolerance = 0.0006f;               // m (source units): profile simplification

        // metal and label as two single-submesh meshes (same vertices), centred on the can, lying along +X, world-size metres
        public static (Mesh metal, Mesh label)? LowPoly(Mesh src, float half, float scale)
        {
            var pos = src.vertices;
            var nrm = src.normals;
            var uv0 = new List<Vector2>(); src.GetUVs(0, uv0);
            var uv1 = new List<Vector2>(); src.GetUVs(1, uv1);
            var sub = new int[pos.Length];
            for (int s = 0; s < src.subMeshCount; s++) foreach (var i in src.GetTriangles(s)) sub[i] = s;
            int ringLen = Segments + 1;

            // the outer revolve run (bottom, body, neck, seam) comes first as rings of ringLen vertices (j = 0 at +X and again
            // at j = Segments); sections meet with a duplicated ring. It ends where the lid panel's edge ring starts.
            bool IsRing(int k) => k + Segments < pos.Length && (pos[k + Segments] - pos[k]).sqrMagnitude < 1e-12f
                && Mathf.Abs(pos[k].z) < 1e-6f && pos[k].x >= -1e-6f;
            var sections = new List<List<int>>();
            int k0 = 0;
            for (; IsRing(k0); k0 += ringLen)
            {
                bool dup = sections.Count > 0 && (pos[k0] - pos[sections[sections.Count - 1].Last()]).sqrMagnitude < 1e-12f
                    && (pos[k0 + Segments / 4] - pos[sections[sections.Count - 1].Last() + Segments / 4]).sqrMagnitude < 1e-12f;
                if (sections.Count == 0 || dup || sub[k0 + 1] != sub[sections[sections.Count - 1].Last() + 1]) sections.Add(new List<int>());
                sections[sections.Count - 1].Add(k0);
            }
            int lidRim = k0;   // AddLidPanel ring 0: Segments points on the panel edge, planar UVs
            if (sections.Count < 3 || lidRim + Segments > pos.Length) return null;   // not the generator's layout

            var v = new List<Vector3>(); var n = new List<Vector3>(); var t0 = new List<Vector2>(); var t1 = new List<Vector2>();
            var tris = new[] { new List<int>(), new List<int>() };
            var offset = new Vector3(0f, half, 0f);
            var lay = Quaternion.Euler(0f, 0f, -90f);   // can axis +Y -> +X (see Orientation)
            int Add(int i)
            {
                v.Add(lay * ((pos[i] - offset) * scale)); n.Add(lay * nrm[i]); t0.Add(uv0[i]); t1.Add(uv1[i]);
                return v.Count - 1;
            }
            void Tri(int s, int a, int b, int c)
            {
                var face = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (Vector3.Dot(face, n[a] + n[b] + n[c]) < 0f) { var x = b; b = c; c = x; }
                tris[s].Add(a); tris[s].Add(b); tris[s].Add(c);
            }

            foreach (var rings in sections)
            {
                int s = Mathf.Min(sub[rings[0] + 1], 1);
                var keep = Simplify(rings.Select(k => new Vector2(pos[k].x, pos[k].y)).ToList(), ProfileTolerance);
                int prev = -1;
                foreach (var ri in keep)
                {
                    int start = v.Count;
                    for (int j = 0; j <= Segments; j += Step) Add(rings[ri] + j);
                    if (prev >= 0)
                        for (int j = 0; j < Segments / Step; j++)
                        {
                            Tri(s, prev + j, prev + j + 1, start + j + 1);
                            Tri(s, prev + j, start + j + 1, start + j);
                        }
                    prev = start;
                }
            }

            // lid: fan from the panel edge to its centre
            int rim = v.Count;
            for (int j = 0; j < Segments; j += Step) Add(lidRim + j);
            int count = Segments / Step;
            var centre = Vector3.zero; var c0 = Vector2.zero; var c1 = Vector2.zero;
            for (int j = 0; j < count; j++) { centre += v[rim + j]; c0 += t0[rim + j]; c1 += t1[rim + j]; }
            v.Add(new Vector3(0f, 0f, centre.z / count)); n.Add(lay * Vector3.up); t0.Add(c0 / count); t1.Add(c1 / count);
            int mid = v.Count - 1;
            for (int j = 0; j < count; j++) Tri(0, mid, rim + j, rim + (j + 1) % count);

            Mesh Part(string name, List<int> t)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, t0); m.SetUVs(1, t1);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                m.RecalculateTangents();
                return m;
            }
            return (Part("DrinkCan Thrown Metal", tris[0]), Part("DrinkCan Thrown Label", tris[1]));
        }

        // Douglas–Peucker on a profile polyline: indices kept (first and last always)
        static List<int> Simplify(List<Vector2> p, float tol)
        {
            var keep = new SortedSet<int> { 0, p.Count - 1 };
            void Rec(int a, int b)
            {
                int best = -1; float bestD = tol;
                var ab = p[b] - p[a];
                for (int i = a + 1; i < b; i++)
                {
                    var ap = p[i] - p[a];
                    float d = ab.sqrMagnitude < 1e-14f ? ap.magnitude : Mathf.Abs(ab.x * ap.y - ab.y * ap.x) / ab.magnitude;
                    if (d > bestD) { bestD = d; best = i; }
                }
                if (best < 0) return;
                keep.Add(best);
                Rec(a, best);
                Rec(best, b);
            }
            if (p.Count > 2) Rec(0, p.Count - 1);
            return keep.ToList();
        }

        static void Emitter(Transform avatarRoot, Transform handPoint, string name, Mesh mesh, Material mat, Result res, float parentScale, float centre)
        {
            var go = new GameObject(name);
            go.transform.SetParent(handPoint, false);
            go.transform.localPosition = new Vector3(0f, centre / parentScale, 0f);   // HandPoint = can bottom, +Y = can axis
            var rc = go.AddComponent<VRCRotationConstraint>();   // facing with the avatar (the Kick's direction); position stays on the hand
            rc.Sources.Add(new VRCConstraintSource(avatarRoot, 1f));
            rc.RotationOffset = Vector3.zero;
            rc.IsActive = true;
            rc.Locked = true;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // a build in Play mode starts it at once: seed / duration refused
            ps.useAutoRandomSeed = false;
            ps.randomSeed = Seed;

            var main = ps.main;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;   // both emitters must step together
            main.simulationSpeed = 1f;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = Lifetime;
            main.startSpeed = Kick;   // along the shape's +Z = the avatar's forward
            main.startSize = 1f;
            main.startRotation3D = true;
            main.startRotationX = 0f;   // roll phase: fixed (random draws differ between the emitters, see Kick)
            main.startRotationY = 0f;   // heading: from the velocity (renderer alignment)
            main.startRotationZ = 0f;
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;   // the baked mesh is already world-size
            main.maxParticles = 1;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
            main.startColor = Color.white;

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;   // raised for a moment by the FX Out layer (Thrown states); maxParticles keeps it to one

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;   // a straight cone = along +Z only
            shape.angle = 0f;
            shape.radius = 0.0001f;
            shape.radiusThickness = 0f;

            var iv = ps.inheritVelocity;
            iv.enabled = true;
            iv.mode = ParticleSystemInheritVelocityMode.Initial;
            iv.curveMultiplier = InheritHand;

            var col = ps.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.High;
            col.collidesWith = CollideWith;
            col.enableDynamicColliders = true;
            col.bounce = Bounce;
            col.dampen = Dampen;
            col.lifetimeLoss = 0f;
            col.minKillSpeed = KillSpeed;   // -> the resting copy (see Rest)
            col.radiusScale = 2f * (res.collisionRadius + RideLift);   // collision radius = size x radiusScale / 2 (measured, 9-44)

            var rbs = ps.rotationBySpeed;
            rbs.enabled = true;
            rbs.separateAxes = true;
            // rolling without slipping: ω = sqrt(v² - Rest²) / r (see Rest), sampled with straight segments. Keyframe(t, v) alone
            // has flat tangents (an S-curve): the earlier linear 0..1 turned ~1/12 of v / r at 0.1-0.2 m/s and the can slid (9-56)
            var roll = new AnimationCurve();
            for (int k = 0; k <= 40; k++)
            {
                float sp = RollFull * k / 40f;
                roll.AddKey(sp / RollFull, Mathf.Sqrt(Mathf.Max(0f, sp * sp - Rest * Rest)) / RollFull);
            }
            for (int k = 0; k < roll.length; k++) { AnimationUtility.SetKeyLeftTangentMode(roll, k, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(roll, k, AnimationUtility.TangentMode.Linear); }
            rbs.x = new ParticleSystem.MinMaxCurve(RollFull / res.collisionRadius, roll);
            rbs.y = 0f;
            rbs.z = 0f;
            rbs.range = new Vector2(0f, RollFull);

            // no shrink here: it normally ends at KillSpeed and the resting copy shrinks away

            Renderer(go.GetComponent<ParticleSystemRenderer>(), mesh, mat);
            var se = ps.subEmitters;
            se.enabled = true;
            se.AddSubEmitter(RestCopy(go.transform, "Rest", mesh, mat, res.collisionRadius), ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritRotation);
            if (Application.isPlaying) ps.Play();   // restart after the Stop above (Play mode builds only; an upload is built stopped)
        }

        // the resting copy (see the header): spawned once by the rolling can's death. Not InheritLifetime — a particle killed
        // on a collision has 0 left, so the copy died at once (Play mode)
        static ParticleSystem RestCopy(Transform parent, string name, Mesh mesh, Material mat, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = Seed;

            var main = ps.main;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = Lifetime;
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.startRotation3D = true;
            main.startRotationX = 0f;   // + the parent's (InheritRotation)
            main.startRotationY = 0f;
            main.startRotationZ = 0f;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.maxParticles = 1;
            main.startColor = Color.white;

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });   // one per trigger

            var shape = ps.shape;   // a point CopyLift above the dying particle (the world-up of this emitter: it turns with the avatar)
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.00001f;
            shape.radiusThickness = 0f;
            shape.position = new Vector3(0f, CopyLift / Mathf.Max(1e-6f, go.transform.lossyScale.y), 0f);

            var iv = ps.inheritVelocity;   // a sub-emitter inherits the dying particle's velocity
            iv.enabled = true;
            iv.mode = ParticleSystemInheritVelocityMode.Initial;
            iv.curveMultiplier = 1f;

            // the inherited velocity carries that frame's gravity (pre-floor): its vertical part is clamped off, or the copy
            // sank into the floor. X / Z are not limited (a per-axis clamp would turn the heading); the drag slows it (RestDrag)
            var lv = ps.limitVelocityOverLifetime;
            lv.enabled = true;
            lv.separateAxes = true;
            lv.space = ParticleSystemSimulationSpace.World;
            lv.limitX = 100f;
            lv.limitY = 0f;
            lv.limitZ = 100f;
            lv.dampen = 1f;

            // the floor again (no gravity): the rolling can dies a frame's fall into the floor and the copy started 1-5 mm low —
            // seen in game as a little drop at the swap (9-63). Lifted by CopyLift, its first move (the inherited, still downward
            // velocity) mostly hits the floor and puts it back at its radius; one that starts inside the floor stays a little low
            // (no push-out). Also stops it at walls
            var col = ps.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.High;
            col.collidesWith = CollideWith;
            col.enableDynamicColliders = true;
            col.bounce = 0f;
            col.dampen = 0f;
            col.lifetimeLoss = 0f;
            col.minKillSpeed = 0f;
            col.radiusScale = 2f * radius;
            float ramp = RestDragRamp / Lifetime;
            var drag = new AnimationCurve(new Keyframe(0f, RestDrag0 / RestDrag), new Keyframe(ramp, 1f), new Keyframe(1f, 1f));
            for (int k = 0; k < drag.length; k++) { AnimationUtility.SetKeyLeftTangentMode(drag, k, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(drag, k, AnimationUtility.TangentMode.Linear); }
            lv.drag = new ParticleSystem.MinMaxCurve(RestDrag, drag);
            lv.multiplyDragByParticleSize = false;
            lv.multiplyDragByParticleVelocity = true;

            // rolls on exactly (no gravity here, so no g·dt to take out as in the parent's Rest)
            var rbs = ps.rotationBySpeed;
            rbs.enabled = true;
            rbs.separateAxes = true;
            rbs.x = new ParticleSystem.MinMaxCurve(RollFull / radius, new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f)));
            rbs.y = 0f;
            rbs.z = 0f;
            rbs.range = new Vector2(0f, RollFull);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.95f, 1f), new Keyframe(1f, 0f)));

            Renderer(go.GetComponent<ParticleSystemRenderer>(), mesh, mat);
            return ps;
        }

        static void Renderer(ParticleSystemRenderer r, Mesh mesh, Material mat)
        {
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = mesh;
            r.sharedMaterial = mat;
            // TEXCOORD0 = mesh UV0 (.xy) + filler (.zw), TEXCOORD1 = mesh UV1 — lilToon's uv1 (detail atlas)
            r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Tangent,
                ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomXY,
                ParticleSystemVertexStream.UV2,
            });
            r.alignment = ParticleSystemRenderSpace.Velocity;   // see Orientation
            r.enableGPUInstancing = false;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
        }
    }
}
