using System;
using UnityEngine;

// 赤井抄太郎的終結技:右鍵觸發,打斷普攻和戰技,角色順時針旋轉一圈(0.4 秒轉完),
// 子彈不是一次性生成,而是旋轉角度掃過該子彈預定方向時才發射,總共 20 顆(等同普攻規格,100% 傷害),純冷卻制,不消耗充能。
// 子彈沿發射角度直線飛行、可穿透敵人(見 Bullet 的 pierce)。
// 不佔用移動鎖(見 IsActive 恆為 false),旋轉期間的忙碌狀態改由 IsSpinning 對外查詢。
public class RadialBurstSkill : ISkill {
    const int bulletCount = 10;
    const float spinDuration = 0.2f; // 觸發後角色順時針轉一圈所需時間,子彈依序在轉到對應角度時發射
    const float bulletSpeedMultiplier = 1f; // 相對普攻子彈速度的倍數
    const float bulletLifeTime = 6f;

    readonly PlayerBase owner;
    readonly GameObject bulletPrefab;
    readonly float damageMultiplier;
    readonly float cooldown;
    readonly Action onProjectileHit;
    readonly ISkill barrageSkillToInterrupt; // 觸發當下打斷戰技(普攻由 AutoLockShootSkill 的 isSuppressed 自行判斷 IsSpinning/戰技是否開火中)
    readonly Action onTrigger;

    public float Cooldown => cooldown * CooldownMultiplier;
    public float CooldownMultiplier { get; set; } = 1f;
    // 上限夾在 Cooldown:lastTriggerTime 初始值是 -Infinity(代表一開始就緒),若不夾住,Time.time - (-Infinity) 恆為 +Infinity,
    // UIManager 對這個值做 Mathf.FloorToInt 轉型會溢位成 int.MinValue(顯示異常的一長串負數)
    public float CooldownCurrent => Mathf.Min(Cooldown, Mathf.Max(0f, Time.time - lastTriggerTime));
    public bool IsActive => false; // 旋轉期間不鎖 WASD 移動(見設計決議),忙碌狀態改由 IsSpinning 對外查詢
    public bool IsSpinning { get; private set; }

    float lastTriggerTime = -Mathf.Infinity;
    bool wasReady;

    float spinElapsed;
    float startRotationZ;
    float damage;
    int nextBulletIndex;

    public RadialBurstSkill(PlayerBase owner, GameObject bulletPrefab, float damageMultiplier, float cooldown, ISkill barrageSkillToInterrupt, Action onProjectileHit, Action onTrigger = null) {
        this.owner = owner;
        this.bulletPrefab = bulletPrefab;
        this.damageMultiplier = damageMultiplier;
        this.cooldown = cooldown;
        this.barrageSkillToInterrupt = barrageSkillToInterrupt;
        this.onProjectileHit = onProjectileHit;
        this.onTrigger = onTrigger;
    }

    public bool TryExecute() {
        if (!Input.GetMouseButtonDown(1)) return false;
        if (Time.time - lastTriggerTime < Cooldown) {
            AudioManager.Instance.PlaySFX("no");
            return false;
        }

        lastTriggerTime = Time.time;
        barrageSkillToInterrupt.Interrupt();
        onTrigger?.Invoke(); // 重置普攻冷卻,避免旋轉收尾時普攻剛好同時開火(見 RedSuperCat 的 onTrigger 接線)

        startRotationZ = owner.transform.eulerAngles.z;
        damage = owner.stats.baseAttack * damageMultiplier;
        spinElapsed = 0f;
        IsSpinning = true;

        nextBulletIndex = 0;
        FireBullet(startRotationZ); // 觸發當下先朝目前方向射出第一顆,其餘的隨旋轉依序發射
        nextBulletIndex++;

        AudioManager.Instance.PlaySFX("crit_hit2");
        return true;
    }

    public void Tick() {
        bool isReady = CooldownCurrent >= Cooldown;
        if (isReady && !wasReady) AudioManager.Instance.PlaySFX("skill_done");
        wasReady = isReady;

        if (!IsSpinning) return;

        spinElapsed += Time.fixedDeltaTime;
        float progress = Mathf.Clamp01(spinElapsed / spinDuration);
        float sweptDegrees = progress * 360f;
        owner.transform.rotation = Quaternion.Euler(0f, 0f, startRotationZ - sweptDegrees); // 順時針:Z 角度隨時間遞減

        while (nextBulletIndex < bulletCount && nextBulletIndex * (360f / bulletCount) <= sweptDegrees) {
            FireBullet(startRotationZ - nextBulletIndex * (360f / bulletCount));
            nextBulletIndex++;
        }

        if (progress >= 1f) {
            owner.transform.rotation = Quaternion.Euler(0f, 0f, startRotationZ); // 轉滿一圈,歸位避免浮點數殘留
            IsSpinning = false;
        }
    }

    void FireBullet(float angleZ) {
        Vector3 origin = owner.transform.position;
        float angleRad = angleZ * Mathf.Deg2Rad;
        Vector3 direction = new Vector3(Mathf.Cos(angleRad), Mathf.Sin(angleRad), 0f);

        Bullet.Spawn(bulletPrefab, owner, origin, origin + direction, damage, targetTag: "Enemy", BulletType.Destructive, onProjectileHit,
            speed: Bullet.defaultSpeed * bulletSpeedMultiplier, lifeTime: bulletLifeTime, pierce: true);
    }

    public void Interrupt() {
        if (!IsSpinning) return;
        IsSpinning = false;
        owner.transform.rotation = Quaternion.Euler(0f, 0f, startRotationZ); // 被打斷時歸位,剩餘尚未發射的子彈直接取消
    }

    public void OnHitEnemy(Collider2D enemyCollider) { }
    public void ReduceCooldown(float seconds) { lastTriggerTime -= seconds; }
}
