using System.Collections.Generic;
using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// M4b-3 的第二层：把 <see cref="SettlementPlan.Layout"/> 变成网格。
    /// <b>零美术资源</b>（§10.3）：全部由 Box / Prism / Cyl / Cone 四种基元组合出来，
    /// 与 §7.3「参数化可复现」是同一条原则 —— 能被代码算出来的，不要存成美术文件。
    ///
    /// ⚠️ <b>本文件的头号风险是三角形绕序</b>。本工程已经在 <c>TribeMarkerLayer</c> 上栽过一次：
    /// 绕序反了 → 整层被背面剔除 → <b>一个像素都不画，且 Unity 一句错都不报</b>。
    /// 故这里不靠"仔细写"，而是每次都做一次<b>几何自检</b>：
    /// 每个基元都是凸体，于是"从形体中心指向三角形重心的方向"与"三角形几何法线"
    /// 必须同向 —— 反向即说明该面朝内。<see cref="InwardFaceCount"/> 记录违规数，
    /// 调用方在渲染前检查它，非零就报警。<b>这条自检能自动抓住绕序 bug，而不是靠肉眼。</b>
    ///
    /// 参考：<c>east × north = up</c> 之类的方向推导见 <c>TribeMarkerLayer.EmitQuad</c> 的注释。
    /// </summary>
    public sealed class SettlementGeometry
    {
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int>[] _tris;

        /// <summary>绕序自检发现的朝内面数。非零即为 bug，会在日志里报出来。</summary>
        public int InwardFaceCount { get; private set; }

        public int VertexCount => _verts.Count;

        public SettlementGeometry()
        {
            _tris = new List<int>[SettlementPlan.MatCount];
            for (int i = 0; i < _tris.Length; i++) _tris[i] = new List<int>();
        }

        public void Emit(SettlementPlan.Layout layout)
        {
            foreach (var b in layout.Buildings) EmitBuilding(b);
        }

        private void EmitBuilding(SettlementPlan.Building b)
        {
            int bucket = (int)b.Mat;
            switch (b.Kind)
            {
                case SettlementPlan.Kind.Hut:
                case SettlementPlan.Kind.House:
                case SettlementPlan.Kind.Tenement:
                case SettlementPlan.Kind.Kiln:
                case SettlementPlan.Kind.Smelter:
                case SettlementPlan.Kind.Forge:
                case SettlementPlan.Kind.Granary:
                case SettlementPlan.Kind.Pen:
                case SettlementPlan.Kind.Field:
                case SettlementPlan.Kind.Plaza:
                case SettlementPlan.Kind.Road:
                case SettlementPlan.Kind.Wall:
                case SettlementPlan.Kind.Tower:
                case SettlementPlan.Kind.Stele:
                case SettlementPlan.Kind.Workshop:
                case SettlementPlan.Kind.Factory:
                case SettlementPlan.Kind.Chimney:
                case SettlementPlan.Kind.Dock:
                case SettlementPlan.Kind.Theater:
                    // 主体一律是盒子 —— 这不是偷懒：土坯房、厂房、围墙、田块
                    // 在聚落视景的距离上，体量比轮廓重要得多（§10.3）。
                    AddBox(bucket, b, 0f);
                    break;

                case SettlementPlan.Kind.Tent:
                    // 帐篷 = 盒子（帐身）+ 圆锥（顶）
                    AddBox(bucket, b, 0f);
                    AddCone(bucket, Local(b, 0f, b.H, 0f), Mathf.Max(b.W, b.D) * 0.78f, b.H * 1.15f, 6, b.RotY);
                    break;

                case SettlementPlan.Kind.Fire:
                    // 火塘 = 矮圆柱（石圈）
                    AddCyl(bucket, Local(b, 0f, 0f, 0f), b.W * 0.5f, b.H, 8, b.RotY);
                    break;
            }

            // 屋顶：住人的房子才有，且屋脊沿进深方向。
            // ⚠️ <see cref="Local"/> 的 y 是<b>绝对高度</b>（不是相对偏移），
            //    故这里必须给 b.H（墙顶），给 b.H*0.5 会让屋顶长在墙腰上。
            if (b.Kind == SettlementPlan.Kind.House || b.Kind == SettlementPlan.Kind.Hut
                || b.Kind == SettlementPlan.Kind.Theater)
                AddPrism((int)SettlementPlan.Mat.Thatch, Local(b, 0f, b.H, 0f),
                         b.W * 1.08f, b.D * 1.08f, b.W * 0.42f, b.RotY);

            // 烟囱：锻炉一根，从地面直窜过屋顶 —— 远距离就看得出"这里有火"
            if (b.Kind == SettlementPlan.Kind.Forge)
                AddCyl((int)SettlementPlan.Mat.Metal, Local(b, b.W * 0.32f, 0f, 0f),
                       b.W * 0.09f, b.H * 1.6f, 6, b.RotY);

            // 公仓：架高的柱子（§10.4 的「架高＋锥顶」）
            if (b.Kind == SettlementPlan.Kind.Granary)
                AddCone((int)SettlementPlan.Mat.Thatch, Local(b, 0f, b.H, 0f),
                        b.W * 0.72f, b.H * 0.75f, 6, b.RotY);
        }

        /// <summary>构件本地坐标 → 聚落坐标。先绕 Y 转 RotY，再平移 (X, Z)。</summary>
        private static Vector3 Local(SettlementPlan.Building b, float lx, float ly, float lz)
        {
            float r = b.RotY * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector3(b.X + lx * c + lz * s, ly, b.Z - lx * s + lz * c);
        }

        // ══════════════════════════════════════════════════════════════
        //  基元
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 长方体。<paramref name="yBase"/> 是底面高度（−0.5 倍高即"贴地"）。
        ///
        /// 六个面的绕序<b>逐个手推验证过</b>（取 hx=hy=hz=1 的立方体，
        /// 每个面算 (b−a)×(c−a) 看法线是否等于该面的外法线）：
        /// +Z 的 (v001,v101,v111,v011) → (0,0,4) ✓ · −Z 的 (v100,v000,v010,v110) → (0,0,−4) ✓
        /// +X 的 (v101,v100,v110,v111) → (4,0,0) ✓ · −X 的 (v000,v001,v011,v010) → (−4,0,0) ✓
        /// +Y 的 (v010,v011,v111,v110) → (0,4,0) ✓ · −Y 的 (v000,v100,v101,v001) → (0,−4,0) ✓
        /// 且自检（<see cref="InwardFaceCount"/>）会再兜一层。
        /// </summary>
        private void AddBox(int bucket, SettlementPlan.Building b, float yBase)
        {
            float hx = b.W * 0.5f, hy = b.H * 0.5f, hz = b.D * 0.5f;
            Vector3 ctr = Local(b, 0f, yBase + hy, 0f);
            // 形体中心用于自检：取盒子中心
            var center = ctr;

            Vector3 P(float x, float y, float z) => Local(b, x, yBase + hy + y, z);

            var v000 = P(-hx, -hy, -hz); var v100 = P(hx, -hy, -hz);
            var v110 = P(hx, hy, -hz); var v010 = P(-hx, hy, -hz);
            var v001 = P(-hx, -hy, hz); var v101 = P(hx, -hy, hz);
            var v111 = P(hx, hy, hz); var v011 = P(-hx, hy, hz);

            Quad(bucket, v001, v101, v111, v011, center);   // +Z
            Quad(bucket, v100, v000, v010, v110, center);   // −Z
            Quad(bucket, v101, v100, v110, v111, center);   // +X
            Quad(bucket, v000, v001, v011, v010, center);   // −X
            Quad(bucket, v010, v011, v111, v110, center);   // +Y
            Quad(bucket, v000, v100, v101, v001, center);   // −Y
        }

        /// <summary>三棱柱屋顶。屋脊沿 <b>Z</b>（进深）方向，底面在 <paramref name="basePos"/> 的 y 上。</summary>
        private void AddPrism(int bucket, Vector3 basePos, float w, float d, float h, float rotY)
        {
            float hx = w * 0.5f, hz = d * 0.5f;
            float r = rotY * Mathf.Deg2Rad;
            float cs = Mathf.Cos(r), sn = Mathf.Sin(r);
            Vector3 P(float x, float y, float z) =>
                new Vector3(basePos.x + x * cs + z * sn, basePos.y + y, basePos.z - x * sn + z * cs);

            var a0 = P(-hx, 0, -hz); var b0 = P(hx, 0, -hz); var t0 = P(0, h, -hz);   // 近端三角
            var a1 = P(-hx, 0, hz); var b1 = P(hx, 0, hz); var t1 = P(0, h, hz);     // 远端三角

            var center = new Vector3(basePos.x, basePos.y + h * 0.35f, basePos.z);

            // 两坡面（外法线朝斜上）
            Quad(bucket, a1, t1, t0, a0, center);   // 左坡
            Quad(bucket, t1, b1, b0, t0, center);   // 右坡
            // 两个山墙（三角端面）
            Tri(bucket, a0, t0, b0, center);        // 近端，外法线 −Z
            Tri(bucket, b1, t1, a1, center);        // 远端，外法线 +Z
            // ⚠️ 底面**故意不画**：它与墙顶完全重合，画了只会 z-fighting。
        }

        /// <summary>圆柱。轴沿 Y。</summary>
        private void AddCyl(int bucket, Vector3 basePos, float radius, float height, int segments, float rotY)
        {
            segments = Mathf.Clamp(segments, 3, 32);
            var center = new Vector3(basePos.x, basePos.y + height * 0.5f, basePos.z);

            Vector3 Rim(int i, float y)
            {
                float a = (i % segments) * (Mathf.PI * 2f / segments) + rotY * Mathf.Deg2Rad;
                return new Vector3(basePos.x + Mathf.Cos(a) * radius, basePos.y + y, basePos.z + Mathf.Sin(a) * radius);
            }

            for (int i = 0; i < segments; i++)
                Quad(bucket, Rim(i + 1, 0), Rim(i, 0), Rim(i, height), Rim(i + 1, height), center);

            // 顶盖：扇形三角，绕序使法线朝 +Y。
            // ⚠️ 这两个 Rim 的**先后不能换**。原先写的是 (topC, Rim(i), Rim(i+1))，
            //    叉积算出来是 (0, −0.707r², 0) —— <b>法线朝下</b>，顶盖整个朝内。
            //    本文件的自检第一次跑就抓到了它：报「14 个面朝内」，
            //    而 14 = 火塘的 8 段 + 锻炉烟囱的 6 段，两个圆柱的顶盖一格不差。
            //    换序后 (b−a)×(c−a) = (0, +0.707r², 0) ✓。
            //    —— 这就是那条自检存在的理由：绕序错了不报错、不崩，只是**少画一块**，
            //    靠肉眼在图上是找不出来的。
            var topC = new Vector3(basePos.x, basePos.y + height, basePos.z);
            for (int i = 0; i < segments; i++) Tri(bucket, topC, Rim(i + 1, height), Rim(i, height), center);
        }

        /// <summary>圆锥。轴沿 Y，顶点在上。</summary>
        private void AddCone(int bucket, Vector3 basePos, float radius, float height, int segments, float rotY)
        {
            segments = Mathf.Clamp(segments, 3, 32);
            var apex = new Vector3(basePos.x, basePos.y + height, basePos.z);
            var center = new Vector3(basePos.x, basePos.y + height * 0.25f, basePos.z);

            Vector3 Rim(int i)
            {
                float a = (i % segments) * (Mathf.PI * 2f / segments) + rotY * Mathf.Deg2Rad;
                return new Vector3(basePos.x + Mathf.Cos(a) * radius, basePos.y, basePos.z + Mathf.Sin(a) * radius);
            }

            for (int i = 0; i < segments; i++) Tri(bucket, Rim(i + 1), Rim(i), apex, center);
        }

        // ══════════════════════════════════════════════════════════════
        //  低层写入 + 绕序自检
        // ══════════════════════════════════════════════════════════════

        private void Quad(int bucket, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 center)
        {
            Tri(bucket, a, b, c, center);
            Tri(bucket, a, c, d, center);
        }

        private void Tri(int bucket, Vector3 a, Vector3 b, Vector3 c, Vector3 center)
        {
            // ── 绕序自检 ──────────────────────────────────────────────
            // 基元都是凸体，故「形体中心 → 三角形重心」与「三角形几何法线」必须同向。
            // 反向 ⇒ 这个面朝内 ⇒ 会被背面剔除掉（正是 TribeMarkerLayer 栽过的坑）。
            // 用叉积长度判退化：共线的三角形没有法线，跳过不报。
            Vector3 geo = Vector3.Cross(b - a, c - a);
            if (geo.sqrMagnitude > 1e-9f)
            {
                Vector3 mid = (a + b + c) / 3f;
                if (Vector3.Dot(geo, mid - center) < 0f) InwardFaceCount++;
            }

            int v = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c);
            _normals.Add(Vector3.Normalize(geo)); _normals.Add(Vector3.Normalize(geo)); _normals.Add(Vector3.Normalize(geo));
            _uvs.Add(new Vector2(0, 0)); _uvs.Add(new Vector2(1, 0)); _uvs.Add(new Vector2(0, 1));

            _tris[bucket].Add(v); _tris[bucket].Add(v + 1); _tris[bucket].Add(v + 2);
        }

        /// <summary>
        /// 把攒好的四个列表直接交出去（不拷贝）。
        ///
        /// 之所以不给 <c>ToMesh</c>，是因为时间轴一拖就是<b>每帧重建</b>：
        /// 每帧 new 一个 Mesh 会把内存吃光，正确做法是复用同一个 Mesh 对象、
        /// 逐帧只改它的顶点数组。于是"建 Mesh"这件事归调用方管，这里只交数据。
        /// </summary>
        public void CopyTo(out List<Vector3> verts, out List<Vector3> normals,
                           out List<Vector2> uvs, out List<int>[] tris)
        {
            verts = _verts; normals = _normals; uvs = _uvs; tris = _tris;
        }
    }
}
