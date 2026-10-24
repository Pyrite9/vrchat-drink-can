// Builds the FX controller for one can (merged by MA Merge Animator, paths relative to the DrinkCan root).
//   Out    : Hold on/off; taking the can out refills it (local driver: Opened = false, Level = sips) after one frame at
//            the throw emitters' clearing speed (a can still lying around is gone); letting go of a held can
//            (Out 1 -> 0) is a throw: Thrown raises both emitters' rate for 0.1 s (max one particle each) — DrinkCanThrow
//   Drop   : on the throw, a delay picked from the floor distance below the hand (VRC Raycast) -> drop sound at DropPoint
//   Flick  : the owner swings an empty can hard (hand PhysBone sensors, DrinkCanThrow.BuildFlickSensor) -> drives Out off = the
//            throw above. Armed only 1 s after the can became empty (the last sip's lowering is not a throw); the menu still throws
//   Open   : Closed -> (tab touched or Open button, local) -> Opening (0.8 s, open sound) -> Opened; Opened = false closes it again.
//            A beer head's foam layer thins out to a film over the first seconds after opening (FoamSettle, 9-96)
//   Level  : liquid surface per sips-left value (int states, 0.5 s blend); 0 sips = surface scaled away.
//            Every state below full plays the gulp on enter, on the owner's client only (9-69). Volumes come from the
//            inspector (DrinkCan.openVolume / sipVolume / dropVolume); a sound at 0 gets no Play Audio behaviour
//   Drink  : desktop button -> can goes to the mouth (Hold constraint: 0 = HandPoint, 1 = MouthPoint), one sip at 0.8 s
//            while it is there, back to the hand at 1.6 s; VR: lid held at the mouth (contact) 0.3 s -> one sip
//   Hand   : SwapHand moves HandPoint between GripR (source 0) and GripL (source 1) in 0.15 s; the spawn hand is the default.
//            Each constraint's weights always add up to 1 (no reliance on weight normalisation).
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDKBase;
using static Pyrite9.DrinkCan.DrinkCanConst;

namespace Pyrite9.DrinkCan
{
    static class DrinkCanAnimator
    {
        const string Model = Hold + "/" + DrinkCanModel.ModelName + "/Can";
        const string TabPath = Model + "/Tab";
        const string TearPath = Model + "/TearPanel";
        const string LiquidPath = Model + "/" + LiquidLevel + "/Liquid";   // the liquid bone under its level pivot (9-50)

        public const float TabOpen = -22f, TearOpen = -80f, TabPull = -65f, TearPull = -35f;

        public const float HandMove = 0.15f;
        public const float LipHold = 0.3f;      // VR: opening held at the mouth this long = one sip (9-17 V4)
        public const float SipAt = 0.8f;        // desktop sip: the sip counts here (at the mouth 0.45 - 1.15 s), so the gulp is heard there

        const string SoundPath = Hold + "/" + Sound;
        static readonly string[] ThrowPaths = { HandPoint + "/" + Throw, HandPoint + "/" + ThrowLabel };
        public const float ThrowEmit = 0.1f;     // emission window of a throw (rate 1000/s, max particles 1)

        // the can's tilt steps of the liquid table (DrinkCanPlugin.LiquidTable), degrees
        public static readonly float[] TiltSteps = { 0f, 15f, 30f, 45f, 60f, 75f, 90f, 105f, 120f, 135f, 150f, 165f, 180f };

        public static AnimatorController Build(int sips, bool spawnLeft, DrinkCanMeshBuilder.Rig rig, float[,] liquidY, float[,] liquidA, Color liquidColor, float foamRate, bool foamDome, Color? foamLayer, AudioClip openSound, AudioClip gulpSound,
            bool canThrow, AudioClip dropSound, List<(float maxDistance, float delay)> dropDelays, float openVolume, float gulpVolume, float dropVolume, List<Object> made)
        {
            var c = new AnimatorController { name = "Pyrite9 DrinkCan FX" };
            made.Add(c);
            c.AddParameter(Out, AnimatorControllerParameterType.Bool);
            c.AddParameter(Opened, AnimatorControllerParameterType.Bool);
            c.AddParameter(new AnimatorControllerParameter { name = Level, type = AnimatorControllerParameterType.Int, defaultInt = sips });
            c.AddParameter(Drink, AnimatorControllerParameterType.Bool);
            c.AddParameter(SwapHand, AnimatorControllerParameterType.Bool);
            c.AddParameter(OpenBtn, AnimatorControllerParameterType.Bool);
            c.AddParameter(TabTouch, AnimatorControllerParameterType.Bool);
            c.AddParameter(LipTouch, AnimatorControllerParameterType.Bool);
            c.AddParameter(IsLocal, AnimatorControllerParameterType.Bool);
            c.AddParameter(FloorHit, AnimatorControllerParameterType.Bool);
            c.AddParameter(FloorDistance, AnimatorControllerParameterType.Float);
            c.AddParameter(TiltAngle, AnimatorControllerParameterType.Float);
            if (canThrow)
            {
                c.AddParameter(FlickAAngle, AnimatorControllerParameterType.Float);
                c.AddParameter(FlickBAngle, AnimatorControllerParameterType.Float);
            }

            OutLayer(c, sips, canThrow, made);
            if (canThrow) DropLayer(c, dropSound, dropVolume, dropDelays, made);
            if (canThrow) FlickLayer(c, made);
            OpenLayer(c, openSound, openVolume, foamRate, foamDome, foamLayer, made);
            LevelLayer(c, sips, liquidY, liquidA, liquidColor, gulpSound, gulpVolume, made);
            DrinkLayer(c, made);
            HandLayer(c, spawnLeft, made);
            return c;
        }

