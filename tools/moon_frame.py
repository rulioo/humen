"""月亮的初始公转相位该取多少 —— 既在画面里、又不被蓝星挡住、还能看出月相。

复刻 unity 侧三个脚本的算式，逐字对应：
  PlanetBootstrap.Radius      = 1000
  OrbitCamera                 : Yaw=-35, Pitch=18, DistanceFactor=3.0
  PlanetSceneBuilder          : fieldOfView=60（竖直）, 2560x1369
  SunMoonSystem.PlaceMoon / BuildSunDirection

**这个脚本存在的理由**：`_moonAngleRad` 默认 0，而 θ=0 时月亮离视轴 54°，
在 60° 视锥之外 —— 播放器里**根本没有月亮**（编辑器取证图里那个白盘是**太阳**，
屏上偏角 36.8°，落在画面右侧 x≈0.85）。选一个默认相位本来要手算，
但轨道面有 5.1° 倾角、FOV 又是竖直的，解析式容易错；这里直接扫全周，
判据用的就是最终真正作数的那个（投影到 viewport）。

⚠️ 相机基的左右手约定照 Unity 来（左手系）：right = cross(up, forward)。
   第一版写成了 cross(forward, up)，太阳被镜像到画面另一半 ——
   而 y 完全对得上，只有 x 反了，看着很像"太阳相位角取反了"。
   判定依据是**已经渲出来的那张图**：日盘在画面右侧。→ 自检一节就是干这个的。
"""

import math

R = 1000.0
YAW, PITCH, DIST_F = -35.0, 18.0, 3.0
FOV_V = 60.0
W, H = 2560, 1369
ASPECT = W / float(H)
SUN_DIST_F, SUN_PHASE, SUN_RADIUS_F = 1.8, 55.0, 0.20
MOON_INC, MOON_RADIUS_F = 5.1, 0.27
PLANET_ANG = math.degrees(math.asin(R / (DIST_F * R)))      # 蓝星视半径 ≈ 19.5°


def sub(a, b): return tuple(x - y for x, y in zip(a, b))
def add(a, b): return tuple(x + y for x, y in zip(a, b))
def mul(a, s): return tuple(x * s for x in a)
def dot(a, b): return sum(x * y for x, y in zip(a, b))
def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def norm(a):
    return mul(a, 1.0 / math.sqrt(dot(a, a)))
def ang(a, b):
    return math.degrees(math.acos(max(-1.0, min(1.0, dot(norm(a), norm(b))))))


def euler_vec(x, y, z, v):
    """Unity Quaternion.Euler(x,y,z) 作用到向量：R = Ry * Rx * Rz。"""
    xr, yr, zr = map(math.radians, (x, y, z))
    v = (v[0] * math.cos(zr) - v[1] * math.sin(zr),
         v[0] * math.sin(zr) + v[1] * math.cos(zr), v[2])
    v = (v[0], v[1] * math.cos(xr) - v[2] * math.sin(xr),
         v[1] * math.sin(xr) + v[2] * math.cos(xr))
    v = (v[0] * math.cos(yr) + v[2] * math.sin(yr), v[1],
         -v[0] * math.sin(yr) + v[2] * math.cos(yr))
    return v


CAM = mul(euler_vec(PITCH, YAW, 0.0, (0.0, 0.0, -1.0)), DIST_F * R)
FWD = norm(mul(CAM, -1.0))
RIGHT = norm(cross((0.0, 1.0, 0.0), FWD))          # Unity 左手系：right = cross(up, fwd)
UP = cross(FWD, RIGHT)
TANV = math.tan(math.radians(FOV_V * 0.5))

TO_CAM = norm(CAM)
SPREAD = norm(cross((0.0, 1.0, 0.0), mul(TO_CAM, -1.0)))   # = cross(up, forward)
_ph = math.radians(SUN_PHASE)
SUN_DIR = norm(add(mul(TO_CAM, math.cos(_ph)), mul(SPREAD, math.sin(_ph))))
SUN_POS = mul(SUN_DIR, SUN_DIST_F * R)
MOON_R = MOON_RADIUS_F * R


