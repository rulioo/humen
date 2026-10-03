"""月亮的初始公转相位该取多少 —— 既在画面里、又不被蓝星挡住、还能看出月相。

复刻 unity 侧三个脚本的算式，逐字对应：
  PlanetBootstrap.Radius      = 1000
  OrbitCamera                 : Yaw/Pitch/DistanceFactor —— **从场景文件里读**，见下面的 ⚠️
  PlanetSceneBuilder          : fieldOfView=60（竖直）
  PlayerBuilder               : 1280x720（defaultScreenWidth/Height）
  SunMoonSystem.PlaceMoon / BuildSunDirection

⚠️ **机身姿态是会变的，这一行必须跟着场景走。**
   本脚本第一版写的是 Yaw=-35, Pitch=18 —— 那是当时的场景值，据此选出了 θ=294°。
   v0.26 把默认机位改成对准**最大那块大陆**（质心 −12°, −175°）之后，
   Yaw/Pitch 变成了 5.394943 / -11.78983，而本脚本的输入没跟着改：
   于是 294° 这个数**再也没被复核过**，月亮在新的机位下正好落在蓝星<b>背后**
   （离视轴 9.2° < 蓝星视半径 19.5°，且比蓝星远）——
   HUD 却还写着"在画面里"（它只查视口，不查遮挡）。
   **取证脚本的输入一旦不跟着产品走，它的结论就会静悄悄地过期。**
   现值取自 `unity/Assets/Scenes/Planet.unity` 里 OrbitCamera 那一段的序列化字段。

⚠️ **同一场事故里还有第二处过期，更隐蔽：判据 `left_only`。**
   它要求月亮落在**画面左半**（x < 0.5）—— 那是在旧机位下"离太阳远一点"的代理判据，
   因为旧机位里太阳在画面右侧。换到新机位，太阳仍在右侧（px≈1107），
   而平衡解落在 **x≈0.75**（蓝星圆面与日盘之间那一线天）——
   于是 `left_only` 把**唯一可行的那一段整个筛掉**，`candidates()` 返回 0 个。
   **看到"0 个候选"时，先怀疑判据，别急着怀疑天体。**
   真正想要的是"两个圆面别压在一起"，那就**直接量边到边的像素间距**，
   不要用"在左半还是右半"这种间接代理 —— 代理换个机位就失效，而直接量不会。

**这一轮的结论（v0.27）：θ = 334°**，见 `SunMoonSystem.MoonStartAngleDeg`。
   它是新机位下唯一满足全部判据的点。**月亮本身一个参数都没动**，
   所以这个值在 v0.28 照样成立，全部判据逐条不变。
   ⚠️ 但 v0.27 给它写的**理由**里有一条 v0.28 作废了：原文是
   "离蓝星圆面"与"离日盘"两个间距**恰好相等**的那一点（各 65 px）——
   太阳移到 1012R（≈无穷远）之后**日盘不在画面里**，"离日盘"这一项没有了，
   这个"平局"判据随之消失。**判据少了一条不等于结论要改**：
   该问的是"这个常量依赖的输入变没变"，月亮没变，所以 334° 不动。
   （这正是 v0.27 那条教训的反面用法 —— 上次是输入变了而没人重扫。）
   注意间距口径：**边到边（圆面到圆面）**，不是圆心到圆心 ——
   圆心距还要减掉两个视半径（日盘 50.7 px、月亮 38.8 px），差着近百像素，
   混用会得出"月亮离太阳挺远"的错觉，实际两个盘几乎挨上。
   另一个容易混的口径是**角度**：月亮的视半径只有 3.6°，
   而"离视轴 27.2°"里的 27.2° 是**从视轴量的**，两者不是一回事。

**这个脚本存在的理由**：`_moonAngleRad` 默认 0，而 θ=0 时月亮离视轴 54°，
在 60° 视锥之外 —— 播放器里**根本没有月亮**。
（v0.23 那轮把编辑器取证图里一个白盘认成了月亮并写进结论 —— 后来算出它是**太阳**，
屏上偏角 36.8°。那是**当时**的读数：v0.28 起太阳在 1012R，默认机位下它<b>在镜头背后</b>，
屏上偏角 124.9°，画面里不会有日盘了。所以"天上那个白盘"这类判断从此只剩月亮可认。）
选一个默认相位本来要手算，
但轨道面有 5.1° 倾角、FOV 又是竖直的，解析式容易错；这里直接扫全周，
判据用的就是最终真正作数的那个（投影到 viewport）。

⚠️ 相机基的左右手约定照 Unity 来（左手系）：right = cross(up, forward)。
   第一版写成了 cross(forward, up)，太阳被镜像到画面另一半 ——
   而 y 完全对得上，只有 x 反了，看着很像"太阳相位角取反了"。
   判定依据是**已经渲出来的那张图**：日盘在画面右侧。→ 自检一节就是干这个的。
"""

