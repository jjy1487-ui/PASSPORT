# -*- coding: utf-8 -*-
"""뉴스(news) 내용+아이콘만 엑셀 기준으로 게임에 반영 (타겟 — 다른 테이블/에셋 무손상).

- 소스: data/여권_정리_updated.xlsx 'news' 시트 (news_id/sequence/day/news_title/news_content/icon_ref/value(캐릭터))
- 반영 대상:
   1) Assets/GameData/_source/GameData.source.json 의 news 행 (day/title/content/value/icon_ref 갱신, attrirube 보존)
   2) Assets/GameData/NewsTable.asset 의 rows 블록 재생성 (런타임 SO — 임포터 안 거침 → 엔딩 오버라이드 등 무손상)
- icon_ref = "News{id}" (이미지는 Assets/Resources/News/News{id}.png)
- attrirube(워치리스트 단서)는 엑셀에 없으므로 source.json 기존값을 news_id 기준 보존.

전체 임포터(Tools/Passport/Import Data)는 EndingTable 수동 오버라이드를 덮으므로 쓰지 않는다.
"""
import json
import os
import sys

import openpyxl

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
XLSX = os.path.join(ROOT, "data", "여권_정리_updated.xlsx")
SRC = os.path.join(ROOT, "Assets", "GameData", "_source", "GameData.source.json")
ASSET = os.path.join(ROOT, "Assets", "GameData", "NewsTable.asset")

COLS = ["news_id", "day", "news_title", "news_content", "icon_ref", "attrirube", "value"]


def read_excel_news():
    wb = openpyxl.load_workbook(XLSX, read_only=True, data_only=True)
    ws = wb["news"]
    out = {}
    order = []
    for r in ws.iter_rows(values_only=True):
        c = [(str(x).strip() if x is not None else "") for x in r]
        if not c or not c[0] or not c[0].isdigit():
            continue
        nid = c[0]
        # 컬럼: 0 id, 1 sequence, 2 day, 3 title, 4 content, 5 icon_ref(무시), 6 value(캐릭터)
        out[nid] = {
            "news_id": nid,
            "day": c[2],
            "news_title": c[3],
            "news_content": c[4],
            "value": c[6] if len(c) > 6 else "",
        }
        order.append(nid)
    wb.close()
    return out, order


def find_news_list(o):
    if isinstance(o, dict):
        for v in o.values():
            r = find_news_list(v)
            if r is not None:
                return r
    elif isinstance(o, list):
        if o and isinstance(o[0], dict) and "news_id" in o[0]:
            return o
        for it in o:
            r = find_news_list(it)
            if r is not None:
                return r
    return None


def patch_source(news):
    with open(SRC, encoding="utf-8") as f:
        d = json.load(f)
    nl = find_news_list(d)
    n = 0
    for row in nl:
        nid = str(row.get("news_id"))
        if nid in news:
            e = news[nid]
            row["day"] = e["day"]
            row["news_title"] = e["news_title"]
            row["news_content"] = e["news_content"]
            row["value"] = e["value"]
            row["icon_ref"] = "News" + nid
            # attrirube 보존
            n += 1
    with open(SRC, "w", encoding="utf-8") as f:
        json.dump(d, f, ensure_ascii=False, indent=1)
    return nl, n


def yq(s):
    """YAML 더블쿼트 스칼라(UTF-8 유지, " 와 \\ 만 이스케이프). Unity가 읽음."""
    if s is None:
        s = ""
    s = str(s).replace("\\", "\\\\").replace('"', '\\"')
    return '"' + s + '"'


def regen_asset(nl_by_id, order):
    with open(ASSET, encoding="utf-8") as f:
        text = f.read()
    marker = "\n  rows:\n"
    idx = text.find(marker)
    if idx < 0:
        print("ERROR: NewsTable.asset 의 rows: 블록을 못 찾음", file=sys.stderr)
        sys.exit(1)
    head = text[: idx + len(marker)]

    lines = []
    for nid in order:
        row = nl_by_id[nid]
        vals = {
            "news_id": row.get("news_id"),
            "day": row.get("day"),
            "news_title": row.get("news_title"),
            "news_content": row.get("news_content"),
            "icon_ref": row.get("icon_ref"),
            "attrirube": row.get("attrirube"),
            "value": row.get("value"),
        }
        lines.append("  - keys:")
        for k in COLS:
            lines.append("    - " + k)
        lines.append("    values:")
        for k in COLS:
            v = vals.get(k)
            if k in ("news_id", "day"):
                lines.append("    - " + (str(v) if v not in (None, "") else "0"))
            else:
                if v in (None, ""):
                    lines.append("    - ")
                else:
                    lines.append("    - " + yq(v))
    new_text = head + "\n".join(lines) + "\n"
    with open(ASSET, "w", encoding="utf-8") as f:
        f.write(new_text)


def main():
    news, order = read_excel_news()
    print("엑셀 뉴스 행:", len(news), "ids:", ",".join(order))
    nl, n = patch_source(news)
    print("source.json news 갱신:", n)
    nl_by_id = {str(r["news_id"]): r for r in nl}
    regen_asset(nl_by_id, order)
    print("NewsTable.asset rows 재생성 완료:", len(order), "행")
    # 검증 출력
    for nid in order:
        r = nl_by_id[nid]
        print(f"  ID{nid:>2} day={r.get('day'):>2} icon={r.get('icon_ref')} | {str(r.get('news_title'))[:34]}")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
