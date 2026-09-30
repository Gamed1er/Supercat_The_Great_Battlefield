using System.Collections;
using UnityEngine;

// 侏儸貓(1-2):地面遊走+槌擊(位於玩家正下方時觸發的垂直跳躍攻擊)+泰山隕石墜(純冷卻觸發的大招,離場蓄力後從警告區域砸落)。
// 跟 PhotoCat 共用一包血量池(見 PairedBossEnemy),固定 66% 免傷,被暈眩時免傷失效。
public class JurassicCat : PairedBossEnemy {
    [Header("音效")]
    [SerializeField] string hammerSfxName = "shoot"; // 槌擊起跳時播放的音效
    [SerializeField] string meteorSfxName = "shoot"; // 泰山隕石墜蓄力時播放的音效

    [Header("移動 (固定值,不隨難度變化)")]
    public float moveSpeed = 9f;
    public float directionCheckInterval = 1f;

    [Header("槌擊 (垂直跳躍,邏輯同 DogeEnemy 的跳躍但沒有水平位移)")]
    public float hammerXAlignTolerance = 0.5f;
    public float hammerWindupDuration = 0.3f;
    public float hammerAirDuration = 0.6f;
    public float hammerJumpHeight = 2f;
    const float maxHammerPeakYBelowMaxDifficulty = 3.5f; // 比照 DogeEnemy 的跳躍上限慣例,難度 5 不受限
    public float hammerCooldownBase = 3f;
    public float hammerCooldownPerDifficulty = 0.2f;
    public float hammerDamagePercent = 1f;

    [Header("泰山隕石墜")]
    public float meteorCooldown = 20f;
    public float meteorChargeDuration = 0.5f;
    public float meteorLaunchHeight = 6f; // 起飛動畫的垂直位移量,飛到這個高度後隱藏本體(視為離場)
    public float meteorLaunchSpeed = 12f; // 起飛的垂直移動速度(單位/秒),用速度而不是固定時間,理由同 PhotoCat.verticalMoveSpeed
    public float meteorWarningDuration = 5f;
    public float meteorImpactRadius = 2.5f;
    public float meteorWarningTopMargin = 0.5f; // 警示區固定顯示在地圖最上方,往下留一點邊界(見 MeteorRoutine)
    public float meteorFollowSpeed = 4f; // 警示區追蹤玩家水平位置的速度,故意比侏儸貓自己的移動速度慢很多,才閃得掉
    public float meteorSlowdownStartRemaining = 2f; // 倒數剩這麼多秒時開始線性減速
    public float meteorFreezeRemaining = 0.8f; // 倒數剩這麼多秒時完全鎖定不動
    public float meteorFallDuration = 0.2f; // 從最上方掉到地面的視覺位移時間
    public float meteorPlayerDamagePercent = 2.5f;
    public float meteorPartnerStunDuration = 10f;
    public GameObject warningZonePrefab; // 選填,沒指定就用 PlaceholderSprite 產生一個半透明紅色圓

    [Header("免傷")]
    [Range(0f, 1f)] public float damageImmunePercent = 0.66f;

    [Header("槌擊接觸傷害 (只在槌擊跳躍期間有效,見 isHammering)")]
    public float bodyContactDamageInterval = 0.5f;

    PlayerBase player;
    SpriteRenderer spriteRenderer;
    Collider2D bodyCollider;

    int difficulty;
    float hammerCooldown;
    float hammerCooldownTimer;
    float meteorCooldownTimer;
    float directionCheckTimer;
    float bodyContactDamageTimer;
    int wanderDirection = 1; // +1 右, -1 左
    bool isPerformingAction;
    bool isUninterruptible; // 槌擊/隕石墜起跳中,不可被擊退/暈眩打斷
    bool isHammering; // 只有槌擊的空中階段為 true,碰撞傷害判定用
    bool wasPlayerStunnedLastFrame;
    GameObject activeWarningZone;
    float groundY; // 地面遊走的實際高度:地面帶(groundMin~groundMax)的中點,而不是最底部(那是牆壁/地圖邊界的位置)

