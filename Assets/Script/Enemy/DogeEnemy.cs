using System.Collections;
using UnityEngine;

// 難度:由 BattleManager 在 Awake 套用到 DogeEnemy 身上,決定基礎數值
public enum DogeDifficulty { Easy, Normal, Hard }

// 狗仔:新手教學敵人。目前只有跳躍,血量低於一半進入第二階段,新增大狗叫。
// 狗被限制在地面矩形範圍內遊走/追擊,跳躍時可以暫時跳出範圍。
[RequireComponent(typeof(Animator))]
public class DogeEnemy : EnemyBase {
    static readonly Vector2 GroundMin = new Vector2(-8f, -3.5f);
    static readonly Vector2 GroundMax = new Vector2(8f, -2f);

    const float phase2HealthPercent = 0.65f; // 血量低於此比例進入二階段,全難度共用

    [Header("顯示")]
    public string enemyName = "普通狗仔"; // 給 UI 血條用的名稱,暫定

    [Header("基礎數值")]
    public float baseAttack = 5f;
    public float moveSpeed = 6f;
    public float phase1CycleInterval = 1.5f;
    public float phase2CycleInterval = 0.5f;
    public float chaseSpeedMultiplier = 1.5f;

    [Header("跳躍")]
    public float jumpDuration = 0.8f;
    public float jumpHeight = 2f;

    [Header("咬 (玩家躲在地面以下、跳躍打不到時使用)")]
    public float biteThresholdY = -2.5f; // 玩家 y 低於這個值時,攻擊改用咬而非跳躍
    public float biteChaseDuration = 1f; // 追擊這麼久之後就咬一下,不用真的追到

    [Header("大狗叫 (二階段)")]
    public float bigBarkChargeDuration = 1.5f;
    public float bigBarkSweepAngle = 120f; // 度
    public float bigBarkRotationSpeed = 90f; // 度/秒
    public float bigBarkBeamLength = 24f;
    public float bigBarkBeamWidth = 1.5f;
    public float bigBarkBeamGap = 1f; // 音波離狗的距離,不直接貼身
    public float bigBarkCooldown = 10f;
    public GameObject sonicWavePrefab;

    const float mouthAnimDuration = 10f / 60f; // OpenMouth / CloseMouth 動畫長度
    const float bodyContactDamageInterval = 0.5f; // 碰到玩家的傷害,最多每 0.5 秒觸發一次

    Rigidbody2D rb;
    SpriteRenderer spriteRenderer;
    Transform player;

    bool isPerformingAction; // 跳/大狗叫進行中時,暫停遊走與攻擊判定
    bool isBigBarking; // 大狗叫進行中(移動到定位/蓄力/掃射全程)不可被打斷
    float attackCycleTimer;
    float bigBarkCooldownTimer;
    bool hasEnteredPhase2;
    float bodyContactDamageTimer;
    float maxHealth;
    Vector2 wanderTarget;

    bool IsPhase2 => health <= maxHealth * phase2HealthPercent;
    float CurrentCycleInterval => IsPhase2 ? phase2CycleInterval : phase1CycleInterval;
    protected override bool CanBeKnockedBack => !isBigBarking;

    // 給 UI 血條讀取用
    public float HealthRatio => maxHealth > 0f ? Mathf.Clamp01(health / maxHealth) : 0f;

    // 依難度覆寫基礎數值,由 BattleManager 在自己的 Awake 中呼叫,
    // 必須早於下面 Start() 抓 maxHealth/設定初始攻擊週期,才會套用到正確的數值
    public void ApplyDifficulty(DogeDifficulty difficulty) {
        switch (difficulty) {
            case DogeDifficulty.Easy:
                baseAttack = 6f;
                health = 400f;
                phase1CycleInterval = 2f;
                phase2CycleInterval = 1f;
                jumpDuration = 1f;
                break;
            case DogeDifficulty.Normal:
                baseAttack = 10f;
                health = 600f;
                phase1CycleInterval = 1.5f;
                phase2CycleInterval = 0.5f;
                break;
            case DogeDifficulty.Hard:
                baseAttack = 20f;
                health = 750f;
                phase1CycleInterval = 1f;
                phase2CycleInterval = 0.3f;
                bigBarkRotationSpeed = 140f;
                break;
        }
    }

