using UnityEngine;

// 赤井抄太郎:火力至上的彈幕角色,機動性較差(沒有位移技能)。
// 普攻/戰技/大招子彈皆具破壞性且都算「普攻類型子彈」,命中敵人或摧毀敵方子彈時觸發被動,縮短戰技與大招冷卻。
public class RedSuperCat : PlayerBase {
    [SerializeField] GameObject bulletPrefab;

    const float passiveCooldownReduction = 0.5f; // 被動:命中時縮短戰技/大招冷卻的秒數

    BarrageSkill barrageSkill;
    RadialBurstSkill radialBurstSkill;

    protected override void Awake() {
        base.Awake();

        // TODO: 尚未接上升級系統,先寫死 Lv1 數值。成長公式:數值 = 基礎值 * (10 + 等級),Lv1/Lv10 皆已對過:
        // 攻擊 0.5*(10+1)=5.5、0.5*(10+10)=10;血量 12*(10+1)=132、12*(10+10)=240
        const int level = 1;
        const float baseAttack = 0.5f;
        const float baseHealth = 12f;
        float attack = baseAttack * (10 + level);
        float health = baseHealth * (10 + level);

        stats = new PlayerStats(attack: attack, health: health, moveSpeed: 7.2f);

        // 戰技開火、大招旋轉發射期間都會打斷普攻;大招觸發當下直接打斷戰技(見 RadialBurstSkill.TryExecute)
        normalAttack = new AutoLockShootSkill(this, bulletPrefab, damageMultiplier: 1.0f, bulletType: BulletType.Destructive,
            onHit: OnProjectileHit, isSuppressed: () => barrageSkill.IsFiring || radialBurstSkill.IsSpinning);

        barrageSkill = new BarrageSkill(this, bulletPrefab, damageMultiplier: 1.0f, cooldown: 4.5f,
            onProjectileHit: OnProjectileHit);

        radialBurstSkill = new RadialBurstSkill(this, bulletPrefab, damageMultiplier: 1.0f, cooldown: 20f,
            barrageSkillToInterrupt: barrageSkill, onProjectileHit: OnProjectileHit);

        dashSkill = barrageSkill;
        ultimateSkill = radialBurstSkill;
    }

    // 大招是純冷卻制(不吃充能),UI 用剩餘秒數顯示比「n / 120」的充能式格式更直觀
    public override bool S2_ShowSecondsFormat => true;

    // 被動:普攻/戰技/大招的子彈命中敵人或摧毀敵方子彈時,縮短戰技和大招的冷卻(僅限這個角色,不動 PlayerBase/ISkill 以外的共用邏輯)
    void OnProjectileHit() {
        barrageSkill.ReduceCooldown(passiveCooldownReduction);
        radialBurstSkill.ReduceCooldown(passiveCooldownReduction);
    }
}