    protected override bool CanBeKnockedBack => !isUninterruptible;

    public override void ApplyDifficulty(int difficulty) {
        base.ApplyDifficulty(difficulty);
        this.difficulty = Mathf.Clamp(difficulty, 0, MaxDifficulty);
        hammerCooldown = hammerCooldownBase - this.difficulty * hammerCooldownPerDifficulty;
    }

    protected override void Awake() {
        base.Awake();
        spriteRenderer = GetComponent<SpriteRenderer>();
        bodyCollider = GetComponent<Collider2D>();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.GetComponent<PlayerBase>();
    }

    protected override void Start() {
        base.Start();
        groundY = (groundMin.y + groundMax.y) * 0.5f;
        hammerCooldownTimer = 0f; // 一開始就能放,只要位置條件滿足
        meteorCooldownTimer = meteorCooldown;
        directionCheckTimer = directionCheckInterval;
    }

    void Update() {
        if (IsDead || IsPaused || player == null) return;
        // 暈眩時動畫暫停播放已經在 EnemyBase.FixedUpdate 統一處理,這裡不用重複做

        // 冷卻不管有沒有在放技能/硬直都持續倒數,眩暈時整個暫停,麻痺時變慢,比照 DogeEnemy 大狗叫冷卻的慣例
        if (!IsStunned) {
            if (hammerCooldownTimer > 0f) hammerCooldownTimer -= Time.deltaTime / CooldownTimeMultiplier;
            if (meteorCooldownTimer > 0f) meteorCooldownTimer -= Time.deltaTime / CooldownTimeMultiplier;
        }

        bool playerStunnedNow = player.IsStunned;
        if (playerStunnedNow && !wasPlayerStunnedLastFrame && !isPerformingAction) UpdateWanderDirection();
        wasPlayerStunnedLastFrame = playerStunnedNow;

        if (isPerformingAction || IsKnockedBack || IsStunned) return;

        if (meteorCooldownTimer <= 0f) {
            StartCoroutine(MeteorRoutine());
            return;
        }

        if (hammerCooldownTimer <= 0f && Mathf.Abs(rb.position.x - player.transform.position.x) < hammerXAlignTolerance) {
            StartCoroutine(HammerRoutine());
            return;
        }

        directionCheckTimer -= Time.deltaTime;
        if (directionCheckTimer <= 0f) {
            directionCheckTimer = directionCheckInterval;
            UpdateWanderDirection();
        }
    }

    // 玩家方向與目前遊走方向相反時翻向,碰牆時另外在 FixedUpdate 處理
    void UpdateWanderDirection() {
        float toPlayer = player.transform.position.x - rb.position.x;
        if (Mathf.Abs(toPlayer) <= 0.01f) return;
        wanderDirection = toPlayer > 0f ? 1 : -1;
    }

    protected override void FixedUpdate() {
        base.FixedUpdate();
        if (bodyContactDamageTimer > 0f) bodyContactDamageTimer -= Time.fixedDeltaTime;

        if (IsDead || IsPaused || isPerformingAction || IsKnockedBack || IsStunned) return;

        Vector2 current = rb.position;
        float nextX = current.x + wanderDirection * moveSpeed * MoveSpeedMultiplier * Time.fixedDeltaTime;

        if (nextX <= groundMin.x) { nextX = groundMin.x; wanderDirection = 1; }
        else if (nextX >= groundMax.x) { nextX = groundMax.x; wanderDirection = -1; }

        rb.MovePosition(new Vector2(nextX, groundY));
        SetFacing(wanderDirection);
    }

    void SetFacing(float dx) {
        if (dx > 0.01f) spriteRenderer.flipX = false;
        else if (dx < 0f) spriteRenderer.flipX = true;
    }

