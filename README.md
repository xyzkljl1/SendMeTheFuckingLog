WIP

## 图片托管

使用 Cloudflare Pages 托管图片，由访客浏览器直接加载，不经应用服务器转发。

- 图片保存在仓库的 `wwwroot/images` 目录，可按需要创建子目录。
- 在自己的 Cloudflare 账号中创建 Pages 项目并连接自己的仓库，输出目录为 `wwwroot/images`，其它配置使用默认。
- `https://sendmethefuckinglog.pages.dev` 是作者自己的图片托管项目域名，仅供作者项目使用，不提供给其他项目使用。自行部署时必须创建自己的 Pages 项目，并使用自己的域名。
- 图片地址由自己的托管域名和相对于 `wwwroot/images` 的文件路径组成。例如，`wwwroot/images/example.jpg` 对应 `https://<你的托管域名>/example.jpg`。
- 图片变更提交并推送到 `main` 后，Pages 会自动部署；部署成功后才能访问新文件。可在 Pages 的 Build watch paths 中将包含路径设置为 `wwwroot/images/*`，减少无关代码变更触发的部署。
- 在使用图片的页面或配置中填写对应的完整 HTTPS 图片地址。
- 替换图片时建议使用新文件名，并同步更新引用地址，避免浏览器缓存旧图；应先确认新图片已发布，再更新使用该图片的页面或配置。
