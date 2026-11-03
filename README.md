# Send Me The Log

给 Mod 作者使用的日志收集网站。用户按照图文指引选择游戏文件、填写昵称并发送，作者在自己的服务器上查看和下载日志。

## AI 生成说明

本项目的代码、文案和文档包含 OpenAI Codex 生成及修改的内容，功能需求由作者提出。

## 功能

- **自动查找**：用户拖入游戏安装文件夹或其中的全部内容，网站识别游戏，只选取配置中指定的日志和设置文件。
- **手动选择**：用户选择游戏，按照对应指引添加文件；必须添加全部主要日志才能发送，次要文件仅在作者要求时添加。
- **中英文界面**：根据浏览器语言选择默认语言，支持手动切换；在浏览器中记住语言、昵称、游戏和上传模式。文件需要每次重新选择。
- **图片及 GIF 指引**：每条操作说明都可以附加图片，支持自动切换中英文素材。
- **本地存储**：按提交保存文件，文件夹名称包含日期、时间和游戏标识；存储容量不足时自动清理最旧的提交。
- **作者后台**：`/files` 按提交展示昵称、游戏和文件，支持分页、文本预览和完整下载。预览最多显示前 64K 字符。
- **公开状态页**：`/status` 显示请求额度、流量监控状态、含游戏和时间信息的上传文件夹名称及文件数量。
- **运行保护**：全站滚动请求限额、Linux 宿主机流量监控、自动 HTTPS 签发与续期，以及 GitHub 推送后的自动部署。

## 部署前准备

