// Pyrite9 Drink Can — DrinkCan inspector (UI Toolkit, design 'Assay' — same look and layout as the wheelchair's WheelchairRootEditor)
//   about + features (fold) · preview in hand (DcHoldPreview) · 01 basics (menu language, spawn hand) · 02 can (size, label)
//   · 03 drink (colour, sips) · 04 sound (open / gulp / landing volume gauges) · 05 fine tuning (gauge + value: drag the gauge,
//   double-click resets) · 06 hand pose (muscle gauges, fingers in a fold; the preview's hand follows them) · 07 last build
//   (DcBuildRecord) · 08 changelog (fold — this version's line, read from the README '변경 기록 / Changelog / 更新履歴' section). Fold state in EditorPrefs.
//   Text follows the menu language. Tooltips start with the English name the README uses (Spawn Hand etc.).
//   Style: DrinkCanInspector.uss next to this script — without it everything still works (Unity's default look).
//   Button groups and value labels refresh in place every 300 ms (rebuilding would drop a gauge drag).
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Pyrite9.DrinkCan
{
    [CustomEditor(typeof(DrinkCan))]
    public class DrinkCanEditor : Editor
    {
        static readonly Color Gold = new Color32(0xE2, 0xCE, 0x74, 0xFF);

        VisualElement root;
        int lang = -1;
        string key;
        int secNo;
        readonly List<System.Action> live = new List<System.Action>();
        string Num() => (++secNo).ToString("00");

        // ---------------------------------------------------------------- text (English / Japanese / Korean = DrinkCanMenuLanguage order)
        static string T(int l, string en, string jp, string ko) => l == 1 ? jp : l == 2 ? ko : en;

        DrinkCan Can => (DrinkCan)target;

        public override VisualElement CreateInspectorGUI()
        {
            root = new VisualElement();
            root.AddToClassList("dc-root");
            var dir = EditorDir();
            var uss = dir != null ? AssetDatabase.LoadAssetAtPath<StyleSheet>(dir + "/DrinkCanInspector.uss") : null;
            if (uss != null) root.styleSheets.Add(uss);
            Build();
            root.schedule.Execute(Poll).Every(300);
            System.Action onChange = () => { if (root.panel != null) Build(); };
            DcHoldPreview.Changed += onChange;
            DcUpdateCheck.Changed += onChange;
            root.RegisterCallback<DetachFromPanelEvent>(_ => { DcHoldPreview.Changed -= onChange; DcUpdateCheck.Changed -= onChange; });
            DcUpdateCheck.CheckIfDue();
            return root;
        }

        // rebuild only for what changes the layout; everything else refreshes in place
        string Key()
        {
            var c = Can;
            return $"{(int)c.menuLanguage}|{DcBuildRecord.Stamp()}|{DcHoldPreview.Stale(c)}|{(c.labelTexture != null ? c.labelTexture.GetInstanceID() : 0)}"
                 + $"|{c.metalMaterial != null}{c.labelMaterial != null}{c.liquidMaterial != null}";
        }

        void Poll()
        {
            if (target == null) return;
            serializedObject.UpdateIfRequiredOrScript();
            if (Key() != key) { Build(); return; }
            foreach (var a in live) a();
        }

        void Build()
        {
            serializedObject.Update();
            lang = (int)Can.menuLanguage;
            key = Key();
            secNo = 0;
            live.Clear();
            root.Clear();
            root.Add(Header());
            var body = new VisualElement(); body.AddToClassList("dc-body");
            root.Add(body);
            body.Add(AboutSection());
            body.Add(PreviewBar());
            body.Add(Divider());
            body.Add(BasicSection());
            body.Add(Divider());
            body.Add(CanSection());
            body.Add(Divider());
            body.Add(DrinkSection());
            body.Add(Divider());
            body.Add(SoundSection());
            body.Add(Divider());
            body.Add(TuningSection());
            body.Add(Divider());
            body.Add(HandPoseSection());
            body.Add(Divider());
            body.Add(BuildSection());
            body.Add(Divider());
            body.Add(ChangelogSection());
            root.Bind(serializedObject);
        }

        // ---------------------------------------------------------------- header
        VisualElement Header()
        {
            var h = new VisualElement(); h.AddToClassList("dc-header");
            var left = new VisualElement(); left.AddToClassList("dc-header__left");
            var glyph = new VisualElement(); glyph.AddToClassList("dc-glyph");
            glyph.generateVisualContent += DrawGlyph;
            left.Add(glyph);
            var titles = new VisualElement(); titles.AddToClassList("dc-header__titles");
            titles.Add(Label("PYRITE9", "dc-kicker"));
            titles.Add(Label("Drink Can", "dc-title"));
            left.Add(titles);
            h.Add(left);
            h.Add(VersionBox());
            return h;
        }

        // version + new version check (DcUpdateCheck): click the version to check again, click the new-version line for the release page
        VisualElement VersionBox()
        {
            var box = new VisualElement(); box.AddToClassList("dc-versionbox");
            var ver = Label("v" + DrinkCanConst.Version, "dc-version");
            ver.tooltip = T(lang, "Click to check for updates", "クリックで更新を確認", "누르면 새 버전 확인");
            ver.RegisterCallback<ClickEvent>(_ => DcUpdateCheck.Check());
            box.Add(ver);
            Label st;
            switch (DcUpdateCheck.Current)
            {
                case DcUpdateCheck.State.Checking: st = Label(T(lang, "checking…", "確認中…", "확인 중…"), "dc-update"); break;
                case DcUpdateCheck.State.Latest: st = Label(T(lang, "up to date", "最新", "최신"), "dc-update"); break;
                case DcUpdateCheck.State.Failed: st = Label(T(lang, "couldn't check", "確認できません", "확인 못 함"), "dc-update"); break;
                case DcUpdateCheck.State.Newer:
                    st = Label(T(lang, DcUpdateCheck.LatestTag + " available", DcUpdateCheck.LatestTag + " があります", DcUpdateCheck.LatestTag + " 있음"), "dc-update");
                    st.AddToClassList("dc-update--new");
                    st.tooltip = DcUpdateCheck.LatestUrl;
                    st.RegisterCallback<ClickEvent>(_ => Application.OpenURL(DcUpdateCheck.LatestUrl));
                    break;
                default: st = null; break;
            }
            if (st != null) box.Add(st);
            return box;
        }

        // can side view (34x34): top ellipse with the tab, body sides, bottom half-ellipse
        static void DrawGlyph(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            p.strokeColor = Gold;
            p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            void Ellipse(float cx, float cy, float rx, float ry, float a0, float a1, float w)
            {
                p.lineWidth = w;
                p.BeginPath();
                const int n = 24;
                for (int i = 0; i <= n; i++)
                {
                    float a = Mathf.Lerp(a0, a1, i / (float)n) * Mathf.Deg2Rad;
                    var v = new Vector2(cx + rx * Mathf.Cos(a), cy + ry * Mathf.Sin(a));
                    if (i == 0) p.MoveTo(v); else p.LineTo(v);
                }
                p.Stroke();
            }
            Ellipse(17f, 7f, 9f, 3f, 0f, 360f, 1.5f);      // lid rim
            Ellipse(17f, 27f, 9f, 3f, 0f, 180f, 1.5f);     // bottom (front half)
            p.lineWidth = 1.5f;
            p.BeginPath(); p.MoveTo(new Vector2(8f, 7f)); p.LineTo(new Vector2(8f, 27f)); p.Stroke();
            p.BeginPath(); p.MoveTo(new Vector2(26f, 7f)); p.LineTo(new Vector2(26f, 27f)); p.Stroke();
            p.lineWidth = 1.2f;
            p.BeginPath(); p.MoveTo(new Vector2(14f, 7f)); p.LineTo(new Vector2(20.5f, 7f)); p.Stroke();   // tab
            p.lineWidth = 0.8f;
            Ellipse(17f, 13f, 9f, 2f, 0f, 180f, 0.8f);     // label band
        }

        // ---------------------------------------------------------------- about + features
        // short inspector text (the README 'Features' section is longer). Change both together.
        static readonly string[][] Features =
        {
            new[] { "Take out, open and drink — menu buttons on desktop; in VR open the tab with your other index finger and drink by raising the can to your mouth",
                    "取り出す・開ける・飲む — デスクトップはメニューのボタン、VR は反対の手の人差し指でプルタブを起こし、缶を口に当てて飲む",
                    "꺼내기·따기·마시기 — 데스크톱은 메뉴 버튼, VR은 반대 손 검지로 따개를 올려 따고 캔을 입에 대서 마심" },
            new[] { "The drink stays level as you tilt the can and goes down with each sip",
                    "缶を傾けても飲み物は水平のまま、一口ごとに減る",
                    "캔을 기울여도 음료는 수평, 한 모금마다 줄어듦" },
            new[] { "Beer head (foam dome when opened) or fizz, with your own foam colour",
                    "ビールの泡（開けると泡のドーム）または炭酸、泡の色も自由に",
                    "맥주 거품(딸 때 거품 돔) 또는 탄산, 거품 색도 마음대로" },
            new[] { "Swing an empty can to throw it — it rolls on the floor (or turn Take out off)",
                    "空き缶は振って投げる — 床を転がる（「取り出す」をオフでも）",
                    "빈 캔은 휘둘러 던짐 — 바닥을 구름(꺼내기를 꺼도 됨)" },
            new[] { "Switch hands from the menu while playing",
                    "プレイ中にメニューで持ち替え",
                    "실행 중 메뉴로 손 바꾸기" },
            new[] { "Your own size (four presets or any height and diameter), label image and drink colour",
                    "大きさ（プリセット 4 種または高さ・直径を自由に）・ラベル画像・飲み物の色を自由に",
                    "크기(프리셋 4종 또는 높이·지름 마음대로)·라벨 이미지·음료 색을 마음대로" },
            new[] { "Fits the avatar's hand at build time — the fingers close round the can",
                    "ビルド時にアバターの手に合わせる — 指が缶を握る",
                    "빌드 때 아바타 손에 맞춤 — 손가락이 캔을 감쌈" },
            new[] { "Aluminium that reflects the world around it (lilToon, reflection probes)",
                    "周りのワールドを映すアルミ（lilToon・リフレクションプローブ）",
                    "주변 월드를 비추는 알루미늄(lilToon·반사 프로브)" },
            new[] { "Seen by everyone — 12 synced bits",
                    "みんなに見える — 同期 12 bit",
                    "모두에게 보임 — 동기화 12 bit" },
        };

        VisualElement AboutSection()
        {
            var sec = new VisualElement(); sec.AddToClassList("dc-section");
            sec.Add(Label(T(lang, "Drop it into a humanoid avatar — it fits itself to that avatar's hand when you build. Your scene is not changed.",
                "ヒューマノイドのアバターに入れるだけで、ビルド時にそのアバターの手に合わせます。シーンは変わりません。",
                "휴머노이드 아바타에 넣기만 하면 빌드할 때 그 아바타의 손에 맞춰집니다. 씬은 바뀌지 않습니다."), "dc-intro"));
            var list = new VisualElement(); list.AddToClassList("dc-features");
            foreach (var f in Features)
            {
                var row = new VisualElement(); row.AddToClassList("dc-feature");
                var dot = new VisualElement(); dot.AddToClassList("dc-feature__dot"); row.Add(dot);
                row.Add(Label(f[Mathf.Clamp(lang, 0, 2)], "dc-feature__text"));
                list.Add(row);
            }
            sec.Add(Fold("Pyrite9DC.ShowFeatures", true, T(lang, "FEATURES", "機能", "기능"), list));
            return sec;
        }

        // fold: click the head (small title + chevron), state in EditorPrefs
        static VisualElement Fold(string prefKey, bool defaultOpen, string title, VisualElement content)
        {
            var box = new VisualElement(); box.AddToClassList("dc-fold");
            var head = new VisualElement(); head.AddToClassList("dc-fold__head");
            head.Add(Label(title, "dc-fold__title"));
            var chev = new VisualElement(); chev.AddToClassList("dc-fold__chevron");
            bool open = EditorPrefs.GetBool(prefKey, defaultOpen);
            chev.generateVisualContent += mgc =>
            {
                var p = mgc.painter2D;
                p.strokeColor = new Color32(0x7D, 0x83, 0x91, 0xFF); p.lineWidth = 1.5f; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                p.BeginPath();
                if (open) { p.MoveTo(new Vector2(2f, 4f)); p.LineTo(new Vector2(5f, 7f)); p.LineTo(new Vector2(8f, 4f)); }
                else { p.MoveTo(new Vector2(4f, 2f)); p.LineTo(new Vector2(7f, 5f)); p.LineTo(new Vector2(4f, 8f)); }
                p.Stroke();
            };
            head.Add(chev);
            box.Add(head);
            box.Add(content);
            content.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            head.RegisterCallback<ClickEvent>(_ =>
            {
                open = !open;
                EditorPrefs.SetBool(prefKey, open);
                content.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                chev.MarkDirtyRepaint();
            });
            return box;
        }

        // ---------------------------------------------------------------- preview in hand
        VisualElement PreviewBar()
        {
            var can = Can;
            var desc = can.GetComponentInParent<VRCAvatarDescriptor>(true);
            bool on = desc != null && DcHoldPreview.IsActiveFor(desc.gameObject);
            bool stale = DcHoldPreview.Stale(can);
            var bar = new VisualElement(); bar.AddToClassList("dc-preview");
            var row = new VisualElement(); row.AddToClassList("dc-preview__row");
            string label = DcHoldPreview.Busy ? T(lang, "Building…", "作成中…", "만드는 중…")
                : on ? T(lang, "Preview in hand — on", "手に持った姿のプレビュー — オン", "손에 든 모습 미리보기 — 켜짐")
                : T(lang, "Preview in hand", "手に持った姿をプレビュー", "손에 든 모습 미리보기");
            var btn = new Button(() =>
            {
                if (on) DcHoldPreview.Stop();
                else DcHoldPreview.Start(can);
            }) { text = label };
            btn.AddToClassList("dc-preview__toggle");
            if (on) btn.AddToClassList("dc-preview__toggle--on");
            btn.SetEnabled(desc != null && !DcHoldPreview.Busy && !EditorApplication.isPlaying);
            row.Add(btn);
            if (on && stale)
            {
                var re = new Button(() => DcHoldPreview.Start(can)) { text = T(lang, "Refresh", "更新", "새로 고침") };
                re.AddToClassList("dc-button"); re.AddToClassList("dc-preview__refresh");
                row.Add(re);
            }
            bar.Add(row);
            string note = desc == null ? T(lang, "Available when the can is inside an avatar.", "アバターの中にあるときに使えます。", "아바타 안에 있을 때 쓸 수 있습니다.")
                : DcHoldPreview.LastError != null ? DcHoldPreview.LastError
                : on && stale ? T(lang, "Settings changed — refresh to see them.", "設定が変わりました。更新すると反映されます。", "설정이 바뀌었습니다. 새로 고침하면 반영됩니다.")
                : on ? T(lang, "A built copy holding the can in the spawn hand. Scene view only, never saved. The avatar is hidden in the Scene view until you turn this off.",
                         "ビルドしたコピーが出す手で缶を持った姿です。Scene ビューのみ・保存されません。オフにするまでアバターは Scene ビューで隠れます。",
                         "빌드한 사본이 꺼내는 손에 캔을 든 모습입니다. Scene 뷰에서만 보이고 저장되지 않습니다. 끌 때까지 아바타는 Scene 뷰에서 숨겨집니다.")
                : T(lang, "Builds a copy with your current settings and puts the can in its hand (takes a few seconds).",
                      "今の設定でコピーをビルドして、缶を手に持たせます（数秒かかります）。",
                      "지금 설정으로 사본을 빌드해 손에 캔을 쥐여 봅니다(몇 초 걸립니다).");
            var n = Label(note, "dc-note"); n.AddToClassList("dc-preview__note");
            if (DcHoldPreview.LastError != null) n.AddToClassList("dc-fact__value--warn");
            else if (on && stale) n.AddToClassList("dc-fact__value--gold");
            bar.Add(n);
            return bar;
        }

        // ---------------------------------------------------------------- 01 basics
        VisualElement BasicSection()
        {
            var sec = Section(Num(), T(lang, "BASICS", "基本", "기본"), null);
            var grid = new VisualElement(); grid.AddToClassList("dc-rows");
            sec.Add(grid);
            grid.Add(Row(T(lang, "Menu language", "メニュー言語", "메뉴 언어"),
                "Menu Language — " + T(lang, "Expression Menu labels, applied at build.", "Expression Menu の表示。ビルド時に反映。", "Expression Menu 표시. 빌드 때 적용."),
                Seg("menuLanguage", new[] { 0, 1, 2 }, new[] { "EN", "日本語", "한국어" })));
            grid.Add(Row(T(lang, "Spawn hand", "出す手", "꺼내는 손"),
                "Spawn Hand — " + T(lang, "The hand the can appears in when you take it out. 'Switch hand' in the menu changes it while playing.",
                    "取り出したとき最初に持つ手。メニューの「持ち替え」でプレイ中も変えられます。",
                    "캔을 꺼낼 때 처음 쥐어지는 손. 메뉴 '손 바꾸기'로 실행 중에도 바꿀 수 있습니다."),
                Seg("hand", new[] { (int)DrinkCanHand.Right, (int)DrinkCanHand.Left },
                    new[] { T(lang, "Right hand", "右手", "오른손"), T(lang, "Left hand", "左手", "왼손") })));
            sec.Add(Label(T(lang, "A hand switched from the menu goes back to this one when the avatar is reloaded.",
                "メニューで持ち替えた手は、アバターを読み込み直すとこの手に戻ります。",
                "메뉴로 바꾼 손은 다음에 아바타를 불러올 때 이 값으로 돌아갑니다."), "dc-note"));
            sec.Add(Label(T(lang, "In VR you open the tab with the other hand's index finger.",
                "VR では反対の手の人差し指でプルタブを起こします。",
                "VR에서 따기는 반대 손 검지로 따개를 건드립니다."), "dc-note"));
            var grip = new VisualElement(); grip.AddToClassList("dc-rows"); grip.style.marginTop = 8;
            grip.Add(Row(T(lang, "Hand pose", "手の形", "쥐는 손 모양"),
                "Grip Hand Pose — " + T(lang, "While holding, that hand's fingers close round the can (fitted to the avatar at build). Finger tracking and gestures of that hand pause while holding.",
                    "持っている間、その手の指が缶を握ります（ビルド時にアバターに合わせる）。持っている間はその手の指トラッキング・ジェスチャーが止まります。",
                    "들고 있는 동안 그 손가락이 캔을 감쌉니다(빌드 때 아바타에 맞춤). 들고 있는 동안 그 손의 손가락 추적·제스처는 멈춥니다."),
                Check("gripHandPose", T(lang, "Close the fingers round the can", "指で缶を握る", "손가락으로 캔을 감싸기"))));
            sec.Add(grip);
            return sec;
        }

        // check + text. The checkmark is drawn by hand (Unity's default image covers the gold fill)
        static VisualElement Check(string path, string text)
        {
            var box = new VisualElement(); box.AddToClassList("dc-check");
            var tg = new Toggle { bindingPath = path };
            tg.AddToClassList("dc-toggle");
            var mark = tg.Q(className: "unity-toggle__checkmark");
            if (mark != null)
            {
                mark.generateVisualContent += mgc =>
                {
                    if (!tg.value) return;
                    var p = mgc.painter2D;
                    p.strokeColor = new Color32(0x15, 0x17, 0x1C, 0xFF);
                    p.lineWidth = 2f; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                    p.BeginPath();
                    p.MoveTo(new Vector2(3.5f, 7.5f)); p.LineTo(new Vector2(6.5f, 10.5f)); p.LineTo(new Vector2(11.5f, 4.5f));
                    p.Stroke();
                };
                tg.RegisterValueChangedCallback(_ => mark.MarkDirtyRepaint());
            }
            box.Add(tg);
            box.Add(Label(text, "dc-check__text"));
            return box;
        }

        // ---------------------------------------------------------------- 02 can
        VisualElement CanSection()
        {
            var sec = Section(Num(), T(lang, "CAN", "缶", "캔"), "mm");
            var grid = new VisualElement(); grid.AddToClassList("dc-rows");
            sec.Add(grid);
            grid.Add(Stack(Row(T(lang, "Size", "サイズ", "크기"),
                "Size Preset — " + T(lang, "Fills height and diameter. Changing them by hand switches to Custom.", "高さ・直径を埋めます。手で変えると「カスタム」に。", "높이·지름을 채웁니다. 직접 바꾸면 '직접'으로."),
                Seg("sizePreset",
                    new[] { (int)DrinkCanSizePreset.Can350, (int)DrinkCanSizePreset.Can500, (int)DrinkCanSizePreset.Slim250, (int)DrinkCanSizePreset.Coffee190, (int)DrinkCanSizePreset.Custom },
                    new[] { "350", "500", T(lang, "250 slim", "250 スリム", "250 슬림"), T(lang, "190 coffee", "190 コーヒー", "190 커피"), T(lang, "Custom", "カスタム", "직접") }, true))));
            grid.Add(GaugeRow("heightMm", T(lang, "Height", "高さ", "높이"),
                "Height — " + T(lang, "mm. Double-click: the preset's value.", "mm。ダブルクリックでプリセットの値。", "mm. 두 번 누르면 프리셋 값."),
                DrinkCanShape.MinHeightMm, DrinkCanShape.MaxHeightMm, float.NaN, () => PresetShape().heightMm, 0.5f, "0.0"));
            grid.Add(GaugeRow("diameterMm", T(lang, "Diameter", "直径", "지름"),
                "Diameter — " + T(lang, "mm. Double-click: the preset's value.", "mm。ダブルクリックでプリセットの値。", "mm. 두 번 누르면 프리셋 값."),
                DrinkCanShape.MinDiameterMm, DrinkCanShape.MaxDiameterMm, float.NaN, () => PresetShape().diameterMm, 0.5f, "0.0"));

            // label texture + open the template
            var lrow = new VisualElement(); lrow.AddToClassList("dc-field-row");
            var obj = new ObjectField { bindingPath = "labelTexture", objectType = typeof(Texture2D), allowSceneObjects = false };
            obj.AddToClassList("dc-field");
            lrow.Add(obj);
            var tb = new Button(() => { var tpl = TemplatePath(); if (tpl != null) EditorUtility.OpenWithDefaultApp(tpl); })
                { text = T(lang, "Open template", "テンプレートを開く", "템플릿 열기") };
            tb.AddToClassList("dc-button");
            tb.SetEnabled(TemplatePath() != null);
            lrow.Add(tb);
            var gb = new Button(() => { var g = LabelGuidePath(); if (g != null) EditorUtility.OpenWithDefaultApp(g); })
                { text = T(lang, "Label guide", "ラベルの作り方", "라벨 가이드") };
            gb.AddToClassList("dc-button");
            gb.SetEnabled(LabelGuidePath() != null);
            lrow.Add(gb);
            grid.Add(Row(T(lang, "Label", "ラベル", "라벨"),
                "Label Texture — " + T(lang, "Wraps around the straight body. Empty: plain aluminium. The template (for the chosen size) shows the safe area and the seam; drawn at its aspect, Stretch does not distort.",
                    "まっすぐな胴に巻きます。空ならアルミのまま。テンプレート（選んだ大きさ用）に安全範囲と継ぎ目があります。その縦横比で描けば引き伸ばしでもゆがみません。",
                    "곧은 몸통에 감깁니다. 비우면 알루미늄 그대로. 템플릿(고른 크기용)에 안전 영역과 이음매가 있고, 그 비율로 그리면 늘림에서도 찌그러지지 않습니다."), lrow));
            grid.Add(Row(T(lang, "Label fit", "ラベルの合わせ方", "라벨 맞춤"),
                "Label Fit — " + T(lang, "Stretch fills the body; Keep aspect leaves bands; Repeat tiles the image around the can.",
                    "引き伸ばし＝胴いっぱい、比率維持＝余白が出る、繰り返し＝周りに並べる。",
                    "늘림 = 몸통 가득, 비율 유지 = 여백이 생김, 반복 = 둘레에 이어 붙임."),
                Seg("labelFit", new[] { (int)DrinkCanLabelFit.Stretch, (int)DrinkCanLabelFit.KeepAspect, (int)DrinkCanLabelFit.Repeat },
                    new[] { T(lang, "Stretch", "引き伸ばし", "늘림"), T(lang, "Keep aspect", "比率維持", "비율 유지"), T(lang, "Repeat", "繰り返し", "반복") })));

            // aspect note: body (circumference : straight height) vs. the label image
            var aspect = Label("", "dc-note");
            void ShowAspect()
            {
                var c = Can;
                if (c.labelTexture == null || c.labelFit != DrinkCanLabelFit.Stretch) { aspect.style.display = DisplayStyle.None; return; }
                var d = DrinkCanMeshBuilder.GetDims(c.Shape);
                float body = 2f * Mathf.PI * d.R / Mathf.Max(1f, d.bodyY1 - d.bodyY0);
                float img = c.LabelAspect;
                float diff = Mathf.Abs(img / body - 1f);
                if (diff < 0.05f) { aspect.style.display = DisplayStyle.None; return; }
                aspect.style.display = DisplayStyle.Flex;
                string much = diff < 0.2f ? T(lang, "a little", "少し", "조금") : T(lang, "noticeably", "かなり", "꽤");
                aspect.text = T(lang, $"This body is {body:0.00}:1, the label is {img:0.00}:1 — Stretch distorts it {much}.",
                    $"この大きさの胴は {body:0.00}:1、ラベルは {img:0.00}:1 — 引き伸ばしで{much}ゆがみます。",
                    $"이 크기 몸통은 {body:0.00}:1, 라벨은 {img:0.00}:1 — 늘림은 {much} 늘어납니다.");
            }
            ShowAspect();
            live.Add(ShowAspect);
            sec.Add(aspect);
            return sec;
        }

        DrinkCanShape PresetShape()
        {
            if (DrinkCanShape.TryGetPreset(Can.sizePreset, out var p)) return p;
            DrinkCanShape.TryGetPreset(DrinkCanSizePreset.Can350, out p);
            return p;
        }

        // ---------------------------------------------------------------- 03 drink
        VisualElement DrinkSection()
        {
            var sec = Section(Num(), T(lang, "DRINK", "飲み物", "음료"), null);
            var grid = new VisualElement(); grid.AddToClassList("dc-rows");
            sec.Add(grid);
            var color = new ColorField { bindingPath = "liquidColor", showAlpha = false };
            color.AddToClassList("dc-field");
            grid.Add(Row(T(lang, "Drink colour", "飲み物の色", "액체 색"),
                "Liquid Color — " + T(lang, "Seen through the opening.", "飲み口から見える色。", "마시는 입구로 보이는 색."), color));

            var step = new VisualElement(); step.AddToClassList("dc-stepper");
            var value = Label("", "dc-stepper__value");
            void Bump(int d)
            {
                var p = serializedObject.FindProperty("sips");
                p.intValue = Mathf.Clamp(p.intValue + d, 3, 10);
                serializedObject.ApplyModifiedProperties();
                value.text = p.intValue.ToString();
            }
            var minus = new Button(() => Bump(-1)) { text = "−" }; minus.AddToClassList("dc-button");
            var plus = new Button(() => Bump(1)) { text = "+" }; plus.AddToClassList("dc-button");
            step.Add(minus); step.Add(value); step.Add(plus);
            void ShowSips() => value.text = serializedObject.FindProperty("sips").intValue.ToString();
            ShowSips();
            live.Add(ShowSips);
            grid.Add(Row(T(lang, "Sips", "飲む回数", "모금 수"),
                "Sips — " + T(lang, "Sips until the can is empty (3–10).", "空になるまでの回数（3〜10）。", "빌 때까지 모금 수(3~10)."), step));

            grid.Add(Row(T(lang, "Foam", "泡", "거품"),
                "Foam — " + T(lang, "Beer head: a foam layer on the drink and foam welling up when opened. Fizz: droplets when opened (cola, cider). None: no foam.",
                    "ビールの泡：飲み物の上の泡の層と、開けたときにあふれる泡。炭酸：開けたときに飛ぶしぶき（コーラ・サイダー）。なし：泡なし。",
                    "맥주 거품: 액체 위 거품층 + 딸 때 넘치는 거품. 탄산: 딸 때 튀는 방울(콜라·사이다). 없음: 거품 없음."),
                Seg("foam", new[] { (int)DrinkCanFoam.None, (int)DrinkCanFoam.Bubbles, (int)DrinkCanFoam.Head },
                    new[] { T(lang, "None", "なし", "없음"), T(lang, "Fizz", "炭酸", "탄산"), T(lang, "Beer head", "ビールの泡", "맥주 거품") })));
            var foamColor = new ColorField { bindingPath = "foamColor", showAlpha = false };
            foamColor.AddToClassList("dc-field");
            var foamRow = Row(T(lang, "Foam colour", "泡の色", "거품 색"),
                "Foam Color — " + T(lang, "The foam layer and the foam / droplets when opened.", "泡の層と、開けたときの泡・しぶきの色。", "거품층과 딸 때 거품·방울의 색."), foamColor);
            grid.Add(foamRow);
            void ShowFoam() => foamRow.style.display = Can.foam == DrinkCanFoam.None ? DisplayStyle.None : DisplayStyle.Flex;
            ShowFoam();
            live.Add(ShowFoam);
            return sec;
        }

        // ---------------------------------------------------------------- 04 sound
        VisualElement SoundSection()
        {
            var sec = Section(Num(), T(lang, "SOUND", "サウンド", "소리"), null);
            var grid = new VisualElement(); grid.AddToClassList("dc-rows");
            sec.Add(grid);
            grid.Add(GaugeRow("openVolume", T(lang, "Opening", "開ける音", "따는 소리"),
                "Open Volume — " + T(lang, "The tab and the pop when opened. Everyone nearby hears it.", "プルタブを起こして開ける音。周りの人にも聞こえます。", "따개를 올려 따는 소리. 주변 사람에게도 들립니다."),
                0f, 1f, float.NaN, () => 1f, 0.05f, "0.00"));
            grid.Add(GaugeRow("sipVolume", T(lang, "Gulp", "飲む音", "꿀꺽 소리"),
                "Sip Volume — " + T(lang, "The gulp on each sip. Only you hear it.", "一口ごとの飲む音。自分にだけ聞こえます。", "한 모금마다 나는 소리. 나에게만 들립니다."),
                0f, 1f, float.NaN, () => 0.5f, 0.05f, "0.00"));
            grid.Add(GaugeRow("dropVolume", T(lang, "Landing", "着地音", "착지 소리"),
                "Drop Volume — " + T(lang, "A tossed can hitting the floor. Everyone nearby hears it.", "投げた缶が床に落ちる音。周りの人にも聞こえます。", "던진 캔이 바닥에 떨어지는 소리. 주변 사람에게도 들립니다."),
                0f, 1f, float.NaN, () => 1f, 0.05f, "0.00"));
            sec.Add(Label(T(lang, "1 = the loudest VRChat lets an avatar play, 0 = off. Full volume up to 2 m, silent beyond 5 m. Double-click a gauge to reset it.",
                "1 = VRChat のアバターで出せる最大、0 = 鳴らさない。2 m まではそのまま、5 m より先は聞こえません。ゲージをダブルクリックで元に戻ります。",
                "1 = VRChat 아바타가 낼 수 있는 최대, 0 = 끔. 2 m까지는 그대로, 5 m 밖에서는 안 들립니다. 게이지를 더블클릭하면 기본값으로."), "dc-note"));
            return sec;
        }

        // ---------------------------------------------------------------- 05 fine tuning
        VisualElement TuningSection()
        {
            var sec = Section(Num(), T(lang, "FINE TUNING", "微調整", "미세 조정"), "m");
            var grid = new VisualElement(); grid.AddToClassList("dc-rows");
            sec.Add(grid);
            grid.Add(GaugeRow("sizeScale", T(lang, "Size scale", "大きさの倍率", "크기 배율"),
                "Size Scale — " + T(lang, "Can size relative to the hand. 1 = a real can in a real-sized hand.", "手に対する缶の大きさ。1 = 実物の手に実物の缶。", "손에 대한 캔 크기. 1 = 실제 손에 실제 캔."),
                0.5f, 2f, 1f, () => 1f, 0.01f, "0.00"));
            grid.Add(GaugeRow("gripOffset.x", T(lang, "Off the palm", "手のひらから外へ", "손바닥 바깥"),
                "Grip Offset X — " + T(lang, "+ moves the can away from the palm", "+ で手のひらから離す", "+는 손바닥에서 멀리"),
                -0.05f, 0.05f, 0f, () => 0f, 0.001f, "+0.000;-0.000;0.000"));
            grid.Add(GaugeRow("gripOffset.y", T(lang, "Toward fingertips", "指先の方へ", "손끝 쪽"),
                "Grip Offset Y — " + T(lang, "+ toward the fingertips", "+ で指先の方へ", "+는 손끝 쪽으로"),
                -0.05f, 0.05f, 0f, () => 0f, 0.001f, "+0.000;-0.000;0.000"));
            grid.Add(GaugeRow("gripOffset.z", T(lang, "Toward thumb", "親指の方へ", "엄지 쪽"),
                "Grip Offset Z — " + T(lang, "+ toward the thumb (the can's top)", "+ で親指（缶の上）の方へ", "+는 엄지(캔 위) 쪽으로"),
                -0.05f, 0.05f, 0f, () => 0f, 0.001f, "+0.000;-0.000;0.000"));
            grid.Add(GaugeRow("gripTwist", T(lang, "Twist", "回転", "돌림"),
                "Grip Twist — " + T(lang, "Turns the can around its own axis (degrees).", "缶を軸のまわりに回す（度）。", "캔을 축 둘레로 돌림(도)."),
                -180f, 180f, 0f, () => 0f, 1f, "+0;-0;0"));
            grid.Add(GaugeRow("gripTilt.x", T(lang, "Tilt to fingertips", "指先へ傾ける", "손끝 쪽 기울임"),
                "Grip Tilt X — " + T(lang, "+ leans the can's top toward the fingertips, - toward the wrist (degrees, about its middle).",
                    "+ で缶の上を指先へ、- で手首へ傾ける（度、缶の中ほどを軸に）。", "+는 캔 위쪽을 손끝 쪽, -는 손목 쪽으로 기울임(도, 캔 가운데 기준)."),
                -45f, 45f, 0f, () => 0f, 1f, "+0;-0;0"));
            grid.Add(GaugeRow("gripTilt.y", T(lang, "Tilt off the palm", "手のひらから傾ける", "손바닥 바깥 기울임"),
                "Grip Tilt Y — " + T(lang, "+ leans the can's top away from the palm, - into it (degrees, about its middle).",
                    "+ で缶の上を手のひらから離し、- で寄せる（度、缶の中ほどを軸に）。", "+는 캔 위쪽을 손바닥에서 멀리, -는 손바닥 쪽으로 기울임(도, 캔 가운데 기준)."),
                -45f, 45f, 0f, () => 0f, 1f, "+0;-0;0"));
            sec.Add(Label(T(lang, "Offsets are in hand space, so they mirror for the other hand.",
                "手の向きが基準なので、反対の手では左右対称になります。",
                "손 기준이라 반대 손에는 좌우 대칭으로 적용됩니다."), "dc-note"));
            return sec;
        }

        // ---------------------------------------------------------------- 06 hand pose
        // the holding hand as humanoid muscles (DrinkCan.pose*). The preview in hand re-poses at once; the can's seat is
        // refitted on Refresh / build. An avatar it does not fit gets no pose at build (warning under Last build)
        VisualElement HandPoseSection()
        {
            var sec = Section(Num(), T(lang, "HAND POSE", "手の形", "쥐는 손 모양"), null);
            var modeRow = new VisualElement(); modeRow.AddToClassList("dc-rows");
            modeRow.Add(Row(T(lang, "Fit", "合わせ方", "맞춤"),
                "Hand Pose Mode — " + T(lang, "Auto fits the hand and the can's seat to this avatar at build. Manual uses the sliders below.",
                    "自動はビルド時にこのアバターの手に合わせて手の形と缶の位置を決めます。手動は下のスライダーを使います。",
                    "자동은 빌드 때 이 아바타 손에 맞춰 손 모양과 캔 자리를 찾습니다. 수동은 아래 슬라이더 값을 씁니다."),
                Seg("handPoseMode", new[] { (int)DrinkCanHandPoseMode.Auto, (int)DrinkCanHandPoseMode.Manual },
                    new[] { T(lang, "Auto", "自動", "자동"), T(lang, "Manual", "手動", "수동") })));
            sec.Add(modeRow);
            var modeNote = Label("", "dc-note");
            sec.Add(modeNote);
            void ShowMode()
            {
                bool a = Can.handPoseMode == DrinkCanHandPoseMode.Auto;
                modeNote.text = a
                    ? T(lang, "Auto: each finger is curled until it lies on the can, the thumb is laid round it, the can goes where the hand closes best. Takes a few seconds per build (up to ~20 on detailed hands). If no grip is found, the Manual pose is used. Moving a slider switches to Manual, starting from what Auto found (Refresh the preview first).",
                        "自動: 指を一本ずつ缶に沿うまで曲げ、親指を缶にまわし、手が一番よく閉じる位置に缶を置きます。ビルドごとに数秒（細かい手で最大20秒ほど）。握れる形が見つからないときは手動の形を使います。スライダーを動かすと手動になり、自動の結果から始まります（先にプレビューを更新）。",
                        "자동: 손가락을 하나씩 캔에 닿을 때까지 굽히고, 엄지를 캔에 두르고, 손이 가장 잘 감기는 자리에 캔을 놓습니다. 빌드마다 몇 초(세밀한 손은 최대 20초 정도). 쥐는 모양을 못 찾으면 수동 모양을 씁니다. 슬라이더를 움직이면 수동으로 바뀌고, 자동 결과에서 시작합니다(먼저 미리보기 새로 고침).")
                    : T(lang, "Manual: −1 = fully curled, 0 = rest, +1 = bent back. When the build says the pose does not fit, adjust here with the preview on.",
                        "手動: −1 = 曲げきる、0 = 基本、+1 = 反らす。ビルドで「合わない」と出たら、プレビューを見ながらここで直してください。",
                        "수동: −1 = 다 굽힘, 0 = 기본, +1 = 젖힘. 빌드에서 '맞지 않음'이 뜨면 미리보기를 켜고 여기서 조절하세요.");
            }
            ShowMode();
            live.Add(ShowMode);

            // a rig with its own hand (DrinkCanGripFit.Profile, Lapwing): its pose is the default here, and an untouched can
            // gets it written in so the sliders show what the build uses
            var desc = Can.GetComponentInParent<VRCAvatarDescriptor>(true);
            var prof = DrinkCanGripFit.ProfileFor(desc != null ? desc.GetComponent<Animator>() : null);
            Vector4 dIndex = prof?.index ?? DrinkCan.DefaultPoseIndex, dMiddle = prof?.middle ?? DrinkCan.DefaultPoseMiddle, dRing = prof?.ring ?? DrinkCan.DefaultPoseRing,
                dLittle = prof?.little ?? DrinkCan.DefaultPoseLittle, dThumb = prof?.thumb ?? DrinkCan.DefaultPoseThumb;
            bool poseByUs = false;   // SetPose / import: not the user moving a slider
            void SetPose(string undo)
            {
                poseByUs = true;
                Undo.RecordObject(Can, undo);
                Can.poseIndex = dIndex; Can.poseMiddle = dMiddle; Can.poseRing = dRing; Can.poseLittle = dLittle; Can.poseThumb = dThumb;
                PrefabUtility.RecordPrefabInstancePropertyModifications(Can);
                EditorUtility.SetDirty(Can);
                serializedObject.Update();
            }
            if (prof != null && Can.handPoseMode == DrinkCanHandPoseMode.Manual)
            {
                if (DrinkCanGripFit.IsDefaultPose(Can)) SetPose("Apply " + prof.name + " hand pose");
                sec.Add(Label(T(lang, $"{prof.name} hand recognised — its own pose and can position are used (the default below is the {prof.name} pose).",
                    $"{prof.name} の手を認識しました — 専用の手の形と缶の位置を使います（下の初期値は {prof.name} 用）。",
                    $"{prof.name} 손을 알아봤습니다 — 전용 손 모양과 캔 자리를 씁니다(아래 기본값은 {prof.name}용)."), "dc-note"));
            }

            // with the preview on: does the pose fit this avatar? (the build turns it off when the can cannot sit against the palm)
            if (desc != null && DcHoldPreview.IsActiveFor(desc.gameObject) && Can.gripHandPose && !DcHoldPreview.HandFits)
                sec.Add(Warn(T(lang, "This pose does not fit this avatar — the build leaves the hand to the controller. Adjust it (the preview shows it), then Refresh to check.",
                    "この形はこのアバターに合いません — ビルドでは手はコントローラーのままになります。調整して（プレビューに出ます）、更新で確認してください。",
                    "이 모양은 이 아바타에 맞지 않습니다 — 빌드에서는 손이 컨트롤러 그대로입니다. 조절한 뒤(미리보기에 보임) 새로 고침으로 확인하세요."),
                    desc.gameObject.name + "/PreviewPoseNotFit"));

            var thumb = new VisualElement(); thumb.AddToClassList("dc-rows");
            sec.Add(thumb);
            string tipT = T(lang, "Humanoid muscle. Double-click: default.", "ヒューマノイドの筋肉値。ダブルクリックで初期値。", "휴머노이드 근육 값. 두 번 누르면 기본값.");
            thumb.Add(GaugeRow("poseThumb.x", T(lang, "Thumb spread", "親指の開き", "엄지 벌림"),
                "Thumb Spread — " + tipT + T(lang, " Below −1 goes past the avatar's limit.", " −1 より下はアバターの限界の外。", " −1 아래는 아바타 한계 밖."),
                -3f, 1f, 0f, () => dThumb.x, 0.01f, "0.00"));
            thumb.Add(GaugeRow("poseThumb.y", T(lang, "Thumb root", "親指 付け根", "엄지 뿌리"), "Thumb 1 — " + tipT, -1f, 1f, 0f, () => dThumb.y, 0.01f, "0.00"));
            thumb.Add(GaugeRow("poseThumb.z", T(lang, "Thumb middle", "親指 中", "엄지 가운데"), "Thumb 2 — " + tipT, -1f, 1f, 0f, () => dThumb.z, 0.01f, "0.00"));
            thumb.Add(GaugeRow("poseThumb.w", T(lang, "Thumb tip", "親指 先", "엄지 끝"), "Thumb 3 — " + tipT, -1f, 1f, 0f, () => dThumb.w, 0.01f, "0.00"));

            var fingers = new VisualElement(); fingers.AddToClassList("dc-rows");
            var names = new[] { ("poseIndex", T(lang, "Index", "人差し指", "검지"), dIndex), ("poseMiddle", T(lang, "Middle", "中指", "중지"), dMiddle),
                                ("poseRing", T(lang, "Ring", "薬指", "약지"), dRing), ("poseLittle", T(lang, "Little", "小指", "새끼"), dLittle) };
            var parts = new[] { ("x", T(lang, "root", "付け根", "뿌리")), ("y", T(lang, "middle", "中", "가운데")), ("z", T(lang, "tip", "先", "끝")), ("w", T(lang, "spread", "開き", "벌림")) };
            foreach (var (path, name, def) in names)
                foreach (var (c, part) in parts)
                {
                    float dv = c == "x" ? def.x : c == "y" ? def.y : c == "z" ? def.z : def.w;
                    fingers.Add(GaugeRow(path + "." + c, name + " " + part, name + " " + part + " — " + tipT, -1f, 1f, 0f, () => dv, 0.01f, "0.00"));
                }
            sec.Add(Fold("Pyrite9DrinkCan.FoldFingers", false, T(lang, "FINGERS", "指", "손가락"), fingers));

            var buttons = new VisualElement(); buttons.AddToClassList("dc-buttons");
            var reset = new Button(() => SetPose("Reset hand pose")) { text = T(lang, "Default pose", "初期の形に戻す", "기본 모양으로") };
            reset.AddToClassList("dc-button");
            buttons.Add(reset);
            string fileTip = T(lang, "A .json file with the hand pose and the can's offsets / twist / tilt — keep it, share it, or load it into another avatar's can.",
                "手の形と缶の位置・回転・傾きを .json ファイルに — 保存・共有したり、別のアバターの缶に読み込めます。",
                "손 모양과 캔 위치·돌림·기울임을 담은 .json 파일 — 보관·공유하거나 다른 아바타의 캔에 불러올 수 있습니다.");
            var export = new Button(ExportPose) { text = T(lang, "Export…", "書き出し…", "내보내기…"), tooltip = fileTip };
            export.AddToClassList("dc-button");
            buttons.Add(export);
            var import = new Button(ImportPose) { text = T(lang, "Import…", "読み込み…", "불러오기…"), tooltip = fileTip };
            import.AddToClassList("dc-button");
            buttons.Add(import);
            sec.Add(buttons);

            // the preview's hand follows the sliders; a slider moved in Auto switches to Manual, the other values taken from what
            // Auto built (the preview's last build) so the hand does not jump
            string last = null;
            Vector4[] lastPose = null;
            void Follow()
            {
                var c = Can;
                var cur = new[] { c.poseIndex, c.poseMiddle, c.poseRing, c.poseLittle, c.poseThumb };
                if (lastPose != null && !poseByUs && c.handPoseMode == DrinkCanHandPoseMode.Auto && !cur.SequenceEqual(lastPose))
                {
                    Undo.RecordObject(c, "Hand pose: Manual");
                    if (desc != null && DcHoldPreview.IsActiveFor(desc.gameObject) && DcHoldPreview.BuiltPose(out var auto))
                        for (int i = 0; i < 5; i++)
                            for (int k = 0; k < 4; k++)
                                if (Mathf.Approximately(cur[i][k], lastPose[i][k])) cur[i][k] = auto[i][k];
                    c.poseIndex = cur[0]; c.poseMiddle = cur[1]; c.poseRing = cur[2]; c.poseLittle = cur[3]; c.poseThumb = cur[4];
                    c.handPoseMode = DrinkCanHandPoseMode.Manual;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                    EditorUtility.SetDirty(c);
                    serializedObject.Update();
                }
                poseByUs = false;
                lastPose = cur;
                string now = $"{c.handPoseMode}{c.poseIndex}{c.poseMiddle}{c.poseRing}{c.poseLittle}{c.poseThumb}{c.gripHandPose}{c.gripOffset.ToString("F4")}{c.gripTwist:F2}{c.gripTilt.ToString("F2")}";   // F4: Vector3's own ToString rounds to 0.01 (offsets step 0.001)
                if (now == last) return;
                last = now;
                DcHoldPreview.ApplyHandPose(c);
            }
            live.Add(Follow);
            return sec;
        }

        // hand pose file: the 20 muscles + the can's offset / twist as JSON — numbers only, nothing from the avatar — so a pose
        // shaped on one avatar (Lapwing, whose thumb needs its own values) can be kept, shared and loaded into another can
        [System.Serializable]
        class PoseFile
        {
            public string format;     // PoseFormat; anything else is not ours
            public string version;    // DrinkCan version that wrote it
            public string avatar;     // where it was shaped (shown on load)
            public Vector4 index, middle, ring, little, thumb;
            public Vector3 canOffset;
            public float canTwist;
            public Vector2 canTilt;   // absent in files from before the tilt: 0
        }
        const string PoseFormat = "pyrite9.drinkcan.handpose/1";
        const string PoseDirPref = "Pyrite9DrinkCan.PoseFileDir";

        void ExportPose()
        {
            var c = Can;
            var desc = c.GetComponentInParent<VRCAvatarDescriptor>(true);
            string avatar = desc != null ? desc.gameObject.name : c.gameObject.name;
            string path = EditorUtility.SaveFilePanel(T(lang, "Export hand pose", "手の形を書き出す", "손 모양 내보내기"),
                EditorPrefs.GetString(PoseDirPref, ""), avatar + "_DrinkCanHandPose", "json");
            if (string.IsNullOrEmpty(path)) return;
            EditorPrefs.SetString(PoseDirPref, Path.GetDirectoryName(path));
            WritePoseFile(c, avatar, path);
        }

        void ImportPose()
        {
            string title = T(lang, "Import hand pose", "手の形を読み込む", "손 모양 불러오기");
            string path = EditorUtility.OpenFilePanel(title, EditorPrefs.GetString(PoseDirPref, ""), "json");
            if (string.IsNullOrEmpty(path)) return;
            EditorPrefs.SetString(PoseDirPref, Path.GetDirectoryName(path));
            if (!ReadPoseFile(Can, path))
            {
                EditorUtility.DisplayDialog(title, T(lang, "This is not a DrinkCan hand pose file.", "DrinkCan の手の形ファイルではありません。",
                    "DrinkCan 손 모양 파일이 아닙니다."), "OK");
                return;
            }
            // a loaded pose is used as it is: Manual
            Can.handPoseMode = DrinkCanHandPoseMode.Manual;
            EditorUtility.SetDirty(Can);
            serializedObject.Update();
        }

        static void WritePoseFile(DrinkCan c, string avatar, string path)
        {
            var f = new PoseFile
            {
                format = PoseFormat, version = DrinkCanConst.Version, avatar = avatar,
                index = c.poseIndex, middle = c.poseMiddle, ring = c.poseRing, little = c.poseLittle, thumb = c.poseThumb,
                canOffset = c.gripOffset, canTwist = c.gripTwist, canTilt = c.gripTilt,
            };
            File.WriteAllText(path, JsonUtility.ToJson(f, true));
        }

        // false = not a hand pose file (nothing changed)
        static bool ReadPoseFile(DrinkCan c, string path)
        {
            PoseFile f = null;
            try { f = JsonUtility.FromJson<PoseFile>(File.ReadAllText(path)); } catch (System.Exception) { }
            if (f == null || f.format != PoseFormat) return false;
            // clamped to the sliders' ranges (thumb spread goes to −3, see the slider)
            Vector4 V(Vector4 v) => new Vector4(Mathf.Clamp(v.x, -1f, 1f), Mathf.Clamp(v.y, -1f, 1f), Mathf.Clamp(v.z, -1f, 1f), Mathf.Clamp(v.w, -1f, 1f));
            Undo.RecordObject(c, "Import hand pose");
            c.poseIndex = V(f.index); c.poseMiddle = V(f.middle); c.poseRing = V(f.ring); c.poseLittle = V(f.little);
            c.poseThumb = new Vector4(Mathf.Clamp(f.thumb.x, -3f, 1f), V(f.thumb).y, V(f.thumb).z, V(f.thumb).w);
            c.gripOffset = new Vector3(Mathf.Clamp(f.canOffset.x, -0.05f, 0.05f), Mathf.Clamp(f.canOffset.y, -0.05f, 0.05f), Mathf.Clamp(f.canOffset.z, -0.05f, 0.05f));
            c.gripTwist = Mathf.Clamp(f.canTwist, -180f, 180f);
            c.gripTilt = new Vector2(Mathf.Clamp(f.canTilt.x, -45f, 45f), Mathf.Clamp(f.canTilt.y, -45f, 45f));
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            EditorUtility.SetDirty(c);
            Debug.Log($"[Pyrite9 Drink Can] hand pose imported from {Path.GetFileName(path)} (shaped on {f.avatar}, v{f.version})");
            return true;
        }

        // gauge + value. center = NaN: fills from the left (sizes); otherwise fills from the center (offsets).
        // drag the gauge to change (snapped to step), double-click resets to def(); gold when not at def()
        VisualElement GaugeRow(string path, string label, string tip, float min, float max, float center, System.Func<float> def, float step, string format)
        {
            bool centered = !float.IsNaN(center);
            var row = new VisualElement(); row.AddToClassList("dc-offset");
            row.tooltip = tip;
            row.Add(Label(label, "dc-label"));

            var gauge = new VisualElement(); gauge.AddToClassList("dc-gauge");
            var track = new VisualElement(); track.AddToClassList("dc-gauge__track"); gauge.Add(track);
            var zero = new VisualElement(); zero.AddToClassList("dc-gauge__zero"); gauge.Add(zero);
            if (!centered) zero.style.display = DisplayStyle.None;
            var fill = new VisualElement(); fill.AddToClassList("dc-gauge__fill"); gauge.Add(fill);
            var knob = new VisualElement(); knob.AddToClassList("dc-gauge__knob"); gauge.Add(knob);
            row.Add(gauge);

            var field = new FloatField { bindingPath = path, formatString = format };
            field.AddToClassList("dc-value");
            row.Add(field);

            float ToT(float v)
            {
                v = Mathf.Clamp(v, min, max);
                if (!centered) return (v - min) / (max - min);
                return v < center ? 0.5f * (v - min) / (center - min) : 0.5f + 0.5f * (v - center) / (max - center);
            }
            float FromT(float t)
            {
                t = Mathf.Clamp01(t);
                if (!centered) return Mathf.Lerp(min, max, t);
                return t < 0.5f ? Mathf.Lerp(min, center, t * 2f) : Mathf.Lerp(center, max, t * 2f - 1f);
            }
            void Show()
            {
                var prop = serializedObject.FindProperty(path);
                if (prop == null) return;
                float v = prop.floatValue;
                float t = ToT(v);
                float from = centered ? 0.5f : 0f;
                fill.style.left = Length.Percent(Mathf.Min(t, from) * 100f);
                fill.style.width = Length.Percent(Mathf.Abs(t - from) * 100f);
                knob.style.left = Length.Percent(t * 100f);
                row.EnableInClassList("dc-offset--on", Mathf.Abs(v - def()) > step * 0.5f);
            }
            field.RegisterValueChangedCallback(_ => Show());
            Show();
            live.Add(Show);

            void Set(float v)
            {
                v = Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
                var prop = serializedObject.FindProperty(path);
                if (Mathf.Approximately(prop.floatValue, v)) return;
                prop.floatValue = v;
                serializedObject.ApplyModifiedProperties();
                Show();
            }
            bool drag = false;
            void SetFrom(Vector2 local)
            {
                float w = gauge.contentRect.width;
                if (w < 1f) return;
                Set(FromT(local.x / w));
            }
            gauge.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                if (e.clickCount == 2) { Set(def()); return; }
                drag = true; gauge.CapturePointer(e.pointerId); SetFrom(e.localPosition);
            });
            gauge.RegisterCallback<PointerMoveEvent>(e => { if (drag) SetFrom(e.localPosition); });
            gauge.RegisterCallback<PointerUpEvent>(e => { drag = false; gauge.ReleasePointer(e.pointerId); });
            return row;
        }

        // button group bound to an int/enum property; the 'on' item refreshes in place (Poll)
        VisualElement Seg(string path, int[] values, string[] names, bool small = false)
        {
            var seg = new VisualElement(); seg.AddToClassList("dc-seg");
            if (small) seg.AddToClassList("dc-seg--small");
            var items = new List<(Button b, int v)>();
            void Refresh()
            {
                var prop = serializedObject.FindProperty(path);
                if (prop == null) return;
                foreach (var (b, v) in items) b.EnableInClassList("dc-seg__item--on", v == prop.intValue);
            }
            for (int i = 0; i < values.Length; i++)
            {
                int v = values[i];
                var b = new Button(() =>
                {
                    var prop = serializedObject.FindProperty(path);
                    prop.intValue = v;
                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                    Refresh();
                }) { text = names[i] };
                b.AddToClassList("dc-seg__item");
                if (i == 0) b.AddToClassList("dc-seg__item--first");
                seg.Add(b);
                items.Add((b, v));
            }
            Refresh();
            live.Add(Refresh);
            return seg;
        }

        // ---------------------------------------------------------------- 07 last build
        VisualElement BuildSection()
        {
            var can = Can;
            var desc = can.GetComponentInParent<VRCAvatarDescriptor>(true);
            var rec = desc != null ? DcBuildRecord.Get(desc.gameObject.name) : null;
            string meta = rec != null ? $"{rec.avatar} · {ShortTime(rec.time)}" : null;
            var sec = Section(Num(), T(lang, "LAST BUILD", "最後のビルド", "마지막 빌드"), meta);

            if (desc == null)
                sec.Add(Label(T(lang, "Put the can inside an avatar (under the object with the VRC Avatar Descriptor).",
                    "アバターの中（VRC Avatar Descriptor のオブジェクトの下）に入れてください。",
                    "아바타 안(VRC Avatar Descriptor가 있는 오브젝트 밑)에 넣어 주세요."), "dc-note"));
            else if (rec == null)
                sec.Add(Label(T(lang, "No build yet — Play or Build & Test fills this in.",
                    "まだビルドの記録がありません。Play または Build & Test で表示されます。",
                    "아직 빌드 기록이 없습니다. Play 또는 Build & Test 때 채워집니다."), "dc-note"));
            else
            {
                var grid = new VisualElement(); grid.AddToClassList("dc-facts");
                sec.Add(grid);
                if (rec.fitted)
                {
                    bool far = rec.canScale < 0.6f || rec.canScale > 1.8f;
                    grid.Add(Fact(T(lang, "Can scale", "缶の倍率", "캔 배율"),
                        "×" + rec.canScale.ToString("0.00") + (far ? T(lang, " — far from the reference", " — 基準から遠い", " — 기준과 멂") : ""), far ? "dc-fact__value--gold" : null));
                    bool uneven = Mathf.Abs(rec.handLengthR - rec.handLengthL) > 0.1f * Mathf.Max(rec.handLengthR, rec.handLengthL);
                    grid.Add(Fact(T(lang, "Hand length", "手の長さ", "손 길이"),
                        $"R {rec.handLengthR:0.000} · L {rec.handLengthL:0.000} m" + (uneven ? T(lang, " — hands differ", " — 左右で違う", " — 양손이 다름") : ""), uneven ? "dc-fact__value--gold" : null));
                    grid.Add(Fact(T(lang, "Spawn hand", "出す手", "꺼내는 손"),
                        rec.hand == (int)DrinkCanHand.Left ? T(lang, "Left", "左手", "왼손") : T(lang, "Right", "右手", "오른손"), null));
                    grid.Add(Fact(T(lang, "Sips", "飲む回数", "모금 수"), rec.sips.ToString(), null));
                }
                else
                    grid.Add(Fact(T(lang, "Can", "缶", "캔"), T(lang, "not built — needs a humanoid avatar", "作られていません — ヒューマノイドが必要", "안 만들어짐 — 휴머노이드 필요"), "dc-fact__value--gold"));
                grid.Add(Fact(T(lang, "Warnings", "警告", "경고"), rec.warnings.Count == 0 ? T(lang, "none", "なし", "없음") : rec.warnings.Count.ToString(),
                    rec.warnings.Count == 0 ? null : "dc-fact__value--warn"));
                if (rec.version != DrinkCanConst.Version)
                    grid.Add(Fact(T(lang, "Built with", "ビルド時の版", "빌드 버전"), "v" + rec.version + T(lang, " — rebuild", " — 再ビルドを", " — 다시 빌드 필요"), "dc-fact__value--gold"));
                // in the menu language when the record has the keys (DcWarn), else the English line. OK folds one per avatar
                bool keyed = rec.warnKeys != null && rec.warnKeys.Count == rec.warnings.Count;
                for (int i = 0; i < Mathf.Min(6, rec.warnings.Count); i++)
                {
                    string w = keyed ? DcWarn.Text(lang, rec.warnKeys[i]) ?? rec.warnings[i] : rec.warnings[i];
                    sec.Add(Warn(w, rec.avatar + "/" + (keyed ? DcWarn.OkKey(rec.warnKeys[i]) : rec.warnings[i])));
                }
                if (rec.warnings.Count > 6) sec.Add(Label($"… +{rec.warnings.Count - 6}", "dc-note"));
            }
            if (can.metalMaterial == null || can.labelMaterial == null || can.liquidMaterial == null)
                sec.Add(Warn(T(lang, "A material template is empty (Metal / Label / Liquid) — use the prefab from the package.",
                    "マテリアルのテンプレートが空です（Metal / Label / Liquid）— パッケージのプレハブを使ってください。",
                    "머티리얼 템플릿이 비어 있습니다(Metal / Label / Liquid) — 패키지의 프리팹을 쓰세요."),
                    (desc != null ? desc.gameObject.name : "-") + "/MaterialEmpty"));

            var buttons = new VisualElement(); buttons.AddToClassList("dc-buttons");
            var logPath = Path.GetFullPath(DrinkCanConst.LogPath);
            var logBtn = new Button(() => EditorUtility.OpenWithDefaultApp(logPath)) { text = T(lang, "Build log", "ビルドログ", "빌드 로그") };
            logBtn.AddToClassList("dc-button");
            logBtn.SetEnabled(File.Exists(logPath));
            buttons.Add(logBtn);
            var readme = ReadmePath();
            var rmBtn = new Button(() => EditorUtility.OpenWithDefaultApp(readme)) { text = "README" };
            rmBtn.AddToClassList("dc-button");
            rmBtn.SetEnabled(readme != null);
            buttons.Add(rmBtn);
            sec.Add(buttons);
            return sec;
        }

        // ---------------------------------------------------------------- 08 changelog
        VisualElement ChangelogSection()
        {
            var readme = ReadmePath();
            var entries = readme != null ? ReadChangelog(readme) : new List<(string ver, string date, string text)>();
            var cur = entries.FirstOrDefault(e => e.ver == DrinkCanConst.Version);
            if (cur.ver == null && entries.Count > 0) cur = entries[0];
            var sec = Section(Num(), T(lang, "CHANGELOG", "更新履歴", "변경 기록"), cur.ver != null ? "v" + cur.ver + " · " + cur.date : null);
            var content = new VisualElement();
            if (cur.ver == null) content.Add(Label(T(lang, "See the README.", "README を見てください。", "README를 보세요."), "dc-note"));
            else content.Add(Label(cur.text, "dc-changelog"));
            if (readme != null)
            {
                var all = Label(T(lang, "Full changelog — README", "すべての更新履歴 — README", "전체 변경 기록 — README"), "dc-link");
                all.RegisterCallback<ClickEvent>(_ => EditorUtility.OpenWithDefaultApp(readme));
                content.Add(all);
            }
            sec.Add(Fold("Pyrite9DC.ShowChangelog", true, T(lang, "THIS VERSION", "この版", "이번 버전"), content));
            return sec;
        }

        // README '## 변경 기록 / Changelog / 更新履歴' lines "- **0.1.0** (2026-10-21): text" (indented next lines are joined)
        static List<(string ver, string date, string text)> ReadChangelog(string path)
        {
            var list = new List<(string ver, string date, string text)>();
            try
            {
                bool inSec = false;
                var rx = new System.Text.RegularExpressions.Regex(@"^-\s*\*\*(?<v>[0-9][0-9.]*)\*\*\s*[(（](?<d>[^)）]*)[)）]\s*[:：]\s*(?<t>.*)$");
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.StartsWith("## ")) { inSec = line.Contains("Changelog") || line.Contains("변경 기록") || line.Contains("更新履歴"); continue; }
                    if (!inSec) continue;
                    var m = rx.Match(line.Trim());
                    if (m.Success) list.Add((m.Groups["v"].Value, m.Groups["d"].Value.Trim(), Plain(m.Groups["t"].Value)));
                    else if (list.Count > 0 && line.StartsWith("  ") && line.Trim().Length > 0)
                    {
                        var last = list[list.Count - 1];
                        list[list.Count - 1] = (last.ver, last.date, last.text + " " + Plain(line.Trim()));
                    }
                }
            }
            catch { }
            return list;
        }

        static string Plain(string md) => md.Replace("**", "").Replace("`", "");

        // ---------------------------------------------------------------- paths
        string EditorDir()
        {
            var script = MonoScript.FromScriptableObject(this);
            return script != null ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(script)) : null;
        }

        string PackageDir() { var d = EditorDir(); return d != null ? Path.GetDirectoryName(d) : null; }

        string ReadmePath()
        {
            var pkg = PackageDir();
            if (pkg == null) return null;
            string name = lang == 1 ? "README_jp.md" : lang == 2 ? "README_ko.md" : "README_en.md";
            var p = Path.GetFullPath(Path.Combine(pkg, name));
            return File.Exists(p) ? p : null;
        }

        string TemplatePath()
        {
            var pkg = PackageDir();
            if (pkg == null) return null;
            // one per preset at that body's exact aspect (make_label.py); a custom size gets the general 2:1 one
            string tag = Can.sizePreset == DrinkCanSizePreset.Can350 ? "_350ml" : Can.sizePreset == DrinkCanSizePreset.Can500 ? "_500ml"
                : Can.sizePreset == DrinkCanSizePreset.Slim250 ? "_250mlSlim" : Can.sizePreset == DrinkCanSizePreset.Coffee190 ? "_190gCoffee" : "";
            var p = Path.GetFullPath(Path.Combine(pkg, "Labels", "DrinkCan_LabelTemplate" + tag + ".png"));
            if (!File.Exists(p)) p = Path.GetFullPath(Path.Combine(pkg, "Labels", "DrinkCan_LabelTemplate.png"));
            return File.Exists(p) ? p : null;
        }

        // Labels/LabelGuide_<en|jp|ko>.md in the menu language (9-87)
        string LabelGuidePath()
        {
            var pkg = PackageDir();
            if (pkg == null) return null;
            string name = lang == 1 ? "LabelGuide_jp.md" : lang == 2 ? "LabelGuide_ko.md" : "LabelGuide_en.md";
            var p = Path.GetFullPath(Path.Combine(pkg, "Labels", name));
            return File.Exists(p) ? p : null;
        }

        static string ShortTime(string t)
        {
            if (!System.DateTime.TryParse(t, out var d)) return t;
            return d.Date == System.DateTime.Today ? d.ToString("HH:mm") : d.ToString("MM-dd HH:mm");
        }

        // ---------------------------------------------------------------- shared pieces
        // warning: orange box with a drawn warning sign (triangle + '!') — has to read as a warning at a glance.
        // okKey: an OK button folds the box to one dim line, remembered in EditorPrefs (someone who has seen it and adjusted
        // does not want it shouting every time); the folded line's button opens it again. null = no button
        VisualElement Warn(string text, string okKey = null)
        {
            var box = new VisualElement(); box.AddToClassList("dc-warn");
            var icon = new VisualElement(); icon.AddToClassList("dc-warn__icon");
            icon.generateVisualContent += mgc =>
            {
                var p = mgc.painter2D;
                p.fillColor = new Color32(0xFF, 0x8A, 0x4C, 0xFF);
                p.BeginPath(); p.MoveTo(new Vector2(9f, 1f)); p.LineTo(new Vector2(17.5f, 16.5f)); p.LineTo(new Vector2(0.5f, 16.5f)); p.ClosePath(); p.Fill();
                p.strokeColor = new Color32(0x3A, 0x1F, 0x14, 0xFF); p.lineWidth = 2f; p.lineCap = LineCap.Round;
                p.BeginPath(); p.MoveTo(new Vector2(9f, 6.5f)); p.LineTo(new Vector2(9f, 11f)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(9f, 13.8f)); p.LineTo(new Vector2(9f, 14f)); p.Stroke();
            };
            box.Add(icon);
            var label = Label(text, "dc-warn__text");
            box.Add(label);
            if (okKey == null) return box;

            string pref = "Pyrite9DrinkCan.WarnOk." + okKey;
            var btn = new Button(); btn.AddToClassList("dc-warn__ok");
            void Show(bool ok)
            {
                box.EnableInClassList("dc-warn--ok", ok);
                label.tooltip = ok ? text : null;   // folded to one line: the whole text on hover
                btn.text = ok ? T(lang, "Show", "開く", "펼치기") : T(lang, "OK", "確認", "확인");
                btn.tooltip = ok ? null : T(lang, "Seen it — fold this warning for this avatar (only the numbers changing keeps it folded; a different warning shows open).",
                    "確認済み — このアバターのこの警告をたたみます（数値が変わってもたたんだまま、別の警告は開いて出ます）。",
                    "확인함 — 이 아바타의 이 경고를 접습니다(수치만 바뀌면 접힌 채로, 다른 경고는 펼쳐서 나옵니다).");
            }
            btn.clicked += () =>
            {
                bool ok = !EditorPrefs.GetBool(pref, false);
                if (ok) EditorPrefs.SetBool(pref, true); else EditorPrefs.DeleteKey(pref);
                Show(ok);
            };
            Show(EditorPrefs.GetBool(pref, false));
            box.Add(btn);
            return box;
        }

        static Label Label(string text, string cls)
        {
            var l = new Label(text);
            if (cls != null) foreach (var k in cls.Split(' ')) l.AddToClassList(k);
            return l;
        }

        static VisualElement Section(string num, string title, string meta)
        {
            var sec = new VisualElement(); sec.AddToClassList("dc-section");
            var head = new VisualElement(); head.AddToClassList("dc-section__head");
            head.Add(Label(num, "dc-section__num"));
            head.Add(Label(title, "dc-section__title"));
            if (meta != null) head.Add(Label(meta, "dc-section__meta"));
            sec.Add(head);
            return sec;
        }

        static VisualElement Divider() { var d = new VisualElement(); d.AddToClassList("dc-divider"); return d; }

        static VisualElement Row(string label, string tip, VisualElement control)
        {
            var row = new VisualElement(); row.AddToClassList("dc-row");
            row.tooltip = tip;
            row.Add(Label(label, "dc-label"));
            control.AddToClassList("dc-row__control");
            row.Add(control);
            return row;
        }

        static VisualElement Stack(VisualElement row) { row.AddToClassList("dc-row--stack"); return row; }

        static VisualElement Fact(string label, string value, string valueCls)
        {
            var row = new VisualElement(); row.AddToClassList("dc-fact");
            row.Add(Label(label, "dc-label"));
            var v = Label(value, "dc-fact__value");
            if (valueCls != null) v.AddToClassList(valueCls);
            row.Add(v);
            return row;
        }
    }
}
#endif
