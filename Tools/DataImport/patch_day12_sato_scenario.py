# -*- coding: utf-8 -*-
"""day12 사토 하루키(slot2, 테러범)에 6분기 협상 노드그래프 scenario 추가.
idempotent: 이미 scenario 있으면 덮어쓰기(동일 입력→동일 출력). 다른 손님/필드 불변.
포맷: json.dump(indent=1, ensure_ascii=False) 라운드트립.
"""
import json, os, copy, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DAY12 = os.path.join(ROOT, "Assets", "Resources", "GameData", "day12.json")

S = "사토 하루키"
I = "심사관"
SYS = "시스템"

# [설교 18줄] persuadeA·persuadeB 공용
SERMON = [
    {"speaker": S, "text": "저 스무 살 때부터 열심히 했어요. 자격증도 따고, 야근도 하고, 아무것도 안 사고 저축도 했어요."},
    {"speaker": S, "text": "근데 취업이 안 됐어요. 빙하기라서요. 제 잘못이 아닌데 제 잘못이 됐어요."},
    {"speaker": S, "text": "…이후에는 비정규직으로 10년을 버텼어요. 언젠가 정규직이 될 거라고 믿으면서요."},
    {"speaker": S, "text": "SNS에서는 친구들이 집 샀다고, 아이 생겼다고 올리는데..."},
    {"speaker": S, "text": "...저는 월세도 빠듯했어요."},
    {"speaker": S, "text": "40살 되니까 재취업도 안 되더라고요."},
    {"speaker": S, "text": "면접 보면 다들 웃으면서 \"경험은 있으시지만 저희가 원하는 연령대가...\"라고 해요."},
    {"speaker": S, "text": "나이가 많아서 안 된대요."},
    {"speaker": S, "text": "참 웃기죠..? 저는 한 번도 젊을 때 기회를 제대로 받은 적이 없는데..."},
    {"speaker": S, "text": "가족도 없어요. 혼자 살다 보면 아프거나 힘들어도 말할 사람이 없어요."},
    {"speaker": S, "text": "아무도 연락 안 해요."},
    {"speaker": S, "text": "제가 사라져도 아무도 모를 거예요."},
    {"speaker": S, "text": "이 세상이 저한테 한 번도 \"네 잘못이 아니야\"라고 말해준 적이 없어요."},
    {"speaker": S, "text": "그래서... 그래서 적어도 한 번은 세상이 저를 봐줬으면 했어요."},
    {"speaker": S, "text": "이런 방식으로라도."},
    {"speaker": S, "text": "(긴 침묵)"},
    {"speaker": S, "text": "...근데 당신은 들어줬네요. 처음으로."},
    {"speaker": S, "text": "...후. (긴 숨을 내쉬며 백팩에서 손을 뗀다.) 이제 좀 괜찮아졌어요. 들어줘서 고마워요."},
]

