"""Local persistent JSON-lines worker. No network, audio, or screen access."""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "packages"))
import ctranslate2
import sentencepiece
from opencc import OpenCC

MODEL = ROOT / "model" / "translate-en_zh-1_9"
sp = sentencepiece.SentencePieceProcessor(model_file=str(MODEL / "sentencepiece.model"))
cc = OpenCC("s2twp")
# At most two resident engines; serialized requests never run them together.
engines = {}
NAMES = re.compile(r"\b(?:Deckard Cain|Ashwold Cemetery|Mad King's Breach|Leoric|Cain|Xul|Charsi|Akara|Kashya|Warriv|Lethes|Ashwold|Wortham)\b", re.I)
TERMS = [
    (r"\bsummons?\b", [("傳票", "召喚物"), ("召集", "召喚")]),
    (r"\bshards?\b", [("堅硬之人", "碎片"), ("碎屑", "碎片")]),
    (r"\bundead\b", [("不死之人", "不死族")]),
    (r"\bskills?\b", [("技術", "技能")]),
    (r"\bdamage\b", [("損害", "傷害")]),
    (r"\bhealth\b", [("健康", "生命值")]),
    (r"\bequipment\b", [("裝置", "裝備")]),
    (r"\bitems?\b", [("專案", "物品")]),
]

def translate(text, threads):
    threads = max(1, min(2, int(threads)))
    if threads not in engines:
        engines[threads] = ctranslate2.Translator(str(MODEL / "model"), device="cpu", compute_type="int8",
            inter_threads=1, intra_threads=threads)
    engine = engines[threads]
    names = []
    def protect(match):
        marker = str(900001 + len(names))
        while marker in text or any(marker == existing for existing, _ in names):
            marker = str(int(marker) + 1000)
        names.append((marker, match.group()))
        return marker
    source = NAMES.sub(protect, text)
    source = re.sub(r"\bsummons\b", "summoned creatures", source, flags=re.I)
    result = engine.translate_batch([sp.encode(source, out_type=str)], beam_size=2,
        max_input_length=512, max_decoding_length=384, replace_unknowns=True)[0]
    output = cc.convert(sp.decode(result.hypotheses[0])).replace("▁", "").strip()
    for marker, name in names:
        if output.count(marker) != 1:
            raise ValueError("name_not_preserved")
        output = output.replace(marker, name)
    for pattern, fixes in TERMS:
        if re.search(pattern, text, re.I):
            for wrong, right in fixes:
                output = output.replace(wrong, right)
    # Do not silently drop quantities or reverse a negation. This is a guard,
    # not a complete semantic proof; uncertain output is never cached.
    numbers = re.findall(r"\d+(?:\.\d+)?", text)
    if sorted(numbers) != sorted(re.findall(r"\d+(?:\.\d+)?", output)):
        raise ValueError("quantity_not_preserved")
    for quantity in re.findall(r"(\d+(?:\.\d+)?)\s*%", text):
        if not re.search(re.escape(quantity) + r"\s*%|百分之\s*" + re.escape(quantity), output):
            raise ValueError("percentage_not_preserved")
    if re.search(r"\b(?:not|never|cannot)\b|n't\b", text, re.I) and not re.search("不|沒|未|別|勿|無|禁止", output):
        raise ValueError("negation_not_preserved")
    for pattern, required in [(r"\bdamage\b", "傷害|損傷|受傷"),
                              (r"\bhealth\b", "生命|血量"),
                              (r"\buntil\b", "直到|之前|才|為止")]:
        if re.search(pattern, text, re.I) and not re.search(required, output):
            raise ValueError("key_meaning_missing")
    if not re.search(r"[\u3400-\u9fff]", output):
        raise ValueError("no_chinese")
    return output

if __name__ == "__main__":
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8")
    for line in sys.stdin:
        req = None
        try:
            # Windows PowerShell 5.1 hosts can prepend a UTF-8 BOM to a pipe.
            req = json.loads(line.lstrip("\ufeff"))
            text = req.get("text", "")
            if not isinstance(text, str) or not 1 <= len(text) <= 700:
                raise ValueError("invalid_input")
            reply = {"id": req["id"], "text": translate(text, req.get("threads", 1))}
        except Exception as exc:
            reply = {"id": req.get("id") if isinstance(locals().get("req"), dict) else None,
                     "error": str(exc)[:160]}
        # ASCII JSON escapes also survive legacy console code pages; the .NET
        # JSON reader restores the same Traditional Chinese Unicode string.
        print(json.dumps(reply, ensure_ascii=True), flush=True)
