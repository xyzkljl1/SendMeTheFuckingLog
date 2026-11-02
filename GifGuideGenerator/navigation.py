"""Validate explicit directory listings and render a file-navigation demonstration."""

import copy
from pathlib import Path
from PIL import Image, ImageDraw

ROW_TOP = 220
ROW_HEIGHT = 46
VISIBLE_ROWS = 14


def plain(value, label, empty=False):
    if not isinstance(value, str) or (not empty and not value.strip()) or any(ord(c) < 32 for c in value):
        raise ValueError(f"{label} must be a single-line string")
    return value


def validate(data):
    if not isinstance(data, dict) or data.get("schemaVersion") != 1:
        raise ValueError("Navigation input requires schemaVersion 1")
    levels = data.get("levels")
    if not isinstance(levels, list) or not 1 <= len(levels) <= 8:
        raise ValueError("Navigation requires 1 to 8 directory levels")
    previous = None
    for index, level in enumerate(levels):
        if not isinstance(level, dict) or level.get("complete") is not True:
            raise ValueError("Each level must declare complete: true for its full listing")
        path = plain(level.get("path"), "directory path")
        if any(part in ("", ".", "..") for part in path.split("/")) or any(c in path for c in "\\:"):
            raise ValueError("Directory paths must use relative forward-slash components")
        if previous and path != previous["path"] + "/" + previous["target"]:
            raise ValueError("Directory levels must follow the preceding target exactly")
        target = plain(level.get("target"), "target")
        entries = level.get("entries")
        if not isinstance(entries, list) or not 1 <= len(entries) <= 200:
            raise ValueError("Each directory requires 1 to 200 entries in display order")
        names = set()
        for entry in entries:
            if not isinstance(entry, dict):
                raise ValueError("Each entry must be an object")
            name = plain(entry.get("name"), "entry name")
            if name in (".", "..") or any(c in name for c in '/\\:*?"<>|'):
                raise ValueError("Invalid file or folder name")
            if name.casefold() in names:
                raise ValueError("Duplicate file or folder name")
            names.add(name.casefold())
            if entry.get("kind") not in ("folder", "file"):
                raise ValueError("Entry kind must be folder or file")
            plain(entry.get("modified"), "modified", empty=True)
            plain(entry.get("size"), "size", empty=True)
            labels = entry.get("type")
            if not isinstance(labels, dict) or set(labels) != {"zh", "en"}:
                raise ValueError("Each entry needs explicit zh/en type labels")
            for text in labels.values():
                plain(text, "type label")
        matches = [entry for entry in entries if entry["name"] == target]
        expected = "file" if index == len(levels) - 1 else "folder"
        if len(matches) != 1 or matches[0]["kind"] != expected:
            raise ValueError(f"Target must be a listed {expected}: {target}")
        previous = level
    return data


def build_template(template, data):
    validate(data)
    result = copy.deepcopy(template)
    scenes = result["scenes"]
    timeline = []
    previews = []
    for index, level in enumerate(data["levels"]):
        target = next(i for i, entry in enumerate(level["entries"]) if entry["name"] == level["target"])
        start = target // VISIBLE_ROWS * VISIBLE_ROWS
        base = f"directory-{index}"
        selected = base + "-selected"
        layer = {"type": "explorer", "level": index, "start": 0}
        scenes[base] = {"layers": [layer]}
        timeline.append({"action": "hold", "scene": base, "at": [1010, 900], "durationMs": 1400})
        if start:
            # Show consecutive scroll positions, retaining the complete input listing.
            for offset in range(VISIBLE_ROWS, start + 1, VISIBLE_ROWS):
                page = f"{base}-scroll-{offset}"
                scenes[page] = {"layers": [{**layer, "start": offset}]}
                timeline.append({"action": "hold", "scene": page, "at": [1215, 740], "durationMs": 600})
            base = page
        layer = {**layer, "start": start, "selected": target}
        scenes[selected] = {"layers": [layer]}
        point = [210, ROW_TOP + (target - start) * ROW_HEIGHT + 14]
        timeline.extend([
            {"action": "move", "to": point, "durationMs": 480},
            {"action": "click", "durationMs": 160},
            {"action": "hold", "scene": selected, "durationMs": 100}])
        if index < len(data["levels"]) - 1:
            timeline.extend([{"action": "click", "durationMs": 160},
                             {"action": "hold", "durationMs": 250}])
        else:
            timeline.append({"action": "hold", "durationMs": 1800})
        previews.append(selected)

    # Refer to a rendered scene instead of requiring intermediate screenshot files.
    scenes["upload-ready"]["layers"].append({"type": "scene", "name": selected,
                                               "at": [8, 305], "size": [690, 568]})
    source = [125, round(305 + point[1] * 568 / 1020)]
    timeline.extend([
        {"action": "hold", "scene": "upload-ready", "at": source, "durationMs": 1700},
        {"action": "drag", "to": [925, 690], "durationMs": 1800, "icon": "file", "label": {"zh": "+ 复制", "en": "+ Copy"}},
        {"action": "drag", "scene": "dragging", "to": [925, 690], "durationMs": 600, "icon": "file", "label": {"zh": "+ 复制", "en": "+ Copy"}},
        {"action": "hold", "scene": "drop", "durationMs": 2000}])
    result["timeline"] = timeline
    # Keep every level in playback; preview samples at most five directory levels.
    if len(previews) > 5:
        previews = [previews[round(i * (len(previews) - 1) / 4)] for i in range(5)]
    result["previewScenes"] = previews + ["upload-ready", "dragging", "drop"]
    return result


