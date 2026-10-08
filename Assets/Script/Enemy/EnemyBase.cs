using System.Collections;
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

    const float DeathFadeDelay = 1f; // 死亡後等這麼久才開始隱藏
    const float DeathFadeDuration = 0.3f; // 隱藏(alpha 淡出)過程的時間
    const float KnockbackDistanceMultiplier = 0.5f; // 所有敵人受到的擊退位移統一打折,原始數值(攻擊端指定的 distance)覺得太誇張

    [Header("難度數值 (health/baseAttack 是難度 0 的數值,每 +1 級乘一次對應倍率;難度 5 額外多乘一次拉開落差)")]
    public float health = 50f; // 難度 0 的血量,ApplyDifficulty 後會被覆寫成套用難度後的目前血量
    public float baseAttack = 5f; // 難度 0 的攻擊力,ApplyDifficulty 後會被覆寫成套用難度後的目前攻擊力
    public float healthMultiplier = 1.2f;
    public float attackMultiplier = 1.2f;

    public float knockbackDuration = 0.5f; // 被打斷後的硬直時間,子類別可覆寫預設值
    public GameObject hitEffectPrefab; // 受到傷害時的特效

    [Header("妨害效果圖示")]
    [SerializeField] GameObject debuffIconPrefab; // 掛在敵人身上顯示目前妨害效果的圖示,觸發時才 Instantiate,見 DebuffIconStack
    [SerializeField] Vector3 debuffIconLocalOffset = new Vector3(0.4f, -0.4f, 0f); // 角色圖像右下方

    protected Animator animator; // 沒有 Animator 元件的敵人(例如這個測試用的 EnemyBase)會是 null,SetTrigger 前記得判斷
    protected Rigidbody2D rb;

    protected Vector2 groundMin;
    protected Vector2 groundMax;

    float maxHealth;
    KnockbackState knockback;
    EnemyDebuffState debuffState;
    bool wasKnockedBackLastTick; // 偵測 IsKnockedBack 從 true 轉 false 的瞬間,通知圖示疊層擊退已結束

    public bool IsDead { get; private set; }
    public bool IsKnockedBack => knockback.IsKnockedBack; // 硬直中:子類別應暫停自己的行為邏輯,且不能對玩家造成傷害
    // 戰鬥結算(玩家死亡)流程用:外部強制暫停 AI,子類別應比照 IsDead/IsKnockedBack 在 Update/FixedUpdate 開頭擋掉
    public bool IsPaused { get; private set; }

    // 妨害效果(我方施加於敵方):眩暈/麻痺/失焦,見 EnemyDebuffState。子類別在自己的 Update/FixedUpdate 讀取這些套用
    public bool IsStunned => debuffState.IsStunned;
    public float MoveSpeedMultiplier => debuffState.MoveSpeedMultiplier;
    public float CooldownTimeMultiplier => debuffState.CooldownTimeMultiplier;
    public bool HasTargetLock => debuffState.HasTargetLock;
    public Vector2 TargetLockPosition => debuffState.TargetLockPosition;

    public void SetPaused(bool paused) => IsPaused = paused;

    // 給 UI 血條(怪池總血量加總)讀取用
    public float MaxHealth => maxHealth;
    public float HealthRatio => maxHealth > 0f ? Mathf.Clamp01(health / maxHealth) : 0f;
    // 目前護盾量(給 UI 護盾條加總用),沒有護盾機制的敵人固定是 0;有護盾的子類別(例如 CatHero)覆寫
    public virtual float Shield => 0f;

    protected virtual void Awake() {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;

        // 敵人不再對玩家產生物理碰撞(不會互相推擠/卡住),接觸傷害改走 OnTrigger 系列偵測(見子類別的接觸傷害邏輯)
        GetComponent<CircleCollider2D>().isTrigger = true;

        animator = GetComponent<Animator>();
        knockback = new KnockbackState(rb);
        debuffState = new EnemyDebuffState(gameObject, debuffIconPrefab, debuffIconLocalOffset);
    }

    // 必須晚於 LevelManager 呼叫 ApplyDifficulty(在所有物件的 Awake 都跑完後才會進到這裡),才能抓到套用難度後的血量
    protected virtual void Start() {
        maxHealth = health;
    }

    protected virtual void FixedUpdate() {
        knockback.Tick(Time.fixedDeltaTime);
        debuffState.Tick(Time.fixedDeltaTime);
        // 暈眩時動畫暫停播放,套用到所有敵人,不用每個子類別各自處理;死亡後停止套用,避免死亡當下還在暈眩中時,
        // 死亡動畫被卡在 speed=0 直到暈眩計時器跑完(debuffState 不管死活都會繼續 Tick,見上面兩行)
        if (animator != null) animator.speed = (!IsDead && IsStunned) ? 0f : 1f;

        bool isKnockedBackNow = IsKnockedBack;
        if (wasKnockedBackLastTick && !isKnockedBackNow) debuffState.NotifyKnockbackEnded();
        wasKnockedBackLastTick = isKnockedBackNow;
    }

    // 依難度算出目前血量/攻擊力,由 LevelManager 在生成敵人後立刻呼叫(必須早於 Start)。
    // health/baseAttack 在呼叫當下的值視為難度 0 的基準,乘上 multiplier^難度 次方算出目前值;
    // 難度 5(MaxDifficulty)再額外多乘一次,讓最高難度多一段落差。只能對同一個敵人呼叫一次,
    // 重複呼叫會拿上一次算出的結果當底數疊乘。
    // 子類別可覆寫並在呼叫 base.ApplyDifficulty 後,再疊加自己專屬的難度參數(例如攻擊週期、位移速度)。
    public virtual void ApplyDifficulty(int difficulty) {
        health = ScaleForDifficulty(health, healthMultiplier, difficulty);
        baseAttack = ScaleForDifficulty(baseAttack, attackMultiplier, difficulty);
    }

    // 難度倍率公式本體:不需要生成敵人就能算,測試大廳的敵人資訊面板直接拿 prefab 上的難度 0 數值套這個預覽
    public static float ScaleForDifficulty(float difficulty0Value, float multiplier, int difficulty) {
        difficulty = Mathf.Clamp(difficulty, 0, MaxDifficulty);
        int exponent = difficulty == MaxDifficulty ? difficulty + 1 : difficulty;
        return difficulty0Value * Mathf.Pow(multiplier, exponent);
    }

    // 地面系敵人的遊走/追擊範圍,由 LevelManager 依 LevelData 算好後,在生成敵人後立刻呼叫(必須早於 Start)。
    // 不需要地板的敵人(例如飛行系)可以不管這個範圍,不必覆寫。
    public virtual void SetGroundBounds(Vector2 min, Vector2 max) {
        groundMin = min;
        groundMax = max;
    }

    // 目前是否可以被打斷,子類別可覆寫(例如正在放不可打斷的大招時回傳 false)
    protected virtual bool CanBeKnockedBack => true;

    // 死亡淡出時是否播放死亡音效;大量生成/頻繁死亡的召喚物(例如貓俠的滾石)覆寫成 false,避免音效洗版
    protected virtual bool PlaysDeathSfx => true;

    // 觸發被打斷+擊退:播放 KB 動畫,套用位移,進入硬直。回傳是否成功觸發(已死亡/已在硬直中/當下不可被打斷都會失敗)。
    // 硬直時間固定用自己的 knockbackDuration,不受攻擊方指定;distance 給 0 時只有硬直沒有位移。
    public virtual bool TryKnockback(Vector2 direction, float distance) {
        if (IsDead || !CanBeKnockedBack) return false;
        if (!knockback.TryApply(direction, distance * KnockbackDistanceMultiplier, knockbackDuration)) return false;

        if (animator != null) animator.SetTrigger("KB");
        debuffState.NotifyKnockbackApplied();
        return true;
    }

    // 子類別覆寫:打斷手上正在跑的行為(例如 DogeEnemy 的 StopAllCoroutines + 重置 isPerformingAction)。
    // 眩暈第一次從無到有生效時會呼叫這個,基底類別沒有可打斷的行為,預設空實作。
    protected virtual void InterruptCurrentAction() { }

    // 妨害效果(我方施加於敵方)對外 API,回傳是否成功套用。
    // 眩暈沿用 CanBeKnockedBack 這道免疫閘門(跟擊退共用:大狗叫等不可中斷狀態眩暈也打不進去),
    // 第一次從無到有生效時打斷手上正在跑的行為。
    public virtual bool TryApplyStun(float duration) {
        if (IsDead || !CanBeKnockedBack) return false;

        if (debuffState.ApplyStun(duration)) InterruptCurrentAction();
        return true;
    }

    // 麻痺:沒有免疫閘門,移動速度/攻擊週期冷卻的縮放由子類別自行讀取 MoveSpeedMultiplier/CooldownTimeMultiplier 套用
    public virtual bool TryApplySlow(float duration) {
        if (IsDead) return false;
        debuffState.ApplySlow(duration);
        return true;
    }

    // 失焦:不受 CanBeKnockedBack 影響(不可被打斷的狀態依然會失焦),不可疊加,實際瞄準替換由子類別讀取
    // HasTargetLock/TargetLockPosition 自行套用(例如 DogeEnemy 的 Jump/Bite 瞄準點)
    public virtual bool TryApplyTargetLock(Vector2 position, float duration) {
        if (IsDead) return false;
        debuffState.ApplyTargetLock(position, duration);
        return true;
    }

    // virtual:讓「兩隻共用一包血量」這類特殊情境(見 PairedBossEnemy)可以整個覆寫傷害/免傷計算,
    // 不必被這裡的單純扣血邏輯綁住;一般敵人(狗仔等)不需要覆寫,維持原本行為。
    public virtual void TakeDamage(float amount, bool fromEnemyAttack = true) {
        if (IsDead || amount <= 0f) return;

        health -= amount;
        AudioManager.Instance.PlayRandomHurtSfx();
        if (hitEffectPrefab != null) Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);

        if (health <= 0f) Die();
    }

    // 死亡表現(動畫/淡出/停止行為),從 TakeDamage 抽出來,讓覆寫 TakeDamage 的子類別(例如共用血量池見底時)也能呼叫同一套流程。
    protected virtual void Die() {
        IsDead = true;
        StopAllCoroutines(); // 取消所有還在跑的行為(跳躍/大狗叫等),死亡當下立刻打斷,不會播完手上的動作再結束

        if (animator != null) {
            animator.SetBool("isDeath", true);
            animator.SetTrigger("KB");
        }

        StartCoroutine(DeathFadeRoutine());
    }

    // 由外部(例如共用血量池的隊友)直接宣告陣亡,跳過一般扣血流程,沿用同一套死亡表現;已死亡時不重複觸發。
    public void ForceDie() {
        if (!IsDead) Die();
    }

    // 死亡 DeathFadeDelay 秒後,花 DeathFadeDuration 秒把 sprite 淡出隱藏,隱藏開始時播放死亡音效
    IEnumerator DeathFadeRoutine() {
        yield return new WaitForSeconds(DeathFadeDelay);

        if (PlaysDeathSfx) AudioManager.Instance.PlaySFX("death");

        if (!TryGetComponent(out SpriteRenderer spriteRenderer)) yield break;

        Color startColor = spriteRenderer.color;
        float elapsed = 0f;
        while (elapsed < DeathFadeDuration) {
            elapsed += Time.deltaTime;
            spriteRenderer.color = new Color(startColor.r, startColor.g, startColor.b, Mathf.Lerp(startColor.a, 0f, elapsed / DeathFadeDuration));
            yield return null;
        }

        spriteRenderer.color = new Color(startColor.r, startColor.g, startColor.b, 0f);
    }
}
