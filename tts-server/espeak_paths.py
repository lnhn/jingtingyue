"""Let the Windows eSpeak DLL find data when the app directory is Unicode."""
import os
from pathlib import Path

import espeakng_loader
from phonemizer.backend.espeak.wrapper import EspeakWrapper


def configure_espeak() -> None:
    data = Path(espeakng_loader.get_data_path())
    if os.name != "nt" or str(data).isascii():
        return
    # Phonemizer passes an absolute UTF-8 path, while this Windows DLL opens
    # files through narrow path APIs. Relative ASCII paths work from a Unicode
    # working directory without copying data or relying on 8.3 file names.
    relative = os.path.relpath(data, Path.cwd())
    if not relative.isascii():
        raise RuntimeError("eSpeak data must be under the bundled ASCII python directory")
    os.environ["ESPEAK_DATA_PATH"] = relative
    os.environ.pop("PHONEMIZER_ESPEAK_DATA_PATH", None)
    EspeakWrapper.set_data_path(None)
