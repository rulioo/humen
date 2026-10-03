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

⚠️⚠️ v0.28 大修，两件事必须先看：
  1) **这台机位参数早就是过期的，一直在错。** 原来写死 YAW=-35 / PITCH=18 ——
     那是 v0.25 的机位。v0.26 把默认机位改成对准最大那块大陆之后，
     本脚本**再没被复核过**，于是它算出来的每一个屏上坐标都落在另一个机位上，
     而"实测"那一栏是当年的截图。两边的差被逐条打印出来，看着像"有误差"，
     其实**是两个不同的世界**。现在改成从场景文件读到的真值。
  2) **太阳不再有屏上坐标。** 它移到 1012R 后落在相机背后，project() 对它返回 None。
     所以本脚本不再预测"日盘在哪个像素"，改成报**离视轴角度**（出画仍有定义）。
     像素级的预言机锚点整体搬到**月盘**。

⚠️ 因此 MEAS 里的"实测"值全部是 v0.25 机位下的旧读数，**不可与现在的预想值相减**。
   留着是为了别再犯"拿旧截图当新证据"这个错，不是为了对账。
"""
import math

# ── 场景参数 —— 必须与 Planet.unity 的序列化值一致 ─────────────────────────
# ⚠️ 这三行以前是写死的旧值。凡是从场景来的东西，都要在场景改了之后回来改这里，
#    否则本脚本会稳定地给出"看着挺合理、但与产品无关"的一整套读数。
R = 1000.0                        # PlanetBootstrap.Radius
YAW, PITCH = 5.394943, -11.78983  # OrbitCamera（v0.26 起：对准最大那块大陆）
DIST_F = 3.0
FOV_V = 60.0
W, H = 1280, 720           # 取证图实际尺寸
SUN_DIST_F = 1012.0        # v0.28：= 2.6 × 389.17，见 moon_frame.py 的推导
SUN_RADIUS_F = 108.1
SUN_PHASE = 55.0
MOON_ORBIT_F = 2.6
MOON_RADIUS_F = 0.27
MOON_INC = 5.1
MOON_START = 334.0         # MoonStartAngleDeg（产品现值；原来是过期的 294）

# ── v0.25 那张取证图量出来的值（当时的 shoot_check.py 输出）────────────────
# ⚠️ **这是旧机位（YAW=-35 / PITCH=18）与旧太阳（1.8R）下的读数，不是现在的。**
#    原来它叫 MEAS、就地跟预想值相减打印，于是"差值"看着像误差、其实是两台相机。
#    改名的目的就是让这种事不能再安静地发生。要用它，先读上面那段 ⚠️⚠️。
MEAS_V25_OLD_POSE = dict(
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
    # ⚠️ 报**离视轴角**，不报像素。日盘在相机背后时 project() 返回 None，
    #    而角度在那时仍然有定义 —— 这是 v0.28 之后唯一能把太阳读出来的量。
    off_sun = vangle(vsub(sun_pos, CAM_POS), FWD)
    # ⚠️ 差的那不到 0.2deg **不是残差、不是误差**，是**太阳的视差角 ε**：
    #    相机-蓝星-太阳 是一个三角形，δ(离视轴) 与 ψ(相位角) 是其中两个内角，
    #    ε 是**太阳**那个角 ⟹ δ + ψ = 180 − ε。太阳在 1012R 时 ε≈0.14deg，无穷远才是 0。
    #    **能算出来的东西不要写成"残差"**；正弦定理 sin ε = d_cam·sin ψ / d_sun 可独立复核。
    print(f"  离视轴 {off_sun:.1f}deg（期望 180 - SunPhaseDeg({SUN_PHASE:.0f}) = {180.0 - SUN_PHASE:.1f}；"
          f"差的那点就是太阳视差角 ε，1012R 下约 0.14deg —— δ+ψ = 180 − ε，不是「残差」）")
    if pr is None:
        print("  屏上位置 **无定义 —— 日盘在相机背后**（v0.28 起这是默认态，不是故障）")
        print("  => 本脚本不再拿日盘做像素级对账；预言机锚点整体搬到**月盘**")
    else:
        a, b, off = projected_radius(sun_pos, sun_r)
        print(f"  预想屏上 ({pr[0]:.1f},{pr[1]:.1f}) z={pr[2]:.0f}  离视轴 {off:.1f}deg")
        print(f"  预想日盘 短半轴 {a:.1f} 长半轴 {b:.1f} px  (长/短={b/a:.3f})")

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
    if prm is None:
        print("  **月盘也不在相机前方** —— 这不正常，月亮只在 2.6R。查机位参数。")
        return
    am, bm, offm = projected_radius(moon_pos, moon_r)
    print(f"  预想屏上 ({prm[0]:.1f},{prm[1]:.1f}) z={prm[2]:.0f}  离视轴 {offm:.1f}deg")
    print(f"  预想月盘 短半轴 {am:.1f} 长半轴 {bm:.1f} px  长/短={bm/am:.3f}"
          f"（离轴球体必是椭圆，1/cos{offm:.1f}deg = {1/math.cos(math.radians(offm)):.3f}）")
    print(f"  ⬆ 这就是**新的预言机锚点**：以前这条挂在日盘上，日盘出画之后只能挂月盘 ——"
          f"而月盘同样是离轴球体，椭圆比照样验得动**相机模型本身**（含左手系）。")
    print(f"  球心-日心-月心夹角 theta={theta:.2f}deg -> 受照 {lit*100:.1f}%  月龄 {age:.1f} 日")
    print(f"  核对：moon_frame.py / OrbitForensics 给出的现值是 离视轴 27.15deg、屏上 (959, 385)"
          f"（左上角原点）—— 对得上就说明本脚本的机位与产品一致")
    print(f"  （旧的那一栏「实测 (974.0,269.0)」是 v0.25 机位下的，见 MEAS_V25_OLD_POSE 的 ⚠️，不要相减）")

    # ── 月面亮的那一边朝哪 ──────────────────────────────────────────
    # 月球被哪一支光照亮，决定了亮面朝向。这里把**两种做法**都算出来：
    #   A) 场景里只有一盏**平行光**，方向 _sunDir（球心→太阳）。
    #      平行光的含义是"太阳在无穷远"，光线处处同向。
    #   B) 月亮处真正看到的是**从月亮指向太阳**的方向 (sunPos − moonPos)。
    # 二者的夹角，就是这盏平行光在月亮处造成的照明方向误差。
    #
    # ⚠️ v0.27 及以前这个误差是 **74.1°**，而且**修不好** —— 因为日盘画在 1.8R，
    #    落在月亮轨道 2.6R 以内，那是尺度问题不是参数问题。design.md 把它记成
    #    "日盘出框 or 没有月相，二者必居其一"的待裁项。
    #    **v0.28 太阳移到 1012R 之后，这个误差塌成 ~0.15°**，那个二选一自动作废。
    #    所以下面这一节现在是**确认**（数应该很小），不再是"登记一个开着的缺陷"。
    def dir_angle_at(p, d):
        return screen_angle(p, vadd(p, vmul(vnorm(d), 1.0)))

    limb_parallel = dir_angle_at(moon_pos, sun_dir)              # A 现状
    limb_true = dir_angle_at(moon_pos, vsub(sun_pos, moon_pos))  # B 正确
    three_d_err = vangle(sun_dir, vsub(sun_pos, moon_pos))
    bound = math.degrees(math.atan(MOON_ORBIT_F / SUN_DIST_F))

    print("\n== 月面受光朝向（v0.28 起这是确认项，不是待修项）==")
    print(f"  A 平行光 _sunDir 的投影        {limb_parallel:+.2f} deg")
    print(f"  B 月->日 真实方向的投影        {limb_true:+.2f} deg")
    print(f"  A 与 B 在三维里差 {three_d_err:.3f} deg"
          f"（v0.27 是 74.1°；理论上界 atan({MOON_ORBIT_F}/{SUN_DIST_F:.0f}) = {bound:.3f}°）")
    print(f"  => {'通过：月亮亮面朝得到日盘，待裁 C 作废' if three_d_err < 0.5 else '**不对：日盘还压在近处**'}")

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
          f"   (B 才是真正的几何)")
    print(f"  ⚠️ A 与 B 的差在 v0.28 已经塌到看不见（见上一节）——所以 HUD 那个"
          f"「球心处夹角」的口径现在**不再是个错误**，它只是另一个口径而已。")

    # ── 星球受光面朝哪 ──────────────────────────────────────────────
    sub_solar = vmul(sun_dir, R)          # 球面上的日下点
    expect_lit = screen_angle(TARGET, sub_solar)
    print("\n== 星球受光面 ==")
    print(f"  日下点方位（预想）{expect_lit:+.1f} deg")
    print(f"  （旧机位那张图上量到的是 {MEAS_V25_OLD_POSE['planet_lit_deg']:+.1f} deg —— "
          f"**不同机位，不要相减**）")
    print("  注：星球有海陆反照率，重心会被亮色陆地拉偏，这里只作粗核。")
    print(f"  蓝星亮度 cos(phi)：{math.cos(math.radians(vangle(sun_dir, vsub(CAM_POS, TARGET)))):+.3f}"
          f"  （phi = 球心处日-地-相机夹角 = {vangle(sun_dir, vsub(CAM_POS, TARGET)):.1f}°；"
          f"负值代表球心背光）")

    # ── 角径比：日食的几何是否为真 ────────────────────────────────────
    # ⚠️⚠️ **必须说清从哪儿量。** 角径比有两套口径，混用会得到一个看着像结论的数：
    #     · **从蓝星球心量**（两者都取 球心→天体 的距离）—— 这才是"日食成不成立"的口径，
    #       因为挡住太阳的是蓝星上的观测者。判据值 1.029 说的就是这一套。
    #     · 从相机量 —— 相机离月亮（4342）比离球心（3000）远，月盘因此被缩小，
    #       算出来是 1.93，**与 1.029 差了近一倍**。
    #    第一版这里就是把两套混着算的（日盘从相机、月盘……），
    #    正是本工程反复栽的那款「口径不同却并列摆着」。所以两套都打出来，各自标明。
    def ang_r_from(view, target_pos, radius_w):
        return math.degrees(math.atan(radius_w / vlen(vsub(target_pos, view))))

    sun_from_c = ang_r_from(TARGET, sun_pos, sun_r)
    moon_from_c = ang_r_from(TARGET, moon_pos, moon_r)
    sun_from_cam = ang_r_from(CAM_POS, sun_pos, sun_r)
    moon_from_cam = ang_r_from(CAM_POS, moon_pos, moon_r)

    print("\n== 角径比 ==")
    print(f"  从**蓝星球心**量（日食成不成立看这一套；判据是 ≈1.029，真实 1.0289）：")
    print(f"    日盘角半径 {sun_from_c:.2f}deg · 月盘角半径 {moon_from_c:.2f}deg"
          f"  -> 比 {sun_from_c/moon_from_c:.4f}"
          f"  {'通过' if abs(sun_from_c/moon_from_c - 1.029) < 0.01 else '**不对**'}")
    print(f"  从**相机**量（这才是画面上真正看到的；相机离月亮比离球心远，月盘偏小）：")
    print(f"    日盘角半径 {sun_from_cam:.2f}deg · 月盘角半径 {moon_from_cam:.2f}deg"
          f"  -> 比 {sun_from_cam/moon_from_cam:.4f}  （**不是 1.029，别拿这个当判据**）")


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
    # ⚠️ v0.28：日盘在相机背后，project() 返回 None，下面所有"离日盘"的项都没有定义。
    #    这不是要绕过的一处崩溃，是**判据本身没了** —— 所以整列一起撤掉，见 moon_frame.py。
    sun_px = project(sun_pos)
    if sun_px is None:
        print("⚠️ 日盘在相机背后：本扫描不再含「离日盘」判据（v0.28 它不在画面里）")
    else:
        sun_a, sun_b, _ = projected_radius(sun_pos, SUN_RADIUS_F * R)

    print("点光源模型下扫一圈（只列在画面内的）")
    print(" 角度    屏上x    屏上y   受照%   离球缘")
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
        # 球缘间距用**当前机位算出来的**蓝星视半径，不再用旧截图里量到的 220.0 ——
        # 那个数也是 v0.25 机位的。这里直接投影球面边缘点，与机位无关。
        edge = vmul(RIGHT, R)          # 与视轴垂直的一条半径（RIGHT 已在上面定好）
        pe = project(vadd(TARGET, edge))
        planet_r_px = math.hypot(pe[0] - W / 2.0, pe[1] - H / 2.0)
        gap_planet = math.hypot(pr[0] - W / 2.0, pr[1] - H / 2.0) - planet_r_px
        print(f"  {deg:3d}  {pr[0]:7.1f} {pr[1]:7.1f}  {lit*100:5.1f}%  {gap_planet:7.1f}")
        if 0.20 <= lit <= 0.75 and gap_planet > 60:
            best.append((deg, lit, gap_planet))

    print("\n候选（月相 20%~75%，离球缘 >60px；⚠️ 不含「离日盘」，理由见上）：")
    for deg, lit, gp in best:
        print(f"  θ={deg:3d}  受照 {lit*100:4.1f}%  离球缘 {gp:6.1f}px")


if __name__ == "__main__":
    import sys as _s
    if "--sweep" in _s.argv:
        sweep()
    else:
        main()
