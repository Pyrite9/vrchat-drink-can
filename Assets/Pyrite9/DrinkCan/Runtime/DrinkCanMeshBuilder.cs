using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Pyrite9.DrinkCan.DrinkCanLidLayout;

namespace Pyrite9.DrinkCan
{
    /// <summary>
    /// Procedural drink can.
    ///  - Body, neck, double seam, chuck wall, countersink and bottom: a surface of revolution from a profile.
    ///  - Lid panel: a polar height field (keyhole deboss panel, finger well, rivet button).
    ///  - Pull tab: outline with curled (rounded) edges, bent like a real stay-on tab.
    /// Lid/tab layout and the metal atlas come from Source/tools/make_metal_atlas.py (DrinkCanLidLayout.g.cs);
    /// fine detail (scores, beads, embossing, rivet lance, grain, date code) lives in the atlas.
    /// Only the straight body stretches with height/diameter; the lid is a real "202" end and shrinks
    /// (as a whole) only when the can is too narrow for it.
    /// Units are millimetres internally, output is metres. Pivot = bottom centre, +Y up, tab nose points +X.
    /// UV0 = real-scale tiling on metal (one tile = Dims.tileMm, seamless around the can) for the aluminium
    ///       surface maps; the label keeps its own UV in UV0.
    /// UV1 = metal detail atlas (scores, beads, AO ramp, date code) — sampled by lilToon Main 2nd / Normal 2nd.
    /// </summary>
    public static class DrinkCanMeshBuilder
    {
        public const int SubmeshMetal = 0;
        public const int SubmeshLabel = 1;
        public const int SubmeshLiquid = 2;

        // Skinning: every vertex follows exactly one bone. Bones sit at their pivots (metres, can-local, no rotation).
        public const int BoneCan = 0, BoneTab = 1, BoneTear = 2, BoneLiquid = 3;
        public static readonly string[] BoneNames = { "Can", "Tab", "TearPanel", "Liquid" };

        public struct Rig
        {
            public Vector3 tabPivot;      // tab rotates about local Z here (negative angle = ring up, nose down)
            public Vector3 tearPivot;     // tear panel hinge; negative angle about +Z folds it down into the can
            public Vector3 liquidFull;    // liquid surface when full
            public Vector3 liquidEmpty;   // liquid surface when empty
        }

        public static Rig GetRig(DrinkCanShape shape)
        {
            var d = GetDims(shape);
            float k = d.lidK;
            const float mm = 0.001f;
            float hingeX = TearPanel[0].x * k;
            return new Rig
            {
                tabPivot = new Vector3(TabHingeX * k, d.panelY + PanelField(d, 0f, 0f), 0f) * mm,
                tearPivot = new Vector3(hingeX, d.panelY + PanelField(d, hingeX, 0f), 0f) * mm,
                liquidFull = new Vector3(0f, d.bodyY1 - 2f, 0f) * mm,
                liquidEmpty = new Vector3(0f, d.domeD + 1.5f, 0f) * mm,
            };
        }

        // ---- real-world end ("202") and seam, mm. Sources: docs/drink-can.md 9-4 ------------------------
        const float SeamOuterRMax = 27.3f;       // seamed 202 end on a 211 body
        const float SeamHeight = 2.5f;
        const float SeamThickness = 1.1f;
        const float CountersinkDepth = 6.86f;    // seam top -> countersink bottom
        const float CountersinkRadius = 0.5f;
        const float ChuckWallDeg = 12f;
        const float PanelHeight = 2.29f;         // panel above countersink bottom
        const float PanelRRef = 22.94f;          // panel radius of the 202 end (lid detail scale 1)
        const float NeckPlug = 1.2f;             // short vertical neck right under the seam
        const float TabThickness = 0.9f;
        const float RivetHeadR = 1.8f, RivetHeadH = 0.5f;
        const float TileTargetMm = 40f;          // UV0: about one surface-map tile per 40 mm

        public struct Dims
        {
            public float R, H;
            public float seamR, seamInR, neckR, neckH;
            public float chuckBottomR, csY, panelR, panelY, lidK;
            public float standR, noseR, bottomH, domeD;
            public float bodyY0, bodyY1;   // straight body (label area)
            public float tileMm; public int tileN;   // UV0 tile size (mm) and tiles around the can
        }

