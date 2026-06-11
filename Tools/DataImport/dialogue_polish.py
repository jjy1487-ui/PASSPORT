# -*- coding: utf-8 -*-
"""
dialogue_polish.py — day JSON 대사 정화(post-processing). 파이프라인의 마지막 손질 단계.

이 모듈은 build_days(2~14) 와 apply_character_dialogue_day1(day1) 양쪽에서
손님 한 명(customer_entry)을 만든 뒤 마지막에 호출한다. **단일 정화 지점**(6장: 변경 흡수 한 곳).

세 가지를 처리한다(작업 지시):
  (1) 액션/시스템/몽타주 narration 라인 제거.
      - 게임 시스템(얼굴대조/지문/X-ray)이 처리할 흐름 지시문이 '대사 라인'으로 들어간 것을 드롭한다.
      - 라인 text 가 `(플레이어)`/`[시스템]`/`[모션]`/`[UI`/`[결과]`/`[사전 이벤트]` 로 시작하거나
        `↔ ... 대조|재대조` 포함 / `몽타주 인식`·`지문 인식 결과`·`X-ray —` 등 시스템 narration 이면 드롭.
      - 단 실제 발화(「」 또는 손님/심사관이 말하는 문장)는 보존한다.
      - 라인을 드롭하면 같은 case 의 order 를 1부터 재정렬한다(구조는 자연 시퀀스 유지).

  (2) 외국인(비KOR) 손님 대사를 `{모국어} ({한국어})` 형식으로 통일.
      - 국적별 모국어: JPN=일본어, CHN=중국어, USA/그 외 서양=영어. 한 캐릭터 안에서 언어 일관.
      - 언어 혼용(한국어+영어 등)을 제거하고 항상 한국어 번역을 괄호로 병기한다.
      - 순수 지문(괄호·말줄임표만 있는 action 묘사)은 그대로 둔다(발화가 아님).

  (3) 윤서린(범죄자(성형수술)) 거절 반응 다양화 + 마스크 흐름 정리.
      - 캐논 rejectReaction 한 줄이 모든 거절 케이스에 반복되던 것을 케이스별 변주로 교체.
      - 분기 거부(보스) 시퀀스는 손님 발화를 '위치'별로 교체한다: 심사관 마스크 요청 라인을
        앵커로 그 이전=오프닝 인사, 직후=마스크 머뭇/거부, 신원 적발 후(마지막)=수긍/침묵.
        (전 줄을 같은 머뭇 대사로 덮어 인사가 마스크 요청보다 먼저 나오던 역순/반복 버그 수정.)

설계 원칙:
  - 케이스 '개수'는 불변(라인 드롭은 case 안에서만). 라인 수는 (1)에서 줄 수 있으나 order 재정렬로 일관 유지.
  - idempotent: 이미 정화된 입력에 다시 돌려도 같은 결과(번역 병기는 재병기 안 함, 시스템 라인은 이미 없음).
  - 결정론: 난수·시각 의존 없음.
  - 모든 텍스트 utf-8, 다국어 보존.
"""
import re


# ── 국적 코드 도출 ───────────────────────────────────────────
def nat_code(nationality):
    """'대한민국(KOR)' / 'KOR' / 'USA' -> 'KOR'/'USA'... 괄호 코드 우선."""
    s = str(nationality or "")
    m = re.search(r"\(([A-Z]+)\)", s)
    if m:
        return m.group(1)
    return s.strip()


# 서양/영어권으로 묶을 코드(영어 모국어). 그 외 비KOR 은 국적별 언어로.
ENGLISH_NATS = {"USA", "GBR", "CAN", "AUS", "IRL", "NZL"}


def native_lang(code):
    """국적 코드 -> 모국어 키('ja'|'zh'|'en'). 한국/미지정은 None(=정화 대상 아님)."""
    if code == "KOR" or not code:
        return None
    if code == "JPN":
        return "ja"
    if code == "CHN":
        return "zh"
    if code in ENGLISH_NATS:
        return "en"
    # 그 외 비KOR(서류상 4개국 제한이지만 안전망) → 영어로 폴백.
    return "en"


# ── (1) 액션/시스템 라인 필터 ────────────────────────────────
# text 가 이 접두로 시작하면 시스템/연출 narration → 대사에서 드롭.
_DROP_PREFIXES = ("(플레이어)", "[시스템]", "[모션]", "[UI", "[결과]", "[사전 이벤트]",
                  "(플레이어 선택)", "(낮은 목소리로)")
