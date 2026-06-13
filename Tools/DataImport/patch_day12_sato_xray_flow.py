# -*- coding: utf-8 -*-
"""day12 사토 하루키(테러범) X-ray 해금 흐름 전환 타깃 패치 (전체 리빌드 금지).

새 흐름(Option B): 뉴스로 사토를 이름 지목하던 방식 폐기 →
  비자↔여권 여권번호 불일치(visa JP1012287 ≠ passport JP9911287) 적발 시 코드(CrossCheckController.
  DetectPassportNoMismatchUnlock)가 X-ray 잠금 해제 → 폭발물 부품(다리) 적발 → 거절.

이 패처가 day12.json 에 하는 일(전부 idempotent):
  (1) 뉴스 912000 "위험물 반입 경보" 를 특정인 미지목 일반 경보로 교체
      (claim 의 attr=name/value="SATO HARUKI"/unlocksScan="xray" 제거 → contraband 일반 단서, unlocksScan="").
  (2) 사토(customerId=11) 여권 문서 passport_no = JP9911287 로 정합(비자 JP1012287 과 불일치 유지, 앞자리 JP).
  (3) 사토 '정상 거절' 심사관 라인 = 정밀 검사 위험물 적발 멘트로 교체(claim.label="위험물 적발").
  (4) 사토 '정상 거절' 손님 라인 = "…(굳은 얼굴로 입을 다문 채 끌려 나간다.)" 로 교체(굳은 표정/끌려 나감).

day11(911000·JOHN CARTER)·day14(914000·KANG DOSIK) 뉴스/흐름은 별도 newsId 라 손대지 않는다.
사토 xray 데이터/correctResult(정상 거절)/characterType(테러범)/비자≠여권 번호 불일치는 유지.

build_days.py 소스(SCAN_TRIGGERS 에서 day12 제거 + GENERIC_ALERT_NEWS day12 추가 +
REJECT_INSPECTOR_OVERRIDE[11])도 동기화되어 있어 향후 리빌드 시 같은 결과를 재현한다.
"""
import json
import os

DAY12 = os.path.join(os.path.dirname(__file__), "..", "..",
                     "Assets", "Resources", "GameData", "day12.json")
DAY12 = os.path.normpath(DAY12)

NEWS_ID = 912000
SATO_CID = 11

GENERIC_NEWS_TITLE = "[속보] 위험물 반입 경보"
GENERIC_NEWS_CONTENT = (
    "국제 공조 수사 결과 최근 입국 경로에서 폭발물 부품·밀수품 은닉 사례가 다수 적발되었습니다. "
    "여권·비자 등 서류 정보가 서로 일치하지 않는 입국자는 위험물 반입 가능성을 의심해 정밀 검사를 시행하십시오."
)
GENERIC_NEWS_CLAIM = {
    "attr": "contraband",
    "value": "",
    "label": "위험물 반입 주의",
    "unlocksScan": "",
}

SATO_PASSPORT_NO = "JP9911287"  # 여권 문서 번호(비자 JP1012287 과 불일치, 앞자리 JP → 여권 규정 무위반)

SATO_REJECT_TEXT = "정밀 검사에서 위험물이 발견되었습니다. 입국을 허가할 수 없으며, 보안 절차에 회부됩니다."
SATO_REJECT_CLAIM = {
    "attr": "contraband",  # X-ray 위험물 적발 결과(거절 근거)
    "value": "",
    "label": "위험물 적발",
    "unlocksScan": "",
}
# 사토 '정상 거절' 손님 반응 라인(굳은 표정/끌려 나감). 입장 대사는 유지.
SATO_REJECT_VISITOR_TEXT = "…(굳은 얼굴로 입을 다문 채 끌려 나간다.)"


def patch_news(data):
    changed = False
    for news in data.get("news", []):
        if int(news.get("newsId", 0)) != NEWS_ID:
            continue
        before = json.dumps(news, ensure_ascii=False, sort_keys=True)
        news["title"] = GENERIC_NEWS_TITLE
        news["content"] = GENERIC_NEWS_CONTENT
        news["iconRef"] = news.get("iconRef", "")
        news["claims"] = [dict(GENERIC_NEWS_CLAIM)]
        after = json.dumps(news, ensure_ascii=False, sort_keys=True)
        if before != after:
            changed = True
    return changed


def patch_sato_reject(data):
    """사토 '정상 거절' 케이스: 심사관 라인(위험물 적발) + 손님 라인(끌려 나감) 교체.
    구조/케이스 수/order/speaker 는 불변, text/claim 만 교체."""
    changed = False
    for cust in data.get("customers", []):
        if int(cust.get("customerId", 0)) != SATO_CID:
            continue
        name_kr = cust.get("nameKr", "")
        for case in cust.get("dialogueCases", []):
            if case.get("caseType") == "일반 심사" and case.get("gameResult") == "정상 거절":
                for line in case.get("lines", []):
                    sp = line.get("speaker")
                    if sp == "심사관":
                        if line.get("text") != SATO_REJECT_TEXT:
                            line["text"] = SATO_REJECT_TEXT
                            changed = True
                        if line.get("claim") != SATO_REJECT_CLAIM:
                            line["claim"] = dict(SATO_REJECT_CLAIM)
                            changed = True
                    elif sp not in ("심사관", "시스템"):
                        # 손님 라인(speaker == nameKr 또는 캐릭터/손님) → 끌려 나감 반응으로 교체.
                        if line.get("text") != SATO_REJECT_VISITOR_TEXT:
                            line["text"] = SATO_REJECT_VISITOR_TEXT
                            changed = True
    return changed


def patch_sato_passport(data):
    """사토 여권 문서 passport_no = JP9911287 (비자 JP1012287 과 불일치 유지)."""
    changed = False
    for cust in data.get("customers", []):
        if int(cust.get("customerId", 0)) != SATO_CID:
            continue
        for doc in cust.get("documents", []):
            # 여권 문서 식별: documentType=="여권" 우선, 없으면 비자번호 없는 passport_no 필드 보유 문서.
            is_passport = doc.get("documentType") == "여권"
            if not is_passport:
                keys = {f.get("key") for f in doc.get("fields", [])}
                is_passport = ("passport_no" in keys) and ("visa_no" not in keys)
            if not is_passport:
                continue
            for fld in doc.get("fields", []):
                if fld.get("key") == "passport_no" and fld.get("value") != SATO_PASSPORT_NO:
                    fld["value"] = SATO_PASSPORT_NO
                    changed = True
    return changed


def main():
    with open(DAY12, encoding="utf-8") as f:
        data = json.load(f)

    n_changed = patch_news(data)
    p_changed = patch_sato_passport(data)
    s_changed = patch_sato_reject(data)

    if n_changed or p_changed or s_changed:
        with open(DAY12, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        print(f"[patch_day12] news={'updated' if n_changed else 'unchanged'} "
              f"passport={'updated' if p_changed else 'unchanged'} "
              f"sato_reject={'updated' if s_changed else 'unchanged'} -> wrote {DAY12}")
    else:
        print(f"[patch_day12] already up to date (idempotent no-op) -> {DAY12}")


if __name__ == "__main__":
    main()