        public static Dims GetDims(DrinkCanShape shape)
        {
            var s = shape.Clamped;
            var d = new Dims();
            d.H = s.heightMm;
            d.R = s.diameterMm * 0.5f;
            d.tileN = Mathf.Max(1, Mathf.RoundToInt(2f * Mathf.PI * d.R / TileTargetMm));
            d.tileMm = 2f * Mathf.PI * d.R / d.tileN;

            d.seamR = Mathf.Min(SeamOuterRMax, d.R - 1.2f);
            d.seamInR = d.seamR - SeamThickness;
            d.neckR = d.seamR - 0.7f;
            d.csY = d.H - CountersinkDepth;
            float chuckDrop = CountersinkDepth - 0.5f - CountersinkRadius;
            d.chuckBottomR = d.seamInR - chuckDrop * Mathf.Tan(ChuckWallDeg * Mathf.Deg2Rad);
            d.panelR = d.chuckBottomR - 2.0f;
            d.panelY = d.csY + PanelHeight;
            d.lidK = Mathf.Min(1f, d.panelR / PanelRRef);

            float sc = Mathf.Clamp(s.diameterMm / 66f, 0.7f, 1.3f);
            d.standR = d.R * 0.75f;
            d.noseR = 1.4f * sc;
            d.bottomH = Mathf.Clamp((d.R - d.standR) * 1.15f, 3f, 14f);
            d.domeD = Mathf.Min(10f * sc, d.H * 0.15f);

            d.neckH = Mathf.Clamp((d.R - d.seamR) * 2.2f + 2f, NeckPlug + 1f, 16f);
            d.bodyY0 = d.bottomH;
            d.bodyY1 = d.H - SeamHeight - d.neckH;
            if (d.bodyY1 - d.bodyY0 < 5f)
            {
                d.neckH = Mathf.Max(NeckPlug + 0.5f, d.H - SeamHeight - d.bottomH - 5f);
                d.bodyY1 = d.H - SeamHeight - d.neckH;
            }
            return d;
        }

        enum UvMode { Ramp, Label, Bottom }
        enum AoKind { None, Neck, Seam, Interior }

        struct Section
        {
            public int submesh;
            public UvMode uv;
            public AoKind ao;
            public List<Vector2> pts;   // (r, y)
            public List<float> v;       // label v per point (label sections only)
            public float uScale;
        }

        class Buf
        {
            public readonly List<Vector3> pos = new List<Vector3>();
            public readonly List<Vector3> nrm = new List<Vector3>();
            public readonly List<Vector2> uv0 = new List<Vector2>();
            public readonly List<Vector2> uv1 = new List<Vector2>();
            public readonly List<int> bone = new List<int>();
            public readonly List<int>[] tris = { new List<int>(), new List<int>(), new List<int>() };
            public int curBone = BoneCan;

            public int Add(Vector3 p, Vector3 n, Vector2 t0, Vector2 t1)
            {
                pos.Add(p); nrm.Add(n); uv0.Add(t0); uv1.Add(t1); bone.Add(curBone);
                return pos.Count - 1;
            }

            // Winds the triangle so its face normal agrees with `want` (Unity: front = Cross(b - a, c - a)).
            public void Tri(int sub, int a, int b, int c, Vector3 want)
            {
                var f = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
                if (f.sqrMagnitude < 1e-12f) return;
                var t = tris[sub];
                if (Vector3.Dot(f, want) >= 0f) { t.Add(a); t.Add(b); t.Add(c); }
                else { t.Add(a); t.Add(c); t.Add(b); }
            }

            public void Quad(int sub, int a, int b, int c, int d)
            {
                var want = nrm[a] + nrm[b] + nrm[c] + nrm[d];
                Tri(sub, a, b, c, want);
                Tri(sub, a, c, d, want);
            }
        }

