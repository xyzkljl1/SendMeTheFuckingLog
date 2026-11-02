# GIF Generator：AI agent 使用说明

本工具用 Python/Pillow 将截图素材、目录清单、游戏名称和鼠标动画合成为操作演示 GIF。它不操作真实 Steam、不录屏，也不会自动理解任意截图中的按钮位置。支持从 Steam 打开目录、框选拖入网页，以及从游戏根目录逐级查找文件并拖入网页三种模板。

每次通过命令行提供游戏参数，不要为单次任务创建永久游戏 JSON、游戏专用脚本或命令记录文件。通用模板 JSON 可以长期复用。本文只说明工具用法，不替代项目和用户的授权要求。

## 环境与入口

- 入口：`GifGuideGenerator/generate.py`。
- 依赖：Python 和 `requirements.txt` 中指定版本的 Pillow；开发环境已使用 Python 3.13 验证。先检查现有环境，缺少依赖时按项目授权规则处理，不擅自修改全局环境。
- 字体：自动查找 Windows 微软雅黑、Linux Noto Sans CJK 或 macOS PingFang；找不到时用 `--font` 指定已有字体。工具不会安装字体。
- 下文命令均在仓库根目录运行，使用 `python -B` 避免生成 `__pycache__`。

```powershell
python -B GifGuideGenerator/generate.py --help
```

## 一次生成流程

1. 确认目标游戏、真实操作顺序和需要的语言。相同 Steam 操作可直接复用默认模板；不同操作须先准备对应模板和素材。
2. 准备图标、中文目录截图和英文目录截图。输入文件只读，可以位于用户指定的其它目录，无须复制到工具目录。制作或处理素材时仍须遵守当前任务的文件写入和临时文件许可要求。
3. 通过命令参数生成，默认同时输出中英文。
4. 检查输出预览图及动画，确认菜单文字、游戏名称、目录内容和鼠标位置正确。
5. 仅在任务包含网页接入或发布时，将最终 GIF 接入项目已有的托管素材流程。

现有素材的可执行示例（PowerShell）：

```powershell
python -B GifGuideGenerator/generate.py god-eater-2 `
  --name 'GOD EATER 2 Rage Burst' `
  --icon 'GifGuideGenerator/assets/games/god-eater-2/icon.png' `
  --directory 'GifGuideGenerator/assets/games/god-eater-2/directory-{lang}.png'
```

不同中英文名称及示意素材的示例：

```powershell
python -B GifGuideGenerator/generate.py phantom-blade-zero-demo `
  --name '影之刃零' `
  --name-en 'Phantom Blade Zero' `
  --icon 'GifGuideGenerator/assets/games/phantom-blade-zero-demo/icon.png' `
  --directory 'GifGuideGenerator/assets/games/phantom-blade-zero-demo/directory-{lang}.png' `
  --illustration
```

为其它游戏生成时替换 ID、名称和素材路径即可，不要恢复已移除的游戏配置文件机制。`--all` 已不存在。

## 可用模板

| `--template` | 演示流程 | 额外素材 | 输出前缀 |
| --- | --- | --- | --- |
| `steam-open-directory`（默认） | Steam 右键游戏 → 管理 → 浏览本地文件 | 无 | `steam-open-` |
| `steam-drag-to-upload` | 上述完整流程 → 从空白处按住鼠标左键框选 → 松开后所有文件和文件夹保持高亮 → 拖入上传页面 → 松开鼠标；文字提示也可按 Ctrl+A 全选 | 必须提供 `--directory-selected`；内置实际渲染的中英文上传页面截图 | `steam-drag-` |
| `navigate-to-file` | 从给定游戏根目录开始 → 依次双击文件夹 → 单击选中最后的指定文件 → 拖入手动上传区域 → 松开鼠标 | `--navigation` 完整目录清单；内置中英文手动模式页面截图，可通过 `--upload-page` 替换 | `navigate-` |

长模板使用示例：

```powershell
python -B GifGuideGenerator/generate.py phantom-blade-zero-demo `
  --template steam-drag-to-upload `
  --name '影之刃零' --name-en 'Phantom Blade Zero' `
  --icon 'GifGuideGenerator/assets/games/phantom-blade-zero-demo/icon.png' `
  --directory 'GifGuideGenerator/assets/games/phantom-blade-zero-demo/directory-drag-{lang}.png' `
  --directory-selected 'GifGuideGenerator/assets/games/phantom-blade-zero-demo/directory-drag-selected-{lang}.png' `
  --illustration
```

