using UnityEngine;

// 掛在妨害效果圖示 prefab 的根物件上:prefab 內建每種效果各自一組子物件(可以是靜態圖或動畫),
// 這裡只負責依目前要顯示的效果 SetActive 對應的子物件、關掉其他的。用不到的效果種類(例如玩家用的
// prefab 沒有 TargetLock)留空即可,Show() 呼叫到未指定的種類時直接跳過。
public class DebuffIconDisplay : MonoBehaviour {
    [SerializeField] GameObject stunIcon;
    [SerializeField] GameObject slowIcon;
    [SerializeField] GameObject knockbackIcon;
    [SerializeField] GameObject curseIcon;
    [SerializeField] GameObject targetLockIcon;

    public void Show(DebuffType type) {
        SetActive(stunIcon, type == DebuffType.Stun);
        SetActive(slowIcon, type == DebuffType.Slow);
        SetActive(knockbackIcon, type == DebuffType.Knockback);
        SetActive(curseIcon, type == DebuffType.Curse);
        SetActive(targetLockIcon, type == DebuffType.TargetLock);
    }

    static void SetActive(GameObject go, bool active) {
        if (go != null) go.SetActive(active);
    }
}
