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
    async function identifyGames(games, source, cancelled) {
        const candidates = games.filter(game => !game.isDemo).flatMap(game => source.prefixes.map(prefix => ({ game, prefix })));
        const matches = new Set();
        const paths = candidates.flatMap(candidate => (candidate.game.executablePaths || []).map(path => candidate.prefix + path));
        await walk(source, paths, path => {
            candidates.forEach(candidate => {
                if ((candidate.game.executablePaths || []).some(expected => normalize(candidate.prefix + expected) === path)) matches.add(candidate);
            });
        }, cancelled);
        return candidates.filter(candidate => matches.has(candidate));
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
    const rules = { droppedSource, pickedSource, identifyGames, collectFiles, matchManualFile };
    if (typeof module !== "undefined" && module.exports) module.exports = rules;
    else root.diagnosticRules = rules;
})(globalThis);
