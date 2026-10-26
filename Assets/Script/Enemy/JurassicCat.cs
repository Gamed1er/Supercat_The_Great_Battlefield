using System.Collections;
using UnityEngine;

// 侏儸貓(1-2):地面遊走+槌擊(位於玩家正下方時觸發的垂直跳躍攻擊)+泰山隕石墜(純冷卻觸發的大招,離場後從垂直警示柱砸落,流程比照 CatHero 墜地拳)。
// 跟 PhotoCat 共用一包血量池(見 PairedBossEnemy),固定 66% 免傷,被暈眩時免傷失效。
public class JurassicCat : PairedBossEnemy {
    [Header("音效")]
    [SerializeField] string hammerSfxName = "shoot"; // 槌擊起跳時播放的音效
    [SerializeField] string meteorSfxName = "shoot"; // 泰山隕石墜蓄力時播放的音效
    [SerializeField] string meteorFallSfxName = "shaking2"; // 泰山隕石墜下墜前 meteorFallSfxLeadTime 秒播放,比照 CatHero 墜地拳
    [SerializeField] float meteorFallSfxLeadTime = 0.2f;

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

    [Header("泰山隕石墜 (流程與數值比照 CatHero 墜地拳)")]
    public float meteorCooldown = 20f;
    public float meteorChargeDuration = 0.5f;
    public float meteorLaunchHeight = 6f; // 起飛動畫的垂直位移量,飛到這個高度後隱藏本體(視為離場)
    public float meteorLaunchSpeed = 15f; // 起飛的垂直移動速度(單位/秒),用速度而不是固定時間,理由同 PhotoCat.verticalMoveSpeed
    public float meteorWarningDurationDifficulty0 = 1.5f;
    public float meteorWarningDurationDifficulty5 = 0.8f;
    public float meteorWarningTrackDuration = 0.6f; // 警示前這麼多秒追蹤玩家 x,之後鎖定不動
    public float meteorFollowSpeed = 5f; // 刻意比玩家移動速度慢,才閃得掉
    public float meteorColumnWidth = 4.5f; // 警示柱寬度 = 實際傷害判定寬度,整條柱子從地圖頂端到底部
    public float meteorFallSpeed = 20f;
    public float meteorFallEdgeMargin = 1f; // 下墜起點離地圖上邊界的距離
    public float meteorPlayerDamagePercent = 2.5f;
    public float meteorPartnerStunDuration = 10f;
    public GameObject warningColumnPrefab; // 選填,沒指定就用 PlaceholderSprite 產生一條半透明紅色柱

    [Header("特效 (全部是 SmokePuff prefab,大小/時間/淡出在 prefab 上調)")]
    public GameObject meteorPowerEffectPrefab; // 隕石墜蓄力時掛在身上,跟著起飛往上
    public GameObject groundCrackEffectPrefab; // 以下四個是落地特效,隕石墜和槌擊共用
    public GameObject mudSplashEffectPrefab;
    public GameObject shockWaveEffectPrefab;
    public GameObject groundAshEffectPrefab;
    public float landingEffectForwardOffset = 0.5f; // 落地特效往面向方向偏移,單位是侏儸貓 sprite 寬度的倍數
    public float hammerLandingEffectScale = 0.7f; // 槌擊落地用縮小版
    public float hammerLandingEffectDurationScale = 0.3f; // 槌擊落地特效的持續時間(含淡出延遲)= 大招的這個倍數

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
    float meteorWarningDuration; // 由 ApplyDifficulty 依難度算出,不直接在 Inspector 調整
    float directionCheckTimer;
    float bodyContactDamageTimer;
    int wanderDirection = 1; // +1 右, -1 左
    bool isPerformingAction;
    bool isUninterruptible; // 槌擊/隕石墜起跳中,不可被擊退/暈眩打斷
    bool isHammering; // 只有槌擊的空中階段為 true,碰撞傷害判定用
    bool wasPlayerStunnedLastFrame;
    GameObject activeWarningColumn;
    float groundY; // 地面遊走的實際高度:地面帶(groundMin~groundMax)的中點,而不是最底部(那是牆壁/地圖邊界的位置)

    protected override bool CanBeKnockedBack => !isUninterruptible;

