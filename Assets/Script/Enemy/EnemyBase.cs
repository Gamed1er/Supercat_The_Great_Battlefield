using UnityEngine;

// 沒有任何技能的敵人,純粹用來測試普攻邏輯。有實際行為的敵人繼承這個類別。
// 打斷(KB)/擊退邏輯放在這裡共用:子類別的攻擊/行為邏輯應該在 Update/FixedUpdate 一開始就擋掉 IsKnockedBack,
// 且子類別覆寫 FixedUpdate 時要記得呼叫 base.FixedUpdate(),否則擊退位移不會生效。
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class EnemyBase : MonoBehaviour, IDamageable, IKnockbackable {
    // 難度連續值範圍 0~5(由 LevelManager 在生成敵人後立刻套用,必須早於 Start,詳見 ApplyDifficulty),
    // 數字越大越難;LevelManager 的難度欄位共用這個上限。
    public const int MaxDifficulty = 5;

    [Header("難度數值 (health/baseAttack 是難度 0 的數值,每 +1 級乘一次對應倍率;難度 5 額外多乘一次拉開落差)")]
    public float health = 50f; // 難度 0 的血量,ApplyDifficulty 後會被覆寫成套用難度後的目前血量
    public float baseAttack = 5f; // 難度 0 的攻擊力,ApplyDifficulty 後會被覆寫成套用難度後的目前攻擊力
    public float healthMultiplier = 1.2f;
    public float attackMultiplier = 1.2f;

    public float knockbackDuration = 0.5f; // 被打斷後的硬直時間,子類別可覆寫預設值
    public GameObject hitEffectPrefab; // 受到傷害時的特效

    protected Animator animator; // 沒有 Animator 元件的敵人(例如這個測試用的 EnemyBase)會是 null,SetTrigger 前記得判斷
    protected Rigidbody2D rb;

    protected Vector2 groundMin;
    protected Vector2 groundMax;

    float maxHealth;
    KnockbackState knockback;

    public bool IsDead { get; private set; }
    public bool IsKnockedBack => knockback.IsKnockedBack; // 硬直中:子類別應暫停自己的行為邏輯,且不能對玩家造成傷害
    // 戰鬥結算(玩家死亡)流程用:外部強制暫停 AI,子類別應比照 IsDead/IsKnockedBack 在 Update/FixedUpdate 開頭擋掉
    public bool IsPaused { get; private set; }

    public void SetPaused(bool paused) => IsPaused = paused;

    // 給 UI 血條(怪池總血量加總)讀取用
    public float MaxHealth => maxHealth;
    public float HealthRatio => maxHealth > 0f ? Mathf.Clamp01(health / maxHealth) : 0f;

    protected virtual void Awake() {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;

        // 敵人不再對玩家產生物理碰撞(不會互相推擠/卡住),接觸傷害改走 OnTrigger 系列偵測(見子類別的接觸傷害邏輯)
        GetComponent<CircleCollider2D>().isTrigger = true;

        animator = GetComponent<Animator>();
        knockback = new KnockbackState(rb);
    }

    // 必須晚於 LevelManager 呼叫 ApplyDifficulty(在所有物件的 Awake 都跑完後才會進到這裡),才能抓到套用難度後的血量
    protected virtual void Start() {
        maxHealth = health;
    }

    protected virtual void FixedUpdate() {
        knockback.Tick(Time.fixedDeltaTime);
    }

    // 依難度算出目前血量/攻擊力,由 LevelManager 在生成敵人後立刻呼叫(必須早於 Start)。
    // health/baseAttack 在呼叫當下的值視為難度 0 的基準,乘上 multiplier^難度 次方算出目前值;
    // 難度 5(MaxDifficulty)再額外多乘一次,讓最高難度多一段落差。只能對同一個敵人呼叫一次,
    // 重複呼叫會拿上一次算出的結果當底數疊乘。
    // 子類別可覆寫並在呼叫 base.ApplyDifficulty 後,再疊加自己專屬的難度參數(例如攻擊週期、位移速度)。
    public virtual void ApplyDifficulty(int difficulty) {
        difficulty = Mathf.Clamp(difficulty, 0, MaxDifficulty);
        int exponent = difficulty == MaxDifficulty ? difficulty + 1 : difficulty;

        health *= Mathf.Pow(healthMultiplier, exponent);
        baseAttack *= Mathf.Pow(attackMultiplier, exponent);
    }

    // 地面系敵人的遊走/追擊範圍,由 LevelManager 依 LevelData 算好後,在生成敵人後立刻呼叫(必須早於 Start)。
    // 不需要地板的敵人(例如飛行系)可以不管這個範圍,不必覆寫。
    public virtual void SetGroundBounds(Vector2 min, Vector2 max) {
        groundMin = min;
        groundMax = max;
    }

    // 目前是否可以被打斷,子類別可覆寫(例如正在放不可打斷的大招時回傳 false)
    protected virtual bool CanBeKnockedBack => true;

    // 觸發被打斷+擊退:播放 KB 動畫,套用位移,進入硬直。回傳是否成功觸發(已死亡/已在硬直中/當下不可被打斷都會失敗)。
    // 硬直時間固定用自己的 knockbackDuration,不受攻擊方指定;distance 給 0 時只有硬直沒有位移。
    public virtual bool TryKnockback(Vector2 direction, float distance) {
        if (IsDead || !CanBeKnockedBack) return false;
        if (!knockback.TryApply(direction, distance, knockbackDuration)) return false;

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
