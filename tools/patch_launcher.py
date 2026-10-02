# -*- coding: utf-8 -*-
"""
把 Unity 相关菜单项补进 启动.cmd。

[注意]️ 启动.cmd 是 ANSI(936) + CRLF 的批处理文件，且**不能加 chcp**（见记忆
   windows-cmd-ansi-encoding）。而 Edit/Write 工具只会写 UTF-8，
   所以这个文件必须绕道 python 做一次编码转换 —— 这就是它存在的原因。
   改完记得肉眼确认一遍菜单能正常显示，中文乱码只会在运行时才看得出来。
"""
import io
import sys

P = '启动.cmd'
B = '\\'   # 反斜杠。单独拎出来，免得在普通字符串里被当成转义起始

# 读进来先把 CRLF 归一成 LF 再匹配 —— 文件是 CRLF，而下面锚点写的是 '\n'，
# 不归一的话每个锚点都会"数到 0 次"，看着像锚点写错了，其实是行尾的事。
s = io.open(P, encoding='gbk', newline='').read().replace('\r\n', '\n')

# ── 1. Unity 路径变量 ────────────────────────────────────────────
# 用三行做锚点：`set "EMPTY=0"` 单独出现两次（一处是变量初始化，一处是防呆计数重置）。
anchor = 'set "ROOT=%~dp0"\nset "EXE=%~dp0bin' + B + 'humen.exe"\nset "EMPTY=0"\n'
assert s.count(anchor) == 1, '锚点 ROOT/EXE/EMPTY 出现 %d 次' % s.count(anchor)
s = s.replace(anchor, anchor + '\n'
    'rem  Unity 编辑器的绝对路径。装在别的盘就改这一行。\n'
    'rem  [注意] 是 <安装目录>' + B + 'Editor' + B + 'Unity.exe —— 不是 <安装目录>' + B + 'Unity.exe，\n'
    'rem     少了中间那层 Editor 目录的话，下面所有 Unity 项都会报"找不到文件"。\n'
    'set "UNITY=G:' + B + 'Unity' + B + 'Editors' + B + '6000.0.30f1' + B + 'Editor' + B + 'Unity.exe"\n', 1)

# ── 2. 菜单显示 ──────────────────────────────────────────────────
old_menu = 'echo    [0]  退出\n'
assert s.count(old_menu) == 1, '锚点 [0] 出现 %d 次' % s.count(old_menu)
s = s.replace(old_menu,
    'echo    --------------------------------------------------------\n'
    'echo    [10] 跑完整演化（30 万年） world.db          约 3 分钟\n'
    'echo    [11] 导出世界快照给 Unity  StreamingAssets   约 2 秒\n'
    'echo    [12] 重建 Unity 星球场景                     约 3 分钟\n'
    'echo    [13] 渲染 Unity 预览图     unity_preview.png 约 3 分钟\n'
    'echo    [14] 打开 Unity 编辑器（可自由飞行）\n'
    'echo    --------------------------------------------------------\n'
    + old_menu, 1)

# ── 3. 分发 ─────────────────────────────────────────────────────
old_disp = 'if "%CH%"=="0" goto done\n'
assert s.count(old_disp) == 1, '锚点 dispatch 出现 %d 次' % s.count(old_disp)
s = s.replace(old_disp,
    'if "%CH%"=="10" goto do_evolve\n'
    'if "%CH%"=="11" goto do_export\n'
    'if "%CH%"=="12" goto do_unity_scene\n'
    'if "%CH%"=="13" goto do_unity_preview\n'
    'if "%CH%"=="14" goto do_unity_open\n'
    + old_disp, 1)

