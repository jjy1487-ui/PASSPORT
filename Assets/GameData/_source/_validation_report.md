# 여권 주세요 — 데이터 검증 리포트

- 원본: `여권_정리_updated.xlsx`
- 시트: 23개
- **오류(error): 0건**
- 참고(note): 6건

## 오류 (해결 필요)
- 없음 (참조 누락/범위 오류 0)

## 참고 / 정보
- day_schedule 데이터 행 수: 98 (기대 98)
- 14일 x 7슬롯 98건 전부 채움 OK
- [확률주의] defect_rule rule_id=6 random_valid_chance 비수치='고정(통과1/거절0)' (런타임 특수처리 필요)
- 비자 보유 고객 수: 19
- PCR 보유 고객 수: 19
- 취업증빙 보유 고객 수: 5

## 변환기 경고
- character_payout: 메인 엑셀 1차 소스 사용(EXTRA 무시)
- character_score: 메인 엑셀 1차 소스 사용(EXTRA 무시)