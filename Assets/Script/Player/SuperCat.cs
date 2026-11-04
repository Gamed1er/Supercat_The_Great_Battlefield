using UnityEngine;

public class SuperCat : PlayerBase {
    [SerializeField] GameObject bulletPrefab;

    // 攻速遞增被動:普攻間隔從 1 秒開始,每 0.5 秒縮短 0.02 秒,最低 0.5 秒;受到任何傷害時重置回 1 秒
    const float attackIntervalStart = 1f;
    const float attackIntervalMin = 0.5f;
    const float attackIntervalStep = 0.02f;
    const float attackIntervalStepPeriod = 0.5f;

    AutoLockShootSkill normalAttackSkill;
    float attackRampTimer;

    protected override void Awake() {
        base.Awake();

        // TODO: 目前先寫死 Lv1 數值,之後接上升級系統再抽成長公式
        stats = new PlayerStats(attack: 11f, health: 110f, moveSpeed: 8f);

        normalAttack = normalAttackSkill = new AutoLockShootSkill(this, bulletPrefab, damageMultiplier: 1.0f, cooldown: attackIntervalStart);
        dashSkill = new DashSkill(this, cooldown: 5f);
        ultimateSkill = new ChargeRamSkill(this, damageMultiplier: 2.0f);
    }

    public override void Update() {
        // 暫停中/死亡後不累積攻速(與 base.Update 的判斷一致)
        if (Time.timeScale != 0f && !IsDead && normalAttackSkill.BaseCooldown > attackIntervalMin) {
            attackRampTimer += Time.deltaTime;
            while (attackRampTimer >= attackIntervalStepPeriod) {
                attackRampTimer -= attackIntervalStepPeriod;
                normalAttackSkill.BaseCooldown = Mathf.Max(attackIntervalMin, normalAttackSkill.BaseCooldown - attackIntervalStep);
            }
        }

        base.Update();
    }

    public override void TakeDamage(float amount, bool fromEnemyAttack = true) {
        float healthBefore = stats.Health;
        base.TakeDamage(amount, fromEnemyAttack);

        // 只有真的扣到血才重置(免傷中被打到不算)
        if (stats.Health < healthBefore) {
            normalAttackSkill.BaseCooldown = attackIntervalStart;
            attackRampTimer = 0f;
        }
    }
}
