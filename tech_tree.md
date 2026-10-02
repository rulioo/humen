# 人类科技树（文明节点库）tech_tree

> 配套 `design.md` §6「文明层：知识树 + 创新引擎」。
> 本文件规定"人类**可能**做出哪些东西、彼此**先后约束**、需要什么**手/料/动机**"，**不规定**"哪一年、由谁做出"——那交给模拟引擎在 10 万人的世界里涌现（见 design.md §6.4）。
> 文件名用 `.md` 是为了**人读与注释方便**：每个节点是一段 fenced YAML，未来引擎可直接把整段抠出来当配置（或转成 `.yaml`/`.json`）。引擎只认 `pre`、`skill`、`geo`、`gate` 等结构化字段；`note` 仅供人类阅读。
> 首批节点覆盖 **req.txt 的全部清单** + 让链条成立所必需的"结构性中间件"。随时可按 §模板 追加。

---

## 1. 字段字典

| 字段 | 含义 | 缺省 |
|---|---|---|
| `id` | 节点唯一英文 id（pre 里引用它） | 必填 |
| `cn` | 中文名 | 必填 |
| `era` | 阶段 L0~L8（仅排序/叙述参考，**非硬约束**） | 必填 |
| `cat` | 类别：能源 / 工具 / 材料 / 工艺 / 农业 / 畜牧 / 交通 / 通讯 / 建筑 / 文字 / 历法 / 音乐 / 化学 / 光学 / 电力 / 电子 / 计算机 / 网络 / 航天 / 媒体 / 文化 | — |
| `kind` | `milestone`=req 点名的文明产物；`bridge`=让链成立的结构性中间件；`base`=最底层知识 | milestone |
| `pre` | 前置节点 id 列表；**全部满足**才可能被"想出来" | [] |
| `skill` | 相关技能域达标线 `{域: 0~100}`，见技能域表 | {} |
| `geo` | 地理/原料要求：该个体所在部落境内需有 | [] |
| `gate` | 文明能力门槛（人口/专职/识字/人均能量…，见阶段表），入引擎后按聚合指标算 | — |
| `tag` | 最易催生它的动机/压力标签（喂给创新引擎的"需求拉动"） | [] |
| `adopt` | 传播难易：`fast` 口耳即传 / `mid` 需示范 / `slow` 需长期师徒 / `ritual` 需文化接纳 | fast |
| `cost` | **负外部性**（v0.4 新增，见附录 D）：该技术的代价向量 `{项: 0~1}`。**缺省 = 无代价** | {} |
| `note` | 一句话人话说明 | — |

## 2. 受控词表

**技能域（skill 的 key）**：`fire`用火控温 · `stone`石工 · `clay`陶与窑 · `fiber`编与织 · `wood`木作车船 · `plant`农与驯植 · `animal`畜牧驯养 · `ore`找矿辨石 · `metal`冶与锻 · `build`营造 · `music`音乐 · `letters`文字读写 · `math`记数与几何 · `astro`观天历法 · `chem`化学 · `optics`光学 · `elec`电与磁 · `mech`机械 · `semi`半导体材料 · `cs`程序网络算法 · `aero`推进与空气动力 · `bio`医与生保 · `comm`电信。

**动机标签（tag）**：`cold`御寒 · `hunger`果腹 · `storage`储存 · `tool`工具 · `hunt`狩猎 · `trade`交换 · `ritual`祭祀 · `prestige`声望 · `war`战争 · `transport`移动 · `comm`通讯 · `curiosity`好奇 · `leisure`闲暇 · `energy`能量 · `light`照明 · `record`记录 · `heal`医疗 · `city`城居。

**传播难易（adopt）**：见字段字典。

**代价项（cost 的 key）**——v0.4 新增，见附录 D：
`carbon` 碳排放（累积 → 全球升温，**唯一全局性项**） · `land` 生态破坏/土地退化 · `deplete` 资源枯竭 · `noise` 信息噪声 · `unrest` 社会撕裂。

**阶段门槛（era + gate 参考）**——每段该"文明能力"大概到了什么线，才会密集出该段发明（实际由 design.md §6.3 聚合指标涌现判定）：

| era | 名称 | 大致的文明能力门槛 |
|---|---|---|
| L0 | 元年·火与粗石器 | 直立行走、原始语言、打制石器、火种 |
| L1 | 游猎强化 | 御寒、绳索、弓与熟食、盐 |
| L2 | 定居·农牧·陶 | 定居与剩余食物、陶窑、纺织、驯养、出现"专职工匠"萌芽 |
| L3 | 青铜·都市·记数 | 专职化上升、聚落上千、矿物认知、记数、历法、私有观念 |
| L4 | 铁器·古典 | 城邦/国家雏形、文字读写圈、市场与长途贸易、远航 |
| L5 | 纸·复制知识 | 识字率上升、纸张廉价化、雕版/活字让知识可复制 |
| L6 | 火药·远航·科学 | 远洋、透镜、机械、近代化学与实验精神 |
| L7 | 工业·电气·化学 | 人均能量跃升（煤/蒸汽/电）、工厂制度、化工 |
| L8 | 电子·信息·航天 | 半导体、计算机、网络、航天、AI 与量子萌芽 |
| L9 | 深地·星际·量子 | 核能、深地全自动采矿、地月地火常态航线、量子算力与跨星际通讯——文明从行星尺度走向星际尺度 |

> 说明：era 只是章节分组用的"人类史上的大致先后"，**真正决定某部落何时能做出某物的，是 `pre` 前置链 + `skill`/`geo` 硬门槛 + `gate` 能力门槛**。引擎绝不看 era 字段行事。
>
> **v0.4**：上表 L7 起的"人均能量跃升"即 `design.md` §4.5 的 **P3 主轴** ——
> 各力的 `gate` 最终折算为该轴上的一个标度（半定量标度表见 `design.md` §4.5，
> "支配 ≠ 消耗"的澄清亦同处）。

## 3. 追加节点模板

```yaml
- id: my_new_thing
  cn: 造出X
  era: L3
  cat: 工艺
  kind: milestone
  pre: [上一级1, 上一级2]
  skill: {fire: 20, metal: 30}
  geo: [黏土]
  tag: [tool]
  adopt: mid
  cost: {land: 0.3}          # 可选：该技术的负外部性，无可不写；见附录 D
  note: 一句话说明它是什么、凭什么被造出来
```

---

## L0 · 元年 · 火与粗石器

> 直立行走的原人，首先驯服两样东西：锋利的石头、不熄的火。

```yaml
- id: stone_flake
  cn: 打制石器（砍砸器/刮削器）
  era: L0
  cat: 工具
  kind: base
  pre: []
  skill: {stone: 10}
  tag: [hunt, hunger]
  note: 石头碰石头崩出刃口，万艺之母
- id: fire_use
  cn: 掌握用火
  era: L0
  cat: 能源
  kind: milestone
  pre: []
  skill: {fire: 10}
  tag: [cold, ritual]
  adopt: fast
  note: 雷击/山火取火种并看守；驱兽、御寒、烤熟之始
- id: fire_making
  cn: 人工取火（钻木/燧石）
  era: L0
  cat: 能源
  kind: milestone
  pre: [fire_use]
  skill: {fire: 20}
  tag: [cold, storage]
  adopt: mid
  note: 摆脱"等天降火"；火从此可随身带
```

## L1 · 游猎与采集强化

> 人要吃得饱、穿得暖、存得住。衣、索、弓、熟食、盐，在这段里陆续登场。

```yaml
- id: clothing_skin
  cn: 兽皮衣/缝衣
  era: L1
  cat: 工艺
  kind: bridge
  pre: [stone_flake]
  skill: {fiber: 15, stone: 10}
  tag: [cold]
  note: 刮软兽皮、骨针缝缀；御寒是第一生产力
- id: fiber_string
  cn: 绳与弦（植物纤维/兽筋捻线）
  era: L1
  cat: 工艺
  kind: bridge
  pre: []
  skill: {fiber: 15}
  tag: [storage]
  note: 编织、弓弦、装柄、捆扎都靠它
- id: spear
  cn: 长矛/投矛
  era: L1
  cat: 工具
  kind: bridge
  pre: [stone_flake, fire_use]
  skill: {wood: 10, stone: 15}
  tag: [hunt, war]
  note: 硬化矛尖配削尖木杆；集体狩猎与战争共用
- id: bow
  cn: 发明弓箭
  era: L1
  cat: 工具
  kind: milestone
  pre: [fiber_string, spear]
  skill: {wood: 25, fiber: 20}
  tag: [hunt, war]
  note: 曲木储弹、弦释能，第一次"离身杀伤"
- id: cooked_food
  cn: 开始吃熟食
  era: L1
  cat: 工艺
  kind: milestone
  pre: [fire_use]
  skill: {fire: 15}
  tag: [hunger, heal]
  note: 烤/石煮让营养与寿命同步上升（脑的燃料）
- id: salt
  cn: 开始吃盐
  era: L1
  cat: 工艺
  kind: milestone
  pre: []
  geo: [盐泉, 海盐, 岩盐]
  skill: {ore: 15}
  tag: [hunger, trade]
  note: 离海岸/盐泉远的人，会为盐走很远；盐=最早的硬通货
- id: cured_food
  cn: 腌制食物防腐
  era: L1
  cat: 工艺
  kind: milestone
  pre: [salt]
  skill: {fire: 15}
  tag: [storage]
  note: 盐渍/烟熏/风干让肉食越过饥季
- id: basket
  cn: 编筐
  era: L1
  cat: 工艺
  kind: milestone
  pre: [fiber_string]
  skill: {fiber: 20}
  tag: [storage]
  note: 植物的第二次驯服——把散物收进容器
```

