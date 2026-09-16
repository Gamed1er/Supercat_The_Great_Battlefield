using System.Collections;
using UnityEngine;

// 共同的東西:血量、移動、輸入接收、充能值
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Animator))]
public class PlayerBase : MonoBehaviour, IDamageable, IKnockbackable {
    public PlayerStats stats;
    protected ISkill normalAttack;
    protected ISkill dashSkill;
    protected ISkill ultimateSkill;

    public GameObject hitEffectPrefab; // 受到傷害時的特效,免傷時不會播放
    public float knockbackDuration = 0.3f; // 被擊退時的硬直時間

    [Header("技能 UI 圖示")]
    [SerializeField] Sprite s1Icon; // 技能槽1(dashSkill)的圖示,各角色 prefab 各自指定
    [SerializeField] Sprite s2Icon; // 技能槽2(ultimateSkill)的圖示,各角色 prefab 各自指定

    [Header("妨害效果圖示")]
    [SerializeField] GameObject debuffIconPrefab; // 掛在角色身上顯示目前妨害效果的圖示,觸發時才 Instantiate,見 DebuffIconStack
    [SerializeField] Vector3 debuffIconLocalOffset = new Vector3(0.4f, -0.4f, 0f); // 角色圖像右下方

    protected Rigidbody2D rb;
    SpriteRenderer spriteRenderer;
    Animator animator;
    Vector2 moveInput;
    KnockbackState knockback;
    PlayerDebuffState debuffState;
    bool wasKnockedBackLastTick; // 偵測 IsKnockedBack 從 true 轉 false 的瞬間,通知圖示疊層擊退已結束
    bool suppressNormalAttack; // 戰鬥結算(勝利)流程用:停止普攻,但移動/其他技能仍可操作
    bool forcedInvincible; // 戰鬥結算(勝利)流程用:強制免傷,由 BattleResultUI 開關

    public Vector2 FacingDirection { get; private set; } = Vector2.right;
    public bool IsKnockedBack => knockback.IsKnockedBack;
    public bool IsStunned => debuffState.IsStunned; // 妨害效果:眩暈中,擋掉移動/技能輸入(見 Update/FixedUpdate)
    public bool IsCursed => debuffState.IsCursed; // 妨害效果:詛咒中,封鎖 dash/ultimate(見 Update),下墜見 FixedUpdate
    // 血量歸零時設 true,擋掉受傷/擊退/移動/技能輸入,由 BattleResultUI 接管後續(位移到定位、播失敗流程)
    public bool IsDead { get; private set; }

    // 終結技衝撞過程中免疫擊退(見 Q15:免傷時不該還會被打飛)
    protected virtual bool CanBeKnockedBack => !ultimateSkill.IsActive;
    // 是否免傷:預設跟 CanBeKnockedBack 綁在一起(貓咪超人的大招衝撞免控也免傷),
    // 但兩者不一定要相同(例如免控但仍會受傷的蓄力技),所以拆成獨立的 virtual 屬性,子類別可以各自覆寫;
    // forcedInvincible 是額外疊加的外部開關(戰鬥結算勝利流程用),優先於原本的邏輯
    protected virtual bool IsDamageImmune => forcedInvincible || !CanBeKnockedBack;

    // 給 UI 讀取狀態用
    public float Health => stats.Health;
    public float MaxHealth => stats.baseHealth;
    public float S1_CooldownCurrent => dashSkill.CooldownCurrent;
    public float S1_Cooldown => dashSkill.Cooldown;
    public float S2_CooldownCurrent => ultimateSkill.CooldownCurrent;
    public float S2_Cooldown => ultimateSkill.Cooldown;
    // 大招 UI 文字格式:充能制(SuperCat)顯示「目前/上限」比較合理,純冷卻制(例如赤井)顯示剩餘秒數才有意義,見 UIManager.UpdateS2
    public virtual bool S2_ShowSecondsFormat => false;
    public Sprite S1_Icon => s1Icon;
    public Sprite S2_Icon => s2Icon;

    protected virtual void Awake() {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        knockback = new KnockbackState(rb);
        debuffState = new PlayerDebuffState(gameObject, debuffIconPrefab, debuffIconLocalOffset);
    }