        public static Mesh Build(DrinkCanShape shape, DrinkCanLabelFit fit, float labelAspect,
            int segments = 64, Mesh target = null)
        {
            var d = GetDims(shape);
            segments = Mathf.Clamp(segments, 16, 128) / 4 * 4;
            labelAspect = labelAspect > 0.01f ? labelAspect : 2f;

            var sections = new List<Section>();
            sections.Add(new Section { submesh = SubmeshMetal, uv = UvMode.Bottom, pts = BottomProfile(d) });
            AddBody(sections, d, fit, labelAspect);
            sections.Add(Metal(NeckProfile(d), AoKind.Neck));
            sections.Add(Metal(SeamAndCountersinkProfile(d), AoKind.Seam));

            var buf = new Buf();
            float arc = 0f;
            foreach (var sec in sections) Revolve(buf, sec, d, segments, ref arc);
            AddLidPanel(buf, d, segments);
            buf.curBone = BoneTab;
            AddTab(buf, d);
            buf.curBone = BoneCan;
            AddInterior(buf, d, segments, ref arc);
            buf.curBone = BoneLiquid;
            // the surface is kept level in game (a constraint on its bone) while the can tilts: wide enough to cross the whole can
            // at any tilt (its corner to the far rim), and only seen through the opening (stencil, LiquidMask)
            AddDisc(buf, d, d.bodyY1 - 2f, Mathf.Sqrt(d.H * d.H + d.R * d.R), segments, SubmeshLiquid, 1f);
            buf.curBone = BoneCan;

            const float mm = 0.001f;
            for (int i = 0; i < buf.pos.Count; i++) buf.pos[i] *= mm;

            var mesh = target != null ? target : new Mesh();
            mesh.Clear();
            mesh.name = $"DrinkCan_{shape.Clamped.diameterMm:0.#}x{shape.Clamped.heightMm:0.#}";
            mesh.SetVertices(buf.pos);
            mesh.SetNormals(buf.nrm);
            mesh.SetUVs(0, buf.uv0);
            mesh.SetUVs(1, buf.uv1);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(buf.tris[SubmeshMetal], SubmeshMetal);
            mesh.SetTriangles(buf.tris[SubmeshLabel], SubmeshLabel);
            mesh.SetTriangles(buf.tris[SubmeshLiquid], SubmeshLiquid);

            var rig = GetRig(shape);
            var pivots = new[] { Vector3.zero, rig.tabPivot, rig.tearPivot, rig.liquidFull };
            var weights = new BoneWeight[buf.pos.Count];
            for (int i = 0; i < weights.Length; i++) weights[i] = new BoneWeight { boneIndex0 = buf.bone[i], weight0 = 1f };
            mesh.boneWeights = weights;
            mesh.bindposes = pivots.Select(pv => Matrix4x4.Translate(-pv)).ToArray();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static Section Metal(List<Vector2> pts, AoKind ao = AoKind.None) =>
            new Section { submesh = SubmeshMetal, uv = UvMode.Ramp, ao = ao, pts = pts };

        // ---- profiles -------------------------------------------------------------------------------------

        static List<Vector2> BottomProfile(Dims d)
        {
            var p = new List<Vector2>();
            float sc = d.noseR / 1.4f;
            // spherical dome: apex -> dome edge
            float rd = d.standR - d.noseR - 1.0f * sc, yd = 3.2f * sc;
            float sag = Mathf.Max(0.5f, d.domeD - yd);
            float rs = (rd * rd + sag * sag) / (2f * sag);
            for (int i = 0; i <= 10; i++)
            {
                float r = rd * i / 10f;
                p.Add(new Vector2(r, d.domeD - (rs - Mathf.Sqrt(rs * rs - r * r))));
            }
            // inner wall down to the stand ring (vertical where it meets the nose)
            Bezier2(p, new Vector2(rd, yd), new Vector2(d.standR - d.noseR, yd - 0.2f * sc),
                new Vector2(d.standR - d.noseR, d.noseR), 4);
            // nose (stand ring): half round, inner side -> bottom -> outer side
            const float aEnd = 2f * Mathf.PI - 0.75f;
            for (int i = 1; i <= 8; i++)
            {
                float a = Mathf.Lerp(Mathf.PI, aEnd, i / 8f);
                p.Add(new Vector2(d.standR + d.noseR * Mathf.Cos(a), d.noseR + d.noseR * Mathf.Sin(a)));
            }
            // outer wall: leaves the nose tangentially, rounds into the vertical body
            var e = p[p.Count - 1];
            var t = new Vector2(-Mathf.Sin(aEnd), Mathf.Cos(aEnd));
            Bezier3(p, e, e + t * (2.5f * sc), new Vector2(d.R, d.bottomH - 2.5f * sc), new Vector2(d.R, d.bottomH), 9);
            return p;
        }

        static List<Vector2> NeckProfile(Dims d)
        {
            var p = new List<Vector2>();
            float curveH = d.neckH - NeckPlug;
            for (int i = 0; i <= 12; i++)
            {
                float t = i / 12f;
                float s = t * t * (3f - 2f * t);
                p.Add(new Vector2(d.R - (d.R - d.neckR) * s, d.bodyY1 + curveH * t));
            }
            p.Add(new Vector2(d.neckR, d.H - SeamHeight));
            return p;
        }

        static List<Vector2> SeamAndCountersinkProfile(Dims d)
        {
            float s = d.seamR, si = d.seamInR, H = d.H, cb = d.chuckBottomR, cy = d.csY;
            var p = new List<Vector2>
            {
                // underside of the seam, then the outer seam wall
                new Vector2(d.neckR, H - SeamHeight),
                new Vector2(s - 0.35f, H - SeamHeight + 0.05f),
                new Vector2(s - 0.06f, H - SeamHeight + 0.3f),
                new Vector2(s, H - 1.8f),
                new Vector2(s + 0.02f, H - 1.0f),
                new Vector2(s - 0.1f, H - 0.35f),
                // rounded top
                new Vector2(s - 0.35f, H - 0.06f),
                new Vector2(s - 0.6f, H),
                new Vector2(si + 0.25f, H - 0.06f),
                new Vector2(si + 0.05f, H - 0.28f),
                new Vector2(si, H - 0.5f),
                // chuck wall (12 deg) down to the countersink
                Vector2.Lerp(new Vector2(si, H - 0.5f), new Vector2(cb, cy + CountersinkRadius), 0.5f),
                new Vector2(cb, cy + CountersinkRadius),
            };
            // countersink U
            float cr = CountersinkRadius;
            var cc = new Vector2(cb - cr, cy + cr);
            for (int i = 1; i <= 7; i++)
            {
                float a = -Mathf.PI * i / 7f;
                p.Add(cc + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * cr);
            }
            // panel wall up to the panel
            p.Add(new Vector2(cb - 1.05f, cy + 1.3f));
            p.Add(new Vector2(cb - 1.25f, cy + 1.85f));
            p.Add(new Vector2(cb - 1.6f, cy + 2.2f));
            p.Add(new Vector2(d.panelR, d.panelY + PanelField(d, d.panelR, 0f)));
            return p;
        }

        static void Bezier2(List<Vector2> p, Vector2 a, Vector2 b, Vector2 c, int n)
        {
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n, u = 1f - t;
                p.Add(u * u * a + 2f * u * t * b + t * t * c);
            }
        }

