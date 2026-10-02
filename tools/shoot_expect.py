# -*- coding: utf-8 -*-
"""
把 SunMoonSystem 的几何**照着代码复刻一遍**，算出每一件天体的屏上位置、
月相、受光朝向，再与 shoot_check.py 从取证图里量出来的数**逐条对**。

为什么不能反过来（先看图再编解释）—— 这个工程栽过一次：
图里那颗白盘被当成了月亮写进结论，其实是太阳。看图得到的只是"像什么"，
唯一能证伪的是把几何独立算一遍，看它落不落在同一个像素上。

复刻的三处，与 C# 一一对应：
  · BuildSunDirection()  —— 球心→相机 起，绕屏幕右方掰 SunPhaseDeg
  · BuildSun()           —— 日盘 = _sunDir * (SunDistanceFactor * R)
  · PlaceMoon()          —— 轨道面法线 (0,cos i,−sin i) 上取正交基 u、w
注意 Unity 是左手系：right = cross(up, forward)；Quaternion.Euler 作用次序 R = Ry·Rx·Rz。
"""
import math

# ── 场景参数（取自 PlanetSceneBuilder / SunMoonSystem 的字段初值）──────────
R = 1000.0                 # PlanetBootstrap.Radius
YAW, PITCH = -35.0, 18.0   # OrbitCamera
DIST_F = 3.0
FOV_V = 60.0
W, H = 1280, 720           # 取证图实际尺寸
SUN_DIST_F = 1.8
SUN_RADIUS_F = 0.20
SUN_PHASE = 55.0
MOON_ORBIT_F = 2.6
MOON_RADIUS_F = 0.27
MOON_INC = 5.1
MOON_START = 294.0         # MoonStartAngleDeg

# ── 从取证图量出来的值（shoot_check.py 的输出）────────────────────────────
MEAS = dict(
    sun_center=(1112.0, 359.5), sun_bbox=(159, 126),
    moon_center=(974.0, 269.0), moon_bbox=(91, 81),
    moon_limb_deg=-5.0,          # 月面受光重心方向（图内坐标，+x=0，+y 向下）
    sun_from_moon_deg=33.3,      # 月心→日心 的屏上方向（我原先拿来当基准的那个）
    planet_center=(639.5, 359.5), planet_r=220.0,
    planet_lit_deg=-6.2,
    phase_pct=28.0,              # HUD 自报
    age_days=4.8,
)


def vadd(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def vsub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def vmul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def vdot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def vcross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])
def vlen(a): return math.sqrt(vdot(a, a))
def vnorm(a):
    n = vlen(a)
    return (a[0] / n, a[1] / n, a[2] / n)


def rot_x(a):
    c, s = math.cos(a), math.sin(a)
    return lambda v: (v[0], c * v[1] - s * v[2], s * v[1] + c * v[2])


def rot_y(a):
    c, s = math.cos(a), math.sin(a)
    return lambda v: (c * v[0] + s * v[2], v[1], -s * v[0] + c * v[2])


def euler_vec(x_deg, y_deg, v):
    """Unity Quaternion.Euler(x,y,z) 作用在 v 上：R = Ry·Rx·Rz。"""
    return rot_y(math.radians(y_deg))(rot_x(math.radians(x_deg))(v))


def vangle(a, b):
    c = max(-1.0, min(1.0, vdot(vnorm(a), vnorm(b))))
    return math.degrees(math.acos(c))


# ── 相机 ────────────────────────────────────────────────────────────────
CAM_POS = vmul(euler_vec(PITCH, YAW, (0.0, 0.0, -1.0)), DIST_F * R)
TARGET = (0.0, 0.0, 0.0)
FWD = vnorm(vsub(TARGET, CAM_POS))
RIGHT = vnorm(vcross((0.0, 1.0, 0.0), FWD))     # 左手系
UP = vcross(FWD, RIGHT)
TAN_V = math.tan(math.radians(FOV_V / 2.0))
TAN_H = TAN_V * (W / float(H))


