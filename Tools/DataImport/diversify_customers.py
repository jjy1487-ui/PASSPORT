# -*- coding: utf-8 -*-
"""
diversify_customers.py — dayN.json 손님 전역 중복 제거(고유화)

문제: day_schedule 가 38명을 14일×7=98슬롯에 재사용 → 같은 인물이 최대 6번 등장.
해결: 전역으로 customerId 가 두 번째 이상 나오는 슬롯은 "복제 인물"로 교체한다.
      - 새 customerId(1001~) + 국적/스타일에 맞는 새 이름(nameKr/nameEn)
      - 이름은 손님 객체 전체에서 '정확히 일치'하는 문자열만 치환(top 이름·서류 name 칸·
        대사 speaker·지문 dbName 등). 위조 결함(이름이 다른 값)은 일치하지 않아 그대로 보존.
      - spriteRef(=얼굴 이미지 키)는 '유지' → 기존 이미지 재활용 + 여권사진 대조 관계 보존.
        (이미지가 다른 캐릭터와 겹치는 건 의도된 임시 상태 — 추후 고유 이미지로 교체 예정.)
결과: 모든 dayN.json 의 customerId 가 전역 고유(총 98명) → CustomerRoster 전역 중복방지와 함께
      한 playthrough 에서 같은 인물이 두 번 등장하지 않는다.

파이프라인: build_days.py 가 dayN.json 을 (재)생성한 '뒤'에 실행한다(_run_full_rebuild.py 에 단계 추가).
재실행 안전(idempotent): 이미 고유한(=중복 없는) 데이터에 돌리면 아무 것도 바꾸지 않는다.
"""
import json, os, sys, io

# cp949 콘솔에서도 한글/em-dash 가 깨지거나 죽지 않게 stdout 을 UTF-8 로 재설정(직접 실행 안전).
try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
except Exception:
    pass

GDIR = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Resources", "GameData")
GDIR = os.path.normpath(GDIR)
DAYS = range(1, 15)
CLONE_ID_START = 1001

# 국적별 새 이름 풀 (nameKr, nameEn). 충분한 여유분. 이미지는 기존 것을 재활용하므로 이름만 새로 부여.
POOLS = {
    "korean": [
        ("김도윤","KIM DOYUN"),("이서준","LEE SEOJUN"),("박하준","PARK HAJUN"),("최예준","CHOI YEJUN"),
        ("정시우","JEONG SIWOO"),("강주원","KANG JUWON"),("조지호","JO JIHO"),("윤건우","YOON GEONWOO"),
        ("장우진","JANG WOOJIN"),("임선우","IM SEONWOO"),("한현우","HAN HYUNWOO"),("오은우","OH EUNWOO"),
        ("서지안","SEO JIAN"),("신유준","SHIN YUJUN"),("권민재","KWON MINJAE"),("황지환","HWANG JIHWAN"),
        ("안준영","AN JUNYEONG"),("송재윤","SONG JAEYUN"),("전도현","JEON DOHYUN"),("홍성민","HONG SEONGMIN"),
        ("김서아","KIM SEOAH"),("이지우","LEE JIWOO"),("박하윤","PARK HAYUN"),("최수아","CHOI SUAH"),
        ("정다은","JEONG DAEUN"),("강예린","KANG YERIN"),("조유진","JO YUJIN"),("윤채원","YOON CHAEWON"),
        ("장소율","JANG SOYUL"),("임가은","IM GAEUN"),("한지아","HAN JIA"),("오나은","OH NAEUN"),
        ("서하린","SEO HARIN"),("신예은","SHIN YEEUN"),("권서윤","KWON SEOYUN"),("황민서","HWANG MINSEO"),
        ("배은서","BAE EUNSEO"),("문지유","MOON JIYU"),("양수빈","YANG SUBIN"),("백지원","BAEK JIWON"),
        ("노태경","NOH TAEKYUNG"),("유준호","YOO JUNHO"),("심재훈","SIM JAEHOON"),("구본혁","KOO BONHYUK"),
        ("남승현","NAM SEUNGHYUN"),
    ],
    "western": [
        ("데이비드 스미스","DAVID SMITH"),("토머스 무어","THOMAS MOORE"),("다니엘 테일러","DANIEL TAYLOR"),
        ("매튜 앤더슨","MATTHEW ANDERSON"),("크리스 토머스","CHRIS THOMAS"),("앤드류 화이트","ANDREW WHITE"),
        ("에밀리 클락","EMILY CLARK"),("올리비아 루이스","OLIVIA LEWIS"),("소피아 워커","SOPHIA WALKER"),
        ("에마 홀","EMMA HALL"),("그레이스 영","GRACE YOUNG"),("한나 킹","HANNAH KING"),
        ("라이언 그린","RYAN GREEN"),("케빈 베이커","KEVIN BAKER"),
    ],
    "chinese": [
        ("리 강","LI GANG"),("왕 팡","WANG FANG"),("장 민","ZHANG MIN"),("류 옌","LIU YAN"),
        ("첸 하오","CHEN HAO"),("자오 친","ZHAO QIN"),("황 레이","HUANG LEI"),("우 팅","WU TING"),
        ("쉬 펑","XU FENG"),("선 메이","SUN MEI"),("주 빈","ZHU BIN"),("후 쥔","HU JUN"),
        ("궈 신","GUO XIN"),("린 타오","LIN TAO"),("허 룽","HE LONG"),("가오 윈","GAO YUN"),
    ],
    "japanese": [
        ("사토 유토","SATO YUTO"),("스즈키 소라","SUZUKI SORA"),("다카하시 리쿠","TAKAHASHI RIKU"),
        ("와타나베 하나","WATANABE HANA"),("이토 메이","ITO MEI"),("야마다 카이","YAMADA KAI"),
        ("나카무라 츠바사","NAKAMURA TSUBASA"),("고바야시 사쿠라","KOBAYASHI SAKURA"),
        ("가토 다이키","KATO DAIKI"),("요시다 미오","YOSHIDA MIO"),("야마구치 하루","YAMAGUCHI HARU"),
        ("마츠모토 리오","MATSUMOTO RIO"),
    ],
}


