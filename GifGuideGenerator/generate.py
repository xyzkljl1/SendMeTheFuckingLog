"""Generate screenshot animations from command-line inputs and a reusable template."""

import argparse
import json
import math
import os
from pathlib import Path
import re
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
TOKEN = re.compile(r"\{([a-zA-Z][a-zA-Z0-9_.]*)\}")
IDENTIFIER = re.compile(r"[a-z0-9]+(?:-[a-z0-9]+)*")
MAX_FRAMES = 180
MAX_FRAME_BYTES = 400 * 1024 * 1024


def inside(value, root=ROOT):
    """All tool-managed inputs and outputs stay below their declared root."""
    if not isinstance(value, str) or not value or "\\" in value or ":" in value:
        raise ValueError(f"Expected a relative path using forward slashes: {value!r}")
    relative = Path(value)
    if relative.is_absolute() or any(part in ("", ".", "..") for part in value.split("/")):
        raise ValueError(f"Unsafe path: {value}")
    resolved = (root / relative).resolve()
    if not resolved.is_relative_to(root.resolve()):
        raise ValueError(f"Path escapes tool directory: {value}")
    return resolved


def read_config(path):
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(data, dict) or data.get("schemaVersion") != 1:
        raise ValueError(f"Expected schemaVersion 1 in {path.name}")
    return data


def expand(value, variables, language, depth=0):
    """Expand typed references and bilingual values; never evaluate expressions."""
    if depth > 12:
        raise ValueError("Circular or excessively nested template reference")
    if isinstance(value, dict):
        if set(value) == {"zh", "en"}:
            return expand(value[language], variables, language, depth + 1)
        return {key: expand(item, variables, language, depth + 1) for key, item in value.items()}
    if isinstance(value, list):
        return [expand(item, variables, language, depth + 1) for item in value]
    if not isinstance(value, str):
        return value

    def lookup(match):
        result = variables
        for part in match.group(1).split("."):
            if not isinstance(result, dict) or part not in result:
                raise ValueError(f"Unknown template variable: {match.group(0)}")
            result = result[part]
        return expand(result, variables, language, depth + 1)

    full = TOKEN.fullmatch(value)
    if full:
        return lookup(full)

    def substitute(match):
        item = lookup(match)
        if not isinstance(item, (str, int, float)):
            raise ValueError(f"Non-scalar variable inside text: {match.group(0)}")
        return str(item)

    result = TOKEN.sub(substitute, value)
    if "{" in result or "}" in result:
        raise ValueError(f"Invalid template placeholder: {result}")
    return result


def positive_int(value, label, maximum):
    if type(value) is not int or not 1 <= value <= maximum:
        raise ValueError(f"{label} must be an integer between 1 and {maximum}")
    return value


def pair(value, label):
    if not isinstance(value, list) or len(value) != 2 or any(type(n) is not int for n in value):
        raise ValueError(f"{label} must contain two integer coordinates")
    return tuple(value)


def find_font(override=None):
    if override:
        path = Path(override).expanduser().resolve()
        if not path.is_file():
            raise ValueError(f"Font file not found: {path}")
        return path
    candidates = [
        Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts/msyh.ttc",
        Path("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc"),
        Path("/System/Library/Fonts/PingFang.ttc"),
    ]
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    raise ValueError("No CJK-capable font found. Pass --font PATH; the tool does not install fonts.")


