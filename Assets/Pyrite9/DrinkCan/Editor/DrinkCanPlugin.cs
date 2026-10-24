// Pyrite9 Drink Can — builds everything per avatar at build time (the prefab only carries the DrinkCan settings).
//   Resolving, before MA:
//     model    : generated mesh (size, label fit) -> Hold/CanModel (SkinnedMeshRenderer, bones Can/Tab/TearPanel/Liquid),
//                material copies (label texture, liquid colour), Hold scaled to the avatar's hand
//     anchors  : GripR / GripL (MA Bone Proxy to each hand, keep world pose), HeadPivot (Bone Proxy to the neck + VRC Rotation
//                Constraint from the head) with MouthPoint + MouthSender under it
//                HandPoint follows GripR or GripL (VRC Parent Constraint, 0 = R, 1 = L — weights from the FX Hand layer),
//                Hold follows HandPoint or MouthPoint (VRC Parent Constraint, 0 = hand, 1 = mouth — weights from the Drink layer)
//                (not under the head: the first-person head shrink would pull them in — 9-15 / 9-21)
//     contacts : TabTouch on the tab (index fingers, self only), LipTouch over the whole lid (mouth sender, self only)
//     sound    : Hold/Sound (AudioSource + VRC Spatial Audio Source), clips from Sounds/ by GUID, played by the FX layers
//     throw    : HandPoint/Throw (low-poly mesh particle emitter), DropRay (VRC Raycast down) -> DropPoint
//                (drop sound source) — DrinkCanThrow
//     hand     : grip fitted to the skin and a holding hand pose (DrinkCanGripFit) -> Gesture controller (finger mask, weight
//                raised only while that hand holds the can); off with DrinkCan.gripHandPose
//     fx/menu  : FX controller (DrinkCanAnimator) via MA Merge Animator, MA Parameters,
//                MA menu (Take out / Open / Drink / Switch hand, labels in the inspector's menu language)
//   The DrinkCan settings component is IEditorOnly and dropped by the SDK.
//   Log: Logs/Pyrite9_DrinkCan_build.txt when the project has a Logs folder. Last build per avatar: DcBuildRecord (Library).
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using static Pyrite9.DrinkCan.DrinkCanConst;

[assembly: ExportsPlugin(typeof(Pyrite9.DrinkCan.DrinkCanPlugin))]

namespace Pyrite9.DrinkCan
{
    public class DrinkCanPlugin : Plugin<DrinkCanPlugin>
    {
        public override string QualifiedName => "pyrite9.drinkcan";
        public override string DisplayName => "Pyrite9 Drink Can";

        protected override void Configure()
        {
            InPhase(BuildPhase.Resolving)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Build drink can", Build);
        }

        static void Build(BuildContext ctx)
        {
            var log = new StringBuilder();
            log.AppendLine($"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] Pyrite9 Drink Can {Version} — {ctx.AvatarRootObject.name}");
            var cans = ctx.AvatarRootTransform.GetComponentsInChildren<DrinkCan>(true);
            DcWarn.Begin();
            if (cans.Length > 1) DcWarn.Add(log, DcWarn.Id.ManyCans, cans.Length.ToString());
            var rec = new DcBuildRecord.Entry
            {
                avatar = ctx.AvatarRootObject.name,
                time = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                version = Version,
            };
            try
            {
                if (cans.Length > 0) BuildOne(ctx, cans[0], log, rec);
            }
            catch (System.Exception e)
            {
                log.AppendLine("EXCEPTION " + e);
                Debug.LogException(e);
                DcWarn.Add(null, DcWarn.Id.BuildFailed, e.Message);
            }
            foreach (var c in cans) Object.DestroyImmediate(c);
            if (Directory.Exists("Logs")) File.AppendAllText(LogPath, log.ToString());
            var warns = log.ToString().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("WARN")).ToList();
            foreach (var line in warns) Debug.LogWarning("[Pyrite9 Drink Can] " + line);
            if (cans.Length == 0) return;
            // English lines as logged, and the same warnings as keys for the inspector's language (DcWarn)
            var keys = DcWarn.Keys();
            rec.warnings.AddRange(keys.Select(k => DcWarn.Text(0, k)));
            rec.warnKeys.AddRange(keys);
            DcBuildRecord.Put(rec);
        }

