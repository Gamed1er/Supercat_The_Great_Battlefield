using UnityEngine;

// PhotoCat 與 JurassicCat 的共用基底(1-2 關卡):這兩隻共用同一包血量池,且各自有「被暈眩時免傷失效」的規則,
// 兩者的差異(66% 固定免傷 vs 前後方向判定)由子類別實作 GetIncomingDamageMultiplier。
// 血量池/夥伴參照用場景搜尋惰性解析(見 EnsureLinked),不依賴 Awake/Start 執行順序,也不用靜態欄位(避免場景重載時殘留)。
public abstract class PairedBossEnemy : EnemyBase {
    SharedEnemyHealthPool healthPool;
    PairedBossEnemy partner;
    bool linked;

    protected SharedEnemyHealthPool HealthPool {
        get { EnsureLinked(); return healthPool; }
    }

    // 隊友參照(泰山隕石墜命中判定、清冷卻等隊伍互動用);目前這組設計固定剛好一隻,找不到就是 null。
    protected PairedBossEnemy Partner {
        get { EnsureLinked(); return partner; }
    }

    // 惰性解析,找場景裡「唯一的另一隻」當夥伴;血量池由 GetInstanceID 較小的那隻建立,兩邊都讀同一個參照。
    void EnsureLinked() {
        if (linked) return;
        linked = true;

        PairedBossEnemy canonical = this;
        foreach (PairedBossEnemy candidate in FindObjectsOfType<PairedBossEnemy>()) {
            if (candidate == this) continue;
            partner = candidate; // 目前只會有一隻夥伴,找到就是它
            if (candidate.GetInstanceID() < canonical.GetInstanceID()) canonical = candidate;
        }

        healthPool = canonical == this ? new SharedEnemyHealthPool(health) : canonical.HealthPool;
    }

    // 被暈眩時免傷失效,固定回傳 1(全額傷害);否則交給子類別算(66% 固定 / PhotoCat 前後方向)。
    protected abstract float GetIncomingDamageMultiplier();

    // 隊友攻擊命中玩家時的通知鉤子(例如 PhotoCat 打中玩家會清空 JurassicCat 的槌擊冷卻),預設不做事,子類別視需要覆寫。
    public virtual void OnPartnerHitPlayer() { }

    public override void TakeDamage(float amount, bool fromEnemyAttack = true) {
        if (IsDead || amount <= 0f) return;

        float multiplier = IsStunned ? 1f : GetIncomingDamageMultiplier();
        float actualDamage = amount * multiplier;
        if (actualDamage <= 0f) return; // 全免傷:不觸發受傷音效/特效,比照 PlayerBase.IsDamageImmune 的處理

        AudioManager.Instance.PlayRandomHurtSfx();
        if (hitEffectPrefab != null) Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);

        HealthPool.ApplyDamage(actualDamage);
        health = HealthPool.Current;
        if (Partner != null) Partner.health = HealthPool.Current;

        if (HealthPool.IsDepleted) {
            Die();
            if (Partner != null) Partner.ForceDie();
        }
    }
}
