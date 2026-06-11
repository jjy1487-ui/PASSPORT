# -*- coding: utf-8 -*-
"""
authored_lines.py — branch_dialogue 에 없는 [TODO 대사] 슬롯을 day1 톤으로 채우는 폴백 테이블.

설계 원칙(작업 지시 A안 준수):
  - branch_dialogue_map.fill_customer_dialogue 가 채우지 못하고 [TODO] 로 남긴 라인만 채운다.
  - 이미 채워진 라인은 절대 덮어쓰지 않는다.
  - 케이스 구조(개수/order/speaker)는 불변. text 만 교체한다.
  - day1.json 의 케이스별 형식(심사관 통과/거절·번복, 캐릭터 유형별 반응)을 그대로 계승.

조회 키: (characterType, caseType, gameResult, rejectCount, order, speaker)
  - 심사관 라인(통과/거절/번복)은 대부분 유형 무관 → 공통 기본값(INSPECTOR_*) 사용.
  - 캐릭터 라인은 유형별 성격 반영 → CHAR_LINES 테이블.

idempotent: 같은 입력이면 같은 결과(난수·시각 의존 없음).
"""

TODO = "[TODO 대사]"

# ── 심사관 공통 대사 (day1 의 심사관 멘트를 그대로 계승) ──────────────────
INSPECTOR_APPROVE = "확인 완료되었습니다. 지나가셔도 됩니다."          # 정상 승인 / 잘못 허가 o1
INSPECTOR_REJECT_GENERIC = "죄송하지만 입국은 어렵습니다."             # 잘못 거절 o1

# 잘못 거절 rc 별 심사관 번복 라인(o3). day1 의 rc1/rc2/rc3 번복 패턴 계승.
# (rc3 는 day1 에서 8줄 구조이나, 본 파이프라인 케이스는 4줄(rc 공통)이므로
#  각 rc 의 '최종 번복' 멘트를 강도에 맞춰 배치.)
INSPECTOR_REVERSAL = {
    1: "다시 확인해보니 서류가 맞네요. 지나가시면 됩니다.",
    2: "다시 확인했는데 서류가 모두 맞네요. 죄송합니다. 지나가셔도 됩니다.",
    3: "고객님 서류 확인되셔서 바로 지나가시면 될 것 같습니다. 정말 죄송합니다.",
}

