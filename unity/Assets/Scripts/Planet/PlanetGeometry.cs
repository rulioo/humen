using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// 经纬度 ↔ 星体局部坐标。**全工程唯一的换算处。**
    ///
    /// 为什么不把这行公式抄在各自的脚本里：<see cref="UvSphere"/> 生成网格时用的是
    /// 同一套约定，任何一处写歪（哪怕只是把 <c>sin</c> 与 <c>cos</c> 对调），
    /// 结果都是"部落标记整体飘到海里去"，而且飘得很有规律、看着像数据错了。
    /// 收敛到一处，改也只改一处。
    ///
    /// 约定与 <see cref="UvSphere.Create"/> 逐字对应：
    /// <code>
    /// lat: +π/2（北极）… −π/2（南极）
    /// lon: −π（贴图左边缘）… +π
    /// n = (cos·sin(lon), sin(lat), cos·cos(lon))   其中 cos = cos(lat)
    /// </code>
    /// 于是 <c>u = (lon+180)/360</c>、<c>v</c> 自北向南 —— 与 Core 侧
    /// <c>RenderEquirectMap</c> 产出的贴图一致（见 <c>PlanetBootstrap</c> 的说明）。
    /// </summary>
    public static class PlanetGeometry
    {
        /// <summary>经纬度（度）→ 星体局部坐标。星球中心在局部原点。</summary>
        public static Vector3 LatLonToLocal(double latDeg, double lonDeg, float radius)
        {
            double lat = latDeg * Mathf.Deg2Rad;
            double lon = lonDeg * Mathf.Deg2Rad;

            double cosLat = System.Math.Cos(lat);
            double x = cosLat * System.Math.Sin(lon);
            double y = System.Math.Sin(lat);
            double z = cosLat * System.Math.Cos(lon);

            return new Vector3((float)(x * radius), (float)(y * radius), (float)(z * radius));
        }

        /// <summary>该点的<b>外法线</b>（单位向量）。球面上就是位置归一化。</summary>
        public static Vector3 NormalAt(double latDeg, double lonDeg)
        {
            return LatLonToLocal(latDeg, lonDeg, 1f);
        }

        /// <summary>
        /// 该点的切平面基：<paramref name="east"/> 指向东、<paramref name="north"/> 指向北，
        /// 与 <paramref name="up"/>（外法线）构成右手系。
        ///
        /// 用途：把"平铺在球面上的方块"摆正 —— 标记与农田都长在地表切平面上，
        /// 不摆正的话它们会各自朝向不同方向，近看是一堆翻倒的纸片。
        /// 极点处 east 退化（东西方向无定义），此时退回一个任取的切向：
        /// 极点本来就没人住（§7.8 的选址判据排除了冰盖），不值得为它写分支。
        /// </summary>
        public static void SurfaceFrame(double latDeg, double lonDeg,
                                       out Vector3 up, out Vector3 east, out Vector3 north)
        {
            up = NormalAt(latDeg, lonDeg);

            var worldUp = Vector3.up;
            east = Vector3.Cross(worldUp, up);
            if (east.sqrMagnitude < 1e-9f)
            {
                east = Vector3.right;   // 极点兜底
            }
            east.Normalize();
            north = Vector3.Cross(up, east).normalized;
        }

        /// <summary>
        /// 沿大圆在两点之间插值（球面线性插值，slerp）。
        ///
        /// 河流折线用它加密：`rivers.spline_json` 里相邻控制点相距最远约 2°，
        /// 直接连直线的话，弦会<b>陷进球体里</b>（1000 单位半径下最深约 0.15 单位），
        /// 拉近看就是河"钻进了地下"。加密到 0.25° 一段后弦深约为 0.0024 单位，
        /// 肉眼不可见。
        /// </summary>
        public static Vector3 SlerpLocal(Vector3 a, Vector3 b, float t)
        {
            float mag = a.magnitude;
            Vector3 ua = a / mag, ub = b / b.magnitude;

            float dot = Mathf.Clamp(Vector3.Dot(ua, ub), -1f, 1f);
            float theta = Mathf.Acos(dot);

            // 两点几乎重合时 slerp 的 sin(θ) 会除零；退化成线性插值即可
            if (theta < 1e-5f) return Vector3.Lerp(a, b, t);

            float s = Mathf.Sin(theta);
            Vector3 u = (Mathf.Sin((1f - t) * theta) / s) * ua + (Mathf.Sin(t * theta) / s) * ub;
            return u * mag;
        }

        /// <summary>把经纬度写成给人看的字符串（东经/西经、北纬/南纬）。</summary>
        public static string FormatLatLon(double latDeg, double lonDeg)
        {
            string ns = latDeg >= 0 ? "北纬" : "南纬";
            string ew = lonDeg >= 0 ? "东经" : "西经";
            return $"{ns} {System.Math.Abs(latDeg):0.##}° · {ew} {System.Math.Abs(lonDeg):0.##}°";
        }
    }
}
