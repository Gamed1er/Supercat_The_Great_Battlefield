using UnityEngine;

// 貓咪超人的普攻:朝滑鼠游標的世界座標發射子彈,每 1 秒一發
public class AutoLockShootSkill : ISkill {
    readonly PlayerBase owner;
    readonly GameObject bulletPrefab;
    readonly float damageMultiplier;
    readonly Camera mainCamera;

    public float Cooldown => 1f;
    public float CooldownRemaining => Mathf.Max(0f, Cooldown - (Time.time - lastFireTime));
    public bool IsActive => false; // 瞬發技能,沒有進行中的位移狀態

    float lastFireTime = -Mathf.Infinity;

    public AutoLockShootSkill(PlayerBase owner, GameObject bulletPrefab, float damageMultiplier) {
        this.owner = owner;
        this.bulletPrefab = bulletPrefab;
        this.damageMultiplier = damageMultiplier;
        mainCamera = Camera.main;
    }

    public bool TryExecute() {
        if (Time.time - lastFireTime < Cooldown) return false;

        lastFireTime = Time.time;

        Vector3 targetPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        targetPos.z = owner.transform.position.z;

        Bullet.Spawn(bulletPrefab, owner, owner.transform.position, targetPos, owner.stats.baseAttack * damageMultiplier);
        AudioManager.Instance.PlaySFX("shoot");
        return true;
    }

    public void Tick() { }
    public void Interrupt() { }
    public void OnHitEnemy(Collider2D enemyCollider) { }
}
