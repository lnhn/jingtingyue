"""Keep punctuation pauses inside the sentence WAV, before it is buffered."""
import re
from collections.abc import Callable

import numpy as np

_NORMALIZE = str.maketrans("，、；：。！？", ",,;:.!?")
_MARKS = re.compile(r"[,;:.!?…]+")
_CLOSERS = "\"'”’」』）)]】"
_NON_SPEECH = " \t\r\n,;:.!?…" + _CLOSERS + "“‘「『（([【"
_PAUSES = {",": 0.18, ";": 0.30, ":": 0.24, ".": 0.40,
           "!": 0.40, "?": 0.40, "…": 0.40}


def clauses(phonemes: str, speed: float) -> list[tuple[str, float]]:
    phonemes = phonemes.translate(_NORMALIZE)
    parts = []
    position = 0
    for match in _MARKS.finditer(phonemes):
        end = match.end()
        while end < len(phonemes) and phonemes[end] in _CLOSERS:
            end += 1
        fragment = phonemes[position:end].strip()
        pause = max(_PAUSES[mark] for mark in match.group()) / speed
        if fragment.strip(_NON_SPEECH):
            parts.append((fragment, pause))
        elif parts:
            previous, old_pause = parts[-1]
            parts[-1] = (previous + fragment, max(old_pause, pause))
        position = end
    tail = phonemes[position:].strip()
    if tail.strip(_NON_SPEECH):
        parts.append((tail, 0.0))
    elif tail and parts:
        previous, pause = parts[-1]
        parts[-1] = (previous + tail, pause)
    return parts


def with_trailing_pause(audio: np.ndarray, sample_rate: int, seconds: float) -> np.ndarray:
    if not seconds or not len(audio):
        return audio
    # Top up existing silence rather than stacking another full pause on it.
    active = np.flatnonzero(np.abs(audio) > float(np.max(np.abs(audio))) * 0.001)
    trailing = len(audio) - int(active[-1]) - 1 if len(active) else len(audio)
    missing = max(0, round(seconds * sample_rate) - trailing)
    if not missing:
        return audio
    return np.concatenate((audio, np.zeros(missing, dtype=audio.dtype)))


def synthesize(phonemes: str, speed: float,
               create: Callable[[str], tuple[np.ndarray, int]]) -> tuple[np.ndarray, int]:
    parts = clauses(phonemes, speed)
    if not parts:
        raise ValueError("No speech remains after punctuation normalization")
    audio_parts = []
    sample_rate = None
    for fragment, pause in parts:
        audio, rate = create(fragment)
        if sample_rate is not None and rate != sample_rate:
            raise ValueError("Clause sample rates do not match")
        sample_rate = rate
        audio_parts.append(with_trailing_pause(audio, rate, pause))
    return np.concatenate(audio_parts), sample_rate
