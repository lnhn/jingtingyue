import sys
import unittest
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tts-server"))
from punctuation_audio import clauses, synthesize, with_trailing_pause


class PunctuationTests(unittest.TestCase):
    def test_chinese_and_ascii_marks_have_same_pauses(self):
        self.assertEqual(clauses("a，b；c。", 1), clauses("a,b;c.", 1))
        self.assertEqual(clauses("a，b；c。", 1),
                         [("a,", .18), ("b;", .30), ("c.", .40)])

    def test_closing_quotes_do_not_lose_pause_or_create_extra_clause(self):
        self.assertEqual(clauses('a；” b。”', 1), [('a;”', .30), ('b.”', .40)])

    def test_repeated_marks_get_one_longest_pause(self):
        self.assertEqual(clauses("a，；b！？", 1), [("a,;", .30), ("b!?", .40)])

    def test_pause_tracks_playback_speed(self):
        parts = clauses("a，b；", 1.5)
        self.assertEqual([text for text, _ in parts], ["a,", "b;"])
        self.assertAlmostEqual(parts[0][1], .12)
        self.assertAlmostEqual(parts[1][1], .20)

    def test_existing_silence_is_topped_up_without_cutting_speech(self):
        audio = np.concatenate((np.ones(100, dtype=np.float32), np.zeros(50, dtype=np.float32)))
        result = with_trailing_pause(audio, 1000, .18)
        np.testing.assert_array_equal(result[:100], audio[:100])
        self.assertEqual(len(result), 280)
        self.assertTrue(np.all(result[100:] == 0))
        self.assertIs(with_trailing_pause(audio, 1000, .04), audio)

    def test_clauses_become_one_waveform_with_pauses_in_correct_places(self):
        calls = []

        def create(fragment):
            calls.append(fragment)
            return np.full(100, len(calls), dtype=np.float32), 1000

        audio, rate = synthesize("a，b；c。", 1, create)
        self.assertEqual(calls, ["a,", "b;", "c."])
        self.assertEqual(rate, 1000)
        self.assertEqual(len(audio), 1180)
        np.testing.assert_array_equal(audio[100:280], 0)
        np.testing.assert_array_equal(audio[280:380], 2)
        np.testing.assert_array_equal(audio[380:680], 0)
        np.testing.assert_array_equal(audio[680:780], 3)
        np.testing.assert_array_equal(audio[780:], 0)

    def test_text_without_marks_remains_one_call_without_extra_pause(self):
        original = np.ones(100, dtype=np.float32)
        audio, _ = synthesize("abc", 1, lambda text: (original, 1000))
        np.testing.assert_array_equal(audio, original)
        self.assertEqual(clauses("，；。”", 1), [])


if __name__ == "__main__":
    unittest.main()