## L2 · 定居·农牧·陶·纺织

> 驯化植物与动物，人才会"待着不走"。定居带来陶、织、屋、乐，与最早的匠人。

```yaml
- id: settlement
  cn: 定居聚落
  era: L2
  cat: 建筑
  kind: bridge
  pre: [planting]
  skill: {build: 10}
  tag: [city]
  note: 剩余食物让人不必逐水草而居
- id: planting
  cn: 学会种地（农业）
  era: L2
  cat: 农业
  kind: milestone
  pre: []
  geo: [可耕地]
  skill: {plant: 20}
  tag: [hunger]
  note: 撒籽到留种到选育，人口上限第一次被食物改写
- id: animal_herd
  cn: 驯养畜禽（猪/羊/鸡…）
  era: L2
  cat: 畜牧
  kind: bridge
  pre: [settlement]
  skill: {animal: 20}
  tag: [hunger, ritual]
  note: 圈养让肉与粪肥随叫随到
- id: ox_dom
  cn: 驯牛
  era: L2
  cat: 畜牧
  kind: bridge
  pre: [animal_herd]
  skill: {animal: 25}
  tag: [hunger]
  note: 力大但温顺，等待挽具与犁来兑现（牛力见 L4）
- id: horse_dom
  cn: 驯马
  era: L2
  cat: 畜牧
  kind: bridge
  pre: [animal_herd]
  geo: [草原, 野马]
  skill: {animal: 30}
  tag: [transport]
  note: 速度与耐力，等待骑乘与马车（马力见 L4）
- id: pottery
  cn: 造出陶罐
  era: L2
  cat: 工艺
  kind: milestone
  pre: []
  geo: [黏土]
  skill: {clay: 20}
  tag: [storage, hunger]
  note: 捏塑→晒干→火烤；第一个"人造石头"，能存能煮
- id: clay_kiln
  cn: 陶窑（初代控温窑）
  era: L2
  cat: 工艺
  kind: bridge
  pre: [pottery]
  skill: {fire: 20, clay: 25}
  tag: [storage]
  note: 封顶聚热，陶的质量跃升——日后冶金的高温之根
- id: spinning
  cn: 纺线
  era: L2
  cat: 工艺
  kind: bridge
  pre: [fiber_string]
  skill: {fiber: 25}
  note: 捻锭把毛麻捻成长而匀的线
- id: loom
  cn: 织机
  era: L2
  cat: 工艺
  kind: bridge
  pre: [spinning]
  skill: {fiber: 30}
  note: 经纬交错，布料自此成幅成匹
- id: cloth
  cn: 制作布匹
  era: L2
  cat: 工艺
  kind: milestone
  pre: [loom]
  skill: {fiber: 35}
  tag: [cold, prestige]
  note: 从兽皮到织物，身物与体面同步出现
- id: mudbrick
  cn: 用土坯垒房子
  era: L2
  cat: 建筑
  kind: milestone
  pre: [settlement]
  skill: {build: 20, clay: 20}
  tag: [city]
  note: 晒干的土砖让房子从"窝"变"墙"
- id: sugar
  cn: 提炼糖（甘蔗/甜菜结晶）
  era: L2
  cat: 工艺
  kind: milestone
  pre: [pottery, planting]
  geo: [甘蔗, 甜菜]
  skill: {fire: 25, plant: 20}
  tag: [hunger, leisure]
  note: 榨汁熬煮结晶——"甜"第一次被从植物里抽出来、储存起来
- id: dugout
  cn: 独木舟
  era: L2
  cat: 交通
  kind: bridge
  pre: [fire_use]
  skill: {wood: 25, fire: 20}
  tag: [transport]
  note: 火烧掏空树干——河从此是路
- id: hafting
  cn: 石器装柄
  era: L2
  cat: 工具
  kind: bridge
  pre: [fiber_string, stone_flake]
  skill: {stone: 25, wood: 20}
  tag: [tool]
  note: 石+木+绳三位一体，杠杆臂来了
- id: stone_axe
  cn: 石斧
  era: L2
  cat: 工具
  kind: milestone
  pre: [hafting]
  skill: {stone: 35}
  tag: [tool, wood]
  note: 磨光斧刃配柄——砍树开荒的第一功臣
- id: song
  cn: 做出曲子（歌与旋律）
  era: L2
  cat: 音乐
  kind: milestone
  pre: []
  skill: {music: 20}
  tag: [ritual, leisure]
  note: 劳动号子→歌；声音第一次被"组织"成艺术品
- id: lithophone
  cn: 磬（石制打击乐器）
  era: L2
  cat: 音乐
  kind: milestone
  pre: [song, stone_flake]
  skill: {music: 25, stone: 25}
  tag: [ritual]
  note: 悬石击鸣，礼乐的钟磬之祖
- id: drum
  cn: 鼓
  era: L2
  cat: 音乐
  kind: milestone
  pre: [animal_herd, fiber_string]
  skill: {music: 25, wood: 20}
  tag: [ritual, war, comm]
  note: 蒙皮绷面：节拍、祭鼓、战鼓、传讯一物多用
```

## L3 · 青铜·都市·记数·文字

> 定居变聚落、聚落变城。人要记账、要看天、要炫富——于是矿物、历法、轮、私有与"最古老的价值"一起出现。**账记多了，符号就长成了文字。**

