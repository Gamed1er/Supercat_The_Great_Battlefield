using UnityEngine;
using UnityEngine.UI;

// 選關地圖上兩個節點之間的折線(水平 → 垂直 → 水平,兩端同高時就是一條直線)。
// 節點用比例錨點擺放,實際像素位置跟著解析度變,所以每幀依兩端節點目前的位置重排三段線,不在生成時寫死。
// 本物件和兩端節點必須在同一個父物件底下,且本物件撐滿父物件、pivot 置中,三段線的 anchoredPosition 才跟節點同一個座標系
public class MapLinkUI : MonoBehaviour {
    [SerializeField] LevelNodeUI fromNode; // 前置關卡
    [SerializeField] LevelNodeUI toNode;
    [SerializeField] Image[] segments = new Image[3];
    [SerializeField] float thickness = 8f;
    [SerializeField] Color unlockedColor = new Color(0.9f, 0.9f, 0.9f);
    [SerializeField] Color lockedColor = new Color(0.35f, 0.35f, 0.38f);

    public void Refresh() {
        foreach (Image segment in segments) segment.color = toNode.IsUnlocked ? unlockedColor : lockedColor;
    }

    void LateUpdate() {
        Vector2 from = fromNode.transform.localPosition;
        Vector2 to = toNode.transform.localPosition;
        float midX = (from.x + to.x) * 0.5f;

        PlaceHorizontal(segments[0].rectTransform, from.x, midX, from.y);
        PlaceVertical(segments[1].rectTransform, midX, from.y, to.y);
        PlaceHorizontal(segments[2].rectTransform, midX, to.x, to.y);
    }

    void PlaceHorizontal(RectTransform segment, float x1, float x2, float y) {
        segment.anchoredPosition = new Vector2((x1 + x2) * 0.5f, y);
        segment.sizeDelta = new Vector2(Mathf.Abs(x2 - x1) + thickness, thickness);
    }

    void PlaceVertical(RectTransform segment, float x, float y1, float y2) {
        segment.anchoredPosition = new Vector2(x, (y1 + y2) * 0.5f);
        segment.sizeDelta = new Vector2(thickness, Mathf.Abs(y2 - y1) + thickness);
    }
}
