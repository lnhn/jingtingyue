import re
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tts-server"))
from mixed_phonemes import to_phonemes


class RoutingTests(unittest.TestCase):
    def convert(self, text):
        self.chinese_calls = []
        self.english_calls = []

        def chinese(fragment):
            self.chinese_calls.append(fragment)
            return "zh"

        def english(fragment):
            self.english_calls.append(fragment)
            return "en"

        return to_phonemes(text, chinese, english)

    def test_pure_chinese_uses_original_whole_sentence(self):
        self.assertEqual(self.convert("中文123，继续阅读。"), "zh")
        self.assertEqual(self.chinese_calls, ["中文123，继续阅读。"])
        self.assertEqual(self.english_calls, [])

    def test_pure_english_keeps_sentence_context(self):
        self.convert("Hello world, Python and AI.")
        self.assertEqual(self.english_calls, ["Hello world, Python and AI."])
        self.assertEqual(self.chinese_calls, [])

    def test_mixed_phrases_get_english_conversion(self):
        self.convert("这里说 Hello world，使用 Python，然后学习 machine learning。")
        self.assertEqual(self.english_calls, ["Hello world", "Python", "machine learning"])
        self.assertFalse(any(re.search(r"[A-Za-z]", fragment) for fragment in self.chinese_calls))

    def test_contractions_and_numbered_terms_stay_together(self):
        self.convert("请读 I can't use GPT-4 and Windows 11 这一句。")
        self.assertEqual(self.english_calls, ["I can't use GPT-4 and Windows 11"])

    def test_acronyms_code_terms_and_accented_letters(self):
        self.convert("解释 AI、3D、C++、C#、U.S.A. 和 café 的发音。")
        self.assertEqual(self.english_calls, ["AI", "3D", "C++", "C#", "U.S.A", "café"])


class RealConverterTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from misaki.zh import ZHG2P
        from kokoro_onnx.tokenizer import Tokenizer
        cls.zh = ZHG2P()
        cls.tokenizer = Tokenizer()

    def test_english_words_become_real_phonemes(self):
        for phrase in ["Hello world", "Python", "AI", "machine learning", "GPT-4", "Windows 11"]:
            with self.subTest(phrase=phrase):
                result = to_phonemes(f"这里是{phrase}，请继续。", self.zh, self.tokenizer.phonemize)
                self.assertIn(self.tokenizer.phonemize(phrase), result)
                self.assertNotIn(phrase, result)
                self.assertIsNone(re.search(r"[\u4e00-\u9fff]", result))
                self.assertIn("，", result)
                self.assertIn("。", result)

    def test_numeric_chinese_fragment_at_start(self):
        result = to_phonemes("2024年使用Python编程。", self.zh, self.tokenizer.phonemize)
        self.assertIsNone(re.search(r"[\u4e00-\u9fff0-9]", result))
        self.assertIn(self.tokenizer.phonemize("Python"), result)

    def test_pure_chinese_phonemes_unchanged(self):
        text = "我们今天读中文，这是第二句话。"
        self.assertEqual(to_phonemes(text, self.zh, self.tokenizer.phonemize), self.zh(text))


if __name__ == "__main__":
    unittest.main()
