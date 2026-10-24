using UnityEngine;
using VRC.SDKBase;

namespace Pyrite9.DrinkCan
{
    public enum DrinkCanHand { Right = 0, Left = 1 }

    /// <summary>Expression Menu labels and inspector text (same order as the wheelchair's MenuLanguage).</summary>
    public enum DrinkCanMenuLanguage { English = 0, Japanese = 1, Korean = 2 }

    /// <summary>Holding hand: Auto = fitted to each avatar's hand at build (fingers and thumb on the can), Manual = the pose sliders.</summary>
    public enum DrinkCanHandPoseMode { Auto = 0, Manual = 1 }

    /// <summary>Foam: none, fizz when opened (cola, cider), or a beer head (a foam layer + foam welling out when opened).</summary>
    public enum DrinkCanFoam { None = 0, Bubbles = 1, Head = 2 }

    /// <summary>
    /// Can settings. Editor-only (stripped from the uploaded avatar); the build plugin (Editor/DrinkCanPlugin.cs)
    /// turns these values into the can model, materials, hand/mouth anchors, contacts, menu and FX layers.
    /// In the editor a preview model is shown (not saved): on this object's MeshFilter when it has one (dev scenes),
    /// otherwise as a hidden child.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [AddComponentMenu("Pyrite9/Drink Can")]
    public class DrinkCan : MonoBehaviour, IEditorOnly
    {
        [Tooltip("Expression Menu labels (applied at build) and inspector text.")]
        public DrinkCanMenuLanguage menuLanguage = DrinkCanMenuLanguage.English;

        [Header("Size")]
        public DrinkCanSizePreset sizePreset = DrinkCanSizePreset.Can350;
        [Range(DrinkCanShape.MinHeightMm, DrinkCanShape.MaxHeightMm)] public float heightMm = 122f;
        [Range(DrinkCanShape.MinDiameterMm, DrinkCanShape.MaxDiameterMm)] public float diameterMm = 66f;

        [Header("Label")]
        public Texture2D labelTexture;
        public DrinkCanLabelFit labelFit = DrinkCanLabelFit.Stretch;

        [Header("Drink")]
        [Tooltip("Colour of the drink seen through the opening.")]
        public Color liquidColor = new Color(0.80f, 0.50f, 0.12f);
        [Range(3, 10), Tooltip("Sips until the can is empty.")]
        public int sips = 6;
        [Tooltip("None / fizz when opened (cola, cider) / beer head (a foam layer on the drink + foam welling out when opened).")]
        public DrinkCanFoam foam = DrinkCanFoam.Head;
        [Tooltip("Colour of the foam layer and the foam / droplets when opened.")]
        public Color foamColor = new Color(1f, 0.97f, 0.9f);

        [Header("Sound")]
        [Range(0f, 1f), Tooltip("Volume of the opening sound (everyone nearby hears it). 1 = the loudest an avatar may play, 0 = off.")]
        public float openVolume = 1f;
        // default half (-6 dB): at 1 it was too loud next to the opening in game (the author 2026-10-23, 9-48)
        [Range(0f, 1f), Tooltip("Volume of the gulp (only you hear it). 1 = the loudest an avatar may play, 0 = off.")]
        public float sipVolume = 0.5f;
        [Range(0f, 1f), Tooltip("Volume of a tossed can landing (everyone nearby hears it). 1 = the loudest an avatar may play, 0 = off.")]
        public float dropVolume = 1f;

        [Header("Holding")]
        [Tooltip("Hand the can appears in when taken out. The menu can switch hands while playing; reloading the avatar returns to this one.")]
        public DrinkCanHand hand = DrinkCanHand.Right;
        [Tooltip("While the can is held, that hand's fingers close round it (Gesture layer). Finger tracking and gestures of that hand pause while holding.")]
        public bool gripHandPose = true;
        [Tooltip("Can size relative to the avatar's hand. 1 = a real can in a real-sized hand.")]
        [Range(0.5f, 2f)] public float sizeScale = 1f;
        [Tooltip("Grip adjustment in the hand (m, hand space): + moves the can away from the palm / toward the fingertips / toward the thumb.")]
        public Vector3 gripOffset;
        [Tooltip("Turns the can around its own axis in the hand (degrees).")]
        public float gripTwist;
        [Tooltip("Leans the can in the hand about its middle (degrees): x tips the top toward the fingertips, y out of the palm.")]
        public Vector2 gripTilt;

        [Header("Hand pose")]
        [Tooltip("Auto: the build fits the holding hand and the can's seat to this avatar's hand. Manual: the sliders below (moving one switches to Manual).")]
        public DrinkCanHandPoseMode handPoseMode = DrinkCanHandPoseMode.Auto;
        // set once: a can saved before the mode existed keeps a pose its owner shaped (Manual), an untouched one goes Auto
        [SerializeField, HideInInspector] bool _handPoseModeSet;
        [Tooltip("Holding hand, humanoid muscles (-1 = fully curled, 0 = rest, +1 = bent back): x root, y middle, z tip joint, w spread.")]
        public Vector4 poseIndex = DefaultPoseIndex;
        public Vector4 poseMiddle = DefaultPoseMiddle;
        public Vector4 poseRing = DefaultPoseRing;
        public Vector4 poseLittle = DefaultPoseLittle;
        [Tooltip("Holding thumb, humanoid muscles: x spread (below -1 = past the avatar's limit), y root, z middle, w tip joint.")]
        public Vector4 poseThumb = DefaultPoseThumb;