    protected override void Awake() {
        base.Awake();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;
    }

    void Start() {
        // 所有物件的 Awake 都跑完後才會進到這裡,確保 BattleManager 已經套用完難度數值
        maxHealth = health;
        PickNewWanderTarget();
        attackCycleTimer = CurrentCycleInterval;
        bigBarkCooldownTimer = bigBarkCooldown;
    }

    protected override void Update() {
        base.Update(); // 處理 KB 硬直計時

        if (IsDead || player == null || isPerformingAction || IsKnockedBack) return;

        if (IsPhase2) {
            if (!hasEnteredPhase2) {
                hasEnteredPhase2 = true;
                bigBarkCooldownTimer = 0f; // 剛進入二階段立刻施放一次大狗叫
            } else {
                bigBarkCooldownTimer -= Time.deltaTime;
            }

            if (bigBarkCooldownTimer <= 0f) {
                StartCoroutine(BigBarkRoutine());
                return;
            }
        }

        attackCycleTimer -= Time.deltaTime;
        if (attackCycleTimer <= 0f) {
            attackCycleTimer = CurrentCycleInterval;
            if (player.position.y < biteThresholdY) {
                StartCoroutine(BiteRoutine()); // 玩家躲太低,跳躍打不到,改用貼地的咬
            } else {
                StartCoroutine(JumpRoutine());
            }
        }
    }

    protected override void FixedUpdate() {
        if (bodyContactDamageTimer > 0f) bodyContactDamageTimer -= Time.fixedDeltaTime;

        if (IsDead || isPerformingAction || IsKnockedBack) return; // 攻擊中的移動由各自的 Routine 自己處理,硬直中站著不動

        if (Vector2.Distance(rb.position, wanderTarget) < 0.2f) {
            PickNewWanderTarget();
        } else {
            MoveTowards(wanderTarget, moveSpeed);
        }
    }

    void PickNewWanderTarget() {
        wanderTarget = new Vector2(
            Random.Range(GroundMin.x, GroundMax.x),
            Random.Range(GroundMin.y, GroundMax.y)
        );
    }

    // 往目標移動一個 FixedUpdate 的量,並依水平方向翻面
    void MoveTowards(Vector2 target, float speed) {
        Vector2 current = rb.position;
        Vector2 next = Vector2.MoveTowards(current, target, speed * Time.fixedDeltaTime);
        next.x = Mathf.Clamp(next.x, GroundMin.x, GroundMax.x);
        next.y = Mathf.Clamp(next.y, GroundMin.y, GroundMax.y);
        rb.MovePosition(next);

        SetFacing(target.x - current.x);
    }

    void SetFacing(float dx) {
        if (dx > 0.01f) spriteRenderer.flipX = false;
        else if (dx < -0.01f) spriteRenderer.flipX = true;
    }

    IEnumerator JumpRoutine() {
        isPerformingAction = true;

        Vector2 start = rb.position;
        float targetX = player.position.x; // 只鎖定水平方向;跳躍最高點會鎖定比玩家高一點的位置,但落地一定回到起跳的地面高度,不會卡在半空中
        float peakHeight = Mathf.Max(jumpHeight, player.position.y + 1f - start.y);

        SetFacing(targetX - start.x);
        animator.SetTrigger("OpenMouth");

        float elapsed = 0f;
        while (elapsed < jumpDuration) {
            elapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(elapsed / jumpDuration);

            float x = Mathf.Lerp(start.x, targetX, t);
            float y = start.y + peakHeight * 4f * t * (1f - t); // 拋物線弧度,像重力一樣升起(至少到玩家上方一點)再落回起跳的地面高度
            rb.MovePosition(new Vector2(x, y));

            yield return new WaitForFixedUpdate();
        }

        animator.SetTrigger("CloseMouth");
        yield return new WaitForSeconds(mouthAnimDuration);

        isPerformingAction = false;
    }