class Renderer:
    def __init__(self, template, game, language, font):
        if language not in ("zh", "en"):
            raise ValueError("Language must be zh or en")
        variables = {"game": game, "lang": language, "layout": template.get("layout", {})}
        self.template = expand(template, variables, language)
        self.size = pair(self.template["canvas"], "canvas")
        width, height = self.size
        if not 64 <= width <= 1920 or not 64 <= height <= 1440 or width * height > 2200000:
            raise ValueError("Canvas must fit within 1920 x 1440 and 2.2 million pixels")
        self.font_path = font
        self.fonts = {}
        self.scenes = {}
        self.assets = {}
        self.frame_ms = positive_int(self.template.get("frameMs", 90), "frameMs", 1000)
        if self.frame_ms % 10:
            raise ValueError("frameMs must be a multiple of 10 for GIF timing")

    def font(self, size):
        positive_int(size, "font size", 200)
        if size not in self.fonts:
            self.fonts[size] = ImageFont.truetype(str(self.font_path), size)
        return self.fonts[size]

    def scene(self, name, ancestors=()):
        if name in ancestors:
            raise ValueError(f"Circular scene inheritance: {name}")
        if name in self.scenes:
            return self.scenes[name]
        definition = self.template["scenes"].get(name)
        if not isinstance(definition, dict):
            raise ValueError(f"Unknown scene: {name}")
        parent = definition.get("extends")
        image = self.scene(parent, (*ancestors, name)).copy() if parent else Image.new("RGB", self.size, "#23282f")
        draw = ImageDraw.Draw(image)
        for layer in definition.get("layers", []):
            kind = layer["type"]
            if kind == "image":
                # Path objects come only from explicit CLI inputs; template paths stay confined.
                path = layer["path"] if isinstance(layer["path"], Path) else inside(layer["path"])
                if path not in self.assets:
                    with Image.open(path) as asset:
                        asset.load()
                        self.assets[path] = asset.convert("RGBA")
                asset = self.assets[path]
                if "crop" in layer:
                    box = layer["crop"]
                    if (not isinstance(box, list) or len(box) != 4
                            or any(type(n) is not int for n in box)
                            or not 0 <= box[0] < box[2] <= asset.width
                            or not 0 <= box[1] < box[3] <= asset.height):
                        raise ValueError("Invalid image crop rectangle")
                    asset = asset.crop(box)
                if "size" in layer:
                    size = pair(layer["size"], "image size")
                    if any(n < 1 for n in size) or size[0] * size[1] > 2200000:
                        raise ValueError("Invalid image layer dimensions")
                    asset = asset.resize(size, Image.Resampling.LANCZOS)
                x, y = pair(layer["at"], "image position")
                self.check_box((x, y, x + asset.width - 1, y + asset.height - 1))
                image.paste(asset, (x, y), asset)
            elif kind == "rectangle":
                box = layer["box"]
                self.check_box(box)
                draw.rectangle(box, fill=layer.get("fill"), outline=layer.get("outline"),
                               width=positive_int(layer.get("width", 1), "outline width", 20))
            elif kind == "text":
                x, y = pair(layer["at"], "text position")
                size = positive_int(layer.get("size", 22), "text size", 200)
                value = str(layer["value"])
                available = positive_int(layer.get("maxWidth", self.size[0] - x), "text width", self.size[0])
                while size > 12 and draw.textlength(value, font=self.font(size)) > available:
                    size -= 1
                if draw.textlength(value, font=self.font(size)) > available:
                    raise ValueError(f"Text does not fit: {value}")
                bounds = draw.textbbox((x, y), value, font=self.font(size))
                self.check_box(bounds)
                draw.text((x, y), value, font=self.font(size), fill=layer.get("fill", "white"))
            else:
                raise ValueError(f"Unknown layer type: {kind}")
        self.scenes[name] = image
        return image

    def check_box(self, box):
        if len(box) != 4 or any(type(n) is not int for n in box):
            raise ValueError("A rectangle must contain four integer coordinates")
        x0, y0, x1, y1 = box
        if not (0 <= x0 <= x1 < self.size[0] and 0 <= y0 <= y1 < self.size[1]):
            raise ValueError(f"Layer is outside the canvas: {box}")

    def check_cursor(self, position):
        x, y = position
        self.check_box((x, y, x + 28, y + 38))

    def frames(self):
        frames, durations, first_times = [], [], {}
        scene = None
        position = None
        elapsed = 0

        def append(point, duration, pulse=None, drag_label=None, marquee=None):
            nonlocal elapsed
            if len(frames) >= MAX_FRAMES or (len(frames) + 1) * self.size[0] * self.size[1] * 3 > MAX_FRAME_BYTES:
                raise ValueError("Animation exceeds the frame/memory limit; reduce resolution or movement frames")
            image = self.scene(scene).copy()
            if marquee is not None:
                self.draw_marquee(image, marquee[0], point, marquee[1], marquee[2])
            if drag_label is not None:
                self.draw_drag(image, point, drag_label)
            draw_cursor(image, point, pulse)
            frames.append(image)
            durations.append(duration)
            first_times.setdefault(scene, elapsed)
            elapsed += duration

        timeline = self.template.get("timeline", [])
        if not timeline or len(timeline) > 100:
            raise ValueError("Timeline must contain 1 to 100 actions")
        for step in timeline:
            scene = step.get("scene", scene)
            if scene is None:
                raise ValueError("The first action must select a scene")
            if "at" in step:
                position = pair(step["at"], "cursor position")
            if position is None:
                raise ValueError("The first action must set the cursor position")
            self.check_cursor(position)
            duration = positive_int(step["durationMs"], "durationMs", 10000)
            if duration % 10:
                raise ValueError("Action duration must be a multiple of 10 ms")
            action = step["action"]
            if action == "hold":
                append(position, duration)
            elif action in ("move", "click", "drag", "marquee"):
                target = pair(step["to"], "cursor destination") if action != "click" else position
                self.check_cursor(target)
                marquee = None
                if action == "marquee":
                    anchor = pair(step.get("anchor", list(position)), "selection anchor")
                    self.check_cursor(anchor)
                    rows = step.get("rows")
                    if not isinstance(rows, list) or not rows:
                        raise ValueError("Marquee action requires non-empty row rectangles")
                    for row in rows:
                        self.check_box(row)
                    marquee = (anchor, rows, self.scene(step["selectedScene"]))
                if action == "click" and step.get("button", "left") not in ("left", "right"):
                    raise ValueError("Click button must be left or right")
                for index, ms in enumerate(split_duration(duration, self.frame_ms)):
                    count = math.ceil(duration / self.frame_ms)
                    t = (index + 1) / count
                    eased = t * t * (3 - 2 * t)
                    point = tuple(round(a + (b - a) * eased) for a, b in zip(position, target))
                    append(point, ms, index / max(1, count - 1) if action == "click" else None,
                           step.get("label", "") if action == "drag" else None, marquee)
                position = target
            else:
                raise ValueError(f"Unknown timeline action: {action}")
        return frames, durations, first_times

    def draw_marquee(self, image, anchor, point, rows, selected):
        left, right = sorted((anchor[0], point[0]))
        top, bottom = sorted((anchor[1], point[1]))
        for x0, y0, x1, y1 in rows:
            if left < x1 and right > x0 and top < y1 and bottom > y0:
                image.paste(selected.crop((x0, y0, x1 + 1, y1 + 1)), (x0, y0))
        overlay = Image.new("RGBA", image.size)
        draw = ImageDraw.Draw(overlay)
        draw.rectangle((left, top, right, bottom), fill=(0, 120, 215, 65),
                       outline=(0, 120, 215, 255), width=2)
        image.paste(overlay, (0, 0), overlay)

    def draw_drag(self, image, position, label):
        draw = ImageDraw.Draw(image)
        font = self.font(20)
        width = max(120, math.ceil(draw.textlength(label, font=font)) + 32)
        height = 90
        if width + 16 > image.width or height + 16 > image.height:
            raise ValueError("Drag label does not fit on canvas")
        x = max(8, min(position[0] + 28, image.width - width - 8))
        y = max(8, min(position[1] + 32, image.height - height - 8))
        draw.rounded_rectangle((x + 5, y + 5, x + width + 5, y + height + 5), radius=5, fill="#d1d9dc")
        draw.rounded_rectangle((x, y, x + width, y + height), radius=5, fill="#ffffff", outline="#74898d", width=2)
        draw.rectangle((x + 16, y + 14, x + 43, y + 35), fill="#ffd778", outline="#c59c36")
        draw.rectangle((x + 21, y + 20, x + 48, y + 41), fill="#ffe4a0", outline="#c59c36")
        draw.text((x + 16, y + 50), label, font=font, fill="#176e67")


