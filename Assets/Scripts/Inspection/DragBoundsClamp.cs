using UnityEngine;

/// <summary>
/// 드래그 허용 영역(월드 사각형)을 주입받는 카드 드래그 컴포넌트가 구현하는 인터페이스.
/// DocumentView 가 카드 종류(여권/일반)와 무관하게 같은 한 줄로 영역을 넣어줄 수 있게 한다.
/// 상태(닫힘/펼침)에 따라 머무는 영역이 달라지므로 두 영역(스폰/책상)을 함께 받는다.
/// </summary>
public interface IDragBoundsReceiver
{
    /// <summary>
    /// 상태별 드래그 허용 영역(월드 좌표 Rect)을 지정한다.
    /// - spawnZone: 접힘(ClosedView) 상태가 머무는 영역(좌측 스폰 띠).
    /// - documentZone: 펼침(OpenView) 상태가 머무는 영역(우측 책상).
    /// </summary>
    void ConfigureDragBounds(Rect spawnZone, Rect documentZone);
}

/// <summary>
/// 드래그 중 카드가 상태에 맞는 영역 안에 머물도록 되미는 공통 클램프.
/// DraggableDocument·PassportDocument 가 함께 쓴다.
///
/// 상태별 규칙(클릭 깨짐 방지의 핵심):
/// - 카드가 '닫힘'(ClosedView 활성)이면 → 작은 ClosedView 만 스폰 영역(SpawnZone)에 가둔다.
/// - 카드가 '펼침'(OpenView 활성)이면 → 큰 카드 루트를 책상 영역(DocumentZone)에 가둔다.
/// 절대 큰 펼친 카드를 작은 스폰 영역에 욱여넣지 않는다(레이캐스트가 다른 UI 위로 밀려 클릭이 깨졌던 과거 버그 방지).
///
/// 간극 데드락 방지:
/// - SpawnZone.xMax 와 DocumentZone.xMin 사이의 ~7px 빈틈에 닫힌 카드가 갇히지 않도록,
///   닫힘 영역의 오른쪽 경계를 DocumentZone.xMin 까지 늘려 두 영역을 맞붙인다(BridgeClosedZone).
///   덕분에 닫힌 카드를 책상 경계까지 끌고 가 그 위에서 놓으면(드롭 지점은 클램프 대상이 아님) 펼쳐진다.
/// </summary>
public static class DragBoundsClamp
{
    /// <summary>
    /// 카드의 '보이는 사각형'(닫힘=ClosedView, 펼침=루트)이 상태에 맞는 영역 안에 머물도록 되민다.
    /// - 닫힘이면 spawnZone(오른쪽을 documentZone.xMin 까지 확장해 간극 제거)에,
    /// - 펼침이면 documentZone(큰 책상)에 가둔다.
    /// </summary>
    public static void ClampForState(RectTransform card, bool hasBounds, Rect spawnZone, Rect documentZone)
    {
        if (!hasBounds || card == null) return;

        RectTransform visible;
        Rect bounds;
        if (IsClosed(card, out visible))
        {
            // 닫힘: 작은 표지만 스폰 영역에 가둔다(오른쪽은 책상 시작점까지 확장 → 간극 데드락 없음).
            bounds = BridgeClosedZone(spawnZone, documentZone);
        }
        else
        {
            // 펼침: 큰 카드 루트를 책상에 가둔다(책상은 펼친 카드보다 커서 욱여넣기 없음).
            visible = card;
            bounds = documentZone;
        }

        ClampRectInside(card, visible, bounds);
    }

    /// <summary>visible 사각형이 worldBounds 안에 머물도록 card 위치를 축별로 되민다(영역이 좁은 축은 건너뜀).</summary>
    private static void ClampRectInside(RectTransform card, RectTransform visible, Rect worldBounds)
    {
        if (card == null || visible == null) return;
        if (worldBounds.width <= 0f || worldBounds.height <= 0f) return;

        Vector3[] vc = new Vector3[4];
        visible.GetWorldCorners(vc); // 0=좌하 1=좌상 2=우상 3=우하
        float minX = vc[0].x, maxX = vc[2].x;
        float minY = vc[0].y, maxY = vc[1].y;

        Vector3 shift = Vector3.zero;

        // X축: 영역이 보이는 폭보다 넓을 때만 클램프(좁으면 욱여넣기 금지 → 클릭 영역 보존).
        if (worldBounds.width >= (maxX - minX))
        {
            if (minX < worldBounds.xMin) shift.x += worldBounds.xMin - minX; // 왼쪽으로 새면 오른쪽으로
            else if (maxX > worldBounds.xMax) shift.x += worldBounds.xMax - maxX; // 오른쪽으로 새면 왼쪽으로
        }
        // Y축: 위쪽 공항으로 못 올라가게(영역 안에서만).
        if (worldBounds.height >= (maxY - minY))
        {
            if (minY < worldBounds.yMin) shift.y += worldBounds.yMin - minY; // 아래로 새면 위로
            else if (maxY > worldBounds.yMax) shift.y += worldBounds.yMax - maxY; // 위로 새면 아래로
        }

        if (shift != Vector3.zero) card.position += shift;
    }

    /// <summary>
    /// 닫힘 영역 = 스폰 영역이되, 오른쪽 경계를 책상(documentZone)의 시작점까지 늘려
    /// 두 영역 사이의 빈틈(~7px)을 메운다. 이러면 닫힌 카드가 간극에 갇히지 않고
    /// 책상 경계까지 미끄러져 그 위에서 드롭→펼침이 가능하다.
    /// documentZone 이 비었거나 스폰보다 왼쪽이면 스폰 영역을 그대로 쓴다.
    /// </summary>
    private static Rect BridgeClosedZone(Rect spawnZone, Rect documentZone)
    {
        if (spawnZone.width <= 0f || spawnZone.height <= 0f) return spawnZone;
        float right = spawnZone.xMax;
        if (documentZone.width > 0f && documentZone.xMin > right) right = documentZone.xMin; // 간극까지 확장
        return Rect.MinMaxRect(spawnZone.xMin, spawnZone.yMin, right, spawnZone.yMax);
    }

    /// <summary>카드가 닫힘 상태인가(ClosedView 활성). 닫힘이면 보이는 표지를 out 으로 돌려준다.</summary>
    private static bool IsClosed(RectTransform card, out RectTransform closedView)
    {
        closedView = null;
        Transform closed = card.Find("ClosedView");
        if (closed != null && closed.gameObject.activeInHierarchy && closed is RectTransform crt)
        {
            closedView = crt;
            return true;
        }
        return false;
    }
}
