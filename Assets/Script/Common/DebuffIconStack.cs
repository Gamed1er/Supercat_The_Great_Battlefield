using System.Collections.Generic;
using UnityEngine;

// 妨害效果圖示的疊層邏輯,PlayerDebuffState/EnemyDebuffState 各持有一份(組合,不走繼承,比照 KnockbackState 的分工方式)。
// 規則:同時間最多顯示一張圖示,永遠顯示「最後套用、且仍生效中」的那個;該效果結束後自動還原顯示次新的仍生效效果,
// 全部效果都結束才真正收掉圖示。圖示 prefab 在第一個效果套用時才生成(掛在角色物件下、指定的本地座標偏移),
// 全部效果結束時銷毀,而非只是隱藏——比照 hitEffectPrefab 之類「觸發時才 Instantiate」的既有風格。
public class DebuffIconStack {
    readonly GameObject ownerObject;
    readonly GameObject iconPrefab;
    readonly Vector3 localOffset;

    readonly List<DebuffType> activeOrder = new List<DebuffType>(); // 依套用先後排序,最後一個是目前顯示中的
    GameObject iconInstance;
    DebuffIconDisplay display;

    public DebuffIconStack(GameObject ownerObject, GameObject iconPrefab, Vector3 localOffset) {
        this.ownerObject = ownerObject;
        this.iconPrefab = iconPrefab;
        this.localOffset = localOffset;
    }

    // 效果從「無」變「有」時呼叫一次(呼叫端自行判斷,疊加只延長時間的情況不要重複呼叫)
    public void NotifyApplied(DebuffType type) {
        activeOrder.Remove(type);
        activeOrder.Add(type);

        if (iconInstance == null && iconPrefab != null) {
            iconInstance = Object.Instantiate(iconPrefab, ownerObject.transform);
            iconInstance.transform.localPosition = localOffset;
            iconInstance.TryGetComponent(out display);
        }

        if (display != null) display.Show(type);
    }

    // 效果結束時呼叫:從疊層移除,還原顯示次新的仍生效效果,全部結束就收掉圖示
    public void NotifyExpired(DebuffType type) {
        activeOrder.Remove(type);

        if (activeOrder.Count == 0) {
            if (iconInstance != null) Object.Destroy(iconInstance);
            iconInstance = null;
            display = null;
        } else if (display != null) {
            display.Show(activeOrder[activeOrder.Count - 1]);
        }
    }
}
