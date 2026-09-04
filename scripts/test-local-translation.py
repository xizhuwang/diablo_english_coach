"""Offline-only pilot. Does not change the coach, listen, or upload text.

Use Python 3.12 and project-isolated packages under .local-translation/packages.
Model: Argos English-Chinese 1.9, derived from OPUS-MT (CC-BY 4.0).
Authors: Jorg Tiedemann and Santhosh Thottingal, EAMT 2020.
"""
import argparse
import ctypes
import gc
import json
from pathlib import Path
import sys
import time

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / ".local-translation" / "packages"))
import ctranslate2
import sentencepiece
from opencc import OpenCC


class MemoryCounters(ctypes.Structure):
    _fields_ = [("cb", ctypes.c_ulong), ("PageFaultCount", ctypes.c_ulong)] + [
        (name, ctypes.c_size_t) for name in (
            "PeakWorkingSetSize", "WorkingSetSize", "QuotaPeakPagedPoolUsage",
            "QuotaPagedPoolUsage", "QuotaPeakNonPagedPoolUsage",
            "QuotaNonPagedPoolUsage", "PagefileUsage", "PeakPagefileUsage")]


def peak_memory_mib():
    counters = MemoryCounters()
    counters.cb = ctypes.sizeof(counters)
    kernel = ctypes.windll.kernel32
    kernel.GetCurrentProcess.restype = ctypes.c_void_p
    query = ctypes.windll.psapi.GetProcessMemoryInfo
    query.argtypes = [ctypes.c_void_p, ctypes.POINTER(MemoryCounters), ctypes.c_ulong]
    if not query(kernel.GetCurrentProcess(), ctypes.byref(counters), counters.cb):
        return None
    return round(counters.PeakWorkingSetSize / 1024 / 1024, 1)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="local-translation-test.json")
    args = parser.parse_args()
    model = ROOT / ".local-translation" / "model" / "translate-en_zh-1_9"
    tokenizer = sentencepiece.SentencePieceProcessor(model_file=str(model / "sentencepiece.model"))
    converter = OpenCC("s2twp")
    examples = [
        "Head Forward and Search for Leoric.",
        "Leave Mad King's Breach.",
        "I need your help. Follow me and stay close.",
        "This effect increases the damage dealt by your summons.",
        "Defeat the undead and find the shard.",
        "This shield absorbs damage, but it does not restore your health.",
        "Compare the two items before you replace your equipment.",
        "The skill is not ready yet. Wait for the cooldown to end.",
        "The door will remain closed until you find the key.",
        "Butthere'ssomethingevil inthatforestnow.",
    ]
    report = {"engine": ctranslate2.__version__, "model": "Argos en_zh 1.9",
              "device": "cpu", "compute_type": "int8", "beam_size": 2, "runs": []}
    for threads in (1, 4):
        started = time.perf_counter()
        translator = ctranslate2.Translator(str(model / "model"), device="cpu", compute_type="int8",
                                           inter_threads=1, intra_threads=threads)
        run = {"threads": threads, "load_ms": round((time.perf_counter() - started) * 1000), "sentences": []}
        for text in examples:
            started = time.perf_counter()
            cpu_started = time.process_time()
            result = translator.translate_batch([tokenizer.encode(text, out_type=str)],
                beam_size=2, max_decoding_length=256, max_input_length=256, replace_unknowns=True)[0]
            translated = converter.convert(tokenizer.decode(result.hypotheses[0]))
            item = {"english": text, "chinese": translated,
                    "ms": round((time.perf_counter() - started) * 1000),
                    "cpu_ms": round((time.process_time() - cpu_started) * 1000)}
            run["sentences"].append(item)
            print(json.dumps({"threads": threads, **item}, ensure_ascii=False), flush=True)
        report["runs"].append(run)
        translator.unload_model()
        del translator
        gc.collect()
    report["peak_process_memory_mib"] = peak_memory_mib()
    Path(args.output).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