```yaml
- id: bellows
  cn: 皮风箱
  era: L3
  cat: 工艺
  kind: bridge
  pre: [fire_making, fiber_string]
  skill: {wood: 20, fire: 25}
  tag: [energy]
  note: 把风鼓进火里，温度从此可以"吹"上去
- id: charcoal
  cn: 木炭
  era: L3
  cat: 材料
  kind: bridge
  pre: [fire_making]
  skill: {fire: 25, wood: 20}
  tag: [energy]
  cost: {land: 0.4}
  note: 密闭闷烧去掉挥发分，留下的炭是高热且纯净的还原剂
- id: copper_ore
  cn: 认识铜矿（孔雀石/自然铜）
  era: L3
  cat: 材料
  kind: bridge
  pre: []
  geo: [铜矿]
  skill: {ore: 25}
  tag: [curiosity]
  note: 绿石头在火里流出红金属——人第一次见到"从石头里生出的金属"
- id: tin_ore
  cn: 认识锡矿
  era: L3
  cat: 材料
  kind: bridge
  pre: [copper_ore]
  geo: [锡矿]
  skill: {ore: 25}
  tag: [curiosity]
  note: 锡软而亮；单用无用，混铜即成利器（关键配方之一）
- id: high_temp_kiln
  cn: 高温窑
  era: L3
  cat: 工艺
  kind: bridge
  pre: [clay_kiln, bellows, charcoal]
  skill: {fire: 35, clay: 35}
  tag: [energy]
  note: 上抽式窑道+鼓风，突破 1000℃——金属熔化的门槛
- id: copper_smelt
  cn: 炼铜
  era: L3
  cat: 材料
  kind: bridge
  pre: [copper_ore, high_temp_kiln, charcoal]
  skill: {metal: 30, fire: 30}
  geo: [铜矿]
  cost: {land: 0.3}
  note: 熔出纯铜、趁软锻打；红铜之始
- id: casting_mold
  cn: 陶范/铸造
  era: L3
  cat: 工艺
  kind: bridge
  pre: [pottery]
  skill: {clay: 35}
  note: 用耐火的泥模浇出形状——"塑形"从泥走到金属
- id: bronze
  cn: 青铜器（从岩石炼出青铜）
  era: L3
  cat: 材料
  kind: milestone
  pre: [copper_ore, tin_ore, copper_smelt, high_temp_kiln, casting_mold]
  geo: [铜矿, 锡矿]
  skill: {metal: 40, fire: 35}
  tag: [tool, war, prestige]
  adopt: slow
  cost: {land: 0.3, deplete: 0.3}
  note: 铜+锡共熔更硬更利，礼器与兵器同时登顶；冶铜者成为最早的"高技术专家"
- id: counting
  cn: 记数（结绳/刻痕/算筹）
  era: L3
  cat: 文字
  kind: bridge
  pre: []
  skill: {math: 20}
  tag: [trade, record]
  note: 数牲畜、数粮、数人头——最早的"思维符号化"
- id: granary
  cn: 公仓与剩余粮食
  era: L3
  cat: 农业
  kind: bridge
  pre: [planting, settlement]
  skill: {build: 20}
  tag: [storage, city]
  note: 第一次有富余、有人管、有账要记——剩余养活了祭司与匠人
- id: trade_barter
  cn: 以物易物
  era: L3
  cat: 社会组织
  kind: bridge
  pre: [granary]
  skill: {}
  tag: [trade]
  note: 部落间余缺互换；交换把陌生人的东西变成"我的选项"
- id: calendar
  cn: 发现太阳运行规律（历法/纪年）
  era: L3
  cat: 历法
  kind: milestone
  pre: [counting, planting, granary]
  skill: {astro: 30}
  tag: [ritual, record]
  note: 日影/日出方位的年周期→定农时、定节气，历法让"明年"可计划
- id: beacon
  cn: 烽火传讯（烟火接力）
  era: L3
  cat: 通讯
  kind: milestone
  pre: [fire_use]
  tag: [comm, war]
  note: 隔山点火、逐墩接力——信息第一次比跑得快的人更快
- id: property_private
  cn: 私有观念（"我的"）
  era: L3
  cat: 社会组织
  kind: bridge
  pre: [granary, trade_barter, pottery]
  tag: [prestige]
  cost: {unrest: 0.4}
  note: 剩余+交换→所有权与继承；身份开始用"占有什么"来标记
- id: gem_gather
  cn: 采集珍石（玉/玛瑙/琥珀）
  era: L3
  cat: 材料
  kind: bridge
  pre: [stone_flake]
  geo: [宝石矿, 美石河滩]
  skill: {ore: 20}
  tag: [ritual, prestige]
  note: 从"能用的石头"里分出了"好看的石头"
- id: wealth_show
  cn: 把宝石戴在头上炫富
  era: L3
  cat: 文化
  kind: milestone
  pre: [property_private, gem_gather, bronze]
  tag: [prestige]
  adopt: ritual
  note: 珍稀之物+私有观念+等级欲望 三者齐备，才有人把宝石顶在头上告诉别人自己有钱
- id: gold
  cn: 发现黄金
  era: L3
  cat: 材料
  kind: milestone
  pre: [copper_smelt]
  geo: [沙金, 金矿]
  skill: {ore: 25, metal: 25}
  tag: [prestige, trade]
  note: 河床天然块金，久放不锈、灿灿生光——价值的"天生代言物"
- id: silver
  cn: 发现白银
  era: L3
  cat: 材料
  kind: milestone
  pre: [gold, copper_smelt]
  geo: [银矿, 铅银矿]
  skill: {ore: 25, metal: 25}
  tag: [prestige, trade]
  note: 常在铅里；比金多、比铜贵，铸币的理想金属
- id: potters_wheel
  cn: 陶轮（转盘拉坯）
  era: L3
  cat: 工艺
  kind: bridge
  pre: [pottery]
  skill: {clay: 30, wood: 20}
  note: 让泥快速旋转——人第一次被"绕轴转的东西"吸引
- id: wheel
  cn: 发现轮子
  era: L3
  cat: 交通
  kind: milestone
  pre: [stone_axe, potters_wheel]
  skill: {wood: 30}
  tag: [transport]
  note: 滚木→轴+轮；圆从"自然现象"变成"人造机械"
- id: cart
  cn: 车（手推/牛马车）
  era: L3
  cat: 交通
  kind: bridge
  pre: [wheel, hafting]
  skill: {wood: 35, fiber: 25}
  tag: [transport]
  note: 轮子上架厢与轴，重物第一次长脚
- id: lever
  cn: 发现杠杆
  era: L3
  cat: 机械
  kind: milestone
  pre: [hafting]
  skill: {mech: 25}
  tag: [energy, build]
  note: 撬石、翘物、汲水——省力原理第一次被自觉利用
- id: plow
  cn: 犁
  era: L3
  cat: 农业
  kind: bridge
  pre: [planting, hafting]
  skill: {wood: 25}
  tag: [hunger]
  cost: {land: 0.2}
  note: 破土开沟的农具；人力可用，畜力更佳
- id: harness
  cn: 轭/挽具
  era: L3
  cat: 畜牧
  kind: bridge
  pre: [fiber_string, stone_axe]
  skill: {fiber: 25, wood: 20}
  note: 让拉力从"勒脖子"变成"用肩颈承重"，畜力才不伤牲口
- id: writing
  cn: 原始文字（记账符号→象形）
  era: L3
  cat: 文字
  kind: bridge
  pre: [counting, granary]
  skill: {letters: 30}
  tag: [record, ritual]
  note: 记忆外置：契约、王命、神谕都写在看得见的东西上（v0.8 由 L4 前移至 L3）
```

## L4 · 铁器·古典·远航

> 铁让农具兵器全面升级；马与船把人的活动半径推到河与海之外；货币与远航把聚落凝结成文明。

```yaml
- id: ox_power
  cn: 将牛变成生产力（牛耕/牛车）
  era: L4
  cat: 农业
  kind: milestone
  pre: [ox_dom, plow, harness, cart]
  geo: [牛草场]
  skill: {animal: 35, wood: 25}
  tag: [hunger, energy]
  note: 一个劳动力换回三头牛的力气，田亩上限暴涨
- id: iron_ore
  cn: 认识铁矿
  era: L4
  cat: 材料
  kind: bridge
  pre: [copper_ore]
  geo: [铁矿石]
  skill: {ore: 35}
  note: 褐黑矿石入火不熔只红——它需要更狠的温度和"还原"
- id: iron
  cn: 从岩石提炼出铁
  era: L4
  cat: 材料
  kind: milestone
  pre: [iron_ore, high_temp_kiln, bellows, charcoal, copper_smelt]
  geo: [铁矿石]
  skill: {metal: 55, fire: 40}
  tag: [tool, war]
  adopt: slow
  cost: {land: 0.5}
  note: 块炼铁反复锻打渗碳→钢；比铜硬、矿藏多得多，农业兵器同时革命
- id: sail
  cn: 帆
  era: L4
  cat: 交通
  kind: bridge
  pre: [cloth, dugout]
  skill: {fiber: 30, wood: 25}
  tag: [transport]
  note: 布面吃风——人第一次借用"天空的力"移动
- id: ship
  cn: 造出船（板船/帆船远航）
  era: L4
  cat: 交通
  kind: milestone
  pre: [dugout, sail, hafting]
  skill: {wood: 40, mech: 25}
  tag: [transport, trade]
  note: 板材拼接、龙骨与帆桁；离开海岸线，四条河的世界彼此可期
- id: market
  cn: 集市/市场
  era: L4
  cat: 社会组织
  kind: bridge
  pre: [trade_barter, cart]
  tag: [trade, city]
  note: 固定地点+固定日期换物，陌生人的信用开始有场可依
- id: weighing
  cn: 称重计量（天平/砝码）
  era: L4
  cat: 文字
  kind: bridge
  pre: [counting]
  skill: {math: 25, metal: 20}
  tag: [trade]
  note: 交易要"公道"，先得有"多少"
- id: horse_bridle
  cn: 骑具（辔头/鞍/镫）
  era: L4
  cat: 畜牧
  kind: bridge
  pre: [horse_dom, fiber_string]
  skill: {animal: 30, fiber: 25}
  note: 控马、稳骑、久骑——马从猎物变成延伸的腿
- id: horse_power
  cn: 将马变成生产力（骑乘/马车）
  era: L4
  cat: 交通
  kind: milestone
  pre: [horse_bridle, cart, harness]
  geo: [草原]
  skill: {animal: 40}
  tag: [transport, war, trade]
  note: 信使一日百里、骑兵改变战争、马车拉通商路
- id: money
  cn: 货币作为一般等价物
  era: L4
  cat: 社会组织
  kind: milestone
  pre: [gold, silver, market, weighing, counting]
  geo: [金银矿]
  skill: {metal: 30, math: 25}
  tag: [trade]
  adopt: ritual
  cost: {unrest: 0.3}
  note: 贵金属统一成"计量+储藏+交换"三合一，交易脱离物物相抵
```

## L5 · 纸与知识复制

> 文字若只刻在骨石竹木上，知识活不过一代抄手。纸让思想贬值到人人都摸得着，印刷让它增殖。