        static void Bezier3(List<Vector2> p, Vector2 a, Vector2 b, Vector2 c, Vector2 e, int n)
        {
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n, u = 1f - t;
                p.Add(u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * e);
            }
        }

        static void AddBody(List<Section> sections, Dims d, DrinkCanLabelFit fit, float aspect)
        {
            float y0 = d.bodyY0, y1 = d.bodyY1, len = y1 - y0;
            float circ = Mathf.PI * d.R * 2f;
            switch (fit)
            {
                case DrinkCanLabelFit.KeepAspect:
                {
                    float labelH = circ / aspect, mid = (y0 + y1) * 0.5f;
                    if (labelH >= len)
                    {
                        // image taller than the body: crop top and bottom evenly
                        sections.Add(Label(y0, y1, 0.5f + (y0 - mid) / labelH, 0.5f + (y1 - mid) / labelH, 1f));
                    }
                    else
                    {
                        float b0 = mid - labelH * 0.5f, b1 = mid + labelH * 0.5f;
                        sections.Add(Metal(new List<Vector2> { new Vector2(d.R, y0), new Vector2(d.R, b0) }));
                        sections.Add(Label(b0, b1, 0f, 1f, 1f));
                        sections.Add(Metal(new List<Vector2> { new Vector2(d.R, b1), new Vector2(d.R, y1) }));
                    }
                    break;
                }
                case DrinkCanLabelFit.Repeat:
                {
                    float k = Mathf.Max(1f, Mathf.Round(circ / (len * aspect)));
                    sections.Add(Label(y0, y1, 0f, 1f, k));
                    break;
                }
                default:
                    sections.Add(Label(y0, y1, 0f, 1f, 1f));
                    break;
            }

            Section Label(float ya, float yb, float va, float vb, float uScale) => new Section
            {
                submesh = SubmeshLabel,
                uv = UvMode.Label,
                pts = new List<Vector2> { new Vector2(d.R, ya), new Vector2(d.R, yb) },
                v = new List<float> { va, vb },
                uScale = uScale,
            };
        }

        // ---- revolve --------------------------------------------------------------------------------------

