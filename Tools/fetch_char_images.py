# -*- coding: utf-8 -*-
"""나영 브랜치 '캐릭터 이미지' 폴더의 PNG를 customer photo_ref 키로 받아 Resources/Characters 에 저장."""
import json, os, urllib.request, urllib.parse, ssl

REPO = "jjy1487-ui/PASSPORT"
BRANCH = "나영"
FOLDER = "캐릭터 이미지"
ROOT = r"C:\Users\chris\Documents\produc_build_reecture"
OUT = os.path.join(ROOT, "Assets", "Resources", "Characters")
ctx = ssl.create_default_context()

# nameKr -> (spriteRef, photo_ref) from _map.txt
m = {}
for line in open(os.path.join(ROOT, "_map.txt"), encoding="utf-8").read().splitlines()[1:]:
    cid, nameKr, spr, photo = line.split("|")
    m[nameKr] = (spr, photo)

# 표기 차이 보정: 데이터 nameKr -> 이미지 파일명(확장자 제외)
ALIAS = {"자오 레이": "지오 레이"}

api = f"https://api.github.com/repos/{REPO}/contents/{urllib.parse.quote(FOLDER)}?ref={urllib.parse.quote(BRANCH)}"
data = json.load(urllib.request.urlopen(urllib.request.Request(api, headers={"User-Agent": "fetch"}), context=ctx))
by_stem = {os.path.splitext(x["name"])[0]: x["download_url"] for x in data if x["type"] == "file"}

os.makedirs(OUT, exist_ok=True)
matched, unmatched = [], []
for nameKr, (spr, photo) in m.items():
    stem = ALIAS.get(nameKr, nameKr)
    url = by_stem.get(stem)
    if not url:
        unmatched.append(nameKr); continue
    dest = os.path.join(OUT, f"{photo}.png")
    url = urllib.parse.quote(url, safe="/:%?=&")
    with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "fetch"}), context=ctx) as r:
        open(dest, "wb").write(r.read())
    matched.append(f"{nameKr} -> {photo}.png")

used_stems = {ALIAS.get(n, n) for n in m}
extra = [s for s in by_stem if s not in used_stems]
print("MATCHED", len(matched))
for x in matched: print("  ", x)
print("UNMATCHED customers:", unmatched)
print("EXTRA images (no customer):", extra)