# text 안에 이 패턴이 있으면(인용문 없는 절차 narration) 드롭.
_DROP_CONTAINS = (
    "지문 인식 결과",
    "몽타주 인식",
    "몽타주 대조",
    "X-ray —",
    "X-ray 검사 결과",
    "[거부 대사 템플릿]",
)
_RE_CROSSCHECK = re.compile(r"↔.*(대조|재대조)")     # '... ↔ ... 대조/재대조'
_RE_ARROW_REF = re.compile(r"→\s*\[.*?\]\s*참조")      # '→ [...] 참조'
_RE_QUOTE = re.compile(r"「.+?」", re.S)


def _is_action_system_line(text):
    """이 라인이 시스템/연출 narration(=대사 아님)이면 True → 드롭 대상.
    실제 발화(「」 인용문 포함)는 절대 드롭하지 않는다(보존 우선)."""
    s = (text or "").strip()
    if not s:
        return True
    # 실제 발화 보존: 「」 인용문이 있으면 발화로 본다(시스템 라인엔 인용문이 없다).
    if _RE_QUOTE.search(s):
        return False
    if s.startswith(_DROP_PREFIXES):
        return True
    if any(k in s for k in _DROP_CONTAINS):
        return True
    if _RE_CROSSCHECK.search(s):
        return True
    if _RE_ARROW_REF.search(s):
        return True
    return False


def _filter_action_lines(customer):
    """각 dialogueCase 에서 시스템/연출 라인을 제거하고 order 를 1부터 재정렬.
    반환: 제거한 라인 수."""
    removed = 0
    for case in customer.get("dialogueCases", []):
        kept = []
        for ln in case.get("lines", []):
            if _is_action_system_line(ln.get("text")):
                removed += 1
                continue
            kept.append(ln)
        # order 재정렬(1부터 순차) — 라인 드롭 후 빈 번호/중복 방지.
        for i, ln in enumerate(kept, start=1):
            ln["order"] = i
        case["lines"] = kept
    return removed