`directory-drag-*` 是依据用户提供的截图制作的双语示意素材，包含 Engine、platform、ProjectPBZ、steam、modinfo.ini、steam_api64.dll。它们与原短模板示例素材分开保存。换游戏时应传入该游戏自己的未选中/全选截图，不要沿用影之刃零的目录内容。

长模板在松开鼠标处结束，不演示扫描结果、确认游戏、填写昵称或发送成功；拖入网页不等于已经上传。`--illustration` 仍只记录元数据。

## 参数与路径

| 参数 | 含义 |
| --- | --- |
| 首个位置参数 | 游戏 ID，用于输出文件名，例如 `my-game`。仅允许小写 ASCII 字母、数字和分隔用的单连字符。 |
| `--name` | 必填，中文或默认显示名称。 |
| `--name-en` | 英文名称，省略时使用 `--name`，不会自动翻译。 |
| `--icon` | Steam 模板必填，图标文件路径。逐级目录模板不使用。 |
| `--directory` | Steam 模板必填，目录截图路径；`{lang}` 会替换为 `zh` 或 `en`。没有占位符时两种语言使用同一张图片。逐级目录模板不使用。 |
| `--directory-selected` | 全部文件和文件夹已选中的目录截图，支持 `{lang}`，长模板必填。应与未选中截图保持同样的窗口位置、尺寸、滚动位置和目录内容，且没有残留的框选矩形。 |
| `--language` | `both`、`zh` 或 `en`，默认 `both`。只生成一种语言时只要求该语言的目录图片存在。 |
| `--template` | 操作模板 ID，默认 `steam-open-directory`，读取 `templates/<ID>.json`。ID 格式与游戏 ID 相同。 |
| `--illustration` | 在 GIF 元数据中记录素材为示意，不会自动在画面上加「示意」文字。需要可见标识时应在素材里体现。 |
| `--output` | `output/` 下的子目录，例如 `review` 或 `review/game`。不能指定绝对路径或 `..`。 |
| `--font` | 已有 TTF/OTF/TTC 字体文件路径，应支持所需中英文字符。 |
| `--navigation` | 逐级目录模板必填：已有 UTF-8 JSON 清单文件的只读路径，或 `-` 表示从标准输入读取。清单不会另存为游戏配置。 |
| `--upload-page` | 逐级目录模板可选：已选好游戏的手动上传页面截图路径，支持 `{lang}`。省略时使用 `assets/upload/manual-{lang}.png`。 |

命令行输入素材和字体路径支持绝对路径，或相对于执行命令时的当前目录。模板内普通素材路径则相对于 `GifGuideGenerator/`，必须使用正斜杠且不能越出该目录。这两类路径不要混淆。

工具仅将输出写入 `GifGuideGenerator/output/` 或其子目录。默认模板产物为：

```text
steam-open-<游戏ID>-zh.gif
steam-open-<游戏ID>-en.gif
steam-open-<游戏ID>-zh-preview.png
steam-open-<游戏ID>-en-preview.png
```

已有同名产物会被覆盖；要保留之前结果，用不同的 `--output` 子目录。输出目录已加入 Git 忽略。预览 PNG 是从实际编码后的 GIF 提取关键画面合成的检查图。

## 素材要求与已知边界

- 当前 Steam 模板画布为 **1238 × 1020**。目录截图会直接缩放到这个尺寸，不自动裁剪或保持比例；应先准备相同比例、文字可读的画面。
- 游戏图标缩放为 36 × 36。游戏名称过长时会缩小字号；仍放不下时生成失败。
- 模板只替换左侧目标游戏行的图标和名称。背景中的其它游戏、账号文字、右侧资讯等仍来自通用底图，不会自动变成目标游戏的完整 Steam 页面。
- 目录截图应包含可辨认的资源管理器界面和游戏目录路径，避免只截一块无法判断来源的空白区域。
- 目录截图应去掉原有鼠标指针或点击标记；工具会另外绘制鼠标。默认末帧鼠标位置是 `(1020, 535)`。
- 选择英文版本只会选取英文模板素材和名称，不会翻译截图中的文字。应提供英文目录截图；菜单用语要依据真实英文客户端核实，不能凭中文直译后声称与客户端一致。
- 示例影之刃零目录画面属于修改素材制作的示意，不能当作真实目录结构证据。其它游戏也须依据用户提供的素材或已核实的信息制作。
- `click` 的左右键参数描述操作语义，目前两者使用相同的点击圆环效果。菜单显示由后续场景切换控制，不是点击坐标自动触发。