```yaml
- id: paper
  cn: 造纸
  era: L5
  cat: 材料
  kind: milestone
  pre: [writing, fiber_string]
  geo: [麻, 树皮, 竹]
  skill: {fiber: 35, chem: 20}
  tag: [record, comm]
  note: 沤麻打浆抄纸帘——廉价平整的书写面，知识的载体革命
- id: ink
  cn: 墨（烟炱/炭黑制墨）
  era: L5
  cat: 材料
  kind: bridge
  pre: [charcoal, writing]
  skill: {chem: 20}
  tag: [record]
  note: 炭黑+胶成墨，让"印"能均匀留在纸上
- id: seal_carve
  cn: 印章与雕刻（阳文）
  era: L5
  cat: 工艺
  kind: bridge
  pre: [writing, stone_flake]
  skill: {stone: 35}
  note: 反刻凸文蘸墨捺印——"复制"的思想早已在盖章里
- id: woodblock_print
  cn: 雕版印刷
  era: L5
  cat: 工艺
  kind: milestone
  pre: [paper, ink, seal_carve]
  skill: {wood: 30, letters: 25}
  tag: [record, comm]
  adopt: mid
  note: 整版反刻、刷墨覆纸，一卷变百卷
- id: movable_type
  cn: 活字印刷
  era: L5
  cat: 工艺
  kind: milestone
  pre: [woodblock_print]
  skill: {letters: 30, metal: 25}
  tag: [record, comm]
  adopt: slow
  note: 单字成模可排可拆，印书不再雕一版毁一版
- id: glass
  cn: 玻璃
  era: L5
  cat: 材料
  kind: bridge
  pre: [high_temp_kiln]
  geo: [石英砂, 草木灰/碱]
  skill: {fire: 40, chem: 20}
  tag: [curiosity, light]
  note: 沙与碱在 1200℃ 下化浆成晶莹固体
- id: lens
  cn: 磨制透镜
  era: L5
  cat: 光学
  kind: bridge
  pre: [glass]
  skill: {optics: 30}
  tag: [curiosity, light]
  note: 玻璃曲面对光有"方向"——放大、聚焦、成像皆从此来
```

## L6 · 火药·远航·科学

> 指南针把人送出海岸，火药把城墙炸开口子，透镜与定量精神让"为什么"第一次能被证明。

```yaml
- id: niter
  cn: 提硝（硝石/土硝）
  era: L6
  cat: 化学
  kind: bridge
  pre: []
  geo: [硝土, 硝石矿]
  skill: {ore: 30, chem: 20}
  tag: [curiosity]
  note: 白色结晶，炼丹者眼里的"长生料"，实际是氧化剂的宝藏
- id: gunpowder
  cn: 发明火药（一硫二硝三木炭）
  era: L6
  cat: 化学
  kind: milestone
  pre: [niter, charcoal]
  geo: [硫磺]
  skill: {chem: 30}
  tag: [war, ritual]
  adopt: slow
  note: 三种粉末在密闭里瞬间放气；最早的"人造雷霆"
- id: fireworks
  cn: 烟花
  era: L6
  cat: 化学
  kind: milestone
  pre: [gunpowder]
  skill: {chem: 30}
  tag: [ritual, leisure]
  note: 火药+发色金属盐；节庆的火焰美术
- id: early_rocket
  cn: 火药火箭（原始推进）
  era: L6
  cat: 航天
  kind: bridge
  pre: [gunpowder]
  skill: {mech: 20}
  tag: [war, ritual]
  note: 竹筒装药绑在箭上，反作用力第一次把人造物送上天——现代火箭的远祖
- id: compass
  cn: 指南针（磁罗盘）
  era: L6
  cat: 交通
  kind: bridge
  pre: [iron, magnetic_stone]
  skill: {ore: 25}
  tag: [transport]
  note: 悬磁石指向南北——迷航的人第一次有"固定的北方"
- id: magnetic_stone
  cn: 认识天然磁石
  era: L6
  cat: 材料
  kind: bridge
  pre: [iron_ore]
  skill: {ore: 30}
  note: 吸铁的石头，且恒指一方
- id: gear
  cn: 齿轮与传动
  era: L6
  cat: 机械
  kind: bridge
  pre: [wheel, iron]
  skill: {mech: 35}
  tag: [energy]
  note: 啮合轮齿让运动可以变速变向、连绵传递
- id: mechanical_clock
  cn: 机械钟（摆/擒纵）
  era: L6
  cat: 机械
  kind: bridge
  pre: [gear]
  skill: {mech: 45}
  tag: [comm, record]
  note: 均匀节律擒纵——时间第一次可被"携带与测量"
- id: telescope
  cn: 望远镜
  era: L6
  cat: 光学
  kind: bridge
  pre: [lens]
  skill: {optics: 40}
  tag: [curiosity]
  note: 镜片组把远处拉近——天体第一次向人坦白
- id: science_revolution
  cn: 近代科学精神（实验+定量）
  era: L6
  cat: 认知
  kind: bridge
  pre: [telescope, movable_type]
  skill: {math: 40}
  tag: [curiosity]
  note: 印刷让学说接受检验、透镜让眼见可复现；问"如何"升为问"为何"
- id: city_stage
  cn: 市井演出/剧场
  era: L6
  cat: 文化
  kind: bridge
  pre: [market, song]
  tag: [leisure, city]
  cost: {land: 0.3, unrest: 0.2}
  note: 城市有余钱有闲人，卖艺者把戏台支在街头
```

## L7 · 工业·电气·化学·早期媒体

> 蒸汽与电把"人力"换成"机器力"，化学把药与炸药分开，光影把现实复制下来。