# ── (2) 외국인 대사 -> {모국어} ({한국어}) ──────────────────
# 번역 테이블: (lang, 원문) -> 최종 표시문("{native} ({korean})").
# 캐논·authored 에 나온 모든 외국인 손님 라인을 1:1 로 정규화(뉘앙스 보존, 결정론).
#  - 순수 모국어 원문   : 한국어 번역을 괄호로 병기.
#  - 순수 한국어 원문   : 같은 의미의 모국어 문장을 앞에 붙임.
#  - 혼용/괄호병기 원문 : 모국어를 국적 언어로 통일하고 한국어 번역 정리.
# 키의 lang 은 그 손님 모국어(native_lang). 같은 한국어라도 국적별로 모국어가 달라 lang 으로 분리한다.
POLISH_MAP = {
    # ===== 영어권(USA 등) — en =====
    ("en", "Here is my passport."): "Here is my passport. (여기 여권 드릴게요.)",
    ("en", "Hello, could you check this please?"): "Hello, could you check this, please? (안녕하세요, 이것 좀 확인해 주시겠어요?)",
    ("en", "Good morning~ Excited to be here!"): "Good morning! I'm so excited to be here. (안녕하세요! 오게 되어 정말 설레요.)",
    ("en", "Good morning, here's my passport."): "Good morning. Here's my passport. (안녕하세요. 여권 여기 있어요.)",
    ("en", "Hello! Is that a fan meeting going on over there? Huge crowd outside. Anyway, here's my passport."):
        "Hello! Is there a fan event going on? There's a huge crowd outside. Anyway, here's my passport. "
        "(안녕하세요! 저기 팬 미팅이라도 있나요? 밖에 사람이 엄청 많네요. 아무튼, 여권 여기요.)",
    ("en", "Hello, I brought all my health documents just in case. I heard the requirements have been updated. Here."):
        "Hello, I brought all my health documents just in case. I heard the requirements were updated. Here. "
        "(안녕하세요, 혹시 몰라 건강 서류를 다 챙겨 왔어요. 요건이 바뀌었다고 들어서요. 여기요.)",
    ("en", "Hello, I heard there were some health concerns at the airport recently. I'm perfectly fine, here's my passport."):
        "Hello, I heard there were some health concerns at the airport recently. I'm perfectly fine. Here's my passport. "
        "(안녕하세요, 요즘 공항에 방역 문제가 좀 있다고 들었어요. 저는 멀쩡합니다. 여권 여기요.)",
    ("en", "안녕하세요. (최대한 자연스러운 척 하며)"):
        "Hello. (애써 자연스러운 척하며) (안녕하세요.)",
    ("en", "안녕하세요. 사람 많은 데 오는 게 좀 무섭긴 한데, 뭐 별 수 없죠. (억지로 웃으며) 여권 여기요."):
        "Hello. Crowded places make me a little nervous, but what can you do. (억지로 웃으며) Here's my passport. "
        "(안녕하세요. 사람 많은 데 오는 게 좀 무섭긴 한데, 뭐 별 수 없죠. 여권 여기요.)",
    # 승인 반응
    ("en", "Thank you! Appreciate your help."): "Thank you! I appreciate your help. (감사합니다! 도와주셔서 고마워요.)",
    ("en", "Thank you~ So happy!"): "Thank you! I'm so happy. (감사합니다! 정말 기뻐요.)",
    ("en", "Thanks~ I'm so excited!"): "Thanks! I'm so excited! (고맙습니다! 너무 신나요!)",
    ("en", "Thank you so much!"): "Thank you so much! (정말 감사합니다!)",
    ("en", "...감사합니다. (서둘러 이동한다.)"): "...Thank you. (서둘러 이동한다.) (...감사합니다.)",
    ("en", "감사합니다. 몸이 안 좋아서 빨리 가야 해요."):
        "Thank you. I'm not feeling well, so I should hurry. (감사합니다. 몸이 안 좋아서 빨리 가봐야겠어요.)",
    ("en", "감사합니다~ 좋은 하루 보내세요."): "Thank you! Have a good day. (감사합니다! 좋은 하루 보내세요.)",
    ("en", "감사해요. 빨리 쉬어야 해요."): "Thank you. I really need to rest. (감사합니다. 빨리 쉬어야 해서요.)",
    # 거절 반응
    ("en", "I see, my mistake."): "I see, my mistake. (그렇군요, 제 실수네요.)",
    ("en", "I understand, I'll sort it out."): "I understand. I'll sort it out. (알겠습니다. 제가 정리할게요.)",
    ("en", "I'm sorry, I'll fix it."): "I'm sorry, I'll fix it. (죄송해요, 제가 고칠게요.)",
    ("en", "My apologies, I'll fix this."): "My apologies, I'll fix this. (죄송합니다, 제가 바로잡을게요.)",
    ("en", "Oh no... I'm so sorry, I didn't realize."): "Oh no... I'm so sorry, I didn't realize. (이런... 정말 죄송해요, 미처 몰랐어요.)",
    ("en", "Oh, I see. Thank you for letting me know."): "Oh, I see. Thank you for letting me know. (아, 그렇군요. 알려주셔서 감사해요.)",
    ("en", "Sorry about that, I'll sort it."): "Sorry about that, I'll sort it out. (죄송해요, 제가 정리하겠습니다.)",
    ("en", "This is ridiculous! My documents are all correct!"):
        "This is ridiculous! My documents are all correct! (말도 안 돼요! 제 서류는 다 정상이라고요!)",
    # 잘못 거절 항의/수긍(authored 혼용 라인 정리)
    ("en", "Oh 정말 다행이에요! 고마워요!"): "Oh, what a relief! Thank you! (아, 정말 다행이에요! 고마워요!)",
    ("en", "정말 다행이에요. 여행할 수 있어서 다행이에요."):
        "What a relief. I'm so glad I can travel after all. (정말 다행이에요. 여행할 수 있어서 다행이에요.)",
    ("en", "기분이 안 좋아요. You are so rude."): "That was unpleasant. You were quite rude. (기분이 좀 상하네요. 너무 무례하셨어요.)",
    # 전염병/장기/범죄 등 비관광 영어권 — authored 한국어 라인 정리
    ("en", "아, 다행이네요. 감사합니다."): "Oh, what a relief. Thank you. (아, 다행이네요. 감사합니다.)",
    ("en", "휴... 들어갈 수 있어 다행이에요."): "Phew... I'm glad I can get through. (휴... 들어갈 수 있어 다행이에요.)",
    ("en", "이제야 되네요. 다음엔 빨리 좀 봐 주세요."): "Finally. Please be quicker next time. (이제야 되네요. 다음엔 좀 빨리 봐 주세요.)",
    ("en", "다행이네요. 감사합니다."): "What a relief. Thank you. (다행이네요. 감사합니다.)",
    ("en", "네, 확인 감사합니다."): "Yes, thank you for checking. (네, 확인 감사합니다.)",
    ("en", "하... 이제야 되네요. 꼼꼼히 봐 주세요."): "Ugh... finally. Please look more carefully. (하... 이제야 되네요. 좀 꼼꼼히 봐 주세요.)",
    ("en", "여권 여기 있어요. 빨리 좀 부탁드려요."): "Here's my passport. Please make it quick. (여권 여기 있어요. 빨리 좀 부탁드려요.)",
    ("en", "거 봐요. 그럼 들어갈게요."): "See? I'll be on my way, then. (거 봐요. 그럼 들어갈게요.)",
    ("en", "네, 그럼 이만 가보겠습니다."): "Right, I'll be going then. (네, 그럼 이만 가보겠습니다.)",
    ("en", "참... 진작 보내주지. 갑니다."): "Honestly... you could've let me through sooner. I'm off. (참... 진작 보내주지. 갑니다.)",
    ("en", "여기 검사서랑 같이 드릴게요. (기침)"): "I'll hand this over with my test result. (기침) (여기 검사서랑 같이 드릴게요.)",
    ("en", "체류 관련 서류도 같이 드릴게요."): "I'll give you my residence documents as well. (체류 관련 서류도 같이 드릴게요.)",

    # ===== 일본(JPN) — ja =====
    ("ja", "Here is my passport."): "パスポートをどうぞ。 (여기 여권 드릴게요.)",
    ("ja", "おはようございます！"): "おはようございます！ (안녕하세요!)",
    ("ja", "こんにちは。確認お願いします。"): "こんにちは。確認をお願いします。 (안녕하세요. 확인 부탁드립니다.)",
    ("ja", "よろしくお願いします。"): "よろしくお願いします。 (잘 부탁드립니다.)",
    ("ja", "よろしくお願いします。外がすごく混んでいました。ファンの方が多いみたいですね。"):
        "よろしくお願いします。外はすごく混んでいました。ファンの方が多いみたいですね。 "
        "(잘 부탁드립니다. 밖이 엄청 붐비더라고요. 팬분들이 많은가 봐요.)",
    ("ja", "Hello. After everything that happened... I wasn't sure about coming, but here I am. Passport."):
        "色々あって…来るか迷いましたが、来ました。パスポートです。 "
        "(여러 일이 있어서… 올지 망설였는데, 결국 왔어요. 여권입니다.)",
    ("ja", "안녕하세요. 요즘 공항 분위기가 많이 가라앉았죠? 덕분에 편하게 왔어요."):
        "こんにちは。最近、空港の雰囲気がだいぶ落ち着きましたね。おかげで楽に来られました。 "
        "(안녕하세요. 요즘 공항 분위기가 많이 가라앉았죠? 덕분에 편하게 왔어요.)",
    # 승인 반응
    ("ja", "Thank you so much!"): "本当にありがとうございます！ (정말 감사합니다!)",
    ("ja", "Thanks! Can't wait to explore!"): "ありがとう！早く見て回りたいです！ (고맙습니다! 빨리 둘러보고 싶어요!)",
    ("ja", "ありがとう！韓国楽しみます。"): "ありがとう！韓国を楽しみます。 (감사합니다! 한국 여행 즐길게요.)",
    ("ja", "감사해요. 빨리 쉬어야 해요."): "ありがとうございます。早く休まないと。 (감사합니다. 빨리 쉬어야 해서요.)",
    ("ja", "고맙습니다. (지친 표정으로 이동한다.)"): "ありがとうございます。 (지친 표정으로 이동한다.) (고맙습니다.)",
    # 거절 반응
    ("ja", "Please reconsider, my documents are valid."):
        "もう一度確認してください。書類は問題ないはずです。 (다시 확인해 주세요. 서류는 문제없을 텐데요.)",
    ("ja", "あ、そうなんですか。すみません。"): "あ、そうなんですか。すみません。 (아, 그래요? 죄송합니다.)",
    ("ja", "あ、分かりました。ごめんなさい。"): "あ、分かりました。ごめんなさい。 (아, 알겠습니다. 죄송합니다.)",
    ("ja", "あ、本当ですか？知らなかったです。"): "あ、本当ですか？知りませんでした。 (아, 정말요? 몰랐어요.)",
    ("ja", "すみません。また確認して来ます。"): "すみません。もう一度確認してきます。 (죄송합니다. 다시 확인하고 오겠습니다.)",
    ("ja", "何かの間違いだと思います！"): "何かの間違いだと思います！ (뭔가 착오가 있는 것 같아요!)",
    # 잘못 거절 항의/수긍(authored 혼용 라인 정리)
    ("ja", "Oh 정말 다행이에요! 고마워요!"): "ああ、よかった！ありがとうございます！ (아, 정말 다행이에요! 고마워요!)",
    ("ja", "정말 다행이에요. 여행할 수 있어서 다행이에요."):
        "本当によかったです。旅行できて安心しました。 (정말 다행이에요. 여행할 수 있어서 다행이에요.)",
    ("ja", "기분이 안 좋아요. You are so rude."): "少し気分が悪いです。失礼ですよ。 (기분이 좀 상하네요. 너무 무례하시네요.)",
    # 전염병/테러 등 비관광 일본 — authored 한국어 라인 정리(테러범 톤 보존)
    ("ja", "아, 다행이네요. 감사합니다."): "ああ、よかった。ありがとうございます。 (아, 다행이네요. 감사합니다.)",
    ("ja", "휴... 들어갈 수 있어 다행이에요."): "ふぅ…入れてよかったです。 (휴... 들어갈 수 있어 다행이에요.)",
    ("ja", "이제야 되네요. 다음엔 빨리 좀 봐 주세요."): "やっと通れますね。次はもう少し早くお願いします。 (이제야 되네요. 다음엔 좀 빨리 봐 주세요.)",
    ("ja", "여권 여기 있다. 빨리 처리해."): "パスポートだ。早く処理しろ。 (여권 여기 있다. 빨리 처리해.)",
    ("ja", "...그래. 들어가지."): "…ああ。入る。 (...그래. 들어가지.)",
    ("ja", "진작 그럴 것이지."): "最初からそうすればいいんだ。 (진작 그럴 것이지.)",
    ("ja", "...기억해 두지. 비켜."): "…覚えておく。どけ。 (...기억해 두지. 비켜.)",
    ("ja", "여기 검사서랑 같이 드릴게요. (기침)"): "検査書と一緒にお渡しします。 (기침) (여기 검사서랑 같이 드릴게요.)",

    # ===== 중국(CHN) — zh (간체. 폰트가 □ 날 수 있으나 한국어 병기로 의미 보장) =====
    ("zh", "Here is my passport."): "这是我的护照。 (여기 여권 드릴게요.)",
    ("zh", "你好！第一次来韩国。"): "你好！第一次来韩国。 (안녕하세요! 한국은 처음이에요.)",
    ("zh", "你好～准备好了！"): "你好，都准备好了！ (안녕하세요, 준비 다 됐어요!)",
    ("zh", "您好，请帮我看看。"): "您好，请帮我看看。 (안녕하세요, 좀 봐주세요.)",
    ("zh", "Hello, I've heard a lot of people are applying for long-term stay here recently. I have all my work documents ready."):
        "您好，最近听说很多人申请长期居留。我的工作材料都带齐了。 "
        "(안녕하세요, 요즘 장기 체류 신청이 많다고 들었어요. 취업 서류 다 챙겨 왔습니다.)",
    ("zh", "Hello. I heard there's been more identity verification lately. I have everything in order."):
        "您好，听说最近身份核查多了。我的材料都齐全。 "
        "(안녕하세요, 요즘 신원 확인이 강화됐다고 들었어요. 서류는 다 갖췄습니다.)",
    ("zh", "I double-checked everything before coming. Heard someone got rejected over a tiny mismatch. Here you go."):
        "来之前我反复检查过了。听说有人因为一点不符就被拒。给您。 "
        "(오기 전에 몇 번이나 확인했어요. 사소한 불일치로 거절당한 사람이 있다더라고요. 여기요.)",
    ("zh", "最近很多人来韩国工作或留学。我也是来工作的，文件都带来了。(최근 취업·유학 방문이 많다. 나도 취업 목적이고 서류 다 준비했다.)"):
        "最近很多人来韩国工作或留学。我也是来工作的，文件都带来了。 "
        "(요즘 취업·유학으로 한국 오는 사람이 많죠. 저도 취업하러 왔고 서류 다 준비했어요.)",
    ("zh", "안녕하세요. 공항에서 뭔가 조사한다는 얘기 들었어요. 저는 친척 방문이라 별 관계 없겠죠? (조심스럽게)"):
        "您好。听说机场在查什么。我是来探亲的，应该没关系吧？ (조심스럽게) "
        "(안녕하세요. 공항에서 뭔가 조사한다는 얘기 들었어요. 저는 친척 방문이라 별 관계 없겠죠?)",
    ("zh", "안녕하세요. 기준 강화됐다는 얘기 듣고 좀 긴장했어요. (서류 꺼내며) 다 갖춰왔습니다."):
        "您好。听说标准严了，有点紧张。 (서류 꺼내며) 都带齐了。 "
        "(안녕하세요. 기준이 강화됐다는 얘기 듣고 좀 긴장했어요. 다 갖춰왔습니다.)",
    # 승인 반응
    ("zh", "Thank you! Have a great day!"): "谢谢！祝您愉快！ (감사합니다! 좋은 하루 되세요!)",
    ("zh", "Thank you! Lovely service."): "谢谢！服务真好。 (감사합니다! 친절하시네요.)",
    ("zh", "谢谢！终于过了！"): "谢谢！终于过了！ (감사합니다! 드디어 통과네요!)",
    ("zh", "谢谢，很快啊！"): "谢谢，很快啊！ (감사합니다, 빠르네요!)",
    ("zh", "감사합니다. 잘 다녀오겠습니다."): "谢谢，我会好好的。 (감사합니다. 잘 다녀오겠습니다.)",
    ("zh", "감사해요. 빨리 나가야겠어요."): "谢谢，我得赶紧走了。 (감사합니다. 빨리 나가야겠어요.)",
    ("zh", "고맙습니다. 빨리 들어가고 싶네요."): "谢谢，我想快点进去。 (고맙습니다. 빨리 들어가고 싶네요.)",
    # 거절 반응
    ("zh", "哎呀，我搞错了。"): "哎呀，我搞错了。 (아이고, 제가 착각했네요.)",
    ("zh", "哦，原来那里有问题。"): "哦，原来那里有问题。 (아, 거기에 문제가 있었군요.)",
    ("zh", "哦，真的吗？不知道，对不起。"): "哦，真的吗？我不知道，对不起。 (아, 정말요? 몰랐어요, 죄송합니다.)",
    ("zh", "对不起。我再确认一下来。"): "对不起，我再确认一下。 (죄송합니다. 다시 확인하고 올게요.)",
    ("zh", "对不起，我不知道这个。"): "对不起，我不知道这个。 (죄송합니다. 이건 몰랐어요.)",
    ("zh", "我把一切都准备好了！"): "我把一切都准备好了！ (전 다 준비해 왔다고요!)",
    ("zh", "来之前我仔细检查过了！"): "来之前我仔细检查过了！ (오기 전에 꼼꼼히 확인했어요!)",
    # 잘못 거절 항의/수긍(authored 혼용 라인 정리)
    ("zh", "Oh 정말 다행이에요! 고마워요!"): "啊，太好了！谢谢您！ (아, 정말 다행이에요! 고마워요!)",
    ("zh", "정말 다행이에요. 여행할 수 있어서 다행이에요."): "真是太好了，能旅行真让人安心。 (정말 다행이에요. 여행할 수 있어서 다행이에요.)",
    ("zh", "기분이 안 좋아요. You are so rude."): "我有点不高兴。您太没礼貌了。 (기분이 좀 상하네요. 너무 무례하시네요.)",
    # 전염병/장기 등 비관광 중국 — authored 한국어 라인 정리
    ("zh", "아, 다행이네요. 감사합니다."): "啊，太好了。谢谢。 (아, 다행이네요. 감사합니다.)",
    ("zh", "휴... 들어갈 수 있어 다행이에요."): "呼…能进去太好了。 (휴... 들어갈 수 있어 다행이에요.)",
    ("zh", "이제야 되네요. 다음엔 빨리 좀 봐 주세요."): "终于可以了。下次请快一点。 (이제야 되네요. 다음엔 좀 빨리 봐 주세요.)",
    ("zh", "네, 확인 감사합니다."): "好的，谢谢确认。 (네, 확인 감사합니다.)",
    ("zh", "다행이네요. 감사합니다."): "太好了，谢谢。 (다행이네요. 감사합니다.)",
    ("zh", "하... 이제야 되네요. 꼼꼼히 봐 주세요."): "唉…终于可以了。请仔细看清楚。 (하... 이제야 되네요. 좀 꼼꼼히 봐 주세요.)",
    ("zh", "여기 검사서랑 같이 드릴게요. (기침)"): "我把检查报告一起给您。 (기침) (여기 검사서랑 같이 드릴게요.)",
    ("zh", "체류 관련 서류도 같이 드릴게요."): "居留相关材料也一起给您。 (체류 관련 서류도 같이 드릴게요.)",
}