        // Baked ambient occlusion for profile faces, picked from the atlas brightness ramp.
        static float ProfileAo(Dims d, AoKind kind, Vector2 p)
        {
            switch (kind)
            {
                case AoKind.Neck:
                    // crevice under the seam overhang
                    return Mathf.Lerp(0.78f, 1f, Mathf.Clamp01((d.H - SeamHeight - p.y) / 3f));
                case AoKind.Interior:
                    return 0.55f;
                case AoKind.Seam:
                    if (p.y <= d.H - SeamHeight + 0.1f && p.x < d.seamR - 0.2f) return 0.72f;   // seam underside
                    if (p.x >= d.seamInR - 0.01f) return 1f;                                     // outer seam and top
                    float t = Mathf.Clamp01((p.y - d.csY) / (d.H - d.csY));
                    return 0.58f + 0.42f * Mathf.Pow(t, 0.7f);                                    // chuck wall -> countersink
                default:
                    return 1f;
            }
        }

        static Vector2 RampUv(float a, float ao)
        {
            float v = Mathf.Lerp(AtlasRampV0, AtlasRampV1, Mathf.InverseLerp(0.55f, 1f, ao));
            return new Vector2(AtlasRampU + a * 0.004f, v);
        }

        static Vector2 Planar(Dims d, float x, float z) => new Vector2(x, z) / d.tileMm;

        static void Revolve(Buf buf, Section sec, Dims d, int segments, ref float arc)
        {
            var pts = sec.pts;
            int n = pts.Count;
            if (n < 2) return;

            // arc length along the profile -> UV0 v (continuous across sections)
            var s = new float[n];
            if (sec.uv != UvMode.Bottom)
            {
                s[0] = arc;
                for (int i = 1; i < n; i++) s[i] = s[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
                arc = s[n - 1];
            }

            // smooth normals inside a section; sections meet with duplicated vertices
            var n2 = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var t = pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Max(i - 1, 0)];
                n2[i] = new Vector2(t.y, -t.x).normalized;
            }

