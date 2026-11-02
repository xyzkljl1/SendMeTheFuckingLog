"""In-memory regression checks: python -B -m unittest discover -s GifGuideGenerator."""

from io import BytesIO
import unittest
from unittest.mock import patch
from contextlib import redirect_stderr
from io import StringIO

import generate


class GeneratorTests(unittest.TestCase):
    def minimal_template(self):
        return {
            "canvas": [64, 64], "frameMs": 90,
            "scenes": {"first": {"layers": []}, "last": {"layers": [
                {"type": "rectangle", "box": [0, 0, 63, 63], "fill": "white"}]}},
            "timeline": [
                {"action": "hold", "scene": "first", "at": [4, 4], "durationMs": 100},
                {"action": "move", "to": [20, 20], "durationMs": 250},
                {"action": "click", "button": "right", "durationMs": 150},
                {"action": "hold", "scene": "last", "durationMs": 200}],
        }

    def test_localized_and_typed_references(self):
        variables = {"lang": "en", "game": {"name": {"zh": "影之刃零", "en": "Phantom Blade Zero"},
                     "image": "directory-{lang}.png"}, "point": [10, 20]}
        self.assertEqual(generate.expand("{game.name}", variables, "en"), "Phantom Blade Zero")
        self.assertEqual(generate.expand("{game.name}", variables, "zh"), "影之刃零")
        self.assertEqual(generate.expand("{game.image}", variables, "en"), "directory-en.png")
        self.assertEqual(generate.expand("{point}", variables, "en"), [10, 20])
        for value in ("{missing}", "point={point}", "{bad-name}"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                generate.expand(value, variables, "en")
        with self.assertRaises(ValueError):
            generate.expand("{loop}", {"loop": "{loop}"}, "en")

    def test_paths_cannot_escape_tool(self):
        for value in ("../other", "/tmp/output", "C:/output", "a/../../b", "a\\b", "a//b", "."):
            with self.subTest(value=value), self.assertRaises(ValueError):
                generate.inside(value)
        self.assertEqual(generate.inside("assets/steam"), generate.ROOT / "assets" / "steam")

    def test_timing_and_encoded_gif(self):
        renderer = generate.Renderer(self.minimal_template(), {}, "en", None)
        frames, durations, starts = renderer.frames()
        self.assertEqual(sum(durations), 700)
        self.assertEqual(starts, {"first": 0, "last": 500})
        self.assertTrue(all(ms > 0 and ms % 10 == 0 for ms in durations))
        stream = BytesIO()
        generate.encode_gif(frames, durations, stream, "test")
        stream.seek(0)
        count, snapshots = generate.verify_gif(stream, (64, 64), 700, starts)
        self.assertGreater(count, 2)
        self.assertEqual(snapshots["last"].getpixel((63, 63)), (255, 255, 255))

    def test_invalid_timeline_and_scene(self):
        cases = [
            {"durationMs": 101}, {"action": "unknown"}, {"at": [50, 50]},
            {"scene": "missing"}, {"action": "click", "button": "middle"}]
        for change in cases:
            template = self.minimal_template()
            template["timeline"][0].update(change)
            with self.subTest(change=change), self.assertRaises(ValueError):
                generate.Renderer(template, {}, "en", None).frames()
        template = self.minimal_template()
        template["scenes"]["first"]["extends"] = "last"
        template["scenes"]["last"]["extends"] = "first"
        with self.assertRaises(ValueError):
            generate.Renderer(template, {}, "en", None).frames()

    def test_cli_inputs_render_without_game_config(self):
        assets = generate.ROOT / "assets/games/god-eater-2"
        with patch.object(generate, "generate") as render:
            generate.main(["test-game", "--name", "测试游戏", "--name-en", "Test Game",
                           "--icon", str(assets / "icon.png"),
                           "--directory", str(assets / "directory-{lang}.png")])
        game, languages, output, font = render.call_args.args
        self.assertEqual(languages, ["zh", "en"])
        self.assertEqual(output, generate.ROOT / "output")
        self.assertEqual(game["name"], {"zh": "测试游戏", "en": "Test Game"})
        template = generate.read_config(generate.inside(f"templates/{game['template']}.json"))
        for language in languages:
            renderer = generate.Renderer(template, game, language, font)
            self.assertEqual(renderer.template["scenes"]["directory"]["layers"][0]["path"],
                             assets / f"directory-{language}.png")
            for name in renderer.template["previewScenes"]:
                self.assertEqual(renderer.scene(name).size, (1238, 1020))

    def test_cli_single_language_and_default_name(self):
        image = generate.ROOT / "assets/games/god-eater-2/icon.png"
        with patch.object(generate, "generate") as render:
            generate.main(["test-game", "--name", "Test Game", "--language", "en",
                           "--icon", str(image), "--directory", str(image), "--illustration"])
        game, languages, _, _ = render.call_args.args
        self.assertEqual(languages, ["en"])
        self.assertEqual(game["name"], {"zh": "Test Game", "en": "Test Game"})
        self.assertTrue(game["isIllustration"])

    def test_drag_overlay_moves_and_disappears_on_release(self):
        template = self.minimal_template()
        template["canvas"] = [400, 200]
        template["timeline"] = [
            {"action": "drag", "scene": "first", "at": [10, 10], "to": [150, 30],
             "durationMs": 180, "label": "+ Copy"},
            {"action": "hold", "durationMs": 100}]
        renderer = generate.Renderer(template, {}, "en", generate.find_font())
        frames, durations, _ = renderer.frames()
        self.assertEqual(sum(durations), 280)
        self.assertNotEqual(frames[0].tobytes(), frames[1].tobytes())
        self.assertEqual(frames[1].getpixel((190, 100)), (255, 255, 255))
        self.assertEqual(frames[2].getpixel((190, 100)), (35, 40, 47))

    def test_long_template_requires_selected_image(self):
        image = generate.ROOT / "assets/games/god-eater-2/icon.png"
        with redirect_stderr(StringIO()), self.assertRaises(SystemExit) as error:
            generate.main(["test-game", "--name", "Test", "--icon", str(image),
                           "--directory", str(image), "--template", "steam-drag-to-upload"])
        self.assertEqual(error.exception.code, 2)

    def test_marquee_selects_intersected_rows_and_releases_rectangle(self):
        template = self.minimal_template()
        template["canvas"] = [200, 200]
        template["frameMs"] = 100
        rows = [[10, 40, 180, 60], [10, 100, 180, 120]]
        template["scenes"]["selected"] = {"layers": [
            {"type": "rectangle", "box": row, "fill": "#cce8ff"} for row in rows]}
        template["timeline"] = [
            {"action": "marquee", "scene": "first", "at": [150, 160], "to": [20, 10],
             "rows": rows, "selectedScene": "selected", "durationMs": 200},
            {"action": "hold", "scene": "selected", "durationMs": 100}]
        renderer = generate.Renderer(template, {}, "en", None)
        frames, durations, _ = renderer.frames()
        self.assertEqual(sum(durations), 300)
        self.assertEqual(frames[0].getpixel((15, 50)), (35, 40, 47))
        self.assertEqual(frames[0].getpixel((15, 110)), (204, 232, 255))
        self.assertEqual(frames[1].getpixel((15, 50)), (204, 232, 255))
        self.assertNotEqual(frames[1].getpixel((100, 150)), (35, 40, 47))
        self.assertEqual(frames[2].getpixel((100, 150)), (35, 40, 47))
        template["timeline"][0]["rows"] = [[0, 0, 300, 300]]
        with self.assertRaises(ValueError):
            generate.Renderer(template, {}, "en", None).frames()

    def test_long_template_both_languages_and_crop_validation(self):
        assets = generate.ROOT / "assets/games/phantom-blade-zero-demo"
        with patch.object(generate, "generate") as render:
            generate.main(["test-game", "--name", "测试", "--name-en", "Test",
                           "--icon", str(assets / "icon.png"),
                           "--directory", str(assets / "directory-drag-{lang}.png"),
                           "--directory-selected", str(assets / "directory-drag-selected-{lang}.png"),
                           "--template", "steam-drag-to-upload"])
        game, languages, _, font = render.call_args.args
        template = generate.read_config(generate.inside("templates/steam-drag-to-upload.json"))
        for lang in languages:
            renderer = generate.Renderer(template, game, lang, font)
            for name in renderer.template["previewScenes"]:
                self.assertEqual(renderer.scene(name).size, (1238, 1020))
            self.assertEqual(renderer.template["scenes"]["selected"]["layers"][0]["path"],
                             assets / f"directory-drag-selected-{lang}.png")
        template["scenes"]["upload-ready"]["layers"][0]["crop"] = [0, 0, 99999, 1042]
        with self.assertRaisesRegex(ValueError, "crop"):
            generate.Renderer(template, game, "en", font).scene("upload-ready")


if __name__ == "__main__":
    unittest.main()