    // 純垂直跳躍,高度公式沿用 DogeEnemy.JumpRoutine 的邏輯(拋物線+難度上限),但 x 全程固定不變
    IEnumerator HammerRoutine() {
        isPerformingAction = true;
        isUninterruptible = true;
        AudioManager.Instance.PlaySFX(hammerSfxName);

        yield return new WaitForSeconds(hammerWindupDuration);

        isHammering = true;
        Vector2 start = rb.position;
        float peakY = Mathf.Max(start.y + hammerJumpHeight, player.transform.position.y + 1f);
        if (difficulty < MaxDifficulty) peakY = Mathf.Min(peakY, maxHammerPeakYBelowMaxDifficulty);
        float peakHeight = peakY - start.y;

        float elapsed = 0f;
        while (elapsed < hammerAirDuration) {
            elapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(elapsed / hammerAirDuration);
            float y = start.y + peakHeight * 4f * t * (1f - t);
            rb.MovePosition(new Vector2(start.x, y));
            yield return new WaitForFixedUpdate();
        }

        isHammering = false;
        rb.MovePosition(start);

        isUninterruptible = false;
        isPerformingAction = false;
        hammerCooldownTimer = hammerCooldown;
    }

    // 只在槌擊空中階段有效的接觸傷害(見 isHammering);侏儸貓平時遊走不會另外造成接觸傷害
    void OnTriggerStay2D(Collider2D other) {
        if (!isHammering || IsDead || IsPaused || IsKnockedBack || IsStunned) return;
        if (bodyContactDamageTimer > 0f) return;
        if (!other.CompareTag("Player")) return;
        if (!other.TryGetComponent(out PlayerBase target)) return;

        float damage = EnemyCritRoll.ComputeDamage(baseAttack * hammerDamagePercent, difficulty, target, out bool isCrit);
        target.TakeDamage(damage);
        if (isCrit) EnemyCritRoll.PlayCritHitSfx();
        bodyContactDamageTimer = bodyContactDamageInterval;
    }

    // 蓄力 0.5 秒 -> 垂直飛出畫面隱藏 -> 警告區域持續追蹤玩家 5 秒(後段減速鎖定)-> 砸落判定
    IEnumerator MeteorRoutine() {
        isPerformingAction = true;
        isUninterruptible = true;
        AudioManager.Instance.PlaySFX(meteorSfxName);

        yield return new WaitForSeconds(meteorChargeDuration);

        // 固定速度(而不是固定時間)垂直飛出畫面,理由同 PhotoCat.MoveVertically:固定時間對這麼長的距離會看起來像瞬移
        Vector2 launchEnd = rb.position + Vector2.up * meteorLaunchHeight;
        while (Vector2.Distance(rb.position, launchEnd) > 0.02f) {
            rb.MovePosition(Vector2.MoveTowards(rb.position, launchEnd, meteorLaunchSpeed * Time.fixedDeltaTime));
            yield return new WaitForFixedUpdate();
        }

        spriteRenderer.enabled = false;
        if (bodyCollider != null) bodyCollider.enabled = false;

        // 警示區固定顯示在地圖最上方(只追蹤玩家的水平位置),不會疊在角色身上——地圖以 world y=0 垂直置中,
        // 所以「最上方」= -groundMin.y(groundMin.y 是地圖最下方,也就是牆壁的位置,見 groundY 的說明)
        float warningY = -groundMin.y - meteorWarningTopMargin;
        float warningX = player.transform.position.x;
        activeWarningZone = SpawnWarningZone(new Vector2(warningX, warningY));

        float remaining = meteorWarningDuration;
        while (remaining > 0f) {
            remaining -= Time.deltaTime;

            if (remaining > meteorFreezeRemaining) {
                float speed = remaining > meteorSlowdownStartRemaining
                    ? meteorFollowSpeed
                    : Mathf.Lerp(0f, meteorFollowSpeed, (remaining - meteorFreezeRemaining) / (meteorSlowdownStartRemaining - meteorFreezeRemaining));
                warningX = Mathf.MoveTowards(warningX, player.transform.position.x, speed * Time.deltaTime);
                activeWarningZone.transform.position = new Vector2(warningX, warningY);
            }

            yield return null;
        }

        Destroy(activeWarningZone);
        activeWarningZone = null;

        // 從警示區的高度(最上方)掉到地面,花 meteorFallDuration 秒,不要瞬間出現在地面上
        Vector2 landingPos = new Vector2(warningX, groundY);
        Vector2 fallStart = new Vector2(warningX, warningY);
        rb.MovePosition(fallStart);
        if (bodyCollider != null) bodyCollider.enabled = true;
        spriteRenderer.enabled = true;

        float fallElapsed = 0f;
        while (fallElapsed < meteorFallDuration) {
            fallElapsed += Time.fixedDeltaTime;
            rb.MovePosition(Vector2.Lerp(fallStart, landingPos, Mathf.Clamp01(fallElapsed / meteorFallDuration)));
            yield return new WaitForFixedUpdate();
        }
        rb.MovePosition(landingPos);

        // 落地後先解除不可打斷狀態,再判定命中——命中攝影貓時要對自己套用暈眩(TryApplyStun 沿用 CanBeKnockedBack
        // 這道免疫閘門),isUninterruptible 還是 true 的話會擋住自己的暈眩,落地當下就該恢復成可被中斷的狀態。
        isUninterruptible = false;
        ResolveMeteorImpact(landingPos);

        isPerformingAction = false;
        meteorCooldownTimer = meteorCooldown;
    }