# ── 유형별 캐릭터 대사 테이블 ──────────────────────────────────────────────
# 각 유형마다:
#   entry2          : 입장 두 번째 줄(캐릭터)
#   approve         : 정상 승인 캐릭터 반응(o2)
#   reject          : 정상 거절 캐릭터 반응(o2) — 위반 인지/유형 성격
#   wrong_approve   : 잘못 허가 캐릭터 반응(o2) — 결함 인지 못함
#   approve_ins     : (특수) 정상 승인 심사관 o1 이 별도 필요할 때(기본=INSPECTOR_APPROVE)
#   protest         : 잘못 거절 캐릭터 항의(o2) {rc: text}
#   accept          : 잘못 거절 번복 후 캐릭터 수긍(o4) {rc: text}
CHAR_LINES = {
    "일반 고객": {
        "entry2": "여권 확인 부탁드립니다.",
        "approve": "감사합니다. 즐거운 하루 되세요!",
        "reject": "앗, 제가 확인을 못 했네요. 죄송합니다.",
        "wrong_approve": "감사합니다. 안녕히 계세요!",
        "protest": {
            1: "어..? 어디가 잘못된 부분이 있나요?",
            2: "분명 제대로 다 챙겼어요. 다시 한 번 확인해 주세요.",
            3: "아니, 뭐 때문에 입국이 안 된다는 거예요? 다시 제대로 확인하세요!",
        },
        "accept": {
            1: "앗 네! 감사합니다!",
            2: "네 알겠습니다. 앞으로는 주의해 주세요.",
            3: "하... 네 알겠습니다. 꼼꼼히 검토 좀 해 주세요.",
        },
    },
    "외국인 관광객": {
        "entry2": "Here is my passport.",
        "approve": "Thank you! Have a nice day!",
        "reject": "Oh... I am sorry. I think I made a mistake.",
        "wrong_approve": "Thank you! Have a nice day!",
        "protest": {
            1: "What? 분명 서류를 다 챙긴 것 같았는데요?",
            2: "Really? 꼼꼼하게 다 확인하고 온 건데 다시 한 번 확인해 줘요.",
            3: "No way... 이해할 수 없어요. 왜 들어갈 수 없는 거예요?",
        },
        "accept": {
            1: "Oh 정말 다행이에요! 고마워요!",
            2: "정말 다행이에요. 여행할 수 있어서 다행이에요.",
            3: "기분이 안 좋아요. You are so rude.",
        },
    },
    "진상 고객": {
        "entry2": "여권 냈으니까 빨리 처리해.",
        "approve": "일처리 하나는 빨라서 맘에 드네.",
        "reject": "뭐? 트집 잡지 말고 그냥 보내줘!",
        "wrong_approve": "진작 이럴 것이지. 시간 끌지 마.",
        "protest": {
            1: "무슨 소리야? 서류에 잘못된 부분이 없는데!",
            2: "눈을 어디에 둔 거야? 서류 다 맞게 챙겼잖아!",
            3: "진짜 미치겠네! 다시 제대로 확인해!",
        },
        "accept": {
            1: "이런... 눈 똑바로 뜨고 일해!",
            2: "시간 낭비하게 만들고 있어!",
            3: "진작 이럴 것이지. 내가 꼭 민원 넣을 거야!",
        },
    },
    "검역 대상자(PCR)": {
        "entry2": "여기 검사서랑 같이 드릴게요. (기침)",
        "approve": "고맙습니다. 빨리 들어가서 쉬고 싶네요.",
        "reject": "아, 제가 서류를 잘못 챙겼나봐요. 돌아갈게요.",
        "wrong_approve": "고맙습니다. 몸이 안 좋아서 빨리 가볼게요.",
        "protest": {
            1: "전 문제없는데요? 다시 확인해 주세요.",
            2: "검사 다 받고 왔는데요. 한 번만 더 봐 주세요.",
            3: "몸도 안 좋은데 자꾸 왜 이러세요. 제대로 확인하세요!",
        },
        "accept": {
            1: "아, 다행이네요. 감사합니다.",
            2: "휴... 들어갈 수 있어 다행이에요.",
            3: "이제야 되네요. 다음엔 빨리 좀 봐 주세요.",
        },
    },
    "취업체류자": {
        "entry2": "취업 서류도 함께 드릴게요.",
        "approve": "감사합니다. 성실히 일하겠습니다.",
        "reject": "아... 서류에 문제가 있었군요. 다시 준비하겠습니다.",
        "wrong_approve": "감사합니다. 잘 부탁드립니다.",
        "protest": {
            1: "어디가 문제일까요? 회사에서 다 받아온 서류인데요.",
            2: "분명 정식으로 발급받은 서류예요. 다시 봐 주세요.",
            3: "일하러 온 건데 이러시면 곤란해요. 제대로 확인해 주세요!",
        },
        "accept": {
            1: "다행입니다. 감사합니다.",
            2: "네, 확인해 주셔서 감사합니다.",
            3: "하... 겨우 들어가네요. 다음엔 잘 봐 주세요.",
        },
    },
    "장기체류자": {
        "entry2": "체류 관련 서류도 같이 드릴게요.",
        "approve": "감사합니다. 잘 지내다 가겠습니다.",
        "reject": "아, 서류가 미비했네요. 다시 챙겨오겠습니다.",
        "wrong_approve": "감사합니다. 그럼 들어가 볼게요.",
        "protest": {
            1: "어느 부분이 문제인가요? 다 맞게 준비했는데요.",
            2: "분명 빠짐없이 챙겼어요. 한 번 더 확인해 주세요.",
            3: "오래 머물 사람인데 이러시면 안 되죠. 제대로 봐 주세요!",
        },
        "accept": {
            1: "다행이네요. 감사합니다.",
            2: "네, 확인 감사합니다.",
            3: "하... 이제야 되네요. 꼼꼼히 봐 주세요.",
        },
    },
    "성형 의심 고객": {
        "entry2": "사진이랑 좀 달라 보여도 저 맞아요.",
        "approve": "감사합니다! 요즘 좀 달라 보이죠?",
        "reject": "사진이랑 다르다고요? 저 맞다니까요...",
        "wrong_approve": "감사합니다. 역시 알아봐 주시네요.",
        "protest": {
            1: "네? 분명히 저 본인인데요?",
            2: "성형 좀 했다고 거절하시는 거예요? 저 맞아요.",
            3: "어떻게 본인을 못 알아봐요? 다시 확인하세요!",
        },
        "accept": {
            1: "거 봐요, 저 맞잖아요. 감사합니다.",
            2: "그쵸? 본인 맞다니까요. 감사합니다.",
            3: "하... 사람 무안하게. 다음엔 잘 봐 주세요.",
        },
    },
    "범죄자(밀수품 범죄자)": {
        "entry2": "여권 여기 있어요. 빨리 좀 부탁드려요.",
        "approve": "후... 감사합니다. 그럼 들어갈게요.",
        "reject": "아니 뭐가 문제죠? 그냥 좀 보내주세요.",
        "wrong_approve": "감사합니다. (다행이군...) 그럼 가볼게요.",
        "protest": {
            1: "예? 제 서류는 아무 문제 없는데요.",
            2: "왜 자꾸 붙잡으세요. 다 정상이잖아요.",
            3: "이거 왜 이래요, 그냥 보내달라니까요!",
        },
        "accept": {
            1: "거 봐요. 그럼 들어갈게요.",
            2: "네, 그럼 이만 가보겠습니다.",
            3: "참... 진작 보내주지. 갑니다.",
        },
    },
    "범죄자(마약 범죄자)": {
        "entry2": "여권 여기요. 별거 없으니 빨리 봐 주세요.",
        "approve": "감사합니다. 그럼 들어가겠습니다.",
        "reject": "뭐가 문제라는 거예요? 다 정상인데.",
        "wrong_approve": "감사합니다. (통과군.) 그럼 가볼게요.",
        "protest": {
            1: "제 서류 멀쩡한데 왜 그러세요?",
            2: "이상한 거 하나도 없잖아요. 다시 보세요.",
            3: "괜한 사람 잡지 말고 그냥 보내줘요!",
        },
        "accept": {
            1: "그쵸? 그럼 들어갈게요.",
            2: "네, 이만 가보겠습니다.",
            3: "참 나... 갑니다.",
        },
    },
    "범죄자(성형수술)": {
        "entry2": "얼굴 좀 바꿨어도 본인 맞아요. 여권 드릴게요.",
        "approve": "감사합니다. 역시 알아봐 주시네요.",
        "reject": "사진이랑 다르다고요? 저 본인 맞는데요...",
        "wrong_approve": "감사합니다. (안 들켰군.) 그럼 갈게요.",
        "protest": {
            1: "왜요? 분명히 저 맞는데요.",
            2: "얼굴 좀 고쳤다고 이러시는 거예요? 저예요.",
            3: "본인을 못 알아보면 어떡해요? 다시 확인하세요!",
        },
        "accept": {
            1: "거 봐요, 저 맞잖아요.",
            2: "그쵸? 본인 맞다니까요.",
            3: "하... 사람 의심하지 좀 마세요. 갑니다.",
        },
    },
    "테러범": {
        "entry2": "여권 여기 있다. 빨리 처리해.",
        "approve": "...수고해.",
        "reject": "뭐? 무슨 근거로 막는 거지.",
        "wrong_approve": "현명한 선택이군. 그럼 들어가지.",
        "protest": {
            1: "막을 이유가 없을 텐데. 다시 봐.",
            2: "쓸데없이 시간 끌지 마. 통과시켜.",
            3: "지금 누구한테 이러는 거야. 당장 보내.",
        },
        "accept": {
            1: "...그래. 들어가지.",
            2: "진작 그럴 것이지.",
            3: "...기억해 두지. 비켜.",
        },
    },
    "특수(연예인)★": {
        "entry2": "사람들 알아보기 전에 빨리 좀 부탁해요.",
        "approve": "고마워요. 역시 일 잘하시네.",
        "reject": "네? 저를 막으신다고요? 제가 누군지 아세요?",
        "wrong_approve": "역시 알아보시는구나. 고마워요.",
        "approve_ins": INSPECTOR_APPROVE,
        "protest": {
            1: "이러시면 곤란한데요. 제가 좀 바빠서요.",
            2: "스케줄 있다니까요? 빨리 좀 봐 주세요.",
            3: "이거 기사라도 나면 어쩌시려고요. 당장 보내주세요!",
        },
        "accept": {
            1: "그쵸? 고마워요.",
            2: "진작 이럴 것이지. 수고하세요.",
            3: "하... 사람 곤란하게. 다음엔 빨리요.",
        },
    },
    "특수(정치인)★": {
        # 정치인 전용 톤: 격식·권위, 보좌관 대리, 표/공약/의정활동 어휘,
        # 스캔들·논란엔 정치인다운 회피·해명. 연예인의 팬·사인·스타성 톤과 구별.
        "entry2": "(수행원이 대신 서류를 내밀며) 의원님 일정이 빠듯하오. 신속히 처리해 주시오.",
        "approve": "수고하셨소. 나라를 위한 길에 늘 애써주시오.",
        "reject": "지금 공무로 입국하는 사람을 막겠다는 거요? 누구 재가를 받았소?",
        "wrong_approve": "그래, 일을 알아서 처리할 줄 아는군. 기억해 두겠소.",
        "approve_ins": INSPECTOR_APPROVE,
        "protest": {
            1: "이런 식으로 일하면 곤란하오. 서류는 보좌관이 다 검토한 것이니 다시 보시오.",
            2: "허, 내가 누군지 모르나? 의정활동에 차질이 생기면 자네가 책임질 텐가?",
            3: "이 일이 기사로 나가도 좋겠소? 나는 국민의 표로 선 사람이오. 당장 통과시키시오!",
        },
        "accept": {
            1: "그럼 그렇지. 수고하시오.",
            2: "진작 그럴 것이지. 공무 처리에 협조 좀 하시오.",
            3: "흠... 이번 일은 잊지 않겠소. 다음부턴 알아서 처신하시게.",
        },
    },
    "특수(현자)★": {
        "entry2": "여기 여권일세. 천천히 보게나.",
        "approve": "고맙네. 좋은 하루 보내시게.",
        "reject": "허허, 어딘가 잘못된 모양이군. 내 불찰일세.",
        "wrong_approve": "고맙네. 그럼 들어가 보겠네.",
        "approve_ins": INSPECTOR_APPROVE,
        "protest": {
            1: "이상하군. 분명 다 맞을 텐데. 한 번 더 보겠나?",
            2: "허허, 다시 한 번 살펴봐 주시게.",
            3: "사람이 실수는 할 수 있네만, 차분히 다시 확인해 주게.",
        },
        "accept": {
            1: "그래, 다행이군. 고맙네.",
            2: "허허, 수고가 많으셨네.",
            3: "괜찮네. 다음엔 천천히 보시게나.",
        },
    },
}


