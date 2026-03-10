# GifRecorder（GIF 录制工具）

一个简单的 Windows 桌面录屏工具：支持全屏/框选区域/窗口录制，并在停止录制后弹出导出设置，可导出 GIF / AVI(MJPEG) / MP4。

> 本项目为 WPF（.NET 8）应用。

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

请到 GitHub Releases 下载已打包好的 exe：

- 免安装版（推荐大多数用户）：`GifRecorder-win-x64-self-contained.exe`
  - 优点：拷贝到任意 Windows 64 位电脑即可运行
  - 缺点：体积更大
- 体积最小版（方案 C）：`GifRecorder-win-x64-framework-dependent.exe`
  - 优点：体积更小
  - 缺点：需要目标电脑安装 **.NET 8 Windows Desktop Runtime**

下载入口：仓库页面 → **Releases**。

---

## 发布（给其他电脑使用）

### 方案 C：框架依赖（体积更小）单文件 exe

输出位置：`dist/win-x64-fdd/GifRecorder.exe`

发布命令：

```bash
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishTrimmed=false \
  -o dist/win-x64-fdd
```

**注意：** 该方案需要目标电脑安装 **.NET 8 Windows Desktop Runtime**，否则无法运行。

### 方案（对比）：自包含单文件 exe（无需安装运行时）

输出位置：`dist/win-x64/GifRecorder.exe`

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishTrimmed=false \
  -o dist/win-x64
```

## 依赖

- Magick.NET（GIF 编码/处理）
- OpenCvSharp（视频相关处理/编码链路）
- SharpAvi（AVI 输出）