        static void BuildOne(BuildContext ctx, DrinkCan can, StringBuilder log, DcBuildRecord.Entry rec)
        {
            var root = can.transform;
            foreach (var t in root.Cast<Transform>().Where(t => t.name == "_DrinkCanPreview").ToArray()) Object.DestroyImmediate(t.gameObject);
            rec.hand = (int)can.hand;
            rec.sips = can.sips;
            var fit = DrinkCanFit.Measure(ctx.AvatarRootTransform, can, log);
            if (!fit.ok) return;
            rec.fitted = true;
            rec.canScale = fit.canScale;
            rec.bodyScale = fit.bodyScale;
            rec.handLengthR = fit.handLengthR;
            rec.handLengthL = fit.handLengthL;
            bool spawnLeft = can.hand == DrinkCanHand.Left;
            int lang = (int)can.menuLanguage;
            if (can.metalMaterial == null || can.labelMaterial == null || can.liquidMaterial == null)
                DcWarn.Add(log, DcWarn.Id.NoMaterial);
            var made = new List<Object>();

            // model
            var shape = can.Shape;
            var mesh = can.BuildMesh();
            mesh.name = "DrinkCan";
            made.Add(mesh);
            var rig = DrinkCanMeshBuilder.GetRig(shape);
            var metal = Copy(can.metalMaterial, "DrinkCan Metal", made);
            var label = Copy(can.labelMaterial, "DrinkCan Label", made);
            if (label != null)
            {
                if (can.labelTexture != null) label.SetTexture("_MainTex", can.labelTexture);
                else PlainLabel(label, metal);   // no label: plain aluminium (the template's own texture is the default label, 9-65)
            }
            var liquid = Copy(can.liquidMaterial, "DrinkCan Liquid", made);
            // the colour from the inspector, the see-through from the template (translucent liquid)
            if (liquid != null) { var lc = can.liquidColor; lc.a = liquid.GetColor("_Color").a; liquid.SetColor("_Color", lc); }

            var hold = new GameObject(Hold).transform;
            hold.SetParent(root, false);
            var parentScale = Mathf.Max(1e-4f, root.lossyScale.x);
            hold.localScale = Vector3.one * (fit.canScale / parentScale);
            var smr = DrinkCanModel.Create(hold, mesh, rig, new[] { metal, label, liquid });
            var bones = smr.bones;
            SplitLiquid(smr, mesh, liquid, made);
            LevelLiquid(ctx.AvatarRootTransform, bones, shape, liquid, made, log);
            var liquidY = LiquidTable(shape, rig, can.sips, out var liquidA, liquid != null ? liquid.GetColor("_Color").a : LiquidClear);
            var foamRate = BuildFoam(can, hold, bones, shape, fit.canScale, liquid, metal, made, log, out var foamDome, out var foamLayer);

            // grip fitted to the skin (palm contact, lid above the thumb) + the hand pose that holds it (9-22 B)
            var gfR = DrinkCanGripFit.Fit(ctx.AvatarRootTransform, fit, can, false, log);
            var gfL = DrinkCanGripFit.Fit(ctx.AvatarRootTransform, fit, can, true, log);
            if (gfR.ok) fit.gripR = gfR.grip;
            if (gfL.ok) fit.gripL = gfL.grip;
            // the inspector's tilt, after the fit and the offsets (DrinkCanFit.Tilt)
            if (can.gripTilt != Vector2.zero)
            {
                var tAnim = ctx.AvatarRootTransform.GetComponent<Animator>();
                float canH = DrinkCanFit.CanHeight(can, fit.canScale);
                fit.gripR = DrinkCanFit.Tilt(fit.gripR, DrinkCanFit.HandFrame(ctx.AvatarRootTransform, tAnim, false), can.gripTilt, canH);
                fit.gripL = DrinkCanFit.Tilt(fit.gripL, DrinkCanFit.HandFrame(ctx.AvatarRootTransform, tAnim, true), can.gripTilt, canH);
            }

            // anchors
            var gripR = Anchor(root, GripR, fit.gripR, HumanBodyBones.RightHand);
            var gripL = Anchor(root, GripL, fit.gripL, HumanBodyBones.LeftHand);
            var spawnGrip = fit.Grip(spawnLeft);
            var handPoint = new GameObject(HandPoint).transform;
            handPoint.SetParent(root, false);
            handPoint.SetPositionAndRotation(spawnGrip.position, spawnGrip.rotation);
            var hc = handPoint.gameObject.AddComponent<VRCParentConstraint>();
            hc.Sources.Add(new VRCConstraintSource(gripR, spawnLeft ? 0f : 1f));
            hc.Sources.Add(new VRCConstraintSource(gripL, spawnLeft ? 1f : 0f));
            hc.IsActive = true;
            hc.Locked = true;
            // mouth points: NOT under the head bone — VRChat shrinks the head locally in first person and its children collapse
            // into it. A VRC Head Chop fixed that for Milfy but on Kipfel (root scale 1.12) the sip point ended up above the
            // head in game (VR test 2026-10-21). Instead: a pivot under the neck at the head joint that copies the head's
            // rotation (VRC Rotation Constraint — rotation is untouched by the shrink); the points hang under the pivot.
            var anim = ctx.AvatarRootTransform.GetComponent<Animator>();
            var neckBone = anim.GetBoneTransform(HumanBodyBones.Neck) != null ? HumanBodyBones.Neck
                : anim.GetBoneTransform(HumanBodyBones.UpperChest) != null ? HumanBodyBones.UpperChest : HumanBodyBones.Chest;
            var headPivot = Anchor(root, HeadPivot, new Pose(fit.headBone.position, fit.headBone.rotation), neckBone);
            var rc = headPivot.gameObject.AddComponent<VRCRotationConstraint>();
            rc.Sources.Add(new VRCConstraintSource(fit.headBone, 1f));
            rc.IsActive = true;
            rc.Locked = true;
            var mouth = Child(headPivot, MouthPoint, fit.mouthCan);
            // sip zone (DrinkCanFit): a capsule from the player's real mouth down past the avatar's lips, radius 60 mm (player scale),
            // turning with the head. Kipfel's view position sits on the forehead with the mouth 126 mm below it, so a zone built
            // from the view position alone was at the brow (VR / desktop test 2026-10-22, 9-24)
            float sipR = 0.06f * fit.eyeScale;
            var sipAxis = fit.sipTop - fit.sipBottom;
            float sipLen = sipAxis.magnitude;
            var axisUp = sipLen > 1e-5f ? sipAxis / sipLen : ctx.AvatarRootTransform.up;
            var senderGo = Child(headPivot, MouthSender, new Pose((fit.sipTop + fit.sipBottom) * 0.5f,
                Quaternion.LookRotation(Vector3.ProjectOnPlane(ctx.AvatarRootTransform.forward, axisUp).normalized, axisUp)));
            var sender = senderGo.gameObject.AddComponent<VRCContactSender>();
            sender.shapeType = ContactBase.ShapeType.Capsule;
            float senderScale = Mathf.Max(1e-4f, senderGo.lossyScale.x);   // world metres -> the sender's local scale (avatar root scale)
            sender.radius = sipR / senderScale;
            sender.height = (sipLen + 2f * sipR) / senderScale;            // capsule along its local Y (top -> bottom of the zone)
            sender.collisionTags = new List<string> { MouthTag };

            var pc = hold.gameObject.AddComponent<VRCParentConstraint>();
            pc.Sources.Add(new VRCConstraintSource(handPoint, 1f));
            pc.Sources.Add(new VRCConstraintSource(mouth, 0f));
            pc.IsActive = true;
            pc.Locked = true;
            hold.SetPositionAndRotation(spawnGrip.position, spawnGrip.rotation);
            hold.gameObject.SetActive(false);

            // sound: one source on the can, played by the FX layers (VRC Animator Play Audio — open for all, gulp for the owner)
            var openSound = LoadSound(OpenSoundGuid, log);
            var gulpSound = LoadSound(GulpSoundGuid, log);
            var soundGo = new GameObject(Sound);
            soundGo.transform.SetParent(hold, false);
            var src = soundGo.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 1f;
            src.dopplerLevel = 0f;
            var spatial = soundGo.AddComponent<VRCSpatialAudioSource>();
            spatial.Gain = SoundGain;
            spatial.Near = SoundNear;
            spatial.Far = SoundFar;
            spatial.EnableSpatialization = true;

            // throwing (F5): mesh particle emitters under HandPoint + the landing-sound probe (DrinkCanThrow)
            var thrown = DrinkCanThrow.Build(ctx.AvatarRootTransform, handPoint, mesh, DrinkCanMeshBuilder.GetDims(shape), fit.canScale, metal, label, made);
            AudioClip dropSound = null;
            List<(float, float)> dropDelays = null;
            if (thrown.HasValue)
            {
                dropSound = LoadSound(DropSoundGuid, log);
                DrinkCanThrow.BuildDropProbe(root, handPoint, SoundGain, SoundNear, SoundFar);
                dropDelays = DrinkCanThrow.DelayBuckets(thrown.Value);
                DrinkCanThrow.BuildFlickSensor(handPoint);
            }
            else DcWarn.Add(log, DcWarn.Id.NoThrowMesh);

            // contacts (receivers live inside the scaled model: sizes in can-local metres)
            var dims = DrinkCanMeshBuilder.GetDims(shape);
            var hole = DrinkCanLidLayout.TabHole.Aggregate(Vector2.zero, (sum, v) => sum + v) / DrinkCanLidLayout.TabHole.Length;
            var holeLocal = new Vector3((hole.x - DrinkCanLidLayout.TabHingeX) * dims.lidK, 1.2f, 0f) * 0.001f;   // finger ring, tab-bone space
            Receiver(bones[DrinkCanMeshBuilder.BoneTab], "TabTouch", holeLocal, 0.009f, TabTouch, "FingerIndexL", "FingerIndexR");
            var lidLocal = new Vector3(0f, dims.H + 2f, 0f) * 0.001f;   // whole lid, not just the opening
            Receiver(bones[DrinkCanMeshBuilder.BoneCan], "LipTouch", lidLocal, (dims.R + 5f) * 0.001f, LipTouch, MouthTag);

            // FX, parameters, menu
            var fx = DrinkCanAnimator.Build(can.sips, spawnLeft, rig, liquidY, liquidA, liquid != null ? liquid.GetColor("_Color") : can.liquidColor, foamRate, foamDome, foamLayer ? can.foamColor : (Color?)null, openSound, gulpSound, thrown.HasValue, dropSound, dropDelays,
                Mathf.Clamp01(can.openVolume), Mathf.Clamp01(can.sipVolume), Mathf.Clamp01(can.dropVolume), made);
            var merge = root.gameObject.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = fx;
            merge.layerType = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Relative;
            merge.matchAvatarWriteDefaults = true;

            if (DrinkCanGripFit.HandPoseEnabled && can.gripHandPose && (gfR.ok || gfL.ok))
            {
                var gesture = root.gameObject.AddComponent<ModularAvatarMergeAnimator>();
                gesture.animator = DrinkCanAnimator.BuildGrip(spawnLeft, gfR.ok ? gfR.muscles : null, gfL.ok ? gfL.muscles : null, made);
                gesture.layerType = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.Gesture;
                gesture.pathMode = MergeAnimatorPathMode.Relative;
                gesture.matchAvatarWriteDefaults = true;
            }

            var ps = root.gameObject.AddComponent<ModularAvatarParameters>();
            ps.parameters.Add(Param(Out, ParameterSyncType.Bool, 0f));
            ps.parameters.Add(Param(Opened, ParameterSyncType.Bool, 0f));
            ps.parameters.Add(Param(Level, ParameterSyncType.Int, can.sips));
            ps.parameters.Add(Param(Drink, ParameterSyncType.Bool, 0f));
            ps.parameters.Add(Param(SwapHand, ParameterSyncType.Bool, 0f));
            ps.parameters.Add(Param(OpenBtn, ParameterSyncType.Bool, 0f, localOnly: true));
            ps.parameters.Add(Param(TabTouch, ParameterSyncType.NotSynced, 0f));
            ps.parameters.Add(Param(LipTouch, ParameterSyncType.NotSynced, 0f));
            ps.parameters.Add(Param(FloorHit, ParameterSyncType.NotSynced, 0f));
            ps.parameters.Add(Param(FloorDistance, ParameterSyncType.NotSynced, 0f));
            var tiltP = Param(Tilt, ParameterSyncType.NotSynced, 0f);
            tiltP.isPrefix = true;
            tiltP.hasExplicitDefaultValue = false;
            ps.parameters.Add(tiltP);
            if (thrown.HasValue)
                foreach (var pb in new[] { FlickA, FlickB })
                {
                    var pc2 = Param(pb, ParameterSyncType.NotSynced, 0f);
                    pc2.isPrefix = true;   // the PhysBone's own <name>_Angle / _Stretch / ...
                    pc2.hasExplicitDefaultValue = false;
                    ps.parameters.Add(pc2);
                }

            BuildMenu(root, lang, log);

            foreach (var o in made.Where(o => o != null && !EditorUtility.IsPersistent(o))) ctx.AssetSaver.SaveAsset(o);
            log.AppendLine($"built: can {shape.diameterMm:0.#} x {shape.heightMm:0.#} mm, sips {can.sips}, spawn hand {(spawnLeft ? "L" : "R")}, menu {can.menuLanguage}, sound open {Mathf.Clamp01(can.openVolume) * 100f:0} % / sip {Mathf.Clamp01(can.sipVolume) * 100f:0} % / landing {Mathf.Clamp01(can.dropVolume) * 100f:0} %, mesh {mesh.vertexCount} verts / {mesh.triangles.Length / 3} tris, {made.Count} assets");
        }

