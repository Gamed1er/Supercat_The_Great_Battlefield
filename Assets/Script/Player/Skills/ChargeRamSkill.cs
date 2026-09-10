using UnityEngine;

// 終結技衝撞:右鍵點擊時朝滑鼠鼠標方向衝撞,並在鼠標周圍偵測最近的敵人(如果有)作為追蹤目標,
// 衝刺過程中方向會小幅度朝目標修正(見 Tick 的 homingTurnRateDegrees),而不是全程鎖死直衝目標。
// 過程中完全免疫傷害(由 PlayerBase.TakeDamage 檢查 IsActive 處理);碰到敵人停止位移,造成一段傷害+高額擊退;碰到牆壁只停止。
public class ChargeRamSkill : ISkill {
    const float ramSpeed = 60f;
    const float ramDuration = 0.4f;
    const float chargeCost = 7f;
    const float knockbackDistance = 4f; // 高額擊退,方位固定用衝刺方向
    const float targetSearchRadius = 10f; // 鼠標周圍搜尋追蹤目標的半徑
    const float homingTurnRateDegrees = 200f; // 衝刺方向每秒最多能朝目標修正的角度,數值小才是「小幅度追蹤」而非鎖死

    readonly PlayerBase owner;
    readonly Rigidbody2D rb;
    readonly Camera mainCamera;
    readonly float damageMultiplier;

    public float Cooldown => 0f; // 用充能消耗來限制施放頻率,沒有額外冷卻
    public float CooldownRemaining => 0f;
    public bool IsActive { get; private set; }

    float ramElapsed;
    Vector2 ramDirection;
    Transform homingTarget; // 施放當下鼠標範圍內最近的敵人,找不到就是 null(退化成純直線衝刺)

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
        homingTarget = FindNearestEnemy(mouseWorldPos);
        AudioManager.Instance.PlaySFX("teleport");
        return true;
    }

    // 在鼠標點擊的世界座標周圍找最近的敵人,只在施放當下判定一次(鎖定的是那個當下最近的敵人,不會放到一半換目標)
    Transform FindNearestEnemy(Vector2 mouseWorldPos) {
        Collider2D[] hits = Physics2D.OverlapCircleAll(mouseWorldPos, targetSearchRadius);

        Transform nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (Collider2D hit in hits) {
            if (!hit.CompareTag("Enemy")) continue;

            float distance = Vector2.Distance(mouseWorldPos, hit.transform.position);
            if (distance < nearestDistance) {
                nearestDistance = distance;
                nearest = hit.transform;
            }
        }
        return nearest;
    }

    public void Tick() {
        if (!IsActive) return;

        if (homingTarget != null) {
            Vector2 toTarget = (Vector2)homingTarget.position - rb.position;
            if (toTarget != Vector2.zero) {
                float maxRadiansDelta = homingTurnRateDegrees * Mathf.Deg2Rad * Time.fixedDeltaTime;
                Vector3 rotated = Vector3.RotateTowards(ramDirection, toTarget.normalized, maxRadiansDelta, 0f); // Vector2 沒有 RotateTowards,借 Vector3 版本(z 分量恆為 0)算完再轉回來
                ramDirection = ((Vector2)rotated).normalized;
            }
        }

        ramElapsed += Time.fixedDeltaTime;
        rb.MovePosition(rb.position + ramDirection * ramSpeed * Time.fixedDeltaTime);

        if (ramElapsed >= ramDuration) {
            IsActive = false;
        }
    }

    public void Interrupt() {
        IsActive = false;
        homingTarget = null;
    }

    public void OnHitEnemy(Collider2D enemyCollider) {
        if (!IsActive) return;

        IsActive = false; // 停止衝撞
        homingTarget = null;

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