# ── 4. 动作 ─────────────────────────────────────────────────────
old_open = ':do_open\n'
assert s.count(old_open) == 1, '锚点 :do_open 出现 %d 次' % s.count(old_open)
s = s.replace(old_open,
''':do_evolve
call :ensure_exe
if errorlevel 1 goto menu
echo.
echo   跑完整演化：100 个创世部落从公元前 30 万年走到公元 2126 年。
echo   部落会分裂、迁徙、发明、失传。
echo.
echo   约 3 分钟。注意：会重写 world.db 里的演化结果
echo   （地理骨架不变，同一个 seed 下逐位相同）。
echo.
pushd "%~dp0."
"%EXE%" evolve --no-pause
popd
call :show_file world.db
call :pause_back
goto menu

:do_export
call :ensure_exe
if errorlevel 1 goto menu
echo.
echo   把 world.db 的演化结果导成 Unity 能读的世界快照：
echo     unity''' + B + 'Assets' + B + 'StreamingAssets' + B + 'World' + B + '''world_view.json
echo.
echo   Unity 侧不引用 Humen.Core（版本对不上），两边只能靠数据文件通信，
echo   这一步就是那座桥。没有它，三维星球上就只有地形、没有人。
echo.
pushd "%~dp0."
"%EXE%" export-world --no-pause
popd
call :pause_back
goto menu

:do_unity_scene
call :unity_run PlanetSceneBuilder.BuildBatch "重建星球场景"
call :pause_back
goto menu

:do_unity_preview
call :unity_run PreviewRenderer.RenderBatch "渲染预览图"
call :pause_back
goto menu

:do_unity_open
if not exist "%UNITY%" (
    echo.
    echo   找不到 Unity：%UNITY%
    echo   装了别的版本就改本文件顶部的 UNITY 变量。
    call :pause_back
    goto menu
)
echo.
echo   正在打开 Unity 编辑器（首次打开工程要几分钟建库）...
pushd "%~dp0."
start "" "%UNITY%" -projectPath "%~dp0unity"
popd
echo.
echo   打开后按 Play 就能看见活的世界：
echo     鼠标左键拖拽转视角 · 滚轮缩放 · 空格暂停时间
echo     左右方向键拖动年份 · Home/End 跳到起点终点
call :pause_back
goto menu

''' + old_open, 1)

# ── 5. :unity_run 子过程 ────────────────────────────────────────
old_rb = 'rem  -- 编译。\n:rebuild\n'
assert s.count(old_rb) == 1, '锚点 :rebuild 出现 %d 次' % s.count(old_rb)
s = s.replace(old_rb,
'''rem  -- 跑一次 Unity 批处理。参数：executeMethod  说明文字
rem     [注意] 绝不能加 -nographics：那个开关会强制 Null 渲染设备，
rem       渲出来是全黑，而 Unity 一句错都不报（见 PreviewRenderer 的注释）。
rem       这个坑本工程踩过，别再踩第二次。
rem     另外一次 Unity 启动只接受一个 -executeMethod，故两个动作各起一次。
:unity_run
if not exist "%UNITY%" (
    echo.
    echo   找不到 Unity：%UNITY%
    echo   装了别的版本就改本文件顶部的 UNITY 变量。
    goto :eof
)
echo.
echo   正在%~2（Unity 批处理，2 到 4 分钟，期间没有界面）...
pushd "%~dp0."
"%UNITY%" -batchmode -quit -projectPath "%~dp0unity" -executeMethod Humen.EditorTools.%~1 -logFile "%~dp0unity-build.log"
popd
echo.
echo   完成。批处理日志：unity-build.log
goto :eof

''' + old_rb, 1)

# ⚠ 先编码成 bytes，<b>成功了再</b>开文件写。
#   写成 io.open(P,'w',encoding='gbk').write(s) 会有个很隐蔽的后果：
#   'w' 一开就把原文件截成 0 字节，而编码错误是在 write 时才抛的 ——
#   于是脚本报了一个编码异常，同时把 启动.cmd 清空了。
#   本工程不是 git 仓库（见记忆 humen-not-a-git-repo），清空就是真没了。
#   这个坑当场踩过一次，靠 启动.cmd.bak 救回来的。
out = s.replace('\n', '\r\n')
try:
    blob = out.encode('gbk')
except UnicodeEncodeError as e:
    sys.stderr.write('GBK 编不了：%s\n' % e)
    sys.stderr.write('出问题的字符是 %r（U+%04X）—— 批处理文件里不能用它。\n'
                     % (e.object[e.start:e.end], ord(e.object[e.start])))
    sys.stderr.write('原文件未改动。\n')
    raise SystemExit(1)

# 自检：编出来的字节必须能原样解回去，且不得含 chcp（见 windows-cmd-ansi-encoding）
assert blob.decode('gbk') == out, 'GBK 往返不自洽'
# 约束是"不得<b>执行</b> chcp"，不是"不得出现 chcp 这个词" ——
# 文件里本来就有四处 rem 在解释为什么不用它。判到行首才算数。
for ln in out.split('\n'):
    head = ln.strip().lower()
    assert not head.startswith('chcp'), '不该出现 chcp 命令：%r' % ln

with open(P, 'wb') as f:
    f.write(blob)
sys.stdout.write('patched ok  (%d bytes)\n' % len(blob))
