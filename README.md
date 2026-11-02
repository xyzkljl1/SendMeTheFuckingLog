WIP

## 图片托管

使用 Cloudflare Pages 托管图片，由访客浏览器直接加载，不经应用服务器转发。

- 图片保存在仓库的 `wwwroot/images` 目录，可按需要创建子目录。
- 在自己的 Cloudflare 账号中创建 Pages 项目并连接自己的仓库，输出目录为 `wwwroot/images`，其它配置使用默认，获取托管域名后填到ImageHosting.BaseUrl。
- 配置中的图片地址只填写相对于 `wwwroot/images` 的路径，例如 `games/cover.jpg` 或 `games/guide.gif`，不要填写完整网址或本站 `/images/` 地址。
- 图片变更提交并推送到 `main` 后，Pages 会自动部署；。