    // 貼地咬:玩家躲在地面矩形以下、跳躍的拋物線打不到時使用。朝玩家水平方向追擊一段時間後咬一下。
    // 傷害不在這裡判定,只要追擊過程中碰到玩家就會由通用的接觸傷害處理。
    IEnumerator BiteRoutine() {
        isPerformingAction = true;

        float chaseSpeed = moveSpeed * chaseSpeedMultiplier;
        float elapsed = 0f;

        while (elapsed < biteChaseDuration) {
            elapsed += Time.fixedDeltaTime;

            Vector2 current = rb.position;
            Vector2 target = new Vector2(player.position.x, current.y); // 咬是貼地攻擊,只追水平方向
            MoveTowards(target, chaseSpeed);

            yield return new WaitForFixedUpdate();
        }

        animator.SetTrigger("OpenMouth");
        yield return new WaitForSeconds(mouthAnimDuration);
        animator.SetTrigger("CloseMouth");
        yield return new WaitForSeconds(mouthAnimDuration);

        isPerformingAction = false;
    }

    IEnumerator BigBarkRoutine() {
        isPerformingAction = true;
        isBigBarking = true; // 大狗叫全程(含移動到定位)不可被打斷

        Vector2 barkPos = new Vector2(0f, GroundMin.y);
        while (Vector2.Distance(rb.position, barkPos) > 0.1f) {
            MoveTowards(barkPos, moveSpeed);
            yield return new WaitForFixedUpdate();
        }

        bool aimRight = Random.value < 0.5f; // 每次隨機從左邊或右邊發射,不鎖定玩家所在側
        spriteRenderer.flipX = !aimRight; // 蓄力時面向音波會出來的那一側

        AudioManager.Instance.PlaySFX("DogeWave");

        // 蓄力期間先生成音波,讓它在 bigBarkChargeDuration 內漸漸淡入,同時嘴巴不停開合
        GameObject wave = null;
        SonicWave sonicWave = null;
        if (sonicWavePrefab != null) {
            wave = Instantiate(sonicWavePrefab, transform.position, Quaternion.identity);
            wave.TryGetComponent(out sonicWave);
            sonicWave?.Init(transform, aimRight, bigBarkSweepAngle, bigBarkRotationSpeed, bigBarkBeamLength, bigBarkBeamWidth, bigBarkBeamGap, baseAttack);
        }

        float chargeElapsed = 0f;
        float mouthTimer = 0f;
        bool mouthOpen = false;
        while (chargeElapsed < bigBarkChargeDuration) {
            float dt = Time.deltaTime;
            chargeElapsed += dt;
            mouthTimer += dt;

            if (mouthTimer >= mouthAnimDuration) {
                mouthTimer = 0f;
                mouthOpen = !mouthOpen;
                animator.SetTrigger(mouthOpen ? "OpenMouth" : "CloseMouth");
            }

            sonicWave?.SetChargingVisual(chargeElapsed / bigBarkChargeDuration);

            yield return null;
        }

        sonicWave?.BeginSweep();

        float sweepDuration = bigBarkSweepAngle / bigBarkRotationSpeed;
        yield return new WaitForSeconds(sweepDuration);

        if (wave != null) Destroy(wave);

        bigBarkCooldownTimer = bigBarkCooldown;
        isBigBarking = false;
        isPerformingAction = false;
    }

    // 大招命中時打斷跳/咬的動作(大狗叫因為 CanBeKnockedBack 擋掉,不會被中斷到)
    public override bool TryKnockback() {
        if (!base.TryKnockback()) return false;

        StopAllCoroutines();
        isPerformingAction = false;
        return true;
    }

    // 通用接觸傷害:不管有沒有在放技能,只要碰到玩家就造成傷害,最多每 bodyContactDamageInterval 秒觸發一次;硬直中不會造成傷害
    void OnCollisionStay2D(Collision2D collision) {
        if (IsDead || IsKnockedBack) return;
        if (bodyContactDamageTimer > 0f) return;
        if (!collision.collider.CompareTag("Player")) return;

        if (collision.collider.TryGetComponent(out IDamageable damageable)) {
            damageable.TakeDamage(baseAttack);
            bodyContactDamageTimer = bodyContactDamageInterval;
        }
    }
}
