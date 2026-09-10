using UnityEngine;

// 終結技衝撞:右鍵點擊時朝滑鼠鼠標方向衝撞。
// 過程中完全免疫傷害(由 PlayerBase.TakeDamage 檢查 IsActive 處理);碰到敵人停止位移,造成一段傷害+高額擊退;碰到牆壁只停止。
public class ChargeRamSkill : ISkill {
    const float ramSpeed = 60f;
    const float ramDuration = 0.4f;
    const float chargeCost = 7f;
    const float knockbackDistance = 4f; // 高額擊退,方位固定用衝刺方向

    readonly PlayerBase owner;
    readonly Rigidbody2D rb;
    readonly Camera mainCamera;
    readonly float damageMultiplier;

    public float CooldownCurrent => 0f; // 用充能消耗來限制施放頻率,沒有額外冷卻
    public float Cooldown => 0f;
    public bool IsActive { get; private set; }

    float ramElapsed;
    Vector2 ramDirection;

    public ChargeRamSkill(PlayerBase owner, float damageMultiplier) {
        this.owner = owner;
        this.damageMultiplier = damageMultiplier;
        rb = owner.GetComponent<Rigidbody2D>();
        mainCamera = Camera.main;
    }

    public bool TryExecute() {
        if (!Input.GetMouseButtonDown(1)) return false;
        if (owner.stats.Charge < chargeCost) {
            AudioManager.Instance.PlaySFX("no"); // 充能不夠,放技能失敗
            return false;
        }

        Vector2 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 direction = mouseWorldPos - rb.position;
        if (direction == Vector2.zero) return false;
        direction.Normalize();

        owner.stats.SpendCharge(chargeCost);
        IsActive = true;
        ramElapsed = 0f;
        ramDirection = direction;
        AudioManager.Instance.PlaySFX("teleport");
        return true;
    }

    public void Tick() {
        if (!IsActive) return;

        ramElapsed += Time.fixedDeltaTime;
        rb.MovePosition(rb.position + ramDirection * ramSpeed * Time.fixedDeltaTime);

        if (ramElapsed >= ramDuration) {
            IsActive = false;
        }
    }

    public void Interrupt() {
        IsActive = false;
    }

    public void OnHitEnemy(Collider2D enemyCollider) {
        if (!IsActive) return;

        IsActive = false; // 停止衝撞

        if (enemyCollider.TryGetComponent(out IDamageable target)) {
            target.TakeDamage(owner.stats.baseAttack * damageMultiplier);
            AudioManager.Instance.PlaySFX("crit_hit2");
        }

        // 大招命中時嘗試打斷+擊退敵人(方位是衝刺方向;敵人自己決定當下能不能被打斷,例如正在放不可打斷的大招)
        if (enemyCollider.TryGetComponent(out IKnockbackable knockbackTarget)) {
            knockbackTarget.TryKnockback(ramDirection, knockbackDistance);
        }
    }
}