def viewport(p):
    d = sub(p, CAM)
    z = dot(d, FWD)
    if z <= 0:
        return None
    return (0.5 + 0.5 * (dot(d, RIGHT) / (z * TANV * ASPECT)),
            0.5 + 0.5 * (dot(d, UP) / (z * TANV)), z)


def to_px(v):
    return (v[0] * W, (1.0 - v[1]) * H)


def occluded(p):
    """相机→天体的线段是否被半径 R 的球挡住。"""
    d = sub(p, CAM)
    L = math.sqrt(dot(d, d))
    u = mul(d, 1.0 / L)
    t = -dot(CAM, u)
    if t < 0 or t > L:
        return False
    c = add(CAM, mul(u, t))
    return math.sqrt(dot(c, c)) < R


def moon_pos(theta_deg, orbit_f):
    inc = math.radians(MOON_INC)
    on = (0.0, math.cos(inc), -math.sin(inc))
    u = norm(cross(on, (0.0, 1.0, 0.0)))
    w = norm(cross(on, u))
    th = math.radians(theta_deg)
    return mul(add(mul(u, math.cos(th)), mul(w, math.sin(th))), orbit_f * R)


def moon_lit(p):
    """从相机看，月面被照亮的那一份。"""
    return (1.0 + math.cos(math.radians(ang(SUN_DIR, sub(CAM, p))))) * 0.5


def sun_screen_gap_px(px):
    s = to_px(viewport(SUN_POS))
    return math.hypot(px[0] - s[0], px[1] - s[1])


def in_frame(p, margin_r=1.5):
    """在视锥里，且四周留出月亮自己视半径的 margin_r 倍。
    ⚠️ viewport() 返回非 None 只说明「在相机前方」—— 必须再查 0..1，
       否则球背面偏到 54° 的点也会被算成「在画面里」。"""
    v = viewport(p)
    if v is None:
        return None
    x, y, z = v
    mx = math.degrees(math.atan(MOON_R / z)) / (FOV_V * 0.5) * 0.5
    if not (mx * margin_r < x < 1 - mx * margin_r and mx * margin_r < y < 1 - mx * margin_r):
        return None
    return v


def visible_intervals(orbit_f):
    """全周扫一遍，返回月亮「在画面里且不被蓝星挡住」的 θ 区间。"""
    ok = []
    for deg in range(0, 360):
        p = moon_pos(deg, orbit_f)
        if in_frame(p) and not occluded(p):
            ok.append(deg)
    runs = []
    for d in ok:
        if runs and d == runs[-1][1] + 1:
            runs[-1][1] = d
        else:
            runs.append([d, d])
    # 首尾相接的绕圈情形合并
    if len(runs) > 1 and runs[0][0] == 0 and runs[-1][1] == 359:
        runs[0][0] = runs[-1][0] - 360
        runs.pop()
    return runs


def candidates(orbit_f, lo_phase=0.35, hi_phase=0.68, left_only=True):
    out = []
    for deg in range(0, 360, 1):
        p = moon_pos(deg, orbit_f)
        v = viewport(p)
        if v is None:
            continue
        x, y, z = v
        mrad = math.degrees(math.atan(MOON_R / z))
        mx = mrad / (FOV_V * 0.5) * 0.5                 # viewport 单位的月亮半径
        if not (mx * 1.5 < x < 1 - mx * 1.5 and mx * 1.5 < y < 1 - mx * 1.5):
            continue
        oa = ang(sub(p, CAM), FWD)
        if occluded(p):
            continue
        if oa < PLANET_ANG + mrad + 2.0:                # 别被蓝星圆面压住
            continue
        lit = moon_lit(p)
        if not (lo_phase <= lit <= hi_phase):
            continue
        if left_only and x >= 0.5:
            continue
        out.append((deg, x, y, lit, oa, mrad, z))
    return out