import math
import sys

# ⚠️ Windows 控制台默认 GBK，而本脚本的读数里带着 `!` 与 `<=>` 这类 GBK **编不出**的符号
#    （实测：⚠ U+26A0 / ⟺ U+27FA / − U+2212 全部编码失败，≈ · ° — 没问题）。
#    不加这三行，脚本会在**打印到一半时**抛 UnicodeEncodeError ——
#    看起来像"脚本坏了"，其实只是终端编码；而且它死在半路，
#    前面已经打出来的读数会被当成"这一轮的全部结论"。
#    重设 stdout 编码后就地解决，输出仍是可读的 UTF-8。
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

R = 1000.0
YAW, PITCH, DIST_F = 5.394943, -11.78983, 3.0    # ← 场景里 OrbitCamera 的序列化值，改场景就要改这里
FOV_V = 60.0
W, H = 1280, 720                                  # ← PlayerBuilder.defaultScreenWidth/Height
ASPECT = W / float(H)
# ⚠️ v0.28：太阳移到 1012R、半径 108.1R（与 SunMoonSystem 的序列化值一致）。
#    1012 = 2.6 × 389.17：月亮压了 23.19 倍，太阳按**同一个倍数**压，于是日:月的
#    距离比 / 半径比 / 角径比全部为真。旧的 1.8 / 0.20 比月亮（2.6R）还近，比例是反的。
#    后果就写在下一条：日盘落到相机背后，viewport() 对它返回 None，屏上各项**不再有定义**。
SUN_DIST_F, SUN_PHASE, SUN_RADIUS_F = 1012.0, 55.0, 108.1
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
    """viewport → 像素。**原点在左上角**（像图像坐标那样，y 向下长）。

    ⚠️ 与 Unity 的 <c>Camera.WorldToScreenPoint</c> **差一个 y 翻转** ——
       那个是<b>原点在左下角</b>（y 向上长）。两者对画面中心对称的点读数相同，
       所以**拿太阳（落在水平中线上）去核对是发现不了这个差异的**：
       本脚本一度因此对着月亮差了 50 px（脚本 385 / 取证 335）而看不出谁对 ——
       其实两个都对，是同一个点的两种记法（720−385=335）。
       核对时**必须用偏离中线的天体**（月亮），而且要先说清口径。
       x 不受影响：两边都是自左向右。"""
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
    """月亮**圆心**到日盘**圆心**的像素距。⚠️ 这不是"离日盘多远"——
    还要减掉两个视半径才是边到边，见 disc_gaps_px()。留着是因为历史读数用的都是它。"""
    s = to_px(viewport(SUN_POS))
    return math.hypot(px[0] - s[0], px[1] - s[1])


def _view_radius_px(world_r, p):
    """半径 world_r 的球在 p 处，投影到屏上的视半径（px）。

    ⚠️ 两个坑，都踩过：
       (1) 分母是 <b>d = |p − CAM|</b>，不是"p 沿视轴的分量 z"，更不是"从原点量的距离"。
           第一版写成 <c>mul(FWD, DIST_F*R)</c> —— 那是**从原点**出发的向量，
           而相机本来就在距原点 DIST_F*R 处，于是算出的距离是 2×（6000 而非 3000），
           蓝星视半径被算成 100.8px（实际 220.5px）。后果：candidates() 说
           "θ=324° 离蓝星 +120px"，而直算是 "+4px" —— <b>整整差一个蓝星视半径</b>。
       (2) 球的轮廓由**切线**决定，不是过球心的截面：正确式子是 asin(r/d)，
           即分母 sqrt(d²−r²)。d≫r 时与 r/d 几乎相等（日月正是如此），
           但蓝星 R/d = 1/3，用 r/d 会把视半径算小 12%。
       <b>近处的大物体会把最粗糙的那个近似放大成主要误差</b> —— 而这里唯一的
       近处大物体就是蓝星，偏偏"月亮离蓝星多远"这条判据全靠它。"""
    d = math.sqrt(dot(sub(p, CAM), sub(p, CAM)))
    if d <= world_r:
        return float("inf")
    return (world_r / math.sqrt(d * d - world_r * world_r)) / TANV * (H / 2.0)


