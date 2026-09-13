using System;
using UnityEngine;

// 赤井抄太郎的戰技:左鍵朝滑鼠方向鎖定一次,接著沿同一方向連續發射 5 顆子彈(每顆間隔 0.2 秒),子彈規格等同普攻(破壞性、100% 傷害)。
// 不佔用移動鎖(見 IsActive 恆為 false),只用 IsFiring 讓其他技能查詢/打斷;isSuppressed 用來在大招蓄力期間擋住觸發。
public class BarrageSkill : ISkill {
    const int bulletCount = 5;
    // 每個子彈之間的角度間距（度）
    const float angleBetweenDegrees = 10f;

    readonly PlayerBase owner;
    readonly GameObject bulletPrefab;
    readonly float damageMultiplier;
    readonly float cooldown;
    readonly Action onProjectileHit;
    readonly Func<bool> isSuppressed;
    readonly Camera mainCamera;

    public float Cooldown => cooldown;
    // 上限夾在 cooldown,理由同 RadialBurstSkill:避免 lastTriggerTime 還沒被觸發過(-Infinity)時,CooldownCurrent 變成 +Infinity
    public float CooldownCurrent => Mathf.Min(cooldown, Mathf.Max(0f, Time.time - lastTriggerTime));
    public bool IsActive => false; // 開火期間不鎖 WASD 移動(見設計決議),忙碌狀態改由 IsFiring 對外查詢
    public bool IsFiring { get; private set; }
    float lastTriggerTime = -Mathf.Infinity;
    bool wasReady;
    Vector2 fireDirection;

    public BarrageSkill(PlayerBase owner, GameObject bulletPrefab, float damageMultiplier, float cooldown, Action onProjectileHit, Func<bool> isSuppressed = null) {
        this.owner = owner;
        this.bulletPrefab = bulletPrefab;
        this.damageMultiplier = damageMultiplier;
        this.cooldown = cooldown;
        this.onProjectileHit = onProjectileHit;
        this.isSuppressed = isSuppressed;
        mainCamera = Camera.main;
    }

    public bool TryExecute() {
        if (!Input.GetMouseButtonDown(0)) return false;
        if (isSuppressed != null && isSuppressed()) return false; // 大招蓄力中,不能放戰技

        if (Time.time - lastTriggerTime < cooldown) {
            AudioManager.Instance.PlaySFX("no");
            return false;
        }

        Vector2 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 direction = mouseWorldPos - (Vector2)owner.transform.position;
        if (direction == Vector2.zero) return false;

        lastTriggerTime = Time.time;
        fireDirection = direction.normalized;
        // 立即一次性發射霰彈（多個子彈，角度固定間距）
        FireVolley();
        return true;
    }

    void FireVolley() {
        Vector3 origin = owner.transform.position;
        // 中心角度為朝向游標的方向，子彈依 angleBetweenDegrees 等距分布
        float halfSpan = (bulletCount - 1) * 0.5f * angleBetweenDegrees;
        for (int i = 0; i < bulletCount; i++) {
            float angle = -halfSpan + i * angleBetweenDegrees; // 左負右正
            Vector3 rotatedDir = Quaternion.Euler(0f, 0f, angle) * (Vector3)fireDirection;
            Vector3 targetPos = origin + rotatedDir;
            Bullet.Spawn(bulletPrefab, owner, origin, targetPos, owner.stats.baseAttack * damageMultiplier, targetTag: "Enemy", BulletType.Destructive, onProjectileHit);
        }
        AudioManager.Instance.PlaySFX("shoot");
    }

    public void Tick() {
        bool isReady = CooldownCurrent >= Cooldown;
        if (isReady && !wasReady) AudioManager.Instance.PlaySFX("skill_done");
        wasReady = isReady;
        // 霰彈為一次性發射,不需 Tick 中的連續發射邏輯
    }

    public void Interrupt() {
        IsFiring = false; // 被大招/撞牆/擊退打斷時,停止後續尚未發射的子彈
    }

    public void OnHitEnemy(Collider2D enemyCollider) { }
    public void ReduceCooldown(float seconds) { lastTriggerTime -= seconds; }
}
