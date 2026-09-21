using UnityEngine;

// 掛在 BC 紙娃娃匯入產生的每個零件節點上。原始格式的「透明度」是由上往下累乘的（父節點淡出，
// 底下所有子零件要跟著淡出），但 Unity 的 SpriteRenderer 彼此獨立、不會自動繼承父物件的顏色，
// 所以透明度動畫改成驅動這裡的 Alpha 欄位，再由本元件即時往上乘過整條 parent chain 算出最終顯示的透明度。
// [ExecuteAlways]：不只 Play 模式，連 Editor 裡用 Animation 視窗預覽/拖動時間軸也要能即時算出正確的
// 疊乘透明度，否則沒進 Play 模式看到的都會是「沒套用任何動畫」的原始姿勢（兩套骨架都是 100% 不透明）。
[ExecuteAlways]
public class BcRigNode : MonoBehaviour {
    public float Alpha = 1f;
    public SpriteRenderer SpriteRenderer;

    public float EffectiveAlpha {
        get {
            var parentNode = transform.parent != null ? transform.parent.GetComponentInParent<BcRigNode>() : null;
            return Alpha * (parentNode != null ? parentNode.EffectiveAlpha : 1f);
        }
    }

    void LateUpdate() {
        if (SpriteRenderer == null) return;
        var c = SpriteRenderer.color;
        c.a = EffectiveAlpha;
        SpriteRenderer.color = c;
    }
}
