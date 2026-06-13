# -*- coding: utf-8 -*-
"""
설계doc(data/대사_스크립트.xlsx)의 개선된 대사를 게임 런타임 day*.json에 이식.

원칙(작업 지시 기준):
 - dialogueCases의 case 구조(입장/정상승인/정상거절/잘못허가/잘못거절)·claim·fingerprint·xray 객체는 보존,
   발화 text만 교체/추가.
 - '서류/지문/X-ray/음성/경보 대조' 중간 단계 줄은 게임에 없음 → 이식 안 함(설계doc에만 남김).
 - 결함/판정/문서는 1단계서 끝났으므로 건드리지 않음.
 - 멱등(idempotent): 같은 입력 → 같은 출력. 재실행해도 변화 없음.

실행:
  PYTHONIOENCODING=utf-8 python Tools/DataImport/port_design_dialogue.py
"""
import io
import sys
import os
import json
import glob
import shutil

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
GAMEDATA = os.path.join(ROOT, "Assets", "Resources", "GameData")

# ── 1. 오판 거절 자연화 (전체 day1~14, main + altVariant) ──────────────
WRONG_REJECT_OLD = "확인되지 않은 사유로 입국이 어렵습니다. 죄송합니다."
WRONG_REJECT_NEW = "죄송합니다. 좀 걸리는 부분이 있어서… 이번엔 입국이 어렵겠습니다."

# ── 2. 비자종류 거짓 5명 입장 진술 이식 ──────────────────────────────
#   key=(파일, customerId) → 그 입장 케이스에서 claim(attr=visa_type)을 가진 라인의
#   text를 design 인삿말(거짓 목적 진술)로, claim.value를 그 진술 목적과 일치(=visa_type과 달라야 함).
#   리강(d4)은 유지하므로 포함하지 않음.
VISA_LIE = {
    ("day7.json", 58): {
        "text": "你好。我是来出差谈业务的。(안녕하세요. 출장 왔어요, 업무 때문에.)",
        "claim_value": "출장",   # 비자=관광 → 다름
    },
    ("day9.json", 67): {
        "text": "您好。我是来参加朋友婚礼的。(안녕하세요. 친구 결혼식 보러 왔어요.)",
        "claim_value": "결혼식",  # 비자=취업 → 다름
    },
    ("day10.json", 3): {
        "text": "你好！我是来留学的，第一次来韩国。(안녕하세요! 유학 왔어요, 한국은 처음이에요.)",
        "claim_value": "유학",   # 비자=관광 → 다름
    },
    ("day12.json", 85): {
        "text": "你好！第一次来韩国，就是来旅游的。(안녕하세요! 한국 처음이에요, 그냥 관광 왔어요.)",
        "claim_value": "관광",   # 비자=취업 → 다름
    },
    ("day13.json", 92): {
        "text": "你好～我是来短期旅游的。(안녕하세요~ 잠깐 여행 왔어요.)",
        "claim_value": "여행",   # 비자=장기 체류 → 다름
    },
}

# ── 3. 특수/개선 캐릭터 입장·승인 대사 이식 ───────────────────────────
#   각 항목: 입장 라인 교체/추가, 정상승인(=approve) 라인 교체.
#   speaker는 보존, 라인 수/순서/심사관 라인 존재는 보존하되 지시된 부분만 변경.

def get_customer(data, cid):
    for c in data["customers"]:
        if c["customerId"] == cid:
            return c
    return None


def get_case(cust, case_type, game_result=None):
    for case in cust.get("dialogueCases", []):
        if case.get("caseType") != case_type:
            continue
        if game_result is None or case.get("gameResult") == game_result:
            return case
    return None


def entry_case(cust):
    return get_case(cust, "입장")


def approve_case(cust):
    return get_case(cust, "일반 심사", "정상 승인")


def set_line_text(case, idx, text):
    """case.lines[idx].text 를 교체. idx 범위 밖이면 무시(보호)."""
    if case is None:
        return False
    lines = case.get("lines", [])
    if 0 <= idx < len(lines):
        lines[idx]["text"] = text
        return True
    return False