# 순수 action 묘사(발화 아님)는 외국어 정화 대상에서 제외. 예: "...(아무 말 없이 빠르게 통과한다.)"
# 전체가 말줄임표/괄호 안 묘사로만 이뤄진 라인은 그대로 둔다(번역 병기 불필요).
_RE_ACTION_ONLY = re.compile(r"^[\.…\s]*\([^)]*\)[\.…\s]*$")


def _has_native_paren(text):
    """이미 '... (한국어)' 형태로 정화돼 있으면 True(idempotent 보호)."""
    s = (text or "").rstrip()
    return s.endswith(")") and "(" in s


def _polish_foreign_line(text, lang):
    """외국인 손님 라인 1개를 {모국어} ({한국어})로 정규화. 맵에 없으면 폴백."""
    s = (text or "").strip()
    if not s:
        return text
    # 순수 action 묘사는 발화가 아님 → 그대로.
    if _RE_ACTION_ONLY.match(s):
        return text
    mapped = POLISH_MAP.get((lang, s))
    if mapped is not None:
        return mapped
    # 폴백: 이미 한국어 괄호 병기가 있으면(정화본/캐논 병기) 그대로 둔다.
    if _has_native_paren(s) and not _RE_ACTION_ONLY.match(s):
        # 한국어가 이미 병기되어 있고 앞이 외국어면 통과(언어 혼용은 아래에서만 걸러짐).
        return text
    # 그 외 미매핑 라인: 안전하게 원문 유지(보존 우선). 리포트에서 잡아 후속 매핑.
    return text