def split_duration(duration, frame_ms):
    count = math.ceil(duration / frame_ms)
    ticks, extra = divmod(duration // 10, count)
    return [(ticks + (index < extra)) * 10 for index in range(count)]


def draw_cursor(image, position, pulse=None):
    x, y = position
    draw = ImageDraw.Draw(image)
    if pulse is not None:
        radius = 12 + int(pulse * 22)
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), outline="#fbc967", width=3)
    points = [(0, 0), (0, 28), (8, 22), (15, 36), (21, 33), (14, 20), (26, 20)]
    draw.polygon([(x + a + 2, y + b + 2) for a, b in points], fill="#11171e")
    draw.polygon([(x + a, y + b) for a, b in points], fill="white", outline="#11171e", width=2)


def encode_gif(frames, durations, destination, comment):
    # Sample across the entire animation, including the last scene, for a stable palette.
    samples = [frames[round(i * (len(frames) - 1) / 5)] for i in range(6)]
    atlas = Image.new("RGB", (frames[0].width * 3, frames[0].height * 2))
    for index, sample in enumerate(samples):
        atlas.paste(sample, ((index % 3) * sample.width, (index // 3) * sample.height))
    palette = atlas.quantize(colors=240, method=Image.Quantize.MEDIANCUT)
    # Small UI accents can otherwise disappear when white webpage pixels dominate.
    accents = ["ffd45d", "edba3c", "cce8ff", "48a8ef", "176e67", "217d70", "fbc967", "ffffff",
               "000000", "23282f", "253b40", "14191e", "414a53", "71859a", "ffe4a0", "c59c36"]
    palette.putpalette(palette.getpalette()[:720] +
                       [int(color[index:index + 2], 16) for color in accents for index in (0, 2, 4)])
    encoded = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    encoded[0].save(destination, format="GIF", save_all=True, append_images=encoded[1:],
                    duration=durations, loop=0, disposal=1, optimize=True, comment=comment.encode("utf-8"))


def verify_gif(path, size, duration, sample_times):
    snapshots = {}
    elapsed = 0
    with Image.open(path) as gif:
        if gif.size != size or gif.info.get("loop") != 0:
            raise ValueError("Encoded GIF has incorrect dimensions or looping")
        count = gif.n_frames
        for index in range(count):
            gif.seek(index)
            gif.load()
            ms = gif.info.get("duration", 0)
            if ms <= 0:
                raise ValueError("Encoded GIF contains a frame without a duration")
            for name, when in sample_times.items():
                if elapsed <= when < elapsed + ms:
                    snapshots[name] = gif.convert("RGB")
            elapsed += ms
        if elapsed != duration:
            raise ValueError(f"GIF duration mismatch: {elapsed} != {duration}")
    return count, snapshots


def generate(game, languages, output, font):
    for field in ("id", "template"):
        if not isinstance(game.get(field), str) or not IDENTIFIER.fullmatch(game[field]):
            raise ValueError(f"Invalid {field}: {game.get(field)}")
    template = read_config(inside(f"templates/{game['template']}.json"))
    for language in languages:
        renderer = Renderer(template, game, language, font)
        name = renderer.template["outputName"]
        if not IDENTIFIER.fullmatch(name):
            raise ValueError(f"Invalid output name: {name}")
        scenes = renderer.template.get("previewScenes", [])
        if not scenes or len(scenes) > 8:
            raise ValueError("previewScenes must contain 1 to 8 scene names")
        frames, durations, first_times = renderer.frames()
        if any(scene not in first_times for scene in scenes):
            raise ValueError("Every preview scene must occur in the timeline")
        output.mkdir(parents=True, exist_ok=True)
        path = inside(name + ".gif", output)
        comment = json.dumps({"game": game['id'], "language": language,
                              "template": game['template'], "illustration": game.get("isIllustration", False)})
        encode_gif(frames, durations, path, comment)
        count, snapshots = verify_gif(path, renderer.size, sum(durations), {scene: first_times[scene] for scene in scenes})
        preview = Image.new("RGB", (renderer.size[0] * 2, renderer.size[1] * math.ceil(len(scenes) / 2)), "#23282f")
        for index, scene in enumerate(scenes):
            preview.paste(snapshots[scene], ((index % 2) * renderer.size[0], (index // 2) * renderer.size[1]))
        preview.thumbnail((1280, 1600), Image.Resampling.LANCZOS)
        preview.save(inside(name + "-preview.png", output))
        print(f"{path.relative_to(ROOT)}: {count} frames, {sum(durations)/1000:.2f}s, {path.stat().st_size:,} bytes")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""Examples (run from the project root):
  python -B GifGuideGenerator/generate.py my-game --name "My Game" --icon "icon.png" --directory "directory-{lang}.png"

Provide game inputs on each invocation; no per-game configuration is saved.
Input paths are read-only, absolute or relative to the current working directory.
--name supplies the Chinese/default name; --name-en optionally supplies the English name.
Use {lang} in --directory for directory-zh.png and directory-en.png, or pass one image for both.
New operations use templates/<template-id>.json: scenes/layers + hold/move/click timeline.
{lang} selects zh/en; {game.name} selects the localized name automatically.
Outputs stay in GifGuideGenerator/output; no uploads, publishing or changes to Steam.
Use a CJK font with --font if automatic font discovery is unavailable.
""")
    parser.add_argument("game", help="game ID used in output filenames, e.g. my-game")
    parser.add_argument("--name", required=True, help="Chinese/default display name")
    parser.add_argument("--name-en", help="English display name (defaults to --name)")
    parser.add_argument("--icon", required=True, help="game icon image, read-only")
    parser.add_argument("--directory", required=True, help="directory screenshot path; supports {lang}")
    parser.add_argument("--directory-selected", help="screenshot of all items selected; supports {lang}; required by the drag template")
    parser.add_argument("--template", default="steam-open-directory", help="reusable operation template ID")
    parser.add_argument("--illustration", action="store_true", help="mark GIF metadata as an illustration")
    parser.add_argument("--language", choices=("both", "zh", "en"), default="both")
    parser.add_argument("--output", help="optional subfolder inside the tool's output directory")
    parser.add_argument("--font", help="path to an installed CJK-capable TTF/OTF/TTC font (read-only)")
    args = parser.parse_args(argv)
    if not IDENTIFIER.fullmatch(args.game):
        parser.error("Invalid game ID")
    if not IDENTIFIER.fullmatch(args.template):
        parser.error("Invalid template ID")
    if not args.name.strip() or (args.name_en is not None and not args.name_en.strip()):
        parser.error("Display names must not be empty")
    output = inside("output")
    if args.output:
        output = inside(args.output, output)
    languages = ["zh", "en"] if args.language == "both" else [args.language]
    icon = Path(args.icon).expanduser().resolve()
    directories = {lang: Path(args.directory.replace("{lang}", lang)).expanduser().resolve()
                   for lang in ("zh", "en")}
    template = read_config(inside(f"templates/{args.template}.json"))
    if template.get("requiresSelectedDirectory") and not args.directory_selected:
        parser.error("This template requires --directory-selected")
    selected = ({lang: Path(args.directory_selected.replace("{lang}", lang)).expanduser().resolve()
                 for lang in ("zh", "en")} if args.directory_selected else {})
    for path in [icon, *(directories[lang] for lang in languages),
                 *(selected[lang] for lang in languages if selected)]:
        if not path.is_file():
            raise ValueError(f"Input image not found: {path}")
    game = {"id": args.game, "template": args.template,
            "name": {"zh": args.name, "en": args.name_en or args.name},
            "icon": icon, "directoryImage": directories, "selectedDirectoryImage": selected,
            "isIllustration": args.illustration}
    font = find_font(args.font)
    generate(game, languages, output, font)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, TypeError, OSError) as error:
        print(f"Generation failed: {error}", file=sys.stderr)
        raise SystemExit(1)
