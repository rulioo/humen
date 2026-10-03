# -*- coding: utf-8 -*-
"""
量取证图，而不是用眼睛看它。

为什么非要有这个脚本 —— 这个工程已经栽过一次：图里明明没有月亮，
却因为"右边那个白盘看着像月亮"就把"有月亮"写进了结论，而它是太阳。
后来把几何复刻出来才算清楚。教训不是"要小心"，是：
**关于画面的结论必须落到像素上** —— 要么量出来，要么别写。

本脚本回答四个问题，全部给数：
  1. 月亮在 01/02 两张图里**动了没有**，以及动的方向对不对
     （⚠️ v0.28 改了口径：原来问的是"日月的跨图像素差是不是 0"，
      而那个判据**已经被作废** —— 见下面 ⚠️）
  2. 拨球之后星球是不是真的转了（大陆动没动）
  3. 月相是多少，亮的那一边是不是**朝着该朝的方向**（"还原真实的关系"）
  4. 星球的受光面是不是朝着太阳

⚠️ **v0.28：日盘不在画面里了，"日月"这套只会剩月亮。**
   太阳移到 1012R 之后默认机位下它在镜头背后，天空带里只有一个月盘。
   于是凡是"挑出更亮的那个当天体/拿日心当方向基准"的做法都会静默走偏 ——
   这里原来就靠"两个天体里更亮的那个是太阳"来定位，现在它会**只找到一个**，
   然后整节跳过，输出一段看着像"数据不足"的沉默。所以下面改成只认月亮。

⚠️ **并且"日月像素差 = 0"这条判据已经退场，别再把它捡回来。**
   它在两种**截然相反**的原因下取同一个值：关系真的保住了，
   以及整个世界被冻住了。v0.25 正是把那个 0 读成"关系保持完美"而推翻了整版设计。
   **一个指标在两种相反原因下同值，它就不是判据。**
   现在改量的是一件事的两面：月亮跨图的**位移量**（必须非零）
   与三维不变量（必须不变）——分别由本脚本与 OrbitForensics 给出。

不依赖 scipy：连通域用"列方向连续段"来分，够用且不会误连。
输出全 ASCII —— 这台机器的控制台是 GBK，中文输出会炸。
"""
import math
import os
import sys
import numpy as np
from PIL import Image


def load(path):
    im = Image.open(path).convert("RGB")
    return np.asarray(im).astype(np.float32)


def luminance(a):
    return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114


def bg_color(a):
    """背景色取右上角一小块 —— 那一片在两版取证图里都是空天。"""
    h, w, _ = a.shape
    return a[4:24, w - 24:w - 4].reshape(-1, 3).mean(axis=0)


def runs(cols):
    """把一维布尔数组切成若干连续 True 段的 [起, 止] 列表。"""
    out = []
    start = None
    for i, v in enumerate(cols):
        if v and start is None:
            start = i
        elif not v and start is not None:
            out.append((start, i - 1))
            start = None
    if start is not None:
        out.append((start, len(cols) - 1))
    return out


def objects_in(a, band, bg, thresh=14.0, min_cols=3):
    """
    在 x 区间 band 里找出所有"非背景"的块，按列连续段切分。
    返回 [{x0,x1,y0,y1,cx,cy,area,mean_rgb,lum_min,lum_max,lum_mean}, ...]
    """
    x0, x1 = band
    sub = a[:, x0:x1]
    d = np.sqrt(((sub - bg) ** 2).sum(axis=2))
    mask = d > thresh
    cols = mask.any(axis=0)
    lum_all = luminance(sub)
    out = []
    for (c0, c1) in runs(cols):
        if c1 - c0 + 1 < min_cols:
            continue
        col = mask[:, c0:c1 + 1]
        rr = runs(col.any(axis=1))
        if not rr:
            continue
        r0, r1 = rr[0][0], rr[-1][1]

        # 对象自己的围盒内的掩膜 —— 之后 lit_centroid 直接按同一套坐标切片即可
        om = col[r0:r1 + 1, :]
        lum = lum_all[r0:r1 + 1, c0:c1 + 1][om]
        px = sub[:, c0:c1 + 1][r0:r1 + 1, :][om]
        ys, xs = np.nonzero(om)
        out.append(dict(
            x0=c0 + x0, x1=c1 + x0, y0=r0, y1=r1,
            cx=xs.mean() + c0 + x0, cy=ys.mean() + r0,
            area=int(om.sum()),
            mean_rgb=px.mean(axis=0),
            lum_min=float(lum.min()), lum_max=float(lum.max()),
            lum_mean=float(lum.mean()),
            mask=om))
    return out