        // can sounds: +10 dB (x3.2, VRChat's default and the avatar maximum) — at 0 dB they were far too quiet in game
        // (the author 2026-10-23: "three times louder"). Heard by everyone (opening, landing): full volume to 2 m, fading out by 5 m; 10 m
        // carried them across the whole room (the author 2026-10-24, 9-69 → 4 m, 9-70 → 2-5 m). The gulp plays on the owner's
        // client only (DrinkCanAnimator.LevelLayer)
        const float SoundGain = 10f, SoundNear = 2f, SoundFar = 5f;

        // the liquid stays level and shows only through the opening (2026-10-23, 9-49 / 9-50):
        //   - LiquidLevel: a pivot at the middle of the can's inside that copies the avatar root's rotation (VRC Rotation
        //     Constraint; the root stands upright); the liquid bone hangs under it and the FX Level layer sets its height along
        //     world up from the volume table (LiquidTable) by the can's tilt — the surface, a disc wider than the can, stays level
        //   - TiltSensor: a short PhysBone hanging from the can's middle under gravity (pull 0): its _Angle is the can's tilt
        //   - LiquidMask: a disc just under the lid that writes stencil LiquidStencil where nothing is in front of it (= through
        //     the opening); the liquid material (lilToon transparent) draws only there, so the wide disc never shows outside
        //   - Inside: the inner wall and bottom (inward faces), dark and opaque, drawn only through the opening (stencil Equal,
        //     queue 2002 — after the mask, before the liquid). The can had no inside: through a submerged opening the look ran
        //     through liquid into nothing and showed what was behind the can (9-55). A liquid-coloured disc under the opening
        //     (LiquidCap, 9-55) covered it but read as a flat sticker; a real can is dark inside (little light gets in) (9-59)
        public const int LiquidStencil = 77;