        // the author's pose (shaped by eye on Milfy, 2026-10-22): fingers nearly straight along the can, thumb spread wide
        public static readonly Vector4 DefaultPoseIndex = new Vector4(0.09f, -0.01f, 0.09f, -0.09f);
        public static readonly Vector4 DefaultPoseMiddle = new Vector4(0f, 0.08f, 0.03f, 0.1f);
        public static readonly Vector4 DefaultPoseRing = new Vector4(0.09f, 0.33f, -0.24f, -0.23f);
        public static readonly Vector4 DefaultPoseLittle = new Vector4(0.21f, 0.4f, 0.08f, 0f);
        public static readonly Vector4 DefaultPoseThumb = new Vector4(-1.74f, 0.26f, 0.14f, 0.01f);

        [Header("Materials (templates)")]
        public Material metalMaterial;
        public Material labelMaterial;
        public Material liquidMaterial;

        [SerializeField, HideInInspector] DrinkCanSizePreset _lastPreset = DrinkCanSizePreset.Can350;

        [System.NonSerialized] Mesh _preview;
        [System.NonSerialized] GameObject _previewModel;

        public DrinkCanShape Shape => new DrinkCanShape(heightMm, diameterMm).Clamped;

        /// <summary>The label image's width : height as drawn — the source file's size, not the imported texture's (an image
        /// that is not a power of two is scaled to one on import by default, e.g. 2048 x 944 → 2048 x 1024)</summary>
        public float LabelAspect
        {
            get
            {
                if (labelTexture == null) return 2f;
                int w = labelTexture.width, h = labelTexture.height;
#if UNITY_EDITOR
                if (UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(labelTexture)) is UnityEditor.TextureImporter imp)
                {
                    imp.GetSourceTextureWidthAndHeight(out int sw, out int sh);
                    if (sw > 0 && sh > 0) { w = sw; h = sh; }
                }
#endif
                return h > 0 ? w / (float)h : 2f;
            }
        }

        /// <summary>Fills height/diameter from the preset; editing the size by hand switches the preset to Custom.</summary>
        public void SyncPreset()
        {
            if (sizePreset != _lastPreset && DrinkCanShape.TryGetPreset(sizePreset, out var p))
            {
                heightMm = p.heightMm;
                diameterMm = p.diameterMm;
            }
            else if (DrinkCanShape.TryGetPreset(sizePreset, out var cur)
                     && (!Mathf.Approximately(cur.heightMm, heightMm) || !Mathf.Approximately(cur.diameterMm, diameterMm)))
            {
                sizePreset = DrinkCanSizePreset.Custom;
            }
            _lastPreset = sizePreset;
        }

        /// <summary>The hand pose mode, settled first for a can saved before the mode existed (its pose untouched: Auto).</summary>
        public DrinkCanHandPoseMode HandPoseModeNow { get { MigrateHandPoseMode(); return handPoseMode; } }

        void MigrateHandPoseMode()
        {
            if (_handPoseModeSet) return;
            _handPoseModeSet = true;
            bool untouched = poseIndex == DefaultPoseIndex && poseMiddle == DefaultPoseMiddle && poseRing == DefaultPoseRing
                && poseLittle == DefaultPoseLittle && poseThumb == DefaultPoseThumb;
            handPoseMode = untouched ? DrinkCanHandPoseMode.Auto : DrinkCanHandPoseMode.Manual;
        }

        public Mesh BuildMesh(Mesh target = null) => DrinkCanMeshBuilder.Build(Shape, labelFit, LabelAspect, 64, target);

#if UNITY_EDITOR
        void OnEnable()
        {
            MigrateHandPoseMode();
            RebuildPreview();
        }

        void OnDisable() => ClearPreviewModel();

        void Reset() => _handPoseModeSet = true;

        void OnValidate()
        {
            MigrateHandPoseMode();
            SyncPreset();
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) RebuildPreview(); };
        }

        void OnDestroy()
        {
            ClearPreviewModel();
            if (_preview != null) DestroyImmediate(_preview);
        }

        void ClearPreviewModel()
        {
            if (_previewModel != null) DestroyImmediate(_previewModel);
            _previewModel = null;
        }

        public void RebuildPreview()
        {
            if (_preview == null) _preview = new Mesh { hideFlags = HideFlags.DontSave };
            BuildMesh(_preview);
            var mf = GetComponent<MeshFilter>();
            if (mf != null)
            {
                if (mf.sharedMesh != _preview) mf.sharedMesh = _preview;
                return;
            }
            if (!isActiveAndEnabled) return;
            ClearPreviewModel();
            var holder = new GameObject("_DrinkCanPreview") { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.SetParent(transform, false);
            var mats = new[] { metalMaterial, labelMaterial, liquidMaterial };
            var smr = DrinkCanModel.Create(holder.transform, _preview, DrinkCanMeshBuilder.GetRig(Shape), mats);
            foreach (var t in holder.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            if (labelMaterial != null)
            {
                var block = new MaterialPropertyBlock();
                if (labelTexture != null) block.SetTexture("_MainTex", labelTexture);
                else
                {
                    // no label: plain aluminium, as the build does (9-65)
                    block.SetTexture("_MainTex", Texture2D.whiteTexture);
                    if (metalMaterial != null)
                    {
                        block.SetColor("_Color", metalMaterial.GetColor("_Color"));
                        foreach (var p in new[] { "_Metallic", "_Smoothness", "_Reflectance" })
                            if (metalMaterial.HasProperty(p)) block.SetFloat(p, metalMaterial.GetFloat(p));
                    }
                }
                smr.SetPropertyBlock(block, DrinkCanMeshBuilder.SubmeshLabel);
            }
            if (liquidMaterial != null)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", liquidColor);
                smr.SetPropertyBlock(block, DrinkCanMeshBuilder.SubmeshLiquid);
            }
            _previewModel = holder;
        }
#endif
    }
}
