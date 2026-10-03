# -*- coding: utf-8 -*-
"""给 启动.cmd 补一项：渲染时间轴进化图（M4c 时间轴取证）。

⚠️ 本文件遵守已登记的两条硬约束：
  1. .cmd 必须是 ANSI(936) + CRLF，且绝不能加 chcp（见 memory: windows-cmd-ansi-encoding）；
  2. **先编码成 bytes，成功了再开文件写** —— 反面教材见 design.md v0.21 ⑧：
     直接 `io.open(P,'w',encoding='gbk')` 会在编码失败前就把文件截成 0 字节。

⚠️ 锚点一律取 **ASCII 片段**（`[17]`、`"17" goto do_player`、`:do_unity_open`），
   不取中文 —— 这个脚本自己的源码是 UTF-8，而目标文件是 GBK，
   拿中文当锚点会写出一个"在源码里看着对、在文件里匹配不上"的匹配式。
"""
import io

P = '启动.cmd'
s = io.open(P, encoding='gbk', newline='').read()
lines = s.split('\r\n')

# ── 1. 菜单：插在 [17] 那一行之后 ──────────────────────────────
NEW_MENU = [
    'echo    [18] 渲染时间轴进化图   unity_tl_*.png 约 5 分钟',
]

# ── 2. 分派：插在 [17] 那一行之后 ──────────────────────────────
NEW_DISPATCH = [
    'if "%CH%"=="18" goto do_timeline_eras',
]

# ── 3. 处理块：插在 :do_unity_open 之前 ────────────────────────
NEW_HANDLERS = [
    ':do_timeline_eras',
    'rem  一次出 26 张：同一块大陆、同一个机位，只有年份不同 —— 时间轴取证。',
    'rem  两年份之间只有年份在变，故画面上变了的就是进化，没变的就是没做出来。',
    'call :unity_run TimelinePreviewRenderer.RenderBatch "渲染时间轴进化图"',
    'call :pause_back',
    'goto menu',
    '',
]


def insert_after(lines, needle, new, label):
    """在唯一一行含 needle 的行之后插入 new。命中数必须为 1。"""
    hits = [i for i, ln in enumerate(lines) if needle in ln]
    assert len(hits) == 1, '%s：锚点命中 %d 次' % (label, len(hits))
    return lines[:hits[0] + 1] + new + lines[hits[0] + 1:]


lines = insert_after(lines, '[17]', NEW_MENU, '菜单')
lines = insert_after(lines, '"17" goto do_player', NEW_DISPATCH, '分派')

hits = [i for i, ln in enumerate(lines) if ln.strip() == ':do_unity_open']
assert len(hits) == 1, '处理块：:do_unity_open 命中 %d 次' % len(hits)
lines = lines[:hits[0]] + NEW_HANDLERS + lines[hits[0]:]

out = '\r\n'.join(lines)

# ⚠️ 先编码，成功了再落盘 —— 编码失败时文件必须原封不动。
data = out.encode('gbk')          # 失败会抛出，且此时还没碰文件
io.open(P, 'wb').write(data)

# 自检：读回来逐项确认，不靠"写完了应该没事"
back = io.open(P, encoding='gbk', newline='').read()
for probe in ['[18] 渲染时间轴进化图', '"18" goto do_timeline_eras',
              ':do_timeline_eras', 'TimelinePreviewRenderer.RenderBatch']:
    assert probe in back, '回读校验失败：缺 %r' % probe
# ⚠️ 只查**真的命令行**里的 chcp。本文件顶部有一段 rem 专门解释"为什么不能加 chcp"，
#    里面满篇都是这个词 —— 第一次写成 `b'chcp' not in data`，被自己的注释绊倒。
bad = [ln for ln in out.split('\r\n')
       if 'chcp' in ln.lower() and not ln.strip().lower().startswith('rem')]
assert not bad, '实际命令行里不能出现 chcp：%r' % bad
assert data.count(b'\r\n') == out.count('\n'), '换行必须是 CRLF'
print('ok  %d bytes，菜单/分派/处理块三处均已写入并回读校验' % len(data))