def apply_visa_lie(data, fname):
    changed = 0
    for (f, cid), spec in VISA_LIE.items():
        if f != fname:
            continue
        cust = get_customer(data, cid)
        if cust is None:
            print(f"  [WARN] {f}: customer {cid} 없음")
            continue
        ec = entry_case(cust)
        if ec is None:
            print(f"  [WARN] {f}: customer {cid} 입장 케이스 없음")
            continue
        # claim(attr=visa_type)을 가진 라인을 찾아 text·claim.value 갱신
        hit = False
        for ln in ec.get("lines", []):
            cl = ln.get("claim")
            if cl and cl.get("attr") == "visa_type":
                if ln.get("text") != spec["text"]:
                    ln["text"] = spec["text"]
                    changed += 1
                if cl.get("value") != spec["claim_value"]:
                    cl["value"] = spec["claim_value"]
                    changed += 1
                hit = True
        if not hit:
            print(f"  [WARN] {f}: customer {cid} visa_type claim 라인 없음")
    return changed


def apply_special(data, fname):
    """특수/개선 캐릭터 대사 이식. 반환=변경 카운트."""
    changed = 0

    def setif(case, idx, text):
        nonlocal changed
        if case is None:
            return
        lines = case.get("lines", [])
        if 0 <= idx < len(lines) and lines[idx].get("text") != text:
            lines[idx]["text"] = text
            changed += 1

    def insert_line(case, idx, speaker, text):
        """case.lines 의 idx 위치에 라인 삽입(이미 같은 라인 있으면 무시 → 멱등).
        order 는 1부터 재부여."""
        nonlocal changed
        if case is None:
            return
        lines = case.get("lines", [])
        # 멱등: 같은 (speaker,text) 라인이 이미 있으면 삽입하지 않음
        for ln in lines:
            if ln.get("speaker") == speaker and ln.get("text") == text:
                return
        lines.insert(idx, {"order": 0, "speaker": speaker, "text": text})
        for i, ln in enumerate(lines):
            ln["order"] = i + 1
        changed += 1

    # ── 오현석 (현자, d8, cid=16) ──
    if fname == "day8.json":
        c = get_customer(data, 16)
        if c:
            ec = entry_case(c)
            # 입장(현자 톤): design 인삿말 1줄. 2번째 줄은 현자 톤 여권 안내 유지.
            setif(ec, 0, "안녕하신가. 천천히 보게나, 급할 것 없네.")
            setif(ec, 1, "여기 여권일세. 인연이 닿았으니 보아주시게.")
            # 정상승인: 마법 아이템 건네는 대사(손님 반응 라인)
            ac = approve_case(c)
            setif(ac, 0, "즐거운 여행 되십시오.")
            setif(ac, 1, "고맙네. 수고하는 그대에게 작은 정표를 두고 가지… 부디 가는 길이 환하기를.")

    # ── 첸 리 (장기체류/취업, d8, cid=18) : 정상승인 취업 맥락 ──
    if fname == "day8.json":
        c = get_customer(data, 18)
        if c:
            ac = approve_case(c)
            setif(ac, 0, "네, 재직 서류까지 다 확인했어요. 한국에서 좋은 일자리 구하시길 바랄게요.")

    # ── 장 민 (장기체류, d8, cid=62) : 입장에 취업 인사 + 심사관 재직증명서 도입 안내 ──
    if fname == "day8.json":
        c = get_customer(data, 62)
        if c:
            ec = entry_case(c)
            # 입장 손님 발화(취업 인사) — design 인삿말
            setif(ec, 0, "你好，我是来这边工作的。材料都带来了。(안녕하세요, 여기로 일하러 왔어요. 서류 다 가져왔습니다.)")
            # 심사관 재직증명서 도입 안내 추가(멱등 삽입). 손님 응답까지 도입.
            insert_line(ec, 1, "심사관", "아, 일하러 오셨구나. 오늘부터 취업·체류로 오신 분들은 재직증명서도 같이 확인해요. 보여주시겠어요?")
            insert_line(ec, 2, "장 민", "好的，给您。(네, 여기 있습니다.)")

    # ── 서지안 (일반, d6, cid=53) : 입장에 심사관 방역 PCR 안내 + 본인 응답 ──
    if fname == "day6.json":
        c = get_customer(data, 53)
        if c:
            ec = entry_case(c)
            setif(ec, 0, "안녕하세요. 방역 서류 더 늘어났다는 얘기 들었는데, 저는 여권만 있으면 되는 거 맞죠? (서류 꺼내며)")
            # 심사관 방역 PCR 안내 + 본인 응답
            insert_line(ec, 1, "심사관", "방역 기간이라 PCR 검사서도 같이 봐야 해요. 갖고 계시죠?")
            insert_line(ec, 2, "서지안", "아, 그렇군요. 여기 있습니다. (PCR 검사서를 꺼낸다)")

    # ── 존 카터 (밀수범, d11, cid=8) : 입장 태연한 척 + 심사관 X-ray 도입 안내 1줄 ──
    if fname == "day11.json":
        c = get_customer(data, 8)
        if c:
            ec = entry_case(c)
            setif(ec, 0, "Morning. Just here on business — nothing to declare. (안녕하세요. 사업차 왔어요. 신고할 것도 없고요.)")
            # 심사관 X-ray 도입 안내 1줄 + 손님 응답(단계별 대조 줄은 제외)
            insert_line(ec, 1, "심사관", "요즘 보안이 강화돼서, 오늘부터 수하물도 X-ray로 함께 봅니다. 가방 좀 올려주시겠어요?")
            insert_line(ec, 2, "존 카터", "Oh. Sure, of course. (아… 네, 그럼요.)")

    # ── 임가은 (일반, d12, cid=81) : 입장 오타 수정본 ──
    if fname == "day12.json":
        c = get_customer(data, 81)
        if c:
            ec = entry_case(c)
            setif(ec, 0, "안녕하세요. 보안 강화됐다더니… 공항 분위기가 영 살벌하네요. (주위를 두리번거리며)")

    # ── 황민서 (진상, d13, cid=90) : 입장 2변형(main=진상 톤 / alt=가벼운 불안) ──
    if fname == "day13.json":
        c = get_customer(data, 90)
        if c:
            ec = entry_case(c)
            setif(ec, 0, "요즘 공항 오면 영 마음이 안 놓이네. 빨리 좀 처리해줘요.")
            av = c.get("altVariant")
            if av:
                aec = get_case(av, "입장")
                setif(aec, 0, "아 진짜, 빨리 좀요. 이런 때 공항에 오래 있고 싶지 않다고요.")

    # ── 양수빈 (성형, d14, cid=96) : altVariant 정상거절 = 지문 신원 불일치 확인 ──
    if fname == "day14.json":
        c = get_customer(data, 96)
        if c:
            av = c.get("altVariant")
            if av:
                rc = get_case(av, "일반 심사", "정상 거절")
                setif(rc, 0, "지문 신원이 여권 이름과 일치하지 않습니다. 입국하실 수 없습니다.")

    return changed