def project(p):
    """世界点 → 屏幕像素。返回 (px, py, 深度 z)。"""
    d = vsub(p, CAM_POS)
    z = vdot(d, FWD)
    if z <= 1e-6:
        return None
    nx = vdot(d, RIGHT) / (z * TAN_H)
    ny = vdot(d, UP) / (z * TAN_V)
    return ((nx + 1.0) * 0.5 * W, (1.0 - ny) * 0.5 * H, z)


def screen_angle(from_p, to_p):
    """两点投到屏上之后的方向角（度）。+x = 0°，+y（下） = +90°。"""
    a = project(from_p)
    b = project(to_p)
    return math.degrees(math.atan2(b[1] - a[1], b[0] - a[0]))


def projected_radius(center_w, radius_w):
    """球体投到屏上是**椭圆**：主轴沿背离画面中心的方向拉长 1/cos(偏角)。"""
    pr = project(center_w)
    px_per_unit = (H / 2.0) / (pr[2] * TAN_V)
    a = radius_w * px_per_unit                      # 垂直于视线的半轴
    off = math.atan(math.hypot(vdot(vsub(center_w, CAM_POS), RIGHT),
                               vdot(vsub(center_w, CAM_POS), UP))
                    / vdot(vsub(center_w, CAM_POS), FWD))
    return a, a / math.cos(off), math.degrees(off)


