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

    protected Rigidbody2D rb;
    SpriteRenderer spriteRenderer;
    Animator animator;
    Vector2 moveInput;
    KnockbackState knockback;

    public Vector2 FacingDirection { get; private set; } = Vector2.right;
    public bool IsKnockedBack => knockback.IsKnockedBack;

    // 終結技衝撞過程中完全免疫傷害,擊退也一併免疫(見 Q15:免傷時不該還會被打飛)
    protected virtual bool CanBeKnockedBack => !ultimateSkill.IsActive;

    // 給 UI 讀取狀態用
    public float Health => stats.Health;
    public float MaxHealth => stats.baseHealth;
    public float DashCooldown => dashSkill.Cooldown;
    public float DashCooldownRemaining => dashSkill.CooldownRemaining;
    public float UltimateCharge => stats.Charge;
    public float UltimateMaxCharge => PlayerStats.MaxCharge;

    protected virtual void Awake() {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        knockback = new KnockbackState(rb);
    }

    public virtual void Update() {
        if (Time.timeScale == 0f) return; // 暫停(設定選單開啟中)時不接收任何移動/技能輸入

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
        } else if (collision.collider.CompareTag("Enemy")) {
            dashSkill.OnHitEnemy(collision.collider);
            ultimateSkill.OnHitEnemy(collision.collider);
        }
    }

    public virtual void TakeDamage(float amount)
    {
        float multiplier = CanBeKnockedBack ? 1f : 0f; // 跟擊退共用同一個無敵判斷(見 CanBeKnockedBack)
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

    public float Health;
    public float Charge;

    public PlayerStats(float attack, float health, float moveSpeed){
        baseAttack = attack;
        baseHealth = health;
        this.moveSpeed = moveSpeed;
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
}