```yaml
- id: chem_revolution
  cn: 近代化学（元素/定量/氧化）
  era: L7
  cat: 化学
  kind: bridge
  pre: [science_revolution, gunpowder]
  skill: {chem: 45}
  tag: [curiosity]
  note: 物质不再神秘：燃烧是氧化、物质由元素组成——整个 L7 化学的根
- id: coal_fuel
  cn: 煤炭开发
  era: L7
  cat: 能源
  kind: bridge
  pre: [iron_ore]
  geo: [煤矿]
  skill: {ore: 30}
  tag: [energy]
  cost: {carbon: 0.6, land: 0.3}
  note: 地下的黑石比木炭热得多——工业化第一桶"燃料预算"
- id: steam_engine
  cn: 蒸汽机
  era: L7
  cat: 机械
  kind: bridge
  pre: [iron, coal_fuel, gear, mechanical_clock]
  skill: {mech: 55, metal: 45}
  tag: [energy]
  adopt: slow
  cost: {carbon: 0.4}
  note: 锅炉蒸汽推活塞再转曲柄——人工动力第一次可以"开与关"
- id: steamship
  cn: 造出轮船
  era: L7
  cat: 交通
  kind: milestone
  pre: [steam_engine, ship]
  skill: {mech: 50, metal: 40}
  tag: [transport, trade]
  cost: {carbon: 0.3}
  note: 蒸汽顶风逆流、定时跨洋，海路从此不靠天
- id: petroleum
  cn: 石油开采
  era: L7
  cat: 能源
  kind: bridge
  pre: [coal_fuel]
  geo: [油田]
  skill: {ore: 30}
  tag: [energy]
  cost: {carbon: 0.5, deplete: 0.4}
  note: 敲地取黑油——比煤更便于携带的能量
- id: refinery
  cn: 炼油（煤油/汽油/柴油）
  era: L7
  cat: 能源
  kind: bridge
  pre: [petroleum, chem_revolution]
  skill: {chem: 40}
  tag: [energy]
  cost: {carbon: 0.4, land: 0.2}
  note: 蒸馏把黑油拆成白油——内燃机的专用粮
- id: internal_combustion
  cn: 内燃机
  era: L7
  cat: 机械
  kind: bridge
  pre: [refinery, gear, battery]
  skill: {mech: 55, chem: 35}
  tag: [energy]
  cost: {carbon: 0.7}
  note: 油雾在缸内爆燃直接推活塞——轻而猛，能装在车上飞上天
- id: car
  cn: 造出汽车
  era: L7
  cat: 交通
  kind: milestone
  pre: [internal_combustion, wheel, iron]
  skill: {mech: 50}
  tag: [transport]
  cost: {carbon: 0.5, land: 0.3}
  note: 四轮+内燃+方向机，路从"走出来的"变成"铺出来的"
- id: airplane
  cn: 造出飞机
  era: L7
  cat: 交通
  kind: milestone
  pre: [internal_combustion]
  skill: {aero: 55, mech: 50}
  tag: [transport, war]
  adopt: slow
  cost: {carbon: 0.5}
  note: 高功重比引擎+翼面升力，人第一次离地久飞
- id: battery
  cn: 发明电池（伏打电堆）
  era: L7
  cat: 电力
  kind: milestone
  pre: [chem_revolution, copper, zinc]
  geo: [金属]
  skill: {chem: 40}
  tag: [energy, curiosity]
  note: 两种金属夹酸液——持续稳定的电流从化学里"流"出来
- id: copper
  cn: 纯铜/导线铜
  era: L7
  cat: 材料
  kind: bridge
  pre: [copper_smelt]
  skill: {metal: 35}
  note: 延展性好、导电佳——电的"血管"
- id: zinc
  cn: 锌
  era: L7
  cat: 材料
  kind: bridge
  pre: [copper_smelt, chem_revolution]
  geo: [锌矿]
  skill: {metal: 40}
  note: 黄铜/电极都靠它；冶炼时白烟缭绕的"隐秘金属"
- id: electromagnetism
  cn: 电磁学（电生磁/磁生电）
  era: L7
  cat: 电力
  kind: bridge
  pre: [battery, magnetic_stone, copper]
  skill: {elec: 40}
  tag: [curiosity]
  note: 通电线圈拨动磁针、磁铁在线圈里进出生出电流——电与磁原是同一物
- id: electric_gen
  cn: 发电机/电动机
  era: L7
  cat: 电力
  kind: bridge
  pre: [electromagnetism]
  skill: {elec: 45, mech: 40}
  tag: [energy]
  note: 转动的磁与线圈互变电与功——能源可"送出去用了"
- id: direct_current
  cn: 发现直流电
  era: L7
  cat: 电力
  kind: milestone
  pre: [battery]
  skill: {elec: 30}
  tag: [curiosity, light]
  note: 稳恒单向的电流——电化学与早期照明都靠它
- id: transformer
  cn: 变压器
  era: L7
  cat: 电力
  kind: bridge
  pre: [electromagnetism]
  skill: {elec: 45}
  note: 绕圈数改变电压——电从此可以"升压远送、降压入户"
- id: alternating_current
  cn: 发现交流电
  era: L7
  cat: 电力
  kind: milestone
  pre: [electric_gen, transformer]
  skill: {elec: 50}
  tag: [energy]
  note: 方向周期性反转的电+变压器，城际电网的唯一解
- id: vacuum_bulb
  cn: 真空玻璃（灯泡/电子管共享工艺）
  era: L7
  cat: 工艺
  kind: bridge
  pre: [glass]
  skill: {fire: 45, chem: 30}
  tag: [light]
  note: 抽空玻璃壳，灯丝不烧断、电子能飞起来
- id: light_bulb
  cn: 发明灯泡（白炽）
  era: L7
  cat: 电力
  kind: milestone
  pre: [direct_current, vacuum_bulb]
  skill: {elec: 40, chem: 30}
  tag: [light]
  note: 碳丝/钨丝在真空里白热——夜被缩短，人工白昼降临
- id: telegraph
  cn: 电报（有线电磁传讯）
  era: L7
  cat: 通讯
  kind: bridge
  pre: [battery, electromagnetism, copper]
  skill: {elec: 45, comm: 30}
  tag: [comm]
  note: 电流的断与续越过山川——信息的第一次脱体远行
- id: morse_code
  cn: 发明摩斯电码
  era: L7
  cat: 通讯
  kind: milestone
  pre: [telegraph]
  skill: {comm: 30}
  tag: [comm, record]
  note: 点划长短成码表——用最少信号说最多的话
- id: radio
  cn: 无线电/电磁波通讯
  era: L7
  cat: 通讯
  kind: bridge
  pre: [electromagnetism, antenna]
  skill: {elec: 50, comm: 40}
  tag: [comm]
  note: 电磁波不借导线传播——"无线"时代的门
- id: antenna
  cn: 天线/线圈
  era: L7
  cat: 通讯
  kind: bridge
  pre: [electromagnetism, copper]
  skill: {elec: 40}
  note: 让电振荡进空气、也把空中的振荡接回来
- id: camera_obscura
  cn: 暗箱取景
  era: L7
  cat: 光学
  kind: bridge
  pre: [lens]
  skill: {optics: 35}
  tag: [record, curiosity]
  note: 小孔/透镜把景色倒投进暗盒——画家的偷懒工具
- id: light_sensitive
  cn: 卤化银感光（光化学）
  era: L7
  cat: 化学
  kind: bridge
  pre: [chem_revolution, silver]
  skill: {chem: 45}
  tag: [record]
  note: 见光的银盐变黑、显影定影留住影像——光第一次能被"抓住"
- id: camera_photo
  cn: 发明照相机（银版摄影）
  era: L7
  cat: 光学
  kind: milestone
  pre: [camera_obscura, light_sensitive]
  skill: {optics: 45, chem: 40}
  tag: [record]
  adopt: mid
  note: 暗箱+感光板，人第一次把"看到的瞬间"存成实物
- id: silver_film
  cn: 银盐黑白胶片（底片/胶卷）
  era: L7
  cat: 光学
  kind: milestone
  pre: [camera_photo, chem_revolution]
  skill: {chem: 45}
  tag: [record]
  note: 玻璃/赛璐珞涂卤化银乳剂——一张负片能洗无数张，可连拍可放映
- id: color_film
  cn: 彩色胶片
  era: L7
  cat: 光学
  kind: milestone
  pre: [silver_film]
  skill: {chem: 50}
  tag: [record, leisure]
  note: 三色感光层叠加——世界的颜色终于留在纸上
- id: movie
  cn: 发明电影
  era: L7
  cat: 媒体
  kind: milestone
  pre: [silver_film]
  skill: {optics: 45, mech: 40}
  tag: [leisure, record]
  note: 逐帧连续摄影+间歇放映——静止的画面在人眼前"活"了过来
- id: phonograph
  cn: 机械留声机（声波刻纹）
  era: L7
  cat: 媒体
  kind: bridge
  pre: [mechanical_clock, copper]
  skill: {mech: 45}
  note: 声波推动刻针在蜡筒留下纹路，再循纹放声
- id: sound_recorder
  cn: 发明录音机（磁带录音）
  era: L7
  cat: 媒体
  kind: milestone
  pre: [phonograph, electromagnetism]
  skill: {elec: 45, mech: 40}
  tag: [record, leisure]
  note: 声音变成磁带上可抹可复录的磁迹——声音也能"剪贴"了
- id: tnt
  cn: 发明 TNT 炸药
  era: L7
  cat: 化学
  kind: milestone
  pre: [chem_revolution, gunpowder]
  skill: {chem: 55}
  tag: [war, energy]
  adopt: ritual
  cost: {land: 0.4}
  note: 硝基有机物受控爆轰——从黑药到高能炸药的工业跳跃
```

## L8 · 电子·信息·航天·后信息

> 电子管让"无中生有"的放大出现；晶体管与集成电路把它塞进指甲盖；于是电脑、网络、手机、机器人、火箭与量子在这个阶段扎堆爆发。

