# Humen · Unity 侧（看得见的蓝星）

这是 `design.md` §10 的可视化落地。**M4b-1 / M4b-2 / M4b-3 均已完成**：
工程可开、URP 已挂、蓝星渲得出来、河流与部落标记在图上、可按 `Tab` 降落到 C 级聚落场景。

## 怎么看到球

1. Unity Hub → **Add** → **Add project from disk** → 选 `E:\cc\humen\unity`
2. 打开（首次导入约 1~3 分钟，会把 4096×2048 贴图编进 Library）
3. 打开场景 `Assets/Scenes/Planet.unity` —— **不用按 Play 就能看见球**
4. 按 **Play** 后见下表

### 操作

| 操作 | 效果 |
|---|---|
| **左键拖拽** | **转蓝星** —— 按住的那块地皮一直黏在光标底下（照 CesiumJS 的 `rotate3D`），松手有惯性 |
| **右键拖拽** | 拉远近（乘性：往下拖 = 拉近） |
| **中键拖拽** | 转视角（tilt。缩放到贴地之后还得能换个角度看大陆） |
| 滚轮 | 拉远近（同上，限在 §10.1 的 A/B 两级，1.005R ~ 4.0R） |
| **`F`** | **复位** —— 机架回开机位姿 · 星球自转回开机角度 · 惯性清零（三件事，缺一样就"看着差不多、其实还歪着"） |
| **`R`** | **开 / 停地球自转**（HUD 上有"自转：开/停"读数） |
| **`Tab`** | 降落 / 返回（切到 C 级聚落场景） |
| 左键单击 | 选中光标下的部落（有位移门限，所以"点一下"不会被当成"拨球"） |
| `空格` | 时间轴播放 / 暂停 |
| `←` `→` | 逐年拖动；`Home` / `End` 跳到两端 |

> ⚠️ **v0.27 更正：左键拖拽转的是相机，不是球。**（本段此前写的「拨的是球，不是相机 ——
> **这条是硬的**」，**是反的**，留着这句是为了记住它怎么错的。）
> 作者的原话：「拖动蓝星的时候，月亮和太阳的位置也得相应地改变……**不能改变相对关系**……
> 得有**距离与角度**的空间关系。」关键在最后半句：**"距离与角度"是三维量。**
> 转球时世界被整个冻住、屏幕上一个像素都不动 —— 而**"屏幕不动"并不蕴含"关系不变"**，
> 那个 0 恰好也是"什么都没发生"的指纹。**一个指标在两种截然相反的原因下取同一个值，它就不是判据。**
>
> 现在（转相机）：世界一位没动，所以**日地距、月地距、日-地-月夹角逐位不变**；
> 而日月在**屏上**各按自己绕球心的轨道半径走不同的量（**真视差**）。
> 两种做法在数学上是**恒等**的（`R' = d·R`、`t' = c + d·(t−c)`），所以跟手精度一分没丢
> —— 实测 0.10 / 0.11 / 0.27 / 0.36 px。详见 `design.md` §10.9.1 与 v0.27 ①②。
>
> ⚠️ **v0.28：太阳在 1012R**（月亮那 23.19 倍压缩照搬到太阳上，于是日:月的距离比、半径比、
> 角径比**全部为真**）。代价是**默认机位下日盘退到镜头后面** ——
> **首屏看不见太阳是正常态**，往夜面拖它会升进来；HUD 一直报着它在哪儿。
> 详见 `design.md` §10.9.3。

命令行也能出图（不开编辑器界面）：

```
Unity.exe -batchmode -quit -projectPath E:\cc\humen\unity ^
          -executeMethod Humen.EditorTools.PreviewRenderer.RenderBatch
```

⚠️ **不要加 `-nographics`** —— 它强制 Null 渲染设备，出来是全黑的，而且**不报错**。

### 让播放器自己出取证图

打包出来的播放器带 `-humen-capture <目录>` 启动，会依次摆好几个状态、各截一张图然后退出
（走的是**真实的**拨球与自转代码路径，不另写一套"测试专用"的转动）。
**不带这个参数时该组件完全不存在**，正常运行一点影响都没有。

```
Build\Windows\Humen.exe -humen-capture unity-capture
```

为什么非要它：**编辑器取证图与播放器画面是两条不同的路** ——
场景序列化的结果、`StreamingAssets` 与 `Resources` 的打包结果、HUD、输入，
任何一环在打包后出问题，编辑器图都**照样是好的**。

## 依赖方向（重要）

**Unity 侧一个 `Humen.Core` 的类型都不引用。** 这不是风格偏好，是硬约束：

| | |
|---|---|
| Unity 6 脚本配置 | `.NET Standard 2.1`（`Editor/Data/NetStandard/ref/2.1.0/`） |
| `Humen.Core` | `net8.0` + `Microsoft.Data.Sqlite`（含原生 `e_sqlite3`） |
| 后果 | net8.0 程序集引用 `System.Runtime 8.0.0.0`，Unity 运行时不给 → **加载即失败** |

