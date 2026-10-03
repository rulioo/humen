# -*- coding: utf-8 -*-
"""
保存进度 —— 生成一个**脱离 git 也能读**的还原点。

    python tools/snapshot.py v0.28-20261003
    → 生成 snapshot-v0.28-20261003/，含说明.txt

⚠️ **本项目 2026-10-03 起已经是 git 仓库**（`github.com/rulioo/humen`，**PUBLIC**），
   所以本工具**不是**版本控制的替代品，意义变了：
   · git 管的是"逐次改动与回滚"；本工具管的是"**此刻的整份可读副本**"，
     解压即读，不需要仓库、不需要 git log，适合归档与离线查看。
   · 而且它**不含 world.db 与构建产物**（见下），所以它**不是**一份完整的可运行副本 ——
     别把它当成"有它就够了"。

只收「丢了就写不回来」的东西：源码 · 文档 · 脚本 · Unity 脚本/编辑器脚本/场景/工程设置 · 数据契约。
不收构建产物、渲染图、日志（都能重生成；E: 盘余量在 1~20 GB 之间浮动，每次现量）。

⚠️ **本工具自带核对，理由是本工程栽过太多次同一款跟头：缺失不报错。**
   做这个工具时就中了两次，都**看起来是成功的**：
     · 源码被摊平 —— `src/Humen.Core` 与 `src/Humen.Cli` 的文件混进同一个 `src/`，
       同名者互相覆盖且一声不吭；
     · 场景被静默跳过 —— 扫的是 `unity/Assets` 根目录，而 `*.unity` 在 `Scenes/` 子目录下，
       于是两个场景文件没进快照，`files: 68` 照样打印得像一次成功。
   所以收完必须**反向核对**：把「应该有的」列出来逐项查，缺一个就报错退出。
   快照的价值全在「能原路径盖回去」，缺件而不自知的快照比没有快照更坏。
"""
import io, os, shutil, sys, time

SKIP_EXT = {".png", ".jpg", ".jpeg", ".exr", ".tga", ".psd"}


# (源目录, 目标前缀, 排除目录)
TREES = [
    ("src/Humen.Core", "src/Humen.Core", ("bin", "obj", "__pycache__")),
    ("src/Humen.Cli", "src/Humen.Cli", ("bin", "obj", "__pycache__")),
    ("tools", "tools", ("__pycache__",)),
    ("unity/Assets/Scripts", "unity/Assets/Scripts", ("__pycache__",)),
    # ⚠️ v0.28 补：`unity/Assets/Editor` 此前**不在清单里**，于是整个场景构建器
    #    （PlanetSceneBuilder / SettlementSceneBuilder / PlayerBuilder / UrpSetup /
    #     PreviewRenderer / OrbitForensics / TimelinePreviewRenderer）**一个都没进快照** ——
    #    恢复出来的工程能按 Play，但**重建不了场景、跑不了取证**。
    #    这正是本文件开头记的那款跟头（"场景被静默跳过"）**换个地方又犯了一遍**：
    #    缺件不报错，`files: 68` 照样打印得像一次成功。
    #    核对的清单和打包的清单是同一份（见 inventory），所以**清单本身漏了，
    #    自带的核对也一起瞎** —— 反向核对只能查"清单里的项在不在"，查不出"清单少了什么"。
    ("unity/Assets/Editor", "unity/Assets/Editor", ()),
    ("unity/Assets/Scenes", "unity/Assets/Scenes", ()),
    # StreamingAssets 只有 `World/world_view.json`（2.1 MB，Core 与 Unity 之间**唯一的数据契约**）。
    # 它由 `humen export` 从 world.db 生成，而 **world.db 不进快照** ——
    # 所以站在"只靠这个快照能不能还原"的角度，它**是不可再生的**，正是本工具要收的那一类。
    ("unity/Assets/StreamingAssets", "unity/Assets/StreamingAssets", ()),
    ("unity/ProjectSettings", "unity/ProjectSettings", ()),
    ("unity/Packages", "unity/Packages", ()),
]
# ⚠️ 有意**不收** `unity/Assets/Settings`（URP 资产与 .mat，42 个小文件）：它们全部由
#    `UrpSetup` 与 `PlanetSceneBuilder.EnsureMarkerMaterialAssets` **按代码生成**，
#    属于本工程那条「能被代码重新算出来的，就不要存成手工状态」。
#    更要紧的是：把一份**旧的**生成物还原到**新的**代码上，两边会静默地对不上
#    （v0.22 ⑧ 那款"改了配色、材质还是旧的"就是这么来的）。
LOOSE = ["design.md", "tech_tree.md", "power.txt", "aim.txt", "req.txt",
         "id.md", "world.summary.json", u"启动.cmd"]