            int ringLen = segments + 1;
            int start = buf.pos.Count;
            float botScale = AtlasBotRefR / d.R;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j <= segments; j++)
                {
                    float a = j / (float)segments;
                    float th = a * Mathf.PI * 2f;
                    float c = Mathf.Cos(th), sn = Mathf.Sin(th);
                    var p = new Vector3(pts[i].x * c, pts[i].y, pts[i].x * sn);
                    var nn = new Vector3(n2[i].x * c, n2[i].y, n2[i].x * sn);
                    Vector2 uv0, uv1;
                    switch (sec.uv)
                    {
                        case UvMode.Label:
                            uv0 = uv1 = new Vector2(a * sec.uScale, sec.v[i]);
                            break;
                        case UvMode.Bottom:
                            // seen from below the plane is mirrored: flip Z so the date code reads correctly
                            uv1 = AtlasBotC + new Vector2(p.x * botScale * AtlasBotPpm / AtlasW, -p.z * botScale * AtlasBotPpm / AtlasH);
                            uv0 = Planar(d, p.x, -p.z);
                            break;
                        default:
                            uv1 = RampUv(a, ProfileAo(d, sec.ao, pts[i]));
                            uv0 = new Vector2(a * d.tileN, s[i] / d.tileMm);
                            break;
                    }
                    buf.Add(p, nn, uv0, uv1);
                }
            }

            for (int i = 1; i < n; i++)
            {
                int prev = start + (i - 1) * ringLen, cur = start + i * ringLen;
                for (int j = 0; j < segments; j++)
                {
                    int a = prev + j, b = prev + j + 1, c = cur + j + 1, dd = cur + j;
                    var want = buf.nrm[a] + buf.nrm[c];
                    buf.Tri(sec.submesh, dd, c, b, want);
                    buf.Tri(sec.submesh, dd, b, a, want);
                }
            }
        }

        // ---- lid panel height field -----------------------------------------------------------------------

        static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        static readonly Rect KeyholeBounds = BoundsOf(Keyhole);

        static Rect BoundsOf(Vector2[] poly)
        {
            Vector2 lo = poly[0], hi = poly[0];
            foreach (var p in poly) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
            return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
        }

        // Signed distance (mm, + inside) to a closed polygon.
        static float PolySdf(Vector2 p, Vector2[] poly)
        {
            float best = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i], e = b - a, w = p - a;
                float t = Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                best = Mathf.Min(best, (w - e * t).sqrMagnitude);
                if ((a.y > p.y) != (b.y > p.y) && p.x < e.x * (p.y - a.y) / e.y + a.x) inside = !inside;
            }
            float dist = Mathf.Sqrt(best);
            return inside ? dist : -dist;
        }

        static float KeyholeSdf(Vector2 p)
        {
            // far outside: the deboss profile is flat there anyway
            float margin = DebossSoftWidth;
            var b = KeyholeBounds;
            if (p.x < b.xMin - margin || p.x > b.xMax + margin || p.y < b.yMin - margin || p.y > b.yMax + margin)
                return -margin;
            return PolySdf(p, Keyhole);
        }

        /// <summary>Lid panel height above the panel plane (mm) at lid position (x, z) in mm.</summary>
        static float PanelField(Dims d, float x, float z)
        {
            float k = d.lidK;
            var l = new Vector2(x / k, z / k);
            float rn = Mathf.Clamp01(Mathf.Sqrt(x * x + z * z) / d.panelR);
            float h = 0.15f * (1f - rn * rn);

            // keyhole deboss panel around the tab and the tear panel (soft step; the normal map sharpens it)
            h -= DebossDepth * Smooth01(0.5f + KeyholeSdf(l) / DebossSoftWidth);

            // finger well under the tab ring
            float qx = (l.x - WellC.x) / WellA, qz = (l.y - WellC.y) / WellB;
            h -= WellDepth * Smooth01(1f - (qx * qx + qz * qz));

            // rivet button
            h += 0.55f * Smooth01(1f - l.magnitude / 2.4f);
            return h;
        }

        static Vector2 LidUv(Dims d, float x, float z) =>
            AtlasLidC + new Vector2(x / d.lidK * AtlasLidPpm / AtlasW, z / d.lidK * AtlasLidPpm / AtlasH);

        // Lid panel in two pieces that share the exact score outline:
        //   B = everything outside the tear panel (rays from the panel edge to the tear outline),
        //   A = the tear panel (same rays continued to TearCenter) on its own bone, so it can fold into the can.
        static void AddLidPanel(Buf buf, Dims d, int outerSegments)
        {
            const int Rays = 128, RingsB = 26, RingsA = 9;
            const float eps = 0.05f;
            float k = d.lidK;
            var c = TearCenter * k;
            var tear = TearPanel.Select(v => v * k).ToArray();

            Vector3 P(Vector2 q) => new Vector3(q.x, d.panelY + PanelField(d, q.x, q.y), q.y);
            Vector3 N(Vector2 q)
            {
                float dx = (PanelField(d, q.x + eps, q.y) - PanelField(d, q.x - eps, q.y)) / (2f * eps);
                float dz = (PanelField(d, q.x, q.y + eps) - PanelField(d, q.x, q.y - eps)) / (2f * eps);
                return new Vector3(-dx, 1f, -dz).normalized;
            }
            int AddPt(Vector2 q) => buf.Add(P(q), N(q), Planar(d, q.x, q.y), LidUv(d, q.x, q.y));

            // ring 0 = panel edge with the revolve's segment count (no cracks)
            int ring0 = buf.pos.Count;
            for (int j = 0; j < outerSegments; j++)
            {
                float th = j / (float)outerSegments * Mathf.PI * 2f;
                AddPt(new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * d.panelR);
            }

            // rays: outer point just inside the edge -> where the ray toward TearCenter meets the score outline
            var outer = new Vector2[Rays];
            var inner = new Vector2[Rays];
            for (int j = 0; j < Rays; j++)
            {
                float th = j / (float)Rays * Mathf.PI * 2f;
                outer[j] = new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * (d.panelR - 0.6f);
                inner[j] = RayHit(c, (outer[j] - c).normalized, tear);
            }

            int prev = -1;
            for (int i = 0; i <= RingsB; i++)
            {
                float t = i / (float)RingsB;
                int row = buf.pos.Count;
                for (int j = 0; j < Rays; j++) AddPt(Vector2.Lerp(outer[j], inner[j], t));
                if (i == 0) Stitch(buf, ring0, outerSegments, row, Rays);
                else QuadRow(buf, prev, row, Rays);
                prev = row;
            }

            buf.curBone = BoneTear;
            prev = -1;
            for (int i = 0; i < RingsA; i++)
            {
                float t = i / (float)RingsA;
                int row = buf.pos.Count;
                for (int j = 0; j < Rays; j++) AddPt(Vector2.Lerp(inner[j], c, t));
                if (prev >= 0) QuadRow(buf, prev, row, Rays);
                prev = row;
            }
            int centre = AddPt(c);
            for (int j = 0; j < Rays; j++)
                buf.Tri(SubmeshMetal, centre, prev + j, prev + (j + 1) % Rays, Vector3.up);
            buf.curBone = BoneCan;
        }

        static void QuadRow(Buf buf, int a, int b, int n)
        {
            for (int j = 0; j < n; j++)
            {
                int j2 = (j + 1) % n;
                buf.Quad(SubmeshMetal, a + j, a + j2, b + j2, b + j);
            }
        }

        // First crossing of a ray (origin inside a star-shaped polygon) with the polygon outline.
        static Vector2 RayHit(Vector2 o, Vector2 dir, Vector2[] poly)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], e = poly[i] - a, w = a - o;
                float den = dir.x * e.y - dir.y * e.x;
                if (Mathf.Abs(den) < 1e-9f) continue;
                float t = (w.x * e.y - w.y * e.x) / den;       // along the ray
                float u = (w.x * dir.y - w.y * dir.x) / den;   // along the edge
                if (t > 1e-4f && u >= -1e-4f && u <= 1f + 1e-4f && t < best) best = t;
            }
            return best < float.MaxValue ? o + dir * best : o;
        }

        // ---- inside of the can (seen through the opening) ------------------------------------------------------

        public const float InteriorWall = 0.3f;   // mm in from the outside

        static void AddInterior(Buf buf, Dims d, int segments, ref float arc)
        {
            Revolve(buf, new Section { submesh = SubmeshMetal, uv = UvMode.Ramp, ao = AoKind.Interior, pts = InteriorProfile(d, InteriorWall) }, d, segments, ref arc);
            AddDisc(buf, d, d.domeD, d.R - InteriorWall, segments, SubmeshMetal, 0.55f);    // inner bottom
        }

        // the inner wall's profile (r, y) in mm from the top (under the countersink) down, `wall` in from the outside — also the
        // drink can plugin's dark Inside, further in (9-60)
        public static List<Vector2> InteriorProfile(Dims d, float wall)
        {
            var pts = new List<Vector2> { new Vector2(d.chuckBottomR - wall, d.csY) };
            var neck = NeckProfile(d);
            for (int i = neck.Count - 1; i >= 0; i--)
                if (neck[i].y < d.csY - 0.5f) pts.Add(new Vector2(neck[i].x - wall, neck[i].y));
            pts.Add(new Vector2(d.R - wall, d.bodyY0));
            return pts;
        }

        // Flat disc facing up (inner bottom, liquid surface).
        static void AddDisc(Buf buf, Dims d, float y, float r, int segments, int submesh, float ao)
        {
            var uv1 = RampUv(0f, ao);
            int centre = buf.Add(new Vector3(0f, y, 0f), Vector3.up, Planar(d, 0f, 0f), uv1);
            for (int j = 0; j < segments; j++)
            {
                float th = j / (float)segments * Mathf.PI * 2f;
                var q = new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * r;
                buf.Add(new Vector3(q.x, y, q.y), Vector3.up, Planar(d, q.x, q.y), uv1);
            }
            for (int j = 0; j < segments; j++)
                buf.Tri(submesh, centre, centre + 1 + j, centre + 1 + (j + 1) % segments, Vector3.up);
        }

        // Joins two concentric rings with different vertex counts (both start at angle 0).
        static void Stitch(Buf buf, int a0, int na, int b0, int nb)
        {
            int i = 0, j = 0;
            while (i < na || j < nb)
            {
                float ta = (i + 1) / (float)na, tb = (j + 1) / (float)nb;
                int ai = a0 + i % na, bj = b0 + j % nb;
                if (j >= nb || (i < na && ta <= tb))
                {
                    buf.Tri(SubmeshMetal, ai, a0 + (i + 1) % na, bj, Vector3.up);
                    i++;
                }
                else
                {
                    buf.Tri(SubmeshMetal, ai, b0 + (j + 1) % nb, bj, Vector3.up);
                    j++;
                }
            }
        }

        // ---- pull tab -------------------------------------------------------------------------------------

        static Vector2 TabUv(Dims d, Vector2 q) =>
            AtlasTabC + new Vector2((q.x / d.lidK - AtlasTabX0) * AtlasTabPpm / AtlasW, q.y / d.lidK * AtlasTabPpm / AtlasH);

        static void AddTab(Buf buf, Dims d)
        {
            int K = TabOuter.Length;   // samples around the outline (paired with the finger hole)
            const int M = 6;           // samples across each curled edge
            float k = d.lidK;
            float rc = TabThickness * 0.5f;
            float y0 = d.panelY + PanelField(d, 0f, 0f);   // tab sits on the rivet button
            float yc = y0 + rc;
            int tabStart = buf.pos.Count;

            var outer = new Vector2[K];
            var inner = new Vector2[K];
            for (int i = 0; i < K; i++)
            {
                outer[i] = TabOuter[i] * k;
                inner[i] = TabHole[i] * k;
            }
            var outerN = TabOuterN;
            var innerN = TabHoleN;     // points away from the material (into the hole)

            // closed strip of loops: outer edge top -> bottom, inner edge bottom -> top
            var loops = new List<int>();
            int Loop(Vector2[] ring, Vector2[] rn, float phi)
            {
                int b = buf.pos.Count;
                float sp = Mathf.Sin(phi), cp = Mathf.Cos(phi);
                for (int i = 0; i < K; i++)
                {
                    var q = ring[i] - rn[i] * (rc * (1f - sp));
                    var p = new Vector3(q.x, yc + rc * cp, q.y);
                    var n = new Vector3(rn[i].x * sp, cp, rn[i].y * sp).normalized;
                    buf.Add(p, n, Planar(d, q.x, q.y), TabUv(d, q));
                }
                return b;
            }
            for (int m = 0; m <= M; m++) loops.Add(Loop(outer, outerN, Mathf.PI * m / M));
            for (int m = M; m >= 0; m--) loops.Add(Loop(inner, innerN, Mathf.PI * m / M));
            for (int l = 0; l < loops.Count; l++)
            {
                int a = loops[l], b = loops[(l + 1) % loops.Count];
                // top/bottom faces (between the two edges) need their own flat normal
                bool face = l == M || l == loops.Count - 1;
                for (int i = 0; i < K; i++)
                {
                    int i2 = (i + 1) % K;
                    if (face)
                    {
                        var want = buf.nrm[a + i];
                        buf.Tri(SubmeshMetal, a + i, a + i2, b + i2, want);
                        buf.Tri(SubmeshMetal, a + i, b + i2, b + i, want);
                    }
                    else buf.Quad(SubmeshMetal, a + i, a + i2, b + i2, b + i);
                }
            }

            // bend: finger end lifts behind the rivet, nose dips onto the tear panel
            Bend(buf, tabStart, y0, 0f, -1f, 2.5f);
            Bend(buf, tabStart, y0, 2.5f * k, 1f, -5f);

            // rivet head
            const int R = 24;
            float rr = RivetHeadR * k, rh = RivetHeadH;
            float top = y0 + TabThickness;
            int prev = -1;
            for (int ring = 0; ring <= 4; ring++)
            {
                float t = ring / 4f;                 // 0 = centre, 1 = rim
                float r = rr * Mathf.Sin(t * Mathf.PI * 0.5f);
                float y = top - 0.1f + (rh + 0.1f) * Mathf.Cos(t * Mathf.PI * 0.5f);   // rim sinks into the tab (no z-fight)
                int b = buf.pos.Count;
                for (int j = 0; j < R; j++)
                {
                    float th = j / (float)R * Mathf.PI * 2f;
                    var q = new Vector2(Mathf.Cos(th), Mathf.Sin(th));
                    var n = new Vector3(q.x * Mathf.Sin(t * 1.2f), 1f, q.y * Mathf.Sin(t * 1.2f)).normalized;
                    buf.Add(new Vector3(q.x * r, y, q.y * r), n, Planar(d, q.x * r, q.y * r), TabUv(d, q * r));
                }
                if (prev >= 0)
                    for (int j = 0; j < R; j++)
                        buf.Quad(SubmeshMetal, prev + j, prev + (j + 1) % R, b + (j + 1) % R, b + j);
                prev = b;
            }
        }

        // Rotates tab vertices beyond `pivotX` (in direction `side`) about a Z-parallel line at height y0.
        static void Bend(Buf buf, int start, float y0, float pivotX, float side, float deg)
        {
            float a = deg * Mathf.Deg2Rad * side;
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            for (int v = start; v < buf.pos.Count; v++)
            {
                var p = buf.pos[v];
                float lx = p.x - pivotX;
                if (lx * side <= 0f) continue;
                float ly = p.y - y0;
                buf.pos[v] = new Vector3(pivotX + lx * ca - ly * sa, y0 + lx * sa + ly * ca, p.z);
                var n = buf.nrm[v];
                buf.nrm[v] = new Vector3(n.x * ca - n.y * sa, n.x * sa + n.y * ca, n.z);
            }
        }
    }
}
