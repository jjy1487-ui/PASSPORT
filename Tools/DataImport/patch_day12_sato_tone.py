# -*- coding: utf-8 -*-
"""day12 사토 하루키 scenario 대사를 '확정 새 톤'으로 교체 (in-place).

새 톤 (이전 세션에서 사용자 확정):
 - 입장: 정중→외로움 복선→단계적 X-ray 발각→차가운 돌변 (나레이션 X, 시스템 등장줄 제거)
 - 분기점1: [잠깐, 진정하세요…](진정·경청) / [당장 멈추세요. 손 드세요.](강경 통제)
 - B(강경) 노드: "절차" → "명령·위험물 취급" 프레이밍
 - 말걸기: 강경 직후 사토 의심 반응 추가
 - 신고: 지시문만 있던 곳에 긴장 연출 추가
 - 백팩 → 기폭장치 통일, '시한폭탄 투척' → '기폭장치를 누른다'

노드 ID/타이머/timeoutNext/outcome/choice.next 는 불변. lines 와 일부 choice.label 만 교체.
idempotent. 다른 손님/필드 불변. json.dump(indent=1, ensure_ascii=False) 라운드트립.
"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DAY12 = os.path.join(ROOT, "Assets", "Resources", "GameData", "day12.json")

S = "사토 하루키"
I = "심사관"
SYS = "시스템"

# ---- 노드별 새 lines (speaker, text) ----
LINES = {
    "intro": [
        (S, "안녕하세요. (가볍게 목례하며 여권을 내민다.)"),
        (I, "안녕하세요. 방문 목적이 어떻게 되십니까?"),
        (S, "그냥… 관광이요. 오랜만에 좀 쉬려고요. (옅게 웃는다.)"),
        (I, "머무시는 기간은 어떻게 되십니까?"),
        (S, "글쎄요. 딱히 정한 건 없어요. 발길 닿는 대로요. (담담하게)"),
        (I, "동행은 없으십니까?"),
        (S, "…네, 혼자예요. 원래 늘 혼자였어요. (잠깐 시선을 내린다)"),
        (I, "오늘부터 보안 강화로 전신 X-ray 검색이 있습니다. 검색대를 통과해 주시겠어요?"),
        (S, "(살짝 멈칫) …X-ray요. (애써 웃으며) 절차라면 따라야죠. (천천히 검색대로 걸어간다)"),
        (I, "(화면을 확인하다 손이 멎는다) ……잠깐만요."),
        (I, "이거, 가방이 아니라… 몸에 둘러져 있는데요. 복부, 가슴… 이게 다 뭡니까?"),
        (S, "(검색대 위에서 미동도 없이) …생각보단 빨리 찾으시네요."),
        (I, "손 천천히 들고, 그대로 계세요. 지금 당장—"),
        (S, "(천천히 검문관을 본다. 얼굴에서 표정이 사라진다) ……아. 들켰네요."),
        (S, "근데 뭐, 상관없어요. 어차피 여기까지 온 거니까."),
        (S, "(목소리가 낮게 가라앉으며) 이렇게 된 이상… 다 같이 가는 거예요. 여기 있는 사람들, 전부 다."),
    ],
    "appease": [  # A. 진정·경청
        (I, "잠깐, 진정하세요. 아무도 안 다쳐요. …원하는 게 뭐예요? 저한테 얘기해 봐요."),
        (S, "…너는 내 얘기를 들을 생각이 있는 거야? (손에 힘을 풀며 조심스럽게 바라본다.)"),
        (I, "한 번에 다 풀리진 않겠지만, 제가 들을게요."),
        (S, "아무도 내 얘기를 들어주려는 사람이 없었는데…"),
        (S, "…당신도 이 세상이 불공평하다고 느끼죠?"),
    ],
    "demand": [  # B. 강경 통제
        (I, "당장 멈추세요. 기폭장치 내려놓고, 손 천천히 드십시오."),
        (S, "(낮게 웃으며) …역시. 다들 명령만 하지, 아무도 \"왜 그러냐\"곤 안 물어."),
        (S, "명령 듣는 사람만 사람 취급받는 거잖아요. 거기서 벗어난 사람은 그냥… 위험물 취급이고."),
        (S, "너도 남들과 똑같아. 날 이해하는 사람은 아무도 없어. (기폭장치를 쥐며)"),
    ],
    "talk": [  # B-1. 말걸기 (+ 사토 의심 반응)
        (I, "(한 발 물러서며, 목소리를 누그러뜨린다) …무슨 일이신데 그러세요. 저한테 말해주세요."),
        (I, "(천천히, 낮은 목소리로) 명령 아니에요. 그냥… 무슨 일이 있었는지 듣고 싶어요."),
        (S, "(기폭장치를 쥔 손이 멈칫한다) …방금까지 멈추라고 소리치던 사람이."),
        (S, "(의심스럽게) 그것도 다 매뉴얼이에요? 사람 진정시키는 법, 뭐 그런 거."),
        (I, "매뉴얼 아닙니다. …그냥 당신이 무슨 말을 하고 싶은 건지 듣고 싶어요."),
        (S, "(잠시 침묵) …왜 이러느냐고 진짜로 물어본 사람은, 당신이 처음이에요."),
    ],
    "report": [  # B-2. 신고 (+ 긴장 연출)
        (S, "(기폭장치에 손을 올린 채) 뭘 그렇게 봐요. 빨리 결정해요."),
        (I, "(시선을 피하며) …아, 네. 잠시만요."),
        (S, "(목소리가 날카로워지며) 설마 지금… 무슨 수작 부리는 거 아니죠?"),
        (SYS, "사토가 눈치채지 못하게 해야 한다. 어떻게 신고할지 선택하라."),
    ],
}

# ---- choice.label 교체 (next 는 불변) ----
CHOICE_LABELS = {
    "intro": ["잠깐, 진정하세요…", "당장 멈추세요. 손 드세요."],
}

# ---- 단일 라인 텍스트 치환 (백팩/폭탄 표현 통일) ----
SERMON_LAST = "...후. (긴 숨을 내쉬며 기폭장치에서 손을 뗀다.) 이제 좀 괜찮아졌어요. 들어줘서 고마워요."
SINGLE_TEXT = {
    # node_id -> {line_index: new_text}
    "persuadeA": {17: SERMON_LAST},
    "persuadeB": {17: SERMON_LAST},
    "bombA": {4: "어차피 우리 둘 다 이 세상이 필요 없다고 생각하는 거잖아요. (기폭장치를 누른다)"},
    "bombB": {5: "같잖은 동정 주는 너도 남들과 똑같아!! 진짜로 들어주려고 한 사람은 아무도 없었어. 아무도!! (기폭장치를 누른다)"},
    "reportCaught": {5: "(기폭장치를 들어올리며) 어차피 세상은 안 바뀌니까. (즉시 폭발)"},
}


def set_lines(node, pairs):
    node["lines"] = [{"order": i, "speaker": sp, "text": tx}
                     for i, (sp, tx) in enumerate(pairs)]


def main():
    with open(DAY12, encoding="utf-8") as f:
        d = json.load(f)

    sato = next((c for c in d["customers"] if c.get("scenario")), None)
    if sato is None or sato.get("nameKr") != S:
        print("ERROR: 사토 하루키 scenario customer not found", file=sys.stderr)
        sys.exit(1)

    others_before = json.dumps(
        [c for c in d["customers"] if c is not sato],
        ensure_ascii=False, sort_keys=True)

    nodes = {n["id"]: n for n in sato["scenario"]["nodes"]}

    # 1) lines 교체
    for nid, pairs in LINES.items():
        set_lines(nodes[nid], pairs)

    # 2) choice 라벨 교체 (next 유지)
    for nid, labels in CHOICE_LABELS.items():
        chs = nodes[nid]["choices"]
        for ci, lab in enumerate(labels):
            chs[ci]["label"] = lab

    # 3) 단일 라인 텍스트 치환
    for nid, idx_map in SINGLE_TEXT.items():
        for li, tx in idx_map.items():
            nodes[nid]["lines"][li]["text"] = tx

    with open(DAY12, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=1, ensure_ascii=False)

    # ---- 검증 라운드트립 ----
    with open(DAY12, encoding="utf-8") as f:
        d2 = json.load(f)
    sato2 = next(c for c in d2["customers"] if c.get("scenario"))
    others_after = json.dumps(
        [c for c in d2["customers"] if not (c.get("nameKr") == S and c.get("scenario"))],
        ensure_ascii=False, sort_keys=True)
    n2 = {n["id"]: n for n in sato2["scenario"]["nodes"]}

    print("=== PATCH day12 사토 톤 ===")
    print("other customers unchanged:", others_before == others_after)
    print("node count:", len(n2))
    print("intro lines:", len(n2["intro"]["lines"]), "(첫줄 화자:", n2["intro"]["lines"][0]["speaker"], ")")
    print("intro choices:", [c["label"] + "→" + c["next"] for c in n2["intro"]["choices"]])
    print("appease[0]:", n2["appease"]["lines"][0]["text"][:30])
    print("demand[0]:", n2["demand"]["lines"][0]["text"][:30])
    print("talk lines:", len(n2["talk"]["lines"]), "/ report lines:", len(n2["report"]["lines"]))
    print("persuadeA last:", n2["persuadeA"]["lines"][-1]["text"][-30:])
    print("bombA last:", n2["bombA"]["lines"][-1]["text"][-20:])
    print("bombB last:", n2["bombB"]["lines"][-1]["text"][-20:])
    print("reportCaught last:", n2["reportCaught"]["lines"][-1]["text"][-20:])

    # 그래프 무결성: 모든 next/timeoutNext 타깃 존재
    ids = set(n2.keys())
    bad = []
    for nid, nd in n2.items():
        refs = [c["next"] for c in nd.get("choices", [])]
        if nd.get("timeoutNext"):
            refs.append(nd["timeoutNext"])
        for r in refs:
            if r and r not in ids:
                bad.append((nid, r))
    print("dangling refs:", bad)
    # 백팩/시한폭탄 잔재 검사
    leftover = []
    for nid, nd in n2.items():
        for ln in nd["lines"]:
            if "백팩" in ln["text"] or "시한폭탄 투척" in ln["text"]:
                leftover.append((nid, ln["text"][:25]))
    print("백팩/시한폭탄 잔재:", leftover)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