- GitHub 仓库和 Linux x86_64 服务器。
- 服务器安装 systemd、Python 3、提供 `ip` 命令的工具，以及 [Docker Engine 和 Compose 插件](https://docs.docker.com/engine/install/ubuntu/)。全新的 Ubuntu 24.04 x86_64 服务器可使用下面的初始化脚本。
- SSH 部署账号支持密钥登录，并能通过 `sudo -n` 执行 Python 3。
- 网站域名解析到服务器，开放 TCP 80、443 和 SSH 端口。
- 图片托管地址，配置方式见下文。

当前网站容器内存上限为 128 MiB，临时证书容器上限为 256 MiB。

### 首次初始化服务器

登录服务器，下载自己仓库中的 [deployment/init-ubuntu.sh](deployment/init-ubuntu.sh)。将下面的 `OWNER/REPOSITORY` 替换为自己的 GitHub 仓库：

```bash
curl -fL https://raw.githubusercontent.com/OWNER/REPOSITORY/main/deployment/init-ubuntu.sh -o init-ubuntu.sh
sudo bash init-ubuntu.sh
```

也可以通过 SFTP 上传脚本。已下载完整仓库时，在仓库根目录执行 `sudo bash deployment/init-ubuntu.sh`。

脚本支持 **Ubuntu 24.04、x86_64（amd64）、systemd**，依次检查环境、安装基础依赖、添加 Docker 官方软件源、安装 Docker Engine 和 Compose、启用并检查 Docker 服务。脚本以 root 写入系统软件源和软件包目录；相关软件包按需安装或升级，已有软件包不会被自动移除。

执行时会显示每条命令及原始输出。任一命令失败立即停止，末尾的 `ERROR` 显示脚本行号、退出码和失败命令。根据该命令的输出排查，也可在服务器终端使用 `sudo` 单独执行失败命令；修正后重新运行脚本即可。

初始化成功后，完成这些设置：

- 将网站域名解析到服务器。在云防火墙及已启用的系统防火墙中开放 TCP 80、443 和 SSH 端口（默认 22）。
- 配置部署账号的 SSH 公钥和免密 sudo。用该账号运行 `sudo -n python3 --version`，应直接输出版本。
- 按下文填写网站配置以及 GitHub Variables、Secrets，再运行部署工作流。

初始化脚本只准备服务器依赖。网站、证书和监控由现有 Actions 部署；后续版本更新直接使用 Actions。

## 配置自己的网站

[appsettings.json](appsettings.json) 设置作者名称、图片托管地址、后台白名单、容量和请求限额。本机运行和部署使用同一份设置。

在 [appsettings.json](appsettings.json) 的 `Tls.Domain` 中填写网站域名，例如 `logs.example.com`，用于证书签发、续期和 HTTPS 访问检查。

| 配置项 | 用途 |
| --- | --- |
| `UploadPage.AuthorName` | 上传页面显示的收件作者名称 |
| `ImageHosting.BaseUrl` | 图片托管地址，格式为 `https://图片域名` |
| `AuthorAccess.AllowedClients` | 后台访问白名单，支持域名、IPv4、IPv6，或 `*`（任意 IP） |
| `Storage.MaxStorageMiB` | 上传存储总容量，默认 1024 MiB |
| `Storage.MaxFileMiB` | 单个文件大小上限，默认 20 MiB |
| `Storage.MaxSubmissionMiB` | 一次提交的文件总大小上限，默认 100 MiB |
| `GlobalRateLimit.MaxRequestsPerHour` | 最近 1 小时全站请求上限 |
| `GlobalRateLimit.MaxRequestsPerDay` | 最近 24 小时全站请求上限 |
| `GlobalRateLimit.MaxRequestsPerWeek` | 最近 168 小时全站请求上限 |

容量关系：单文件上限 ≤ 单次提交上限 ≤ 上传存储总容量。上传存储总容量统计上传文件及提交元数据。

### 游戏和日志

在 [Configuration/Games](Configuration/Games) 中为每款游戏维护一份配置，填写游戏标识、中英文名称、封面、操作说明、识别条件及文件清单。

- 文件路径相对于游戏安装根目录填写。
- `enable` 控制游戏是否启用，省略时默认为 `true`。设置为 `false` 后，该游戏停止展示、自动识别和接收上传；配置、识别映射及素材可以保留。
- `files` 定义需要收集的文件；`kind` 使用 `log` 或 `config`，`isPrimary: true` 表示手动模式必须上传。
- `instructions` 提供中英文操作说明，每条说明的 `images` 可以包含多张图片或 GIF。
- [Configuration/GameRecognition.json](Configuration/GameRecognition.json) 维护 UE 根目录名称、RE 根目录 EXE 名称到游戏标识的映射。未采用通用规则的游戏使用自身 `executablePaths` 识别。
- 自动模式会选择文件清单中的全部匹配项，包括次要文件。

修改配置后重新运行或部署网站。

### 图片及 GIF 托管

所有界面图片和动图从独立托管地址加载，访客直接访问图片服务，减少应用服务器的出站流量。可使用 Cloudflare Pages 托管仓库中的 [wwwroot/images](wwwroot/images)。

自动模式使用内置的通用演示 GIF，无需配置路径；手动模式的指引图片由各游戏配置指定。两者均使用 `ImageHosting.BaseUrl`，并自动选择中英文素材。

1. 在 Cloudflare 中创建 **Pages** 项目，连接自己的 GitHub 仓库。
2. 生产分支选择 `main`，框架选择 `None`，构建命令填写 `exit 0`，构建输出目录填写 `wwwroot/images`，根目录使用仓库根目录。详见 [Cloudflare 官方指南](https://developers.cloudflare.com/pages/framework-guides/deploy-anything/)。
3. 部署完成后，将获得的 HTTPS 域名填入 `ImageHosting.BaseUrl`。
4. 游戏封面和指引图片填写相对于图片根目录的路径，例如 `games/cover.webp`。
5. 中英文指引可以填写一次 `guides/open-folder-{lang}.gif`，并提供对应的 `-zh.gif`、`-en.gif` 文件；不含 `{lang}` 时两种语言共用同一图片。

推送图片变更后，Pages 自动更新。浏览器缓存期限为一年，图片内容变化时应用生成新的版本参数。更新图片时，同时完成图片托管和应用部署。

### 作者后台访问

访问 `https://你的网站域名/files`。在 `AuthorAccess.AllowedClients` 中填写允许访问的来源，可混合使用域名和 IP：

```json
"AuthorAccess": {
  "AllowedClients": ["author.example.com", "203.0.113.10", "2001:db8::10"]
}
```

域名应解析到**作者访问网站时使用的公网出口 IP**，支持动态 DNS。

`["*"]` 允许所有访客查看和下载后台文件；空列表拒绝所有访问。

网站启动时读取白名单，之后每 10 分钟更新并重新解析域名。解析失败时撤销该域名的旧地址，其余条目继续有效。列表、预览和下载均检查来源 IP，未匹配时返回 403。本机调试可将 `127.0.0.1` 和 `::1` 加入白名单。

后台使用客户端连接 IP 判断权限。接入 CDN 或反向代理时，需要适配可信代理和真实客户端 IP。

## GitHub 自动部署

工作流使用当前个人账号下的仓库，自动获取所有者和仓库名，镜像地址为 `ghcr.io/所有者/仓库名`（小写），部署分支为 `main`。

### 1. 设置 GitHub 变量和密钥

推荐在自己仓库的 **Settings → Secrets and variables → Actions** 配置部署连接参数和密钥：

| 类型 | 名称 | 内容 |
| --- | --- | --- |
| Variable | `DEPLOY_HOST` | 服务器主机名或公网 IPv4 地址 |
| Variable | `DEPLOY_USER` | SSH 登录用户名 |
| Variable | `DEPLOY_PORT` | SSH 端口，默认 `22` |
| Secret | `DEPLOY_SSH_KEY` | 用于部署的 SSH 私钥内容，对应公钥需已安装到服务器 |

也可以使用私有配置仓库集中管理个人设置。在主仓库设置 Variable `CONFIG_REPOSITORY`（格式为 `所有者/仓库名`），并设置 Secret `CONFIG_READ_KEY`，内容为具有该配置仓库只读权限的 SSH 私钥。

私有配置仓库根目录的 `instance.json` 可填写 `Deployment.Host`、`Deployment.User`、`Deployment.Port` 和 `Deployment.Mode`；当前支持 `Direct`（直接部署）和 `BehindProxy`（反向代理）模式，默认 `Direct`。作者名称、图片域名、网站域名等设置也可以在此覆盖。未填写的设置继承主仓库的公开默认值，密钥继续保存在 GitHub Secrets。

部署时按“公开默认配置 → 私有配置 → 非空 GitHub Variables”依次覆盖。`DEPLOY_HOST`、`DEPLOY_USER`、`DEPLOY_PORT` 仍然有效；如果希望完全使用私有配置中的对应值，请删除同名 Variables 或将其留空。其他配置也可通过 `节名__参数名` 格式的 Variable 覆盖，例如 `Deployment__Mode`。未指定配置仓库时，直接使用公开配置和 Variables 部署。

镜像包须为 **Public**，关联当前仓库并授予其 Actions 管理权限。首次推送若创建成 Private，在包设置中改为 Public 后重新运行工作流。设置方法见 [GitHub 包权限说明](https://docs.github.com/en/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility)。

### 2. 部署与验证

将配置和修改推送到 `main`，或在 Actions 中选择 `main` 手动运行部署工作流。流程会：

1. 在 GitHub 上运行程序功能测试，通过后构建镜像并检查启动状态。
2. 发布镜像，通过 SSH 在服务器安装流量监控和证书续期任务。
3. 保留上传数据，更换网站容器，申请或加载证书。
4. 检查容器健康、公网 HTTPS 和证书。
5. 清理本项目旧镜像，服务器和 GHCR 仅保留当前部署版本。

工作流成功后，访问首页、`/status` 和 `/files`。回滚需重新构建并部署目标版本。

## HTTPS、流量和存储维护

- **HTTPS**：部署会使用临时 Certbot 容器签发证书，定时检查续期，并在证书更新后重启网站加载证书。公网 80 端口用于 HTTP-01 验证和 HTTPS 跳转，应保持开放；正常页面通过 443 访问。
- **请求限额**：按全站最近 1、24、168 小时统计，任一窗口达到上限即返回 429；公开状态页仍可查看。正常重启保留计数，现有重新部署流程会重置请求计数。
- **流量保护**：[appsettings.json](appsettings.json) 的 `TrafficMonitor` 设置日、月上限，默认分别为 25 GB 和 900 GB。监控默认路由网卡的入站加出站流量，每分钟检查，按 UTC 自然日和自然月累计，GB 为十进制单位。
- **达到流量上限**：监控随部署启用，检测到超额即关闭整台服务器。重启和重新部署均保留累计值，同一统计周期内开机可能再次触发关机。
- **文件保留**：重新部署保留已有提交，达到容量限制时自动清理最旧提交。

## 自动化测试

推送到 `main`、提交 Pull Request 或手动运行 Actions 工作流时，会先执行程序功能测试。测试失败会阻止部署。

测试在 GitHub 的临时 Linux Runner 上启动网站和 Chromium，覆盖自动识别、手动上传、中英文、浏览器偏好、托管图片、文件保存与清理、后台访问与下载、请求限流、状态页和 HTTPS。模拟文件与上传数据保存在 Runner 的独立目录中。

在 Actions 的 `Program functional tests` 中查看结果。测试报告及浏览器失败截图、跟踪记录保留 7 天。测试统一使用 xUnit v3、ASP.NET Core 测试宿主和 Playwright .NET。

## 本机运行

安装 .NET 10 SDK，在 `appsettings.json` 中填写作者名称、图片域名和后台白名单，然后在仓库根目录执行：

```sh
dotnet run --configuration Debug --no-launch-profile --urls http://localhost:5180
```

本机地址为 `http://localhost:5180`，后台继续执行 IP 白名单检查。数据保存在可执行程序目录下的数据目录。