所以边界划在**数据文件**上：Core 产出等距圆柱贴图与 `world.db`，Unity 只读文件
（运行期读 `Assets/StreamingAssets/World/world_view.json`）。
这比 §8.5 的「Core 不引用 UnityEngine」更强一层，也把 INV-34 的确定性锁在纯 C# 一侧。

## 贴图怎么来

```
bin\humen.exe planet --out unity\Assets\Resources\Planet ^
                     --res 4096 --size 256 --no-mesh --no-shells --no-graticule
```

**`--no-graticule` 不能省。** `map_biome.png` 默认带经纬网（对**地图**是对的，
看地图的人要靠它定位），但直接贴到球上就是一球面孤零零的细线。

## 世界快照怎么来

```
bin\humen.exe export                                   # 导 world_view.json 与 Assets/Data/Names
```

`world_view.json` 是 Core 与 Unity 之间**唯一的数据契约**。
换了 `world.db` 就要重导一次，否则画面里是上一个世界。

> ⚠️ 世界重跑后，**取证部落的 id 会变**。`SettlementSceneBuilder.EvidenceTribe` /
> `LandingState.DefaultTribeId` / `SettlementView.DefaultTribeId` 三处各有一份手工同步的副本
> （Editor 程序集不在运行时引用范围内），漏改一个的症状是"按 `Tab` 降落到一个早已不是前沿的部落"。

## 目录

```
unity/
├─ Assets/
│  ├─ Editor/
│  │  ├─ PlanetSceneBuilder.cs      星球场景由代码生成（不存手工状态，同 §7.3）
│  │  ├─ SettlementSceneBuilder.cs  聚落场景同上
│  │  ├─ PlayerBuilder.cs           打包播放器
│  │  ├─ UrpSetup.cs                建并挂 URP 资产
│  │  ├─ PreviewRenderer.cs         离屏出图，用于取证
│  │  ├─ TimelinePreviewRenderer.cs 时间轴逐年份出图（同机位，只有年份不同）
│  │  └─ OrbitForensics.cs          ★ 日月几何取证（离线角度、扫掠、复位、反事实）
│  ├─ Resources/Planet/             贴图（map_biome.png 是球面着色图）
│  ├─ Scenes/                       Planet.unity · Settlement.unity（都是生成的）
│  ├─ StreamingAssets/World/        world_view.json —— 与 Core 的数据契约
│  ├─ Scripts/Planet/
│  │  ├─ PlanetBootstrap.cs         贴图 → 材质 → 球；自转与 DragHold
│  │  ├─ GlobeDrag.cs               ★ 转蓝星（Cesium rotate3D 的抓取式）+ `F` 复位 + `R` 自转开关
│  │  ├─ SunMoonSystem.cs           ★ 太阳与月亮（真关系：距离、相位、潮汐锁定；太阳在 1012R）
│  │  ├─ OrbitCamera.cs             转视角，距离限在 A/B 两级
│  │  ├─ UvSphere.cs                自建等距圆柱 UV 球
│  │  ├─ PlanetGeometry.cs          地形起伏
│  │  ├─ RiverLayer.cs              河流
│  │  ├─ TribeMarkerLayer.cs        部落标记（按阶段分批）
│  │  ├─ WorldView.cs               读 world_view.json；阶段判据
│  │  ├─ WorldTimeline.cs           时间轴（`空格` / `←` `→`）
│  │  ├─ SceneSwitcher.cs           `Tab` 降落 / 返回、点击选中
│  │  ├─ LandingState.cs            降落落点（A/B/C 三级视图）
│  │  ├─ SettlementGeometry.cs      聚落几何
│  │  ├─ SettlementMaterials.cs     聚落材质
│  │  ├─ SettlementView.cs          聚落 HUD
│  │  └─ PlayerCapture.cs           播放器自取证（`-humen-capture`）
│  └─ Settings/                     URP-Asset / URP-Renderer
└─ README.md
```

## 关于 URP

`Packages/packages-lock.json` 里是：

```json
"com.unity.render-pipelines.universal": { "version": "17.0.3", "source": "builtin" }
```

`source` 是 **`builtin`** —— **URP 随编辑器一起装，不需要联网下载**。
（公开注册表 `packages.unity.com` 上查不到 17.x，那是另一回事。）

菜单 **Humen ▸ 重建星球场景** / **Humen ▸ 重建聚落场景** / **Humen ▸ 切换到 URP** / **Humen ▸ 渲染预览图**。

## 还没做

- 三级视图切换目前是 A（轨道）↔ C（聚落）**两级**，§10.2 的 B 级（大陆）还没接
- `design.md` §10.8 的**待裁 A**（海冰界线不规则化）、**待裁 B**（群系取色插值）
- `design.md` §13.4 末尾的**待裁 ㉓**：4/5 大陆停在「游群」档 ——
  这是**模拟侧**的事（`planting` 的 `geo: [可耕地]` 门槛 + 灌溉机制缺失），
  **不是渲染故障**。在它被裁定之前，三维世界上的"活生生的文明"只在赤道大陆成立。