    public virtual void Update() {
        if (Time.timeScale == 0f) return; // 暫停(設定選單開啟中)時不接收任何移動/技能輸入,自動回血也一併暫停
        if (IsDead) return; // 死亡後完全鎖死輸入,位置交給 BeginDeathSequence 接管

        if (LevelManager.Instance != null) stats.TickRegen(Time.deltaTime, LevelManager.Instance.RegenLevelMultiplier);

        moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;
        if (moveInput != Vector2.zero) FacingDirection = moveInput;

        if (moveInput.x > 0) spriteRenderer.flipX = false;
        else if (moveInput.x < 0) spriteRenderer.flipX = true;

        if (IsKnockedBack || IsStunned) return; // 硬直/眩暈中不能觸發技能

        if (!suppressNormalAttack) normalAttack.TryExecute();
        if (!IsCursed) { // 詛咒封鎖 dash/ultimate,普攻不受影響
            dashSkill.TryExecute();
            ultimateSkill.TryExecute();
        }
    }

    protected virtual void FixedUpdate() {
        if (IsDead) return; // 位置交給 BeginDeathSequence 的 coroutine 接管,這裡完全不動

        debuffState.Tick(Time.fixedDeltaTime);
        ApplyDebuffCooldownEffects();

        bool skillControllingMovement = dashSkill.IsActive || ultimateSkill.IsActive;
        if (!skillControllingMovement && !IsKnockedBack && !IsStunned) {
            Vector2 velocity = debuffState.ApplyCurseToVelocity(moveInput * stats.moveSpeed * debuffState.MoveSpeedMultiplier);

            Vector2 nextPosition = rb.position + velocity * Time.fixedDeltaTime;
            if (IsCursed && LevelManager.Instance != null) nextPosition.y = Mathf.Max(nextPosition.y, LevelManager.Instance.GroundY);
            rb.MovePosition(nextPosition);
        }

        dashSkill.Tick();
        ultimateSkill.Tick();
        knockback.Tick(Time.fixedDeltaTime);

        bool isKnockedBackNow = IsKnockedBack;
        if (wasKnockedBackLastTick && !isKnockedBackNow) debuffState.NotifyKnockbackEnded();
        wasKnockedBackLastTick = isKnockedBackNow;
    }

    // 麻痺:把冷卻縮放倍率寫進三個技能槽;眩暈:額外把冷卻計時器往後推,讓 Cooldown 在硬直期間完全不前進
    // (充能制的 ultimateSkill 對 ReduceCooldown 是 no-op,所以眩暈不會影響蓄力進度,見 ChargeRamSkill)
    void ApplyDebuffCooldownEffects() {
        float multiplier = debuffState.SkillCooldownMultiplier;
        normalAttack.CooldownMultiplier = multiplier;
        dashSkill.CooldownMultiplier = multiplier;
        ultimateSkill.CooldownMultiplier = multiplier;

        if (IsStunned) {
            normalAttack.ReduceCooldown(-Time.fixedDeltaTime);
            dashSkill.ReduceCooldown(-Time.fixedDeltaTime);
            ultimateSkill.ReduceCooldown(-Time.fixedDeltaTime);
        }
    }

    // 戰鬥結算(勝利)流程用:停止/恢復普攻,移動與其他技能不受影響
    public void SetSuppressNormalAttack(bool suppressed) => suppressNormalAttack = suppressed;
    // 戰鬥結算(勝利)流程用:強制免傷開關
    public void SetForcedInvincible(bool invincible) => forcedInvincible = invincible;

    // 戰鬥結算(失敗)流程用:死亡瞬間播 KB,並在 duration 秒內位移到 BattleResultUI 算好的定位點(忽略原本擊殺那下的擊退方向)
    public void BeginDeathSequence(Vector3 targetPosition, float duration) {
        StartCoroutine(DeathMoveRoutine(targetPosition, duration));
    }

    IEnumerator DeathMoveRoutine(Vector3 targetPosition, float duration) {
        animator.SetTrigger("KB");

        Vector3 start = rb.position;
        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.fixedDeltaTime;
            rb.MovePosition(Vector2.Lerp(start, targetPosition, elapsed / duration));
            yield return new WaitForFixedUpdate();
        }