def main():
    print("== 相机 ==")
    print(f"  pos=({CAM_POS[0]:.1f},{CAM_POS[1]:.1f},{CAM_POS[2]:.1f}) |pos|={vlen(CAM_POS):.1f}")
    print(f"  tanV={TAN_V:.4f} tanH={TAN_H:.4f}  半视锥 竖{math.degrees(math.atan(TAN_V)):.1f}deg "
          f"横{math.degrees(math.atan(TAN_H)):.1f}deg")

    # ── 太阳方向（复刻 BuildSunDirection）────────────────────────────
    to_cam = vnorm(CAM_POS)
    right_s = vnorm(vcross((0.0, 1.0, 0.0), vmul(to_cam, -1.0)))
    rad = math.radians(SUN_PHASE)
    sun_dir = vnorm(vadd(vmul(to_cam, math.cos(rad)), vmul(right_s, math.sin(rad))))
    sun_pos = vmul(sun_dir, SUN_DIST_F * R)
    sun_r = SUN_RADIUS_F * R

    print("\n== 太阳 ==")
    pr = project(sun_pos)
    a, b, off = projected_radius(sun_pos, sun_r)
    print(f"  预想屏上 ({pr[0]:.1f},{pr[1]:.1f}) z={pr[2]:.0f}  离视轴 {off:.1f}deg")
    print(f"  预想日盘 短半轴 {a:.1f} 长半轴 {b:.1f} px  (长/短={b/a:.3f})")
    mc = MEAS["sun_center"]
    mb = MEAS["sun_bbox"]
    print(f"  实测屏上 ({mc[0]:.1f},{mc[1]:.1f})  误差 ({mc[0]-pr[0]:+.1f},{mc[1]-pr[1]:+.1f}) px")
    print(f"  实测日盘 {mb[0]}x{mb[1]} px  长/短={mb[0]/mb[1]:.3f}  "
          f"（球体离轴必是椭圆，1/cos{off:.1f}deg = {1/math.cos(math.radians(off)):.3f}）")

    # ── 月亮（复刻 PlaceMoon）────────────────────────────────────────
    inc = math.radians(MOON_INC)
    normal = (0.0, math.cos(inc), -math.sin(inc))
    u = vnorm(vcross(normal, (0.0, 1.0, 0.0)))
    w = vnorm(vcross(normal, u))
    ang = math.radians(MOON_START)
    moon_dir = vnorm(vadd(vmul(u, math.cos(ang)), vmul(w, math.sin(ang))))
    moon_pos = vmul(moon_dir, MOON_ORBIT_F * R)
    moon_r = MOON_RADIUS_F * R

    theta = vangle(sun_dir, moon_dir)          # 日心-球心-月心
    lit = (1.0 - math.cos(math.radians(theta))) * 0.5
    age = theta / 360.0 * 27.32

    print("\n== 月亮 ==")
    prm = project(moon_pos)
    am, bm, offm = projected_radius(moon_pos, moon_r)
    print(f"  预想屏上 ({prm[0]:.1f},{prm[1]:.1f}) z={prm[2]:.0f}  离视轴 {offm:.1f}deg")
    print(f"  预想月盘 短半轴 {am:.1f} 长半轴 {bm:.1f} px")
    mm = MEAS["moon_center"]
    print(f"  实测屏上 ({mm[0]:.1f},{mm[1]:.1f})  误差 ({mm[0]-prm[0]:+.1f},{mm[1]-prm[1]:+.1f}) px")
    print(f"  球心-日心-月心夹角 theta={theta:.2f}deg -> 受照 {lit*100:.1f}%  月龄 {age:.1f} 日")
    print(f"  实测（HUD 自报）      {MEAS['phase_pct']:.0f}%  月龄 {MEAS['age_days']:.1f} 日")

    # ── 月面亮的那一边朝哪 ──────────────────────────────────────────
    # 月球被哪一支光照亮，决定了亮面朝向。这里把**两种做法**都算出来：
    #   A) 现状：场景里只有一盏**平行光**，方向 _sunDir（球心→太阳）。
    #      平行光的意思是"太阳在无穷远"，光线处处同向。可太阳只在 1.8R，
    #      而月亮在 2.6R —— 在月亮那儿，真正的太阳方向与 _sunDir 差得很远。
    #   B) 正确：月亮处看到的是**从月亮指向太阳**的方向 (sunPos − moonPos)。
    # 二者的夹角就是这盏平行光在月亮处造成的照明方向误差。
    def dir_angle_at(p, d):
        return screen_angle(p, vadd(p, vmul(vnorm(d), 1.0)))

    limb_parallel = dir_angle_at(moon_pos, sun_dir)              # A 现状
    limb_true = dir_angle_at(moon_pos, vsub(sun_pos, moon_pos))  # B 正确
    three_d_err = vangle(sun_dir, vsub(sun_pos, moon_pos))

    print("\n== 月面受光朝向 ==")
    print(f"  月心->日心 屏上连线           {screen_angle(moon_pos, sun_pos):+.1f} deg")
    print(f"  A 平行光 _sunDir 的投影（现状）{limb_parallel:+.1f} deg")
    print(f"  B 月->日 真实方向的投影（应然）{limb_true:+.1f} deg")
    print(f"  A 与 B 在三维里差 {three_d_err:.1f} deg  <- 平行光在月亮处的照明方向误差")
    print(f"  实测月面受光重心               {MEAS['moon_limb_deg']:+.1f} deg")
    print(f"  实测 vs A 差 {MEAS['moon_limb_deg'] - limb_parallel:+.1f} deg"
          f"（小 -> 画面确实是被平行光照的）")
    print(f"  实测 vs B 差 {MEAS['moon_limb_deg'] - limb_true:+.1f} deg"
          f"（这才是修好之后应该变成的值）")

    # 月相也一样：HUD 用的是"球心处"的夹角，隐含太阳在无穷远。
    # 另外注意：**看到的月相**由「月→太阳」与「月→相机」的夹角决定，
    # 不是「月→蓝星」的夹角 —— 观测者在相机那儿，不在球心。
    def lit_from(light_dir_from_moon):
        t = vangle(light_dir_from_moon, vsub(CAM_POS, moon_pos))
        return (1.0 + math.cos(math.radians(t))) / 2.0

    theta_at_planet = vangle(sun_dir, moon_dir)
    print("\n== 月相：三种口径 ==")
    print(f"  球心处夹角（日-地-月）{theta_at_planet:.2f}deg -> "
          f"教科书式 (1-cos)/2 = {(1-math.cos(math.radians(theta_at_planet)))/2*100:.1f}%  <- HUD 现用")
    print(f"  A 平行光下、相机看到的    {lit_from(sun_dir)*100:.1f}%"
          f"   (A 就是现状，应接近实测)")
    print(f"  B 点光源下、相机看到的    {lit_from(vsub(sun_pos, moon_pos))*100:.1f}%"
          f"   (B 是修好之后会变成的样子)")
    print(f"  实测（月盘半高阈值粗测）  {MEAS['phase_pct']:.0f}%  <- 只作粗核，纹理有明暗")

    # ── 星球受光面朝哪 ──────────────────────────────────────────────
    sub_solar = vmul(sun_dir, R)          # 球面上的日下点
    expect_lit = screen_angle(TARGET, sub_solar)
    print("\n== 星球受光面 ==")
    print(f"  日下点方位（预想）{expect_lit:+.1f} deg")
    print(f"  实测受光重心      {MEAS['planet_lit_deg']:+.1f} deg  "
          f"差 {MEAS['planet_lit_deg'] - expect_lit:+.1f} deg")
    print("  注：星球有海陆反照率，重心会被亮色陆地拉偏，这里只作粗核。")