```yaml
- id: vacuum_diode
  cn: 二极管（真空管整流/检波）
  era: L8
  cat: 电子
  kind: milestone
  pre: [vacuum_bulb, direct_current]
  skill: {elec: 50}
  tag: [curiosity, comm]
  note: 灯丝加热发射电子、单向导通——电流第一次能"被控制方向"
- id: vacuum_triode
  cn: 三极管（电子管放大）
  era: L8
  cat: 电子
  kind: milestone
  pre: [vacuum_diode]
  skill: {elec: 55}
  tag: [comm, curiosity]
  note: 栅极微变、屏流巨变——人类第一只"放大器"，无线电与电脑的起点
- id: semiconductor
  cn: 半导体材料（硅/锗高纯单晶）
  era: L8
  cat: 材料
  kind: bridge
  pre: [chem_revolution]
  skill: {chem: 55, semi: 40}
  tag: [energy]
  note: 掺一点杂质就改导电性——比真空管小、省、可靠的新介质
- id: transistor
  cn: 晶体管（固体三极管）
  era: L8
  cat: 电子
  kind: milestone
  pre: [semiconductor]
  skill: {semi: 50}
  tag: [curiosity]
  note: 一小片锗硅取代一只热烘烘的管子——电子学的"小时代"开始
- id: lithography
  cn: 光刻
  era: L8
  cat: 工艺
  kind: bridge
  pre: [lens, semiconductor]
  skill: {semi: 60, optics: 50}
  cost: {land: 0.2}
  note: 光把电路图案"照"进晶圆再蚀刻——把成千上万元件画在一粒米上
- id: integrated_circuit
  cn: 集成电路
  era: L8
  cat: 电子
  kind: milestone
  pre: [transistor, lithography]
  skill: {semi: 65}
  tag: [curiosity, comm]
  adopt: slow
  note: 一整块晶片上蚀出整条电路——晶体管的数量从此以指数繁殖
- id: computer_arch
  cn: 电子计算机（存储程序/冯·诺依曼）
  era: L8
  cat: 计算机
  kind: bridge
  pre: [vacuum_triode]
  skill: {cs: 45, elec: 45}
  tag: [curiosity, record]
  note: 程序和数据同住内存、逐条取指令执行——"会算的机器"有了灵魂
- id: cpu
  cn: CPU（微处理器）
  era: L8
  cat: 计算机
  kind: milestone
  pre: [integrated_circuit, computer_arch]
  skill: {semi: 70, cs: 55}
  tag: [comm, record]
  note: 运算控制核心缩成单颗芯片——电脑的心脏
- id: os_software
  cn: 操作系统/通用软件
  era: L8
  cat: 计算机
  kind: bridge
  pre: [computer_arch]
  skill: {cs: 50}
  note: 管理硬件、调度程序——让"电脑"能被普通人而非专家使用
- id: display
  cn: 显示器（显像管→面板）
  era: L8
  cat: 计算机
  kind: milestone
  pre: [vacuum_diode]
  skill: {elec: 55, optics: 45}
  tag: [comm, leisure]
  note: 电子束打亮荧光屏/液晶面板——机器第一次有"能看的脸"
- id: personal_computer
  cn: 造出个人计算机
  era: L8
  cat: 计算机
  kind: milestone
  pre: [cpu, display, os_software]
  skill: {cs: 60, semi: 60}
  tag: [record, leisure]
  note: 键盘+屏幕+能装进口袋的主机——算力从机房走到每个人桌上
- id: video_camera
  cn: 电子摄像（扫描成像）
  era: L8
  cat: 媒体
  kind: bridge
  pre: [camera_photo, display]
  skill: {elec: 55}
  tag: [comm, record]
  note: 把光影扫成逐行电信号——影像从此能"当场发射"
- id: television
  cn: 发明电视机
  era: L8
  cat: 媒体
  kind: milestone
  pre: [display, video_camera, radio]
  skill: {elec: 60, comm: 50}
  tag: [leisure, comm]
  adopt: slow
  note: 摄像-发射-显像闭合——全家围坐看世界的黑匣子
- id: cellular
  cn: 蜂窝移动网（基站接力）
  era: L8
  cat: 通讯
  kind: bridge
  pre: [radio, antenna]
  skill: {comm: 55}
  tag: [comm]
  note: 把区域切成蜂窝、基站间无缝切换——边走边通话成为可能
- id: microwave_comm
  cn: 微波通信/中继
  era: L8
  cat: 通讯
  kind: milestone
  pre: [radio, vacuum_triode]
  skill: {comm: 55}
  tag: [comm]
  note: 更高频电磁波接力+抛物面天线——大带宽长途骨干与卫星地面站
- id: mobile_phone
  cn: 造出手机
  era: L8
  cat: 通讯
  kind: milestone
  pre: [integrated_circuit, battery, display, cellular]
  skill: {comm: 60, semi: 60}
  tag: [comm, leisure]
  adopt: slow
  note: 蜂窝+芯片+电池+小屏——电话与电脑合体，装进口袋
- id: parallel_arch
  cn: 并行计算体系
  era: L8
  cat: 计算机
  kind: bridge
  pre: [computer_arch]
  skill: {cs: 55}
  note: 一千个算核同时算一块——吞吐量从加法变瀑布
- id: gpu_accel
  cn: 算力卡（GPU/AI 加速）
  era: L8
  cat: 计算机
  kind: milestone
  pre: [integrated_circuit, parallel_arch]
  skill: {semi: 65, cs: 60}
  tag: [leisure, curiosity]
  note: 专为海量并行而生的卡——图形之外更喂饱了神经网络
- id: neural_net
  cn: 神经网络算法
  era: L8
  cat: 计算机
  kind: bridge
  pre: [computer_arch]
  skill: {cs: 55}
  note: 模拟"神经元加权激活"去学规律——机器第一次"能从例子自己总结"
- id: deep_learning
  cn: 深度学习
  era: L8
  cat: 计算机
  kind: bridge
  pre: [neural_net, gpu_accel]
  skill: {cs: 70}
  note: 深层的网络+海量数据+大算力——图像语言识别质的飞跃
- id: recommender
  cn: 推荐算法（千人千面）
  era: L8
  cat: 计算机
  kind: bridge
  pre: [deep_learning]
  skill: {cs: 65}
  cost: {noise: 0.6}
  note: 猜你喜欢并持续喂给你——注意力经济的技术底座
- id: sensor_tech
  cn: 传感器（光电/惯性/压力）
  era: L8
  cat: 计算机
  kind: bridge
  pre: [semiconductor]
  skill: {semi: 55}
  tag: [comm, heal]
  note: 把温度、光、运动、声音变成电信号——机器的五官
- id: robot
  cn: 造出机器人
  era: L8
  cat: 计算机
  kind: milestone
  pre: [computer_arch, sensor_tech, electric_gen]
  skill: {mech: 60, cs: 60}
  tag: [energy, war, leisure]
  adopt: slow
  note: 传感器感知+伺服器出力+程序决策——能替人干活的身体
- id: vr_glasses
  cn: 造出 VR 眼镜
  era: L8
  cat: 计算机
  kind: milestone
  pre: [display, sensor_tech, gpu_accel]
  skill: {cs: 65, optics: 55}
  tag: [leisure]
  note: 双目高刷屏+头部追踪——把一整个世界戴在眼前
- id: net_computer
  cn: 计算机网络（局域/互联）
  era: L8
  cat: 网络
  kind: milestone
  pre: [personal_computer]
  skill: {cs: 60, comm: 50}
  tag: [comm]
  note: 把几台电脑用线连起来传文件——"网络"作为一种发明诞生
- id: ip_address
  cn: 发明 IP 地址
  era: L8
  cat: 网络
  kind: milestone
  pre: [net_computer]
  skill: {cs: 60}
  tag: [comm]
  note: 给每台入网的机器一个全网唯一的门牌号——寻址的规则
- id: internet
  cn: 发明互联网
  era: L8
  cat: 网络
  kind: milestone
  pre: [net_computer, ip_address]
  skill: {cs: 65, comm: 55}
  adopt: slow
  note: 异构网络用统一协议彼此联通——信息终于没有国界地流动
- id: online_payment
  cn: 电子支付/网上银行
  era: L8
  cat: 社会组织
  kind: bridge
  pre: [internet]
  skill: {cs: 60}
  tag: [trade]
  note: 加密把"钱"变成一串可核验的比特——交易摆脱实体货币
- id: ecommerce_taobao
  cn: 淘宝（电商平台）
  era: L8
  cat: 网络
  kind: milestone
  pre: [internet, online_payment]
  skill: {cs: 60}
  tag: [trade, leisure]
  adopt: ritual
  note: 网络集市+担保支付+物流——买与卖各自坐在家里完成
- id: microblog_weibo
  cn: 微博（公共广场短讯）
  era: L8
  cat: 网络
  kind: milestone
  pre: [internet]
  skill: {cs: 55}
  tag: [comm, leisure]
  cost: {noise: 0.4}
  note: 140 字的公共发言台——每个人都能对全世界说一句
- id: video_codec
  cn: 视频压缩编码
  era: L8
  cat: 网络
  kind: bridge
  pre: [computer_arch]
  skill: {cs: 65}
  note: 把每秒 30 帧的海洋压进窄带——让"动起来"可以随时传
- id: short_video_douyin
  cn: 抖音（短视频+推荐流）
  era: L8
  cat: 网络
  kind: milestone
  pre: [mobile_phone, internet, video_codec, recommender]
  skill: {cs: 65}
  tag: [leisure]
  adopt: ritual
  cost: {noise: 0.6}
  note: 手机随手拍+压缩上传+推荐喂流——娱乐被切成 15 秒又无限续杯
- id: laser
  cn: 激光
  era: L8
  cat: 光学
  kind: bridge
  pre: [lens, quantum_physics]
  skill: {optics: 60, semi: 55}
  tag: [curiosity]
  note: 受激辐射得到同相单色光——又亮又"直"的光束
- id: optical_fiber
  cn: 光纤
  era: L8
  cat: 通讯
  kind: bridge
  pre: [laser, glass]
  skill: {optics: 60, semi: 50}
  tag: [comm]
  note: 光在极纯玻璃丝里折来折去几乎不损耗——骨干网的血管
- id: quantum_physics
  cn: 量子力学
  era: L8
  cat: 认知
  kind: bridge
  pre: [science_revolution]
  skill: {math: 75}
  tag: [curiosity]
  note: 原子级世界的"不确定性"规律——二十世纪物理的地基
- id: quantum_comms
  cn: 使用量子通讯
  era: L8
  cat: 通讯
  kind: milestone
  pre: [optical_fiber, quantum_physics]
  skill: {optics: 70, cs: 65}
  tag: [comm]
  adopt: slow
  note: 量子态分发密钥、窃听即毁——理论上不可破解的通讯
- id: cnc_precision
  cn: 数控机床/精密制造
  era: L8
  cat: 机械
  kind: bridge
  pre: [computer_arch, gear]
  skill: {mech: 65, cs: 55}
  tag: [energy]
  note: 代码指挥刀具运动——机器开始制造机器
- id: cad_software
  cn: 计算机辅助设计（CAD）
  era: L8
  cat: 计算机
  kind: bridge
  pre: [personal_computer]
  skill: {cs: 60}
  note: 在屏幕上先"造一遍"——图纸从纸上走进内存
- id: printer_3d
  cn: 发明 3D 打印机
  era: L8
  cat: 机械
  kind: milestone
  pre: [cnc_precision, cad_software]
  skill: {mech: 60, cs: 55}
  tag: [curiosity, tool]
  note: 逐层堆叠材料，把数字模型直接"长"成实物——造物从减法变加法
- id: liquid_engine
  cn: 液体火箭发动机
  era: L8
  cat: 航天
  kind: bridge
  pre: [early_rocket, chem_revolution, refinery]
  skill: {aero: 60, mech: 60}
  tag: [war, curiosity]
  note: 液氧+煤油/液氢受控燃烧——推力大到能挣脱地球
- id: rocket
  cn: 造出运载火箭
  era: L8
  cat: 航天
  kind: milestone
  pre: [liquid_engine]
  skill: {aero: 65, cs: 55}
  tag: [curiosity, war]
  adopt: slow
  note: 液体发动机+制导——人造物第一次稳稳飞出大气层
- id: solar_cell
  cn: 太阳能电池
  era: L8
  cat: 能源
  kind: bridge
  pre: [semiconductor]
  skill: {semi: 55}
  tag: [energy]
  note: 光直接变成电——卫星在太空里自给自足的口粮
- id: satellite
  cn: 造出卫星
  era: L8
  cat: 航天
  kind: milestone
  pre: [rocket, solar_cell, integrated_circuit]
  skill: {aero: 60, cs: 60}
  tag: [comm, war]
  adopt: slow
  note: 火箭送入轨道+太阳能板+星载电子——天上有了中继站与眼睛
- id: life_support
  cn: 生命保障系统
  era: L8
  cat: 航天
  kind: bridge
  pre: [chem_revolution]
  skill: {bio: 50, chem: 45}
  note: 氧气再生、温控、水循环、防辐射——让人能离开地表活下来
- id: manned_capsule
  cn: 载人飞船
  era: L8
  cat: 航天
  kind: milestone
  pre: [rocket, life_support]
  skill: {aero: 65, bio: 55}
  adopt: slow
  note: 密封舱+逃逸塔+回收伞——人第一次以乘客身份上太空
- id: moon_landing
  cn: 登录月球
  era: L8
  cat: 航天
  kind: milestone
  pre: [manned_capsule, rocket]
  skill: {aero: 70, cs: 65}
  adopt: slow
  note: 重型火箭+月面软着陆——脚印第一次印到地球之外
- id: deep_space
  cn: 深空探测（引力弹弓/长续航）
  era: L8
  cat: 航天
  kind: bridge
  pre: [satellite, rocket]
  skill: {aero: 70, cs: 65}
  note: 借行星引力加速、几十年孤独飞行——探测器离开"附近"
- id: jupiter_moons
  cn: 到达木星卫星
  era: L8
  cat: 航天
  kind: milestone
  pre: [deep_space]
  skill: {aero: 75}
  note: 越过小行星带——在木卫的冰下海洋寻找"另一个可能的生命"
- id: saturn_moons
  cn: 到达土星卫星
  era: L8
  cat: 航天
  kind: milestone
  pre: [deep_space]
  skill: {aero: 75}
  note: 穿进土星光环——人类最远的信使之一
- id: mars_landing
  cn: 登录火星
  era: L8
  cat: 航天
  kind: milestone
  pre: [manned_capsule, deep_space]
  skill: {aero: 75, bio: 65}
  adopt: slow
  note: 跨行星航程+就地取水制氧——人类文明的第二个家开始有图纸
```