def main():
    print("== 自检：必须与已渲出的那张播放器图对得上 ==")
    print(f"相机位置            {tuple(round(v, 1) for v in CAM)}")
    print(f"太阳屏上偏角        {ang(sub(SUN_POS, CAM), FWD):.1f}°   （脚本注释记 36.8°）")
    sv = viewport(SUN_POS)
    spx = to_px(sv)
    print(f"太阳 viewport       ({sv[0]:.3f}, {sv[1]:.3f}) → px ({spx[0]:.0f}, {spx[1]:.0f})")
    print(f"    实测截图里那个白盘在 px ≈ (2176, 687)，即 viewport (0.85, 0.50)  ← 对上了")
    print(f"蓝星视半径          {PLANET_ANG:.1f}°   （注释记 19.5°）")
    print(f"日盘视半径          {math.degrees(math.atan(SUN_RADIUS_F * R / math.sqrt(dot(sub(SUN_POS, CAM), sub(SUN_POS, CAM))))):.2f}°")
    print()

    # θ=0 就是当前默认值 —— 说明它为什么看不见
    p0 = moon_pos(0.0, 2.6)
    print(f"当前默认 θ=0：离视轴 {ang(sub(p0, CAM), FWD):.1f}°，"
          f"竖直半视锥 {FOV_V/2:.0f}°、水平半视锥 {math.degrees(math.atan(TANV*ASPECT)):.1f}° → "
          f"{'在画面里' if in_frame(p0) else '**在画面外**'}")
    print()

    for orbit_f in (2.6, 2.2, 1.9):
        runs = visible_intervals(orbit_f)
        cover = sum(b - a + 1 for a, b in runs)
        print(f"══ 月轨 {orbit_f}R ══  全周 360° 里 {cover}° 在画面内且不被挡（{cover/3.6:.0f}%）。"
              f"可见区间（θ 增大 = 时间前进）：")
        for a, b in runs:
            pa, pb = moon_pos(a, orbit_f), moon_pos(b, orbit_f)
            print(f"     θ∈[{a:4d}°, {b:4d}°]  月相 {moon_lit(pa):.2f}→{moon_lit(pb):.2f}"
                  f"   屏上 x {to_px(viewport(pa))[0]:.0f}→{to_px(viewport(pb))[0]:.0f}px")
        print(f"   月亮角速度 0.2197°/s（恒星月 27.32 日 × 60 s/日）"
              f" → 最长一段可见 {(max(b-a+1 for a,b in runs))/0.2197:.0f} s")

    print()
    print("══ 月轨半径扫描：多大才既看得见、又不至于贴到地面上 ══")
    print("   （月亮半径 = 0.27R = 270 单位；orbital 1.8R 时月面离地只有 0.53R，已经很挤）")
    print("   orbit   可见占比   最长可见段   θ=0 时在画面里?   月面离地(半径 R 的倍数)")
    for i in range(14, 33, 2):
        orbit_f = i / 10.0
        runs = visible_intervals(orbit_f)
        cover = sum(b - a + 1 for a, b in runs)
        longest = max(b - a + 1 for a, b in runs)
        p0ok = in_frame(moon_pos(0.0, orbit_f)) is not None and not occluded(moon_pos(0.0, orbit_f))
        gap = orbit_f - 1.0 - MOON_RADIUS_F
        print(f"   {orbit_f:.1f}R    {cover/3.6:5.0f}%      {longest:5.0f}°"
              f"      {'是' if p0ok else '否':<10}      {gap:+.2f}R")
    print()
    for orbit_f in (2.6, 2.2, 1.9):
        rows = candidates(orbit_f)
        print(f"══ 月轨 {orbit_f}R ══  月相可辨(0.35~0.68)且偏画面左侧的：{len(rows)} 个")
        scored = sorted(rows, key=lambda r: (abs(r[3] - 0.5), -sun_screen_gap_px(to_px((r[1], r[2], r[6])))))
        for deg, x, y, lit, oa, mrad, z in scored[:8]:
            px = to_px((x, y, z))
            print(f"   θ={deg:3d}°  px=({px[0]:5.0f},{px[1]:5.0f})  vp=({x:.2f},{y:.2f})"
                  f"  月相={lit:.2f}  离视轴={oa:4.1f}°  月视半径={mrad:.1f}°"
                  f"  距={z:.0f}  离日盘={sun_screen_gap_px(px):.0f}px")
        print()


if __name__ == "__main__":
    main()