def inventory():
    """
    **唯一的**「该收哪些」清单，`(源路径, 快照内相对路径)`。

    打包和核对**都从这里取** —— 早先两处各写一遍，规则不一致：
    tools/ 下的 snapshot.py 被打包时按名字跳过、核对时却算作应有，
    于是自己把自己报成缺件。两份规则迟早会分叉，**结构上只留一份**。
    """
    items = []
    for root, pref, excl in TREES:
        if not os.path.isdir(root):
            print("WARN tree missing:", root)
            continue
        for r, d, f in os.walk(root):
            d[:] = [x for x in d if x not in excl]
            for n in f:
                if os.path.splitext(n)[1].lower() in SKIP_EXT:
                    continue
                full = os.path.join(r, n)
                rel = os.path.relpath(full, root).replace("\\", "/")
                items.append((full, pref + "/" + rel))
    for n in LOOSE:
        if os.path.exists(n):
            items.append((n, n))
        else:
            print("WARN loose file missing:", n)
    return items


def build(dst):
    items = inventory()
    if os.path.isdir(dst):
        shutil.rmtree(dst)
    size = 0
    for src, rel in items:
        out = os.path.join(dst, rel.replace("/", os.sep))
        os.makedirs(os.path.dirname(out), exist_ok=True)
        shutil.copyfile(src, out)
        size += os.path.getsize(src)
    return [(s, r) for s, r in items], size


def verify(dst, items):
    """反向核对：清单里的每一项都要真的落盘。"""
    have = set()
    for r, d, f in os.walk(dst):
        for n in f:
            have.add(os.path.relpath(os.path.join(r, n), dst).replace("\\", "/"))
    miss = sorted(rel for _, rel in items if rel not in have)
    if miss:
        print("FAIL: %d file(s) missing from snapshot" % len(miss))
        for m in miss[:20]:
            print("   MISS", m)
        return False
    print("verify OK: all %d expected files present" % len(items))
    return True