## L9 · 深地·星际·量子

> 地表以下 4 公里，是人类血肉之躯的尽头；地球大气层外，是人类摇篮的尽头。
> 两堵墙都由同一把钥匙打开：**会自己干活的机器**，与**不怕距离的量子**。

```yaml
- id: nuclear_power
  cn: 核能发电（重核裂变）
  era: L9
  cat: 能源
  kind: milestone
  pre: [quantum_physics, chem_revolution, electric_gen, alternating_current]
  geo: [铀矿]
  skill: {chem: 65, elec: 60, ore: 45}
  tag: [energy]
  adopt: slow
  cost: {land: 0.3}
  note: 裂变放热烧汽推轮机——一公斤燃料抵千吨煤，是深地与星际唯一能"随身带"的能量
- id: deep_mining_robot
  cn: 深部矿井机器人采矿（4000 米以深）
  era: L9
  cat: 材料
  kind: milestone
  pre: [robot, cnc_precision, sensor_tech, nuclear_power]
  geo: [深部矿体]
  skill: {mech: 75, cs: 75, ore: 60, semi: 65}
  tag: [energy, tool]
  adopt: slow
  cost: {deplete: 0.7}
  note: 4000 米以深地温逾百℃、岩爆突水缺氧，血肉之躯到此为止；机器人接管后矿产可采储量成倍跃升
- id: space_route
  cn: 地月/地火常态化航线（可重复使用运载）
  era: L9
  cat: 航天
  kind: bridge
  pre: [rocket, moon_landing, mars_landing, cnc_precision]
  skill: {aero: 78, mech: 70, cs: 65}
  tag: [transport, trade]
  adopt: slow
  note: 火箭从"一次性奇迹"变成"按班次起飞"——月球与火星之间才算真的有了路
- id: space_mining
  cn: 机器人星际矿业（月球/火星矿场）
  era: L9
  cat: 航天
  kind: milestone
  pre: [deep_mining_robot, space_route, satellite]
  skill: {aero: 80, mech: 78, cs: 75, ore: 65}
  tag: [energy, trade]
  adopt: slow
  cost: {deplete: 0.5}
  note: 月壤稀土与氦-3、火星金属矿——把深地机器人那身本事搬到没有空气也没有人的地方
- id: quantum_computing
  cn: 量子计算
  era: L9
  cat: 计算机
  kind: milestone
  pre: [quantum_physics, integrated_circuit, laser]
  skill: {cs: 78, semi: 72, math: 80}
  tag: [curiosity]
  adopt: slow
  note: 用叠加与纠缠并行试遍所有可能——经典算力追不上的那一类问题上，它是另一条路
- id: interstellar_comm
  cn: 跨星际量子通讯（星际开发基础设施）
  era: L9
  cat: 通讯
  kind: milestone
  pre: [quantum_comms, quantum_computing, space_route]
  skill: {comm: 80, optics: 78, cs: 80}
  tag: [comm]
  adopt: slow
  note: 光速延迟下的地月、地火之间，靠量子密钥与中继维持不可窃听的常态链路——星际开发的神经系统
```

## 文化·大众娱乐与制度（补遗）

> 文明不止有斧与芯片，还有让人发笑的、让人聚在一起的仪式。它们对"城市闲人与大众媒体"这类能力门槛同样敏感。

```yaml
- id: xiangsheng
  cn: 发明相声
  era: L6
  cat: 文化
  kind: milestone
  pre: [city_stage, song]
  skill: {music: 30}
  tag: [leisure, city]
  note: 逗哏捧哏的语言节奏艺术——把"说话"本身变成了表演
- id: drama_theatre
  cn: 舞台戏剧
  era: L6
  cat: 文化
  kind: bridge
  pre: [city_stage]
  skill: {music: 30}
  tag: [leisure]
  note: 角色、冲突、观众——把故事搬上固定的台
- id: sketch_comedy
  cn: 发明小品
  era: L8
  cat: 文化
  kind: milestone
  pre: [drama_theatre, xiangsheng]
  skill: {music: 30}
  tag: [leisure]
  adopt: ritual
  note: 十分钟的喜剧短剧——舞台讽刺在电视时代的浓缩形态
- id: tv_station
  cn: 电视台/全国播出网
  era: L8
  cat: 媒体
  kind: bridge
  pre: [television]
  skill: {comm: 55}
  tag: [comm, leisure]
  note: 频道化定时播出+覆盖网——上亿人能在同一时刻看同一画面
- id: chunwan
  cn: 发明春节联欢晚会
  era: L8
  cat: 文化
  kind: milestone
  pre: [television, tv_station, calendar]
  skill: {music: 40, comm: 55}
  tag: [leisure, ritual]
  adopt: ritual
  note: 电视普及+全国播出网+全民同看——除夕之夜被"直播"成一个仪式
```

---

## 附录 A · req.txt 覆盖率对照

> 逐条核对（含 req 中重复与笔误的处理）。同一行多条用顿号分列；→ 后为该条对应的节点 id。