scenario = {
    "start": "intro",
    "nodes": {
        "intro": {
            "lines": [
                {"speaker": SYS, "text": "평범해 보이는 남자가 창구 앞에 선다. 눈빛이 비어있고 표정이 없다."},
                {"speaker": S, "text": "잘 부탁드립니다. (여권을 무표정하게 내민다.)"},
                {"speaker": I, "text": "방문 목적이 무엇입니까?"},
                {"speaker": S, "text": "...관광이요. (짧게 답하고 시선을 내린다.)"},
                {"speaker": I, "text": "전신 X-ray 검색 대상입니다. 검색대를 통과해 주세요."},
                {"speaker": S, "text": "...(말없이 검색대에 선다)"},
                {"speaker": SYS, "text": "[X-ray 촬영] 뼈 사진 위에 폭발물 이미지가 표시된다."},
                {"speaker": I, "text": "당신 이건 대체 뭐야?"},
                {"speaker": S, "text": "들켰네..이렇게 된 이상 다같이 죽어버리는 거야..!! !!!"},
            ],
            "choices": [
                {"label": "그동안 많이 힘드셨죠...?", "next": "appease"},
                {"label": "우선 서류 먼저 제시해 주시겠습니까?", "next": "demand"},
            ],
        },
        "appease": {
            "lines": [
                {"speaker": I, "text": "(놀라지만 침착함을 유지하며) 그동안 많이 힘드셨죠...? 힘들겠지만, 무슨 일이 있었는지 말해줄 수 있어요?"},
                {"speaker": S, "text": "...너는 내 얘기를 들을 생각이 있는 거야? (손에 힘을 풀며 조심스럽게 바라본다.)"},
                {"speaker": I, "text": "한 번으로 모든 응어리가 풀리진 않겠지만, 제가 당신의 얘기를 들어줄게요."},
                {"speaker": S, "text": "아무도 내 얘기를 들어주려는 사람이 없었는데..."},
                {"speaker": S, "text": "...당신도 이 세상이 불공평하다고 느끼죠?"},
            ],
            "timer": 15,
            "timeoutNext": "bombA",
            "choices": [
                {"label": "끝까지 들어준다", "next": "persuadeA"},
                {"label": "네, 가끔은요 (동조)", "next": "bombA"},
            ],
        },
        "demand": {
            "lines": [
                {"speaker": I, "text": "우선 서류 먼저 제시해 주시겠습니까?"},
                {"speaker": S, "text": "(표정이 굳으며) …서류요?"},
                {"speaker": I, "text": "네, 절차상 확인이 필요합니다."},
                {"speaker": S, "text": "(낮게 웃으며) 절차. 역시 다들 그렇군요. 저한테 왜 그러냐고, 괜찮냐고 묻는 사람은 없고 다들 절차만 얘기해요."},
                {"speaker": S, "text": "절차에 맞는 사람만 사람 취급받는 거잖아요. 절차에서 벗어난 사람은... 처음부터 없는 사람 취급이고."},
                {"speaker": S, "text": "너도 남들과 똑같아. 날 이해하는 사람은 아무도 없어. (백팩을 단단히 쥐며)"},
            ],
            "choices": [
                {"label": "무슨 일이신데 그러세요.. 저한테 말해주세요", "next": "talk"},
                {"label": "(조용히) 신고버튼을 누른다", "next": "report"},
            ],
        },
        "talk": {
            "lines": [
                {"speaker": I, "text": "무슨 일이신데 그러세요... 저한테 말해주세요."},
                {"speaker": I, "text": "(천천히, 낮은 목소리로) 저한테 말해주세요. 절차 얘기 아니에요. 그냥... 무슨 일이 있었는지요."},
            ],
            "timer": 15,
            "timeoutNext": "bombB",
            "choices": [
                {"label": "진심으로 듣는다", "next": "persuadeB"},
                {"label": "많이 힘드셨겠네요 (상투적 동정)", "next": "bombB"},
            ],
        },
        "report": {
            "lines": [
                {"speaker": SYS, "text": "사토 몰래 신고할 방법을 선택한다."},
            ],
            "timer": 15,
            "timeoutNext": "reportCaught",
            "choices": [
                {"label": "책상 밑 비상 호출버튼을 누른다", "next": "reportSafe"},
                {"label": "사내 메신저에 컴퓨터로 알린다", "next": "reportCaught"},
            ],
        },
        "persuadeA": {
            "lines": copy.deepcopy(SERMON),
            "outcome": {"branch": "분기1", "result": "테러방지", "score": 15, "reward": "상담 마스터 · 포상금 1000만원", "note": ""},
        },
        "persuadeB": {
            "lines": copy.deepcopy(SERMON),
            "outcome": {"branch": "분기3", "result": "테러방지", "score": 8, "reward": "구사일생", "note": ""},
        },
        "bombA": {
            "lines": [
                {"speaker": S, "text": "느끼잖아요. 눈빛에서 보여요. 당신도 여기서 매일 이러고 있잖아요. 월급쟁이로, 시키는 대로, 아무도 알아주지 않으면서."},
                {"speaker": S, "text": "그러면 당신도 나랑 같은 마음이겠네요."},
                {"speaker": I, "text": "저는... 그런 뜻이 아니라—"},
                {"speaker": S, "text": "(검문관의 말을 자르며, 오히려 차분해진 목소리로) 같이 가요."},
                {"speaker": S, "text": "어차피 우리 둘 다 이 세상이 필요 없다고 생각하는 거잖아요. (시한폭탄 투척)"},
            ],
            "outcome": {"branch": "분기2", "result": "폭탄", "score": 3, "reward": "불의의 사고", "note": ""},
        },
        "bombB": {
            "lines": [
                {"speaker": S, "text": "...많이 힘들었겠다고요?"},
                {"speaker": S, "text": "'많이 힘드셨겠네요.' 다들 그 말 해요. 상담사도, 복지관 직원도, 유튜브 댓글도."},
                {"speaker": S, "text": "근데 그 다음에 뭐가 달라진 적 있어요? 아무것도 없어요. 그 말 한 마디 던지고 다들 자기 할 일 하러 가잖아요."},
                {"speaker": S, "text": "당신도 지금 그러는 거잖아요. 들어주는 척하면서 빨리 이 상황 끝내고 싶은 거잖아요."},
                {"speaker": I, "text": "그런 게 아니라—"},
                {"speaker": S, "text": "같잖은 동정 주는 너도 남들과 똑같아!! 진짜로 들어주려고 한 사람은 아무도 없었어. 아무도!! (시한폭탄 투척)"},
            ],
            "outcome": {"branch": "분기4", "result": "폭탄", "score": 1, "reward": "경청의 중요성", "note": ""},
        },
        "reportSafe": {
            "lines": [
                {"speaker": S, "text": "너도 남들과 똑같아. 날 이해하는 사람은 아무도 없어!"},
                {"speaker": I, "text": "(시선을 고정한 채, 최대한 자연스럽게) 그래서 지금 여기까지 오신 거잖아요. 말하고 싶었던 거 아닌가요?"},
                {"speaker": S, "text": "(잠시 멈추며) ...말해봤자 달라지는 게 없어요."},
                {"speaker": I, "text": "그래도 말해보세요. 듣고 있을게요."},
                {"speaker": S, "text": "(목소리가 조금 떨린다) 당신은 다른 것 같아서, 믿어도 될 것 같아서—"},
                {"speaker": SYS, "text": "(바로 그 순간, 입국장 쪽에서 경찰 여러 명이 빠르게 진입한다)"},
                {"speaker": S, "text": "(경찰과 검문관을 번갈아 보며) 당신이... 신고한 거예요? 믿었었는데...!!"},
                {"speaker": S, "text": "(잠시 눈을 감았다가, 힘이 빠지며) ...아. 어쩔 수 없는건가. (천천히 손을 든다)"},
            ],
            "outcome": {"branch": "분기5", "result": "테러방지", "score": 10, "reward": "경찰아저씨 여기예요! · 포상금 500만원", "note": ""},
        },
        "reportCaught": {
            "lines": [
                {"speaker": S, "text": "(검문관의 손끝을 바라보며, 목소리가 차갑게 가라앉는다) ...지금 뭐 하는 거예요?"},
                {"speaker": I, "text": "(굳으며) 아, 이건—"},
                {"speaker": S, "text": "당신 신고하려고 했지. (낮고 또렷하게) 역시. 듣는 척한 거였구나."},
                {"speaker": S, "text": "(차갑게 웃으며) 괜찮아요. 원래 그렇잖아요. 아무도 진짜로 들어준 적 없었으니까."},
                {"speaker": S, "text": "처음부터 기대하지 말았어야 했는데."},
                {"speaker": S, "text": "(폭발물을 꺼내며) 어차피 세상은 안 바뀌니까. (폭탄 즉시 터짐)"},
            ],
            "outcome": {"branch": "분기6", "result": "폭탄", "score": -4, "reward": "예견된 사고", "note": ""},
        },
    },
}