def circ_center(o):
    """用包围盒推圆心与半径（够用：日/月都是圆盘）。"""
    r = ((o["x1"] - o["x0"] + 1) + (o["y1"] - o["y0"] + 1)) / 4.0
    return ((o["x0"] + o["x1"]) / 2.0, (o["y0"] + o["y1"]) / 2.0, r)


def describe(tag, o):
    cx, cy, r = circ_center(o)
    mr, mg, mb = o["mean_rgb"]
    print(f"  {tag:<6} bbox=({o['x0']},{o['y0']})-({o['x1']},{o['y1']}) "
          f"center=({cx:.1f},{cy:.1f}) r={r:.1f} area={o['area']} "
          f"rgb=({mr:.0f},{mg:.0f},{mb:.0f}) "
          f"lum {o['lum_min']:.0f}..{o['lum_max']:.0f} mean {o['lum_mean']:.0f}")
    return cx, cy, r


def lit_centroid(a, o):
    """块内亮度加权重心 —— 受光面朝哪边，看它相对几何中心偏向哪边。"""
    lum = luminance(a)[o["y0"]:o["y1"] + 1, o["x0"]:o["x1"] + 1]
    w = np.clip(lum - lum.min(), 0, None) * o["mask"]
    tot = w.sum()
    if tot <= 0:
        return None
    ys, xs = np.mgrid[0:w.shape[0], 0:w.shape[1]]
    return (o["x0"] + (xs * w).sum() / tot, o["y0"] + (ys * w).sum() / tot)


def ang_deg(dx, dy):
    return np.degrees(np.arctan2(dy, dx))


def terminator_profile(a, o, light_deg):
    """
    沿受光方向切一条一维亮度剖面，用来定**终止线**的位置，从而定受照比例。

    为什么要这么费事：之前在月盘上按"亮度超过半高"数像素，得到 21%~28%，
    与几何预想的 56% 差了一倍 —— 但那个数**不可信**，因为月面有程序生成的
    斑块（为了让潮汐锁定看得见），暗斑会被当成夜面。
    改用**每个分箱取中位数**：纹理在箱内被平均掉，剩下的就是光照梯度。
    受照比例由终止线位置给：终止线落在 u=t 处（u 为沿受光方向归一化到 ±1 的坐标），
    则受照 = (1 − t)/2。
    """
    lum = luminance(a)
    cx, cy, r = circ_center(o)
    ys, xs = np.mgrid[o["y0"]:o["y1"] + 1, o["x0"]:o["x1"] + 1]
    m = o["mask"]
    lv = lum[o["y0"]:o["y1"] + 1, o["x0"]:o["x1"] + 1]
    u = ((xs - cx) * math.cos(math.radians(light_deg))
         + (ys - cy) * math.sin(math.radians(light_deg))) / r

    edges = np.linspace(-1.0, 1.0, 21)
    prof = []
    for i in range(len(edges) - 1):
        sel = m & (u >= edges[i]) & (u < edges[i + 1])
        prof.append(float(np.median(lv[sel])) if sel.sum() > 8 else float("nan"))

    have = [p for p in prof if not math.isnan(p)]
    if len(have) < 6:
        return None, prof

    # ⚠️ 不能用"亮度中值"当终止线。朗伯面上，亮度从终止线处的 0 **线性**升到
    #    边缘处的最大 —— 中值点落在斜坡的中点，而不是坡脚。
    #    第一版就是这么错的：剖面明明是 19 19 19 | 37 57 …，
    #    却被判成"受照 2%"，差了一个数量级，而且看着还挺像个结果。
    #    终止线是**离开暗底**的地方，所以判据取"暗底 + 15% 动态范围"。
    floor = float(np.percentile(have, 25))
    top = float(np.max(have))
    thresh = floor + 0.15 * (top - floor)

    t = None
    for i in range(len(prof)):
        if not math.isnan(prof[i]) and prof[i] > thresh:
            t = edges[i]          # 该分箱的下沿即为终止线所在
            break
    frac = None if t is None else (1.0 - t) / 2.0
    return frac, prof


