"""Convert mixed text to one phoneme sequence before a single synthesis call."""
import re
from collections.abc import Callable

_HAN = re.compile(r"[\u4e00-\u9fff]")
_LETTERS = r"A-Za-z\u00c0-\u00d6\u00d8-\u00f6\u00f8-\u024f"
_TOKEN = rf"[{_LETTERS}0-9]+(?:[.'’_-][{_LETTERS}0-9]+)*(?:\+\+|#)?"
_WORD = rf"[0-9]*[{_LETTERS}][{_LETTERS}0-9]*(?:[.'’_-][{_LETTERS}0-9]+)*(?:\+\+|#)?"
# Keep English phrases together so the converter can use their word context.
_ENGLISH = re.compile(rf"{_WORD}(?:[ \t]+{_TOKEN})*")


def has_chinese(text: str) -> bool:
    return bool(_HAN.search(text))


def to_phonemes(text: str, chinese: Callable[[str], str], english: Callable[[str], str]) -> str:
    if not has_chinese(text):
        return english(text)
    matches = list(_ENGLISH.finditer(text))
    if not matches:
        return chinese(text)

    parts = []
    position = 0
    for match in matches:
        fragment = text[position:match.start()]
        if fragment.strip():
            # ZHG2P determines its initial script before normalizing digits.
            # A space also keeps fragments beginning with a number correctly classified.
            parts.append(chinese(" " + fragment).lstrip())
        else:
            parts.append(fragment)
        parts.append(" " + english(match.group()) + " ")
        position = match.end()
    fragment = text[position:]
    if fragment.strip():
        parts.append(chinese(" " + fragment).lstrip())
    else:
        parts.append(fragment)
    return "".join(parts).strip()
