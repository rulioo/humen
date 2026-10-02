using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// M4b-1：把 Humen.Core 导出的等距圆柱贴图，贴成一颗看得见的蓝星。
    ///
    /// <b>依赖方向</b>：本脚本<b>不引用</b> <c>Humen.Core</c>，一个类型都不碰。
    /// 原因是硬的 —— Unity 6 的脚本配置是 .NET Standard 2.1
    /// （<c>Editor/Data/NetStandard/ref/2.1.0/</c>），而 Humen.Core 是 <c>net8.0</c>
    /// 且带 <c>Microsoft.Data.Sqlite</c>（含原生 <c>e_sqlite3</c>）。
    /// net8.0 程序集引用 <c>System.Runtime 8.0.0.0</c>，Unity 的运行时不给这个版本，
    /// 引用即加载失败。所以边界划在<b>数据文件</b>上：
    /// Core 只负责产出 <c>map_biome.png</c> 这类等距圆柱图与 <c>world.db</c>，
    /// Unity 侧只读文件。这比 §8.5 的「Core 不引用 UnityEngine」更强一层。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlanetBootstrap : MonoBehaviour
    {
        [Header("星球")]
        [Tooltip("半径（Unity 单位）。§10.1：Sphere.Radius = 1000。")]
        public float Radius = 1000f;

        [Tooltip("经度方向分段数。")]
        public int Segments = 128;

        [Tooltip("纬度方向分段数。")]
        public int Rings = 64;

        [Tooltip("等距圆柱贴图（u = 经度，v = 纬度）。留空则从 Resources/Planet/map_biome 载入。")]
        public Texture2D SurfaceTexture;

        [Header("自转")]
        [Tooltip("每秒自转的角度。设为 0 即静止。")]
        public float DegreesPerSecond = 6f;

        [Tooltip("自转开关。作者要的就是它 —— 运行时按 R 切换（见 GlobeDrag）。")]
        public bool AutoRotate = true;

        /// <summary>
        /// 拖拽期间由 <see cref="GlobeDrag"/> 置位，<b>暂停</b>自转，但不改 <see cref="AutoRotate"/>。
        ///
        /// <b>为什么必须暂停：</b>按住左键拨地球时，手指底下那颗球如果还在自己转，
        /// 手感立刻就散了 —— 你会觉得"我推的是镜头，球自己在走"，
        /// 而不是"这个地球在我手里"。松手后恢复，开关的意图一点没丢。
        ///
        /// <b>为什么是两个标志位而不是一个：</b>一个标志位表达不了
        /// "用户把自转关了" 与 "用户正按着球" 这两件互相独立的事 ——
        /// 合成一个的话，按住球再松手就会把用户关掉的自转又打开。
        /// </summary>
        [System.NonSerialized] public bool DragHold;

        private void Awake()
        {
            Build();
        }

        /// <summary>按当前参数重建星球。Editor 下的菜单也走这里，保证两条路一致。</summary>
        public void Build()
        {
            var mesh = UvSphere.Create(Radius, Segments, Rings);

            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BuildMaterial();
        }

        private Material BuildMaterial()
        {
            var tex = SurfaceTexture != null
                ? SurfaceTexture
                : Resources.Load<Texture2D>("Planet/map_biome");

            if (tex == null)
            {
                Debug.LogWarning(
                    "[Humen] 没找到球面贴图。请先跑 `humen planet --res 4096 --out unity/Assets/Resources/Planet`，" +
                    "或在 Inspector 里手工指定 SurfaceTexture。");
            }

            var shader = ResolveShader();
            var mat = new Material(shader) { name = "HumenPlanetSurface" };

            if (tex != null)
            {
                // 经度方向必须 Repeat —— 贴图左右边缘是 −180° 与 +180°，是同一条经线，
                // 用 Clamp 会在接缝处拉出一条竖条纹。
                tex.wrapModeU = TextureWrapMode.Repeat;
                tex.wrapModeV = TextureWrapMode.Clamp;

                mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            }

            // 星球是自发光体级别的大面积漫反射面，金属度归零、粗糙度拉高，
            // 免得高光把大陆洗白。
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.05f);

            return mat;
        }

        /// <summary>
        /// URP 与内置管线的 shader 名字不同，运行期探测一次。
        /// 用 <c>Shader.Find</c> 而不是硬编码分支，是为了管线换掉时这里不用改。
        /// </summary>
        private static Shader ResolveShader()
        {
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp != null) return urp;

            var std = Shader.Find("Standard");
            if (std != null) return std;

            // 最后兜底：只要能把贴图显出来就行
            return Shader.Find("Unlit/Texture");
        }

        private void Update()
        {
            if (!AutoRotate || DragHold || DegreesPerSecond == 0f) return;

            // 绕**自身** Y 轴（地轴）转。用 Space.Self 而不是 Space.World ——
            // 拨过球之后地轴已经不在世界 Y 上了，绕世界 Y 转会让球"歪着打转"。
            // 地球仪被拨斜了，也仍然是绕自己那根轴转的。
            transform.Rotate(Vector3.up, DegreesPerSecond * Time.deltaTime, Space.Self);
        }

        /// <summary>
        /// 一个"星球日"是多少秒。<see cref="SunMoonSystem"/> 用它把月亮的公转周期
        /// （27.32 日）折算成秒 —— 这条换算关系是硬的：月亮周期与自转周期之比
        /// 必须真的是 27.32，否则"还原真实的关系"就只是一句口号。
        /// </summary>
        public float SecondsPerDay =>
            DegreesPerSecond == 0f ? 60f : 360f / Mathf.Abs(DegreesPerSecond);
    }
}
