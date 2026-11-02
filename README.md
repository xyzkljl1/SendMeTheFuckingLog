WIP

## 图片托管

影之刃零封面通过 Cloudflare Pages 提供，由访客浏览器直接加载，不经日志服务器转发。图片加载失败时显示占位内容，仍可通过中英文游戏名选择游戏。Fake Game 的演示封面暂使用本站图片。

- 图片保存在仓库的 `wwwroot/images` 目录，游戏封面放在其下的 `games` 目录。
- Cloudflare Pages 连接本仓库，生产分支为 `main`，框架预设为 `None`，构建命令为 `exit 0`，输出目录为 `wwwroot/images`，根目录留空。
- 正式图片域名为 `https://sendmethefuckinglog.pages.dev`。例如，`wwwroot/images/games/phantom-blade-zero.jpg` 对应 [影之刃零封面](https://sendmethefuckinglog.pages.dev/games/phantom-blade-zero.jpg)。
- 图片变更提交并推送到 `main` 后，Pages 会自动部署；部署成功后才能访问新文件。可在 Pages 的 Build watch paths 中将包含路径设置为 `wwwroot/images/*`，减少无关代码变更触发的部署。
- 各游戏配置中的 `image` 可填写完整的 HTTPS 图片地址，也可以使用本站 `/images/` 下的路径。修改游戏配置后需要重启网站。
- 替换封面时建议使用新文件名，并同步更新游戏配置，避免浏览器缓存旧图；应先确认新图片已发布，再部署引用新地址的网站配置。

当前影之刃零图片来源于 [PlayStation 官方游戏页面](https://www.playstation.com/zh-hans-hk/games/phantom-blade-zero/)。新增图片前请确认具有相应使用权限。
