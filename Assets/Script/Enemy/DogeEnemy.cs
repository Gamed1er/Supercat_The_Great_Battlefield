using System.Collections;
using UnityEngine;

// 狗仔:新手教學敵人。目前只有跳躍,血量低於一半進入第二階段,新增大狗叫。
// 狗被限制在地面矩形範圍內遊走/追擊(範圍由 LevelManager 透過 SetGroundBounds 設定),跳躍時可以暫時跳出範圍。
[RequireComponent(typeof(Animator))]
public class DogeEnemy : EnemyBase {
    const float phase2HealthPercent = 0.65f; // 血量低於此比例進入二階段,全難度共用

    [Header("基礎數值")]
    public float moveSpeed = 6f;
    public float chaseSpeedMultiplier = 1.5f;

    [Header("出招冷卻 (難度 0 / 難度 5 的秒數,中間難度用等差數列內插,見 ApplyDifficulty)")]
    public float phase1CooldownDifficulty0 = 2f;
    public float phase1CooldownDifficulty5 = 1f;
    public float phase2CooldownDifficulty0 = 1f;
    public float phase2CooldownDifficulty5 = 0.3f;

    [Header("跳躍")]
    public float jumpDuration = 0.8f;
    public float jumpHeight = 2f;
    const float maxJumpPeakYBelowMaxDifficulty = 3.5f; // 難度 5 以下,跳躍最高點的世界座標 y 上限(寫死,難度 5 不受限)

    [Header("咬 (玩家躲在地面以下、跳躍打不到時使用)")]
    public float biteThresholdY = -2.5f; // 玩家 y 低於這個值時,攻擊改用咬而非跳躍
    public float biteChaseDuration = 1f; // 追擊這麼久之後就咬一下,不用真的追到

    [Header("大狗叫 (二階段:連續發射數顆彎曲長條音波)")]
    public float bigBarkChargeDuration = 1.5f; // 第 1 顆音波的蓄力時間(含嘴巴開合 chatter)
    public float bigBarkBeamLength = 5.25f; // 彈體本身的長度(不是舊掃射設計那種貫穿全場的長度,單顆子彈不需要那麼長)
    public float bigBarkBeamWidth = 0.9f;
    public float bigBarkBeamGap = 1.5f; // 音波離狗的距離,不直接貼身
    public float bigBarkCooldown = 10f;
    public GameObject sonicWavePrefab;

    const int sonicWaveCount = 6; // 一次大狗叫總共發射幾顆(蓄力 1 秒 + 發射期 2.1 秒,發射期均分成 sonicWaveCount-1 段間隔)
    const float sonicWaveInterval = 2.1f / 5f; // 第 2 顆以後,每顆各自的預告/蓄力時間,同時也是發射間隔(2.1 秒發射期 ÷ 5 段間隔)
    const float sonicWaveRandomOffsetDeg = 30f; // 瞄準玩家方向後的隨機偏移範圍(正負)
    const float sonicWaveBarkCloseDelay = 0.15f; // 每顆發射瞬間嘴巴張開後,幾秒內關閉

    [Header("接觸傷害")]
    public float contactKnockbackDistance = 0.5f; // 咬/跳/一般碰撞的微幅擊退距離,只有水平分量(見 OnTriggerStay2D)

    const float mouthAnimDuration = 10f / 60f; // OpenMouth / CloseMouth 動畫長度
    const float bodyContactDamageInterval = 0.5f; // 碰到玩家的傷害,最多每 0.5 秒觸發一次

    SpriteRenderer spriteRenderer;
    Transform player;

    bool isPerformingAction; // 跳/大狗叫進行中時,暫停遊走與攻擊判定
    bool isBigBarking; // 大狗叫進行中(移動到定位/蓄力/掃射全程)不可被打斷
    float attackCycleTimer;
    float bigBarkCooldownTimer;
    bool hasEnteredPhase2;
    float bodyContactDamageTimer;
    Vector2 wanderTarget;

    int difficulty; // ApplyDifficulty 套用時記下,跳躍限高判斷會用到
    float phase1CycleInterval; // 由 ApplyDifficulty 依難度算出,不直接在 Inspector 調整
    float phase2CycleInterval;

    bool IsPhase2 => health <= MaxHealth * phase2HealthPercent;
    float CurrentCycleInterval => IsPhase2 ? phase2CycleInterval : phase1CycleInterval;
    protected override bool CanBeKnockedBack => !isBigBarking;