### 逐级打开目录并选择文件

`navigate-to-file` 不包含开头的 Steam 操作，直接从输入的首级目录开始。每一级必须传入**完整显示清单**，包括不点击的其它文件/文件夹，按实际资源管理器的显示顺序填写。不要只列出目标路径上的项目，也不要凭空补全截图未展示的内容。如果截图没有覆盖完整清单，先取得缺失信息。

输入结构如下。此处是最小结构示例，不是影之刃零的真实文件清单：

```json
{
  "schemaVersion": 1,
  "levels": [
    {
      "path": "ExampleGame",
      "complete": true,
      "target": "logs",
      "entries": [
        {"name":"logs", "kind":"folder", "modified":"", "size":"", "type":{"zh":"文件夹","en":"File folder"}},
        {"name":"settings.ini", "kind":"file", "modified":"", "size":"", "type":{"zh":"配置设置","en":"Configuration settings"}}
      ]
    },
    {
      "path": "ExampleGame/logs",
      "complete": true,
      "target": "output.log",
      "entries": [
        {"name":"output.log", "kind":"file", "modified":"", "size":"", "type":{"zh":"文本文档","en":"Text Document"}},
        {"name":"previous.log", "kind":"file", "modified":"", "size":"", "type":{"zh":"文本文档","en":"Text Document"}}
      ]
    }
  ]
}
```

- `path` 使用相对于示例安装位置的路径，首级包含游戏目录名；后一级必须等于前一级 `path + "/" + target`。路径是画面数据，程序不会读取该真实目录。
- `complete: true` 是调用者对清单完整性的确认，程序无法证明它与真实磁盘一致。
- `entries` 保留输入顺序，不自行按名字或类型重排。名称保持原始大小写和扩展名；`kind` 只能是 `folder` 或 `file`。
- `modified`、`size` 和双语 `type` 都必须提供。日期/大小直接按输入文本显示，不换算、不编造；确实未知时传空字符串。文件名和类型不会自动翻译，类型的中英文必须由调用者核实。
- 除最后一级外，`target` 必须是本级清单中存在的文件夹；最后一级必须是文件。模板会双击目录、单击最后文件，每次只高亮一个目标，拖动提示也使用单文件图标。
- 仅首帧设置鼠标初始位置。打开下一级文件夹后保留上一轮点击位置，再平滑移动到下一目标；滚动目录列表时也保留鼠标位置，不重置到固定角落。
- 清单缺失、路径跳级、目标不存在、目标类型错误、大小写重复项目会报错。过长的文件名或列内容放不下时也会报错，不会静默截断；路径导航栏可从左侧省略过长的前缀。
- 支持 1～8 级目录，每级 1～200 个项目；一次显示 14 行，目标位于下方时依次切换滚动位置并更新滚动条。完整清单仍保留，动画不会删除未选项目。较长路径仍受总动作数、帧数和内存限制，不保证所有最大值组合均能生成。
- 文件夹和文件采用统一示意图标，资源管理器布局按截图样式渲染，不是实际录屏；应用程序的专属图标、系统主题和字体可能与用户电脑不同。输出自动标记为示意。

推荐由 agent 将 JSON 留在内存，通过标准输入传入，不为单次生成新建永久游戏 JSON。PowerShell 示例（`$listingJson` 为按上述结构准备好的 JSON 字符串）：

```powershell
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$listingJson | python -B GifGuideGenerator/generate.py example-log `
  --name '示例游戏' --name-en 'Example Game' `
  --template navigate-to-file --navigation -
```

也可只读使用用户已有的清单：`--navigation 'C:/path/to/existing-listing.json'`。不要同时传 `--icon`、`--directory` 或 `--directory-selected`；目录画面在内存中渲染，不生成中间截图文件。

