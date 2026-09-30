using System.Collections;
using UnityEngine;

// 攝影貓(1-2):地面遊走,每個施法週期跳到定高(地圖垂直中心 y=0 附近)停留施放拍照或閃光燈,再落回地面。
// 跟 JurassicCat 共用一包血量池(見 PairedBossEnemy),被暈眩時免傷失效,前/後方向的免傷程度不同(見 GetIncomingDamageMultiplier)。
public class PhotoCat : PairedBossEnemy {
    enum CastChoice { Photo, Flash }
    enum EdgeSide { Left, Right }

    [Header("音效")]
    [SerializeField] string castSfxName = "shoot"; // 拍照每顆子彈、閃光燈施放當下播放的音效

    [Header("移動 (公式:3 + 難度 * 0.4)")]
    public float moveSpeedBase = 3f;
    public float moveSpeedPerDifficulty = 0.4f;

    [Header("垂直起跳施法 (目標高度 = 地圖垂直中心 world y=0,見 LevelManager 地圖置中慣例,± 這個容許誤差)")]
    public float jumpCastYTolerance = 2f;
    public float verticalMoveSpeed = 6f; // 起跳/落地的垂直移動速度(單位/秒),用速度而不是固定時間,距離不同也能維持一致的平滑感

    [Header("地面範圍內縮 (groundMin/groundMax 是牆壁的實際位置,直接走到那邊會卡牆,水平移動要內縮這個距離)")]
    public float groundEdgeMargin = 1f;

    [Header("拍照 (橫向掃射全場)")]
    public GameObject photoBulletPrefab;
    public int photoBulletCount = 5;
    public float photoBulletFanDegrees = 15f; // 5 顆子彈的總展開角度,平均分布
    public float photoBulletInterval = 0.1f;
    public float photoBulletSpeed = 10f;
    public float photoDamagePercent = 0.5f;
    public float photoStunDuration = 1f;

    [Header("閃光燈 (原地旋轉,單次判定)")]
    public float flashRadius = 3f;
    public float flashSpinDuration = 0.8f;
    public float flashDamagePercent = 0.5f;
    public float flashStunDuration = 2f;
    [Range(0f, 1f)] public float flashChanceWhenOutOfRange = 0.25f; // 玩家不在閃光燈範圍內時,仍有這個機率選閃光燈(其餘選拍照)

    [Header("拍照方向限制 (同一側最多連續放幾次,超過要冷卻幾秒才能再選同一側)")]
    public int maxConsecutivePhotoPerSide = 3;
    public float photoSideCooldown = 5f;

    [Header("前後免傷 (面向 ±這個角度視為正面,其餘視為背後)")]
    [Range(0f, 1f)] public float frontImmunePercent = 0.5f;
    [Range(0f, 1f)] public float backImmunePercent = 1f;
    public float frontHalfAngleDegrees = 120f;

    PlayerBase player;
    SpriteRenderer spriteRenderer;

    int difficulty;
    float moveSpeed;
    bool isPerformingAction;
    Vector2 facingDirection = Vector2.right;
    float groundY; // 地面遊走的實際高度:地面帶(groundMin~groundMax)的中點,而不是最底部(那是牆壁/地圖邊界的位置)

    CastChoice pendingChoice;
    float pendingCastY;
    float pendingApproachTargetX; // 拍照專用:走位目標(最近的左右邊界),閃光燈則每幀直接追蹤玩家目前 x
    EdgeSide pendingPhotoSide;

    EdgeSide? lastPhotoSide;
    int samePhotoSideStreak;
    float leftSideCooldownTimer;
    float rightSideCooldownTimer;

    public override void ApplyDifficulty(int difficulty) {
        base.ApplyDifficulty(difficulty);
        this.difficulty = Mathf.Clamp(difficulty, 0, MaxDifficulty);
        moveSpeed = moveSpeedBase + this.difficulty * moveSpeedPerDifficulty;
    }

    protected override void Awake() {
        base.Awake();
        spriteRenderer = GetComponent<SpriteRenderer>();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.GetComponent<PlayerBase>();
    }

    protected override void Start() {
        base.Start();
        groundY = (groundMin.y + groundMax.y) * 0.5f;
        DecideNextCast();
    }

