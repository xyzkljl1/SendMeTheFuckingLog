# SendMeTheFuckingLog

用于 Mod 日志收集网站的初始项目。目前仅提供一个显示 **Hello World** 的首页，尚未包含日志收集、上传、作者后台或自动部署功能。

## 本机运行

安装 .NET 10 SDK，在项目目录执行：

```shell
dotnet run
```

打开 <http://localhost:5180> 查看首页。在终端按 `Ctrl+C` 停止服务。

也可以使用支持 .NET 10 的 IDE 打开项目文件，选择 Debug 配置启动调试。

## 构建

```shell
dotnet build --configuration Debug
```