影之刃零示例依据本次提供的截图：`PBZ`（6 项）→ `PBZ/platform`（1 项）→ `PBZ/platform/steam`（7 项）→ `PBZ/platform/steam/ue4ss`（11 项），最终选择 `UE4SS.log`；`UE4SS-settings.ini` 等其它文件仍显示但不选中。示例输出名为 `navigate-phantom-blade-zero-ue4ss-log-{lang}.gif`，不会自动替换网站指引。

手动模式底图 `assets/upload/manual-{lang}.png` 来自本机实际渲染、已选中影之刃零的中英文页面。模板裁剪源图 `[0,200,1265,1242]` 并缩放至 1238 × 1020，资源管理器缩放至 690 × 568 后置于 `[8,305]`，拖放终点为 `[925,690]`。用于其它游戏时应通过 `--upload-page` 提供对应游戏的真实手动模式截图；若页面布局不同，还需调整模板的裁剪、窗口位置、高亮区域以及 `navigation.py` 中的终点坐标。不能只换目录清单却继续把影之刃零的网页当作其它游戏的页面。

### 长模板中的网页画面

`assets/upload/page-zh.png` 和 `page-en.png` 来自 2026-11-02 在本机 `http://localhost:5180/` 实际渲染的页面，分别切换中文和 English 后截图；不是凭文字重新绘制的网页。模板在这些截图上叠加缩小后的资源管理器窗口及操作提示，形成拖动演示。

网站布局变动时须重新通过浏览器采集两种语言的页面，不要只改截图里的几个文本。当前原图为 1265 × 1247，模板裁剪 `[0,0,1265,1042]` 后缩放至 1238 × 1020；资源管理器放置于 `[8,320]`，缩放至 690 × 568，拖动从 `[180,510]` 到 `[920,550]`。更新素材后需要同时校准裁剪范围、窗口位置、拖动起点和目标区域高亮。起点必须落在实际选中项上，终点必须位于真实网页拖放区域内。

目录框选由 `layout.selectionStart`、`selectionEnd` 和 `selectionRows` 定义。当前示例从空白处 `[650,680]` 向左上方 `[68,202]` 拉框，覆盖六个文件行；鼠标经过时逐行高亮，松开后移除选择框。换目录截图时需按画布上的实际位置调整各行矩形和起终点，保证起点为空白、所有项目都被框选。程序不自动识别文件数量，也不模拟窗口滚动；超出可见范围的项目应先调整截图或流程，不能声称已全部选中。Ctrl+A 在动画里只是替代操作的文字提示。

中英文页面截图含当时已配置的作者和游戏名称。复用到其它站点或布局前应更新截图。原图中自带的指引 GIF 仅是采集时的一帧，不代表长模板会递归播放自身。工具不会自动刷新网页截图，也不会联网提交任何文件。

## 扩展操作模板

同一操作换游戏只改命令参数。确需新增操作时，在 `templates/` 下新增可复用 JSON，复用 `steam-open-directory.json` 的结构，不要把某款游戏的固定名称和路径写死进通用模板。

| 字段 | 用法 |
| --- | --- |
| `schemaVersion` | 当前必须为 `1`。 |
| `canvas` | `[宽, 高]`，所有图层和鼠标使用该画布上的像素坐标。 |
| `frameMs` | 鼠标移动和点击动画的目标帧间隔；默认模板为 90 ms。 |
| `outputName` | 不含扩展名的输出名称，应含游戏 ID 和语言，例如 `steam-open-{game.id}-{lang}`，以免覆盖不同语言。 |
| `layout` | 可复用坐标等模板变量，可按语言分别给值。 |
| `scenes` | 命名场景，可通过 `extends` 继承另一场景，再依次叠加 `layers`，不允许循环继承。 |
| `previewScenes` | 1～8 个在时间线中实际出现的场景名，按此顺序生成预览。 |
| `timeline` | 顺序执行的 `hold`、`move`、`click`、`drag`、`marquee` 操作。 |
| `requiresSelectedDirectory` | 设为 `true` 时要求 CLI 提供 `--directory-selected`，当前长模板使用此字段。 |
| `requiresNavigation` | 设为 `true` 时使用 `navigation.py` 根据传入清单在内存中生成目录场景和时间线，当前逐级目录模板使用此字段。 |

