# -*- coding: utf-8 -*-
"""
量取证图，而不是用眼睛看它。

为什么非要有这个脚本 —— 这个工程已经栽过一次：图里明明没有月亮，
却因为"右边那个白盘看着像月亮"就把"有月亮"写进了结论，而它是太阳。
后来把几何复刻出来才算清楚。教训不是"要小心"，是：
**关于画面的结论必须落到像素上** —— 要么量出来，要么别写。

本脚本回答四个问题，全部给数：
  1. 太阳、月亮在 01/02 两张图里是不是**同一个位置**（作者要求：拖地球时日月关系保持）
  2. 拨球之后星球是不是真的转了（大陆动没动）
  3. 月相是多少，亮的那一边是不是**朝着太阳**（"还原真实的关系"）
  4. 星球的受光面是不是朝着太阳

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
# 平行光的 _sunDir 投影到月亮处 = −4.3°。这里写死是为了让两个脚本互不依赖，
# 改了几何之后要一起更新（shoot_expect.py 会把它打印出来）。
LIGHT_DIR_DEG = -4.3


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

    # ── 1) 日月位置跨图是否一致 ────────────────────────────
    if len(skies) >= 2:
        names = list(skies)
        print(f"\n-- 日月的跨图一致性（{names[0]} vs {names[1]}） --")
        A, B = skies[names[0]], skies[names[1]]
        if len(A) and len(B):
            for i in range(min(len(A), len(B))):
                a1, b1 = A[i], B[i]
                c1 = circ_center(a1)
                c2 = circ_center(b1)
                dx, dy = c2[0] - c1[0], c2[1] - c1[1]
                print(f"  #{i}: 位移 dx={dx:+.1f} dy={dy:+.1f} px  "
                      f"半径 {c1[2]:.1f} -> {c2[2]:.1f}  "
                      f"rgb ({a1['mean_rgb'][0]:.0f},{a1['mean_rgb'][1]:.0f},{a1['mean_rgb'][2]:.0f})"
                      f" -> ({b1['mean_rgb'][0]:.0f},{b1['mean_rgb'][1]:.0f},{b1['mean_rgb'][2]:.0f})")

        # 直接对像素：日月的包围盒里，两张图应逐像素相同
        n0, n1 = names[0], names[1]
        A0 = [x for x in imgs if x[0] == n0][0][2]
        A1 = [x for x in imgs if x[0] == n1][0][2]
        for i in range(min(len(A), len(B))):
            o = A[i]
            pad = 6
            y0 = max(0, o["y0"] - pad); y1 = min(A0.shape[0], o["y1"] + pad)
            x0 = max(0, o["x0"] - pad); x1 = min(A0.shape[1], o["x1"] + pad)
            d = np.abs(A0[y0:y1, x0:x1] - A1[y0:y1, x0:x1])
            print(f"  #{i} 该块像素差 max={d.max():.0f} mean={d.mean():.3f}  "
                  f"（同位置则应为 0）")

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
    print("\n-- 月相 / 朝向（太阳应在月亮左还是右，看方向） --")
    for name, p, a, bg in imgs:
        objs = skies.get(name, [])
        if len(objs) < 2:
            print(f"{name}: 天空带里只找到 {len(objs)} 个天体，跳过")
            continue
        # 亮的那个是太阳（平均亮度高）
        by_lum = sorted(objs, key=lambda o: -o["lum_mean"])
        sun, moon = by_lum[0], by_lum[1]
        scx, scy, sr = circ_center(sun)
        mcx, mcy, mr = circ_center(moon)
        lc = lit_centroid(a, moon)
        if lc is None:
            continue
        to_sun = ang_deg(scx - mcx, scy - mcy)
        limb = ang_deg(lc[0] - mcx, lc[1] - mcy)
        dd = (limb - to_sun + 540.0) % 360.0 - 180.0
        sep = np.hypot(scx - mcx, scy - mcy)
        print(f"{name}: 太阳 r={sr:.1f} 亮度{sun['lum_mean']:.0f} | "
              f"月亮 r={mr:.1f} 亮度{moon['lum_mean']:.0f}")
        print(f"         月心->日心 方向 {to_sun:+.1f} deg，月面受光重心方向 {limb:+.1f} deg，"
              f"夹角差 {dd:+.1f} deg（0 表示亮面正对太阳）")
        print(f"         日月中心距 {sep:.1f} px，边缘间隙 {sep - sr - mr:.1f} px")
        # 亮面占月盘的比例 => 月相
        lum = luminance(a)
        y0, y1 = int(mcy - mr), int(mcy + mr)
        x0, x1 = int(mcx - mr), int(mcx + mr)
        y0 = max(0, y0); x0 = max(0, x0)
        sub = lum[y0:y1, x0:x1]
        mm = (np.sqrt(((a[y0:y1, x0:x1] - bg) ** 2).sum(axis=2)) > 14.0)
        if mm.sum() > 0:
            lv = sub[mm]
            half = (lv.min() + lv.max()) / 2.0
            print(f"         月盘亮面占比 {100.0*(lv > half).mean():.0f}% "
                  f"（按半高阈值粗数，纹理有明暗，只作参考；亮度 {lv.min():.0f}..{lv.max():.0f}）")

        # 终止线法 —— 受光方向取自 shoot_expect.py 算出的投影角
        frac, prof = terminator_profile(a, moon, LIGHT_DIR_DEG)
        if frac is not None:
            print(f"         终止线法受照比例 {100.0*frac:.0f}%")
        print("         剖面（从背光端到受光端，每格 10%）: "
              + " ".join("--" if math.isnan(p) else f"{p:3.0f}" for p in prof))
    return 0


if __name__ == "__main__":
    sys.exit(main())
