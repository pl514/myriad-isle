# 万象岛（标准版）发布打包脚本
# 用法：  .\Publish.ps1              （默认版本 3.15，可用 -Version 指定）
# 产物：  publish\万象岛-<版本>\  和  publish\万象岛-<版本>.zip
param(
    [string]$Version = "3.15"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$package = "万象岛-$Version"
$outRoot = Join-Path $root 'publish'
$stage = Join-Path $outRoot $package
$zip = Join-Path $outRoot ($package + '.zip')
$build = Join-Path $root 'build'

Write-Host "== 打包 $package ==" -ForegroundColor Cyan

# ---- 1. 编译 ----
& (Join-Path $root 'Build.ps1')
$exe = Join-Path $build 'MyriadIsle.exe'
if (-not (Test-Path $exe)) { throw "编译产物不存在：$exe" }

# ---- 2. 清开发期日志（不随包发布）----
$dataDir = Join-Path $build 'data'
if (Test-Path $dataDir) {
    Get-ChildItem $dataDir -File -Filter '*.log' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
}

# ---- 3. 组包 ----
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item $exe $stage -Force

# ---- 4. 使用说明 ----
$readme = @'
万象岛（标准版）—— Windows 顶部的动态岛胶囊截图 / 翻译工具
版本：__VERSION__

一、运行
  双击 MyriadIsle.exe 即可，免安装、单文件。
  首次运行会在程序目录下创建 data\ 保存图片、文字、配置。

  如果双击没反应：多为杀毒软件拦截未签名程序，把文件夹加入信任后重试。

二、功能速览
  · 截图：区域 / 窗口 / 全屏，图片直接进胶囊
  · 图片编辑：矩形、椭圆、箭头、画笔、高亮、文字、气泡、步骤、模糊、马赛克、裁剪
  · 文字：剪贴板文字自动收进胶囊
  · 翻译：文本翻译 + 屏幕框选翻译（免费 MyMemory，或用自己的 AI 模型）
  · 微信 / QQ：未读消息提示，点击直达
  · 录屏：ffmpeg 录 MP4（需系统有 ffmpeg，或把 ffmpeg.exe 放本目录）
  · 视频：胶囊里的视频可直接播放

三、热键（托盘右键 → 热键设置可改）
  显示 / 收起胶囊   Ctrl + Alt + Shift + Space
  区域截图          Ctrl + Alt + Shift + A
  窗口截图          Ctrl + Alt + Shift + 2
  全屏截图          Ctrl + Alt + Shift + 3
  收起 / 展开图片   Ctrl + Alt + Shift + H
  开始 / 停止录屏   Ctrl + Alt + Shift + R
  屏幕翻译          Ctrl + Alt + Shift + F

四、数据位置
  全部在程序目录下的 data\：
    images\        截图与图片
    screenshots\   剪贴板图片
    Recordings\    录屏
    translate-img\ 图片翻译结果
  想迁移：整个文件夹复制走即可（绿色版）。

五、和 Pro 版的区别
  标准版更轻：只有截图 / 编辑 / 翻译 / 提示 / 录屏，体积小、依赖少。
  Pro 版另有：抖音置顶小窗、音乐面板与桌面歌词、AI 问答面板、更多球状快捷入口。

__VERSION__
'@
$readme = $readme.Replace('__VERSION__', $Version)
[System.IO.File]::WriteAllText((Join-Path $stage '使用说明.txt'), $readme, (New-Object System.Text.UTF8Encoding($true)))

# ---- 5. 压缩 ----
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

$size = [math]::Round((Get-Item $zip).Length / 1MB, 2)
Write-Host ""
Write-Host "完成：" -ForegroundColor Green
Write-Host "  文件夹  $stage"
Write-Host "  压缩包  $zip  ($size MB)"
Get-ChildItem $stage | Select-Object Name, @{n='大小(KB)';e={ if ($_.PSIsContainer) { '<目录>' } else { [math]::Round($_.Length/1KB,0) } }} | Format-Table -AutoSize