def culture_of(nationality):
    n = nationality or ""
    if "KOR" in n: return "korean"
    if "USA" in n or "USA" in n.upper(): return "western"
    if "CHN" in n: return "chinese"
    if "JPN" in n: return "japanese"
    return "korean"  # 알 수 없으면 한국식 폴백


def _rev(name):
    """2토큰 이름의 성-이름 순서를 뒤집는다('WILLIAM BROWN' <-> 'BROWN WILLIAM'). 아니면 None."""
    if not name:
        return None
    parts = name.split()
    return (parts[1] + " " + parts[0]) if len(parts) == 2 else None


def build_repl(old_kr, old_en, new_kr, new_en):
    """이름의 모든 표기형(원형 + 성-이름 역순)을 새 이름의 대응 표기형으로 매핑한다.
    여권 등은 영문 이름을 '성 이름' 역순으로 적기도 하므로 역순형도 치환해야 일관된다."""
    repl = {}
    if old_kr:
        repl[old_kr] = new_kr
    if old_en:
        repl[old_en] = new_en
    okr_r, nkr_r = _rev(old_kr), _rev(new_kr)
    if okr_r and nkr_r:
        repl.setdefault(okr_r, nkr_r)
    oen_r, nen_r = _rev(old_en), _rev(new_en)
    if oen_r and nen_r:
        repl.setdefault(oen_r, nen_r)
    return repl


def rename_in_place(obj, repl):
    """손님 객체 전체에서 repl 맵에 '정확히 일치'하는 이름 문자열만 치환. spriteRef 키는 건너뛴다(이미지 유지)."""
    if isinstance(obj, dict):
        for k, v in obj.items():
            if k == "spriteRef":
                continue  # 얼굴 이미지 키 — 기존 것 재활용(여권사진 대조 관계 보존)
            if isinstance(v, str):
                if v in repl:
                    obj[k] = repl[v]
            else:
                rename_in_place(v, repl)
    elif isinstance(obj, list):
        for it in obj:
            rename_in_place(it, repl)


def main():
    days = {}
    existing_kr, existing_en = set(), set()
    for d in DAYS:
        path = os.path.join(GDIR, f"day{d}.json")
        days[d] = json.load(open(path, encoding="utf-8"))
        for c in days[d].get("customers", []):
            existing_kr.add(c.get("nameKr"))
            existing_en.add(c.get("nameEn"))

    # 풀 커서(국적별) + 사용 기록(기존 이름과 충돌 회피)
    cursor = {k: 0 for k in POOLS}

    def next_name(cult):
        pool = POOLS.get(cult) or POOLS["korean"]
        while cursor[cult] < len(pool):
            kr, en = pool[cursor[cult]]
            cursor[cult] += 1
            if kr in existing_kr or en in existing_en:
                continue
            existing_kr.add(kr); existing_en.add(en)
            return kr, en
        raise RuntimeError(f"이름 풀 소진: '{cult}' — POOLS 에 이름을 더 추가하세요.")

    seen = set()
    next_id = CLONE_ID_START
    clones = 0
    for d in DAYS:
        for c in days[d].get("customers", []):
            cid = c.get("customerId")
            if cid not in seen:
                seen.add(cid)
                continue
            # 전역 중복 → 복제 인물로 교체
            cult = culture_of(c.get("nationality"))
            new_kr, new_en = next_name(cult)
            old_kr, old_en = c.get("nameKr"), c.get("nameEn")
            c["customerId"] = next_id
            rename_in_place(c, build_repl(old_kr, old_en, new_kr, new_en))
            seen.add(next_id)
            next_id += 1
            clones += 1

    for d in DAYS:
        path = os.path.join(GDIR, f"day{d}.json")
        json.dump(days[d], open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)

    total = len(seen)
    print(f"[diversify_customers] 복제 {clones}명 생성 → 전역 고유 customerId {total}명 / 98슬롯")
    if clones == 0:
        print("  (중복 없음 — 이미 고유한 데이터)")


if __name__ == "__main__":
    main()
