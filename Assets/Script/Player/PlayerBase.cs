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

    protected Rigidbody2D rb;
    SpriteRenderer spriteRenderer;
    Animator animator;
    Vector2 moveInput;
    KnockbackState knockback;

    public Vector2 FacingDirection { get; private set; } = Vector2.right;
    public bool IsKnockedBack => knockback.IsKnockedBack;

    // 終結技衝撞過程中免疫擊退(見 Q15:免傷時不該還會被打飛)
    protected virtual bool CanBeKnockedBack => !ultimateSkill.IsActive;
    // 是否免傷:預設跟 CanBeKnockedBack 綁在一起(貓咪超人的大招衝撞免控也免傷),
    // 但兩者不一定要相同(例如免控但仍會受傷的蓄力技),所以拆成獨立的 virtual 屬性,子類別可以各自覆寫
    protected virtual bool IsDamageImmune => !CanBeKnockedBack;

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
    }

    public virtual void Update() {
        if (Time.timeScale == 0f) return; // 暫停(設定選單開啟中)時不接收任何移動/技能輸入,自動回血也一併暫停

        if (LevelManager.Instance != null) stats.TickRegen(Time.deltaTime, LevelManager.Instance.RegenLevelMultiplier);

        moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;
        if (moveInput != Vector2.zero) FacingDirection = moveInput;

        if (moveInput.x > 0) spriteRenderer.flipX = false;
        else if (moveInput.x < 0) spriteRenderer.flipX = true;

        if (IsKnockedBack) return; // 硬直中不能觸發技能

        normalAttack.TryExecute();
        dashSkill.TryExecute();
        ultimateSkill.TryExecute();
    }

    protected virtual void FixedUpdate() {
        bool skillControllingMovement = dashSkill.IsActive || ultimateSkill.IsActive;
        if (!skillControllingMovement && !IsKnockedBack) {
            rb.MovePosition(rb.position + moveInput * stats.moveSpeed * Time.fixedDeltaTime);
        }

        dashSkill.Tick();
        ultimateSkill.Tick();
        knockback.Tick(Time.fixedDeltaTime);
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
        float multiplier = IsDamageImmune ? 0f : 1f;
        float actualDamage = amount * multiplier;
        stats.Health -= actualDamage;

        if (actualDamage > 0f) {
            animator.SetTrigger("KB");
            AudioManager.Instance.PlayRandomHurtSfx();
            if (hitEffectPrefab != null) Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
        }
    }

    // 觸發擊退:成功時打斷正在進行的技能位移(比照撞牆的處理方式),回傳是否成功觸發
    public virtual bool TryKnockback(Vector2 direction, float distance) {
        if (!CanBeKnockedBack) return false;
        if (!knockback.TryApply(direction, distance, knockbackDuration)) return false;

        dashSkill.Interrupt();
        ultimateSkill.Interrupt();
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
