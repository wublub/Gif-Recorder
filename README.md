# GifRecorder（Windows 录屏导出工具）

一个简单的 Windows 桌面录屏工具：支持全屏/框选区域/窗口录制，并在停止录制后弹出导出设置，可导出 **GIF / AVI(MJPEG) / MP4**。

> 技术栈：WPF（.NET 8）

## 功能

- 录制模式
  - 全屏录制
  - 框选区域录制
  - 程序窗口录制
- 快捷键
  - 默认快捷键：`Alt + G` 开始/停止录制
- 素材处理
  - 录制后可导出
  - 支持打开裁剪窗口，对素材进行区间保留/删除
- 导出格式
  - GIF
  - AVI（MJPEG）
  - MP4

## 新增：按文件大小限制导出（GIF）

导出设置中新增「文件大小限制」区块（**仅对 GIF 生效**）：

- 勾选后可填写目标上限（MB）
- 如果导出结果超过上限，会自动下调导出参数并重试（颜色数/抖动/宽度/FPS 等）
- 支持选择策略：均衡 / 优先画质 / 优先流畅
- 支持选择“仍超限时”的处理：
  - 仍导出最小版本并提示
  - 取消导出并提示

> 说明：该功能默认关闭，不影响原有导出流程。

## 使用方法

1. 打开程序
2. 选择“录制模式”以及“目标范围”（全屏模式不需要选择目标）
3. 点击“开始录制”，或按 `Alt + G`
4. 再次按 `Alt + G` 或点击“停止录制”结束
5. 停止后会自动弹出“导出设置”，选择格式与参数并导出

## 快捷键说明

- `Alt + G`：开始录制 / 停止录制（同一个快捷键切换）

如果 `Alt + G` 没反应：请查看主窗口“状态”区域提示，可能是快捷键注册失败或被其他软件占用。

## 构建与运行（开发者）

在项目目录执行：

```bash
# Debug 运行（win-x64 输出路径，避免文件锁导致构建失败）
dotnet run -c Debug -r win-x64
```

## 下载（推荐）

建议到 GitHub Releases 下载已打包好的 exe：

- 免安装版（推荐大多数用户）：`GifRecorder-win-x64-self-contained.exe`
  - 优点：拷贝到任意 Windows 64 位电脑即可运行
  - 缺点：体积更大
- 体积最小版（框架依赖）：`GifRecorder-win-x64-framework-dependent.exe`
  - 优点：体积更小
  - 缺点：需要目标电脑安装 **.NET 8 Windows Desktop Runtime**

下载入口：仓库页面 → **Releases**。

---

## 发布（打包成 exe 给其他电脑使用）

本项目推荐发布为 **单文件 exe**，提供两种方案：

### 方案 A：自包含（免安装运行时）单文件 exe（推荐大多数用户）

输出位置：`dist/win-x64/GifRecorder.exe`

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishTrimmed=false \
  -o dist/win-x64
```

### 方案 C：框架依赖（体积更小）单文件 exe

输出位置：`dist/win-x64-fdd/GifRecorder.exe`

```bash
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishTrimmed=false \
  -o dist/win-x64-fdd
```

**注意：** 框架依赖版需要目标电脑安装 **.NET 8 Windows Desktop Runtime**，否则无法运行。

## Releases 产物命名建议

建议在 GitHub Releases 上传并命名为：

- 免安装版：`GifRecorder-win-x64-self-contained.exe`
- 体积最小版：`GifRecorder-win-x64-framework-dependent.exe`

如果你本地已经把这两个文件放在 `release-assets/`，就可以直接把它们作为 Release 附件上传。

## 依赖

- Magick.NET（GIF 编码/处理）
- OpenCvSharp（视频相关处理/编码链路）
- SharpAvi（AVI 输出）
