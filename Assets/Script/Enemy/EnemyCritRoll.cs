using UnityEngine;

// 敵人對玩家造成傷害時的會心一擊判定,目前只有 PhotoCat/JurassicCat 使用,共用同一份機率表與規則,
// 每次命中各自獨立 roll。爆擊造成 2 倍傷害;若目標角色可被爆擊秒殺(見 PlayerBase.IsInstaKillableByCrit,
// 目前沒有角色符合,是預留的擴充點),直接給予秒殺傷害量,不理會原本的傷害數字。
public static class EnemyCritRoll {
    // 索引 = 難度(0~5),對應 (2 / 6 / 12 / 20 / 33 / 100)%
    static readonly float[] chanceByDifficulty = { 0.02f, 0.06f, 0.12f, 0.20f, 0.33f, 1f };

    const float CritMultiplier = 2f;

    public static float ChanceAt(int difficulty) => chanceByDifficulty[Mathf.Clamp(difficulty, 0, chanceByDifficulty.Length - 1)];
    const float InstaKillDamage = 999999f; // 秒殺用的超大傷害量,實際扣血由 PlayerBase.TakeDamage 自己的免傷/血量夾範圍處理

    // target 是這次攻擊命中的玩家角色(這兩隻敵人的攻擊本來就只打玩家,不透過 IDamageable 介面查詢)。
    // 只負責算傷害數字跟是否爆擊,不在這裡播音效——子彈類攻擊(例如拍照)發射當下還不知道會不會真的命中,
    // 呼叫端要等實際命中玩家的那一刻,isCrit 為 true 才呼叫下面的 PlayCritHitSfx(),見 PhotoCat.PhotoRoutine 的用法。
    public static float ComputeDamage(float baseDamage, int difficulty, PlayerBase target, out bool isCrit) {
        isCrit = Random.value < ChanceAt(difficulty);
        if (!isCrit) return baseDamage;

        if (target != null && target.IsInstaKillableByCrit) return InstaKillDamage;
        return baseDamage * CritMultiplier;
    }

    // 爆擊命中玩家的當下呼叫,播放爆擊音效
    public static void PlayCritHitSfx() => AudioManager.Instance.PlaySFX("crit_hit1");
}