def _polish_foreign(customer):
    """외국인(비KOR) 손님의 손님 라인을 모국어({한국어}) 형식으로 정규화.
    심사관/시스템 라인은 건드리지 않는다. 반환: (정규화 라인 수, 미매핑 라인 [(text)...])."""
    code = nat_code(customer.get("nationality"))
    lang = native_lang(code)
    if lang is None:
        return 0, []
    name_kr = customer.get("nameKr") or ""
    visitor_speakers = {"캐릭터", "손님", name_kr}
    changed = 0
    unmapped = []
    for case in customer.get("dialogueCases", []):
        for ln in case.get("lines", []):
            if ln.get("speaker") not in visitor_speakers:
                continue
            old = ln.get("text") or ""
            new = _polish_foreign_line(old, lang)
            if new != old:
                ln["text"] = new
                changed += 1
            elif (not _RE_ACTION_ONLY.match(old.strip())
                  and (lang, old.strip()) not in POLISH_MAP
                  and not _has_native_paren(old)):
                unmapped.append((code, old))
    return changed, unmapped


# ── (3) 윤서린(범죄자(성형수술)) 거절 반응 다양화 + 마스크 흐름 ──────────────
# 캐논 rejectReaction("...저는 그런 사람이 아닌데요. (눈이 흔들린다.)") 한 줄이 모든 거절
# 케이스에 반복돼 단조롭고, 마스크 벗기 요청 직후에도 같은 말이 나와 흐름이 어색했다.
# → 케이스(gameResult, rejectCount)별로 다른 반응으로 교체. 마스크 요청 뒤 손님 라인은
#   '머뭇/거부', 신원 적발 뒤 손님 라인은 '수긍/침묵'으로.
PLASTIC_WANTED_TYPE = "범죄자(성형수술)"