def naturalize_reject(node):
    """재귀적으로 모든 line.text 의 오판거절 문구를 자연화. 반환=치환 건수."""
    count = 0
    if isinstance(node, dict):
        for k, v in node.items():
            if k == "text" and isinstance(v, str) and v == WRONG_REJECT_OLD:
                node[k] = WRONG_REJECT_NEW
                count += 1
            else:
                count += naturalize_reject(v)
    elif isinstance(node, list):
        for item in node:
            count += naturalize_reject(item)
    return count


def main():
    files = sorted(
        glob.glob(os.path.join(GAMEDATA, "day*.json")),
        key=lambda p: int(os.path.basename(p)[3:-5]),
    )
    total_reject = 0
    total_visa = 0
    total_special = 0
    report = []

    for path in files:
        fname = os.path.basename(path)
        raw = open(path, encoding="utf-8").read()
        had_trailing_nl = raw.endswith("\n")
        data = json.loads(raw)

        # 백업(.bak_dialog) — 없을 때만(멱등: 원본 보존, 재실행해도 덮어쓰지 않음)
        bak = path + ".bak_dialog"
        if not os.path.exists(bak):
            shutil.copyfile(path, bak)

        n_reject = naturalize_reject(data)
        n_visa = apply_visa_lie(data, fname)
        n_special = apply_special(data, fname)

        total_reject += n_reject
        total_visa += n_visa
        total_special += n_special

        out = json.dumps(data, ensure_ascii=False, indent=2)
        if had_trailing_nl:
            out += "\n"
        # 멱등: 내용이 같으면 디스크 안 건드림
        if out != raw:
            with open(path, "w", encoding="utf-8", newline="") as fh:
                fh.write(out)
            report.append((fname, n_reject, n_visa, n_special, "written"))
        else:
            report.append((fname, n_reject, n_visa, n_special, "unchanged"))

    print("=== 이식 리포트 ===")
    print(f"{'파일':<12}{'오판거절':>8}{'비자거짓':>8}{'특수캐릭':>8}  상태")
    for fname, nr, nv, ns, st in report:
        print(f"{fname:<12}{nr:>8}{nv:>8}{ns:>8}  {st}")
    print("-" * 44)
    print(f"오판거절 치환 합계: {total_reject}")
    print(f"비자거짓 변경 합계: {total_visa}")
    print(f"특수캐릭 변경 합계: {total_special}")


if __name__ == "__main__":
    main()
