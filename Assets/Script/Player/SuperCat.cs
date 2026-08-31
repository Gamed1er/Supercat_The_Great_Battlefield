using UnityEngine;

public class SuperCat : PlayerBase {
    [SerializeField] GameObject bulletPrefab;

    protected override void Awake() {
        base.Awake();

        // TODO: 目前先寫死 Lv1 數值,之後接上升級系統再抽成長公式
        stats = new PlayerStats(attack: 11f, health: 110f, moveSpeed: 8f);

        normalAttack = new AutoLockShootSkill(this, bulletPrefab, damageMultiplier: 1.0f);
        dashSkill = new DashSkill(this, cooldown: 2f);
        ultimateSkill = new ChargeRamSkill(this, damageMultiplier: 2.0f);
    }
}