# 月面受光方向在屏上的角度（度）。由 shoot_expect.py 独立算出：
# 平行光 _sunDir 投影到月亮处 = **+1.20°**（v0.28 读数）。
# 这里写死是为了让两个脚本互不依赖，改了几何之后要一起更新
# （shoot_expect.py 的「月面受光朝向」一节会把它打印出来，两处必须一致）。
#
# ⚠️ v0.28 起这个数**不再需要区分"平行光"与"真太阳方向"**：太阳移到 1012R 后，
#    两者在月亮处只差 0.135°（v0.27 是 74.1°），印在 91×81 px 的月盘上分不出来。
#    旧值 −4.3° 是 v0.25 机位下的，换机位就作废了。
LIGHT_DIR_DEG = 1.2


def main():
    paths = sys.argv[1:]
    if len(paths) < 1:
        print("usage: shoot_check.py <png> [png ...]")
        return 2

    imgs = []
    for p in paths:
        a = load(p)
        bg = bg_color(a)
        imgs.append((os.path.basename(p).encode("ascii", "replace").decode(), p, a, bg))
        print(f"== {p}  size={a.shape[1]}x{a.shape[0]}  bg=({bg[0]:.0f},{bg[1]:.0f},{bg[2]:.0f})")

    # ── 天空带：星球右侧那一条，只有太阳和月亮 ──────────────
    print("\n-- 天空带（星球右侧） --")
    skies = {}
    for name, p, a, bg in imgs:
        h, w, _ = a.shape
        objs = objects_in(a, (int(w * 0.68), w), bg)
        objs.sort(key=lambda o: o["cx"])
        print(f"{name}:")
        skies[name] = objs
        for i, o in enumerate(objs):
            describe(f"#{i}", o)

    # ── 星球本体：中央那一段 ────────────────────────────────
    print("\n-- 星球本体（中央） --")
    planets = {}
    for name, p, a, bg in imgs:
        h, w, _ = a.shape
        objs = objects_in(a, (int(w * 0.20), int(w * 0.68)), bg, thresh=10.0, min_cols=20)
        objs.sort(key=lambda o: -o["area"])
        planets[name] = objs[0] if objs else None
        if not objs:
            print(f"{name}: 没找到")
            continue
        print(f"{name}: {len(objs)} 块")
        cx, cy, r = describe("#0(最大)", objs[0])
        lc = lit_centroid(a, objs[0])
        if lc:
            print(f"         受光重心=({lc[0]:.0f},{lc[1]:.0f})  "
                  f"方向 {ang_deg(lc[0]-cx, lc[1]-cy):+.1f} deg")

    # ── 1) 月亮跨图的位移 ──────────────────────────────────
    # ⚠️ 口径换了，理由见文件头：原来量"日月包围盒的像素差，同位置则应为 0"，
    #    而那个 0 在"关系保住"与"世界冻住"两种相反原因下同值 —— 不是判据。
    #    现在量**位移量**：相机在绕球转，月亮就必须在屏上挪窝；挪了才说明世界是活的。
    #    反面（世界冻住）由 OrbitForensics 的三维不变量与本脚本的"拨球效果"一起排掉。
    if len(skies) >= 2:
        names = list(skies)
        print(f"\n-- 月亮跨图位移（{names[0]} vs {names[1]}） --")
        A, B = skies[names[0]], skies[names[1]]
        if len(A) and len(B):
            a1, b1 = A[0], B[0]
            c1 = circ_center(a1)
            c2 = circ_center(b1)
            dx, dy = c2[0] - c1[0], c2[1] - c1[1]
            moved = math.hypot(dx, dy)
            print(f"  月亮: 位移 dx={dx:+.1f} dy={dy:+.1f} px  |位移|={moved:.1f} px  "
                  f"半径 {c1[2]:.1f} -> {c2[2]:.1f}")
            print("  => " + ("通过：月亮确实在屏上挪了（相机绕球转的视差）"
                             if moved > 1.0 else
                             "**位移 ~0 —— 这是「世界被冻住」的指纹，"
                             "不是「关系保持完美」**（v0.25 就是在这里读反的）"))
        else:
            print(f"  天空带里 {len(A)} / {len(B)} 个天体 —— "
                  f"v0.28 起应当各只有月亮 1 个")

    # ── 2) 拨球前后星球是否真的动了 ────────────────────────
    if len(imgs) >= 2:
        print("\n-- 拨球效果 --")
        A0, A1 = imgs[0][2], imgs[1][2]
        h, w, _ = A0.shape
        x0, x1 = int(w * 0.20), int(w * 0.68)
        d = np.abs(A0[:, x0:x1] - A1[:, x0:x1])
        print(f"  星球band 平均像素差={d.mean():.1f}  最大={d.max():.0f}  "
              f"变化像素占比={100.0*(d.max(axis=2) > 20).mean():.1f}%")

    # ── 3) 月相与朝向 ──────────────────────────────────────
    # ⚠️ v0.28：天空带里**只有月亮**了，所以不能再"挑出更亮的那个当太阳"来定方向基准 ——
    #    那样只会找到一个天体，整节静默跳过，输出一段像"数据不足"的沉默。
    #    方向基准改用 shoot_expect.py 独立算出的 LIGHT_DIR_DEG（见它的定义处）。
    print("\n-- 月相 / 朝向（亮面应朝向 LIGHT_DIR_DEG 那个方向） --")
    for name, p, a, bg in imgs:
        objs = skies.get(name, [])
        if not objs:
            print(f"{name}: 天空带里没有天体 —— v0.28 起应当只有月亮 1 个；"
                  f"0 个说明月亮出画或被蓝星挡住了")
            continue
        # ⚠️ **多于一个天体时不许"挑一个当月亮"。** v0.27 及以前的图里天空带确实有两个
        #    （日盘在画面右侧），那时按"面积最大"或"最亮"去挑，挑中的都是**太阳**，
        #    然后把它当月亮量出"受光 0%、剖面一条平线 244" —— 一串看着像结论的数，
        #    而它说的全是太阳。**分不清就别猜**，逐个报出来，人来认。
        if len(objs) > 1:
            print(f"{name}: ⚠️ 天空带里有 {len(objs)} 个天体，而 v0.28 起应当只有月亮一个 ——"
                  f"这些**要么是老图、要么是新图出了事**。逐个列出，请人工认哪个是月亮：")
        for i, body in enumerate(objs):
            mcx, mcy, mr = circ_center(body)
            lc = lit_centroid(a, body)
            limb = None if lc is None else ang_deg(lc[0] - mcx, lc[1] - mcy)
            bw = body["x1"] - body["x0"] + 1
            bh = body["y1"] - body["y0"] + 1
            tag = "月亮" if len(objs) == 1 else f"天体#{i}"
            print(f"{name}: {tag} r={mr:.1f} 亮度{body['lum_mean']:.0f} bbox {bw}x{bh}")
            if limb is None:
                print("         受光重心不可得（整块同亮，可能是个纯色盘）")
                continue
            dd = (limb - LIGHT_DIR_DEG + 540.0) % 360.0 - 180.0
            print(f"         受光重心方向 {limb:+.1f} deg，几何预想 {LIGHT_DIR_DEG:+.1f} deg，"
                  f"差 {dd:+.1f} deg（0 表示亮面朝向对）")
            print(f"         注：围盒比 {max(bw,bh)/min(bw,bh):.3f} 只是个粗看 —— "
                  f"离轴球体投出来是**斜椭圆**，其长短轴比才是判据"
                  f"（shoot_expect 预想 1.124），斜椭圆的围盒不等于它的两个轴。")
            # 亮面占月盘的比例 => 月相
            lum = luminance(a)
            y0, y1 = max(0, int(mcy - mr)), int(mcy + mr)
            x0, x1 = max(0, int(mcx - mr)), int(mcx + mr)
            sub = lum[y0:y1, x0:x1]
            mm = (np.sqrt(((a[y0:y1, x0:x1] - bg) ** 2).sum(axis=2)) > 14.0)
            if mm.sum() > 0:
                lv = sub[mm]
                half = (lv.min() + lv.max()) / 2.0
                print(f"         亮面占比 {100.0*(lv > half).mean():.0f}% "
                      f"（按半高阈值粗数，纹理有明暗，只作参考；亮度 {lv.min():.0f}..{lv.max():.0f}）")

            # 终止线法 —— 受光方向取自 shoot_expect.py 算出的投影角
            frac, prof = terminator_profile(a, body, LIGHT_DIR_DEG)
            if frac is not None:
                print(f"         终止线法受照比例 {100.0*frac:.0f}%")
            print("         剖面（从背光端到受光端，每格 10%）: "
                  + " ".join("--" if math.isnan(q) else f"{q:3.0f}" for q in prof))
    return 0


if __name__ == "__main__":
    sys.exit(main())
