@echo off
setlocal
title Humen - Blue Planet

rem ================================================================
rem  蓝星 Humen · 一键启动（双击本文件即可）
rem
rem  【编码】本文件必须存为 ANSI / CP936，不能用 UTF-8。
rem  cmd.exe 是拿「当前控制台代码页」逐字节解析批处理文件的，
rem  中文 Windows 的控制台代码页是 936；存成 UTF-8 的话，中文
rem  会被当成 GBK 解，注释、echo、title 全变乱码。带 BOM 更糟：
rem  BOM 的三个字节 EF BB BF 会被当成第一行命令，直接报错。
rem
rem  【为什么不用 chcp 65001】那反而会把 GBK 的脚本内容变成乱码。
rem  936 环境下本文件什么都不用设。本文件里一个 chcp 都没有，
rem  理由见下面那条。
rem
rem  【为什么本文件里一个 chcp 都没有】humen.exe 会把控制台代码页切成
rem  65001，但它退出时会自己还原（见 Program.cs 的 Main）。所以这里
rem  不需要、也绝不能加 chcp：实测 chcp 会让 cmd 丢弃重定向的标准
rem  输入，本文件所有 set /p 就全部读空 —— 菜单空转三次然后退出。
rem
rem  【为什么每条 humen 命令都带 --no-pause】humen.exe 在「自己独占
rem  控制台」时会停下等回车。在这个脚本里暂停权归脚本，让它再停一次
rem  会出现两次「按回车」；更要命的是它会从标准输入多读走一行，
rem  把后面 set /p 的输入串位。
rem
rem  【为什么是纯 bat 而不是 ps1】少一层解释器就少一类编码坑。
rem  本文件不依赖 PowerShell，也不需要 ExecutionPolicy。
rem
rem  本脚本只做菜单：真正的逻辑全在 Humen.Cli / Humen.Core 里，
rem  这里不重算任何东西。脚本坏了不影响模拟结果，反之亦然。
rem ================================================================

set "ROOT=%~dp0"
set "EXE=%~dp0bin\humen.exe"
set "EMPTY=0"

rem  Unity 编辑器的绝对路径。装在别的盘就改这一行。
rem  [注意] 是 <安装目录>\Editor\Unity.exe —— 不是 <安装目录>\Unity.exe，
rem     少了中间那层 Editor 目录的话，下面所有 Unity 项都会报"找不到文件"。
set "UNITY=G:\Unity\Editors\6000.0.30f1\Editor\Unity.exe"

:menu
cls
echo.
echo   蓝星 Humen · 文明进化模拟器 · M4
echo   ========================================================
echo    [1]  建世界数据库         world.db             约 6 秒
echo    [2]  画星球（默认视角）   renders\planet.png   约 5 秒
echo    [3]  画星球（四个视角）   renders\view_*       约 20 秒
echo    [4]  末次冰盛期 -21000    renders\lgm          约 5 秒
echo    [5]  看一个具体的人       derive               瞬时
echo    [6]  跑不变式验收         verify               约 6 秒
echo    [7]  打开输出目录
echo    [8]  看星球图
echo    [9]  重新编译
echo    --------------------------------------------------------
echo    [10] 跑完整演化（30 万年） world.db          约 3 分钟
echo    [11] 导出世界快照给 Unity  StreamingAssets   约 2 秒
echo    [12] 重建 Unity 星球场景                     约 3 分钟
echo    [13] 渲染 Unity 预览图     unity_preview.png 约 3 分钟
echo    [14] 打开 Unity 编辑器（可自由飞行）
echo    [15] 重建聚落场景                            约 3 分钟
echo    [16] 渲染聚落演化对比图  unity_settlement_*.png 约 5 分钟
echo    [17] 生成可执行程序     unity\Build\Windows\Humen.exe 约 5 分钟
echo    [18] 渲染时间轴进化图   unity_tl_*.png 约 5 分钟
echo    --------------------------------------------------------
echo    [0]  退出
echo   ========================================================
if not exist "%EXE%" echo    还没编译过，请先选 [9]。
echo.
set "CH="
set /p "CH=  请选择: "
if not defined CH goto no_input
set "EMPTY=0"

