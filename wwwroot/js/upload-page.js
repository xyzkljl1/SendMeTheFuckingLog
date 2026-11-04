(() => {
    "use strict";
    const config = JSON.parse(document.getElementById("page-configuration").textContent);
    const games = config.games;
    const routes = ["auto", "manual", "screenshots"];
    const preloadedImages = new Set(config.preloadedImages || []);
    const rules = globalThis.diagnosticRules;
    const recognizer = rules.createRecognizer(games, config.recognition);
    const $ = id => document.getElementById(id);
    const messages = {
        zh: {
            title: "上传日志给",
            screenshotsTitle: "上传截图", screenshotsPageTitle: "上传截图给", screenshotsHeading: "添加截图",
            dropScreenshots: "拖入截图，或按 Ctrl+V 粘贴", chooseScreenshots: "选择截图", removeScreenshot: "删除截图",
            screenshotHelp: "如何截图？",
            screenshotRegionStart: "方法 1：按",
            screenshotRegionHint: "+Shift+S，框选截图区域，再回到本页按 Ctrl+V 添加。",
            screenshotFullscreenStart: "方法 2：按",
            screenshotFullscreenHint: "+PrtSc（Print Screen）截取全屏，再回到本页按 Ctrl+V 添加；若无法粘贴，点击“选择截图”，从“图片/屏幕截图”中添加图片。",
            screenshotToolHint: "方法 3：在开始菜单搜索并打开“截图工具”，点击“新建”并框选截图区域，在截图预览中按 Ctrl+C，再回到本页按 Ctrl+V 添加。",
            screenshotMethod1Alt: "键盘位置示意：红圈标出需要同时按下的 Win、Shift 和 S。",
            screenshotMethod2Alt: "键盘位置示意：红圈标出需要同时按下的 Win 和 Print Screen。",
            screenshotMethod3Alt: "在开始菜单搜索截图工具，点击红圈中的截图工具应用。",
            invalidImage: "图片格式无效，请选择 PNG、JPG 或 WebP 图片。",
            invalidImageName: "图片文件名过长或含特殊字符，请重命名后添加。",
            tooManyScreenshots: "每次最多添加 {count} 张截图。", imagePreviewFailed: "图片无法预览，请删除后重新选择。",
            guideImageOpen: "点击放大", guideImageUnavailable: "演示图片暂时无法加载，请按文字指引操作。",
            automaticGuideAlt: "操作示例：从 Steam 打开游戏目录，鼠标框选全部内容（也可按 Ctrl+A），再拖入上传区域。",
            chooseRoute: "选择方式", autoTitle: "自动选择日志", manualTitle: "手动选择日志",
            folderHeading: "将整个游戏文件夹拖入", folderDescription: "Steam 中右键游戏 → 管理 → 浏览本地文件。",
            dragInstruction: "全选文件，拖入右侧区域。", dragInstructionBelow: "全选文件，拖入下方区域。",
            dropFolder: "拖到这", chooseFolder: "浏览文件",
            folderPrivacy: "不会上传整个游戏，只会上传日志", supported: "支持的游戏",
            identifying: "正在识别游戏… 文件不会发送。", droppedContents: "拖入的内容",
            detectedGame: "已识别游戏", multipleGames: "识别到多个游戏，请只拖入一个游戏的安装文件夹，或使用手动选择日志。",
            gameNotFound: "没有识别到支持的游戏。请从 Steam 打开本地文件后全选拖入，确保包含游戏程序，或使用手动上传。",
            duplicateFiles: "找到多份相同的诊断文件或游戏目录。请只拖入一份游戏安装的内容。",
            cancelScan: "取消查找", autoHelp: "请确认选择的是游戏安装文件夹。日志可能尚未生成，也可能位于所选目录之外。",
            viewManual: "查看手动查找指引 →", gameHeading: "选择游戏",
            uploadHeading: "上传日志", chooseGame: "请选择游戏", noGames: "暂未配置支持的游戏", guideHeading: "文件位置", demo: "演示", demoOnly: "此游戏仅用于展示控件，不提供文件上传。",
            dropFiles: "拖入日志文件", otherFiles: "其他文件", gameFolder: "游戏目录",
            primaryFile: "必需", secondaryNote: "（只有作者要求时才需要上传）",
            missingRequired: "请先添加必须上传的日志。", missingPrimary: "还需要添加 {names}。",
            chooseFiles: "选择文件", selectedFiles: "已选择的文件",
            nicknameHeading: "输入昵称：",
            nicknamePlaceholder: "例如N网Id",
            send: "发送", sent: "已保存",
            fileTooLarge: "{name} 为 {size}，超过单个文件限制 {limit}。",
            totalTooLarge: "文件合计 {size}，超过总大小限制 {limit}。",
            preparing: "正在检查文件…", uploading: "正在上传… {percent}%", saving: "正在发送…",
            success: "发送成功！", receiptHint: "告诉作者，你填写的昵称是：",
            invalidNickname: "请填写有效昵称（最多 60 个字符）。", noFiles: "请先选择要发送的文件。",
            tooLarge: "文件超出上传限制（单个 {file}，合计 {total}）。请刷新页面后重新选择。", oneGame: "一次只能提交一个游戏的文件，请选择具体的游戏目录。",
            siteBusy: "网站暂时繁忙，请稍后重试。已选择的文件和昵称仍然保留。",
            busy: "当前上传人数较多，请稍后再试。", storageFull: "暂时没有足够的存储空间，请稍后再试或联系作者。",
            storageFailed: "未能确认保存成功，请联系作者确认后再重试。", invalidSubmission: "提交内容不完整或不符合要求，请重新选择文件。",
            invalidFile: "文件不在此游戏支持的清单中，请按指引重新选择。", unsupportedGame: "暂不支持此游戏，请刷新页面重新选择。",
            incompleteUpload: "文件未完整接收或校验未通过，本次提交未保存，请重试。", refreshPage: "页面已过期，请刷新页面后重新选择文件。",
            unknownResult: "连接中断，无法确认是否保存成功。请先联系作者核实，避免重复提交。",
            readFailed: "无法读取所选文件，请重新选择后再试。", secureRequired: "请通过 HTTPS 打开本站后再上传文件。",
            copy: "复制目录", copied: "已复制", copyFailed: "无法复制，请选中路径手动复制。",
            scanning: "正在检查 {folder}… 文件不会发送。", found: "找到日志了！",
            notFound: "在 {folder} 中没有找到诊断文件。请按手动指引检查日志位置。", folderOnly: "请拖入整个游戏文件夹，或全选游戏文件夹里的内容一起拖入。",
            folderUnsupported: "此浏览器不支持拖入文件夹，请使用「浏览文件」按钮。",
            pickerUnsupported: "此浏览器不支持选择文件夹，请使用手动选择文件，或更新浏览器。",
            cancelled: "已取消查找。你可以重新选择文件夹。", scanError: "无法完整读取文件夹，请检查访问权限后重试，或使用手动选择文件。",
            tooMany: "所选目录包含过多项目，请选择具体的游戏安装文件夹。", summary: "{count} 个文件 · {size}",
            missing: "尚未找到：{names}。缺少文件也可以保留已有选择。",
            manualAdded: "已添加 {count} 个文件。", manualRejected: "该文件不是该游戏所需的日志。",
            manualPartiallyAdded: "部分文件不是该游戏所需的日志。",
            filesOnly: "请添加文件，不要添加文件夹。", pickGameFirst: "请先选择游戏。", selected: "已添加", notSelected: "尚未添加"
        },
        en: {
            title: "Upload logs to",
            screenshotsTitle: "Upload screenshots", screenshotsPageTitle: "Upload screenshots to", screenshotsHeading: "Add screenshots",
            dropScreenshots: "Drop screenshots here, or paste with Ctrl+V", chooseScreenshots: "Choose screenshots", removeScreenshot: "Remove screenshot",
            screenshotHelp: "How do I take a screenshot?",
            screenshotRegionStart: "Method 1: Press",
            screenshotRegionHint: "+Shift+S, drag to select an area, then return here and press Ctrl+V to add it.",
            screenshotFullscreenStart: "Method 2: Press",
            screenshotFullscreenHint: "+PrtSc (Print Screen) to capture the full screen, then return here and press Ctrl+V to add it. If pasting does not work, click Choose screenshots and add the image from Pictures/Screenshots.",
            screenshotToolHint: "Method 3: Search for Snipping Tool in the Start menu and open it. Click New, drag to select an area, press Ctrl+C in the screenshot preview, then return here and press Ctrl+V to add it.",
            screenshotMethod1Alt: "Keyboard positions: red circles mark Win, Shift and S. Press these keys together.",
            screenshotMethod2Alt: "Keyboard positions: red circles mark Win and Print Screen. Press these keys together.",
            screenshotMethod3Alt: "English illustration: search for Snipping Tool in Start, then click the circled Snipping Tool app.",
            invalidImage: "Invalid image format. Choose PNG, JPG or WebP images.",
            invalidImageName: "The image filename is too long or contains special characters. Rename it and try again.",
            tooManyScreenshots: "Add up to {count} screenshots per upload.", imagePreviewFailed: "Cannot preview this image. Remove it and choose again.",
            guideImageOpen: "View full size", guideImageUnavailable: "The guide image could not load. Please follow the written steps.",
            automaticGuideAlt: "Example: open your game's folder from Steam, drag a selection box around all items (or press Ctrl+A), then drag them into the upload area.",
            chooseRoute: "Choose a method", autoTitle: "Select logs automatically", manualTitle: "Select logs manually",
            folderHeading: "Drag in the entire game folder", folderDescription: "In Steam, right-click the game → Manage → Browse local files.",
            dragInstruction: "Select all files and drag them into the area on the right.", dragInstructionBelow: "Select all files and drag them into the area below.",
            dropFolder: "Drop here", chooseFolder: "Browse files",
            folderPrivacy: "The whole game won't be uploaded, only logs", supported: "Supported games",
            identifying: "Identifying your game… Nothing will be sent.", droppedContents: "the dropped items",
            detectedGame: "Detected game", multipleGames: "Multiple games were found. Drop the installation folder for just one game, or select logs manually.",
            gameNotFound: "No supported game was identified. Open local files from Steam and drag all contents, including the game executable, or choose files manually.",
            duplicateFiles: "Multiple copies of a diagnostic file or game folder were found. Drop the contents of just one game installation.",
            cancelScan: "Cancel search", autoHelp: "Check that you selected the game’s installation folder. Logs may not exist yet, or may be outside this folder.",
            viewManual: "Show the manual guide →", gameHeading: "Choose your game",
            uploadHeading: "Upload logs", chooseGame: "Choose a game", noGames: "No supported games configured yet", guideHeading: "File locations", demo: "Demo", demoOnly: "This game is a selection preview. File uploads are unavailable.",
            dropFiles: "Drop log files here", otherFiles: "Other files", gameFolder: "Game folder",
            primaryFile: "Required", secondaryNote: "(Only upload if the author requests it)",
            missingRequired: "Please add the required logs first.", missingPrimary: "Please also add {names}.",
            chooseFiles: "Choose files", selectedFiles: "Selected files",
            nicknameHeading: "Nickname:",
            nicknamePlaceholder: "e.g. Nexus ID",
            send: "Send", sent: "Saved",
            fileTooLarge: "{name} is {size}, exceeding the {limit} per-file limit.",
            totalTooLarge: "Files total {size}, exceeding the {limit} total limit.",
            preparing: "Checking files…", uploading: "Uploading… {percent}%", saving: "Sending…",
            success: "Files sent!", receiptHint: "Tell the author the nickname you used:",
            invalidNickname: "Enter a valid nickname (up to 60 characters).", noFiles: "Choose files to send first.",
            tooLarge: "Upload limit exceeded ({file} per file, {total} total). Refresh the page and choose again.", oneGame: "Submit files for one game at a time. Choose a specific game folder.",
            siteBusy: "The website is busy. Please try again later. Your selected files and nickname have been kept.",
            busy: "Too many uploads are in progress. Please try again shortly.", storageFull: "There is not enough storage available. Try later or contact the author.",
            storageFailed: "We could not confirm the files were saved. Check with the author before retrying.", invalidSubmission: "The submission is incomplete or invalid. Please select your files again.",
            invalidFile: "This file is not on the game's supported list. Follow the guide and choose again.", unsupportedGame: "This game is not supported. Refresh the page and choose again.",
            incompleteUpload: "The files were incomplete or failed verification. This submission was not saved. Please retry.", refreshPage: "This page has expired. Refresh it and select your files again.",
            unknownResult: "The connection was interrupted. We cannot confirm whether your files were saved. Check with the author before retrying to avoid duplicates.",
            readFailed: "Could not read the selected files. Please select them again.", secureRequired: "Open this website over HTTPS to upload files.",
            copy: "Copy folder", copied: "Copied", copyFailed: "Could not copy. Select the path and copy it manually.",
            scanning: "Checking {folder}… Nothing will be sent.", found: "Found your logs!",
            notFound: "No diagnostic files were found in {folder}. Check the locations in the manual guide.", folderOnly: "Drop the whole game folder, or select and drop all of its contents together.",
            folderUnsupported: "This browser cannot read dropped folders. Use the Browse files button.",
            pickerUnsupported: "This browser cannot select folders. Choose files manually or update your browser.",
            cancelled: "Search cancelled. You can choose another folder.", scanError: "The folder could not be fully read. Check access permissions and try again, or choose files manually.",
            tooMany: "This folder contains too many items. Choose the specific game installation folder.", summary: "{count} files · {size}",
            missing: "Not found yet: {names}. You can keep the files you already selected.",
            manualAdded: "Added {count} files.", manualRejected: "This file is not a log required for this game.",
            manualPartiallyAdded: "Some files are not logs required for this game.",
            filesOnly: "Please add files, not folders.", pickGameFirst: "Please choose a game first.", selected: "Added", notSelected: "Not added"
        }
    };
    const languageStorageKey = "mod-log-helper.language";
    function readPreference(name) {
        try { return localStorage.getItem("mod-log-helper." + name) || ""; }
        catch { return ""; }
    }
    function savePreference(name, value) {
        try {
            if (value) localStorage.setItem("mod-log-helper." + name, value);
            else localStorage.removeItem("mod-log-helper." + name);
        } catch { /* The page remains usable when browser storage is unavailable. */ }
    }
    function initialLanguage() {
        try {
            const saved = localStorage.getItem(languageStorageKey);
            if (saved === "zh" || saved === "en") return saved;
        } catch {
            // Language selection still works when browser storage is unavailable.
        }
        const preferences = navigator.languages?.length ? navigator.languages : [navigator.language];
        for (const preference of preferences) {
            const language = (preference || "").toLowerCase().split("-")[0];
            if (language === "zh" || language === "en") return language;
        }
        return "en";
    }
    const savedGame = readPreference("game");
    const savedNickname = readPreference("nickname");
    $("nickname").value = savedNickname.length <= 60 && !/[\u0000-\u001f\u007f-\u009f]/.test(savedNickname) ? savedNickname : "";
    const state = {
        language: initialLanguage(),
        route: routes.includes(readPreference("route")) ? readPreference("route") : "auto",
        manualGame: games.some(game => game.id === savedGame) ? savedGame : "",
        autoFiles: [], manualFiles: [], autoStatus: null, manualStatus: null,
        screenshotsFiles: [], screenshotsStatus: null, nextScreenshot: 1,
        scanning: false, scanToken: 0, autoHelp: false,
        recognizedGame: null,
        sending: false, submitted: false, uploadStatus: null, receipt: null, receiptNickname: "", progress: null
    };
    const local = value => value[state.language] || value.en;
    const text = (key, args = {}) => (messages[state.language][key] || key).replace(/\{(\w+)\}/g, (_, name) => args[name] ?? "");
    const sizeText = bytes => bytes < 1024 ? `${bytes} B` : bytes < 1048576 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1048576).toFixed(1)} MB`;
    const make = (tag, className, value) => { const element = document.createElement(tag); element.className = className; if (value !== undefined) element.textContent = value; return element; };
    const currentFiles = () => state[state.route + "Files"];
    const routeStatus = route => $(route === "auto" ? "folder-status" : route + "-status");
    function updateGameNames(container, game) {
        const primary = container.querySelector(".game-name-primary");
        const secondary = container.querySelector(".game-name-secondary");
        primary.textContent = local(game.name);
        secondary.textContent = state.language === "zh" ? game.name.en : "";
        secondary.hidden = state.language !== "zh";
        primary.lang = state.language === "zh" ? "zh-CN" : "en";
        secondary.lang = "en";
    }
    function appendGameNames(container, game) {
        container.append(make("strong", "game-name-primary"), make("span", "game-name-secondary"));
        updateGameNames(container, game);
    }
    function renderGameCards(containerId, choices, group, selected, onChange) {
        const container = $(containerId);
        const cardsKey = JSON.stringify(choices.map(({ game, value }) => [game.id, game.image, value]));
        if (container.dataset.cardsKey === cardsKey) {
            // Keep decoded images when switching routes/languages or updating selection.
            for (const radio of container.querySelectorAll("input[type=radio]")) {
                radio.checked = radio.value === selected;
                radio.closest(".game-card").classList.toggle("selected", radio.checked);
            }
            container.querySelectorAll(".game-card-names").forEach((names, index) => updateGameNames(names, choices[index].game));
            for (const badge of container.querySelectorAll(".demo-badge")) badge.textContent = text("demo");
            if (!choices.length) container.querySelector(".hint").textContent = text("noGames");
            return;
        }
        container.dataset.cardsKey = cardsKey;
        container.replaceChildren();
        for (const { game, value } of choices) {
            const card = make("label", "game-card");
            const radio = make("input", "game-radio");
            radio.type = "radio"; radio.name = group; radio.value = value; radio.checked = value === selected;
            card.classList.toggle("selected", radio.checked);
            const cover = make("span", "game-cover");
            cover.setAttribute("aria-hidden", "true");
            cover.append(make("span", "game-cover-fallback", game.name.en.split(/\s+/).map(word => word[0]).join("").slice(0, 3)));
            if (game.image) {
                const image = make("img", "");
                image.alt = ""; image.width = 480; image.height = 270;
                image.loading = preloadedImages.has(game.image) ? "eager" : "lazy";
                image.decoding = "async";
                image.referrerPolicy = "no-referrer";
                image.addEventListener("error", () => { image.hidden = true; });
                image.src = game.image; cover.append(image);
            }
            const names = make("span", "game-card-names");
            appendGameNames(names, game);
            if (game.isDemo) names.append(make("span", "demo-badge", text("demo")));
            card.append(radio, cover, names); container.append(card);
            radio.addEventListener("change", () => {
                if (!radio.checked || state.sending) return;
                for (const other of container.querySelectorAll("input[type=radio]")) {
                    other.checked = other === radio;
                    other.closest(".game-card").classList.toggle("selected", other.checked);
                }
                onChange(value);
            });
        }
        if (!choices.length) container.append(make("p", "hint", text("noGames")));
    }
    let attentionObserver = null;
    const attentionTimers = new Set();
    function clearAttention() {
        attentionObserver?.disconnect();
        attentionObserver = null;
        for (const timer of attentionTimers) clearTimeout(timer);
        attentionTimers.clear();
        for (const id of ["nickname-target", "send-target"]) $(id).classList.remove("attention-active");
    }
    function showNextStepHints() {
        clearAttention();
        const highlight = target => {
            target.classList.add("attention-active");
            const timer = setTimeout(() => {
                target.classList.remove("attention-active");
                attentionTimers.delete(timer);
            }, 6000);
            attentionTimers.add(timer);
        };
        // Bring both next steps into view before starting the one-off arrows.
        $("upload-next-steps").scrollIntoView({ behavior: "instant", block: "nearest" });
        if (typeof IntersectionObserver === "undefined") {
            for (const id of ["nickname-target", "send-target"]) highlight($(id));
            return;
        }
        attentionObserver = new IntersectionObserver(entries => {
            for (const entry of entries) {
                if (!entry.isIntersecting || entry.intersectionRatio < 0.75) continue;
                attentionObserver.unobserve(entry.target);
                highlight(entry.target);
            }
        }, { threshold: 0.75 });
        for (const id of ["nickname-target", "send-target"]) attentionObserver.observe($(id));
    }
    function sizeErrors(files) {
        const errors = files.filter(record => record.file.size > config.maxFileBytes).map(record => text("fileTooLarge", {
            name: record.file.name, size: sizeText(record.file.size), limit: sizeText(config.maxFileBytes)
        }));
        const total = files.reduce((sum, record) => sum + record.file.size, 0);
        if (total > config.maxSubmissionBytes) errors.push(text("totalTooLarge", { size: sizeText(total), limit: sizeText(config.maxSubmissionBytes) }));
        return errors;
    }
    function renderSizeErrors() {
        for (const route of routes) {
            const errors = sizeErrors(state[route + "Files"]);
            if (route === state.route && state.uploadStatus?.key === "tooLarge" && !errors.length)
                errors.push(text("tooLarge", { file: sizeText(config.maxFileBytes), total: sizeText(config.maxSubmissionBytes) }));
            const element = $(route + "-size-error");
            element.textContent = errors.join("\n");
            element.hidden = !errors.length;
            // A success message must not compete with an actionable size error.
            routeStatus(route).hidden = errors.length > 0 && !state[route + "Status"]?.error;
        }
    }
    function validationError() {
        const files = currentFiles();
        const nickname = $("nickname").value.trim();
        if (state.route === "manual" && games.find(game => game.id === state.manualGame)?.isDemo) return "demoOnly";
        if (state.route === "manual") {
            const game = games.find(game => game.id === state.manualGame);
            if (game?.files.some(item => item.isPrimary && !files.some(record => record.item.id === item.id))) return "missingRequired";
        }
        if (!files.length) return "noFiles";
        if (state.route !== "screenshots" && new Set(files.map(record => record.game.id)).size !== 1) return "oneGame";
        if (sizeErrors(files).length) return "tooLarge";
        if (!nickname || nickname.length > 60 || /[\u0000-\u001f\u007f-\u009f]/.test(nickname)) return "invalidNickname";
        return null;
    }
    function renderUpload() {
        const invalid = validationError();
        const button = $("send-files");
        button.textContent = state.sending ? text(state.progress === null ? "preparing" : state.progress === 100 ? "saving" : "uploading", { percent: state.progress || 0 })
            : state.submitted ? text("sent") : text("send");
        // Require a nickname before sending; other validation errors can still explain how to fix the selection.
        button.disabled = !$("nickname").value.trim() || state.sending || state.scanning || state.submitted || invalid === "noFiles" || invalid === "tooLarge" || invalid === "demoOnly" || invalid === "missingRequired" || (state.route === "auto" && !state.recognizedGame);
        renderSizeErrors();
        const status = state.uploadStatus;
        $("upload-status").textContent = status ? (["success", "tooLarge"].includes(status.key) ? "" : text(status.key, status.args)) : (invalid === "oneGame" ? text(invalid) : "");
        $("upload-status").classList.toggle("error", Boolean(status ? status.error : invalid === "oneGame"));
        $("upload-progress").hidden = !state.sending || state.progress === null;
        $("upload-progress").value = state.progress || 0;
        $("upload-progress").setAttribute("aria-label", text("uploading", { percent: state.progress || 0 }));
        $("upload-receipt").hidden = !state.receipt;
        $("receipt-nickname").textContent = state.receiptNickname;
        $("nickname").disabled = state.sending;
        $("manual-game-choice").disabled = state.sending || !games.length;
        $("choose-files").disabled = state.sending;
        $("choose-folder").disabled = state.sending || state.scanning || !games.length;
        $("route-auto").disabled = state.sending;
        $("route-manual").disabled = state.sending;
        $("route-screenshots").disabled = state.sending;
        $("choose-screenshots").disabled = state.sending;
        $("screenshots-input").disabled = state.sending;
        $("screenshot-files").querySelectorAll("button").forEach(button => { button.disabled = state.sending; });
    }
    function resetUpload() {
        state.submitted = false; state.uploadStatus = null; state.receipt = null; state.receiptNickname = ""; state.progress = null;
    }
    function uploadStatus(key, args = {}, error = false) {
        state.uploadStatus = { key, args, error }; renderUpload();
    }
    function setStatus(route, key, args = {}, error = false) {
        state[route + "Status"] = { key, args, error };
        renderStatus();
    }
    function renderStatus() {
        for (const route of routes) {
            const status = state[route + "Status"];
            const element = routeStatus(route);
            element.textContent = status && status.key !== "manualAdded" ? text(status.key, { ...status.args, folder: status.args.folder === "" ? text("droppedContents") : status.args.folder }) : "";
            element.classList.toggle("error", Boolean(status && status.error));
            element.classList.toggle("recognition-success", route === "auto" && status?.key === "found" && !status.error);
        }
        $("auto-help").hidden = !state.autoHelp;
        $("cancel-scan").hidden = !state.scanning;
        $("choose-folder").disabled = state.scanning || games.length === 0;
        $("folder-drop").setAttribute("aria-busy", String(state.scanning));
        renderUpload();
    }
    function renderDetectedGame() {
        const game = state.route === "auto" ? state.recognizedGame : null;
        const container = $("detected-game-info");
        container.hidden = !game;
        const key = JSON.stringify([game?.id, game?.image, state.language]);
        if (container.dataset.gameKey === key) return;
        container.dataset.gameKey = key;
        container.replaceChildren();
        if (!game) return;
        if (game.image) {
            const image = make("img", "detected-game-cover");
            image.alt = ""; image.width = 128; image.height = 72;
            image.decoding = "async"; image.referrerPolicy = "no-referrer";
            image.addEventListener("error", () => { image.style.visibility = "hidden"; });
            image.src = game.image; container.append(image);
        }
        const names = make("div", "detected-game-names");
        names.append(make("span", "hint", text("detectedGame")));
        appendGameNames(names, game);
        container.append(names);
    }
    function renderGuideImages(container, images, description) {
        const urls = images.map(local);
        const key = JSON.stringify([state.language, urls]);
        container.hidden = !urls.length;
        if (container.dataset.imagesKey === key) return;
        container.dataset.imagesKey = key;
        container.replaceChildren();
        for (const url of urls) {
            const figure = make("figure", "guide-image");
            const frame = make("span", "guide-image-frame");
            const link = make("a", "guide-image-link");
            link.href = url; link.target = "_blank"; link.rel = "noopener noreferrer";
            link.setAttribute("aria-label", `${description} — ${text("guideImageOpen")}`);
            const image = make("img", "");
            image.alt = description; image.loading = "lazy"; image.decoding = "async";
            image.referrerPolicy = "no-referrer";
            const error = make("span", "guide-image-error", text("guideImageUnavailable"));
            error.hidden = true;
            image.addEventListener("error", () => { image.hidden = true; error.hidden = false; link.removeAttribute("href"); });
            image.src = url; frame.append(image, error); link.append(frame);
            link.title = text("guideImageOpen");
            figure.append(link); container.append(figure);
        }
    }
    function renderScreenshotGuides() {
        if ($("screenshot-help").hidden) return;
        for (let method = 1; method <= 3; method++) {
            const image = config.screenshotGuideImages?.[method - 1];
            renderGuideImages($("screenshot-guide-" + method), image ? [image] : [], text("screenshotMethod" + method + "Alt"));
        }
    }
    function fileGroups(files) {
        const groups = new Map();
        for (const item of files) {
            for (const path of item.paths) {
                const parts = path.replace(/\\/g, "/").split("/");
                const name = parts.pop();
                const directory = parts.join("\\");
                if (!groups.has(directory)) groups.set(directory, []);
                groups.get(directory).push({ item, name });
            }
        }
        return groups;
    }
    function renderFileGroups(container, groups, sharedDirectory = null) {
        container.replaceChildren();
        for (const [directory, files] of groups) {
            const group = make("div", "file-directory-group");
            if (directory !== sharedDirectory) {
                const pathRow = make("div", "path-row");
                pathRow.append(make(directory ? "code" : "span", "directory-name", directory || text("gameFolder")));
                if (directory) {
                    const button = make("button", "copy-button", text("copy"));
                    button.type = "button";
                    button.setAttribute("aria-label", `${text("copy")}: ${directory}`);
                    button.addEventListener("click", async () => {
                        try { await navigator.clipboard.writeText(directory); button.textContent = text("copied"); }
                        catch { setStatus("manual", "copyFailed", {}, true); }
                    });
                    pathRow.append(button);
                }
                group.append(pathRow);
            }
            for (const { item, name } of files) {
                const row = make("div", "expected-file-heading");
                row.append(make("strong", "", name));
                if (item.isPrimary) row.append(make("span", "kind-badge primary-file-badge", text("primaryFile")));
                if (state.manualFiles.some(file => file.item.id === item.id)) row.append(make("span", "kind-badge", text("selected")));
                group.append(row);
            }
            container.append(group);
        }
    }
    function renderGuide() {
        const game = games.find(game => game.id === state.manualGame);
        $("file-drop").hidden = !game || Boolean(game.isDemo);
        $("game-guide").hidden = !game;
        $("expected-files").replaceChildren();
        const steps = $("guide-steps");
        const optionalSteps = $("optional-guide-steps");
        const optionalPanel = $("optional-files");
        optionalPanel.hidden = !game || (!game.files.some(item => !item.isPrimary) && !game.instructions.some(step => step.isOptional));
        if (optionalPanel.dataset.game !== (game?.id || "")) {
            optionalPanel.open = false;
            optionalPanel.dataset.game = game?.id || "";
        }
        const guideKey = JSON.stringify([game?.id, state.language]);
        if (steps.dataset.guideKey !== guideKey) {
            steps.dataset.guideKey = guideKey;
            steps.replaceChildren();
            optionalSteps.replaceChildren();
            for (const instruction of game?.instructions || []) {
                const step = make("li", "guide-step");
                step.append(make("p", "guide-step-text", local(instruction)));
                const media = make("div", "guide-images");
                renderGuideImages(media, instruction.imageUrls || [], local(instruction));
                step.append(media); (instruction.isOptional ? optionalSteps : steps).append(step);
            }
            if (game?.files.some(item => !item.isPrimary) && !game.instructions.some(step => step.isOptional))
                optionalSteps.append(make("li", "guide-step", text("secondaryNote")));
        }
        if (!game) return;
        const primary = fileGroups(game.files.filter(item => item.isPrimary));
        const secondary = fileGroups(game.files.filter(item => !item.isPrimary));
        renderFileGroups($("expected-files"), primary);
        // A single shared folder is already shown immediately above the disclosure.
        const shared = primary.size === 1 && secondary.size === 1 ? primary.keys().next().value : null;
        renderFileGroups($("optional-file-paths"), secondary, shared);
    }
    function renderSelection() {
        if (state.route === "screenshots") {
            $("selection-section").hidden = true;
            renderScreenshots(); renderUpload(); return;
        }
        const files = currentFiles();
        const selection = $("selection-section");
        const slot = $(state.route === "auto" ? "auto-selection-slot" : "manual-selection-slot");
        if (selection.parentElement !== slot) slot.append(selection);
        $("selection-section").hidden = !files.length && !(state.route === "auto" && state.recognizedGame);
        renderDetectedGame();
        $("selected-files").replaceChildren();
        $("selection-summary").textContent = text("summary", { count: files.length, size: sizeText(files.reduce((total, record) => total + record.file.size, 0)) });
        for (const record of files) {
            const row = make("li", "");
            const info = make("div", "file-info");
            info.append(make("strong", "", record.file.name));
            row.append(info, make("span", "file-size", sizeText(record.file.size)));
            $("selected-files").append(row);
        }
        const relevant = state.route === "manual" ? games.filter(game => game.id === state.manualGame) : games.filter(game => game.id === state.recognizedGame?.id);
        const missing = relevant.flatMap(game => game.files.filter(item => (state.route !== "manual" || item.isPrimary) && !files.some(file => file.game.id === game.id && file.item.id === item.id)).map(item => state.route === "manual"
            ? [...new Set(item.paths.map(path => path.replace(/\\/g, "/").split("/").pop()))].join(" / ")
            : local(item.name)));
        $("missing-files").textContent = missing.length ? text(state.route === "manual" ? "missingPrimary" : "missing", { names: missing.join(state.language === "zh" ? "、" : ", ") }) : "";
        renderUpload();
    }
    function render() {
        document.documentElement.lang = state.language === "zh" ? "zh-CN" : "en";
        document.title = (state.language === "zh" ? "发送诊断文件" : "Share diagnostic files") + " · Mod Log Helper";
        document.querySelectorAll("[data-i18n]").forEach(element => { element.textContent = text(element.dataset.i18n); });
        document.querySelectorAll("[data-language]").forEach(button => button.setAttribute("aria-pressed", String(button.dataset.language === state.language)));
        $("status-link").href = "/status?lang=" + state.language;
        $("files-link").href = "/files?lang=" + state.language;
        $("author-name").textContent = config.authorName;
        $("upload-title").textContent = text(state.route === "screenshots" ? "screenshotsPageTitle" : "title");
        $("nickname").placeholder = text("nicknamePlaceholder");
        $("nickname-step-number").textContent = state.route === "manual" ? "3" : "2";
        renderDragInstruction();
        renderGuideImages($("automatic-guide"), config.automaticGuideImages || [], text("automaticGuideAlt"));
        renderScreenshotGuides();
        $("supported-games-sidebar").hidden = state.route !== "auto";
        const supported = games.filter(game => !game.isDemo);
        $("supported-games").replaceChildren();
        for (const game of supported) {
            const item = make("li", "");
            item.append(make("strong", "game-name-primary", local(game.name)));
            $("supported-games").append(item);
        }
        if (!supported.length) $("supported-games").append(make("li", "", text("noGames")));
        renderGameCards("game-cards", games.map(game => ({ game, value: game.id })), "manual-game", state.manualGame, value => {
            clearAttention(); resetUpload(); state.manualGame = value; state.manualFiles = []; state.manualStatus = null;
            savePreference("game", value);
            renderGuide(); renderSelection(); renderStatus();
        });
        for (const route of routes) {
            $("route-" + route).classList.toggle("selected", state.route === route);
            $("route-" + route).setAttribute("aria-pressed", String(state.route === route));
            $(route + "-panel").hidden = state.route !== route;
        }
        renderGuide(); renderSelection(); renderStatus();
    }
    function changeRoute(route) {
        if (state.sending) return;
        if (state.route !== route) {
            clearAttention(); resetUpload();
            if (state.scanning) clearAutomatic();
        }
        state.route = route; savePreference("route", route); render();
    }
    document.querySelectorAll("[data-language]").forEach(button => button.addEventListener("click", () => {
        state.language = button.dataset.language;
        try {
            localStorage.setItem(languageStorageKey, state.language);
        } catch {
            // Keep the manual choice for this page even if it cannot be persisted.
        }
        render();
    }));
    $("route-auto").addEventListener("click", () => changeRoute("auto"));
    $("route-manual").addEventListener("click", () => changeRoute("manual"));
    $("route-screenshots").addEventListener("click", () => changeRoute("screenshots"));
    $("switch-manual").addEventListener("click", () => {
        const supported = games.filter(game => !game.isDemo);
        if (!state.manualGame && supported.length === 1) state.manualGame = supported[0].id;
        savePreference("game", state.manualGame);
        changeRoute("manual"); $("game-cards").querySelector("input:checked, input")?.focus();
    });
    $("choose-files").addEventListener("click", () => $("file-input").click());
    $("choose-folder").addEventListener("click", () => {
        if (!("webkitdirectory" in $("folder-input"))) { setStatus("auto", "pickerUnsupported", {}, true); return; }
        $("folder-input").click();
    });
    function addManualFiles(files) {
        if (state.sending) return;
        resetUpload();
        const game = games.find(game => game.id === state.manualGame);
        if (!game) { setStatus("manual", "pickGameFirst", {}, true); return; }
        if (game.isDemo) { setStatus("manual", "demoOnly", {}, true); return; }
        const wasReady = state.manualFiles.length > 0 && !sizeErrors(state.manualFiles).length && validationError() !== "missingRequired";
        let count = 0, ignored = 0;
        for (const file of files) {
            const item = rules.matchManualFile(game, file.name);
            if (!item) { ignored++; continue; }
            state.manualFiles = state.manualFiles.filter(record => record.item.id !== item.id);
            state.manualFiles.push({ game, item, file }); count++;
        }
        const message = ignored ? (count > 0 ? "manualPartiallyAdded" : "manualRejected") : "manualAdded";
        setStatus("manual", message, { count }, ignored > 0);
        renderGuide(); renderSelection();
        if (count > 0 && !wasReady && !sizeErrors(state.manualFiles).length && validationError() !== "missingRequired") showNextStepHints();
    }
    $("file-input").addEventListener("change", event => { addManualFiles(Array.from(event.target.files)); event.target.value = ""; });

    function renderScreenshots() {
        const list = $("screenshot-files");
        const ids = new Set(state.screenshotsFiles.map(record => record.item.id));
        for (const row of Array.from(list.children)) if (!ids.has(row.dataset.id)) row.remove();
        for (const record of state.screenshotsFiles) {
            let row = Array.from(list.children).find(row => row.dataset.id === record.item.id);
            if (!row) {
                row = make("li", "screenshot-card"); row.dataset.id = record.item.id;
                const link = make("a", "screenshot-preview");
                link.href = record.url; link.target = "_blank"; link.rel = "noopener";
                const image = make("img", ""); image.alt = record.file.name; image.src = record.url;
                image.loading = "lazy"; image.decoding = "async";
                image.addEventListener("error", () => { image.hidden = true; row.classList.add("preview-failed"); });
                link.append(image);
                const failed = make("span", "preview-error");
                const remove = make("button", "screenshot-remove"); remove.type = "button";
                remove.addEventListener("click", () => {
                    if (state.sending) return;
                    URL.revokeObjectURL(record.url);
                    state.screenshotsFiles = state.screenshotsFiles.filter(item => item !== record);
                    state.screenshotsStatus = null;
                    clearAttention(); resetUpload(); renderSelection(); renderStatus();
                });
                row.append(link, failed, make("strong", "screenshot-name", record.file.name), make("span", "hint", sizeText(record.file.size)), remove);
                list.append(row);
            }
            row.querySelector("button").textContent = text("removeScreenshot");
            row.querySelector("button").setAttribute("aria-label", text("removeScreenshot") + ": " + record.file.name);
            row.querySelector(".preview-error").textContent = text("imagePreviewFailed");
        }
        $("screenshots-summary").textContent = state.screenshotsFiles.length ? text("summary", {
            count: state.screenshotsFiles.length, size: sizeText(state.screenshotsFiles.reduce((total, record) => total + record.file.size, 0))
        }) : "";
    }
    function addScreenshots(files) {
        if (state.sending || state.route !== "screenshots" || !files.length) return;
        resetUpload();
        const wasEmpty = !state.screenshotsFiles.length;
        const errors = new Set();
        for (const file of files) {
            if (!/\.(png|jpe?g|webp)$/i.test(file.name)) { errors.add("invalidImage"); continue; }
            if (file.name.length > 120 || /[\\/:*?"<>|\u0000-\u001f\u007f-\u009f]/.test(file.name)) { errors.add("invalidImageName"); continue; }
            if (state.screenshotsFiles.length >= config.maxScreenshotFiles) { errors.add("tooManyScreenshots"); continue; }
            state.screenshotsFiles.push({ item: { id: "screenshot-" + state.nextScreenshot++ }, file, url: URL.createObjectURL(file) });
        }
        state.screenshotsStatus = errors.size ? { key: [...errors][0], args: { count: config.maxScreenshotFiles }, error: true } : null;
        renderSelection(); renderStatus();
        if (wasEmpty && state.screenshotsFiles.length && !sizeErrors(state.screenshotsFiles).length) showNextStepHints();
    }
    $("toggle-screenshot-help").addEventListener("click", () => {
        const help = $("screenshot-help");
        help.hidden = !help.hidden;
        $("toggle-screenshot-help").setAttribute("aria-expanded", String(!help.hidden));
        renderScreenshotGuides();
    });
    $("choose-screenshots").addEventListener("click", () => $("screenshots-input").click());
    $("screenshots-input").addEventListener("change", event => { addScreenshots(Array.from(event.target.files)); event.target.value = ""; });
    document.addEventListener("paste", event => {
        if (state.route !== "screenshots" || state.sending) return;
        const files = Array.from(event.clipboardData?.files || []);
        if (!files.length) return;
        event.preventDefault();
        addScreenshots(files);
    });
    function clearAutomatic() {
        clearAttention();
        state.scanToken++;
        state.scanning = false;
        state.recognizedGame = null; state.autoFiles = []; state.autoHelp = false; state.autoStatus = null;
    }
    async function identifySource(source) {
        resetUpload();
        clearAutomatic();
        const token = state.scanToken;
        state.scanning = true;
        setStatus("auto", "identifying"); renderSelection();
        try {
            const candidates = await recognizer.identify(source, () => token !== state.scanToken);
            if (token !== state.scanToken) return;
            if (new Set(candidates.map(candidate => candidate.game.id)).size !== candidates.length) throw new Error("duplicateFiles");
            if (candidates.length > 1) throw new Error("multipleGames");
            if (!candidates.length) {
                state.scanning = false; state.autoHelp = true;
                setStatus("auto", "gameNotFound", {}, true); renderSelection();
                return;
            }
            const candidate = candidates[0];
            setStatus("auto", "scanning", { folder: source.folder });
            const records = await rules.collectFiles(candidate, source, () => token !== state.scanToken);
            if (token !== state.scanToken) return;
            state.scanning = false; state.recognizedGame = candidate.game; state.autoFiles = records;
            if (state.manualGame !== candidate.game.id) {
                state.manualGame = candidate.game.id; state.manualFiles = []; state.manualStatus = null;
            }
            savePreference("game", candidate.game.id);
            state.autoHelp = !records.length;
            setStatus("auto", records.length ? "found" : "notFound", { folder: source.folder, count: records.length }, !records.length);
            renderSelection();
            if (records.length && !sizeErrors(records).length) showNextStepHints();
        } catch (error) { failScan(token, error); }
    }
    function failScan(token, error) {
        if (token !== state.scanToken) return;
        state.scanning = false; state.autoHelp = true; state.autoFiles = []; state.recognizedGame = null;
        setStatus("auto", ["tooMany", "duplicateFiles", "folderOnly", "multipleGames"].includes(error.message) ? error.message : "scanError", {}, true);
        renderSelection();
    }
    $("cancel-scan").addEventListener("click", () => {
        clearAutomatic(); resetUpload(); setStatus("auto", "cancelled"); renderSelection();
    });
    $("folder-input").addEventListener("change", event => {
        if (state.sending || state.scanning) return;
        const files = Array.from(event.target.files); event.target.value = "";
        if (!files.length) return;
        try { identifySource(rules.pickedSource(files)); }
        catch (error) {
            clearAutomatic(); resetUpload(); failScan(state.scanToken, error);
        }
    });
    function installDropZone(id, onDrop) {
        const zone = $(id);
        zone.addEventListener("dragover", event => { event.preventDefault(); zone.classList.add("dragging"); });
        zone.addEventListener("dragleave", event => { if (!zone.contains(event.relatedTarget)) zone.classList.remove("dragging"); });
        zone.addEventListener("drop", event => { event.preventDefault(); zone.classList.remove("dragging"); onDrop(event.dataTransfer); });
    }
    installDropZone("folder-drop", transfer => {
        if (state.sending || state.scanning) return;
        if (!games.length) { setStatus("auto", "noGames", {}, true); return; }
        clearAutomatic(); resetUpload(); renderSelection(); renderStatus();
        const items = Array.from(transfer.items).filter(item => item.kind === "file");
        // Capture all entries before leaving the drop event; browser drop data is short-lived.
        const entries = [];
        for (const item of items) {
            const getter = item.getAsEntry || item.webkitGetAsEntry;
            if (!getter) { setStatus("auto", "folderUnsupported", {}, true); return; }
            const entry = getter.call(item);
            if (!entry) { setStatus("auto", "folderOnly", {}, true); return; }
            entries.push(entry);
        }
        if (!entries.length) { setStatus("auto", "folderOnly", {}, true); return; }
        identifySource(rules.droppedSource(entries));
    });
    installDropZone("file-drop", transfer => {
        if (state.sending) return;
        const entries = Array.from(transfer.items).filter(item => item.kind === "file").map(item => {
            const getter = item.getAsEntry || item.webkitGetAsEntry; return getter ? getter.call(item) : null;
        });
        if (entries.some(entry => entry && entry.isDirectory)) { setStatus("manual", "filesOnly", {}, true); return; }
        addManualFiles(Array.from(transfer.files));
    });
    installDropZone("screenshots-drop", transfer => {
        if (state.sending) return;
        const directory = Array.from(transfer.items).some(item => {
            const getter = item.getAsEntry || item.webkitGetAsEntry;
            return getter?.call(item)?.isDirectory;
        });
        if (directory) { setStatus("screenshots", "filesOnly", {}, true); return; }
        addScreenshots(Array.from(transfer.files));
    });
    // Prevent accidental navigation if files are dropped outside the designated zones.
    document.addEventListener("dragover", event => event.preventDefault());
    document.addEventListener("drop", event => event.preventDefault());
    $("nickname").addEventListener("input", () => {
        if (!state.sending) {
            savePreference("nickname", $("nickname").value);
            resetUpload(); renderUpload();
        }
    });
    $("send-files").addEventListener("click", async () => {
        if (state.sending || state.scanning || state.submitted || (state.route === "auto" && !state.recognizedGame)) return;
        const invalid = validationError();
        if (invalid) { uploadStatus(invalid, {}, true); if (invalid === "invalidNickname") $("nickname").focus(); return; }
        if (!globalThis.crypto || !globalThis.crypto.subtle) { uploadStatus("secureRequired", {}, true); return; }
        const records = [...currentFiles()];
        const nickname = $("nickname").value.trim();
        clearAttention();
        state.sending = true; state.progress = null; state.receipt = null; state.receiptNickname = "";
        uploadStatus("preparing");
        try {
            const files = [];
            for (const record of records) {
                const digest = await crypto.subtle.digest("SHA-256", await record.file.arrayBuffer());
                const sha256 = Array.from(new Uint8Array(digest), value => value.toString(16).padStart(2, "0")).join("");
                files.push({ itemId: record.item.id, fileName: record.file.name, size: record.file.size, sha256 });
            }
            const form = new FormData();
            form.append("metadata", JSON.stringify({ gameId: state.route === "screenshots" ? null : records[0].game.id, nickname, files, selectionMode: state.route }));
            records.forEach((record, index) => form.append("file" + index, record.file, record.file.name));
            await new Promise(resolve => {
                const xhr = new XMLHttpRequest();
                xhr.open("POST", "/api/uploads");
                xhr.timeout = 150000;
                xhr.setRequestHeader("X-CSRF-TOKEN", document.querySelector('meta[name="request-verification-token"]').content);
                xhr.upload.onprogress = event => {
                    state.progress = event.lengthComputable ? Math.min(100, Math.round(event.loaded / event.total * 100)) : 0;
                    uploadStatus(state.progress === 100 ? "saving" : "uploading", { percent: state.progress });
                };
                xhr.upload.onload = () => { state.progress = 100; uploadStatus("saving"); };
                xhr.onload = () => {
                    let response;
                    try { response = JSON.parse(xhr.responseText); } catch { response = {}; }
                    if (xhr.status === 201 && response.saved === true && typeof response.submissionId === "string" && response.fileCount === records.length) {
                        state.submitted = true; state.receipt = response.submissionId; state.receiptNickname = nickname;
                        uploadStatus("success", { count: response.fileCount });
                    } else {
                        const knownErrors = ["tooLarge", "busy", "siteBusy", "storageFull", "storageFailed", "invalidSubmission", "invalidFile", "invalidImage", "unsupportedGame", "incompleteUpload", "refreshPage", "invalidNickname", "missingRequired", "unknownResult"];
                        const key = knownErrors.includes(response.error) ? response.error : xhr.status === 413 ? "tooLarge" : "unknownResult";
                        if (state.route === "screenshots" && key === "invalidImage") {
                            state.uploadStatus = null;
                            setStatus("screenshots", key, {}, true);
                        } else uploadStatus(key, {}, true);
                    }
                    resolve();
                };
                xhr.onerror = xhr.ontimeout = xhr.onabort = () => { uploadStatus("unknownResult", {}, true); resolve(); };
                state.progress = 0; uploadStatus("uploading", { percent: 0 }); xhr.send(form);
            });
        } catch { uploadStatus("readFailed", {}, true); }
        finally { state.sending = false; renderUpload(); }
    });
    const wideViewport = matchMedia("(min-width: 1600px)");
    const stackedGuide = matchMedia("(max-width: 899px)");
    function renderDragInstruction() {
        $("drag-instruction").textContent = text(stackedGuide.matches ? "dragInstructionBelow" : "dragInstruction");
    }
    function positionSupportedGames() { $("supported-games-panel").open = wideViewport.matches; }
    wideViewport.addEventListener("change", positionSupportedGames);
    stackedGuide.addEventListener("change", renderDragInstruction);
    positionSupportedGames();
    render();
})();
