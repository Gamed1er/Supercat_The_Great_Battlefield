using UnityEngine;

// 沒有任何技能的敵人,純粹用來測試普攻邏輯。有實際行為的敵人繼承這個類別。
// 打斷(KB)邏輯放在這裡共用:子類別的攻擊/行為邏輯應該在 Update/FixedUpdate 一開始就擋掉 IsKnockedBack。
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class EnemyBase : MonoBehaviour, IDamageable {
    public float health = 50f;
    public float knockbackDuration = 0.5f; // 被打斷後的硬直時間,子類別可覆寫預設值
    public GameObject hitEffectPrefab; // 受到傷害時的特效

    protected Animator animator; // 沒有 Animator 元件的敵人(例如這個測試用的 EnemyBase)會是 null,SetTrigger 前記得判斷

    protected bool IsDead { get; private set; }
    public bool IsKnockedBack { get; private set; } // 硬直中:子類別應暫停自己的行為邏輯,且不能對玩家造成傷害

    float knockbackTimer;

    protected virtual void Awake() {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;

        animator = GetComponent<Animator>();
    }

    protected virtual void Update() {
        if (!IsKnockedBack) return;

        knockbackTimer -= Time.deltaTime;
        if (knockbackTimer <= 0f) IsKnockedBack = false;
    }

    protected virtual void FixedUpdate() { }

    // 目前是否可以被打斷,子類別可覆寫(例如正在放不可打斷的大招時回傳 false)
    protected virtual bool CanBeKnockedBack => true;

    // 觸發被打斷:播放 KB 動畫並進入硬直。回傳是否成功觸發(已死亡/已在硬直中/當下不可被打斷都會失敗)
    public virtual bool TryKnockback() {
        if (IsDead || IsKnockedBack || !CanBeKnockedBack) return false;

        IsKnockedBack = true;
        knockbackTimer = knockbackDuration;
        if (animator != null) animator.SetTrigger("KB");
        return true;
    }

    public void TakeDamage(float amount) {
        if (IsDead || amount <= 0f) return;

        health -= amount;
        AudioManager.Instance.PlayRandomHurtSfx();
        if (hitEffectPrefab != null) Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);

        if (health <= 0f) {
            IsDead = true;
            Debug.Log($"{name} 死亡");
        }
    }
}