    public override void ApplyDifficulty(int difficulty) {
        base.ApplyDifficulty(difficulty);
        this.difficulty = Mathf.Clamp(difficulty, 0, MaxDifficulty);
        hammerCooldown = hammerCooldownBase - this.difficulty * hammerCooldownPerDifficulty;
        meteorWarningDuration = Mathf.Lerp(meteorWarningDurationDifficulty0, meteorWarningDurationDifficulty5, this.difficulty / (float)MaxDifficulty);
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
        SpawnLandingEffects(start, hammerLandingEffectScale, hammerLandingEffectDurationScale);

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

    // 蓄力 -> 垂直飛出畫面隱藏 -> 全高警示柱(前段追蹤玩家 x,之後鎖定)-> 從地圖頂端沿柱子砸到地面,整段路徑都有判定
    IEnumerator MeteorRoutine() {
        isPerformingAction = true;
        isUninterruptible = true;
        AudioManager.Instance.PlaySFX(meteorSfxName);
        // 掛成子物件,起飛時跟著往上;本體隱藏(離場)時還沒淡完的話一起清掉
        GameObject powerEffect = Instantiate(meteorPowerEffectPrefab, transform);

        yield return new WaitForSeconds(meteorChargeDuration);

        // 固定速度(而不是固定時間)垂直飛出畫面,理由同 PhotoCat.MoveVertically:固定時間對這麼長的距離會看起來像瞬移
        Vector2 launchEnd = rb.position + Vector2.up * meteorLaunchHeight;
        while (Vector2.Distance(rb.position, launchEnd) > 0.02f) {
            rb.MovePosition(Vector2.MoveTowards(rb.position, launchEnd, meteorLaunchSpeed * Time.fixedDeltaTime));
            yield return new WaitForFixedUpdate();
        }

        if (powerEffect != null) Destroy(powerEffect);
        spriteRenderer.enabled = false;
        if (bodyCollider != null) bodyCollider.enabled = false;

        // 地圖以 world y=0 垂直置中,所以地圖頂端 = -groundMin.y(groundMin.y 是地圖最下方,也就是牆壁的位置,見 groundY 的說明)
        float mapTopY = -groundMin.y;
        float columnX = player.transform.position.x;
        activeWarningColumn = SpawnWarningColumn(columnX);

        float elapsed = 0f;
        bool hasPlayedFallSfx = false;
        while (elapsed < meteorWarningDuration) {
            elapsed += Time.deltaTime;
            // 音效提前在下墜前 meteorFallSfxLeadTime 秒播放,聽起來才會跟下墜對上
            if (!hasPlayedFallSfx && elapsed >= meteorWarningDuration - meteorFallSfxLeadTime) {
                hasPlayedFallSfx = true;
                AudioManager.Instance.PlaySFX(meteorFallSfxName);
            }
            if (elapsed < meteorWarningTrackDuration) {
                columnX = Mathf.MoveTowards(columnX, player.transform.position.x, meteorFollowSpeed * Time.deltaTime);
                activeWarningColumn.transform.position = new Vector2(columnX, 0f);
            }
            yield return null;
        }

        Destroy(activeWarningColumn);
        activeWarningColumn = null;

        // 從地圖頂端以固定速度掉到地面。每個物理步驟判定「上一步到這一步」掃過的那段柱子(寬度 = 警示柱寬度),
        // 落地那一步再往下延伸到地圖底部,所以整條警示柱範圍內都會被打到,不會只判定落地點。玩家/攝影貓各最多判定一次。
        Vector2 fallStart = new Vector2(columnX, mapTopY - meteorFallEdgeMargin);
        Vector2 landingPos = new Vector2(columnX, groundY);
        rb.position = fallStart;
        transform.position = fallStart;
        if (bodyCollider != null) bodyCollider.enabled = true;
        spriteRenderer.enabled = true;

        bool hasHitPlayer = false;
        bool hitPartner = false;
        Vector2 previous = fallStart;
        while (true) {
            Vector2 next = Vector2.MoveTowards(previous, landingPos, meteorFallSpeed * Time.fixedDeltaTime);
            rb.MovePosition(next);

            bool landed = next == landingPos;
            float segmentBottom = landed ? groundMin.y : next.y;
            ResolveMeteorSegment(columnX, segmentBottom, previous.y, ref hasHitPlayer, ref hitPartner);

            previous = next;
            yield return new WaitForFixedUpdate();
            if (landed) break;
        }
        SpawnLandingEffects(landingPos, 1f, 1f);

        // 落地後先解除不可打斷狀態,再處理攝影貓命中——命中攝影貓時要對自己套用暈眩(TryApplyStun 沿用 CanBeKnockedBack
        // 這道免疫閘門),isUninterruptible 還是 true 的話會擋住自己的暈眩,落地當下就該恢復成可被中斷的狀態。
        isUninterruptible = false;
        isPerformingAction = false;
        meteorCooldownTimer = meteorCooldown;

        // 自己的暈眩放最後處理:TryApplyStun 第一次觸發時會呼叫 InterruptCurrentAction(內含 StopAllCoroutines),
        // 而這裡本身就在 MeteorRoutine 裡,先把冷卻重置等收尾做完再觸發,避免依賴呼叫順序造成後面的處理被跳過。
        if (hitPartner) {
            Partner.TryApplyStun(meteorPartnerStunDuration);
            TryApplyStun(meteorPartnerStunDuration);
        }
    }

    void ResolveMeteorSegment(float x, float bottomY, float topY, ref bool hasHitPlayer, ref bool hitPartner) {
        Vector2 center = new Vector2(x, (bottomY + topY) * 0.5f);
        Vector2 size = new Vector2(meteorColumnWidth, Mathf.Max(topY - bottomY, 0.01f));

        foreach (Collider2D hit in Physics2D.OverlapBoxAll(center, size, 0f)) {
            if (!hasHitPlayer && hit.CompareTag("Player") && hit.TryGetComponent(out PlayerBase target)) {
                hasHitPlayer = true;
                float damage = EnemyCritRoll.ComputeDamage(baseAttack * meteorPlayerDamagePercent, difficulty, target, out bool isCrit);
                target.TakeDamage(damage);
                if (isCrit) EnemyCritRoll.PlayCritHitSfx();
            } else if (Partner != null && hit.gameObject == Partner.gameObject) {
                hitPartner = true; // 命中攝影貓:雙方互相暈眩,不造成傷害(這條規則目前只寫死對攝影貓生效,見設計討論)
            }
        }
    }

    // 落地特效生在腳底高度(sprite 的下緣)、往面向方向偏移 landingEffectForwardOffset 個身寬的位置,
    // 各 sprite 的 pivot 已經在匯入設定調好對齊方式(裂痕/煙塵以中心、泥漿以底部對齊);
    // 用「目前位置到 sprite 下緣的距離」換算,因為呼叫當下 rb.MovePosition 可能還沒套用到 transform
    void SpawnLandingEffects(Vector2 position, float scale, float durationScale) {
        Bounds body = spriteRenderer.bounds;
        float footOffset = transform.position.y - body.min.y;
        float facingSign = spriteRenderer.flipX ? -1f : 1f;
        Vector2 spawnPos = new Vector2(position.x + facingSign * body.size.x * landingEffectForwardOffset, position.y - footOffset);

        foreach (GameObject prefab in new[] { groundCrackEffectPrefab, mudSplashEffectPrefab, shockWaveEffectPrefab, groundAshEffectPrefab }) {
            GameObject effect = Instantiate(prefab, spawnPos, Quaternion.identity);
            effect.transform.localScale *= scale; // SmokePuff 在 Start 才記起始大小,這裡改得到
            SmokePuff puff = effect.GetComponent<SmokePuff>();
            puff.lifeTime *= durationScale; // lifeTime/fadeDelay 都是 Update 才讀,生成後改得到
            puff.fadeDelay *= durationScale;
        }
    }

    // 從地圖底部到頂端的全高柱子,中心在 y=0(地圖垂直置中)
    GameObject SpawnWarningColumn(float x) {
        Vector2 position = new Vector2(x, 0f);
        if (warningColumnPrefab != null) return Instantiate(warningColumnPrefab, position, Quaternion.identity);

        var go = new GameObject("MeteorWarningColumn");
        go.transform.position = position;
        go.transform.localScale = new Vector3(meteorColumnWidth, -groundMin.y * 2f, 1f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = PlaceholderSprite.Get(new Color(1f, 0f, 0f, 0.35f));
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

    // 共用血量池見底、被夥伴強制陣亡時,順便清掉可能還留在場上的隕石墜警示柱,避免孤兒物件殘留
    protected override void Die() {
        if (activeWarningColumn != null) {
            Destroy(activeWarningColumn);
            activeWarningColumn = null;
        }
        base.Die();
    }
}
