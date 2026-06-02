# branch_key 카탈로그 — 캐릭터 분기 점수/금액 (data-tools 소유)

> 원본: `Downloads/여권주세요_..._금액표추가_캐릭터선지추가_260602.xlsx`
> ④ 캐릭터별 분기별 지급 금액표 → `character_payout`
> ⑤ 캐릭터별 분기점 점수표(단순화) → `character_score`
> 두 테이블은 **(character_type, doc_state, defect_variant, branch_key)** 로 조인된다.
> 자유서술 분기 텍스트 → `branch_key` 정규화는 `branch_normalize.py` 한 곳에서만 한다(규약 6장).
> 게임플레이는 판정 확정 시 이 키로 점수표/금액표를 조회한다. branch_key를 **결정하는 것은 게임플레이**(상태머신 결과)다. data-tools는 키별 점수/돈/트리거만 제공.

## 조인 키
- `character_type`: customer 시트 character_type 어휘(FK). 신규 2종(사이비 신도/꼬마)은 customer 미등재.
- `doc_state`: `normal`(정상) | `defect`(불량).
- `defect_variant`: 같은 캐릭터의 결함/상태 세부 식별자. 없으면 null.
  - 외국인 관광객: `분실` | `출국X`
  - 검역 대상자(PCR): `1-A`(정상) | `1-B 출국X/만료` | `1-C 백신X` | `1-D 모두 미비`
  - 성형 의심 고객(점수 세부): `마스크 미요청` | `지문 미요청` | `몽타주O + 범인 아님` | `몽타주X`
  - 테러범: `상담` | `상담+신고` | `무관심` | `신고`
  - 특수(연예인): `얼굴O` | `얼굴X 마스크` | `매니저 대리`
  - 특수(정치인): `본인 얼굴O` | `보좌관 대리`
  - 특수(현자): `물질+성취+세계` 등 가치관 3단 조합 8종(아이템 1:1 매핑)
- `visit_round`: 사이비 신도 1/2/3회차. 없으면 null.
- `branch_key`: 아래 enum.

## branch_key enum 전체 (41종)

### 공통 기본 판정
| branch_key | 의미 | doc_state |
|---|---|---|
| `approve_correct` | 정상 입국(정답) | normal |
| `reject_correct` | 불량 거부(정답) | defect |
| `approve_wrong` | 불량인데 입국(오판) | defect |
| `reject_wrong` | 정상인데 거부(오판, ⭐단순화 벌금) | normal |

### 재거절 감액(성형/연예인/정치인) — 정상 손님
| branch_key | 의미 |
|---|---|
| `approve_immediate` | 즉시 입국(감액 0, ×1.0) |
| `approve_after_reject_1` | 1회 거절 후 입국(×0.5) |
| `approve_after_reject_2` | 2회 거절 후 입국(×0.3) |
| `approve_after_reject_3` | 3회 거절 후 강제 입국(×0.1) |
| `reject_accrue_1` | 거부 1회 누적(연예/정치 점수 감점) |
| `reject_accrue_2` | 거부 2회 누적 |
| `reject_accrue_3` | 거부 3회 누적(현재 데이터엔 approve_after_reject_3로 수렴) |

### 범죄자/성형범죄자 적발
| branch_key | 의미 |
|---|---|
| `reject_lucky` | 단순 거부(운 좋음, 포상금 X) |
| `detect_montage_reject` | 몽타주 인식 + 거부(포상금 O) |
| `detect_montage_xray_reject` | 몽타주 + X-ray + 거부(최선, 성형범죄자) |
| `approve_wrong_no_montage` | 몽타주 못 보고 입국(오판) |
| `corrupt_accept_gold` | 뇌물 응함 + 금괴 수령 + 입국(부패) |
| `corrupt_accept_drugs` | 뇌물 응함 + 마약/밀수품 + 입국(부패) |

### 테러범(상담/신고/제압 상태머신)
| branch_key | 의미 |
|---|---|
| `terror_persuade_confess` | 자수 유도/달램+설교(상담 마스터, 최고) |
| `terror_persuade_report_ok` | 달램 + 신고 성공 |
| `terror_persuade_then_bomb` | 달램+설교 → 시한폭탄(사고) |
| `terror_persuade_report_caught_bomb` | 달램+신고 들킴 → 시한폭탄 |
| `terror_ignore_then_persuade` | 여권→상담→설교(구사일생) |
| `terror_ignore_then_bomb` | 여권→시한폭탄(경청 실패) |
| `terror_ignore_subdue_ok` | 무관심+제압 성공(인간 방어막) |
| `terror_ignore_subdue_fail` | 무관심+제압 실패 |
| `terror_ignore_report_caught_bomb` | 무관심+신고 들킴 → 시한폭탄(최악) |
| `terror_xray_bomb_reject` | X-ray 폭탄 발견 + 거부(금액표 분기) |

