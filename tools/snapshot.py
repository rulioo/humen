# -*- coding: utf-8 -*-
"""
保存进度 —— 本项目**不是 git 仓库**，改动没有版本控制兜底，所以需要一个还原点。

    python tools/snapshot.py v0.25-20261003
    → 生成 snapshot-v0.25-20261003/，含说明.txt

只收「丢了就写不回来」的东西：源码 · 文档 · 脚本 · Unity 脚本/场景/工程设置。
不收构建产物、渲染图、日志（都能重生成，而 E: 盘只剩 1.2 GB）。

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
    ("unity/Assets/Scenes", "unity/Assets/Scenes", ()),
    ("unity/ProjectSettings", "unity/ProjectSettings", ()),
    ("unity/Packages", "unity/Packages", ()),
]
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
对应文档版本：design.md v0.25

为什么有这个东西：本项目**不是 git 仓库**，改动没有版本控制兜底。
                  这是 `python tools/snapshot.py <标签>` 留下的还原点。

里面是什么
----------
  design.md          设计文档 —— 变更记录 + 待裁册是进度的事实来源
  tech_tree.md       技术树（175 节点）
  power.txt / aim.txt / req.txt   原始需求与设计输入
  world.summary.json 当前世界的概要读数
  src/Humen.Core · src/Humen.Cli        源码（已剔除 bin/obj）
  tools/             出图与量图脚本，以及本快照工具
  unity/Assets/Scripts · unity/Assets/Scenes · 全部 .meta
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
  world.db（28 MB）  跑一次 `humen evolve` 就能重生成；E: 盘只剩 1.2 GB，故不收。
                     ⚠️ 但它是待裁 ㉓ 的证据来源，**别在没备份的情况下重跑 evolve**。
  bin/ build/        构建产物，重编译即可
  renders/ *.log     出图与日志，可重生成
  贴图 *.png         可从 world 重导

当前状态（快照时刻）
--------------------
  · 文档 v0.25：Cesium 式拨球 + 自转开关 + 日月真关系，已在**播放器**里出图并逐像素验证过
    （拖拽时太阳像素差 max=0，月亮只动 1 px，星球本体变了 16.8%）
  · 待裁 ㉓：4/5 大陆停在「游群」的根因 = `planting` 的 `geo: [可耕地]` 门槛 + 灌溉机制缺失
    （规则写在 §6.1 T4，代码里没实现）。**三条改法待作者定，未擅动。**
  · 待裁 ㉒：`MaterialTier` 量的是「机会」不是「成就」
  · ❌ `冰期可驱动` 现测 x1.18（判据 >= x1.5）—— 该判据是两个面积占比之比，
    而陆地面积已从 10.89% 抬到 29.91%，分母换了含义也换了，待作者定
  · ⚠️ **E: 盘只剩 1.2 GB（200 G 已用 100%）** —— 再跑 Unity 构建前先腾地方
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
