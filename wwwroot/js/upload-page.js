(() => {
    "use strict";
    const config = JSON.parse(document.getElementById("page-configuration").textContent);
    const games = config.games;
    const preloadedImages = new Set(config.preloadedImages || []);
    const rules = globalThis.diagnosticRules;
    const recognizer = rules.createRecognizer(games, config.recognition);
    const $ = id => document.getElementById(id);
    const messages = {
        zh: {
            title: "上传日志",
            guideImageOpen: "点击放大", guideImageUnavailable: "演示图片暂时无法加载，请按文字指引操作。",
            automaticGuideAlt: "操作示例：从 Steam 打开游戏目录，鼠标框选全部内容（也可按 Ctrl+A），再拖入上传区域。",
            recipientBefore: "文件将交给作者", recipientAfter: "，用于排查 Mod 问题。",
            chooseRoute: "选择方式", autoTitle: "自动选择日志", autoDescription: "选择游戏文件夹，自动查找日志",
            manualTitle: "手动选择日志", manualDescription: "按下面的演示，找到文件后发送",
            folderHeading: "打开游戏本地文件", folderDescription: "在 Steam 中右键游戏 → 管理 → 浏览本地文件。",
            dragInstruction: "在打开的窗口中选择全部文件，拖入右侧窗口。",
            dropFolder: "把游戏文件夹里的内容拖到这里", oneFolder: "（也可以回到上一级目录并选择游戏文件夹）", chooseFolder: "选择游戏文件夹",
            folderPrivacy: "只发送需要的日志和配置，不会上传整个游戏。", supported: "当前支持：",
            identifying: "正在识别游戏… 文件不会发送。", droppedContents: "拖入的内容",
            detectedGame: "已识别游戏", multipleGames: "识别到多个游戏，请只拖入一个游戏的安装文件夹，或使用手动选择日志。",
            gameNotFound: "没有识别到支持的游戏。请从 Steam 打开本地文件后全选拖入，确保包含游戏程序，或使用手动上传。",
            duplicateFiles: "找到多份相同的诊断文件或游戏目录。请只拖入一份游戏安装的内容。",
            cancelScan: "取消查找", autoHelp: "请确认选择的是游戏安装文件夹。日志可能尚未生成，也可能位于所选目录之外。",
            viewManual: "查看手动查找指引 →", gameHeading: "选择游戏",
            uploadHeading: "上传日志", chooseGame: "请选择游戏", noGames: "暂未配置支持的游戏", guideHeading: "文件位置", demo: "演示", demoOnly: "此游戏仅用于展示控件，不提供文件上传。",
            dropFiles: "把找到的日志和配置文件拖到这里", multipleFiles: "可以选择多个文件，也可以分多次添加。",
            primaryFile: "必须上传", secondaryFile: "次要", secondaryNote: "（只有作者要求时才需要上传）",
            missingRequired: "请先添加必须上传的日志。", missingPrimary: "还需要添加 {names}。",
            chooseFiles: "选择日志和配置文件", selectedFiles: "已选择的文件",
            nicknameHeading: "填写昵称",
            nicknameInputLabel: "输入昵称：",
            nicknamePlaceholder: "能分辨上传者即可，N站来的就填N站id",
            send: "发送给 {author}", sent: "已保存",
            limits: "单个文件不超过 {file}，一次提交合计不超过 {total}。",
            preparing: "正在检查文件…", uploading: "正在上传… {percent}%", saving: "正在发送…",
            success: "发送成功！", receiptHint: "告诉作者，你填写的昵称是：",
            invalidNickname: "请填写有效昵称（最多 60 个字符）。", noFiles: "请先选择要发送的文件。",
            tooLarge: "文件超过大小限制，请按页面上的限制重新选择。", oneGame: "一次只能提交一个游戏的文件，请选择具体的游戏目录。",
            siteBusy: "网站暂时繁忙，请稍后重试。已选择的文件和昵称仍然保留。",
            busy: "当前上传人数较多，请稍后再试。", storageFull: "暂时没有足够的存储空间，请稍后再试或联系作者。",
            storageFailed: "未能确认保存成功，请联系作者确认后再重试。", invalidSubmission: "提交内容不完整或不符合要求，请重新选择文件。",
            invalidFile: "文件不在此游戏支持的清单中，请按指引重新选择。", unsupportedGame: "暂不支持此游戏，请刷新页面重新选择。",
            incompleteUpload: "文件未完整接收或校验未通过，本次提交未保存，请重试。", refreshPage: "页面已过期，请刷新页面后重新选择文件。",
            unknownResult: "连接中断，无法确认是否保存成功。请先联系作者核实，避免重复提交。",
            readFailed: "无法读取所选文件，请重新选择后再试。", secureRequired: "请通过 HTTPS 打开本站后再上传文件。",
            log: "日志", config: "配置", copy: "复制路径", copied: "已复制", copyFailed: "无法复制，请选中路径手动复制。",
            scanning: "正在检查 {folder}… 文件不会发送。", found: "找到日志了！",
            notFound: "在 {folder} 中没有找到诊断文件。请按手动指引检查日志位置。", folderOnly: "请拖入整个游戏文件夹，或全选游戏文件夹里的内容一起拖入。",
            folderUnsupported: "此浏览器不支持拖入文件夹，请使用「选择文件夹」按钮。",
            pickerUnsupported: "此浏览器不支持选择文件夹，请使用手动选择文件，或更新浏览器。",
            cancelled: "已取消查找。你可以重新选择文件夹。", scanError: "无法完整读取文件夹，请检查访问权限后重试，或使用手动选择文件。",
            tooMany: "所选目录包含过多项目，请选择具体的游戏安装文件夹。", summary: "{count} 个文件 · {size}",
            missing: "尚未找到：{names}。缺少文件也可以保留已有选择。",
            manualAdded: "已添加 {count} 个文件。", manualRejected: "该文件不是该游戏所需的日志。",
            manualPartiallyAdded: "已添加 {count} 个文件；其余文件不是该游戏所需的日志。",
            filesOnly: "请添加文件，不要添加文件夹。", pickGameFirst: "请先选择游戏。", selected: "已添加", notSelected: "尚未添加"
        },
        en: {
            title: "Upload logs",
            guideImageOpen: "View full size", guideImageUnavailable: "The guide image could not load. Please follow the written steps.",
            automaticGuideAlt: "Example: open your game's folder from Steam, drag a selection box around all items (or press Ctrl+A), then drag them into the upload area.",
            recipientBefore: "Files will be sent to the author", recipientAfter: " to troubleshoot your mod issue.",
            chooseRoute: "Choose a method", autoTitle: "Select logs automatically", autoDescription: "Choose your game folder to find logs automatically",
            manualTitle: "Select logs manually", manualDescription: "Follow the demo below to find and send the files",
            folderHeading: "Open your game's local files", folderDescription: "In Steam, right-click the game → Manage → Browse local files.",
            dragInstruction: "Select all files in the window that opens, then drag them into the area on the right.",
            dropFolder: "Drop the contents of your game folder here", oneFolder: "(You can also go up one level and select the game folder.)", chooseFolder: "Choose game folder",
            folderPrivacy: "Only the logs and settings we need will be sent, not the whole game.", supported: "Currently supported: ",
            identifying: "Identifying your game… Nothing will be sent.", droppedContents: "the dropped items",
            detectedGame: "Detected game", multipleGames: "Multiple games were found. Drop the installation folder for just one game, or select logs manually.",
            gameNotFound: "No supported game was identified. Open local files from Steam and drag all contents, including the game executable, or choose files manually.",
            duplicateFiles: "Multiple copies of a diagnostic file or game folder were found. Drop the contents of just one game installation.",
            cancelScan: "Cancel search", autoHelp: "Check that you selected the game’s installation folder. Logs may not exist yet, or may be outside this folder.",
            viewManual: "Show the manual guide →", gameHeading: "Choose your game",
            uploadHeading: "Upload logs", chooseGame: "Choose a game", noGames: "No supported games configured yet", guideHeading: "File locations", demo: "Demo", demoOnly: "This game is a selection preview. File uploads are unavailable.",
            dropFiles: "Drop your logs and configuration files here", multipleFiles: "Select multiple files, or add them in separate steps.",
            primaryFile: "Required", secondaryFile: "Secondary", secondaryNote: "(Only upload if the author requests it)",
            missingRequired: "Please add the required logs first.", missingPrimary: "Please also add {names}.",
            chooseFiles: "Choose logs and settings", selectedFiles: "Selected files",
            nicknameHeading: "Enter your nickname",
            nicknameInputLabel: "Nickname:",
            nicknamePlaceholder: "Use a name that identifies you. If you're from Nexus Mods, enter your Nexus Mods ID.",
            send: "Send to {author}", sent: "Saved",
            limits: "Up to {file} per file and {total} per submission.",
            preparing: "Checking files…", uploading: "Uploading… {percent}%", saving: "Sending…",
            success: "Files sent!", receiptHint: "Tell the author the nickname you used:",
            invalidNickname: "Enter a valid nickname (up to 60 characters).", noFiles: "Choose files to send first.",
            tooLarge: "Your files exceed the size limits shown on this page. Please choose again.", oneGame: "Submit files for one game at a time. Choose a specific game folder.",
            siteBusy: "The website is busy. Please try again later. Your selected files and nickname have been kept.",
            busy: "Too many uploads are in progress. Please try again shortly.", storageFull: "There is not enough storage available. Try later or contact the author.",
            storageFailed: "We could not confirm the files were saved. Check with the author before retrying.", invalidSubmission: "The submission is incomplete or invalid. Please select your files again.",
            invalidFile: "This file is not on the game's supported list. Follow the guide and choose again.", unsupportedGame: "This game is not supported. Refresh the page and choose again.",
            incompleteUpload: "The files were incomplete or failed verification. This submission was not saved. Please retry.", refreshPage: "This page has expired. Refresh it and select your files again.",
            unknownResult: "The connection was interrupted. We cannot confirm whether your files were saved. Check with the author before retrying to avoid duplicates.",
            readFailed: "Could not read the selected files. Please select them again.", secureRequired: "Open this website over HTTPS to upload files.",
            log: "Log", config: "Settings", copy: "Copy path", copied: "Copied", copyFailed: "Could not copy. Select the path and copy it manually.",
            scanning: "Checking {folder}… Nothing will be sent.", found: "Found your logs!",
            notFound: "No diagnostic files were found in {folder}. Check the locations in the manual guide.", folderOnly: "Drop the whole game folder, or select and drop all of its contents together.",
            folderUnsupported: "This browser cannot read dropped folders. Use the Choose folder button instead.",
            pickerUnsupported: "This browser cannot select folders. Choose files manually or update your browser.",
            cancelled: "Search cancelled. You can choose another folder.", scanError: "The folder could not be fully read. Check access permissions and try again, or choose files manually.",
            tooMany: "This folder contains too many items. Choose the specific game installation folder.", summary: "{count} files · {size}",
            missing: "Not found yet: {names}. You can keep the files you already selected.",
            manualAdded: "Added {count} files.", manualRejected: "This file is not a log required for this game.",
            manualPartiallyAdded: "Added {count} files. The remaining files are not logs required for this game.",
            filesOnly: "Please add files, not folders.", pickGameFirst: "Please choose a game first.", selected: "Added", notSelected: "Not added"
        }
    };
    const languageStorageKey = "mod-log-helper.language";
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
    const state = {
        language: initialLanguage(),
        route: "auto", manualGame: "", autoFiles: [], manualFiles: [], autoStatus: null, manualStatus: null,
        scanning: false, scanToken: 0, autoHelp: false,
        recognizedGame: null,
        sending: false, submitted: false, uploadStatus: null, receipt: null, receiptNickname: "", progress: null
    };
    const local = value => value[state.language] || value.en;
    const gameName = game => state.language === "zh" ? `${game.name.zh} / ${game.name.en}` : game.name.en;
    const text = (key, args = {}) => (messages[state.language][key] || key).replace(/\{(\w+)\}/g, (_, name) => args[name] ?? "");
    const sizeText = bytes => bytes < 1024 ? `${bytes} B` : bytes < 1048576 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1048576).toFixed(1)} MB`;
    const make = (tag, className, value) => { const element = document.createElement(tag); element.className = className; if (value !== undefined) element.textContent = value; return element; };
    const currentFiles = () => state.route === "auto" ? state.autoFiles : state.manualFiles;
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
    function validationError() {
        const files = currentFiles();
        const nickname = $("nickname").value.trim();
        if (state.route === "manual" && games.find(game => game.id === state.manualGame)?.isDemo) return "demoOnly";
        if (state.route === "manual") {
            const game = games.find(game => game.id === state.manualGame);
            if (game?.files.some(item => item.isPrimary && !files.some(record => record.item.id === item.id))) return "missingRequired";
        }
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
        // Require a nickname before sending; other validation errors can still explain how to fix the selection.
        button.disabled = !$("nickname").value.trim() || state.sending || state.scanning || state.submitted || invalid === "demoOnly" || invalid === "missingRequired" || (state.route === "auto" && !state.recognizedGame);
        $("upload-limits").textContent = text("limits", { file: sizeText(config.maxFileBytes), total: sizeText(config.maxSubmissionBytes) });
        const status = state.uploadStatus;
        $("upload-status").textContent = status ? (status.key === "success" ? "" : text(status.key, status.args)) : (invalid === "tooLarge" || invalid === "oneGame" ? text(invalid) : "");
        $("upload-status").classList.toggle("error", Boolean(status ? status.error : invalid === "tooLarge" || invalid === "oneGame"));
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
        for (const route of ["auto", "manual"]) {
            const status = state[route + "Status"];
            const element = $(route === "auto" ? "folder-status" : "manual-status");
            element.textContent = status ? text(status.key, { ...status.args, folder: status.args.folder === "" ? text("droppedContents") : status.args.folder }) : "";
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
            image.addEventListener("error", () => { image.hidden = true; error.hidden = false; caption.style.visibility = "hidden"; link.removeAttribute("href"); });
            image.src = url; frame.append(image, error); link.append(frame);
            const caption = make("span", "guide-image-caption", text("guideImageOpen"));
            link.append(caption); figure.append(link); container.append(figure);
        }
    }
    function renderGuide() {
        const game = games.find(game => game.id === state.manualGame);
        $("file-drop").hidden = !game || Boolean(game.isDemo);
        $("game-guide").hidden = !game;
        $("expected-files").replaceChildren();
        const steps = $("guide-steps");
        const guideKey = JSON.stringify([game?.id, state.language]);
        if (steps.dataset.guideKey !== guideKey) {
            steps.dataset.guideKey = guideKey;
            steps.replaceChildren();
            for (const instruction of game?.instructions || []) {
                const step = make("li", "guide-step");
                step.append(make("p", "guide-step-text", local(instruction)));
                const media = make("div", "guide-images");
                renderGuideImages(media, instruction.imageUrls || [], local(instruction));
                step.append(media); steps.append(step);
            }
        }
        if (!game) return;
        for (const item of [...game.files].sort((a, b) => Number(Boolean(b.isPrimary)) - Number(Boolean(a.isPrimary)))) {
            const row = make("div", "expected-file");
            const heading = make("div", "expected-file-heading");
            heading.append(make("strong", "", local(item.name)), make("span", "kind-badge", text(item.kind)),
                make("span", item.isPrimary ? "kind-badge primary-file-badge" : "kind-badge", text(item.isPrimary ? "primaryFile" : "secondaryFile")));
            if (!item.isPrimary) heading.append(make("span", "secondary-file-note", text("secondaryNote")));
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
            info.append(make("strong", "", record.file.name), make("span", "", `${gameName(record.game)} · ${local(record.item.name)}`));
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
        $("author-name").textContent = config.authorName;
        $("nickname").placeholder = text("nicknamePlaceholder");
        $("nickname-step-number").textContent = state.route === "manual" ? "3" : "2";
        renderGuideImages($("automatic-guide"), config.automaticGuideImages || [], text("automaticGuideAlt"));
        const supported = games.filter(game => !game.isDemo);
        $("supported-games").textContent = supported.length ? supported.map(gameName).join(" · ") : text("noGames");
        renderGameCards("game-cards", games.map(game => ({ game, value: game.id })), "manual-game", state.manualGame, value => {
            clearAttention(); resetUpload(); state.manualGame = value; state.manualFiles = []; state.manualStatus = null;
            renderGuide(); renderSelection(); renderStatus();
        });
        for (const route of ["auto", "manual"]) {
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
        state.route = route; render();
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
        const hadFiles = state.manualFiles.length > 0;
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
        if (count > 0 && !hadFiles) showNextStepHints();
    }
    $("file-input").addEventListener("change", event => { addManualFiles(Array.from(event.target.files)); event.target.value = ""; });
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
            state.autoHelp = !records.length;
            setStatus("auto", records.length ? "found" : "notFound", { folder: source.folder, count: records.length }, !records.length);
            renderSelection();
            if (records.length) showNextStepHints();
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
    // Prevent accidental navigation if files are dropped outside the designated zones.
    document.addEventListener("dragover", event => event.preventDefault());
    document.addEventListener("drop", event => event.preventDefault());
    $("nickname").addEventListener("input", () => { if (!state.sending) { resetUpload(); renderUpload(); } });
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
            form.append("metadata", JSON.stringify({ gameId: records[0].game.id, nickname, files, selectionMode: state.route }));
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
                        const knownErrors = ["tooLarge", "busy", "siteBusy", "storageFull", "storageFailed", "invalidSubmission", "invalidFile", "unsupportedGame", "incompleteUpload", "refreshPage", "invalidNickname", "missingRequired", "unknownResult"];
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
