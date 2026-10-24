// Pyrite9 Drink Can — build warnings in the inspector's language
//   Every warning has an id and its values. The build log gets the English line ("WARN …", as before); the build record
//   (DcBuildRecord) keeps "id␟value␟value…" next to it, and the inspector's 'Last build' writes it in the menu language.
//   Records from before this (no keys) show the English line.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pyrite9.DrinkCan
{
    public static class DcWarn
    {
        public enum Id { NotHumanoid, BoneMissing, NoVisemes, HandsDiffer, NoHandBones, NoSkin, NoHandVerts, PoseNotFit, HandSinks, ManyCans, NoMaterial, BuildFailed,
            NoThrowMesh, NoMaskMaterial, NoParticleShader, NoMetal, NoFoamTexture, NoSound }
        const char Sep = '\u001f';

        static List<string> current;

        // one build: Begin, the warnings, Keys
        public static void Begin() => current = new List<string>();
        public static List<string> Keys() { var k = current ?? new List<string>(); current = null; return k; }

        // log the English line and remember the key. Side values are "Right" / "Left"
        public static void Add(StringBuilder log, Id id, params string[] args)
        {
            log?.AppendLine("WARN " + Text(0, id, args));
            current?.Add(id + Sep.ToString() + string.Join(Sep.ToString(), args));
        }

        // a recorded key in a language (0 English, 1 Japanese, 2 Korean = DrinkCanMenuLanguage); null if it cannot be read
        public static string Text(int lang, string key)
        {
            var parts = key.Split(Sep);
            if (!System.Enum.TryParse(parts[0], out Id id)) return null;
            return Text(lang, id, parts.Skip(1).ToArray());
        }

        // what the inspector's OK button remembers for a recorded key: the id and its words (side, mesh name) but not the
        // numbers — adjusting the pose changes the millimetres, and a warning already seen should stay folded
        public static string OkKey(string key) => string.Join("/", key.Split(Sep).Where((p, i) => i == 0
            || !float.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)));

        static string A(string[] a, int i) => i < a.Length ? a[i] : "?";

        static string Side(int l, string side) => side == "Left"
            ? (l == 1 ? "左手" : l == 2 ? "왼손" : "Left hand")
            : (l == 1 ? "右手" : l == 2 ? "오른손" : "Right hand");

        public static string Text(int l, Id id, string[] a)
        {
            string T(string en, string jp, string ko) => l == 1 ? jp : l == 2 ? ko : en;
            switch (id)
            {
                case Id.NotHumanoid:
                    return T("The can needs a humanoid avatar with a VRC Avatar Descriptor — not built",
                        "缶には VRC Avatar Descriptor 付きのヒューマノイドアバターが必要です — 作られていません",
                        "캔은 VRC Avatar Descriptor가 있는 휴머노이드 아바타가 필요합니다 — 만들지 않았습니다");
                case Id.BoneMissing:
                    return T("Hand or head bone missing — not built",
                        "手または頭のボーンがありません — 作られていません",
                        "손 또는 머리 본이 없습니다 — 만들지 않았습니다");
                case Id.NoVisemes:
                    return T("No lip-sync visemes — the mouth is guessed from the view position (drinking on desktop may miss the mouth)",
                        "リップシンクのビセームがありません — 口の位置は視点から推定します（デスクトップでは口に届かないことがあります）",
                        "립싱크 비즘이 없습니다 — 입 위치를 시점으로 추정합니다(데스크톱에서는 입에 안 맞을 수 있음)");
                case Id.HandsDiffer:
                    return T($"Hand lengths differ by more than 10 % (right {A(a, 0)} m, left {A(a, 1)} m) — the can is sized for the spawn hand",
                        $"左右の手の長さが 10 % 以上違います（右 {A(a, 0)} m・左 {A(a, 1)} m）— 缶は「出す手」に合わせます",
                        $"양손 길이가 10 % 넘게 다릅니다(오른손 {A(a, 0)} m, 왼손 {A(a, 1)} m) — 캔 크기는 '꺼내는 손'에 맞춥니다");
                case Id.NoHandBones:
                    return T($"{Side(l, A(a, 0))}: hand / index finger bones missing — default grip, no hand pose",
                        $"{Side(l, A(a, 0))}：手・人差し指のボーンがありません — 標準の持ち位置、手の形なし",
                        $"{Side(l, A(a, 0))}: 손·검지 본이 없습니다 — 기본 쥐는 자리, 손 모양 없음");
                case Id.NoSkin:
                    return T($"{Side(l, A(a, 0))}: no skinned mesh on the hand — default grip, no hand pose",
                        $"{Side(l, A(a, 0))}：手のスキンメッシュが見つかりません — 標準の持ち位置、手の形なし",
                        $"{Side(l, A(a, 0))}: 손 피부 메시를 못 찾았습니다 — 기본 쥐는 자리, 손 모양 없음");
                case Id.NoHandVerts:
                    return T($"{Side(l, A(a, 0))}: no hand vertices on {A(a, 1)} — default grip, no hand pose",
                        $"{Side(l, A(a, 0))}：{A(a, 1)} に手の頂点がありません — 標準の持ち位置、手の形なし",
                        $"{Side(l, A(a, 0))}: {A(a, 1)}에 손 정점이 없습니다 — 기본 쥐는 자리, 손 모양 없음");
                case Id.PoseNotFit:
                    return T($"{Side(l, A(a, 0))}: the hand pose does not fit this avatar (the can cannot sit against the palm, {A(a, 1)} mm out) — that hand follows the controller. Adjust 'Hand pose' in the inspector",
                        $"{Side(l, A(a, 0))}：手の形がこのアバターに合いません（缶が手のひらに付かず {A(a, 1)} mm 離れる）— その手はコントローラーのままです。インスペクターの「手の形」で調整してください",
                        $"{Side(l, A(a, 0))}: 손 모양이 이 아바타에 맞지 않습니다(캔이 손바닥에 붙지 못하고 {A(a, 1)} mm 떨어짐) — 그 손은 컨트롤러 그대로입니다. 인스펙터 '쥐는 손 모양'에서 조절하세요");
                case Id.HandSinks:
                    return T($"{Side(l, A(a, 0))}: the hand sinks {A(a, 1)} mm into the can",
                        $"{Side(l, A(a, 0))}：手が缶に {A(a, 1)} mm めり込みます",
                        $"{Side(l, A(a, 0))}: 손이 캔에 {A(a, 1)} mm 파고듭니다");
                case Id.ManyCans:
                    return T($"{A(a, 0)} DrinkCan components — only the first one is built",
                        $"DrinkCan コンポーネントが {A(a, 0)} 個あります — 最初の 1 個だけ作ります",
                        $"DrinkCan 컴포넌트가 {A(a, 0)}개 있습니다 — 첫 번째 것만 만듭니다");
                case Id.NoMaterial:
                    return T("Material template missing (Metal / Label / Liquid) — that part is drawn without a material",
                        "マテリアルのテンプレートがありません（Metal / Label / Liquid）— その部分はマテリアルなしで描かれます",
                        "머티리얼 템플릿이 비어 있습니다(Metal / Label / Liquid) — 그 부분은 머티리얼 없이 그려집니다");
                case Id.BuildFailed:
                    return T($"Build failed: {A(a, 0)}", $"ビルドに失敗しました：{A(a, 0)}", $"빌드 실패: {A(a, 0)}");
                // package files missing or the prefab edited (README: delete the can, drag the prefab in again / re-import)
                case Id.NoThrowMesh:
                    return T("throw particle mesh not made (unexpected can mesh layout) — built without throwing",
                        "投げる缶のメッシュを作れませんでした（缶メッシュの構成が想定外）— 投げる機能なしで作りました",
                        "던지는 캔 메시를 못 만들었습니다(캔 메시 구성이 예상과 다름) — 던지기 없이 만들었습니다");
                case Id.NoMaskMaterial:
                    return T("liquid mask material not found — the liquid is not drawn",
                        "液体マスクのマテリアルが見つかりません — 液体は描かれません",
                        "액체 마스크 머티리얼을 못 찾았습니다 — 액체가 그려지지 않습니다");
                case Id.NoParticleShader:
                    return T("particle shader not found — no fizz when opened",
                        "パーティクルのシェーダーが見つかりません — 開けたときの泡なし",
                        "파티클 셰이더를 못 찾았습니다 — 딸 때 기포 없음");
                case Id.NoMetal:
                    return T("no metal material — no foam dome",
                        "金属のマテリアルがありません — 泡のドームなし",
                        "금속 머티리얼이 없습니다 — 거품 돔 없음");
                case Id.NoFoamTexture:
                    return T($"foam texture missing ({A(a, 0)})",
                        $"泡のテクスチャがありません（{A(a, 0)}）",
                        $"거품 텍스처가 없습니다({A(a, 0)})");
                case Id.NoSound:
                    return T($"sound {A(a, 0)} not found (Sounds folder missing?) — built without it",
                        $"効果音 {A(a, 0)} が見つかりません（Sounds フォルダーがない？）— その音なしで作りました",
                        $"효과음을 못 찾았습니다({A(a, 0)}, Sounds 폴더가 없나요?) — 그 소리 없이 만들었습니다");
            }
            return id.ToString();
        }
    }
}
#endif