README = u"""蓝星 / Humen —— 进度快照
============================
时间：@TIME@
对应文档版本：design.md v0.28

为什么有这个东西：一份**脱离 git 也能读**的还原点（本项目 2026-10-03 起已是
                  git 仓库 github.com/rulioo/humen，**PUBLIC**）。
                  ⚠️ 它**不含 world.db 与构建产物**，所以不是一份完整的可运行副本。

里面是什么
----------
  design.md          设计文档 —— 变更记录 + 待裁册是进度的事实来源
  tech_tree.md       技术树（175 节点）
  power.txt / aim.txt / req.txt   原始需求与设计输入
  world.summary.json 当前世界的概要读数
  src/Humen.Core · src/Humen.Cli        源码（已剔除 bin/obj）
  tools/             出图与量图脚本，以及本快照工具
  unity/Assets/Scripts · unity/Assets/Editor · unity/Assets/Scenes · 全部 .meta
  unity/Assets/StreamingAssets/World/world_view.json   Core 与 Unity 的唯一数据契约
  unity/ProjectSettings · unity/Packages
  启动.cmd           启动器

怎么还原
--------
按原路径覆盖回 E:\\cc\\humen\\ 即可。
⚠️ 覆盖前先把当前文件另存一份 —— 快照是**旧的**，覆盖等于回退。
⚠️ **Unity 部分必须连 .meta 一起还原。** 场景里的脚本引用是**按 GUID** 指的，
   而 GUID 存在 .meta 里；只还原 .cs 不还原 .meta，Unity 会重新分配 GUID，
   Planet.unity 里那些组件就全变成「Missing (Mono Script)」。

没放进来、以及为什么
--------------------
  world.db（28 MB）  跑一次 `humen evolve` 就能重生成，故不收。
                     ⚠️ 但它是待裁 ㉓ 的证据来源，**别在没备份的情况下重跑 evolve**。
                     ⚠️ 也正因为不收它，`world_view.json`（数据契约，2.1 MB）**必须收** ——
                        没有 world.db 就再也导不出它，它记的正是这批画面背后的那个世界。
  unity/Assets/Settings   URP 资产与 .mat，**全部由代码生成**（UrpSetup /
                     EnsureMarkerMaterialAssets）。收了反而危险：把旧的生成物还原到新代码上，
                     两边会静默地对不上（v0.22 ⑧ 那款"改了配色、材质还是旧的"）。
  bin/ build/        构建产物，重编译即可
  renders/ *.log     出图与日志，可重生成
  贴图 *.png         可从 world 重导

当前状态（快照时刻）
--------------------
  · 文档 v0.28：**太阳放到真距离（1012R）** —— 月亮那 23.19 倍压缩照搬到太阳上，
    于是日:月的距离比 389.2 / 半径比 400.4 / 角径比 1.0284 三项全部为真（真实 389.17 / 400.43 / 1.0289）。
    代价作者已接受：**默认机位下日盘在镜头背后**（δ 124.86°，δ+ψ = 180°−ε，ε=0.139° 是太阳视差角）；
    往夜面拖它会升进画面，而那一刻蓝星是暗的（日盘可见 <=> 蓝星背光）。
    **「待裁 C」因此作废** —— 月亮亮面朝不到日盘那个 74.1° 的老问题自己消失了（现测 0.137°）。
  · 文档 v0.27：拨球从「转球」改成**转相机**（Cesium rotate3D 真正在做的事），
    并加了 `F` 复位（机架 + 自转 + 惯性，三件事）。
  · 文档 v0.26：时间轴能在球面上看见进化 —— 疆域层 + 道路层 + 默认机位对准最大大陆。
  · 待裁 ㉓：4/5 大陆停在「游群」的根因 = `planting` 的 `geo: [可耕地]` 门槛 + 灌溉机制缺失
    （规则写在 §6.1 T4，代码里没实现）。**三条改法待作者定，未擅动。**
    补充取证（2026-10-03）：瓶颈是**四种病**，只改霜冻仅救回 7 个部落。
  · 待裁 ㉒：`MaterialTier` 量的是「机会」不是「成就」
  · ❌ `冰期可驱动` 现测 x1.18（判据 >= x1.5）—— 该判据是两个面积占比之比，
    而陆地面积已从 10.89% 抬到 29.91%，分母换了含义也换了，待作者定
  · ⚠️ **E: 盘余量在 1~20 GB 之间浮动，不要信任何具体数字，每次现量** —— 再跑 Unity 构建前先看
"""


def main():
    label = sys.argv[1] if len(sys.argv) > 1 else time.strftime("%Y%m%d-%H%M")
    dst = "snapshot-" + label
    items, size = build(dst)

    io.open(os.path.join(dst, u"说明.txt"), "w", encoding="utf-8").write(
        README.replace("@TIME@", time.strftime("%Y-%m-%d %H:%M")))

    print("%s: %d files, %.1f MB" % (dst, len(items) + 1, size / 1048576.0))
    if not verify(dst, items):
        print("snapshot is INCOMPLETE - do not rely on it")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