        rb.MovePosition(targetPosition);
    }

    protected virtual void OnCollisionEnter2D(Collision2D collision) {
        if (collision.collider.CompareTag("Wall")) {
            dashSkill.Interrupt();
            ultimateSkill.Interrupt();
        }
    }

    // 玩家與敵人之間不再有物理碰撞(EnemyBase.Awake 已把敵人的碰撞體設為 Trigger),
    // 衝刺/終結技撞到敵人改用 OnTriggerEnter2D 偵測,牆壁仍是實體碰撞維持 OnCollisionEnter2D。
    protected virtual void OnTriggerEnter2D(Collider2D other) {
        if (other.CompareTag("Enemy")) {
            dashSkill.OnHitEnemy(other);
            ultimateSkill.OnHitEnemy(other);
        }
    }

    public virtual void TakeDamage(float amount)
    {
        if (IsDead) return;

        float multiplier = IsDamageImmune ? 0f : 1f;
        float actualDamage = amount * multiplier;
        stats.Health -= actualDamage;

        if (actualDamage > 0f) {
            animator.SetTrigger("KB");
            AudioManager.Instance.PlayRandomHurtSfx();
            if (hitEffectPrefab != null) Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);

            if (stats.Health <= 0f) {
                IsDead = true;
                animator.SetBool("isDead", true);
                dashSkill.Interrupt();
                ultimateSkill.Interrupt();
            }
        }
    }

    // 觸發擊退:成功時打斷正在進行的技能位移(比照撞牆的處理方式),回傳是否成功觸發
    public virtual bool TryKnockback(Vector2 direction, float distance) {
        if (IsDead || !CanBeKnockedBack) return false;
        if (!knockback.TryApply(direction, distance, knockbackDuration)) return false;

        dashSkill.Interrupt();
        ultimateSkill.Interrupt();
        debuffState.NotifyKnockbackApplied();
        return true;
    }

    // 妨害效果(敵方施加於我方)對外 API,回傳是否成功套用。
    // 眩暈沿用 CanBeKnockedBack 這道免疫閘門(跟擊退共用:大招衝撞等不可中斷狀態眩暈也打不進去),
    // 第一次從無到有生效時比照撞牆/擊退,打斷手上正在跑的 dash/ultimate。
    public virtual bool TryApplyStun(float duration) {
        if (IsDead || !CanBeKnockedBack) return false;

        if (debuffState.ApplyStun(duration)) {
            dashSkill.Interrupt();
            ultimateSkill.Interrupt();
        }
        return true;
    }

    // 麻痺:沒有免疫閘門,移動速度/技能冷卻的縮放由 FixedUpdate 每幀套用(見 ApplyDebuffCooldownEffects)
    public virtual bool TryApplySlow(float duration) {
        if (IsDead) return false;
        debuffState.ApplySlow(duration);
        return true;
    }

    // 詛咒:沒有免疫閘門,下墜/封鎖 dash-ultimate 的實際效果分別在 FixedUpdate/Update 套用
    public virtual bool TryApplyCurse(float duration) {
        if (IsDead) return false;
        debuffState.ApplyCurse(duration);
        return true;
    }
}

public interface IDamageable {
    void TakeDamage(float amount);
}

public interface IKnockbackable {
    bool TryKnockback(Vector2 direction, float distance);
}

public class PlayerStats
{
    public const float MaxCharge = 7f; // 貓咪超人終結技消耗 7 點,滿充能剛好可以放一次

    public float baseAttack;
    public float baseHealth;
    public float moveSpeed;
    public float healthRegenMultiplier; // 自動回血倍率,角色專屬數值,預設 1

    public float Health;
    public float Charge;

    float regenTimer; // 累積時間,見 TickRegen:每經過 1/(每秒回血量) 秒回 1 點血,而非逐幀回小數血量

    public PlayerStats(float attack, float health, float moveSpeed, float healthRegenMultiplier = 1f){
        baseAttack = attack;
        baseHealth = health;
        this.moveSpeed = moveSpeed;
        this.healthRegenMultiplier = healthRegenMultiplier;
        Health = baseHealth;
    }

    public void AddCharge(float amount) {
        bool wasFull = Charge >= MaxCharge;
        Charge = Mathf.Min(Charge + amount, MaxCharge);
        if (!wasFull && Charge >= MaxCharge) AudioManager.Instance.PlaySFX("ultimate_done");
    }

    public void SpendCharge(float amount) {
        Charge = Mathf.Max(Charge - amount, 0f);
    }

    // 自動回血:每秒回血量 = 最大生命 1% * 角色回血倍率 * 關卡倍率,換算成「每隔多久回 1 血」,累積到滿足間隔才真正加血,
    // 而不是逐幀加零點幾點血——例如每秒回 2.5 點,實際效果是每 0.4 秒回 1 點血。
    public void TickRegen(float deltaTime, float levelMultiplier) {
        if (Health >= baseHealth) {
            regenTimer = 0f;
            return;
        }

        float healPerSecond = baseHealth * 0.01f * healthRegenMultiplier * levelMultiplier;
        if (healPerSecond <= 0f) return;

        float interval = 1f / healPerSecond;
        regenTimer += deltaTime;

        while (regenTimer >= interval && Health < baseHealth) {
            regenTimer -= interval;
            Health = Mathf.Min(Health + 1f, baseHealth);
        }
    }
}
