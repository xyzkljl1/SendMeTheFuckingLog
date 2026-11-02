(() => {
    "use strict";
    const config = JSON.parse(document.getElementById("page-configuration").textContent);
    const games = config.games;
    const rules = globalThis.diagnosticRules;
    const $ = id => document.getElementById(id);
    const messages = {
        zh: {
            eyebrow: "让问题更容易被解决", title: "发送日志，帮助排查 Mod 问题。",
            recipientBefore: "你提交的日志和相关配置文件将交由作者", recipientAfter: "，用于排查 Mod 问题。",
            noAccount: "无需注册或登录，按下面的步骤操作即可。",
            preview: "只有点击发送才会上传选中的文件。存储空间有限，较旧的提交会自动清理。",
            chooseRoute: "你想如何找到文件？", autoTitle: "我找不到日志", autoDescription: "选择游戏文件夹，网页帮你查找",
            manualTitle: "我自己选择日志", manualDescription: "按照游戏指引，添加日志和配置",
            folderHeading: "打开游戏本地文件", folderDescription: "在 Steam 中右键游戏，选择「管理 → 浏览本地文件」。在打开的窗口按 Ctrl+A 全选，把所有内容一起拖到下面。",
            dropFolder: "把游戏文件夹里的内容拖到这里", oneFolder: "也可以拖入整个游戏文件夹。", chooseFolder: "选择游戏文件夹",
            folderPrivacy: "先识别游戏，确认后再查找日志。不会运行或上传游戏程序。", supported: "当前支持：",
            identifying: "正在识别游戏… 文件不会发送。", droppedContents: "拖入的内容",
            confirmHeading: "请确认你的游戏", detectedGame: "识别到：{game}", multipleGames: "识别到多个游戏，请选择本次需要排查的游戏。",
            confirmGame: "确认，查找日志", chooseAgain: "重新选择", gameConfirmed: "已确认：{game}",
            gameNotFound: "没有识别到支持的游戏。请从 Steam 打开本地文件后全选拖入，确保包含游戏程序，或使用手动上传。",
            duplicateFiles: "找到多份相同的诊断文件或游戏目录。请只拖入一份游戏安装的内容。",
            cancelScan: "取消查找", autoHelp: "请确认选择的是游戏安装文件夹。日志可能尚未生成，也可能位于所选目录之外。",
            viewManual: "查看手动查找指引 →", gameHeading: "选择你的游戏", gameDescription: "我们会告诉你需要哪些文件，以及在哪里找到它们。",
            gameLabel: "游戏", chooseGame: "请选择游戏", noGames: "暂未配置支持的游戏", guideHeading: "跟着下面的步骤找到文件", demo: "演示", demoOnly: "此游戏仅用于展示控件，不提供文件上传。",
            dropFiles: "把找到的日志和配置文件拖到这里", multipleFiles: "可以选择多个文件，也可以分多次添加。",
            chooseFiles: "选择日志和配置文件", selectedFiles: "已选择的文件", configNote: "配置文件可能包含私人信息，请在发送前检查内容。",
            nicknameHeading: "告诉作者你是谁", nicknameDescription: "填写你向作者求助时使用的昵称，方便对应问题。",
            nicknameLabel: "你的昵称", required: "必填", nicknamePlaceholder: "例如：你在 Nexus Mods 上的昵称",
            nicknameHint: "昵称只用于这一次提交，不会创建账号。", sendHeading: "确认后，交给作者排查",
            sendNote: "请确认文件和昵称，再点击发送。", send: "发送给 {author}", sent: "已保存",
            limits: "单个文件不超过 {file}，一次提交合计不超过 {total}。",
            preparing: "正在检查文件…", uploading: "正在上传… {percent}%", saving: "文件已传输，正在确认保存…",
            success: "保存成功，共 {count} 个文件。", receiptHeading: "提交编号", receiptHint: "把这个编号告诉作者，方便找到你的文件。",
            invalidNickname: "请填写有效昵称（最多 60 个字符）。", noFiles: "请先选择要发送的文件。",
            tooLarge: "文件超过大小限制，请按页面上的限制重新选择。", oneGame: "一次只能提交一个游戏的文件，请选择具体的游戏目录。",
            siteBusy: "网站暂时繁忙，请稍后重试。已选择的文件和昵称仍然保留。",
            busy: "当前上传人数较多，请稍后再试。", storageFull: "暂时没有足够的存储空间，请稍后再试或联系作者。",
            storageFailed: "未能确认保存成功，请联系作者确认后再重试。", invalidSubmission: "提交内容不完整或不符合要求，请重新选择文件。",
            invalidFile: "文件不在此游戏支持的清单中，请按指引重新选择。", unsupportedGame: "暂不支持此游戏，请刷新页面重新选择。",
            incompleteUpload: "文件未完整接收或校验未通过，本次提交未保存，请重试。", refreshPage: "页面已过期，请刷新页面后重新选择文件。",
            unknownResult: "连接中断，无法确认是否保存成功。请先联系作者核实，避免重复提交。",
            readFailed: "无法读取所选文件，请重新选择后再试。", secureRequired: "请通过 HTTPS 打开本站后再上传文件。",
            footer: "为更轻松的 Mod 问题反馈而设计", footerPrivacy: "文件由你选择，发送由你确认",
            log: "日志", config: "配置", copy: "复制路径", copied: "已复制", copyFailed: "无法复制，请选中路径手动复制。",
            scanning: "正在检查 {folder}… 文件不会发送。", found: "已检查 {folder}，找到 {count} 个诊断文件。",
            notFound: "在 {folder} 中没有找到诊断文件。请按手动指引检查日志位置。", folderOnly: "请拖入整个游戏文件夹，或全选游戏文件夹里的内容一起拖入。",
            folderUnsupported: "此浏览器不支持拖入文件夹，请使用「选择文件夹」按钮。",
            pickerUnsupported: "此浏览器不支持选择文件夹，请使用手动选择文件，或更新浏览器。",
            cancelled: "已取消查找。你可以重新选择文件夹。", scanError: "无法完整读取文件夹，请检查访问权限后重试，或使用手动选择文件。",
            tooMany: "所选目录包含过多项目，请选择具体的游戏安装文件夹。", summary: "{count} 个文件 · {size}",
            missing: "尚未找到：{names}。缺少文件也可以保留已有选择。", remove: "移除 {name}",
            manualAdded: "已添加 {count} 个文件。", manualRejected: "已添加 {count} 个文件；忽略 {ignored} 个不匹配的文件，请按指引选择指定文件。",
            filesOnly: "请添加文件，不要添加文件夹。", pickGameFirst: "请先选择游戏。", selected: "已添加", notSelected: "尚未添加"
        },
        en: {
            eyebrow: "A little context. A quicker fix.", title: "Share your logs. Help fix your mod.",
            recipientBefore: "Your logs and related configuration files will be shared with the author", recipientAfter: " to help investigate your mod issue.",
            noAccount: "No account or sign-in needed. Just follow the steps below.",
            preview: "Files are uploaded only when you click Send. Storage is limited; older submissions are automatically removed.",
            chooseRoute: "How would you like to find your files?", autoTitle: "Help me find my logs", autoDescription: "Choose your game folder. We’ll find the files.",
            manualTitle: "I’ll choose my files", manualDescription: "Follow a game-specific guide to add your files.",
            folderHeading: "Open your game's local files", folderDescription: "In Steam, right-click the game and choose Manage → Browse local files. Press Ctrl+A in that window, then drag everything below.",
            dropFolder: "Drop the contents of your game folder here", oneFolder: "You can also drop the whole game folder.", chooseFolder: "Choose game folder",
            folderPrivacy: "Confirm the game before we find logs. Game executables are never run or uploaded.", supported: "Currently supported: ",
            identifying: "Identifying your game… Nothing will be sent.", droppedContents: "the dropped items",
            confirmHeading: "Confirm your game", detectedGame: "Game found: {game}", multipleGames: "Multiple games were found. Choose the game you need help with.",
            confirmGame: "Confirm and find logs", chooseAgain: "Choose again", gameConfirmed: "Confirmed: {game}",
            gameNotFound: "No supported game was identified. Open local files from Steam and drag all contents, including the game executable, or choose files manually.",
            duplicateFiles: "Multiple copies of a diagnostic file or game folder were found. Drop the contents of just one game installation.",
            cancelScan: "Cancel search", autoHelp: "Check that you selected the game’s installation folder. Logs may not exist yet, or may be outside this folder.",
            viewManual: "Show me the manual guide →", gameHeading: "Choose your game", gameDescription: "We’ll show you which files to select and where to find them.",
            gameLabel: "Game", chooseGame: "Choose a game", noGames: "No supported games configured yet", guideHeading: "Follow these steps to find your files", demo: "Demo", demoOnly: "This game is a selection preview. File uploads are unavailable.",
            dropFiles: "Drop your logs and configuration files here", multipleFiles: "Select multiple files, or add them in separate steps.",
            chooseFiles: "Choose logs and settings", selectedFiles: "Selected files", configNote: "Configuration files may contain private information. Check their contents before sending.",
            nicknameHeading: "Let the author know it’s you", nicknameDescription: "Use the nickname you used when asking the author for help.",
            nicknameLabel: "Your nickname", required: "Required", nicknamePlaceholder: "For example, your Nexus Mods nickname",
            nicknameHint: "Used for this submission only. This does not create an account.", sendHeading: "Ready for the author to investigate",
            sendNote: "Check your files and nickname before sending.", send: "Send to {author}", sent: "Saved",
            limits: "Up to {file} per file and {total} per submission.",
            preparing: "Checking files…", uploading: "Uploading… {percent}%", saving: "Files transferred. Confirming they are saved…",
            success: "Saved successfully. {count} files received.", receiptHeading: "Submission ID", receiptHint: "Share this ID with the author so they can find your files.",
            invalidNickname: "Enter a valid nickname (up to 60 characters).", noFiles: "Choose files to send first.",
            tooLarge: "Your files exceed the size limits shown on this page. Please choose again.", oneGame: "Submit files for one game at a time. Choose a specific game folder.",
            siteBusy: "The website is busy. Please try again later. Your selected files and nickname have been kept.",
            busy: "Too many uploads are in progress. Please try again shortly.", storageFull: "There is not enough storage available. Try later or contact the author.",
            storageFailed: "We could not confirm the files were saved. Check with the author before retrying.", invalidSubmission: "The submission is incomplete or invalid. Please select your files again.",
            invalidFile: "This file is not on the game's supported list. Follow the guide and choose again.", unsupportedGame: "This game is not supported. Refresh the page and choose again.",
            incompleteUpload: "The files were incomplete or failed verification. This submission was not saved. Please retry.", refreshPage: "This page has expired. Refresh it and select your files again.",
            unknownResult: "The connection was interrupted. We cannot confirm whether your files were saved. Check with the author before retrying to avoid duplicates.",
            readFailed: "Could not read the selected files. Please select them again.", secureRequired: "Open this website over HTTPS to upload files.",
            footer: "A simpler way to report mod issues", footerPrivacy: "You choose the files. You confirm what’s sent.",
            log: "Log", config: "Settings", copy: "Copy path", copied: "Copied", copyFailed: "Could not copy. Select the path and copy it manually.",
            scanning: "Checking {folder}… Nothing will be sent.", found: "Checked {folder}. Found {count} diagnostic files.",
            notFound: "No diagnostic files were found in {folder}. Check the locations in the manual guide.", folderOnly: "Drop the whole game folder, or select and drop all of its contents together.",
            folderUnsupported: "This browser cannot read dropped folders. Use the Choose folder button instead.",
            pickerUnsupported: "This browser cannot select folders. Choose files manually or update your browser.",
            cancelled: "Search cancelled. You can choose another folder.", scanError: "The folder could not be fully read. Check access permissions and try again, or choose files manually.",
            tooMany: "This folder contains too many items. Choose the specific game installation folder.", summary: "{count} files · {size}",
            missing: "Not found yet: {names}. You can keep the files you already selected.", remove: "Remove {name}",
            manualAdded: "Added {count} files.", manualRejected: "Added {count} files; skipped {ignored} unmatched files. Please choose the files listed in the guide.",
            filesOnly: "Please add files, not folders.", pickGameFirst: "Please choose a game first.", selected: "Added", notSelected: "Not added"
        }
    };
    const state = {
        language: (navigator.language || "en").toLowerCase().startsWith("zh") ? "zh" : "en",
        route: "auto", manualGame: "", autoFiles: [], manualFiles: [], autoStatus: null, manualStatus: null,
        scanning: false, scanToken: 0, autoHelp: false,
        source: null, candidates: [], candidateIndex: "", confirmedGame: null,
        sending: false, submitted: false, uploadStatus: null, receipt: null, progress: null
    };
    const local = value => value[state.language] || value.en;
    const gameName = game => `${game.name.zh} / ${game.name.en}`;
    const text = (key, args = {}) => (messages[state.language][key] || key).replace(/\{(\w+)\}/g, (_, name) => args[name] ?? "");
    const sizeText = bytes => bytes < 1024 ? `${bytes} B` : bytes < 1048576 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1048576).toFixed(1)} MB`;
    const make = (tag, className, value) => { const element = document.createElement(tag); element.className = className; if (value !== undefined) element.textContent = value; return element; };
    const currentFiles = () => state.route === "auto" ? state.autoFiles : state.manualFiles;
    function renderGameCards(containerId, choices, group, selected, onChange) {
        const container = $(containerId);
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
                image.alt = ""; image.width = 480; image.height = 270; image.loading = "lazy"; image.decoding = "async";
                image.referrerPolicy = "no-referrer";
                image.addEventListener("error", () => { image.hidden = true; });
                image.src = game.image; cover.append(image);
            }
            const names = make("span", "game-card-names");
            const zh = make("strong", "", game.name.zh), en = make("span", "", game.name.en);
            zh.lang = "zh-CN"; en.lang = "en"; names.append(zh, en);
            if (game.isDemo) names.append(make("span", "demo-badge", text("demo")));
            card.append(radio, cover, names); container.append(card);
            radio.addEventListener("change", () => {
                if (!radio.checked || state.sending || (group === "detected-game" && (state.scanning || state.confirmedGame))) return;
                for (const other of container.querySelectorAll("input[type=radio]")) {
                    other.checked = other === radio;
                    other.closest(".game-card").classList.toggle("selected", other.checked);
                }
                onChange(value);
            });
        }
        if (!choices.length) container.append(make("p", "hint", text("noGames")));
    }
    function validationError() {
        const files = currentFiles();
        const nickname = $("nickname").value.trim();
        if (state.route === "manual" && games.find(game => game.id === state.manualGame)?.isDemo) return "demoOnly";
        if (!files.length) return "noFiles";
        if (new Set(files.map(record => record.game.id)).size !== 1) return "oneGame";
        if (!nickname || nickname.length > 60 || /[\u0000-\u001f\u007f-\u009f]/.test(nickname)) return "invalidNickname";
        if (files.some(record => record.file.size > config.maxFileBytes)
            || files.reduce((sum, record) => sum + record.file.size, 0) > config.maxSubmissionBytes) return "tooLarge";
        return null;
    }
    function renderUpload() {
        const invalid = validationError();
        const button = $("send-files");
        button.textContent = state.sending ? text(state.progress === null ? "preparing" : state.progress === 100 ? "saving" : "uploading", { percent: state.progress || 0 })
            : state.submitted ? text("sent") : text("send", { author: config.authorName });
        // Keep invalid submissions clickable to give a clear validation message instead of a silent disabled button.
        button.disabled = state.sending || state.scanning || state.submitted || invalid === "demoOnly" || (state.route === "auto" && state.candidates.length > 0 && !state.confirmedGame);
        $("upload-limits").textContent = text("limits", { file: sizeText(config.maxFileBytes), total: sizeText(config.maxSubmissionBytes) });
        const status = state.uploadStatus;
        $("upload-status").textContent = status ? text(status.key, status.args) : (invalid === "tooLarge" || invalid === "oneGame" ? text(invalid) : "");
        $("upload-status").classList.toggle("error", Boolean(status ? status.error : invalid === "tooLarge" || invalid === "oneGame"));
        $("upload-progress").hidden = !state.sending || state.progress === null;
        $("upload-progress").value = state.progress || 0;
        $("upload-progress").setAttribute("aria-label", text("uploading", { percent: state.progress || 0 }));
        $("upload-receipt").hidden = !state.receipt;
        $("submission-id").textContent = state.receipt || "";
        $("nickname").disabled = state.sending;
        $("manual-game-choice").disabled = state.sending || !games.length;
        $("choose-files").disabled = state.sending;
        $("choose-folder").disabled = state.sending || state.scanning || !games.length;
        $("route-auto").disabled = state.sending;
        $("route-manual").disabled = state.sending;
        $("confirm-game").disabled = state.sending || state.scanning || state.candidateIndex === "";
        $("detected-game-choice").disabled = state.sending || state.scanning || Boolean(state.confirmedGame);
        $("choose-again").disabled = state.sending || state.scanning;
        document.querySelectorAll(".remove-button").forEach(button => { button.disabled = state.sending; });
    }
    function resetUpload() {
        state.submitted = false; state.uploadStatus = null; state.receipt = null; state.progress = null;
    }
    function uploadStatus(key, args = {}, error = false) {
        state.uploadStatus = { key, args, error }; renderUpload();
    }
    function setStatus(route, key, args = {}, error = false) {
        state[route + "Status"] = { key, args, error };
        renderStatus();
    }
    function renderStatus() {
        for (const route of ["auto", "manual"]) {
            const status = state[route + "Status"];
            const element = $(route === "auto" ? "folder-status" : "manual-status");
            element.textContent = status ? text(status.key, { ...status.args, folder: status.args.folder === "" ? text("droppedContents") : status.args.folder }) : "";
            element.classList.toggle("error", Boolean(status && status.error));
        }
        $("auto-help").hidden = !state.autoHelp;
        $("cancel-scan").hidden = !state.scanning;
        $("choose-folder").disabled = state.scanning || games.length === 0;
        $("folder-drop").setAttribute("aria-busy", String(state.scanning));
        renderUpload();
    }
    function renderRecognition() {
        const confirmed = state.confirmedGame;
        const multiple = state.candidates.length > 1;
        $("game-confirmation").hidden = !state.candidates.length;
        $("detected-game-name").textContent = confirmed ? text("gameConfirmed", { game: gameName(confirmed) })
            : multiple ? text("multipleGames")
            : state.candidates.length ? text("detectedGame", { game: gameName(state.candidates[0].game) }) : "";
        renderGameCards("detected-game-cards", state.candidates.map((candidate, index) => ({ game: candidate.game, value: String(index) })),
            "detected-game", state.candidateIndex, value => { state.candidateIndex = value; renderUpload(); });
        $("confirm-game").hidden = Boolean(confirmed);
        renderUpload();
    }
    function renderGuide() {
        const game = games.find(game => game.id === state.manualGame);
        $("file-drop").hidden = !game || Boolean(game.isDemo);
        $("game-guide").hidden = !game;
        $("guide-steps").replaceChildren();
        $("expected-files").replaceChildren();
        if (!game) return;
        for (const instruction of game.instructions) $("guide-steps").append(make("li", "", local(instruction)));
        for (const item of game.files) {
            const row = make("div", "expected-file");
            const heading = make("div", "expected-file-heading");
            heading.append(make("strong", "", local(item.name)), make("span", "kind-badge", text(item.kind)));
            if (state.manualFiles.some(file => file.item.id === item.id)) heading.append(make("span", "kind-badge", text("selected")));
            row.append(heading);
            for (const path of item.paths) {
                const pathRow = make("div", "path-row");
                const button = make("button", "copy-button", text("copy"));
                button.type = "button";
                button.setAttribute("aria-label", `${text("copy")}: ${path}`);
                button.addEventListener("click", async () => {
                    try { await navigator.clipboard.writeText(path.replace(/\//g, "\\")); button.textContent = text("copied"); }
                    catch { setStatus("manual", "copyFailed", {}, true); }
                });
                pathRow.append(make("code", "", path.replace(/\//g, "\\")), button);
                row.append(pathRow);
            }
            $("expected-files").append(row);
        }
    }
    function renderSelection() {
        const files = currentFiles();
        $("selection-section").hidden = !files.length;
        $("selected-files").replaceChildren();
        $("selection-summary").textContent = text("summary", { count: files.length, size: sizeText(files.reduce((total, record) => total + record.file.size, 0)) });
        for (const record of files) {
            const row = make("li", "");
            const info = make("div", "file-info");
            info.append(make("strong", "", record.file.name), make("span", "", `${gameName(record.game)} · ${local(record.item.name)}`));
            const remove = make("button", "remove-button", "×");
            remove.type = "button";
            remove.setAttribute("aria-label", text("remove", { name: record.file.name }));
            remove.addEventListener("click", () => {
                if (state.sending) return;
                resetUpload();
                const key = state.route === "auto" ? "autoFiles" : "manualFiles";
                state[key] = state[key].filter(value => value !== record);
                renderSelection(); renderGuide();
                $("nickname").focus();
            });
            row.append(info, make("span", "file-size", sizeText(record.file.size)), remove);
            $("selected-files").append(row);
        }
        const relevant = state.route === "manual" ? games.filter(game => game.id === state.manualGame) : games.filter(game => files.some(file => file.game.id === game.id));
        const missing = relevant.flatMap(game => game.files.filter(item => !files.some(file => file.game.id === game.id && file.item.id === item.id)).map(item => local(item.name)));
        $("missing-files").textContent = missing.length ? text("missing", { names: missing.join(state.language === "zh" ? "、" : ", ") }) : "";
        renderUpload();
    }
    function render() {
        document.documentElement.lang = state.language === "zh" ? "zh-CN" : "en";
        document.title = (state.language === "zh" ? "发送诊断文件" : "Share diagnostic files") + " · Mod Log Helper";
        document.querySelectorAll("[data-i18n]").forEach(element => { element.textContent = text(element.dataset.i18n); });
        document.querySelectorAll("[data-language]").forEach(button => button.setAttribute("aria-pressed", String(button.dataset.language === state.language)));
        $("author-name").textContent = config.authorName;
        $("nickname").placeholder = text("nicknamePlaceholder");
        const supported = games.filter(game => !game.isDemo);
        $("supported-games").textContent = supported.length ? supported.map(gameName).join(" · ") : text("noGames");
        renderGameCards("game-cards", games.map(game => ({ game, value: game.id })), "manual-game", state.manualGame, value => {
            resetUpload(); state.manualGame = value; state.manualFiles = []; state.manualStatus = null;
            renderGuide(); renderSelection(); renderStatus();
        });
        for (const route of ["auto", "manual"]) {
            $("route-" + route).classList.toggle("selected", state.route === route);
            $("route-" + route).setAttribute("aria-pressed", String(state.route === route));
            $(route + "-panel").hidden = state.route !== route;
        }
        renderGuide(); renderSelection(); renderRecognition(); renderStatus();
    }
    function changeRoute(route) {
        if (state.sending) return;
        if (state.route !== route) {
            resetUpload();
            if (state.scanning) clearAutomatic();
        }
        state.route = route; render();
    }
    document.querySelectorAll("[data-language]").forEach(button => button.addEventListener("click", () => { state.language = button.dataset.language; render(); }));
    $("route-auto").addEventListener("click", () => changeRoute("auto"));
    $("route-manual").addEventListener("click", () => changeRoute("manual"));
    $("switch-manual").addEventListener("click", () => {
        const supported = games.filter(game => !game.isDemo);
        if (!state.manualGame && supported.length === 1) state.manualGame = supported[0].id;
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
        let count = 0, ignored = 0;
        for (const file of files) {
            const item = rules.matchManualFile(game, file.name);
            if (!item) { ignored++; continue; }
            state.manualFiles = state.manualFiles.filter(record => record.item.id !== item.id);
            state.manualFiles.push({ game, item, file }); count++;
        }
        setStatus("manual", ignored ? "manualRejected" : "manualAdded", { count, ignored }, ignored > 0);
        renderGuide(); renderSelection();
    }
    $("file-input").addEventListener("change", event => { addManualFiles(Array.from(event.target.files)); event.target.value = ""; });
    function clearAutomatic() {
        state.scanToken++;
        state.scanning = false; state.source = null; state.candidates = []; state.candidateIndex = "";
        state.confirmedGame = null; state.autoFiles = []; state.autoHelp = false; state.autoStatus = null;
    }
    async function identifySource(source) {
        resetUpload();
        clearAutomatic();
        const token = state.scanToken;
        state.source = source; state.scanning = true;
        setStatus("auto", "identifying"); renderRecognition(); renderSelection();
        try {
            const candidates = await rules.identifyGames(games, source, () => token !== state.scanToken);
            if (token !== state.scanToken) return;
            if (new Set(candidates.map(candidate => candidate.game.id)).size !== candidates.length) throw new Error("duplicateFiles");
            state.candidates = candidates; state.candidateIndex = candidates.length === 1 ? "0" : "";
            state.scanning = false; state.autoHelp = !candidates.length;
            state.autoStatus = candidates.length ? null : { key: "gameNotFound", args: {}, error: true };
            renderRecognition(); renderStatus();
            if (candidates.length === 1) $("confirm-game").focus();
            else if (candidates.length) $("detected-game-cards").querySelector("input")?.focus();
        } catch (error) { failScan(token, error); }
    }
    function failScan(token, error) {
        if (token !== state.scanToken) return;
        state.scanning = false; state.autoHelp = true; state.autoFiles = [];
        setStatus("auto", ["tooMany", "duplicateFiles", "folderOnly"].includes(error.message) ? error.message : "scanError", {}, true);
        renderRecognition(); renderSelection();
    }
    $("cancel-scan").addEventListener("click", () => {
        clearAutomatic(); resetUpload(); setStatus("auto", "cancelled"); renderRecognition(); renderSelection();
    });
    $("choose-again").addEventListener("click", () => {
        if (state.sending || state.scanning) return;
        clearAutomatic(); resetUpload(); render(); $("choose-folder").focus();
    });
    $("confirm-game").addEventListener("click", async () => {
        if (state.sending || state.scanning || state.confirmedGame || state.candidateIndex === "") return;
        const candidate = state.candidates[Number(state.candidateIndex)], source = state.source;
        if (!candidate || !source) return;
        const token = ++state.scanToken;
        resetUpload(); state.scanning = true; state.autoHelp = false;
        setStatus("auto", "scanning", { folder: source.folder });
        try {
            const records = await rules.collectFiles(candidate, source, () => token !== state.scanToken);
            if (token !== state.scanToken) return;
            state.scanning = false; state.confirmedGame = candidate.game; state.autoFiles = records;
            if (state.manualGame !== candidate.game.id) {
                state.manualGame = candidate.game.id; state.manualFiles = []; state.manualStatus = null;
            }
            state.autoHelp = !records.length;
            setStatus("auto", records.length ? "found" : "notFound", { folder: source.folder, count: records.length }, !records.length);
            renderRecognition(); renderSelection();
        } catch (error) { failScan(token, error); }
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
        clearAutomatic(); resetUpload(); renderRecognition(); renderSelection(); renderStatus();
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
    // Prevent accidental navigation if files are dropped outside the designated zones.
    document.addEventListener("dragover", event => event.preventDefault());
    document.addEventListener("drop", event => event.preventDefault());
    $("nickname").addEventListener("input", () => { if (!state.sending) { resetUpload(); renderUpload(); } });
    $("send-files").addEventListener("click", async () => {
        if (state.sending || state.scanning || state.submitted || (state.route === "auto" && !state.confirmedGame)) return;
        const invalid = validationError();
        if (invalid) { uploadStatus(invalid, {}, true); if (invalid === "invalidNickname") $("nickname").focus(); return; }
        if (!globalThis.crypto || !globalThis.crypto.subtle) { uploadStatus("secureRequired", {}, true); return; }
        const records = [...currentFiles()];
        const nickname = $("nickname").value.trim();
        state.sending = true; state.progress = null; state.receipt = null;
        uploadStatus("preparing");
        try {
            const files = [];
            for (const record of records) {
                const digest = await crypto.subtle.digest("SHA-256", await record.file.arrayBuffer());
                const sha256 = Array.from(new Uint8Array(digest), value => value.toString(16).padStart(2, "0")).join("");
                files.push({ itemId: record.item.id, fileName: record.file.name, size: record.file.size, sha256 });
            }
            const form = new FormData();
            form.append("metadata", JSON.stringify({ gameId: records[0].game.id, nickname, files }));
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
                        state.submitted = true; state.receipt = response.submissionId;
                        uploadStatus("success", { count: response.fileCount });
                    } else {
                        const knownErrors = ["tooLarge", "busy", "siteBusy", "storageFull", "storageFailed", "invalidSubmission", "invalidFile", "unsupportedGame", "incompleteUpload", "refreshPage", "invalidNickname", "unknownResult"];
                        const key = knownErrors.includes(response.error) ? response.error : xhr.status === 413 ? "tooLarge" : "unknownResult";
                        uploadStatus(key, {}, true);
                    }
                    resolve();
                };
                xhr.onerror = xhr.ontimeout = xhr.onabort = () => { uploadStatus("unknownResult", {}, true); resolve(); };
                state.progress = 0; uploadStatus("uploading", { percent: 0 }); xhr.send(form);
            });
        } catch { uploadStatus("readFailed", {}, true); }
        finally { state.sending = false; renderUpload(); }
    });
    render();
})();
