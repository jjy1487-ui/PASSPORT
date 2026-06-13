# -*- coding: utf-8 -*-
"""
4일차 전원 입장 대사에 '방문 목적 / 어디 다녀오셨어요' 질문을 주입한다.
- 한국인(비자 없음): 심사관 "어디 다녀오셨습니까?" + 귀국 답변 (대조 없음, 분위기용)
- 외국인(비자 있음): 심사관 "방문 목적이 어떻게 되십니까?" + 진술 + claim(attr=visa_type)
  - 토머스 무어: 비자=관광 → '관광' 진술(진실). 결함은 사진이라 거절 사유는 그대로 사진.
  - 리 강: 비자=장기체류 → '관광' 진술(거짓). 기존 함정 유지.
day4.json 을 직접 패치(런타임 실제 소스). 멱등 — 다시 돌려도 같은 결과.

실행: python Tools/_patch_day4_visit_purpose.py
"""
import json, os, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

DAY4 = r"C:\Users\chris\Documents\produc_build_reecture\Assets\Resources\GameData\day4.json"

VISA_CLAIM = {"attr": "visa_type", "value": "관광", "label": "방문 목적(진술)", "unlocksScan": ""}

def L(speaker, text, claim=None):
    d = {"speaker": speaker, "text": text}
    if claim is not None:
        d["claim"] = claim
    return d

# customerId -> {"main": [lines], "alt": [lines] or None}
# 입장(caseType="입장") 대사의 새 lines (order 는 적용 시 1..N 으로 재부여)
ENTRY = {
    41: {  # 박하준 (한국, 진상)
        "main": [
            L("박하준", "아니 왜 이렇게 오래 걸려요?"),
            L("심사관", "어디 다녀오셨습니까?"),
            L("박하준", "출장 다녀왔어요, 출장. 여권 냈으니까 빨리 처리해."),
        ],
        "alt": [
            L("박하준", "빨리 좀 해줘요, 나 바쁜 사람이야."),
            L("심사관", "어디 다녀오셨습니까?"),
            L("박하준", "출장이요. 빨리 좀 합시다."),
        ],
    },
    42: {  # 최예준 (한국, 일반)
        "main": [
            L("최예준", "안녕하세요. 요즘 여권 사진 때문에 문제됐다는 얘기 들었어요."),
            L("심사관", "어디 다녀오셨습니까?"),
            L("최예준", "친구 만나러 일본에 다녀왔어요. 여권 확인 부탁드립니다."),
        ],
        "alt": None,
    },
    43: {  # 토머스 무어 (미국, 외국인 관광객, 비자=관광, 결함=사진)
        "main": [
            L("토머스 무어", "Good morning! I'm so excited to be here. (안녕하세요! 오게 되어 정말 설레요.)"),
            L("심사관", "방문 목적이 어떻게 되십니까?"),
            L("토머스 무어", "I'm here for sightseeing. (관광하러 왔어요.)", dict(VISA_CLAIM)),
            L("토머스 무어", "Here is my passport. (여기 여권 드릴게요.)"),
        ],
        "alt": None,
    },
    30: {  # 노가은 (한국, 성형)
        "main": [
            L("노가은", "사진 때문에 민원이 많다고 들었어요...잘 봐주세요. (여권 내밀며)"),
            L("심사관", "어디 다녀오셨습니까?"),
            L("노가은", "해외에서 잠깐 지내다 왔어요. 사진이랑 좀 달라 보여도 저 맞아요."),
        ],
        "alt": [
            L("노가은", "(얼굴을 굳히며) 사진이랑 다르면 안 보내준다는 얘기 듣고 좀 긴장했어요."),
            L("심사관", "어디 다녀오셨습니까?"),
            L("노가은", "해외에서 잠깐 지내다 왔어요. 확인해 주세요."),
        ],
    },
    44: {  # 정시우 (한국, 진상)
        "main": [
            L("정시우", "신원 조사 때문에 이렇게 기다려야해? 빨리 처리해줘요."),
            L("심사관", "어디 다녀오셨습니까?"),
            L("정시우", "여행 좀 다녀왔어요. 여권 냈으니까 빨리 처리해."),
        ],
        "alt": [
            L("정시우", "아 진짜, 빨리 좀 해줘요."),
            L("심사관", "어디 다녀오셨습니까?"),
            L("정시우", "여행이요, 여행. 빨리요."),
        ],
    },
    45: {  # 강주원 (한국, 일반)
        "main": [
            L("강주원", "안녕하세요. 사진이랑 똑같이 생겼으니 걱정 없겠죠? (웃으며 여권 내밀며)"),
            L("심사관", "어디 다녀오셨습니까?"),
            L("강주원", "가족이랑 동남아 여행 다녀왔어요. 여권 확인 부탁드립니다."),
        ],
        "alt": None,
    },
    46: {  # 리 강 (중국, 외국인, 방문목적 거짓) — 심사관 질문만 추가, claim 유지
        "main": [
            L("리 강", "您好，请帮我看看。 (안녕하세요, 좀 봐주세요.)"),
            L("심사관", "방문 목적이 어떻게 되십니까?"),
            L("리 강", "我是来旅游观光的。 (관광하러 왔어요.)", dict(VISA_CLAIM)),
            L("리 강", "这是我的护照和签证。 (여권이랑 비자 여기요.)"),
        ],
        "alt": None,
    },
}


def set_entry_lines(cust_obj, new_lines):
    """cust_obj 의 dialogueCases 중 caseType=='입장' 의 lines 를 교체(order 재부여)."""
    cases = cust_obj.get("dialogueCases") or []
    for case in cases:
        if case.get("caseType") == "입장":
            lines = []
            for i, ln in enumerate(new_lines, start=1):
                out = {"order": i, "speaker": ln["speaker"], "text": ln["text"]}
                if "claim" in ln:
                    out["claim"] = ln["claim"]
                lines.append(out)
            case["lines"] = lines
            return True
    return False


def main():
    with open(DAY4, encoding="utf-8") as f:
        data = json.load(f)

    patched = []
    for cust in data.get("customers", []):
        cid = cust.get("customerId")
        spec = ENTRY.get(cid)
        if not spec:
            continue
        if set_entry_lines(cust, spec["main"]):
            patched.append(f"{cid}/{cust.get('nameKr')} (main)")
        alt = cust.get("altVariant")
        if spec.get("alt") and isinstance(alt, dict):
            if set_entry_lines(alt, spec["alt"]):
                patched.append(f"{cid}/{cust.get('nameKr')} (alt)")

    with open(DAY4, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

    print("패치 완료:")
    for p in patched:
        print("  -", p)
    print(f"총 {len(patched)}개 입장 블록 갱신")


if __name__ == "__main__":
    main()