def sweep():
    """
    换照明模型之后，开场相位必须**重新解**一遍。
    原来 θ=294 是在"平行光"下挑的；改成"太阳处点光源"之后，
    同一角度的月相完全变了（99% 满月 —— 正是当初特意避开的那种"白盘"）。
    这里把一圈扫一遍，只留**在画面里**的，并把月相、与日盘/球缘的间隙一并列出。
    """
    inc = math.radians(MOON_INC)
    normal = (0.0, math.cos(inc), -math.sin(inc))
    u = vnorm(vcross(normal, (0.0, 1.0, 0.0)))
    w = vnorm(vcross(normal, u))

    to_cam = vnorm(CAM_POS)
    right_s = vnorm(vcross((0.0, 1.0, 0.0), vmul(to_cam, -1.0)))
    rad = math.radians(SUN_PHASE)
    sun_dir = vnorm(vadd(vmul(to_cam, math.cos(rad)), vmul(right_s, math.sin(rad))))
    sun_pos = vmul(sun_dir, SUN_DIST_F * R)
    sun_px = project(sun_pos)
    sun_a, sun_b, _ = projected_radius(sun_pos, SUN_RADIUS_F * R)

    print("点光源模型下扫一圈（只列在画面内的）")
    print(" 角度    屏上x    屏上y   受照%   离日盘   离球缘")
    best = []
    for deg in range(0, 360, 2):
        a = math.radians(deg)
        md = vnorm(vadd(vmul(u, math.cos(a)), vmul(w, math.sin(a))))
        mp = vmul(md, MOON_ORBIT_F * R)
        pr = project(mp)
        if pr is None:
            continue
        if not (0.05 * W < pr[0] < 0.95 * W and 0.05 * H < pr[1] < 0.95 * H):
            continue
        lit = (1.0 + math.cos(math.radians(
            vangle(vsub(sun_pos, mp), vsub(CAM_POS, mp))))) / 2.0
        gap_sun = math.hypot(pr[0] - sun_px[0], pr[1] - sun_px[1]) - sun_b - MOON_RADIUS_F * R * 0.3
        gap_planet = math.hypot(pr[0] - MEAS["planet_center"][0],
                                pr[1] - MEAS["planet_center"][1]) - MEAS["planet_r"]
        print(f"  {deg:3d}  {pr[0]:7.1f} {pr[1]:7.1f}  {lit*100:5.1f}%  "
              f"{gap_sun:7.1f}  {gap_planet:7.1f}")
        if 0.20 <= lit <= 0.75 and gap_sun > 40 and gap_planet > 60:
            best.append((deg, lit, gap_sun, gap_planet))

    print("\n候选（月相 20%~75%，离日盘 >40px，离球缘 >60px）：")
    for deg, lit, gs, gp in best:
        print(f"  θ={deg:3d}  受照 {lit*100:4.1f}%  离日盘 {gs:6.1f}px  离球缘 {gp:6.1f}px")


if __name__ == "__main__":
    import sys as _s
    if "--sweep" in _s.argv:
        sweep()
    else:
        main()