        static void LevelLiquid(Transform avatarRoot, Transform[] bones, DrinkCanShape shape, Material liquid, List<Object> made, StringBuilder log)
        {
            var d = DrinkCanMeshBuilder.GetDims(shape);
            const float mm = 0.001f;
            var canBone = bones[DrinkCanMeshBuilder.BoneCan];
            var pivot = new GameObject(LiquidLevel).transform;
            pivot.SetParent(canBone, false);
            pivot.localPosition = new Vector3(0f, InsideMiddle(d) * mm, 0f);
            bones[DrinkCanMeshBuilder.BoneLiquid].SetParent(pivot, true);
            var rc = pivot.gameObject.AddComponent<VRCRotationConstraint>();
            rc.Sources.Add(new VRCConstraintSource(avatarRoot, 1f));
            rc.RotationOffset = Vector3.zero;
            rc.IsActive = true;
            rc.Locked = true;

            var sensor = new GameObject(TiltSensor).transform;
            sensor.SetParent(canBone, false);
            sensor.localPosition = pivot.localPosition;
            var tip = new GameObject(TiltSensor + "Tip").transform;
            tip.SetParent(sensor, false);
            tip.localPosition = new Vector3(0f, -0.03f, 0f);   // down the can's axis (can-local metres)
            var pb = sensor.gameObject.AddComponent<VRCPhysBone>();
            pb.rootTransform = sensor;
            pb.integrationType = VRCPhysBoneBase.IntegrationType.Simplified;
            pb.pull = 0f;            // hangs where gravity puts it
            pb.spring = 0.2f;        // a little slosh
            pb.stiffness = 0f;
            pb.gravity = 1f;
            pb.gravityFalloff = 0f;
            pb.immobileType = VRCPhysBoneBase.ImmobileType.AllMotion;
            pb.immobile = 0.3f;
            pb.allowCollision = VRCPhysBoneBase.AdvancedBool.False;
            pb.allowGrabbing = VRCPhysBoneBase.AdvancedBool.False;
            pb.allowPosing = VRCPhysBoneBase.AdvancedBool.False;
            pb.maxStretch = 0f;
            pb.limitType = VRCPhysBoneBase.LimitType.None;
            pb.radius = 0f;
            pb.parameter = Tilt;

            var maskMat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(LiquidMaskMaterialGuid));
            if (maskMat == null) { DcWarn.Add(log, DcWarn.Id.NoMaskMaterial); return; }
            var disc = new Mesh { name = "DrinkCan LiquidMask" };
            made.Add(disc);
            const int n = 32;
            var v = new Vector3[n + 1];
            var tri = new int[n * 3];
            float y = (d.csY - 0.5f) * mm, r = d.panelR * mm;   // under the lid panel, over the fullest liquid
            v[0] = new Vector3(0f, y, 0f);
            for (int i = 0; i < n; i++)
            {
                float th = i / (float)n * Mathf.PI * 2f;
                v[i + 1] = new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r);
                tri[i * 3] = 0; tri[i * 3 + 1] = 1 + (i + 1) % n; tri[i * 3 + 2] = 1 + i;
            }
            disc.vertices = v;
            disc.triangles = tri;
            disc.RecalculateNormals();
            disc.RecalculateBounds();
            var go = new GameObject(LiquidMask);
            go.transform.SetParent(bones[DrinkCanMeshBuilder.BoneCan], false);
            go.AddComponent<MeshFilter>().sharedMesh = disc;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = maskMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            // the inside: the mask's material made visible — colour on, depth on, stencil Equal (only through the opening)
            var inMat = Object.Instantiate(maskMat);
            inMat.name = "DrinkCan Inside";
            inMat.SetFloat("_ColorMask", 15f);
            inMat.SetFloat("_ZWrite", 1f);
            inMat.SetFloat("_StencilComp", (float)UnityEngine.Rendering.CompareFunction.Equal);
            inMat.SetFloat("_StencilPass", (float)UnityEngine.Rendering.StencilOp.Keep);
            inMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            inMat.SetColor("_Color", InsideColor(liquid));
            inMat.renderQueue = 2002;
            made.Add(inMat);
            // in front of the generator's own inside (DrinkCanMeshBuilder: metal inner wall 0.3 mm in, following the neck; inner
            // bottom at domeD) — the first build sat exactly on it and shimmered in game (z-fighting, the author 2026-10-23, 9-60)
            var inMesh = InsideMesh(DrinkCanMeshBuilder.InteriorProfile(d, DrinkCanMeshBuilder.InteriorWall + InsideGapWall), d.domeD + InsideGapBottom, mm);
            made.Add(inMesh);
            var ins = new GameObject(Inside);
            ins.transform.SetParent(bones[DrinkCanMeshBuilder.BoneCan], false);
            ins.AddComponent<MeshFilter>().sharedMesh = inMesh;
            var ir = ins.AddComponent<MeshRenderer>();
            ir.sharedMaterial = inMat;
            ir.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ir.receiveShadows = false;
            ir.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            ir.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        // the liquid's colour, darkened: the opening reads as "a drink inside", dark enough not to look like a lid (the author chose it
        // over dark brown and black, 9-59). Follows the inspector's liquid colour
        public const float InsideShade = 0.25f;
        const float InsideGapWall = 1.5f, InsideGapBottom = 2f;   // mm in from the shell / up from the inner bottom (9-60)
        static Color InsideColor(Material liquid)
        {
            var c = liquid != null ? liquid.GetColor("_Color") : new Color(0.8f, 0.5f, 0.12f);
            return new Color(c.r * InsideShade, c.g * InsideShade, c.b * InsideShade, 1f);
        }

        // the inner wall revolved from `profile` (r, y mm, top down; cut at the bottom disc) + a bottom disc at y0, faces pointing
        // inward (seen from inside), can-local metres
        static Mesh InsideMesh(List<Vector2> profile, float y0, float mm)
        {
            const int n = 32;
            var prof = profile.Where(p => p.y > y0).ToList();
            prof.Add(new Vector2(prof[prof.Count - 1].x, y0));
            float r = prof[prof.Count - 1].x;
            var v = new List<Vector3>(); var nrm = new List<Vector3>(); var tri = new List<int>();
            int rows = prof.Count;
            for (int i = 0; i <= n; i++)
            {
                float th = i / (float)n * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th));
                foreach (var p in prof) { v.Add(new Vector3(dir.x * p.x, p.y, dir.z * p.x) * mm); nrm.Add(-dir); }
            }
            for (int i = 0; i < n; i++)
                for (int k = 0; k < rows - 1; k++)
                {
                    int a = i * rows + k, b = a + 1, c = a + rows, e = c + 1;
                    tri.Add(a); tri.Add(b); tri.Add(c);
                    tri.Add(c); tri.Add(b); tri.Add(e);
                }
            int centre = v.Count;
            v.Add(new Vector3(0f, y0, 0f) * mm); nrm.Add(Vector3.up);
            int ring = v.Count;
            for (int i = 0; i < n; i++)
            {
                float th = i / (float)n * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(th) * r, y0, Mathf.Sin(th) * r) * mm); nrm.Add(Vector3.up);
            }
            for (int i = 0; i < n; i++) { tri.Add(centre); tri.Add(ring + (i + 1) % n); tri.Add(ring + i); }
            var m = new Mesh { name = "DrinkCan Inside" };
            m.SetVertices(v); m.SetNormals(nrm); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            // the winding above is checked against the normals here, so a wrong guess can't cull the wrong side
            var tris = m.triangles;
            for (int k = 0; k < tris.Length; k += 3)
            {
                var p0 = v[tris[k]]; var p1 = v[tris[k + 1]]; var p2 = v[tris[k + 2]];
                var f = Vector3.Cross(p1 - p0, p2 - p0);
                if (Vector3.Dot(f, nrm[tris[k]] + nrm[tris[k + 1]] + nrm[tris[k + 2]]) < 0f) { var x = tris[k + 1]; tris[k + 1] = tris[k + 2]; tris[k + 2] = x; }
            }
            m.triangles = tris;
            return m;
        }

        // the liquid on its own renderer (LiquidSurface, same bones), so the FX can animate its colour without touching the can's
        // materials; the can's renderer keeps metal + label (the liquid's vertices stay in its mesh, unused). The throw's low-poly
        // copy is made from the full mesh as before
        static void SplitLiquid(SkinnedMeshRenderer smr, Mesh mesh, Material liquid, List<Object> made)
        {
            int sub = DrinkCanMeshBuilder.SubmeshLiquid;
            var tris = mesh.GetTriangles(sub);
            var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; var uv2 = mesh.uv2; var bw = mesh.boneWeights;
            var map = new Dictionary<int, int>();
            var lv = new List<Vector3>(); var ln = new List<Vector3>(); var luv = new List<Vector2>(); var luv2 = new List<Vector2>(); var lbw = new List<BoneWeight>();
            var lt = new int[tris.Length];
            for (int k = 0; k < tris.Length; k++)
            {
                int i = tris[k];
                if (!map.TryGetValue(i, out var j))
                {
                    j = lv.Count; map[i] = j;
                    lv.Add(v[i]);
                    ln.Add(n.Length > i ? n[i] : Vector3.up);
                    luv.Add(uv.Length > i ? uv[i] : Vector2.zero);
                    luv2.Add(uv2.Length > i ? uv2[i] : Vector2.zero);
                    lbw.Add(bw[i]);
                }
                lt[k] = j;
            }
            var lm = new Mesh { name = "DrinkCan Liquid" };
            made.Add(lm);
            lm.SetVertices(lv); lm.SetNormals(ln); lm.SetUVs(0, luv); lm.SetUVs(1, luv2);
            lm.boneWeights = lbw.ToArray();
            lm.bindposes = mesh.bindposes;
            lm.SetTriangles(lt, 0);
            lm.RecalculateBounds();

            var body = Object.Instantiate(mesh);
            body.name = mesh.name;
            body.subMeshCount = sub;   // metal, label
            made.Add(body);
            smr.sharedMesh = body;
            smr.sharedMaterials = smr.sharedMaterials.Take(sub).ToArray();

            var go = new GameObject(LiquidSurface);
            go.transform.SetParent(smr.transform.parent, false);
            go.transform.localPosition = smr.transform.localPosition;
            go.transform.localRotation = smr.transform.localRotation;
            go.transform.localScale = smr.transform.localScale;
            var ls = go.AddComponent<SkinnedMeshRenderer>();
            ls.sharedMesh = lm;
            ls.bones = smr.bones;
            ls.rootBone = smr.rootBone;
            ls.sharedMaterials = new[] { liquid };
            ls.localBounds = smr.localBounds;
            ls.updateWhenOffscreen = false;
        }

        // foam (9-65 / 9-66): Head = a foam layer on the drink (the liquid disc again, FoamLift above it, seen through the opening
        // like the drink) + a dome of foam welling up out of the opening when it opens (FoamDome, scaled by the FX Open layer);
        // Bubbles = fizz flying out when it opens (the Foam emitter, under the can bone, local space, its emission rate driven by
        // the FX Open layer — returns that rate, 0 = no emitter); None = nothing. No bubble outlines in any texture: foam
        // particles of bubble-cell clumps read as unpleasant in game (the author 2026-10-24, 9-66).
        // The layer is opaque only at first: the FX Open layer thins it out after the dome (DrinkCanAnimator.FoamSettle) —
        // kept opaque it hid the drink's colour for good (the author 2026-10-25, 9-96); layer = the foam layer was made
        const float FoamLift = 1.5f;          // mm above the drink
        public const float FoamAlpha = 0.97f;
        const float FoamTiling = 6f;
        public const float FoamFizzRate = 120f;   // droplets a second while it fizzes (Opening 0.62 s -> Opened 0.4 s)

        static float BuildFoam(DrinkCan can, Transform hold, Transform[] bones, DrinkCanShape shape, float canScale, Material liquid, Material metal,
            List<Object> made, StringBuilder log, out bool dome, out bool layer)
        {
            dome = false;
            layer = false;
            if (can.foam == DrinkCanFoam.None) return 0f;
            const float mm = 0.001f;
            var d = DrinkCanMeshBuilder.GetDims(shape);
            if (can.foam == DrinkCanFoam.Head && liquid != null)
            {
                var surf = hold.Find(LiquidSurface) != null ? hold.Find(LiquidSurface).GetComponent<SkinnedMeshRenderer>() : null;
                if (surf != null)
                {
                    var fm = Object.Instantiate(surf.sharedMesh);   // can-local metres; the liquid bone is kept level, so +Y is up
                    fm.name = "DrinkCan Foam";
                    var v = fm.vertices;
                    for (int i = 0; i < v.Length; i++) v[i].y += FoamLift * mm;
                    fm.vertices = v;
                    fm.RecalculateBounds();
                    made.Add(fm);
                    var mat = Object.Instantiate(liquid);
                    mat.name = "DrinkCan Foam";
                    var c = can.foamColor; c.a = FoamAlpha;
                    mat.SetColor("_Color", c);
                    var tile = LoadTexture(FoamLayerGuid, log);
                    if (tile != null) { mat.SetTexture("_MainTex", tile); mat.SetTextureScale("_MainTex", Vector2.one * FoamTiling); }
                    mat.renderQueue = liquid.renderQueue + 1;
                    made.Add(mat);
                    var go = new GameObject(FoamSurface);
                    go.transform.SetParent(surf.transform.parent, false);
                    go.transform.localPosition = surf.transform.localPosition;
                    go.transform.localRotation = surf.transform.localRotation;
                    go.transform.localScale = surf.transform.localScale;
                    var r = go.AddComponent<SkinnedMeshRenderer>();
                    r.sharedMesh = fm;
                    r.bones = surf.bones;
                    r.rootBone = surf.rootBone;
                    r.sharedMaterials = new[] { mat };
                    r.localBounds = surf.localBounds;
                    r.updateWhenOffscreen = false;
                    layer = true;
                }
            }

            if (can.foam == DrinkCanFoam.Head)
            {
                dome = BuildFoamDome(can, bones, d, metal, made, log);
                return 0f;
            }

            var shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) { DcWarn.Add(log, DcWarn.Id.NoParticleShader); return 0f; }
            var pm = new Material(shader) { name = "DrinkCan Fizz Particle" };
            pm.SetFloat("_Mode", 2f);   // fade (alpha blend)
            pm.SetOverrideTag("RenderType", "Transparent");
            pm.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            pm.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            pm.SetInt("_ZWrite", 0);
            pm.EnableKeyword("_ALPHABLEND_ON");
            pm.SetColor("_Color", Color.white);
            var dot = LoadTexture(FoamDotGuid, log);
            if (dot != null) pm.SetTexture("_MainTex", dot);
            pm.renderQueue = 3000;
            made.Add(pm);

            var pgo = new GameObject(Foam);
            pgo.transform.SetParent(bones[DrinkCanMeshBuilder.BoneCan], false);
            var tc = DrinkCanLidLayout.TearCenter;
            pgo.transform.localPosition = new Vector3(tc.x * d.lidK, d.panelY, tc.y * d.lidK) * mm;   // the opening
            var ps = pgo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            float lossy = Mathf.Max(1e-6f, pgo.transform.lossyScale.x);
            float s = canScale;
            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;   // the droplets stay with the can
            main.scalingMode = ParticleSystemScalingMode.Shape;            // sizes / speeds below are world metres
            main.maxParticles = 80;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f * s, 0.3f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(0.0015f * s, 0.003f * s);
            main.gravityModifier = 0.3f;
            var fc = can.foamColor;
            main.startColor = new Color(Mathf.Lerp(fc.r, 1f, 0.5f), Mathf.Lerp(fc.g, 1f, 0.5f), Mathf.Lerp(fc.b, 1f, 0.5f), 0.9f);
            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;   // the FX Open layer raises it while the can opens
            var shp = ps.shape;
            shp.enabled = true;
            shp.shapeType = ParticleSystemShapeType.Cone;
            shp.angle = 25f;
            shp.radius = 0.004f * s / lossy;
            shp.rotation = new Vector3(-90f, 0f, 0f);   // along the can's axis, out of the opening
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)));
            var pr = pgo.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.sharedMaterial = pm;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            if (Application.isPlaying) ps.Play();   // a build in Play mode added it stopped (above)
            return FoamFizzRate;
        }

        // the beer head when it opens (9-66): one smooth dome of foam over the opening, hidden until the FX Open layer wells it
        // up and sinks it back (DrinkCanAnimator.DomeScale). Lit by a matcap (multiply) so it reads round whatever the world's
        // lights — with lilToon's own lighting alone it was a flat cream sticker
        const float DomeHalfX = 8.5f, DomeHalfZ = 13.5f, DomeHeight = 13.5f, DomeSink = 1f;   // mm at lid scale; the opening is ~15 x 24 mm

        static bool BuildFoamDome(DrinkCan can, Transform[] bones, DrinkCanMeshBuilder.Dims d, Material metal, List<Object> made, StringBuilder log)
        {
            if (metal == null) { DcWarn.Add(log, DcWarn.Id.NoMetal); return false; }
            const float mm = 0.001f;
            float k = d.lidK;
            var mesh = DomeMesh(DomeHalfX * k * mm, DomeHalfZ * k * mm, DomeHeight * k * mm, -DomeSink * k * mm);
            made.Add(mesh);
            var mat = Object.Instantiate(metal);   // lilToon opaque, as the can
            mat.name = "DrinkCan Foam Dome";
            var c = can.foamColor; c.a = 1f;
            mat.SetColor("_Color", c);
            mat.SetTexture("_MainTex", null);   // plain: any grain on the dome read as bread
            foreach (var p in new[] { "_UseReflection", "_UseBumpMap", "_UseMain2ndTex", "_UseMain3rdTex", "_UseBump2ndMap", "_UseMatCap2nd", "_UseRim", "_UseShadow", "_Metallic" })
                if (mat.HasProperty(p)) mat.SetFloat(p, 0f);   // the metal's 2nd texture and normal drew the lid's lines on it
            foreach (var p in new[] { "_Main2ndTex", "_Bump2ndMap", "_SmoothnessTex" })
                if (mat.HasProperty(p)) mat.SetTexture(p, null);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.2f);
            var mc = LoadTexture(FoamMatcapGuid, log);
            if (mc != null && mat.HasProperty("_UseMatCap"))
            {
                mat.SetFloat("_UseMatCap", 1f);
                mat.SetTexture("_MatCapTex", mc);
                mat.SetColor("_MatCapColor", Color.white);
                mat.SetFloat("_MatCapBlendMode", 3f);   // multiply
                mat.SetFloat("_MatCapBlend", 1f);
                mat.SetFloat("_MatCapEnableLighting", 0f);
            }
            made.Add(mat);
            var go = new GameObject(FoamDome);
            go.transform.SetParent(bones[DrinkCanMeshBuilder.BoneCan], false);
            var tc = DrinkCanLidLayout.TearCenter;
            go.transform.localPosition = new Vector3(tc.x * k, d.panelY, tc.y * k) * mm;   // the opening
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);   // shown only while it foams (FX Open layer)
            return true;
        }

        // a half ellipsoid (half-axes hx, hz, height h; base at y0) with gentle lobes and a slight overhang — local metres
        static Mesh DomeMesh(float hx, float hz, float h, float y0)
        {
            const int nu = 48, nv = 16;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            for (int j = 0; j <= nv; j++)
            {
                float el = j / (float)nv * Mathf.PI * 0.5f, rr = Mathf.Cos(el), yy = Mathf.Sin(el);
                for (int i = 0; i <= nu; i++)
                {
                    float a = i / (float)nu * Mathf.PI * 2f;
                    float lobe = 1f + (0.07f * Mathf.Sin(3f * a + 0.6f) + 0.04f * Mathf.Sin(5f * a + 2f)) * rr;
                    float bulge = 1f + 0.12f * Mathf.Sin(el * 2f);
                    var p = new Vector3(Mathf.Cos(a) * hx * rr * lobe * bulge, y0 + yy * h * (1f + 0.05f * Mathf.Sin(2f * a) * rr),Mathf.Sin(a) * hz * rr * lobe * bulge);
                    v.Add(p);
                    uv.Add(new Vector2(p.x / (2f * hz) + 0.5f, p.z / (2f * hz) + 0.5f));
                }
            }
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a0 = j * (nu + 1) + i, a1 = a0 + 1, b0 = a0 + nu + 1, b1 = b0 + 1;
                    tris.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
                }
            var m = new Mesh { name = "DrinkCan Foam Dome" };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            var n = m.normals;   // the top ring is one point: its normals straight up (a pinched cross otherwise)
            for (int i = 0; i <= nu; i++) n[nv * (nu + 1) + i] = Vector3.up;
            m.normals = n;
            m.RecalculateBounds();
            return m;
        }

        // the label band in the metal's colour and shine, no image — an empty Label Texture (the template material carries the
        // default label, which showed through until 9-65)
        internal static readonly Color PlainMetal = new Color(0.93f, 0.94f, 0.95f);
        static void PlainLabel(Material label, Material metal)
        {
            label.SetTexture("_MainTex", null);
            label.SetColor("_Color", metal != null ? metal.GetColor("_Color") : PlainMetal);
            if (metal == null) return;
            foreach (var p in new[] { "_Metallic", "_Smoothness", "_Reflectance" })
                if (metal.HasProperty(p) && label.HasProperty(p)) label.SetFloat(p, metal.GetFloat(p));
        }

        static Texture2D LoadTexture(string guid, StringBuilder log)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
            if (t == null) DcWarn.Add(log, DcWarn.Id.NoFoamTexture, guid);
            return t;
        }

        // the inside taken as a cylinder: wall 0.3 mm in, from the inner bottom (dome) to the countersink under the lid (mm)
        static float InsideMiddle(DrinkCanMeshBuilder.Dims d) => (d.domeD + d.csY) * 0.5f;

        // [level, tilt step] -> the level surface's height above the inside's middle along world up (can-local metres): for each
        // tilt the height whose plane leaves the level's volume below it (bisection; volume by slices along the axis, each a
        // circular segment). Upright it is exactly the upright height
        // opacity: the template's (see-through) while the surface is under the lid's middle, LiquidDeep once it is DeepOver mm above
        public const float LiquidClear = 0.35f, LiquidDeep = 0.9f, DeepOver = 6f;

        internal static float[,] LiquidTable(DrinkCanShape shape, DrinkCanMeshBuilder.Rig rig, int sips, out float[,] alpha, float clear)
        {
            var d = DrinkCanMeshBuilder.GetDims(shape);
            const float mm = 0.001f;
            float r = d.R - 0.3f, y0 = d.domeD, y1 = d.csY, len = y1 - y0, mid = (y0 + y1) * 0.5f;
            var steps = DrinkCanAnimator.TiltSteps;
            var t = new float[sips + 1, steps.Length];
            alpha = new float[sips + 1, steps.Length];
            for (int i = 0; i <= sips; i++)
            {
                float y = Mathf.Lerp(rig.liquidEmpty.y, rig.liquidFull.y, i / (float)sips) / mm;
                float frac = Mathf.Clamp01((y - y0) / len);
                for (int j = 0; j < steps.Length; j++)
                {
                    float th = steps[j] * Mathf.Deg2Rad, s = Mathf.Sin(th), co = Mathf.Cos(th);
                    float target = frac * Mathf.PI * r * r * len;
                    float lo = -(len * 0.5f + r), hi = len * 0.5f + r;
                    for (int it = 0; it < 40; it++)
                    {
                        float m = (lo + hi) * 0.5f;
                        if (Volume(m) < target) lo = m; else hi = m;
                    }
                    t[i, j] = (lo + hi) * 0.5f * mm;
                    // how far the surface is above the lid's middle (world up, mm): the opening is in the liquid past 0
                    float over = (lo + hi) * 0.5f - len * 0.5f * co;
                    alpha[i, j] = Mathf.Lerp(clear, LiquidDeep, Mathf.Clamp01((over + DeepOver * 0.5f) / DeepOver));

                    float Volume(float h)
                    {
                        const int n = 200;
                        float v = 0f, dz = len / n;
                        for (int k = 0; k < n; k++)
                        {
                            float z = -len * 0.5f + (k + 0.5f) * dz;   // along the axis from the middle
                            float a;
                            if (Mathf.Abs(s) < 1e-4f) a = z * co <= h ? Mathf.PI * r * r : 0f;
                            else
                            {
                                float x = Mathf.Clamp((h - z * co) / s, -r, r);   // the plane across this slice (x along the lean)
                                a = r * r * (Mathf.PI * 0.5f + Mathf.Asin(x / r)) + x * Mathf.Sqrt(Mathf.Max(0f, r * r - x * x));
                            }
                            v += a * dz;
                        }
                        return v;
                    }
                }
            }
            return t;
        }

        static AudioClip LoadSound(string guid, StringBuilder log)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (clip == null) DcWarn.Add(log, DcWarn.Id.NoSound, guid);
            return clip;
        }

        static Material Copy(Material src, string name, List<Object> made)
        {
            if (src == null) return null;
            var m = new Material(src) { name = name };
            made.Add(m);
            return m;
        }

        static Transform Anchor(Transform root, string name, Pose world, HumanBodyBones bone)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            t.SetPositionAndRotation(world.position, world.rotation);
            var bp = t.gameObject.AddComponent<ModularAvatarBoneProxy>();
            bp.boneReference = bone;
            bp.attachmentMode = BoneProxyAttachmentMode.AsChildKeepWorldPose;
            return t;
        }

        static Transform Child(Transform parent, string name, Pose world)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(world.position, world.rotation);
            return t;
        }

        static void Receiver(Transform parent, string name, Vector3 local, float radius, string param, params string[] tags)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var r = go.AddComponent<VRCContactReceiver>();
            r.shapeType = ContactBase.ShapeType.Sphere;
            r.radius = radius;
            r.parameter = param;
            r.receiverType = ContactReceiver.ReceiverType.Constant;
            r.allowSelf = true;
            r.allowOthers = false;
            r.localOnly = true;
            r.collisionTags = new List<string>(tags);
        }

        static ParameterConfig Param(string name, ParameterSyncType type, float def, bool localOnly = false) => new ParameterConfig
        {
            nameOrPrefix = name,
            syncType = type,
            defaultValue = def,
            hasExplicitDefaultValue = true,
            saved = false,
            localOnly = localOnly,
        };

        static void BuildMenu(Transform root, int lang, StringBuilder log)
        {
            var menu = new GameObject(MenuName(lang)).transform;
            menu.SetParent(root, false);
            menu.gameObject.AddComponent<ModularAvatarMenuInstaller>();
            var sub = menu.gameObject.AddComponent<ModularAvatarMenuItem>();
            sub.Control = new VRCExpressionsMenu.Control { name = MenuName(lang), type = VRCExpressionsMenu.Control.ControlType.SubMenu, icon = LoadIcon(MenuIconGuid, log) };
            sub.MenuSource = SubmenuSource.Children;

            void Item(string label, VRCExpressionsMenu.Control.ControlType type, string param, bool synced, string iconGuid)
            {
                var go = new GameObject(label);
                go.transform.SetParent(menu, false);
                var mi = go.AddComponent<ModularAvatarMenuItem>();
                mi.Control = new VRCExpressionsMenu.Control
                {
                    name = label,
                    type = type,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = param },
                    value = 1f,
                    icon = LoadIcon(iconGuid, log),
                };
                mi.isSynced = synced;
                mi.isSaved = false;
            }
            Item(TakeOutLabel(lang), VRCExpressionsMenu.Control.ControlType.Toggle, Out, true, TakeOutIconGuid);
            Item(OpenLabel(lang), VRCExpressionsMenu.Control.ControlType.Button, OpenBtn, false, OpenIconGuid);
            Item(DrinkLabel(lang), VRCExpressionsMenu.Control.ControlType.Button, Drink, true, DrinkIconGuid);
            Item(SwitchHandLabel(lang), VRCExpressionsMenu.Control.ControlType.Toggle, SwapHand, true, SwitchHandIconGuid);
        }

        // a missing icon only leaves that menu slot blank — noted in the log, not a warning
        static Texture2D LoadIcon(string guid, StringBuilder log)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
            if (tex == null) log.AppendLine($"menu icon {guid} not found — that slot has no icon");
            return tex;
        }
    }
}
#endif