# ── characterType 별칭 (소스 갱신으로 day JSON 유형명이 바뀐 경우 흡수) ──────────
# day JSON 의 현행 characterType 이 CHAR_LINES 키와 다르면 같은 톤의 기존 항목으로 매핑한다.
# (캐릭터 손글 대사는 character_dialogue_map 가 이미 손님 라인을 덮으므로, 여기서는
#  심사관 라인·번복 후 수긍 라인 등 잔여 [TODO] 폴백만 채운다.)
CHAR_LINES["전염병 환자"] = CHAR_LINES["검역 대상자(PCR)"]
CHAR_LINES["성형 수술 고객"] = CHAR_LINES["성형 의심 고객"]


def _char(ctype):
    return CHAR_LINES.get(ctype)


def authored_text(ctype, case_type, game_result, reject_count, order, speaker):
    """[TODO] 라인 1개에 들어갈 작성 대사를 반환. 해당 슬롯 작성본이 없으면 None.

    구조 불변 원칙: 이 함수는 text 문자열만 돌려준다(speaker/order 무관 결정은 호출부).
    """
    c = _char(ctype)
    if c is None:
        return None

    # ── 입장: 두 번째 캐릭터 줄(order 2) ──────────────────────────────
    if case_type == "입장":
        if speaker == "캐릭터" and order == 2:
            return c.get("entry2")
        return None

    # ── 일반 심사 ────────────────────────────────────────────────────
    if case_type != "일반 심사":
        return None

    if game_result == "정상 승인":
        if speaker == "심사관":
            return c.get("approve_ins", INSPECTOR_APPROVE)
        return c.get("approve")

    if game_result == "잘못 허가":
        if speaker == "심사관":
            return INSPECTOR_APPROVE
        return c.get("wrong_approve")

    if game_result == "정상 거절":
        if speaker == "심사관":
            # 위반 항목 멘트는 build_days 가 violation_label 로 이미 생성(보통 채워짐).
            # 그래도 [TODO]만 남았다면 일반 거절 멘트로 폴백.
            return "서류 항목에 문제가 있습니다. 입국은 어렵습니다."
        return c.get("reject")

    if game_result == "잘못 거절":
        rc = reject_count if reject_count in (1, 2, 3) else 1
        if speaker == "심사관":
            if order == 1:
                return INSPECTOR_REJECT_GENERIC
            # order 3 (또는 그 이상의 심사관 라인) = 번복
            return INSPECTOR_REVERSAL.get(rc, INSPECTOR_REVERSAL[1])
        # 캐릭터
        if order == 2:
            return c["protest"].get(rc, c["protest"][1])
        # order 4 (또는 그 이상의 캐릭터 라인) = 수긍
        return c["accept"].get(rc, c["accept"][1])

    return None


