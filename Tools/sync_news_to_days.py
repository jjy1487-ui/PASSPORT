# -*- coding: utf-8 -*-
"""엑셀 news 시트 → dayN.json 의 'news' 필드만 동기화.

build_days.py 의 뉴스 생성 로직(derive_news_claims + build_scan_trigger_news)을
그대로 재사용하되, 손님/규정(customers/rules)은 건드리지 않는다(전체 재빌드 회피).
파일 포맷은 build_days 와 동일(json.dump ensure_ascii=False, indent=2).
"""
import os, sys, json, glob, re
import openpyxl

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "Tools", "DataImport"))
import build_days as bd  # noqa: E402

SRC = os.path.join(ROOT, "data", "여권_정리_updated.xlsx")
GAMEDATA = os.path.join(ROOT, "Assets", "Resources", "GameData")


def read_news_rows():
    wb = openpyxl.load_workbook(SRC, data_only=True)
    ws = wb["news"]
    # 헤더 4행(PK/type/eng/kor) 이후 데이터. A=news_id B=day C=title D=content E=icon_ref
    out = []
    for r in ws.iter_rows(min_row=5, values_only=True):
        nid, day = r[0], r[1]
        if nid in (None, "") or day in (None, ""):
            continue
        out.append({
            "news_id": int(nid), "day": int(day),
            "news_title": r[2] or "", "news_content": r[3] or "",
            "icon_ref": (r[4] if len(r) > 4 else None),
        })
    return out


def main():
    rows = read_news_rows()
    by_day = {}
    for r in rows:
        by_day.setdefault(r["day"], []).append(r)

    changed = []
    for p in sorted(glob.glob(os.path.join(GAMEDATA, "day*.json")),
                    key=lambda p: int(re.search(r"day(\d+)", p).group(1))):
        if "_backup" in p:
            continue
        day = int(re.search(r"day(\d+)", p).group(1))
        with open(p, encoding="utf-8") as f:
            data = json.load(f)
        day_news = [
            {"newsId": r["news_id"], "title": r["news_title"],
             "content": r["news_content"], "iconRef": r["icon_ref"],
             "claims": bd.derive_news_claims(r["news_id"], r["news_title"], r["news_content"])}
            for r in by_day.get(day, [])
        ]
        day_news.extend(bd.build_scan_trigger_news(day))
        data["news"] = day_news
        with open(p, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        changed.append((day, [n["newsId"] for n in day_news]))

    for d, ids in changed:
        print(f"day{d}: news_ids={ids}")


if __name__ == "__main__":
    main()