    // 血量/攻擊力已由 base.ApplyDifficulty 處理,這裡只疊加狗仔專屬的難度參數:
    // 出招冷卻用難度 0/難度 5 的秒數線性內插(等差數列)算出中間難度的值。
    public override void ApplyDifficulty(int difficulty) {
        base.ApplyDifficulty(difficulty);

        this.difficulty = Mathf.Clamp(difficulty, 0, MaxDifficulty);
        float t = this.difficulty / (float)MaxDifficulty;
        phase1CycleInterval = Mathf.Lerp(phase1CooldownDifficulty0, phase1CooldownDifficulty5, t);
        phase2CycleInterval = Mathf.Lerp(phase2CooldownDifficulty0, phase2CooldownDifficulty5, t);
    }

    protected override void Awake() {
        base.Awake();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;
    }

    protected override void Start() {
        // 所有物件的 Awake 都跑完後才會進到這裡,確保 LevelManager 已經套用完難度數值與地面範圍
        base.Start(); // 抓 maxHealth
        PickNewWanderTarget();
        attackCycleTimer = CurrentCycleInterval;
        bigBarkCooldownTimer = bigBarkCooldown;
    }

    void Update() {
        if (IsDead || IsPaused || player == null) return;

        // 大狗叫冷卻不管狗仔正在跳/咬/硬直都持續倒數,只是倒數到 0 時不會打斷正在進行的動作,
        // 而是等下面的動作判斷放行(isPerformingAction/IsKnockedBack 都結束)後才真正施放。
        if (IsPhase2 && hasEnteredPhase2) bigBarkCooldownTimer -= Time.deltaTime;

        if (isPerformingAction || IsKnockedBack) return;

        if (IsPhase2) {
            if (!hasEnteredPhase2) {
                hasEnteredPhase2 = true;
                bigBarkCooldownTimer = 0f; // 剛進入二階段立刻施放一次大狗叫
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
        base.FixedUpdate(); // 處理擊退位移,子類別覆寫 FixedUpdate 一定要呼叫,不然擊退不會生效

        if (bodyContactDamageTimer > 0f) bodyContactDamageTimer -= Time.fixedDeltaTime;

        if (IsDead || IsPaused || isPerformingAction || IsKnockedBack) return; // 攻擊中的移動由各自的 Routine 自己處理,硬直中站著不動

        if (Vector2.Distance(rb.position, wanderTarget) < 0.2f) {
            PickNewWanderTarget();
        } else {
            MoveTowards(wanderTarget, moveSpeed);
        }
    }

    void PickNewWanderTarget() {
        wanderTarget = new Vector2(
            Random.Range(groundMin.x, groundMax.x),
            Random.Range(groundMin.y, groundMax.y)
        );
    }

    // 往目標移動一個 FixedUpdate 的量,並依水平方向翻面
    void MoveTowards(Vector2 target, float speed) {
        Vector2 current = rb.position;
        Vector2 next = Vector2.MoveTowards(current, target, speed * Time.fixedDeltaTime);
        next.x = Mathf.Clamp(next.x, groundMin.x, groundMax.x);
        next.y = Mathf.Clamp(next.y, groundMin.y, groundMax.y);
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
        float peakY = Mathf.Max(start.y + jumpHeight, player.position.y + 1f);
        if (difficulty < MaxDifficulty) peakY = Mathf.Min(peakY, maxJumpPeakYBelowMaxDifficulty); // 難度 5 以下限制跳躍最高點,難度 5 維持現狀
        float peakHeight = peakY - start.y;

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

    // 走到場地中央後,依序發射 sonicWaveCount 顆音波:第 1 顆蓄力 bigBarkChargeDuration 秒(含嘴巴開合 chatter),
    // 第 2 顆以後,每顆的預告時間就是發射間隔(sonicWaveInterval),且都在前一顆發射的瞬間才生成,讓玩家看得到排列再閃。
    // 每顆發射時都瞄準當下玩家方向,疊加隨機偏移;每顆發射瞬間額外觸發一次獨立的張嘴/閉嘴(BarkBlipRoutine)。
    IEnumerator BigBarkRoutine() {
        isPerformingAction = true;
        isBigBarking = true; // 大狗叫全程(含移動到定位)不可被打斷

        Vector2 barkPos = new Vector2(0f, groundMin.y);
        while (Vector2.Distance(rb.position, barkPos) > 0.1f) {
            MoveTowards(barkPos, moveSpeed);
            yield return new WaitForFixedUpdate();
        }

        SetFacing(player.position.x - rb.position.x);
        AudioManager.Instance.PlaySFX("DogeWave");

        SonicWave nextWave = SpawnAimedWave();
        for (int i = 0; i < sonicWaveCount; i++) {
            SonicWave currentWave = nextWave;
            float chargeDuration = i == 0 ? bigBarkChargeDuration : sonicWaveInterval;

            yield return StartCoroutine(ChargeWave(currentWave, chargeDuration, animateMouth: i == 0));

            currentWave?.Launch();
            StartCoroutine(BarkBlipRoutine());

            nextWave = i < sonicWaveCount - 1 ? SpawnAimedWave() : null;
        }

        bigBarkCooldownTimer = bigBarkCooldown;
        isBigBarking = false;
        isPerformingAction = false;
    }

    // 生成一顆音波,瞄準當下玩家方向再疊加隨機偏移;沒有指定 sonicWavePrefab 時回傳 null(呼叫端都用 ?. 處理)
    SonicWave SpawnAimedWave() {
        if (sonicWavePrefab == null) return null;

        Vector2 toPlayer = (Vector2)player.position - rb.position;
        float aimAngle = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg + Random.Range(-sonicWaveRandomOffsetDeg, sonicWaveRandomOffsetDeg);
        Vector2 direction = new Vector2(Mathf.Cos(aimAngle * Mathf.Deg2Rad), Mathf.Sin(aimAngle * Mathf.Deg2Rad));

        GameObject waveObj = Instantiate(sonicWavePrefab, transform.position, Quaternion.identity);
        waveObj.TryGetComponent(out SonicWave wave);
        wave?.Init(direction, bigBarkBeamGap, bigBarkBeamLength, bigBarkBeamWidth, baseAttack);
        return wave;
    }

    // 蓄力/預告 duration 秒,期間讓音波淡入;animateMouth 時額外播放原本的嘴巴開合 chatter(只有第 1 顆用到)
    IEnumerator ChargeWave(SonicWave wave, float duration, bool animateMouth) {
        float elapsed = 0f;
        float mouthTimer = 0f;
        bool mouthOpen = false;

        while (elapsed < duration) {
            float dt = Time.deltaTime;
            elapsed += dt;

            if (animateMouth) {
                mouthTimer += dt;
                if (mouthTimer >= mouthAnimDuration) {
                    mouthTimer = 0f;
                    mouthOpen = !mouthOpen;
                    animator.SetTrigger(mouthOpen ? "OpenMouth" : "CloseMouth");
                }
            }

            wave?.SetChargingVisual(elapsed / duration);
            yield return null;
        }
    }

    // 每顆音波發射瞬間的短促張嘴/閉嘴,獨立於 ChargeWave 的蓄力嘴巴動畫,跟外層迴圈同時進行不互相等待
    IEnumerator BarkBlipRoutine() {
        animator.SetTrigger("OpenMouth");
        yield return new WaitForSeconds(sonicWaveBarkCloseDelay);
        animator.SetTrigger("CloseMouth");
    }

    // 大招命中時打斷跳/咬的動作(大狗叫因為 CanBeKnockedBack 擋掉,不會被中斷到)
    public override bool TryKnockback(Vector2 direction, float distance) {
        if (!base.TryKnockback(direction, distance)) return false;

        StopAllCoroutines();
        isPerformingAction = false;
        return true;
    }

    // 通用接觸傷害:不管有沒有在放技能(咬、跳、或單純遊走時撞到),只要碰到玩家就造成傷害+微幅擊退,
    // 最多每 bodyContactDamageInterval 秒觸發一次;硬直中不會造成傷害。方位只取水平分量,避免跳躍落地時把玩家往垂直方向推開。
    // 敵人與玩家之間不再有物理碰撞(EnemyBase.Awake 已把碰撞體設為 Trigger),改用 OnTriggerStay2D 偵測接觸。
    void OnTriggerStay2D(Collider2D other) {
        if (IsDead || IsPaused || IsKnockedBack) return;
        if (bodyContactDamageTimer > 0f) return;
        if (!other.CompareTag("Player")) return;

        if (other.TryGetComponent(out IDamageable damageable)) {
            damageable.TakeDamage(baseAttack);
            bodyContactDamageTimer = bodyContactDamageInterval;

            if (other.TryGetComponent(out IKnockbackable knockbackTarget)) {
                Vector2 direction = new Vector2(other.transform.position.x - rb.position.x, 0f);
                if (direction != Vector2.zero) knockbackTarget.TryKnockback(direction, contactKnockbackDistance);
            }
        }
    }
}