# (gameResult, rejectCount) -> 손님 라인 변주. 거절 반응을 케이스별로 다양화.
_YOON_REJECT_VARIANTS = {
    ("정상 거절", 0): "...뭔가 오해가 있는 것 같은데요. (시선을 피한다.)",
    ("잘못 거절", 1): "네? 어디가 문제라는 거죠? (선글라스 너머로 눈을 굴린다.)",
    ("잘못 거절", 2): "분명히 다 맞게 가져왔어요. 다시 봐 주세요.",
    ("잘못 거절", 3): "왜 자꾸 사람을 의심해요? 제대로 좀 확인하세요!",
}

# 분기 거부(보스 시퀀스) 손님 라인 — 흐름상 위치별 발화.
_YOON_BRANCH_OPENING = "...(선글라스를 낀 채 조용히 여권을 내민다.) 안녕하세요."
_YOON_BRANCH_MASK     = "...꼭 벗어야 하나요? (마스크를 만지작거린다.)"
_YOON_BRANCH_CAUGHT   = "...(굳은 표정으로 잠시 침묵한다.) ...변호사 부르겠습니다."

# 심사관 마스크 요청 라인(앵커). 이 라인 이전 손님 발화=오프닝, 이후=마스크 머뭇.
_RE_MASK_REQUEST = re.compile(r"마스크.*벗어")


