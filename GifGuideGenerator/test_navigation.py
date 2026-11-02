"""Navigation listing validation and generated-scene checks; no temporary files."""

import copy
from io import BytesIO, TextIOWrapper
import json
import unittest
from unittest.mock import patch

import generate
import navigation


def entry(name, kind="file"):
    return {"name": name, "kind": kind, "modified": "", "size": "",
            "type": {"zh": "文件夹" if kind == "folder" else "文件",
                     "en": "File folder" if kind == "folder" else "File"}}


def listing():
    return {"schemaVersion": 1, "levels": [
        {"path": "Game", "complete": True, "target": "logs",
         "entries": [entry("Engine", "folder"), entry("logs", "folder"), entry("settings.ini")]},
        {"path": "Game/logs", "complete": True, "target": "output.log",
         "entries": [entry("backup", "folder"), entry("output.log"), entry("settings.ini")]}]}


class NavigationTests(unittest.TestCase):
    def test_invalid_listing_is_not_silently_corrected(self):
        changes = [
            lambda d: d["levels"][0].update(complete=False),
            lambda d: d["levels"][0].update(target="missing"),
            lambda d: d["levels"][1].update(path="Game/other"),
            lambda d: d["levels"][1].update(target="backup"),
            lambda d: d["levels"][0]["entries"].append(entry("engine", "folder")),
            lambda d: d["levels"][0]["entries"][0].pop("type"),
            lambda d: d["levels"][0]["entries"][0].pop("modified"),
            lambda d: d["levels"][0].update(path="../Game"),
        ]
        for change in changes:
            data = listing()
            change(data)
            with self.subTest(change=change), self.assertRaises(ValueError):
                navigation.validate(data)

    def test_build_preserves_all_rows_and_selects_only_target(self):
        data = listing()
        original = copy.deepcopy(data)
        template = generate.read_config(generate.inside("templates/navigate-to-file.json"))
        built = navigation.build_template(template, data)
        self.assertEqual(data, original)
        self.assertNotIn("directory-0", template["scenes"])
        self.assertEqual(built["scenes"]["directory-1-selected"]["layers"][0]["selected"], 1)
        for lang in ("zh", "en"):
            game = {"id": "test", "navigation": data,
                    "uploadPage": {code: generate.inside(f"assets/upload/manual-{code}.png") for code in ("zh", "en")}}
            renderer = generate.Renderer(built, game, lang, generate.find_font())
            selected = renderer.scene("directory-1-selected")
            self.assertEqual(selected.getpixel((65, 270)), (204, 232, 255))
            self.assertEqual(selected.getpixel((65, 230)), (255, 255, 255))
            self.assertEqual(selected.getpixel((65, 320)), (255, 255, 255))
            for scene in built["previewScenes"]:
                self.assertEqual(renderer.scene(scene).size, (1238, 1020))

    def test_offscreen_target_has_scroll_pages_and_correct_row(self):
        data = {"schemaVersion": 1, "levels": [{"path": "Game", "complete": True,
                "target": "file-29.log", "entries": [entry(f"file-{i}.log") for i in range(30)]}]}
        template = generate.read_config(generate.inside("templates/navigate-to-file.json"))
        built = navigation.build_template(template, data)
        self.assertEqual(built["scenes"]["directory-0-selected"]["layers"][0]["start"], 28)
        self.assertIn("directory-0-scroll-14", built["scenes"])
        self.assertIn("directory-0-scroll-28", built["scenes"])
        self.assertEqual(len(data["levels"][0]["entries"]), 30)

    def test_stdin_cli_does_not_require_game_config_or_screenshots(self):
        stream = TextIOWrapper(BytesIO(json.dumps(listing()).encode("utf-8")), encoding="utf-8")
        with patch.object(generate.sys, "stdin", stream), patch.object(generate, "generate") as render:
            generate.main(["test", "--name", "Test", "--template", "navigate-to-file", "--navigation", "-"])
        game, languages, _, _ = render.call_args.args
        self.assertEqual(game["navigation"], listing())
        self.assertEqual(languages, ["zh", "en"])
        self.assertTrue(game["isIllustration"])

    def test_long_file_names_fail_instead_of_disappearing(self):
        data = listing()
        data["levels"][0]["entries"][0]["name"] = "long-name-" * 20
        font_path = generate.find_font()
        with self.assertRaisesRegex(ValueError, "truncation"):
            navigation.render_directory(data["levels"][0], "en",
                lambda size: generate.ImageFont.truetype(str(font_path), size), (1238, 1020))


if __name__ == "__main__":
    unittest.main()