if "%CH%"=="1" goto do_build_world
if "%CH%"=="2" goto do_planet
if "%CH%"=="3" goto do_four
if "%CH%"=="4" goto do_lgm
if "%CH%"=="5" goto do_derive
if "%CH%"=="6" goto do_verify
if "%CH%"=="7" goto do_open
if "%CH%"=="8" goto do_view
if "%CH%"=="9" goto do_rebuild
if "%CH%"=="10" goto do_evolve
if "%CH%"=="11" goto do_export
if "%CH%"=="12" goto do_unity_scene
if "%CH%"=="13" goto do_unity_preview
if "%CH%"=="14" goto do_unity_open
if "%CH%"=="15" goto do_settle_scene
if "%CH%"=="16" goto do_settle_eras
if "%CH%"=="17" goto do_player
if "%CH%"=="18" goto do_timeline_eras
if "%CH%"=="0" goto done

echo.
echo   没有这一项：%CH%
call :pause_back
goto menu

rem  输入流读到头（EOF）时 set /p 不报错，只是不碰那个变量 ——
rem  变量保持未定义。直接 goto menu 就会无限刷屏（实测刷到 1 MB 还在刷）。
rem  数满三次就退出。用户单纯连按回车也走这里，按三次即退出。
:no_input
set /a EMPTY+=1
if %EMPTY% GEQ 3 goto done
goto menu

rem ================================================================
rem  动作
rem ================================================================

:do_build_world
call :ensure_exe
if errorlevel 1 goto menu
echo.
pushd "%~dp0."
"%EXE%" build-world --out world.db --no-pause
popd
call :show_file world.db
call :pause_back
goto menu

:do_planet
call :ensure_exe
if errorlevel 1 goto menu
echo.
pushd "%~dp0."
"%EXE%" planet --out renders --size 900 --no-pause
popd
call :show_dir renders
call :pause_back
goto menu

:do_four
call :ensure_exe
if errorlevel 1 goto menu
call :render_view view_c1c4 62 -20 "C1 + C4 北极大陆"
call :render_view view_c3 0 180 "C3 赤道大陆"
call :render_view view_c2c5 -62 20 "C2 + C5 南极大陆"
call :show_dir renders
call :pause_back
goto menu

:do_lgm
call :ensure_exe
if errorlevel 1 goto menu
echo.
echo   末次冰盛期：海平面 -120 m，看 5 块大陆之间露出的陆架。
echo.
pushd "%~dp0."
"%EXE%" planet --out renders\lgm --size 900 --year -21000 --no-pause
popd
call :show_dir renders\lgm
call :pause_back
goto menu

:do_derive
call :ensure_exe
if errorlevel 1 goto menu
echo.
echo   c 大陆号 1 到 5，t 部落号 1 到 20，seq 序号大于等于 1。回车用默认值。
echo.
set "DC="
set "DT="
set "DS="
set /p "DC=  c   [1]: "
set /p "DT=  t   [1]: "
set /p "DS=  seq [1]: "
if not defined DC set "DC=1"
if not defined DT set "DT=1"
if not defined DS set "DS=1"
echo.
pushd "%~dp0."
"%EXE%" derive --c %DC% --t %DT% --seq %DS% --no-pause
popd
call :pause_back
goto menu

:do_verify
call :ensure_exe
if errorlevel 1 goto menu
if not exist "%ROOT%world.db" (
    echo.
    echo   还没有 world.db，先建库再验收。
    pushd "%~dp0."
    "%EXE%" build-world --out world.db --no-pause
    popd
    call :pause_back
    goto menu
)
echo.
pushd "%~dp0."
"%EXE%" verify --db world.db --no-pause
popd
call :pause_back
goto menu

:do_evolve
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
echo     unity\Assets\StreamingAssets\World\world_view.json
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

:do_settle_scene
call :unity_run SettlementSceneBuilder.BuildBatch "重建聚落场景"
call :pause_back
goto menu