- 造出石斧 → `stone_axe`
- 陶罐 → `pottery`
- 发现太阳运行规律 → `calendar`
- 学会种地 → `planting`
- 将牛变成生产力 → `ox_power`
- 将马变成生产力 → `horse_power`
- 发现轮子 → `wheel`
- 发现杠杆 → `lever`
- 发明弓箭 → `bow`
- 开始吃熟食 → `cooked_food`
- 开始吃盐 → `salt`
- 腌制食物延长保质期 → `cured_food`
- 提炼出糖 → `sugar`
- 从岩石炼出青铜器 → `bronze`
- 从岩石提炼出铁 → `iron`
- 宝石放头顶显富 → `wealth_show`（含前置 `gem_gather`/`property_private`）
- 发现黄金 → `gold`
- 发现白银 → `silver`
- 货币作一般等价物 → `money`
- 造出纸 → `paper`
- 雕版印刷 → `woodblock_print`
- 活字印刷 → `movable_type`
- 制作布匹 → `cloth`
- 编筐 → `basket`
- 做出磬 → `lithophone`
- 做出曲子 → `song`
- 做出鼓 → `drum`
- 掌握使用火 → `fire_use`
- 造出船 → `ship`
- 用土坯垒房子 → `mudbrick`
- 烽火传递信息 → `beacon`
- 发明摩斯电码 → `morse_code`
- 微波通信 → `microwave_comm`
- 发明照相机 → `camera_photo`
- 银盐黑白胶片 → `silver_film`
- 彩色胶片 → `color_film`
- 发明电影 → `movie`
- 发明录音机 → `sound_recorder`
- 发明电视机 → `television`
- 发明显示器 → `display`
- 发明灯泡 → `light_bulb`
- 发现直流电 → `direct_current`
- 发现交流电 → `alternating_current`
- 发明电池 → `battery`
- 发明二极管 → `vacuum_diode`
- 发明三极管 → `vacuum_triode`（真空管）与 `transistor`（固体管）两个形态都建了
- 集成电路 → `integrated_circuit`
- CPU → `cpu`
- 个人计算机 → `personal_computer`
- 造出手机 → `mobile_phone`
- 造出算力卡 → `gpu_accel`
- 造出机器人 → `robot`
- 造出火箭 → `rocket`
- 造出轮船 → `steamship`
- 造出汽车 → `car`
- 造出飞机 → `airplane`
- 造出卫星 → `satellite`
- 发明火药 → `gunpowder`
- 发明 TNT → `tnt`
- 发明烟花 → `fireworks`
- 发明载人飞船 → `manned_capsule`
- 登录月球 → `moon_landing`
- 登录火星 → `mars_landing`
- 到达木星卫星 → `jupiter_moons`
- 到达土星卫星 → `saturn_moons`
- 使用量子通讯 → `quantum_comms`
- 发明 3D 打印机 → `printer_3d`
- 发明 VR 眼镜 → `vr_glasses`
- 发明网络 → `net_computer`
- 发明互联网 → `internet`
- 发明 IP 地址 → `ip_address`
- 做出淘宝 → `ecommerce_taobao`
- 做出抖音 → `short_video_douyin`
- 做出微博 → `microblog_weibo`
- 发明相声 → `xiangsheng`
- 发明小品 → `sketch_comedy`
- 发明春节联欢晚会 → `chunwan`

## 附录 B · 给引擎/后续迭代的约定

1. **读取方式**：解析脚本对每个 ```yaml 块，取其所有以 `- id:` 开头的节点拼成节点库——这样 §3 的"追加节点模板"块（id 是 `my_new_thing`、pre 里的"上一级N"是占位）会被自动跳过，不会污染库。若日后需要 `.yaml`/`.json`，写个小脚本从 fenced 块抽取即可，无需改手写格式。
2. **解析时忽略的字段**：`cn`、`era`、`cat`、`note` 仅供人读与分组；引擎行为只看 `pre`（前置全满足才可能）、`skill`/`geo`/`gate`（硬门槛）、`tag`（动机加分）、`adopt`（扩散难度）、`cost`（负外部性，v0.4 新增，见附录 D）。
3. **`pre` 是"约束"不是"进度"**：某个节点 `pre` 里引用的节点，即使写在它后面/别的 era，引擎也全局解析——本文件靠章节排版图个读着顺，不靠顺序表依赖。
4. **悬空引用是允许的**：`pre` 里若引用了尚未录入的 id（想补但还没补的中间件），引擎只当作"永不满足"，不会有语法错误——这样你可以先把"远期节点"写出来占位。
5. **世界初始池**：开国时人人默认会（或在部落公共池里）L0 的 `stone_flake`、`fire_use` 与基础语言——具体见 M1 实现时定。

---

## 附录 C · 作者增补节点（2026-10-01）

> 作者在本日追加三条要求（进入 L9）。其中第 3 条实为**两项技术**（量子通讯已有 `quantum_comms`，本处补足"量子计算"与"星际尺度组网"），故拆为两节点；
> 另补两个 `bridge` 节点（`nuclear_power`、`space_route`）——二者是作者原文中"深地 4000 米机器人开采"与"月球火星航线开通"**成立所必需的前置**，缺则链条悬空。

| 作者原话 | 节点 id | 类型 |
|---|---|---|
| 蓝星矿产资源开采到 4000 米地下，环境糟糕不适合人类开发，于是机器人采矿，使矿产开发程度极大提高 | `deep_mining_robot` | milestone（1.1） |
| 随着宇宙航天探索，尤其月球和火星航线的开通，机器人作为星际矿业手段被人类掌握 | `space_mining` | milestone（1.2） |
| 量子通讯与量子计算，使跨星际通讯成为常态技术，作为星际开发的基础设施 | `quantum_computing` + `interstellar_comm` | milestone（1.3，拆二） |
| （上述三条共同的能量前提；`power.txt` 亦两度点名"核能"） | `nuclear_power` | **bridge（本次一并补入）** |
| （"航线开通"的载体：火箭从一次性奇迹变为按班次起飞） | `space_route` | **bridge（本次一并补入）** |

**与 `power.txt` 的对应**：`nuclear_power` 落在 power.txt 第 3 点（"可以在没有太阳照射的地方也可以产生能量的核能"）；
`deep_mining_robot` / `space_mining` 落在第 2 点（"更快的提取自然界的元素"）在机器人时代的外推；
`interstellar_comm` 落在第 1 点（"更快的信息传输、更快的数据交换"）的极限形态。

---

## 附录 D · 代价向量 `cost`（v0.4 新增）

> 配套 `design.md` §4.6「熵代价」。

### D.1 为什么要这个字段

`design.md` §1.0 确立的元原则是：**负向力量不改变进化方向**。所以 `cost` **不是"第七力"**——
它描述的是**技术自身的副作用**，作用有三：

1. 让效率曲线不再是单调上升的直线（否则 5 块大陆的差异退化成"谁快一点"）
2. 制造"上一代技术的代价成为下一代技术的推力"这种反馈
   （如：炼铁耗尽林木 → 逼向煤炭 → `coal_fuel`）
3. 让 §4.6 的碳-气候耦合有数据来源

### D.2 词表

| 项 | 含义 | 累积后果 | 作用范围 |
|---|---|---|---|
| `carbon` | 碳排放 | → 全球升温 → 打断冰期旋回 | **全球（唯一一项）** |
| `land` | 生态破坏 / 土地退化 | → 农业产能↓ → 承载力↓ | 局部 |
| `deplete` | 资源枯竭 | → 矿藏递减 → 逼向深部（`design.md` §4.4） | 局部 |
| `noise` | 信息噪声 | → 有效信任半径↓ | 局部 |
| `unrest` | 社会撕裂 | → 组织效率↓ | 局部 |

数值是**半定量强度 0~1**，不是物理量。**缺省 = 无代价**——绝大多数节点不写 `cost`，
只有副作用真实的节点才标。

### D.3 唯一的全局项：`carbon`

五块大陆地理上永不接触（`design.md` INV-8），**却共享同一层大气**。因此 `carbon` 是本模型中
唯一能把 5 块孤立大陆连成一个系统的机制：

```
寒带大陆（无农业、必须烧更多燃料取暖，见 design.md §6.1）
    → 最早积累 carbon → 全球升温 → 冰期被提前打断 → 温带农业受益
```

这**不违反元原则**：升温是**加速**而非逆转进化方向。
作者原话的限定条件"**只要不是全球性的消失**"，正对应这一项的边界处理——
若 `carbon` 越界触发"失控温室"，才按全局性断点处理（默认关闭，见 `design.md` Q-A8）。

### D.4 与引擎的接口

- `Effect[]` 管**正收益**，`Cost[]` 管**副作用**，两者字段分离、独立演进
- 引擎每步：`CarbonLoad += Σ(该部落已掌握节点的 carbon) × 活动强度`
- **关闭 `cost` 解析时，模拟必须与 v0.3 逐位一致**（`design.md` INV-26）

---

## 附录 E · 预测节点不写进本文件（v0.5 约定）

> 配套 `design.md` §14「外推引擎」与 INV-29 / INV-30。

`design.md` §14 的外推引擎会输出一批**库中尚不存在、但按趋势律应该出现的节点提案**
（作者的例子：智能眼镜、人体植入 IC 芯片、脑波通讯）。**这些提案一律不写入本文件。**

**理由**：本文件是**历史记录**——它描述"人类进化出了什么"。一旦把预测写进来，
回测（`design.md` §14.5 的 B1/B2）就变成拿答案对答案的循环论证，引擎的预测能力再也无法验证。

| | 本文件 `tech_tree.md` | 提案表 `tech_proposals`（`world.db`） |
|---|---|---|
| 语义 | **已发生** | **可能发生** |
| 写入者 | 作者 / 人工确认 | 外推引擎（自动） |
| 可否被回测引用 | 可以（它是历史） | **不可以** |
| 生命周期 | 永久 | 可被 `Realized` / `Falsified` 终结 |

**唯一的流转通道**：某条提案若在模拟中真的被发明出来，或经作者确认，
**才由人工**把它作为正式节点补进本文件对应层级，同时把该提案状态置为 `Realized`。
在此之前，提案只活在 `tech_proposals` 表与 `design.md` §14.6 的说明里。