def _polish_yoon(customer):
    """범죄자(성형수술) 손님의 거절 반응을 케이스별로 다양화하고, '분기 거부' 시퀀스의
    손님 라인을 오프닝→마스크 요청→머뭇→신원 적발→수긍 흐름으로 교체. 반환: 교체한 라인 수."""
    if customer.get("characterType") != PLASTIC_WANTED_TYPE:
        return 0
    name_kr = customer.get("nameKr") or ""
    visitor_speakers = {"캐릭터", "손님", name_kr}
    n = 0

    for case in customer.get("dialogueCases", []):
        ct = case.get("caseType")
        gr = case.get("gameResult")
        rc = case.get("rejectCount", 0)
        lines = case.get("lines", [])

        # 일반 거절 케이스: 손님 라인을 케이스별 변주로.
        if ct == "일반 심사" and (gr, rc) in _YOON_REJECT_VARIANTS:
            variant = _YOON_REJECT_VARIANTS[(gr, rc)]
            for ln in lines:
                if ln.get("speaker") in visitor_speakers and "그런 사람이 아닌데요" in (ln.get("text") or ""):
                    ln["text"] = variant
                    n += 1
                    break  # 잘못 거절의 항의 라인(첫 손님 라인)만 교체. 수긍 라인은 캐논 유지.
            continue

        # 분기 거부(보스 시퀀스): (1) 필터로 시스템 라인은 이미 제거됨.
        #   남은 손님 라인을 흐름상 '위치'에 맞게 교체한다(같은 말 반복·역순 방지).
        #   시퀀스(필터 후): [손님 오프닝] → 심사관 '마스크 벗어주시겠어요?' → [손님:마스크 머뭇]
        #                   → 심사관 지문/X-ray/적발 멘트들 → 심사관 '지명수배자 …' → [손님:수긍/침묵]
        #   마스크 요청 라인을 앵커로, 그 이전 손님 발화는 오프닝, 직후 손님 발화는 머뭇,
        #   마지막 손님 발화는 신원 적발 후 수긍으로 둔다(앵커가 없으면 첫=오프닝/끝=수긍 폴백).
        if ct == "분기 거부":
            mask_at = next((i for i, ln in enumerate(lines)
                            if ln.get("speaker") not in visitor_speakers
                            and _RE_MASK_REQUEST.search(ln.get("text") or "")), None)
            vis_idx = [i for i, ln in enumerate(lines) if ln.get("speaker") in visitor_speakers]
            if not vis_idx:
                continue
            last = vis_idx[-1]
            for i in vis_idx:
                if i == last:
                    text = _YOON_BRANCH_CAUGHT          # 신원 적발 후 = 수긍/체념
                elif mask_at is not None and i < mask_at:
                    text = _YOON_BRANCH_OPENING         # 마스크 요청 이전 = 오프닝 인사
                else:
                    text = _YOON_BRANCH_MASK            # 마스크 요청 이후(직후) = 머뭇/거부
                if lines[i].get("text") != text:
                    n += 1
                lines[i]["text"] = text
            continue

    return n


# ── 공개 진입점 ──────────────────────────────────────────────
def polish_customer(customer, report=None):
    """손님 1명의 dialogueCases 를 정화(in-place). 파이프라인 마지막에 호출.

    순서: (3) 윤서린 흐름 → (1) 액션/시스템 라인 필터 → (2) 외국인 언어 통일.
      (3)이 분기 거부 손님 라인을 먼저 자연스러운 발화로 교체한 뒤, (1)이 시스템 라인만 드롭하고,
      (2)가 외국인 발화에 한국어 병기. (윤서린은 JPN/CHN 아니라 (2) 영향 없음.)

    report: 선택. dict 누적. 키 'removed'/'foreign'/'yoon'/'unmapped'(list).
    반환: (removed, foreign_changed, yoon_changed).
    """
    yoon = _polish_yoon(customer)
    removed = _filter_action_lines(customer)
    foreign, unmapped = _polish_foreign(customer)
    if report is not None:
        report["removed"] = report.get("removed", 0) + removed
        report["foreign"] = report.get("foreign", 0) + foreign
        report["yoon"] = report.get("yoon", 0) + yoon
        if unmapped:
            report.setdefault("unmapped", []).extend(unmapped)
    return removed, foreign, yoon