:do_settle_eras
rem  一次出四张：同一部落的游群／村落／铁器／工业。
rem  年份是按该部落自己的技术门槛挑的，不是随手取的 —— 见 SettlementSceneBuilder 的注释。
call :unity_run SettlementSceneBuilder.RenderErasBatch "渲染聚落演化图"
call :pause_back
goto menu

:do_player
rem  把两个场景打成一个免安装的 Windows 程序，产物在 unity\Build\Windows\。
rem  [注意] 产物里的 exe 也叫 Humen.exe，跟 bin\humen.exe（命令行版）同名但不同目录，
rem     别搞混：前者是三维蓝星，后者是算世界的那个。
rem     打包前 Unity 会先自动重建两个场景吗？不会 —— 所以改过场景生成器的话，
rem     先跑 [12] 与 [15]，再回来打包。这一条以前是靠人记着的，现在写在这里。
call :unity_run PlayerBuilder.BuildWindowsBatch "生成可执行程序"
if exist "%~dp0unity\Build\Windows\Humen.exe" echo   产物：unity\Build\Windows\Humen.exe
call :pause_back
goto menu

:do_timeline_eras
rem  一次出 26 张：同一块大陆、同一个机位，只有年份不同 —— 时间轴取证。
rem  两年份之间只有年份在变，故画面上变了的就是进化，没变的就是没做出来。
call :unity_run TimelinePreviewRenderer.RenderBatch "渲染时间轴进化图"
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
echo     左键拖拽拨地球 · 右键拖拽转视角 · 滚轮缩放 · R 开关自转
echo     左键点部落选中 · Tab 降落看聚落 · 空格暂停时间
echo     左右方向键拖动年份 · Home/End 跳到起点终点
call :pause_back
goto menu

:do_open
if exist "%ROOT%renders" start "" "%ROOT%renders"
if exist "%ROOT%world.db" start "" explorer /select,"%ROOT%world.db"
echo.
echo   已打开输出目录。
call :pause_back
goto menu

:do_view
if not exist "%ROOT%renders\planet.png" (
    echo.
    echo   还没有 renders\planet.png，先跑 [2]。
    call :pause_back
    goto menu
)
start "" "%ROOT%renders\planet.png"
echo.
echo   已用系统默认看图程序打开 renders\planet.png
echo   想看全 5 块大陆，开 renders\map_biome.png 更直观。
call :pause_back
goto menu

:do_rebuild
call :rebuild
call :pause_back
goto menu

:done
endlocal
exit /b 0

rem ================================================================
rem  子过程
rem ================================================================

rem  -- 画一个视角。参数：输出子目录  相机纬度  相机经度  说明文字
:render_view
echo.
echo   --- %~4
pushd "%~dp0."
"%EXE%" planet --out "renders\%~1" --size 900 --cam-lat %~2 --cam-lon %~3 --no-mesh --no-pause
popd
call :show_dir renders\%~1
goto :eof

rem  -- 没有 exe 就先编一次。返回 0 成功 / 1 失败。
:ensure_exe
if exist "%EXE%" exit /b 0
echo.
echo   还没有 bin\humen.exe，先编译一次。
call :rebuild
if exist "%EXE%" exit /b 0
exit /b 1

rem  -- 跑一次 Unity 批处理。参数：executeMethod  说明文字
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

rem  -- 编译。
:rebuild
echo.
echo   正在编译（首次约 10 秒）...
pushd "%~dp0src\Humen.Cli"
dotnet publish -c Release -o "%~dp0bin" --nologo -v q
popd
echo.
if exist "%EXE%" echo   编译完成。
if not exist "%EXE%" echo   编译失败。多半是没装 .NET 8 SDK。
goto :eof

rem  -- 列一个目录里的产物。
:show_dir
if not exist "%ROOT%%~1" goto :eof
echo.
echo   %~1\
dir /b /a-d "%ROOT%%~1" 2>nul
goto :eof

rem  -- 报一个文件的大小。
:show_file
if not exist "%ROOT%%~1" goto :eof
echo.
echo   %~1 已生成：
dir "%ROOT%%~1" | findstr /i "%~1"
goto :eof

rem  -- 停一下再回菜单。
:pause_back
set /p "PAUSE_KEY=  按回车键返回菜单..."
goto :eof