def render_directory(level, language, font, size, selected=None, start=0):
    if size != (1238, 1020):
        raise ValueError("Directory renderer currently requires a 1238 x 1020 canvas")
    image = Image.new("RGB", size, "white")
    draw = ImageDraw.Draw(image)

    def text(at, value, width, fontsize=22, fill="#20252a", shorten=False):
        f = font(fontsize)
        if draw.textlength(value, font=f) > width:
            if not shorten:
                raise ValueError(f"Directory text does not fit without truncation: {value}")
            while draw.textlength(value, font=f) > width:
                value = value[1:]
            value = "… " + value[2:]
        draw.text(at, value, font=f, fill=fill)

    path = level["path"]
    current = path.split("/")[-1]
    draw.rectangle((0, 0, 1237, 44), fill="#e9e9e9")
    draw.rounded_rectangle((10, 6, 400, 44), radius=8, fill="#f9f9f9")
    text((30, 10), current, 350, 20, shorten=True)
    for x, label in ((1100, "−"), (1150, "□"), (1200, "×")):
        text((x, 7), label, 30, 23)
    draw.rectangle((0, 45, 1237, 110), fill="#fbfbfb")
    text((18, 65), "‹   ›   ↑", 120, 23, "#65717a")
    text((150, 65), "common  ›  " + "  ›  ".join(path.split("/")), 680, 22, shorten=True)
    draw.rounded_rectangle((865, 55, 1220, 101), radius=5, fill="#f0f2f4")
    search = f"在 {current} 中搜索" if language == "zh" else f"Search {current}"
    text((880, 65), search, 325, 20, "#69747d", shorten=True)
    draw.line((0, 111, 1238, 111), fill="#d5d5d5")
    with Image.open(Path(__file__).resolve().parent / f"assets/explorer/toolbar-{language}.png") as toolbar:
        image.paste(toolbar.convert("RGB"), (0, 120))
    text((1080, 131), "详细信息" if language == "zh" else "Details", 130, 20)
    draw.line((0, 174, 1237, 174), fill="#d5d5d5")
    draw.rectangle((0, 175, 45, 979), fill="#f4f5f6")
    columns = [(80, "名称", "Name", 440), (540, "修改日期", "Date modified", 210),
               (780, "类型", "Type", 275), (1100, "大小", "Size", 120)]
    for x, zh, en, width in columns:
        text((x, 183), zh if language == "zh" else en, width, 20, "#71859a")
    entries = level["entries"]
    for offset, entry in enumerate(entries[start:start + VISIBLE_ROWS]):
        y = ROW_TOP + offset * ROW_HEIGHT
        if selected == start + offset:
            draw.rectangle((60, y, 1210, y + 41), fill="#cce8ff", outline="#4ba9ee")
        if entry["kind"] == "folder":
            draw.rectangle((78, y + 10, 91, y + 16), fill="#edba3c")
            draw.rectangle((78, y + 15, 102, y + 31), fill="#ffd45d")
        else:
            draw.rectangle((81, y + 7, 100, y + 33), fill="#fafafa", outline="#a2a6aa")
            draw.line((95, y + 7, 95, y + 14, 100, y + 14), fill="#a2a6aa")
            if entry["name"].lower().endswith(".log"):
                for dy in (18, 22, 26):
                    draw.line((85, y + dy, 96, y + dy), fill="#90979d")
        text((115, y + 6), entry["name"], 405, 23)
        text((540, y + 8), entry["modified"], 220, 19, "#525b64")
        text((780, y + 8), entry["type"][language], 285, 19, "#525b64")
        text((1100, y + 8), entry["size"], 110, 19, "#525b64")
    if len(entries) > VISIBLE_ROWS:
        draw.rectangle((1222, ROW_TOP, 1227, 951), fill="#eeeeee")
        height = max(25, round(730 * VISIBLE_ROWS / len(entries)))
        top = ROW_TOP + round((730 - height) * start / max(1, len(entries) - 1))
        draw.rounded_rectangle((1222, top, 1227, min(951, top + height)), radius=2, fill="#8c959d")
    draw.line((0, 980, 1237, 980), fill="#dedede")
    count = len(entries)
    status = f"{count} 个项目" if language == "zh" else f"{count} {'item' if count == 1 else 'items'}"
    if selected is not None:
        status += "   |   已选择 1 个项目" if language == "zh" else "   |   1 item selected"
    text((65, 990), status, 1050, 18, "#5d6872")
    return image
