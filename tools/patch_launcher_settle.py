# -*- coding: utf-8 -*-
"""给 启动.cmd 补两项：重建聚落场景 / 渲染聚落演化图（M4b-3）。

⚠️ 本文件遵守已登记的两条硬约束：
  1. .cmd 必须是 ANSI(936) + CRLF，且绝不能加 chcp（见 memory: windows-cmd-ansi-encoding）;
  2. **先编码成 bytes，成功了再开文件写** —— 反面教材见 design.md v0.21 ⑧：
     直接 `io.open(P,'w',encoding='gbk')` 会在编码失败前就把文件截成 0 字节。
"""
import io

P = '启动.cmd'
s = io.open(P, encoding='gbk', newline='').read()
lines = s.split('\r\n')

# ── 1. 菜单：插在 [14] 那一行之后 ──────────────────────────────
NEW_MENU = [
    'echo    [15] 重建聚落场景                            约 3 分钟',
    'echo    [16] 渲染聚落演化对比图  unity_settlement_*.png 约 5 分钟',
]

# ── 2. 分派：插在 [14] 那一行之后 ──────────────────────────────
NEW_DISPATCH = [
    'if "%CH%"=="15" goto do_settle_scene',
    'if "%CH%"=="16" goto do_settle_eras',
]

# ── 3. 处理块：插在 :do_unity_open 之前 ────────────────────────
NEW_HANDLERS = [
    ':do_settle_scene',
    'call :unity_run SettlementSceneBuilder.BuildBatch "重建聚落场景"',
    'call :pause_back',
    'goto menu',
    '',
    ':do_settle_eras',
    'rem  一次出四张：同一部落的游群／村落／铁器／工业。',
    'rem  年份是按该部落自己的技术门槛挑的，不是随手取的 —— 见 SettlementSceneBuilder 的注释。',
    'call :unity_run SettlementSceneBuilder.RenderErasBatch "渲染聚落演化图"',
    'call :pause_back',
    'goto menu',
    '',
]


def insert_after(lines, needle, new, label):
    """在唯一一行含 needle 的行之后插入 new。命中数必须为 1。"""
    hits = [i for i, ln in enumerate(lines) if needle in ln]
    assert len(hits) == 1, '%s：锚点命中 %d 次' % (label, len(hits))
    return lines[:hits[0] + 1] + new + lines[hits[0] + 1:]


lines = insert_after(lines, '[14] 打开 Unity 编辑器', NEW_MENU, '菜单')
lines = insert_after(lines, '"14" goto do_unity_open', NEW_DISPATCH, '分派')

hits = [i for i, ln in enumerate(lines) if ln.strip() == ':do_unity_open']
assert len(hits) == 1, '处理块：:do_unity_open 命中 %d 次' % len(hits)
lines = lines[:hits[0]] + NEW_HANDLERS + lines[hits[0]:]

out = '\r\n'.join(lines)

# ⚠️ 先编码，成功了再落盘 —— 编码失败时文件必须原封不动。
data = out.encode('gbk')          # 失败会抛出，且此时还没碰文件
io.open(P, 'wb').write(data)

# 自检：读回来逐项确认，不靠"写完了应该没事"
back = io.open(P, encoding='gbk', newline='').read()
for probe in ['[15] 重建聚落场景', '[16] 渲染聚落演化对比图',
              '"15" goto do_settle_scene', '"16" goto do_settle_eras',
              ':do_settle_scene', ':do_settle_eras',
              'SettlementSceneBuilder.RenderErasBatch']:
    assert probe in back, '回读校验失败：缺 %r' % probe
# ⚠️ 只查**真的命令行**里的 chcp。本文件顶部有一段 rem 专门解释"为什么不能加 chcp"，
#    里面满篇都是这个词 —— 第一次写成 `b'chcp' not in data`，被自己的注释绊倒。
bad = [ln for ln in out.split('\r\n')
       if 'chcp' in ln.lower() and not ln.strip().lower().startswith('rem')]
assert not bad, '实际命令行里不能出现 chcp：%r' % bad
assert b'\n' in data and data.count(b'\r\n') == out.count('\n'), '换行必须是 CRLF'
print('ok  %d bytes，菜单/分派/处理块三处均已写入并回读校验' % len(data))