    void ResolveMeteorImpact(Vector2 impactPos) {
        bool hitPartner = false;

        foreach (Collider2D hit in Physics2D.OverlapCircleAll(impactPos, meteorImpactRadius)) {
            if (hit.CompareTag("Player") && hit.TryGetComponent(out PlayerBase target)) {
                float damage = EnemyCritRoll.ComputeDamage(baseAttack * meteorPlayerDamagePercent, difficulty, target, out bool isCrit);
                target.TakeDamage(damage);
                if (isCrit) EnemyCritRoll.PlayCritHitSfx();
            } else if (Partner != null && hit.gameObject == Partner.gameObject) {
                hitPartner = true; // 命中攝影貓:雙方互相暈眩,不造成傷害(這條規則目前只寫死對攝影貓生效,見設計討論)
            }
        }

        // 自己的暈眩放最後處理:TryApplyStun 第一次觸發時會呼叫 InterruptCurrentAction(內含 StopAllCoroutines),
        // 而這個方法本身就是從 MeteorRoutine 呼叫過來的,先把玩家傷害這種跟迴圈本身有關的處理做完,再觸發這個,
        // 避免依賴呼叫順序造成後面的判定被跳過。
        if (hitPartner) {
            Partner.TryApplyStun(meteorPartnerStunDuration);
            TryApplyStun(meteorPartnerStunDuration);
        }
    }

    GameObject SpawnWarningZone(Vector2 position) {
        if (warningZonePrefab != null) return Instantiate(warningZonePrefab, position, Quaternion.identity);

        var go = new GameObject("MeteorWarningZone");
        go.transform.position = position;
        go.transform.localScale = Vector3.one * (meteorImpactRadius * 2f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = PlaceholderSprite.Get(new Color(1f, 0f, 0f, 0.4f));
        sr.sortingOrder = 5;
        return go;
    }

    protected override float GetIncomingDamageMultiplier() => 1f - damageImmunePercent;

    // 攝影貓打中玩家時的通知:清空槌擊冷卻(隊伍連段機制,見設計討論)
    public override void OnPartnerHitPlayer() {
        hammerCooldownTimer = 0f;
    }

    protected override void InterruptCurrentAction() {
        StopAllCoroutines();
        isPerformingAction = false;
        isUninterruptible = false;
        isHammering = false;
    }

    // 擊退命中時比照 DogeEnemy 的慣例,順便打斷手上正在跑的行為(遊走階段才可能發生,槌擊/隕石墜期間 CanBeKnockedBack 已經擋住)
    public override bool TryKnockback(Vector2 direction, float distance) {
        if (!base.TryKnockback(direction, distance)) return false;
        InterruptCurrentAction();
        return true;
    }

    // 共用血量池見底、被夥伴強制陣亡時,順便清掉可能還留在場上的隕石墜警告區域,避免孤兒物件殘留
    protected override void Die() {
        if (activeWarningZone != null) {
            Destroy(activeWarningZone);
            activeWarningZone = null;
        }
        base.Die();
    }
}