    protected override void FixedUpdate() {
        base.FixedUpdate();

        if (!IsStunned) {
            if (leftSideCooldownTimer > 0f) leftSideCooldownTimer -= Time.fixedDeltaTime;
            if (rightSideCooldownTimer > 0f) rightSideCooldownTimer -= Time.fixedDeltaTime;
        }

        if (IsDead || IsPaused || player == null || isPerformingAction || IsKnockedBack || IsStunned) return;

        float targetX = pendingChoice == CastChoice.Photo ? pendingApproachTargetX : player.transform.position.x;
        Vector2 current = rb.position;
        float nextX = Mathf.MoveTowards(current.x, targetX, moveSpeed * MoveSpeedMultiplier * Time.fixedDeltaTime);
        nextX = Mathf.Clamp(nextX, groundMin.x + groundEdgeMargin, groundMax.x - groundEdgeMargin);
        rb.MovePosition(new Vector2(nextX, groundY));

        SetFacing(nextX - current.x);

        // 走到就放,不用固定週期硬等:拍照走位是固定點,走到即可視為抵達;閃光燈是即時追蹤玩家的活動目標,
        // 永遠不會剛好「走到」同一個點,改成「已經近到閃光燈範圍內(用實際會用到的施法高度 pendingCastY 算距離,
        // 不是目前站的地面高度)」就視為抵達,避免玩家一直跑掉導致永遠放不出來。
        bool arrived = pendingChoice == CastChoice.Photo
            ? Mathf.Abs(nextX - pendingApproachTargetX) <= 0.05f
            : Vector2.Distance(new Vector2(nextX, pendingCastY), player.transform.position) <= flashRadius;

        if (arrived) StartCoroutine(CastCycleRoutine());
    }

    void SetFacing(float dx) {
        if (dx > 0.01f) { facingDirection = Vector2.right; spriteRenderer.flipX = false; }
        else if (dx < -0.01f) { facingDirection = Vector2.left; spriteRenderer.flipX = true; }
    }

    // 每個週期開始時決定要放哪個技能,以及地面走位目標。閃光燈的「保底」判定用「目前 x + 這次要跳到的高度」預判,
    // 而不是決定當下的原地位置——起跳只有垂直位移,水平位置本來就不會因為跳躍改變,見設計討論。
    void DecideNextCast() {
        pendingCastY = Random.Range(-jumpCastYTolerance, jumpCastYTolerance);

        // 玩家跟施法高度的垂直落差本身就大於閃光燈範圍時,不管水平位置怎麼追都不可能進到範圍內,
        // 這種情況直接強制拍照,不讓閃光燈進入候選,避免攝影貓卡在原地一直追、永遠放不出技能。
        float verticalGapToCastHeight = Mathf.Abs(player.transform.position.y - pendingCastY);
        bool flashReachable = verticalGapToCastHeight <= flashRadius;

        if (!flashReachable) {
            pendingChoice = CastChoice.Photo;
        } else {
            Vector2 predictedCastPos = new Vector2(rb.position.x, pendingCastY);
            bool guaranteedFlash = Vector2.Distance(predictedCastPos, player.transform.position) <= flashRadius;
            pendingChoice = guaranteedFlash || Random.value < flashChanceWhenOutOfRange ? CastChoice.Flash : CastChoice.Photo;
        }

        if (pendingChoice == CastChoice.Photo) {
            float insetMin = groundMin.x + groundEdgeMargin;
            float insetMax = groundMax.x - groundEdgeMargin;
            float distToMin = Mathf.Abs(rb.position.x - insetMin);
            float distToMax = Mathf.Abs(insetMax - rb.position.x);
            EdgeSide naturalSide = distToMin < distToMax ? EdgeSide.Left : EdgeSide.Right;

            EdgeSide chosenSide = IsSideOnCooldown(naturalSide) ? Opposite(naturalSide) : naturalSide;
            if (IsSideOnCooldown(chosenSide)) chosenSide = naturalSide; // 兩側都在冷卻的極端情況,退而求其次照放,不要卡死

            pendingPhotoSide = chosenSide;
            pendingApproachTargetX = chosenSide == EdgeSide.Left ? insetMin : insetMax;
        }
    }

    bool IsSideOnCooldown(EdgeSide side) => side == EdgeSide.Left ? leftSideCooldownTimer > 0f : rightSideCooldownTimer > 0f;
    static EdgeSide Opposite(EdgeSide side) => side == EdgeSide.Left ? EdgeSide.Right : EdgeSide.Left;

    // 同一側連續放滿 maxConsecutivePhotoPerSide 次之後,那一側進入冷卻;方向切換時連續次數自然重新起算。
    void RegisterPhotoSideUse(EdgeSide side) {
        if (lastPhotoSide == side) samePhotoSideStreak++;
        else { lastPhotoSide = side; samePhotoSideStreak = 1; }

        if (samePhotoSideStreak >= maxConsecutivePhotoPerSide) {
            if (side == EdgeSide.Left) leftSideCooldownTimer = photoSideCooldown;
            else rightSideCooldownTimer = photoSideCooldown;
            samePhotoSideStreak = 0;
        }
    }

    IEnumerator CastCycleRoutine() {
        isPerformingAction = true;

        Vector2 groundPos = rb.position;
        yield return MoveVertically(new Vector2(groundPos.x, pendingCastY));

        if (pendingChoice == CastChoice.Photo) yield return PhotoRoutine();
        else yield return FlashRoutine();

        yield return MoveVertically(new Vector2(rb.position.x, groundY));

        isPerformingAction = false;
        DecideNextCast();
    }