def fill_authored(customer, report=None):
    """customer dialogueCases 의 잔여 [TODO 대사] 를 작성 폴백으로 채운다(in-place).

    branch_dialogue_map.fill_customer_dialogue 이후에 호출. 이미 채워진 라인은
    [TODO] 가 없으므로 건드리지 않는다. 구조 불변, text 만 교체.

    report: 선택. (ctype, caseType, gameResult) -> {'authored':n,'still_todo':n}
    반환: (authored, still_todo) 이번 손님 누적.
    """
    ctype = customer["characterType"]
    authored = 0
    still_todo = 0
    for case in customer["dialogueCases"]:
        ct = case["caseType"]
        gr = case["gameResult"]
        rc = case.get("rejectCount", 0)
        for ln in case["lines"]:
            if TODO not in ln["text"]:
                continue
            tx = authored_text(ctype, ct, gr, rc, ln.get("order"), ln.get("speaker"))
            if tx:
                ln["text"] = tx
                authored += 1
                if report is not None:
                    report.setdefault((ctype, ct, gr), {"authored": 0, "still_todo": 0})["authored"] += 1
            else:
                still_todo += 1
                if report is not None:
                    report.setdefault((ctype, ct, gr), {"authored": 0, "still_todo": 0})["still_todo"] += 1
    return authored, still_todo