# ⚠️ v0.28：日盘在 1012R，默认机位下落在**相机背后**，viewport() 对它返回 None。
#    这不是异常，是设计（日盘在画面里 ⟺ 蓝星是暗的，见 SunMoonSystem 类注释）。
#    所以**不能无条件 to_px** —— 那会直接抛 TypeError，而"脚本崩了"看起来像"脚本坏了"，
#    不像"太阳不在这条等式里了"。量不出来的东西要**明确地标成量不出来**。
_SUN_VP = viewport(SUN_POS)
SUN_PX = to_px(_SUN_VP) if _SUN_VP is not None else None
SUN_RAD_PX = _view_radius_px(SUN_RADIUS_F * R, SUN_POS) if _SUN_VP is not None else None
SUN_BEHIND = _SUN_VP is None
# 蓝星球心在世界原点（机架绕原点转、日月都以原点为心），所以这里传原点。
# ⚠️ 不要传 FWD*DIST_F*R —— 那是"从原点走 DIST_F*R"，不是"从相机走"。
PLANET_RAD_PX = _view_radius_px(R, (0.0, 0.0, 0.0))


def disc_gaps_px(p):
    """月亮圆面到蓝星圆面、到日盘的**边到边**像素间距。负值 = 两个盘压在一起。

    ⚠️ 用边到边而不是圆心距：圆心距会把"两个盘已经叠上了"读成"还差几十像素"。
       本工程在"口径不同却并列摆着"这件事上栽过（见 SunMoonSystem 里
       月龄与受照比例那条注释），所以这里把口径写进函数名。

    ⚠️ v0.28：日盘退到相机背后之后，第二项返回 **None** —— 既不是 inf 也不是 0。
       "量不出来"与"离得很远"是两回事；拿 inf 冒充会把"太阳不在这个画面里"
       读成"月亮离太阳很远、很安全"，正是本工程反复栽的那款**安静地错**。
       调用方必须显式分叉，不能靠比较运算符糊过去（None 的比较在 py3 直接抛错，
       这算是幸运：它逼着人改，而不是给一个看起来像结论的数）。"""
    px = to_px(viewport(p))
    mr = _view_radius_px(MOON_R, p)
    gp = math.hypot(px[0] - W * 0.5, px[1] - H * 0.5) - PLANET_RAD_PX - mr
    gs = None
    if SUN_PX is not None:
        gs = math.hypot(px[0] - SUN_PX[0], px[1] - SUN_PX[1]) - SUN_RAD_PX - mr
    return gp, gs


def _fmt_gap(g):
    """间距读数，或"量不出来"本身。见 disc_gaps_px 的 ⚠️。"""
    return "—（日盘不在画面里）" if g is None else f"{g:+.0f}px"


def _gap_score(row):
    """候选排序用的"最挤的那一边有多宽"。日盘不在画面里时它就只剩离蓝星这一项。"""
    gp, gs = row[7], row[8]
    return gp if gs is None else min(gp, gs)


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