        // Gesture controller: the holding hand's fingers in the fitted grip (DrinkCanGripFit) while the can is out.
        //   One layer per hand, finger-only avatar mask, default weight 0; VRC Animator Layer Control raises it to 1 only while
        //   that hand holds the can (Out and the current hand — spawn hand XOR SwapHand), so finger tracking / gestures are
        //   untouched otherwise. Layer indices are local to this controller (MA Merge Animator offsets them).
        public const float GripBlend = 0.15f;

        public static AnimatorController BuildGrip(bool spawnLeft, Dictionary<string, float> right, Dictionary<string, float> left, List<Object> made)
        {
            var c = new AnimatorController { name = "Pyrite9 DrinkCan Grip" };
            made.Add(c);
            c.AddParameter(Out, AnimatorControllerParameterType.Bool);
            c.AddParameter(SwapHand, AnimatorControllerParameterType.Bool);
            // an empty first layer: the first layer is the base layer (weight forced to 1 by Unity / MA), the hand layers must not be it
            var baseSm = new AnimatorStateMachine { name = "Pyrite9 DrinkCan Grip Base", hideFlags = HideFlags.HideInHierarchy };
            made.Add(baseSm);
            c.AddLayer(new AnimatorControllerLayer { name = baseSm.name, stateMachine = baseSm, defaultWeight = 1f });
            foreach (var (isLeft, muscles) in new[] { (false, right), (true, left) })
            {
                if (muscles == null) continue;
                var side = isLeft ? "L" : "R";
                var mask = new AvatarMask { name = "DrinkCan Grip " + side };
                made.Add(mask);
                for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                    mask.SetHumanoidBodyPartActive(part, part == (isLeft ? AvatarMaskBodyPart.LeftFingers : AvatarMaskBodyPart.RightFingers));
                var sm = new AnimatorStateMachine { name = "Pyrite9 DrinkCan Grip " + side, hideFlags = HideFlags.HideInHierarchy };
                made.Add(sm);
                int index = c.layers.Length;
                c.AddLayer(new AnimatorControllerLayer { name = sm.name, stateMachine = sm, defaultWeight = 0f, avatarMask = mask });
                var clip = new AnimationClip { name = "DrinkCan Grip " + side };
                made.Add(clip);
                foreach (var kv in muscles)
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), MuscleProperty(kv.Key)),
                        new AnimationCurve(new Keyframe(0f, kv.Value), new Keyframe(1f / 60f, kv.Value)));
                var off = State(sm, "Not Holding", clip, made);
                var on = State(sm, "Holding", clip, made);
                sm.defaultState = off;
                LayerWeight(off, index, 0f, made);
                LayerWeight(on, index, 1f, made);
                // this hand holds when Out and (SwapHand == spawnLeft) for the right hand / (SwapHand != spawnLeft) for the left
                bool swapWhenHeld = isLeft ? !spawnLeft : spawnLeft;
                var t = To(off, on, made);
                t.AddCondition(AnimatorConditionMode.If, 0, Out);
                t.AddCondition(swapWhenHeld ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, SwapHand);
                To(on, off, made).AddCondition(AnimatorConditionMode.IfNot, 0, Out);
                To(on, off, made).AddCondition(swapWhenHeld ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, 0, SwapHand);
            }
            return c;
        }

        // HumanTrait name "Right Thumb 1 Stretched" / "Left Index Spread" -> animator property "RightHand.Thumb.1 Stretched" / "LeftHand.Index.Spread"
        static string MuscleProperty(string muscle)
        {
            var parts = muscle.Split(' ');
            return parts[0] + "Hand." + parts[1] + "." + string.Join(" ", parts.Skip(2));
        }

        static void LayerWeight(AnimatorState s, int layer, float weight, List<Object> made)
        {
            var lc = s.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            made.Add(lc);
            lc.playable = VRC_AnimatorLayerControl.BlendableLayer.Gesture;
            lc.layer = layer;
            lc.goalWeight = weight;
            lc.blendDuration = GripBlend;
        }

        // ---- layers -------------------------------------------------------------------------------------------

        static void OutLayer(AnimatorController c, int sips, bool canThrow, List<Object> made)
        {
            var sm = Layer(c, "Pyrite9 DrinkCan Out", made);
            if (!canThrow)
            {
                var hidden = State(sm, "Off", Clip("DrinkCan Off", made, Active(0f)), made);
                var held = State(sm, "On", Clip("DrinkCan On", made, Active(1f)), made);
                sm.defaultState = hidden;
                Driver(held, made, Set(Opened, 0f), Set(Level, sips));
                To(hidden, held, made).AddCondition(AnimatorConditionMode.If, 0, Out);
                To(held, hidden, made).AddCondition(AnimatorConditionMode.IfNot, 0, Out);
                return;
            }
            // every state sets all of: Hold shown, both emitters' rate and whether they are on (write defaults off). New Can
            // switches them off for its moment: that clears the old can (9-58; one frame at simulation speed 2000 would kill it
            // into its resting copy)
            Key[] Keys(float hold, float rate, float emitters) => new[] { Active(hold) }
                .Concat(ThrowPaths.SelectMany(p => new[] { Emit(p, rate), Curve(p, typeof(GameObject), "m_IsActive", emitters) })).ToArray();
            var off = State(sm, "Off", Clip("DrinkCan Off", made, Keys(0f, 0f, 1f)), made);
            var fresh = State(sm, "New Can", Clip("DrinkCan New", made, Keys(0f, 0f, 0f)), made);
            var on = State(sm, "On", Clip("DrinkCan On", made, Keys(1f, 0f, 1f)), made);
            var thrown = State(sm, "Thrown", Clip("DrinkCan Thrown", made, ThrowEmit, Keys(0f, 1000f, 1f)), made);
            sm.defaultState = off;
            Driver(on, made, Set(Opened, 0f), Set(Level, sips));
            To(off, fresh, made).AddCondition(AnimatorConditionMode.If, 0, Out);
            var t = To(fresh, on, made);
            t.hasExitTime = true;
            t.exitTime = 1f;
            To(on, thrown, made).AddCondition(AnimatorConditionMode.IfNot, 0, Out);
            t = To(thrown, off, made);
            t.hasExitTime = true;
            t.exitTime = 1f;
            To(thrown, fresh, made).AddCondition(AnimatorConditionMode.If, 0, Out);
        }

        // the drop sound after a throw: free-fall delay by the floor distance under the hand at that moment
        static void DropLayer(AnimatorController c, AudioClip dropSound, float dropVolume, List<(float maxDistance, float delay)> delays, List<Object> made)
        {
            var sm = Layer(c, "Pyrite9 DrinkCan Drop", made);
            var none = Clip("DrinkCan Drop (no curves)", made);
            var idle = State(sm, "Idle", none, made);
            var armed = State(sm, "Holding", none, made);
            sm.defaultState = idle;
            To(idle, armed, made).AddCondition(AnimatorConditionMode.If, 0, Out);
            if (dropSound != null && delays != null)
            {
                float lo = 0f;
                foreach (var (hi, delay) in delays)
                {
                    var s = State(sm, $"Drop {lo * 1000f:0}-{hi * 1000f:0} mm", none, made);   // no '.' in state names
                    PlayAudio(s, dropSound, 0.95f, 1.05f, made, DropPoint, delay, dropVolume);
                    var t = To(armed, s, made);   // first match wins: ascending distance
                    t.AddCondition(AnimatorConditionMode.IfNot, 0, Out);
                    t.AddCondition(AnimatorConditionMode.If, 0, FloorHit);
                    t.AddCondition(AnimatorConditionMode.Less, hi, FloorDistance);
                    To(s, armed, made).AddCondition(AnimatorConditionMode.If, 0, Out);
                    lo = hi;
                }
            }
            To(armed, idle, made).AddCondition(AnimatorConditionMode.IfNot, 0, Out);   // no floor within reach: silent
        }

        // a flick of an empty can throws it (owner only): Idle -> Wait (1 s) -> Armed -> either sensor past FlickAngle -> Throw
        // (Out off, synced). Out off or a refill goes back to Idle
        // PhysBone _Angle (1 = 180 deg). Built avatar in Play mode (Kipfel moved in 0.3 s strokes): 0.6 m/s 0.008, 1 m/s 0.09,
        // 2 m/s 0.13 -> 0.01 = about 0.7 m/s, any real swing (the author: "even a slight swing", 9-51)
        // 0.01 (~0.7 m/s) threw on any movement (the author: "far too sensitive", 9-54) -> 0.1, about 1.2 m/s
        // 0.1 (~1.2 m/s) then never threw in game (9-56) -> 0.05, about 0.85 m/s
        // 0.05 still took a hard swing ("컨트롤러 던질 것 같아요", 9-57) -> 0.02, about 0.65 m/s (0.01 threw on any movement)
        public const float FlickAngle = 0.02f;
        public const float FlickArmDelay = 1f;
        public const float FlickRelease = 0.1f;   // s from the swing's start to letting go

        static void FlickLayer(AnimatorController c, List<Object> made)
        {
            var sm = Layer(c, "Pyrite9 DrinkCan Flick", made);
            const string path = HandPoint + "/" + FlickSensor;
            var none = Clip("DrinkCan Flick (sensor on)", made, Curve(path, typeof(GameObject), "m_IsActive", 1f));
            var idle = State(sm, "Idle", none, made);
            var wait = State(sm, "Wait", Clip("DrinkCan Flick Wait", made, FlickArmDelay, Curve(path, typeof(GameObject), "m_IsActive", 1f)), made);
            var armed = State(sm, "Armed", none, made);
            // let go a moment into the swing, not as it starts (the threshold is low: released at its start the can only
            // dropped at the feet, 9-53)
            var swing = State(sm, "Swing", Clip("DrinkCan Flick Swing", made, FlickRelease, Curve(path, typeof(GameObject), "m_IsActive", 1f)), made);
            var fire = State(sm, "Throw", none, made);
            sm.defaultState = idle;
            Driver(fire, made, Set(Out, 0f));

            var t = To(idle, wait, made);
            t.AddCondition(AnimatorConditionMode.If, 0, IsLocal);
            t.AddCondition(AnimatorConditionMode.If, 0, Out);
            t.AddCondition(AnimatorConditionMode.Equals, 0, Level);
            t = To(wait, armed, made);
            t.hasExitTime = true;
            t.exitTime = 1f;
            foreach (var p in new[] { FlickAAngle, FlickBAngle })
                To(armed, swing, made).AddCondition(AnimatorConditionMode.Greater, FlickAngle, p);
            t = To(swing, fire, made);
            t.hasExitTime = true;
            t.exitTime = 1f;
            foreach (var s in new[] { wait, armed, swing, fire })
            {
                To(s, idle, made).AddCondition(AnimatorConditionMode.IfNot, 0, Out);
                To(s, idle, made).AddCondition(AnimatorConditionMode.Greater, 0, Level);
            }
        }

        // the opening's fizz emitter (9-65): it fizzes from the pop (0.62 s into Opening) for FoamOn s;
        // the beer head's dome (9-66): wells up from the pop, holds, sinks back (DomeScale)
        const string FoamPath = Model + "/" + Foam;
        const string DomePath = Model + "/" + FoamDome;
        const float FoamFrom = 0.62f, FoamOn = 0.58f, OpeningEnd = 0.8f;   // OpeningEnd: OpeningClip's length
        const float DomeUp = 0.35f, DomeHold = 1.4f, DomeEnd = 2.6f;       // s after the pop

        // the dome's local scale t s after the pop: from 55 % wide and flat to full (ease out), held, then sinking to 15 % high
        static Vector3 DomeScale(float t)
        {
            if (t < DomeUp) { float u = t / DomeUp; u = 1f - (1f - u) * (1f - u); return new Vector3(0.55f + 0.45f * u, Mathf.Max(0.01f, u), 0.55f + 0.45f * u); }
            if (t < DomeHold) return Vector3.one;
            float w = Mathf.Clamp01((t - DomeHold) / (DomeEnd - DomeHold));
            return new Vector3(1f - 0.1f * w, 1f - 0.85f * w, 1f - 0.1f * w);
        }

        // the dome on a clip of the given length whose time 0 is popAt s after the pop (negative: before it) — scale sampled
        // with straight segments, shown only between the pop and DomeEnd (stepped)
        static void DomeCurves(AnimationClip clip, float length, float popAt)
        {
            var sx = new AnimationCurve(); var sy = new AnimationCurve(); var sz = new AnimationCurve(); var on = new AnimationCurve();
            float start = -popAt, end = DomeEnd - popAt;   // clip times of the pop and the end
            float span = Mathf.Clamp(end, 1f / 60f, length);   // past the end the scale holds (a long Opened clip, FoamSettle)
            int n = Mathf.CeilToInt(span * 30f);
            for (int i = 0; i <= n; i++)
            {
                float ct = span * i / n, t = Mathf.Max(0f, ct + popAt);
                var sc = DomeScale(t);
                sx.AddKey(ct, sc.x); sy.AddKey(ct, sc.y); sz.AddKey(ct, sc.z);
            }
            if (span < length) { var sc = DomeScale(length + popAt); sx.AddKey(length, sc.x); sy.AddKey(length, sc.y); sz.AddKey(length, sc.z); }
            on.AddKey(0f, start <= 0f && end > 0f ? 1f : 0f);
            if (start > 0f && start < length) on.AddKey(start, 1f);
            if (end > 0f && end < length) on.AddKey(end, 0f);
            on.AddKey(length, end > length && start < length ? 1f : 0f);
            foreach (var cv in new[] { sx, sy, sz })
                for (int i = 0; i < cv.length; i++) { AnimationUtility.SetKeyLeftTangentMode(cv, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(cv, i, AnimationUtility.TangentMode.Linear); }
            for (int i = 0; i < on.length; i++) { AnimationUtility.SetKeyLeftTangentMode(on, i, AnimationUtility.TangentMode.Constant); AnimationUtility.SetKeyRightTangentMode(on, i, AnimationUtility.TangentMode.Constant); }
            AnimationUtility.SetEditorCurve(clip, Bind(DomePath, typeof(Transform), "m_LocalScale.x"), sx);
            AnimationUtility.SetEditorCurve(clip, Bind(DomePath, typeof(Transform), "m_LocalScale.y"), sy);
            AnimationUtility.SetEditorCurve(clip, Bind(DomePath, typeof(Transform), "m_LocalScale.z"), sz);
            AnimationUtility.SetEditorCurve(clip, Bind(DomePath, typeof(GameObject), "m_IsActive"), on);
        }

        // the beer head's foam layer on the drink (9-96): opaque until the dome starts to sink, then it fades out (ease out)
        // over FoamSettle s, so the drink's colour shows — kept opaque it hid the colour for good; of the leftovers rendered
        // (0 / 0.15 / 0.3 / 0.5) the author chose none at all (2026-10-25). Closed (a fresh can) puts it back. All four
        // channels on its own renderer (see LiquidColor, 9-54)
        const string FoamLayerPath = Hold + "/" + FoamSurface;
        const float FoamSettle = 12f, FoamSettled = 0f;   // s from DomeHold after the pop; the alpha left

        static Key[] FoamLayerColor(Color col, float a) => new[]
        {
            Curve(FoamLayerPath, typeof(SkinnedMeshRenderer), "material._Color.r", col.r), Curve(FoamLayerPath, typeof(SkinnedMeshRenderer), "material._Color.g", col.g),
            Curve(FoamLayerPath, typeof(SkinnedMeshRenderer), "material._Color.b", col.b), Curve(FoamLayerPath, typeof(SkinnedMeshRenderer), "material._Color.a", a),
        };

        // the layer's alpha t s after the pop
        static float FoamLayerAlpha(float t)
        {
            float u = Mathf.Clamp01((t - DomeHold) / FoamSettle);
            return Mathf.Lerp(DrinkCanPlugin.FoamAlpha, FoamSettled, 1f - (1f - u) * (1f - u));
        }

        // the layer's colour on a clip of the given length whose time 0 is popAt s after the pop (straight segments, 2 a second)
        static void FoamLayerCurves(AnimationClip clip, float length, float popAt, Color col)
        {
            var a = new AnimationCurve();
            int n = Mathf.Max(1, Mathf.CeilToInt(length * 2f));
            for (int i = 0; i <= n; i++) { float ct = length * i / n; a.AddKey(ct, FoamLayerAlpha(Mathf.Max(0f, ct + popAt))); }
            for (int i = 0; i < a.length; i++) { AnimationUtility.SetKeyLeftTangentMode(a, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(a, i, AnimationUtility.TangentMode.Linear); }
            foreach (var k in FoamLayerColor(col, 0f).Take(3))
                AnimationUtility.SetEditorCurve(clip, k.binding, new AnimationCurve(new Keyframe(0f, k.value), new Keyframe(length, k.value)));
            AnimationUtility.SetEditorCurve(clip, Bind(FoamLayerPath, typeof(SkinnedMeshRenderer), "material._Color.a"), a);
        }

        static void OpenLayer(AnimatorController c, AudioClip openSound, float openVolume, float foamRate, bool dome, Color? foamLayer, List<Object> made)
        {
            var sm = Layer(c, "Pyrite9 DrinkCan Open", made);
            bool foam = foamRate > 0f;
            var closedKeys = new List<Key> { Rot(TabPath, 0f), Rot(TearPath, 0f) };
            if (foam) closedKeys.Add(Emit(FoamPath, 0f));
            if (dome) closedKeys.Add(Curve(DomePath, typeof(GameObject), "m_IsActive", 0f));
            if (foamLayer.HasValue) closedKeys.AddRange(FoamLayerColor(foamLayer.Value, DrinkCanPlugin.FoamAlpha));
            var closedClip = Clip("DrinkCan Closed", made, closedKeys.ToArray());
            var closed = State(sm, "Closed", closedClip, made);
            var touch = State(sm, "Open Request", closedClip, made);
            var openingClip = OpeningClip(made);
            if (foam)
                AnimationUtility.SetEditorCurve(openingClip, Bind(FoamPath, typeof(ParticleSystem), "EmissionModule.rateOverTime.scalar"),
                    new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(FoamFrom - 0.02f, 0f), new Keyframe(FoamFrom, foamRate), new Keyframe(OpeningEnd, foamRate)));
            if (dome) DomeCurves(openingClip, OpeningEnd, -FoamFrom);
            if (foamLayer.HasValue) FoamLayerCurves(openingClip, OpeningEnd, -FoamFrom, foamLayer.Value);
            var opening = State(sm, "Opening", openingClip, made);
            PlayAudio(opening, openSound, 1f, 1f, made, volume: openVolume);   // tab lift at 0.05 s, pop at 0.66 s as the panel folds in (0.6 s)
            // Opened: the rest of the fizz / the dome, then off (the clip does not loop: it holds its last key)
            float rest = FoamOn - (OpeningEnd - FoamFrom), afterPop = OpeningEnd - FoamFrom;
            float openedLen = dome ? DomeEnd - afterPop + 0.02f : foam ? rest + 0.02f : 1f / 60f;
            if (foamLayer.HasValue) openedLen = Mathf.Max(openedLen, DomeHold + FoamSettle - afterPop + 0.02f);
            var openedClip = Clip("DrinkCan Opened", made, openedLen, Rot(TabPath, TabOpen), Rot(TearPath, TearOpen));
            if (foam)
                AnimationUtility.SetEditorCurve(openedClip, Bind(FoamPath, typeof(ParticleSystem), "EmissionModule.rateOverTime.scalar"),
                    new AnimationCurve(new Keyframe(0f, foamRate), new Keyframe(rest, foamRate), new Keyframe(rest + 0.02f, 0f)));
            if (dome) DomeCurves(openedClip, openedLen, afterPop);
            if (foamLayer.HasValue) FoamLayerCurves(openedClip, openedLen, afterPop, foamLayer.Value);
            var opened = State(sm, "Opened", openedClip, made);
            sm.defaultState = closed;
            Driver(touch, made, Set(Opened, 1f));

            foreach (var trigger in new[] { TabTouch, OpenBtn })
            {
                var t = To(closed, touch, made);
                t.AddCondition(AnimatorConditionMode.If, 0, trigger);
                t.AddCondition(AnimatorConditionMode.If, 0, IsLocal);
                t.AddCondition(AnimatorConditionMode.If, 0, Out);
                t.AddCondition(AnimatorConditionMode.IfNot, 0, Opened);
            }
            To(touch, opening, made).AddCondition(AnimatorConditionMode.If, 0, Opened);
            To(closed, opening, made).AddCondition(AnimatorConditionMode.If, 0, Opened);
            var done = To(opening, opened, made);
            done.hasExitTime = true;
            done.exitTime = 1f;
            To(opening, closed, made).AddCondition(AnimatorConditionMode.IfNot, 0, Opened);
            To(opened, closed, made).AddCondition(AnimatorConditionMode.IfNot, 0, Opened);
        }

        // each level: the level surface's height above the inside's middle (along world up — the pivot is kept level) as the can
        // tilts, a 1D blend over the hanging sensor's angle, from the volume table (the surface through the axis at the upright
        // height went up to the lid once the can leaned past ~45 deg — in game 2026-10-23, 9-50)
        // and the liquid's opacity: see-through while the surface is below the lid, deep (nearly opaque) once the opening is under
        // it — tilted far, the opening went into the liquid and showed the empty inside through the thin surface (9-52). On the
        // liquid's own renderer, all four channels: animating _Color.a alone on the can's renderer set r, g, b to 0 on every
        // material of it — the whole can went black (9-54)
        // (a submerged opening still showed through the can until it got a dark inside, DrinkCanPlugin.LevelLiquid, 9-59)
        const string RendererPath = Hold + "/" + LiquidSurface;

        static Key[] LiquidColor(Color col, float a) => new[]
        {
            Curve(RendererPath, typeof(SkinnedMeshRenderer), "material._Color.r", col.r), Curve(RendererPath, typeof(SkinnedMeshRenderer), "material._Color.g", col.g),
            Curve(RendererPath, typeof(SkinnedMeshRenderer), "material._Color.b", col.b), Curve(RendererPath, typeof(SkinnedMeshRenderer), "material._Color.a", a),
        };

        static void LevelLayer(AnimatorController c, int sips, float[,] liquidY, float[,] liquidA, Color liquidColor, AudioClip gulpSound, float gulpVolume, List<Object> made)
        {
            var sm = Layer(c, "Pyrite9 DrinkCan Level", made);
            AnimatorState full = null;
            for (int i = 0; i <= sips; i++)
            {
                Motion motion;
                // empty: no surface at all (a disc left on the bottom read as liquid still in the can — VR test 2026-10-21)
                if (i == 0)
                    motion = Clip("DrinkCan Level 0", made, new[] { Curve(LiquidPath, typeof(Transform), "m_LocalPosition.y", liquidY[0, 0]),
                        Curve(LiquidPath, typeof(Transform), "m_LocalScale.x", 0f), Curve(LiquidPath, typeof(Transform), "m_LocalScale.y", 0f),
                        Curve(LiquidPath, typeof(Transform), "m_LocalScale.z", 0f) }.Concat(LiquidColor(liquidColor, liquidA[0, 0])).ToArray());
                else
                {
                    var tree = new BlendTree { name = $"DrinkCan Level {i}", blendType = BlendTreeType.Simple1D, blendParameter = TiltAngle, useAutomaticThresholds = false };
                    made.Add(tree);
                    for (int j = 0; j < TiltSteps.Length; j++)
                        tree.AddChild(Clip($"DrinkCan Level {i} tilt {TiltSteps[j]:0}", made, new[] { Curve(LiquidPath, typeof(Transform), "m_LocalPosition.y", liquidY[i, j]),
                            Curve(LiquidPath, typeof(Transform), "m_LocalScale.x", 1f), Curve(LiquidPath, typeof(Transform), "m_LocalScale.y", 1f),
                            Curve(LiquidPath, typeof(Transform), "m_LocalScale.z", 1f) }.Concat(LiquidColor(liquidColor, liquidA[i, j])).ToArray()), TiltSteps[j] / 180f);
                    motion = tree;
                }
                var s = State(sm, $"Level {i}", motion, made);
                // below full is only reached by a sip (a refill jumps to full) — slight pitch spread so sips don't sound copied.
                // The gulp is the drinker's own: only the owner's client enters the state that plays it (others heard every
                // sip — the author 2026-10-24, 9-69); the rest take the same level silently
                bool gulp = i < sips && gulpSound != null && gulpVolume > 0f;
                var t = sm.AddAnyStateTransition(s);
                made.Add(t);
                t.hasExitTime = false;
                t.duration = 0.5f;
                t.canTransitionToSelf = false;
                t.AddCondition(AnimatorConditionMode.Equals, i, Level);
                if (gulp)
                {
                    t.AddCondition(AnimatorConditionMode.IfNot, 0, IsLocal);
                    var own = State(sm, $"Level {i} (owner)", motion, made);
                    PlayAudio(own, gulpSound, 0.95f, 1.05f, made, volume: gulpVolume);
                    var o = sm.AddAnyStateTransition(own);
                    made.Add(o);
                    o.hasExitTime = false;
                    o.duration = 0.5f;
                    o.canTransitionToSelf = false;
                    o.AddCondition(AnimatorConditionMode.Equals, i, Level);
                    o.AddCondition(AnimatorConditionMode.If, 0, IsLocal);
                }
                if (i == sips) full = s;
            }
            sm.defaultState = full;
        }

        static void DrinkLayer(AnimatorController c, List<Object> made)
        {
            var sm = Layer(c, "Pyrite9 DrinkCan Drink", made);
            var holdClip = Clip("DrinkCan In Hand", made, Weight(0, 1f), Weight(1, 0f));
            var idle = State(sm, "Idle", holdClip, made);
            var sip = State(sm, "Sip (button)", SipClip("DrinkCan Sip", 0f, SipAt, made), made);
            var sipBack = State(sm, "Sip Back (button)", SipClip("DrinkCan Sip Back", SipAt, SipEnd, made), made);
            var sipDone = State(sm, "Sip Done (button)", holdClip, made);
            var lip = State(sm, "At Mouth (VR)", Clip("DrinkCan At Mouth", made, LipHold, Weight(0, 1f), Weight(1, 0f)), made);
            var lipDone = State(sm, "Sip Done (VR)", holdClip, made);
            sm.defaultState = idle;
            Driver(sipBack, made, Add(Level, -1f));
            Driver(lipDone, made, Add(Level, -1f));

            // Out: without it, Drink after throwing an opened can still sipped with no can in hand (gulp, Level -1; 9-90)
            var a = To(idle, sip, made);
            a.AddCondition(AnimatorConditionMode.If, 0, Drink);
            a.AddCondition(AnimatorConditionMode.If, 0, Out);
            a.AddCondition(AnimatorConditionMode.If, 0, Opened);
            a.AddCondition(AnimatorConditionMode.Greater, 0, Level);
            // thrown mid-sip: back to idle before Sip Back takes the sip
            To(sip, idle, made).AddCondition(AnimatorConditionMode.IfNot, 0, Out);
            var b = To(sip, sipBack, made);
            b.hasExitTime = true;
            b.exitTime = 1f;
            b = To(sipBack, sipDone, made);
            b.hasExitTime = true;
            b.exitTime = 1f;
            To(sipDone, idle, made).AddCondition(AnimatorConditionMode.IfNot, 0, Drink);

            var v = To(idle, lip, made);
            v.AddCondition(AnimatorConditionMode.If, 0, LipTouch);
            v.AddCondition(AnimatorConditionMode.If, 0, Out);
            v.AddCondition(AnimatorConditionMode.If, 0, Opened);
            v.AddCondition(AnimatorConditionMode.If, 0, IsLocal);
            v.AddCondition(AnimatorConditionMode.Greater, 0, Level);
            To(lip, idle, made).AddCondition(AnimatorConditionMode.IfNot, 0, LipTouch);
            var w = To(lip, lipDone, made);
            w.hasExitTime = true;
            w.exitTime = 1f;
            To(lipDone, idle, made).AddCondition(AnimatorConditionMode.IfNot, 0, LipTouch);
        }

        static void HandLayer(AnimatorController c, bool spawnLeft, List<Object> made)
        {
            var sm = Layer(c, "Pyrite9 DrinkCan Hand", made);
            var right = State(sm, "Right", Clip("DrinkCan Right Hand", made, HandWeight(0, 1f), HandWeight(1, 0f)), made);
            var left = State(sm, "Left", Clip("DrinkCan Left Hand", made, HandWeight(0, 0f), HandWeight(1, 1f)), made);
            var spawn = spawnLeft ? left : right;
            var other = spawnLeft ? right : left;
            sm.defaultState = spawn;
            var t = To(spawn, other, made);
            t.AddCondition(AnimatorConditionMode.If, 0, SwapHand);
            t.duration = HandMove;
            t.hasFixedDuration = true;
            t = To(other, spawn, made);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, SwapHand);
            t.duration = HandMove;
            t.hasFixedDuration = true;
        }

        // ---- clips --------------------------------------------------------------------------------------------

        static AnimationClip OpeningClip(List<Object> made)
        {
            var clip = new AnimationClip { name = "DrinkCan Opening" };
            made.Add(clip);
            AnimationUtility.SetEditorCurve(clip, Bind(TabPath, typeof(Transform), "localEulerAnglesRaw.z"),
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.32f, TabPull), new Keyframe(0.8f, TabOpen)));
            AnimationUtility.SetEditorCurve(clip, Bind(TearPath, typeof(Transform), "localEulerAnglesRaw.z"),
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.1f, 0f), new Keyframe(0.32f, TearPull), new Keyframe(0.6f, TearOpen), new Keyframe(0.8f, TearOpen)));
            return clip;
        }

        const float SipEnd = 1.6f;

        // the 1.6 s desktop sip (mouth weight 0 -> 1 at 0.45 s, held to 1.15 s, back to 0 at 1.6 s, flat tangents), cut at
        // [from, to] inside the hold so the sip can be counted in between — the two halves play back as the one clip did
        static AnimationClip SipClip(string name, float from, float to, List<Object> made)
        {
            var clip = new AnimationClip { name = name };
            made.Add(clip);
            float Mouth(float t) => t <= 0.45f ? t / 0.45f : t <= 1.15f ? 1f : 1f - (t - 1.15f) / (SipEnd - 1.15f);
            var times = new[] { from, 0.45f, 1.15f, to }.Where(t => t >= from && t <= to).Distinct().ToArray();
            var toMouth = new AnimationCurve(times.Select(t => new Keyframe(t - from, Mouth(t))).ToArray());
            var toHand = new AnimationCurve(times.Select(t => new Keyframe(t - from, 1f - Mouth(t))).ToArray());
            AnimationUtility.SetEditorCurve(clip, WeightBinding(0), toHand);
            AnimationUtility.SetEditorCurve(clip, WeightBinding(1), toMouth);
            return clip;
        }

        // plays clip once on a source (default Hold/Sound) when the state is entered — on every client that enters it.
        // volume is the inspector's (DrinkCan.openVolume / sipVolume / dropVolume); 0 = no behaviour at all
        static void PlayAudio(AnimatorState s, AudioClip clip, float pitchMin, float pitchMax, List<Object> made, string path = SoundPath, float delay = 0f, float volume = 1f)
        {
            if (clip == null || volume <= 0f) return;
            var p = s.AddStateMachineBehaviour<VRCAnimatorPlayAudio>();
            made.Add(p);
            p.SourcePath = path;
            p.DelayInSeconds = delay;
            p.Clips = new[] { clip };
            p.ClipsApplySettings = VRC_AnimatorPlayAudio.ApplySettings.AlwaysApply;
            p.PlaybackOrder = VRC_AnimatorPlayAudio.Order.Roundabout;
            p.Volume = new Vector2(volume, volume);
            p.VolumeApplySettings = VRC_AnimatorPlayAudio.ApplySettings.AlwaysApply;
            p.Pitch = new Vector2(pitchMin, pitchMax);
            p.PitchApplySettings = VRC_AnimatorPlayAudio.ApplySettings.AlwaysApply;
            p.Loop = false;
            p.LoopApplySettings = VRC_AnimatorPlayAudio.ApplySettings.AlwaysApply;
            p.StopOnEnter = true;
            p.PlayOnEnter = true;
        }

        struct Key
        {
            public EditorCurveBinding binding;
            public float value;
        }

        static Key Curve(string path, System.Type type, string prop, float value) =>
            new Key { binding = Bind(path, type, prop), value = value };

        static Key Active(float v) => Curve(Hold, typeof(GameObject), "m_IsActive", v);
        static Key Emit(string path, float rate) => Curve(path, typeof(ParticleSystem), "EmissionModule.rateOverTime.scalar", rate);
        static Key Rot(string path, float z) => Curve(path, typeof(Transform), "localEulerAnglesRaw.z", z);
        static Key Weight(int source, float w) => new Key { binding = WeightBinding(source), value = w };
        static EditorCurveBinding WeightBinding(int source) => Bind(Hold, typeof(VRCParentConstraint), $"Sources.source{source}.Weight");
        static Key HandWeight(int source, float w) =>
            new Key { binding = Bind(HandPoint, typeof(VRCParentConstraint), $"Sources.source{source}.Weight"), value = w };

        static EditorCurveBinding Bind(string path, System.Type type, string prop) => EditorCurveBinding.FloatCurve(path, type, prop);

        static AnimationClip Clip(string name, List<Object> made, params Key[] keys) => Clip(name, made, 1f / 60f, keys);

        static AnimationClip Clip(string name, List<Object> made, float length, params Key[] keys)
        {
            var clip = new AnimationClip { name = name };
            made.Add(clip);
            foreach (var k in keys)
                AnimationUtility.SetEditorCurve(clip, k.binding, new AnimationCurve(new Keyframe(0f, k.value), new Keyframe(length, k.value)));
            return clip;
        }

        // ---- graph helpers ------------------------------------------------------------------------------------

        static AnimatorStateMachine Layer(AnimatorController c, string name, List<Object> made)
        {
            var sm = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            made.Add(sm);
            c.AddLayer(new AnimatorControllerLayer { name = name, stateMachine = sm, defaultWeight = 1f });
            return sm;
        }

        static AnimatorState State(AnimatorStateMachine sm, string name, Motion motion, List<Object> made)
        {
            var s = sm.AddState(name);
            made.Add(s);
            s.motion = motion;
            s.writeDefaultValues = false;
            return s;
        }

        static AnimatorStateTransition To(AnimatorState from, AnimatorState to, List<Object> made)
        {
            var t = from.AddTransition(to);
            made.Add(t);
            t.hasExitTime = false;
            t.duration = 0f;
            return t;
        }

        static VRC_AvatarParameterDriver.Parameter Set(string name, float value) =>
            new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Set, name = name, value = value };

        static VRC_AvatarParameterDriver.Parameter Add(string name, float value) =>
            new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Add, name = name, value = value };

        static void Driver(AnimatorState s, List<Object> made, params VRC_AvatarParameterDriver.Parameter[] ps)
        {
            var d = s.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            made.Add(d);
            d.localOnly = true;
            d.parameters = new List<VRC_AvatarParameterDriver.Parameter>(ps);
        }
    }
}