图层类型：`image` 使用 `path`、`at` 和可选 `size`、`crop`；`crop` 是源图像上的 `[左,上,右,下]`，右/下边界不包含在内，先裁剪再缩放。`rectangle` 使用 `box`、可选 `fill`/`outline`/`width`；`text` 使用 `value`、`at`、可选 `size`/`maxWidth`/`fill`。图层必须落在画布内。

时间线首项必须设置 `scene` 和鼠标 `at`。后续项默认沿用上一场景及鼠标位置，可通过 `scene`、`at` 覆盖。每项均需 `durationMs`；`move` 还需 `to`，`click` 可设置 `button` 为 `left` 或 `right`。`hold` 保持画面，`move` 平滑移动鼠标，`click` 绘制点击圆环。

`drag` 同样需要 `to`，在移动过程中绘制随鼠标移动的文件提示卡片，`label` 可使用双语值（例如 `{"zh":"+ 复制","en":"+ Copy"}`）。若要表现保持按住鼠标停留在目标区域，再添加一项终点不变的 `drag`；之后切换到 `hold` 会移除提示卡片，表示松开鼠标。它只是动画，不调用真实拖放或上传 API。全选高亮来自传入的选中截图，不会自动识别文件行或按 Ctrl+A。

`marquee` 演示按住鼠标拉出半透明选择框。需要 `to`、`rows`（画布内各文件行的 `[左,上,右,下]`，含边界）及 `selectedScene`（对应已选中截图的场景名）；`anchor` 可指定选择框起点，省略时取本项操作开始时的鼠标位置。框与某行相交时，从 `selectedScene` 复制该行的高亮画面。若要在完整框选时停留，可追加一项终点不变的 `marquee`，并保留原 `anchor`；随后使用 `hold` 切到已选中场景表示松开鼠标。不要用 `click` 插在拉框中间，否则会把按住拖动误演示成点击。

可用变量为 `{lang}`、`{game.id}`、`{game.name}`、`{game.icon}`、`{game.directoryImage}` 和 `{layout.字段名}` 等。恰好包含 `zh`、`en` 两个键的对象会按语言取值。完整占位符可保留坐标数组等类型；图层的 `path` 引用 CLI 素材时应直接写 `"{game.icon}"` 或 `"{game.directoryImage}"`，不要再拼接路径前缀。

长模板另使用 `"{game.selectedDirectoryImage}"` 引用全选后的截图。短模板仍不需要这个参数。

逐级目录模板使用 `"{game.uploadPage}"` 引用页面素材；`explorer` 图层按 `level`、`start`、`selected` 索引渲染清单，`scene` 图层可将已有命名场景缩放后叠加到画面，不需要中间 PNG。清单本身不会经过占位符解释，带花括号的合法文件名仍按原文显示。`drag` 可设置 `icon: "file"` 表示单个文件；省略时使用原有多项目图标。

时长与帧间隔均须为正的 10 ms 倍数，每项时长最多 10 秒，`frameMs` 最多 1000 ms。时间线最多 100 项，生成的原始帧最多 180 张，原始 RGB 帧数据最多 400 MiB；这不是进程总内存上限，编码还会使用额外内存。画布宽 64～1920、高 64～1440，且总像素不超过 220 万。触发限制时应减少移动帧数或画面尺寸，不要为单次任务随意取消限制。

## 验证与交付

修改生成器或模板后，可执行现有的内存内回归测试：

```powershell
python -B -m unittest discover -s GifGuideGenerator -v
```

生成器会重新解码 GIF 检查尺寸、无限循环和总时长，并打印帧数、时长及文件大小。仍需人工或通过图像查看工具检查预览和动画，尤其是菜单截断、重影、语言混用、鼠标遮挡和目录内容。不要仅凭命令退出成功判断教学内容正确。

失败时检查错误输出及素材路径。一次生成中英文不是事务操作：中文可能已生成而英文失败；失败时也可能留下部分产物。交付前应确认所需语言全部通过检查。

生成器不上传、不提交、不推送，也不修改网页。用于网站的最终 GIF 必须通过项目已有图片托管流程提供，不能直接把本机 `output/` 路径写进页面。接入前查看当前页面和游戏指引配置的约定，继续使用单个含 `{lang}` 的资源路径自动选取语言，不为中英文重复配置路径。发布是否成功须独立检查，不能由本地生成成功推断。