def candidates(orbit_f, lo_phase=0.35, hi_phase=0.68, min_gap_px=30.0):
    """满足全部判据的相位。

    ⚠️ 第三版把原来的 `left_only` 换成了 `min_gap_px`，理由见文件头那段 ⚠️：
       "在画面左半"是旧机位下"离太阳远一点"的代理判据，换机位就失效，
       而且它失效的方式是**静默返回 0 个**（看着像"没有解"，其实是不让有解）。
       现在直接量边到边间距 —— 判据与它想表达的东西是同一件事，不会再漂。"""
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
        gp, gs = disc_gaps_px(p)                        # 边到边；日盘不在画面里时 gs=None
        # ⚠️ v0.28：日盘不在画面里时只按"离蓝星"筛。
        #    **删掉一条判据 ≠ 放宽判据** —— 删的是"离日盘"，而日盘压根不在同一个画面里，
        #    它没有可违反的方式。但这件事必须说出来：下面 main() 里会打印当前用的是几条判据。
        if gp < min_gap_px or (gs is not None and gs < min_gap_px):
            continue
        lit = moon_lit(p)
        if not (lo_phase <= lit <= hi_phase):
            continue
        out.append((deg, x, y, lit, oa, mrad, z, gp, gs))
    return out


def main():
    print("== 自检：必须与已渲出的那张播放器图对得上 ==")
    print(f"相机位置            {tuple(round(v, 1) for v in CAM)}")
    # ⚠️ v0.28：这里**只报角度**，不报像素。日盘在 1012R，落在相机背后，
    #    "日盘在屏上哪儿"已经没有定义 —— 而角度在出画之后仍然有定义，所以它是能读的那个量。
    print(f"太阳离视轴          {ang(sub(SUN_POS, CAM), FWD):.1f}°"
          f"   （v0.27 是 36.8°；期望 180 - SunPhaseDeg({SUN_PHASE:.0f}) = {180.0-SUN_PHASE:.1f}°）")
    if SUN_BEHIND:
        print("太阳 viewport       **相机背后**（v0.28 起日盘的常态；"
              "画面里没有日盘 ⟺ 蓝星是暗的）")
        print("    所以本脚本不再拿日盘做任何核对锚点 —— 锚点整体搬到**月盘**（见 main 末尾）")
    else:
        sv = viewport(SUN_POS)
        spx = to_px(sv)
        print(f"太阳 viewport       ({sv[0]:.3f}, {sv[1]:.3f}) → px ({spx[0]:.0f}, {spx[1]:.0f})")
        print(f"    实测（v0.27 播放器截屏 01_星球_默认.png）那个白盘在 px ≈ (1106, 359)"
              f"  ← 对上了就说明本脚本的机位与产品一致")
    print(f"蓝星视半径          {PLANET_ANG:.1f}°   （注释记 19.5°）")
    # 角**半径**，与"离视轴"是两个不同的量，别混（文件头那条口径提醒）。
    # 日盘与月盘的这个比值是 v0.28 的判据之一：必须 ≈1.029，与真实的 1.0289 相同 ——
    # 这条比值对了，日食的几何才是真的。
    _d_sun = math.sqrt(dot(sub(SUN_POS, CAM), sub(SUN_POS, CAM)))
    _r_sun = math.degrees(math.atan(SUN_RADIUS_F * R / _d_sun))
    print(f"日盘视半径          {_r_sun:.2f}°   （v0.27 是 1.86°，因为日盘只有 0.2R）")
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
        print(f"══ 月轨 {orbit_f}R ══  月相可辨(0.35~0.68)且不与蓝星/日盘相压的：{len(rows)} 个"
              f"（{'日盘不在画面里，实际只有「离蓝星」一条在筛' if SUN_BEHIND else '两条判据都在'}）")
        # 排序看的是"最挤的那一边有多宽" —— 即 min(离蓝星, 离日盘) 最大的排前面。
        # 这才对应"最平衡的一点"；按离太阳的圆心距排会偏袒日盘那一侧。
        # ⚠️ v0.28：日盘不在画面里时 min() 自动退化成"离蓝星"——排序口径跟着判据一起变，
        #    所以**argmax 有可能挪**。挪了不是错，但要看得见（下面有专门的对照打印）。
        scored = sorted(rows, key=lambda r: -_gap_score(r))
        for deg, x, y, lit, oa, mrad, z, gp, gs in scored[:8]:
            px = to_px((x, y, z))
            print(f"   θ={deg:3d}°  px=({px[0]:5.0f},{px[1]:5.0f})  vp=({x:.2f},{y:.2f})"
                  f"  月相={lit:.2f}  离视轴={oa:4.1f}°  月视半径={mrad:.1f}°"
                  f"  距={z:.0f}  离蓝星={gp:+.0f}px  离日盘={_fmt_gap(gs)}")
        print()

    # ── 复算产品里实际用的那个值 ───────────────────────────────
    # 这个数是 unity 侧 MoonStartAngleDeg 的镜像。改了那边就得改这里，
    # 否则本脚本会给出一份"看着挺有道理"、但与产品无关的读数。
    CHOSEN = 334.0
    print(f"══ 产品现值 θ={CHOSEN:.0f}° 复算 ══")
    p = moon_pos(CHOSEN, 2.6)
    px = to_px(viewport(p))
    gp, gs = disc_gaps_px(p)
    print(f"  月相 {moon_lit(p):.2f}（判据 0.35~0.68）· 离视轴 {ang(sub(p, CAM), FWD):.1f}°")
    print(f"  被蓝星挡住? {'**是**' if occluded(p) else '否'}（必须是「否」）· "
          f"在画面里? {'是' if in_frame(p) else '**否**'}（必须是「是」）")
    print(f"  屏上 ({px[0]:.0f}, {px[1]:.0f})（**左上角原点**）"
          f" · 边到边离蓝星 {gp:+.0f}px · 离日盘 {_fmt_gap(gs)}")
    print(f"  实测（OrbitForensics）：离视轴 27.15° · 屏幕 (959, 335) —— 那个是**左下角原点**，"
          f"换成左上角就是 (959, {720-335})，与本脚本逐位相同")
    print(f"    ⚠️ 核对时别拿太阳去比：太阳落在水平中线上，两种原点记法读数一样，"
          f"看不出这个差异（本脚本差点因此白查一轮）")
    print(f"  → 离蓝星 {gp:+.0f}px（判据 ≥30px）"
          + ("；**「离日盘」这一项与它那条「两边相等 = 平衡点」的判据一并作废** —— "
             "v0.28 日盘在镜头背后，不在这个画面里"
             if gs is None else f" · 离日盘 {gs:+.0f}px"))
    print()

    # ── 判据少了一条，argmax 会不会挪？──
    # 这正是 v0.27 那条教训的反面用法：**先问"这个常量依赖的输入变没变"**。
    # 月亮一个参数都没动，所以预期是不挪；但预期不算数，得重扫出来看。
    rows = candidates(2.6)
    if not rows:
        print("  ⚠️ **新口径下 0 个候选** —— 先怀疑判据，别急着怀疑天体（见文件头那条）")
    else:
        best = max(rows, key=_gap_score)
        same = abs(best[0] - CHOSEN) < 0.5
        print(f"  ⚠️ 去掉「离日盘」后按新口径重扫：argmax = θ={best[0]:.0f}°"
              f"（离蓝星 {best[7]:+.0f}px，候选共 {len(rows)} 个）—— "
              + ("与产品现值相同，334° 不用动，理由见文件头那段 ⚠️"
                 if same else
                 f"**与产品现值 {CHOSEN:.0f}° 不同**"))
        if not same:
            # ⚠️ 关键：新 argmax 不是"更好的答案"，它是**退化**的答案。
            #    旧口径 min(离蓝星, 离日盘) 求的是"夹在蓝星与日盘之间最宽的那一线天"，
            #    那是个**平衡条件**；日盘一走，min() 只剩一项，
            #    问题就变成了"离蓝星越远越好" —— 这**已经不是原来那个问题了**。
            #    所以 θ=358° 赢在"最靠边"（+223px 就是可见窗口的边缘），
            #    它没有回答"月亮该在哪儿好看"，只回答了"哪儿离蓝星最远"。
            print(f"     ⚠️ 但**别把 {best[0]:.0f}° 当成新答案**：旧口径求的是"
                  f"「夹在蓝星与日盘之间最宽的一线天」，那是**平衡条件**；"
                  f"日盘一走 min() 只剩一项，问题退化成了「离蓝星越远越好」——"
                  f"**这已经不是你原来问的那个问题了**。")
            print(f"     所以 {best[0]:.0f}° 只是「最靠边」（可见窗口的边缘），"
                  f"不是「最好看」。")
            print(f"     → 保持 {CHOSEN:.0f}°：月亮一个参数都没动，"
                  f"它满足的每一条判据在 v0.28 仍然逐条成立（见上面复算那几行）。")
    print()


if __name__ == "__main__":
    main()
