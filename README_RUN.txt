运行环境：需要安装 .NET 8 SDK 才能本地构建。

开发运行：
1. 打开终端进入 GifRecorder 目录
2. 执行：dotnet restore
3. 执行：dotnet run

发布 EXE：
1. 执行：dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true
2. 生成文件位于：bin/Release/net8.0-windows/win-x64/publish/
