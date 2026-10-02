using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// 等距圆柱（equirectangular）UV 的球体网格。
    ///
    /// 为什么不直接用 <c>GameObject.CreatePrimitive(PrimitiveType.Sphere)</c>：
    /// Unity 内置球体的 UV 并非等距圆柱 —— 经度是沿着一条接缝展开的，
    /// 且两极的三角形扇退化得很厉害。而 Humen.Core 导出的贴图
    /// （<c>map_biome.png</c>，由 <c>PlanetRenderer.RenderEquirectMap</c> 产出）
    /// 是标准的 <b>u = 经度、v = 纬度</b> 等距圆柱图，贴到内置球上会错位。
    ///
    /// 这里自己生成：顶点按 <c>(lat, lon)</c> 规则排布，UV 直接由经纬度归一化得到，
    /// 与 Core 侧的采样约定一一对应，不存在"看着差不多"的余地。
    /// </summary>
    public static class UvSphere
    {
        /// <summary>
        /// 生成球体网格。
        /// </summary>
        /// <param name="radius">半径（Unity 单位）。</param>
        /// <param name="segments">经度方向分段数（绕一圈）。</param>
        /// <param name="rings">纬度方向分段数（从北极到南极）。</param>
        public static Mesh Create(float radius, int segments = 128, int rings = 64)
        {
            segments = Mathf.Max(3, segments);
            rings = Mathf.Max(2, rings);

            // 顶点数：(rings + 1) 行 × (segments + 1) 列。
            // 经度方向多一列是必须的 —— 最后一列与第一列位置重合、但 u 从 1 回到 0，
            // 接缝才不会把贴图整个抹歪。
            int cols = segments + 1;
            int rows = rings + 1;
            var vertices = new Vector3[rows * cols];
            var uvs = new Vector2[rows * cols];
            var normals = new Vector3[rows * cols];

            for (int r = 0; r < rows; r++)
            {
                // v: 0 = 北极, 1 = 南极（与等距圆柱图的"上北下南"一致）
                float v = (float)r / rings;
                float lat = (0.5f - v) * Mathf.PI;          // +π/2 .. −π/2
                float cosLat = Mathf.Cos(lat);
                float sinLat = Mathf.Sin(lat);

                for (int c = 0; c < cols; c++)
                {
                    float u = (float)c / segments;
                    // 贴图左边缘是 −180°，所以从 −π 起算
                    float lon = (u - 0.5f) * 2f * Mathf.PI;

                    var n = new Vector3(
                        cosLat * Mathf.Sin(lon),
                        sinLat,
                        cosLat * Mathf.Cos(lon));

                    int i = r * cols + c;
                    normals[i] = n;
                    vertices[i] = n * radius;
                    uvs[i] = new Vector2(u, 1f - v);   // Unity 的 v 轴朝上，故翻转
                }
            }

            // 三角形：两极处仍按四边形切两刀，退化的那一片面积为零、不产生视觉瑕疵，
            // 但省掉了单独处理极点扇形的分支。
            var tris = new int[segments * rings * 6];
            int t = 0;
            for (int r = 0; r < rings; r++)
            {
                for (int c = 0; c < segments; c++)
                {
                    int a = r * cols + c;
                    int b = a + 1;
                    int d = a + cols;
                    int e = d + 1;

                    tris[t++] = a; tris[t++] = d; tris[t++] = b;
                    tris[t++] = b; tris[t++] = d; tris[t++] = e;
                }
            }

            var mesh = new Mesh
            {
                name = "HumenUvSphere",
                indexFormat = vertices.Length > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
