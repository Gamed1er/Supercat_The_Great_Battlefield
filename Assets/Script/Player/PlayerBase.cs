using UnityEngine;

// 共同的東西:血量、移動、輸入接收、充能值
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Animator))]
public class PlayerBase : MonoBehaviour, IDamageable {
    public PlayerStats stats;
    protected ISkill normalAttack;
    protected ISkill dashSkill;
    protected ISkill ultimateSkill;

    public GameObject hitEffectPrefab; // 受到傷害時的特效,免傷時不會播放

    protected Rigidbody2D rb;
    SpriteRenderer spriteRenderer;
    Animator animator;
    Vector2 moveInput;

    public Vector2 FacingDirection { get; private set; } = Vector2.right;

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
    }

    public virtual void Update() {
        moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;
        if (moveInput != Vector2.zero) FacingDirection = moveInput;

        if (moveInput.x > 0) spriteRenderer.flipX = false;
        else if (moveInput.x < 0) spriteRenderer.flipX = true;

        normalAttack.TryExecute();
        dashSkill.TryExecute();
        ultimateSkill.TryExecute();
    }

    protected virtual void FixedUpdate() {
        bool skillControllingMovement = dashSkill.IsActive || ultimateSkill.IsActive;
        if (!skillControllingMovement) {
            rb.MovePosition(rb.position + moveInput * stats.moveSpeed * Time.fixedDeltaTime);
        }

        dashSkill.Tick();
        ultimateSkill.Tick();
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
        float multiplier = ultimateSkill.IsActive ? 0f : 1f; // 終結技衝撞過程中完全免疫傷害
        float actualDamage = amount * multiplier;
        stats.Health -= actualDamage;

        if (actualDamage > 0f) {
            animator.SetTrigger("KB");
            AudioManager.Instance.PlayRandomHurtSfx();
            if (hitEffectPrefab != null) Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
        }
    }
}

public interface IDamageable {
    void TakeDamage(float amount);
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