    // 用固定速度(而不是固定時間)移動到目標高度:垂直距離可能因為起跳高度/地面高度不同而差很多,
    // 固定時間的話距離一大就會看起來像瞬移,固定速度才能不管距離多遠都維持一致的平滑感。
    IEnumerator MoveVertically(Vector2 to) {
        while (Vector2.Distance(rb.position, to) > 0.02f) {
            rb.MovePosition(Vector2.MoveTowards(rb.position, to, verticalMoveSpeed * Time.fixedDeltaTime));
            yield return new WaitForFixedUpdate();
        }
        rb.MovePosition(to);
    }

    // 朝場地另一側橫向掃射:站在哪一側邊界,就往對側開火,5 顆子彈以扇形展開,製造縫隙讓玩家能閃過去
    IEnumerator PhotoRoutine() {
        RegisterPhotoSideUse(pendingPhotoSide);

        Vector2 origin = rb.position;
        Vector2 fireDir = pendingPhotoSide == EdgeSide.Left ? Vector2.right : Vector2.left;
        float halfSpan = photoBulletFanDegrees * 0.5f;

        for (int i = 0; i < photoBulletCount; i++) {
            float t = photoBulletCount > 1 ? i / (float)(photoBulletCount - 1) : 0.5f;
            float angle = Mathf.Lerp(-halfSpan, halfSpan, t);
            Vector3 rotatedDir = Quaternion.Euler(0f, 0f, angle) * (Vector3)fireDir;
            Vector3 targetPos = (Vector3)origin + rotatedDir;

            float damage = EnemyCritRoll.ComputeDamage(baseAttack * photoDamagePercent, difficulty, player, out bool isCrit);
            Bullet.Spawn(photoBulletPrefab, this, origin, targetPos, damage, targetTag: "Player",
                onHit: () => OnPhotoBulletHit(isCrit), speed: photoBulletSpeed);
            AudioManager.Instance.PlaySFX(castSfxName);

            // 最後一顆打完就直接結束,不要多等一次間隔才讓 CastCycleRoutine 繼續往下墜落
            if (i < photoBulletCount - 1) yield return new WaitForSeconds(photoBulletInterval);
        }
    }

    void OnPhotoBulletHit(bool isCrit) {
        if (isCrit) EnemyCritRoll.PlayCritHitSfx(); // 命中玩家的當下才播,不是發射當下(子彈可能飛出去卻沒打中)
        player.TryApplyStun(photoStunDuration);
        Partner?.OnPartnerHitPlayer();
    }

    // 原地旋轉,轉完瞬間單次判定範圍內的傷害+暈眩(不是持續性判定,見設計討論)
    IEnumerator FlashRoutine() {
        AudioManager.Instance.PlaySFX(castSfxName); // 施放當下的音效;crit_hit1 是命中判定時爆擊才會另外播放的音效,不是施法音效,兩者不衝突
        yield return new WaitForSeconds(flashSpinDuration);

        bool hitPlayer = false;
        foreach (Collider2D hit in Physics2D.OverlapCircleAll(rb.position, flashRadius)) {
            if (!hit.CompareTag("Player")) continue;

            float damage = EnemyCritRoll.ComputeDamage(baseAttack * flashDamagePercent, difficulty, player, out bool isCrit);
            player.TakeDamage(damage);
            if (isCrit) EnemyCritRoll.PlayCritHitSfx();
            player.TryApplyStun(flashStunDuration);
            hitPlayer = true;
        }

        if (hitPlayer) Partner?.OnPartnerHitPlayer();
    }

    protected override float GetIncomingDamageMultiplier() {
        bool isBehind = IsHitFromBehind();
        return 1f - (isBehind ? backImmunePercent : frontImmunePercent);
    }

    // 用玩家目前位置相對於攝影貓面向的角度,當作攻擊來源方向的判斷依據(遊戲裡的攻擊基本上都是從玩家自身位置發出的)
    bool IsHitFromBehind() {
        if (player == null) return false;
        Vector2 toPlayer = (Vector2)player.transform.position - rb.position;
        if (toPlayer == Vector2.zero) return false;
        return Vector2.Angle(facingDirection, toPlayer) > frontHalfAngleDegrees;
    }

    protected override void InterruptCurrentAction() {
        StopAllCoroutines();
        isPerformingAction = false;
    }

    // 擊退命中時比照 DogeEnemy 的慣例,順便打斷手上正在跑的施法流程
    public override bool TryKnockback(Vector2 direction, float distance) {
        if (!base.TryKnockback(direction, distance)) return false;
        InterruptCurrentAction();
        return true;
    }
}
