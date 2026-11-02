(function (root) {
    "use strict";
    const normalize = path => path.replace(/\\/g, "/").replace(/^\/+|\/+$/g, "").toLowerCase();
    // Sources expose names and lazy file readers; identifying a game never reads executable contents.
    function droppedSource(entries) {
        const folder = entries.length === 1 && entries[0].isDirectory ? entries[0].name : "";
        // One folder is the installation root; several items are its immediate contents.
        return { entries, folder, prefixes: [folder ? folder + "/" : ""] };
    }
    function pickedSource(files) {
        const roots = new Set(files.map(file => file.webkitRelativePath.split("/")[0]));
        if (roots.size !== 1 || files.some(file => !file.webkitRelativePath.includes("/"))) throw new Error("folderOnly");
        const folder = [...roots][0];
        return { files, folder, prefixes: [folder + "/"] };
    }
    async function walk(source, paths, onMatch, cancelled) {
        const targets = new Set(paths.map(normalize));
        const prefixes = new Set();
        for (const path of targets) {
            const parts = path.split("/");
            for (let index = 1; index < parts.length; index++) prefixes.add(parts.slice(0, index).join("/"));
        }
        let visited = 0;
        const check = () => {
            if (cancelled()) throw new Error("cancelled");
            if (++visited > 100000) throw new Error("tooMany");
        };
        if (source.files) {
            for (const file of source.files) {
                check();
                const path = normalize(file.webkitRelativePath);
                if (targets.has(path)) await onMatch(path, () => Promise.resolve(file));
                if (visited % 200 === 0) await new Promise(resolve => setTimeout(resolve, 0));
            }
            return;
        }
        async function visit(entry, path) {
            check();
            path = normalize(path);
            if (entry.isFile && targets.has(path)) {
                await onMatch(path, () => new Promise((resolve, reject) => entry.file(resolve, reject)));
            } else if (entry.isDirectory && prefixes.has(path)) {
                const reader = entry.createReader();
                while (!cancelled()) {
                    const batch = await new Promise((resolve, reject) => reader.readEntries(resolve, reject));
                    if (cancelled()) throw new Error("cancelled");
                    if (!batch.length) break;
                    for (const child of batch) await visit(child, path + "/" + child.name);
                    await new Promise(resolve => setTimeout(resolve, 0));
                }
            }
        }
        for (const entry of source.entries) await visit(entry, entry.name);
    }
    async function rootItems(source, onItem, cancelled) {
        let visited = 0;
        const check = () => {
            if (cancelled()) throw new Error("cancelled");
            if (++visited > 100000) throw new Error("tooMany");
        };
        if (source.files) {
            // Folder pickers expose files, so infer direct subdirectories from their paths.
            // Empty directories are not exposed by this browser API.
            const seen = new Set();
            for (const file of source.files) {
                check();
                const parts = normalize(file.webkitRelativePath).split("/").slice(1);
                const directory = parts.length > 1;
                const key = (directory ? "d:" : "f:") + parts[0];
                if (!seen.has(key)) { seen.add(key); onItem(parts[0], directory); }
                if (visited % 200 === 0) await new Promise(resolve => setTimeout(resolve, 0));
            }
        } else if (source.folder) {
            const reader = source.entries[0].createReader();
            while (true) {
                if (cancelled()) throw new Error("cancelled");
                const batch = await new Promise((resolve, reject) => reader.readEntries(resolve, reject));
                if (cancelled()) throw new Error("cancelled");
                if (!batch.length) break;
                for (const entry of batch) { check(); onItem(entry.name, entry.isDirectory); }
                await new Promise(resolve => setTimeout(resolve, 0));
            }
        } else {
            for (const entry of source.entries) {
                check(); onItem(entry.name, entry.isDirectory);
                if (visited % 200 === 0) await new Promise(resolve => setTimeout(resolve, 0));
            }
        }
    }
    function createRecognizer(games, mappings = {}) {
        const supported = games.filter(game => !game.isDemo);
        const byId = new Map(supported.map(game => [game.id, game]));
        const ue = new Map(), re = new Map(), genericIds = new Set();
        const excluded = new Set(["engine", "mods", "platform", "steam"]);
        for (const [entries, index] of [[mappings.ue || {}, ue], [mappings.re || {}, re]]) {
            for (const [name, id] of Object.entries(entries)) {
                if (!byId.has(id)) continue;
                index.set(name.toLowerCase(), byId.get(id)); genericIds.add(id);
            }
        }
        const independent = supported.filter(game => !genericIds.has(game.id));
        return {
            async identify(source, cancelled) {
                if (cancelled()) throw new Error("cancelled");
                const hits = new Set();
                if (ue.size || re.size) {
                    await rootItems(source, (name, directory) => {
                        name = name.toLowerCase();
                        const game = directory ? (!excluded.has(name) && ue.get(name))
                            : (name.endsWith(".exe") && re.get(name));
                        if (game) hits.add(game);
                    }, cancelled);
                }
                // Keep all root-level candidates for user confirmation; do not scan independent rules after a hit.
                if (hits.size) return supported.filter(game => hits.has(game))
                    .map(game => ({ game, prefix: source.prefixes[0] }));
                const candidates = independent.flatMap(game => source.prefixes.map(prefix => ({ game, prefix })));
                const paths = new Map();
                for (const candidate of candidates) {
                    for (const path of candidate.game.executablePaths || []) {
                        const key = normalize(candidate.prefix + path);
                        if (!paths.has(key)) paths.set(key, []);
                        paths.get(key).push(candidate);
                    }
                }
                if (!paths.size) return [];
                const matches = new Set();
                await walk(source, [...paths.keys()], path => {
                    paths.get(path).forEach(candidate => matches.add(candidate));
                }, cancelled);
                return candidates.filter(candidate => matches.has(candidate));
            }
        };
    }
    async function identifyGames(games, source, cancelled) {
        return createRecognizer(games).identify(source, cancelled);
    }
    async function collectFiles(candidate, source, cancelled) {
        const { game, prefix } = candidate;
        const paths = game.files.flatMap(item => item.paths.map(path => prefix + path));
        const found = new Map();
        await walk(source, paths, (path, read) => {
            for (const item of game.files) {
                if (!item.paths.some(expected => normalize(prefix + expected) === path)) continue;
                // Do not silently combine files from multiple roots or alternative locations.
                if (found.has(item.id)) throw new Error("duplicateFiles");
                found.set(item.id, { item, read });
            }
        }, cancelled);
        const records = [];
        for (const { item, read } of found.values()) {
            if (cancelled()) throw new Error("cancelled");
            const file = await read();
            if (cancelled()) throw new Error("cancelled");
            records.push({ game, item, file });
        }
        return records;
    }
    function matchManualFile(game, name) {
        return game.files.find(item => item.paths.some(path =>
            normalize(path).split("/").pop() === normalize(name))) || null;
    }
    const rules = { droppedSource, pickedSource, createRecognizer, identifyGames, collectFiles, matchManualFile };
    if (typeof module !== "undefined" && module.exports) module.exports = rules;
    else root.diagnosticRules = rules;
})(globalThis);
