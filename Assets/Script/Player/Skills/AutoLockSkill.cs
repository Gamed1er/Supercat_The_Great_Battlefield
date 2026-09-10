using System;
using UnityEngine;

// 貓咪超人的普攻:朝滑鼠游標的世界座標發射子彈,每 1 秒一發
// bulletType/onHit/isSuppressed 皆為選用參數(預設維持貓咪超人原本的行為),供其他角色(例如赤井抄太郎)客製子彈種類、命中回呼、暫停開火條件
public class AutoLockShootSkill : ISkill {
    readonly PlayerBase owner;
    readonly GameObject bulletPrefab;
    readonly float damageMultiplier;
    readonly BulletType bulletType;
    readonly Action onHit;
    readonly Func<bool> isSuppressed;
    readonly Camera mainCamera;

    public float CooldownCurrent => Mathf.Max(0f, Cooldown - (Time.time - lastFireTime));
    public float Cooldown => 1f;
    public bool IsActive => false; // 瞬發技能,沒有進行中的位移狀態

    float lastFireTime = -Mathf.Infinity;

    public AutoLockShootSkill(PlayerBase owner, GameObject bulletPrefab, float damageMultiplier, BulletType bulletType = BulletType.Normal, Action onHit = null, Func<bool> isSuppressed = null) {
        this.owner = owner;
        this.bulletPrefab = bulletPrefab;
        this.damageMultiplier = damageMultiplier;
        this.bulletType = bulletType;
        this.onHit = onHit;
        this.isSuppressed = isSuppressed;
        mainCamera = Camera.main;
    }

    public bool TryExecute() {
        if (isSuppressed != null && isSuppressed()) return false; // 被其他技能(例如正在開火/蓄力)打斷,靜靜跳過這次自動開火
        if (Time.time - lastFireTime < Cooldown) return false;

        lastFireTime = Time.time;

        Vector3 targetPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        targetPos.z = owner.transform.position.z;

        Bullet.Spawn(bulletPrefab, owner, owner.transform.position, targetPos, owner.stats.baseAttack * damageMultiplier, targetTag: "Enemy", bulletType, onHit);
        AudioManager.Instance.PlaySFX("shoot");
        return true;
    }

    public void Tick() { }
    public void Interrupt() { }
    public void OnHitEnemy(Collider2D enemyCollider) { }
    public void ReduceCooldown(float seconds) { lastFireTime -= seconds; }
}
