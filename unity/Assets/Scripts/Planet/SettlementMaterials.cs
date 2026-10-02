using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// 聚落视景的七个材质槽，下标与 <see cref="SettlementPlan.Mat"/> 一一对应。
    ///
    /// 与 <see cref="MarkerMaterials"/> 的两点不同，都是有意的：
    /// <list type="number">
    ///   <item>这里用 <b>Lit（受光）</b>而不是 Unlit。标记是<b>符号</b>，符号要恒定的颜色才认得出；
    ///   聚落是<b>实体</b>，实体要靠明暗才能看出体量与朝向 —— 一排房子若都是平涂色块，
    ///   看上去就是一堆纸片，那正是作者说的"一张旋转的图片"的味道。</item>
    ///   <item>颜色取<b>低饱和的土色系</b>。上一版标记配色栽在"颜色和地形撞了"（见
    ///   <see cref="MarkerMaterials"/> 的注释），但聚落这一层恰恰相反：
    ///   它就该像土里长出来的，不需要跳出来。</item>
    /// </list>
    /// </summary>
    public static class SettlementMaterials
    {
        public const string Folder = "Assets/Settings/Materials/Settlement";

        public static readonly string[] AssetNames =
        {
            "Set_Ground.mat", "Set_Thatch.mat", "Set_Mud.mat", "Set_Stone.mat",
            "Set_Timber.mat", "Set_Metal.mat", "Set_Crop.mat",
        };

        public static readonly Color[] Colors =
        {
            new Color(0.40f, 0.34f, 0.27f),   // Ground 夯土路面
            new Color(0.68f, 0.58f, 0.32f),   // Thatch 茅草／兽皮
            new Color(0.52f, 0.40f, 0.29f),   // Mud    土坯墙
            new Color(0.55f, 0.54f, 0.52f),   // Stone  石／砖
            new Color(0.36f, 0.25f, 0.16f),   // Timber 木
            new Color(0.33f, 0.36f, 0.40f),   // Metal  金属／工业
            new Color(0.42f, 0.60f, 0.22f),   // Crop   作物
        };

        /// <summary>建一个受光材质。URP 与内置管线的 shader 名不同，故运行期探测。</summary>
        public static Material Create(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse");

            var mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            // 粗糙、不反光 —— 土墙与茅草没有高光。金属那档稍微亮一点，否则和石头分不开。
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.08f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            return mat;
        }

        public static Material[] Resolve()
        {
            var list = new Material[AssetNames.Length];
            for (int i = 0; i < list.Length; i++)
            {
                string path = $"{Folder}/{AssetNames[i]}";
#if UNITY_EDITOR
                list[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
#else
                list[i] = null;
#endif
                if (list[i] == null) list[i] = Create(Colors[i]);
            }
            return list;
        }
    }
}
