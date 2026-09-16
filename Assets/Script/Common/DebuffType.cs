// 妨害效果種類,PlayerDebuffState/EnemyDebuffState 共用同一組列舉,方便圖示 prefab 共用同一份對照表(DebuffIconDisplay)。
// 各角色不一定用得到全部種類(例如 Curse 只有玩家會中,TargetLock 只有敵人會中),用不到的直接不呼叫對應的 Apply 就好。
public enum DebuffType {
    Stun,       // 眩暈
    Slow,       // 麻痺
    Knockback,  // 擊退
    Curse,      // 詛咒(僅玩家)
    TargetLock  // 失焦(僅敵人)
}