### 사이비 신도(visit_round로 1/2/3회차 구분)
| branch_key | 의미 |
|---|---|
| `cult_yyy` | 예/예/예 입국 |
| `cult_partial_yes` | 예/예/아니오 또는 예/아니오/예 |
| `cult_all_no` | 아니오/아니오/아니오 |
| `cult_other_combo` | 기타 조합(점수 범위) |
| `cult_best_combo` | 2회차 최고 조합(강해야/예/예) → 호칭 '스며들기' |
| `cult_reject` | 거부 |
| `cult_yyy_brainwash` | 3회차 예/예/예 → '새뇌'(#13 발동) |
| `cult_nyy_follower` | 3회차 아니오/예/예 → '신종 추종자'(#14 발동) |

### 연예인/정치인 마스크·대리
| branch_key | 의미 |
|---|---|
| `mask_request_turn_1` | 마스크 벗기 요청 1턴 적립 |
| `mask_request_turn_2` | 마스크 벗기 요청 2턴 적립 |
| `scandal_third_turn` | 3번째 턴 → 인성 논란(다음 날 이벤트) |
| `proxy_self_request` | 대리(매니저/보좌관) → 본인 직접 요청 |

### 현자/꼬마
| branch_key | 의미 |
|---|---|
| `approve_sage_item` | 현자 입국 + 마법 아이템 1종 드롭(defect_variant=가치관조합) |
| `approve_with_step` | 꼬마 입국 + 발판 제공('맛있는 사탕') |
| `approve_no_step` | 꼬마 입국 + 발판 미제공(발판 아이템) |

## 트리거 일람 (점수/돈/호칭/아이템/조기엔딩)

### 호칭(title) — character_score.title
- 우수 사원: 성형 의심 고객 `approve_correct`(몽타주O+범인아님, +10)
- 청렴한 직원: 성형범죄자 `detect_montage_xray_reject`(+12), 범죄자(외국 도피자) `detect_montage_reject`(+7), 범죄자(국내 유입자) `detect_montage_reject`(+10)
- 전문 테러 방지반: 테러범 `terror_persuade_confess`(+15)
- 경찰아저씨 여기예요: 테러범 `terror_persuade_report_ok`(+10)
- 스며들기: 사이비 신도 2회차 `cult_best_combo`(+9)
- 새뇌: 사이비 신도 3회차 `cult_yyy_brainwash`(+10)
- 신종 추종자: 사이비 신도 3회차 `cult_nyy_follower`(+7)
- 해탈한 자: 현자 누적 8회(점수 0, 누적 카운터 필요 — 게임플레이 구현)

### 아이템 드롭(item_drop) — character_payout.item_drop
- 현자: `마법 아이템`(8종 중 1, defect_variant로 1:1 결정 — 점수표 note의 마법의 수정구슬/구슬/손목시계/거울/손수건/주머니/와인/지갑)
- 꼬마: `맛있는 사탕`(발판 제공) / `발판`(미제공)
- 범죄자/성형범죄자 부패 분기: `금괴`/`마약`(뇌물 수령물, 돈 +)

### 조기엔딩(early_ending) — character_payout.early_ending / character_score.event_id
- `#11`: 범죄자(외국 도피자/국내 유입자) `corrupt_accept_gold`/`corrupt_accept_drugs` 일부 → 즉시 엔딩
- `#12`: 성형범죄자 `corrupt_accept_drugs` → 즉시 엔딩
- `#13`: 사이비 신도 3회차 `cult_yyy_brainwash`(포교 성공)
- `#14`: 사이비 신도 3회차 `cult_nyy_follower`(난 돈을 믿어)
- `#15`: 검역 대상자 `approve_wrong`(백신X/모두미비) — 방역 실패 누적 카운트(엔딩 트리거)
- `#16`: 외국인 관광객/장기체류자 `approve_wrong`(출국X) — 누적 카운트

> #11~#16은 기존 `reward`/`ending` 테이블의 trigger_condition과 연결될 후보다.
> 현재 매핑은 데이터에 텍스트로만 존재 → 게임플레이가 event_id ↔ ending_id 연결 테이블을 확정해야 함(결정 필요).