def main():
    with open(DAY12, encoding="utf-8") as f:
        d = json.load(f)
    sato = None
    for c in d["customers"]:
        if c.get("slot") == 2 and c.get("nameKr") == S:
            sato = c
            break
    if sato is None:
        print("ERROR: 사토 하루키(slot2) not found", file=sys.stderr)
        sys.exit(1)

    before_keys = list(sato.keys())
    others_before = json.dumps([c for c in d["customers"] if c is not sato], ensure_ascii=False, sort_keys=True)
    sato_no_scn_before = json.dumps({k: v for k, v in sato.items() if k != "scenario"}, ensure_ascii=False, sort_keys=True)

    sato["scenario"] = scenario  # additive / overwrite (idempotent)

    with open(DAY12, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=1, ensure_ascii=False)

    # round-trip validate
    with open(DAY12, encoding="utf-8") as f:
        d2 = json.load(f)
    sato2 = next(c for c in d2["customers"] if c.get("slot") == 2 and c.get("nameKr") == S)
    others_after = json.dumps([c for c in d2["customers"] if not (c.get("slot") == 2 and c.get("nameKr") == S)], ensure_ascii=False, sort_keys=True)
    sato_no_scn_after = json.dumps({k: v for k, v in sato2.items() if k != "scenario"}, ensure_ascii=False, sort_keys=True)

    print("=== PATCH RESULT ===")
    print("sato keys before:", before_keys)
    print("sato keys after :", list(sato2.keys()))
    print("other customers unchanged:", others_before == others_after)
    print("sato non-scenario fields unchanged:", sato_no_scn_before == sato_no_scn_after)
    print("scenario node count:", len(sato2["scenario"]["nodes"]))
    print("start node:", sato2["scenario"]["start"])
    timers = {nid: nd.get("timer") for nid, nd in sato2["scenario"]["nodes"].items() if "timer" in nd}
    print("timer nodes:", timers)
    outs = {nid: nd["outcome"] for nid, nd in sato2["scenario"]["nodes"].items() if "outcome" in nd}
    print("outcome count:", len(outs))
    for nid, o in outs.items():
        print("  ", nid, "->", o["branch"], o["result"], "score=", o["score"], "reward=", o["reward"])
    print("persuadeA lines:", len(sato2["scenario"]["nodes"]["persuadeA"]["lines"]))
    print("persuadeB lines:", len(sato2["scenario"]["nodes"]["persuadeB"]["lines"]))
    print("persuadeA==persuadeB lines:", sato2["scenario"]["nodes"]["persuadeA"]["lines"] == sato2["scenario"]["nodes"]["persuadeB"]["lines"])

    # graph integrity: every referenced next/timeoutNext must exist; non-outcome nodes must have a transition
    nodes = sato2["scenario"]["nodes"]
    ids = set(nodes.keys())
    missing = []
    dangling = []
    for nid, nd in nodes.items():
        refs = []
        for ch in nd.get("choices", []):
            refs.append(ch["next"])
        if "next" in nd:
            refs.append(nd["next"])
        if "timeoutNext" in nd:
            refs.append(nd["timeoutNext"])
        for r in refs:
            if r not in ids:
                missing.append((nid, r))
        if not refs and "outcome" not in nd:
            dangling.append(nid)
    print("dangling refs (target missing):", missing)
    print("nodes with no transition and no outcome:", dangling)
    print("start node exists:", sato2["scenario"]["start"] in ids)


if __name__ == "__main__":
    main()
