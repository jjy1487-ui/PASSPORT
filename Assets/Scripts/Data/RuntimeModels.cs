using System.Collections.Generic;

// ─────────────────────────────────────────────────────────────
//  RuntimeModels — 변조 결과 런타임 모델 (규약 3.7)
//  data-tools는 "형(shape)"만 정의하고, 실제 생성/변조는 gameplay가 한다.
//  서류 개별 필드는 고정 멤버로 박지 않고 키-값 딕셔너리로 다룬다(6장 변경 대응).
// ─────────────────────────────────────────────────────────────

/// <summary>화면에 보일 서류 1장 (진실 복제 + 결함 0~1개).</summary>
public class DocumentInstance
{
    /// <summary>문자열 타입("여권"/"비자"/"pcr_test"...). enum으로 박지 않음.</summary>
    public string documentType;

    /// <summary>컬럼명(키) -> 값. 진실값 복제 후 결함 시 한 키만 교체.</summary>
    public Dictionary<string, string> fields = new Dictionary<string, string>();

    /// <summary>표시명(한글)/자료형 등. 임포트 시 data-tools가 테이블 columns에서 채워준 것을 복사.</summary>
    public Dictionary<string, FieldMeta> meta = new Dictionary<string, FieldMeta>();

    public bool hasDefect;
    public string defectField;      // 변조된 키
    public string corruptionType;   // EXPIRE / ALTER_FIELD / MISMATCH_PHOTO ...

    /// <summary>키의 한글 표시명. meta 우선, 없으면 키 그대로.</summary>
    public string Label(string key)
        => meta != null && meta.TryGetValue(key, out var m) && m != null && !string.IsNullOrEmpty(m.label)
            ? m.label : key;

    public string Get(string key)
        => fields != null && fields.TryGetValue(key, out var v) ? v : null;
}

/// <summary>손님 1명 심사 케이스 (gameplay가 생성·갱신).</summary>
public class InspectionCase
{
    public int customerId;
    public int scheduleId;
    public bool isNormal;                 // 슬롯 valid_chance 1회 판정 결과
    public List<DocumentInstance> documents = new List<DocumentInstance>();
    public Verdict correctVerdict;        // isNormal ? Approve : Reject (보조검사 적발은 Reject)

    public int rejectCount;               // 재심사 라운드 0~3 (4-A 루프)
    public string currentCaseType;        // 대사 선택 키 (입장/일반 심사 ...)
    public string gameResult;             // 정상 승인 / 잘못 거절 / ...

    // 연결된 보조 데이터(있을 때만)
    public DataRow dialogueCaseRow;
    public DataRow xrayRow;
    public DataRow fingerprintRow;
}
