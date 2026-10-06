using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 貓俠(1-3):無重力關卡(沒有地板概念),閒置時原地漂浮。每個技能放完後閒置 (3 - 難度 * 0.4) 秒,再從光彈/墜地拳隨機挑一招。
// 血量 ≤ 60% 進入二階段:等當前技能放完立刻放超級霰彈(帶護盾,射到護盾被打破為止),之後每 35 秒強制放一次。
// 另外自己管理召喚物(滾石/飛碟,見 CatHeroSummon),召喚物不經過 LevelManager,不算進怪池血條/通關判定。
// 所有傷害都不擲爆擊(跟 PhotoCat/JurassicCat 不同,見設計討論)。
public class CatHero : EnemyBase {
    enum Skill { LightBullet, FallPunch, SuperShotgun }

    [Header("佔位圖 (還沒有美術時,SpriteRenderer 沒指定 sprite 就用這個顏色的方塊)")]
    [SerializeField] Color placeholderColor = new Color(0.9f, 0.75f, 0.2f);

    [Header("音效")]
    [SerializeField] string shootSfxName = "shoot"; // 光彈/超級霰彈每次開火
    [SerializeField] string fallPunchSfxName = "shaking2"; // 墜地拳下墜前 fallSfxLeadTime 秒播放
    [SerializeField] float fallSfxLeadTime = 0.2f;
    [SerializeField] string explosionSfxName = "death"; // 飛碟爆炸
    [SerializeField] string shieldHitSfxName = "hurt_shield"; // 護盾被攻擊

    [Header("閒置 (公式:base - 難度 * perDifficulty 秒,技能放完才開始計時)")]
    public float idleIntervalBase = 3f;
    public float idleIntervalPerDifficulty = 0.4f;
    public float idleBobAmplitude = 0.25f;
    public float idleBobFrequency = 0.8f; // 每秒上下來回幾次(Hz)

    [Header("移動 / 站位 (站位帶:離左右牆 insetMin~insetMax,y 在地圖上下各內縮 yInset 的範圍內隨機,不站中間)")]
    public float flySpeed = 8f;
    public float stationWallInsetMin = 1.5f;
    public float stationWallInsetMax = 4f;
    public float stationYInset = 1.5f;

    [Header("貓俠光彈")]
    public GameObject bulletPrefab;
    public int lightBulletCount = 15;
    public float lightBulletInterval = 0.1f;
    public float lightBulletRecoilDistance = 0.12f; // 每次開火往射擊方向抖出去的距離,模擬出拳/發射的動作
    public float lightBulletRecoilDuration = 0.08f; // 抖出去再回到原位的總時間,要小於 lightBulletInterval 才不會跟下一發疊在一起
    public float bulletSpeed = 15f;
    public float bulletDamagePercent = 1f;

    [Header("墜地拳 (比照 JurassicCat 泰山隕石墜:往上飛出 -> 隱藏 -> 柱狀警示 -> 從地圖頂端垂直下墜穿出畫面底部 -> 飛回施放前的位置)")]
    public float vanishLaunchHeight = 4f;
    public float vanishLaunchSpeed = 15f;
    // 警示持續時間:難度 0 / 難度 5 的秒數,中間難度用等差數列內插(見 ApplyDifficulty,比照 DogeEnemy 出招冷卻的做法)
    public float warningDurationDifficulty0 = 1.5f;
    public float warningDurationDifficulty5 = 0.8f;
    public float warningTrackDuration = 0.6f; // 警示前這麼多秒追蹤玩家 x,之後鎖定不動
    public float warningFollowSpeed = 4f; // 刻意比玩家移動速度慢,才閃得掉
    public float warningColumnWidth = 1.4f;
    public GameObject warningColumnPrefab; // 選填,沒指定就用 PlaceholderSprite 產生一條半透明紅色柱
    public float fallSpeed = 30f;
    public float fallEdgeMargin = 1f; // 下墜起點離地圖上邊界的距離
    public float fallExitDistance = 2f; // 下墜終點在地圖底部再往下這麼遠(畫面外),鏡頭夾在地圖範圍內,所以地圖底部 = 畫面底部
    public float fallDamagePercent = 2.25f;
    public float fallKnockbackDistance = 1.5f;

    [Header("飛碟爆炸 (墜地拳下墜途中碰到飛碟時觸發)")]
    public float explosionRadius = 4f;
    public float explosionDamagePercent = 1f;
    public float explosionKnockbackDistance = 1.5f;
    public int explosionRockCount = 6;
    public GameObject explosionEffectPrefab; // 選填,沒指定就用 PlaceholderSprite 閃一下

    [Header("二階段 / 超級霰彈")]
    [Range(0f, 1f)] public float phase2HealthPercent = 0.6f;
    public float superShotgunInterval = 35f; // 二階段獨立計時器,超級霰彈結束(護盾被破)時重設
    [Range(0f, 1f)] public float shieldPercent = 0.15f; // 護盾量 = 套用難度後的 MaxHealth × 這個比例
    public int shotgunBulletsPerRound = 3;
    public int shotgunBulletsPerRoundMaxDifficulty = 4;
    public float shotgunStartInterval = 0.5f;
    public float shotgunIntervalDecrement = 0.03f;
    public float shotgunMinInterval = 0.2f;
    public float shieldBreakStunDuration = 5f;

    [Header("召喚:滾石 (開戰 firstDelay 秒後,每 min ~ (maxBase - 難度 * maxPerDifficulty) 秒召喚 countMin~countMax 顆)")]
    public RollingRock rockPrefab;
    public float rockFirstDelay = 10f;
    public float rockIntervalMin = 5f;
    public float rockIntervalMaxBase = 15f;
    public float rockIntervalMaxPerDifficulty = 2f;
    public int rockCountMin = 2;
    public int rockCountMax = 3;
    public float rockSpeed = 6f;
    public float rockAngleJitterDegrees = 10f; // 一般召喚的滾石不是純水平,移動方向在水平 ± 這個角度內隨機(飛碟爆炸的放射狀滾石不套用)

    [Header("召喚:飛碟 (開戰 firstDelay 秒後,每 min ~ (maxBase - 難度 * maxPerDifficulty) 秒召喚 1 台,場上滿 maxAlive 台就跳過)")]
    public Ufo ufoPrefab;
    public float ufoFirstDelay = 15f;
    public float ufoIntervalMin = 10f;
    public float ufoIntervalMaxBase = 15f;
    public float ufoIntervalMaxPerDifficulty = 1f;
    public int ufoMaxAlive = 2;
    public float ufoSpeed = 3f;

    [Header("召喚物生成位置 (離左右牆的距離要比各自的 wallMargin 大,離上下邊界 yInset)")]
    public float summonSpawnWallInset = 1f;
    public float summonSpawnYInset = 1f;

    PlayerBase player;
    SpriteRenderer spriteRenderer;
    CircleCollider2D bodyCollider;

    int difficulty;
    Vector2 mapMin; // 地圖左下角(= 牆的位置)
    Vector2 mapMax; // 地圖右上角

    bool isPerformingAction;
    bool isUninterruptible; // 墜地拳全程(含往上飛/隱藏/下墜)、超級霰彈全程(含移動到中央)
    bool isUntargetable; // 墜地拳隱藏 + 下墜期間不可被攻擊
    bool isShotgunning;
    bool hasEnteredPhase2;
    float idleTimer;
    float phase2Timer;
    float warningDuration; // 由 ApplyDifficulty 依難度算出,不直接在 Inspector 調整
    float idleBobTime;
    float shield;
    float shieldMax;
    GameObject activeWarningColumn;

    float rockTimer;
    float ufoTimer;
    readonly List<CatHeroSummon> summons = new List<CatHeroSummon>();

    bool IsPhase2 => health <= MaxHealth * phase2HealthPercent;
    float IdleInterval => idleIntervalBase - difficulty * idleIntervalPerDifficulty;
    protected override bool CanBeKnockedBack => !isUninterruptible;

    // 給護盾 UI 讀取用(UIManager 透過 LevelManager.EnemyGroupShieldRatio 加總)
    public override float Shield => shield;
    public float ShieldMax => shieldMax;

    public override void ApplyDifficulty(int difficulty) {
        base.ApplyDifficulty(difficulty);
        this.difficulty = Mathf.Clamp(difficulty, 0, MaxDifficulty);
        warningDuration = Mathf.Lerp(warningDurationDifficulty0, warningDurationDifficulty5, this.difficulty / (float)MaxDifficulty);
    }

    // 1-3 是無重力關卡,沒有地板,這裡只用來拿整張地圖的範圍:地圖以 world (0,0) 置中(見 LevelManager),
    // groundMin 是地圖左下角,所以右上角 = (groundMax.x, -groundMin.y),跟 JurassicCat 算地圖頂端的方式一樣
    public override void SetGroundBounds(Vector2 min, Vector2 max) {
        base.SetGroundBounds(min, max);
        mapMin = min;
        mapMax = new Vector2(max.x, -min.y);
    }

    protected override void Awake() {
        base.Awake();
        spriteRenderer = GetComponent<SpriteRenderer>();
        bodyCollider = GetComponent<CircleCollider2D>();
        if (spriteRenderer.sprite == null) spriteRenderer.sprite = PlaceholderSprite.Get(placeholderColor);

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.GetComponent<PlayerBase>();
    }

    protected override void Start() {
        base.Start();
        idleTimer = IdleInterval;
        rockTimer = rockFirstDelay;
        ufoTimer = ufoFirstDelay;
    }

    void Update() {
        summons.RemoveAll(s => s == null);
        foreach (CatHeroSummon summon in summons) summon.SetPaused(IsPaused); // 戰鬥結算暫停 AI 時,召喚物跟著貓俠一起停

        if (IsDead || IsPaused || player == null) return;

        // 計時器不管有沒有在放技能/硬直都持續倒數,只在眩暈時暫停,麻痺時變慢(比照 DogeEnemy 大狗叫冷卻的慣例)
        if (!IsStunned) {
            float dt = Time.deltaTime / CooldownTimeMultiplier;
            if (hasEnteredPhase2 && !isShotgunning) phase2Timer -= dt;
            TickSummonTimers(dt);
        }

        if (isPerformingAction || IsKnockedBack || IsStunned) return;

        // 剛跌破二階段門檻:等當前技能放完(上面 isPerformingAction 已經擋住)後立刻放超級霰彈,跳過閒置間隔
        if (!hasEnteredPhase2 && IsPhase2) {
            hasEnteredPhase2 = true;
            StartSkill(Skill.SuperShotgun);
            return;
        }

        idleTimer -= Time.deltaTime / CooldownTimeMultiplier;
        if (idleTimer > 0f) return;

        if (hasEnteredPhase2 && phase2Timer <= 0f) StartSkill(Skill.SuperShotgun);
        else StartSkill(Random.value < 0.5f ? Skill.LightBullet : Skill.FallPunch);
    }

    void StartSkill(Skill skill) {
        switch (skill) {
            case Skill.LightBullet: StartCoroutine(LightBulletRoutine()); break;
            case Skill.FallPunch: StartCoroutine(FallPunchRoutine()); break;
            case Skill.SuperShotgun: StartCoroutine(SuperShotgunRoutine()); break;
        }
    }

    // 技能正常放完:回到閒置,重新開始計時
    void EndSkill() {
        isPerformingAction = false;
        idleTimer = IdleInterval;
    }

    protected override void FixedUpdate() {
        base.FixedUpdate();

        if (IsDead || IsPaused || player == null || isPerformingAction || IsKnockedBack || IsStunned) return;

        // 閒置原地漂浮:每幀只套用正弦波的「增量」,不綁定固定錨點,被擊退到別的位置後也能從新位置接著漂
        float previous = Mathf.Sin(idleBobTime * idleBobFrequency * 2f * Mathf.PI);
        idleBobTime += Time.fixedDeltaTime;
        float current = Mathf.Sin(idleBobTime * idleBobFrequency * 2f * Mathf.PI);
        rb.MovePosition(rb.position + Vector2.up * (current - previous) * idleBobAmplitude);

        FacePlayer();
    }

    void FacePlayer() => SetFacing(player.transform.position.x - rb.position.x);

    void SetFacing(float dx) {
        if (dx > 0.01f) spriteRenderer.flipX = false;
        else if (dx < -0.01f) spriteRenderer.flipX = true;
    }

    void SetAnimTrigger(string trigger) {
        if (animator != null) animator.SetTrigger(trigger); // 佔位 prefab 還沒有 Animator,見 EnemyBase.animator
    }

    // 左右兩側隨機站位,不站中間
    Vector2 PickSideStation() {
        bool left = Random.value < 0.5f;
        float inset = Random.Range(stationWallInsetMin, stationWallInsetMax);
        float x = left ? mapMin.x + inset : mapMax.x - inset;
        float y = Random.Range(mapMin.y + stationYInset, mapMax.y - stationYInset);
        return new Vector2(x, y);
    }

    // 等速飛行到目標點;可不可以被打斷由呼叫端的 isUninterruptible 決定
    IEnumerator FlyTo(Vector2 target) {
        while (Vector2.Distance(rb.position, target) > 0.05f) {
            Vector2 next = Vector2.MoveTowards(rb.position, target, flySpeed * MoveSpeedMultiplier * Time.fixedDeltaTime);
            SetFacing(next.x - rb.position.x);
            rb.MovePosition(next);
            yield return new WaitForFixedUpdate();
        }
        rb.MovePosition(target);
    }

    // ---------- 貓俠光彈 ----------

    IEnumerator LightBulletRoutine() {
        isPerformingAction = true;

        yield return FlyTo(PickSideStation());

        SetAnimTrigger("Attack");
        Vector2 restPosition = rb.position; // 開火期間的基準位置:子彈都從這裡射出,抖動結束也回到這裡
        for (int i = 0; i < lightBulletCount; i++) {
            FacePlayer();
            // 每顆都重新瞄準玩家當下的位置,壓迫感比較好(見設計討論)
            Vector2 targetPos = player.transform.position;
            Bullet.Spawn(bulletPrefab, this, restPosition, targetPos, baseAttack * bulletDamagePercent,
                targetTag: "Player", speed: bulletSpeed);
            AudioManager.Instance.PlaySFX(shootSfxName);

            // 最後一顆只等抖動播完就結束,不多等一次間隔
            float wait = i < lightBulletCount - 1 ? lightBulletInterval : lightBulletRecoilDuration;
            yield return RecoilRoutine(restPosition, (targetPos - restPosition).normalized, wait);
        }

        EndSkill();
    }

    // 開火抖動:往射擊方向推出 lightBulletRecoilDistance 再拉回原位(半個正弦波),總共佔用 totalDuration 秒,
    // 抖動本身只花 lightBulletRecoilDuration 秒,剩下的時間停在原位等下一發
    IEnumerator RecoilRoutine(Vector2 restPosition, Vector2 direction, float totalDuration) {
        float elapsed = 0f;
        while (elapsed < totalDuration) {
            elapsed += Time.fixedDeltaTime;
            float t = lightBulletRecoilDuration > 0f ? Mathf.Clamp01(elapsed / lightBulletRecoilDuration) : 1f;
            rb.MovePosition(restPosition + direction * Mathf.Sin(t * Mathf.PI) * lightBulletRecoilDistance);
            yield return new WaitForFixedUpdate();
        }
        rb.MovePosition(restPosition);
    }

    // ---------- 墜地拳 ----------

    IEnumerator FallPunchRoutine() {
        isPerformingAction = true;
        isUninterruptible = true;
        SetAnimTrigger("Vanish");

        Vector2 originalPosition = rb.position; // 下墜穿出畫面後飛回這裡

        Vector2 launchEnd = rb.position + Vector2.up * vanishLaunchHeight;
        while (Vector2.Distance(rb.position, launchEnd) > 0.02f) {
            rb.MovePosition(Vector2.MoveTowards(rb.position, launchEnd, vanishLaunchSpeed * Time.fixedDeltaTime));
            yield return new WaitForFixedUpdate();
        }
        SetBodyHidden(true);

        // 垂直柱狀警示標出下墜路徑:前 warningTrackDuration 秒追蹤玩家 x(速度比玩家慢),之後鎖定
        float columnX = player.transform.position.x;
        activeWarningColumn = SpawnWarningColumn(columnX);
        float elapsed = 0f;
        bool hasPlayedFallSfx = false;
        while (elapsed < warningDuration) {
            elapsed += Time.deltaTime;
            // 音效提前在下墜前 fallSfxLeadTime 秒播放(警示倒數剩這麼多秒時),聽起來才會跟下墜對上
            if (!hasPlayedFallSfx && elapsed >= warningDuration - fallSfxLeadTime) {
                hasPlayedFallSfx = true;
                AudioManager.Instance.PlaySFX(fallPunchSfxName);
            }
            if (elapsed < warningTrackDuration) {
                columnX = Mathf.MoveTowards(columnX, player.transform.position.x, warningFollowSpeed * Time.deltaTime);
                activeWarningColumn.transform.position = new Vector2(columnX, (mapMin.y + mapMax.y) * 0.5f);
            }
            yield return null;
        }
        Destroy(activeWarningColumn);
        activeWarningColumn = null;

        // 從地圖頂端垂直往下墜,一路穿出畫面底部,整段路徑碰到玩家造成傷害(最多一次),碰到的每台飛碟都引爆(可連爆多台)。
        // 下墜期間碰撞體維持關閉(仍不可被攻擊),命中改用每個物理步驟手動 OverlapCircle 判定。
        // Rigidbody 是 Kinematic,不會被底部的牆擋住,可以直接穿出去。
        Vector2 fallStart = new Vector2(columnX, mapMax.y - fallEdgeMargin);
        Vector2 fallEnd = new Vector2(columnX, mapMin.y - fallExitDistance);
        rb.position = fallStart;
        transform.position = fallStart;
        spriteRenderer.enabled = true;
        SetAnimTrigger("Fall");

        float hitRadius = bodyCollider.radius * Mathf.Abs(transform.lossyScale.x);
        bool hasHitPlayer = false;
        while (true) {
            Vector2 next = Vector2.MoveTowards(rb.position, fallEnd, fallSpeed * Time.fixedDeltaTime);
            rb.MovePosition(next);

            foreach (Collider2D hit in Physics2D.OverlapCircleAll(next, hitRadius)) {
                if (!hasHitPlayer && hit.CompareTag("Player") && hit.TryGetComponent(out PlayerBase target)) {
                    hasHitPlayer = true;
                    target.TakeDamage(baseAttack * fallDamagePercent);
                    float dx = target.transform.position.x - next.x;
                    Vector2 knockbackDir = new Vector2(Mathf.Abs(dx) > 0.01f ? Mathf.Sign(dx) : (Random.value < 0.5f ? -1f : 1f), 0f);
                    target.TryKnockback(knockbackDir, fallKnockbackDistance);
                } else if (hit.TryGetComponent(out Ufo ufo) && !ufo.IsDead) {
                    DetonateUfo(ufo);
                }
            }

            yield return new WaitForFixedUpdate();
            if (next == fallEnd) break;
        }

        // 穿出畫面後恢復可被攻擊,從畫面下方飛回施放前的位置。飛回地圖範圍內之前維持不可打斷,
        // 避免在畫面外被擊退/暈眩打斷後卡在玩家打不到的地方閒置;回到地圖內之後才照常可以被打斷。
        SetBodyHidden(false);
        float reenterY = mapMin.y + fallEdgeMargin;
        while (rb.position.y < reenterY && rb.position != originalPosition) {
            Vector2 next = Vector2.MoveTowards(rb.position, originalPosition, flySpeed * MoveSpeedMultiplier * Time.fixedDeltaTime);
            SetFacing(next.x - rb.position.x);
            rb.MovePosition(next);
            yield return new WaitForFixedUpdate();
        }
        isUninterruptible = false;

        yield return FlyTo(originalPosition);
        EndSkill();
    }

    void SetBodyHidden(bool hidden) {
        isUntargetable = hidden;
        spriteRenderer.enabled = !hidden;
        bodyCollider.enabled = !hidden;
    }

    GameObject SpawnWarningColumn(float x) {
        Vector2 position = new Vector2(x, (mapMin.y + mapMax.y) * 0.5f);
        if (warningColumnPrefab != null) return Instantiate(warningColumnPrefab, position, Quaternion.identity);

        var go = new GameObject("FallPunchWarning");
        go.transform.position = position;
        go.transform.localScale = new Vector3(warningColumnWidth, mapMax.y - mapMin.y, 1f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = PlaceholderSprite.Get(new Color(1f, 0f, 0f, 0.35f));
        sr.sortingOrder = 5;
        return go;
    }

    // 飛碟被墜地拳碰到:半徑內的玩家受傷 + 往外擊退,並朝 360 度均分放出放射狀滾石(起始角度隨機),然後飛碟死亡
    void DetonateUfo(Ufo ufo) {
        Vector2 center = ufo.transform.position;
        AudioManager.Instance.PlaySFX(explosionSfxName);
        SpawnExplosionEffect(center);

        foreach (Collider2D hit in Physics2D.OverlapCircleAll(center, explosionRadius)) {
            if (!hit.CompareTag("Player") || !hit.TryGetComponent(out PlayerBase target)) continue;

            target.TakeDamage(baseAttack * explosionDamagePercent);
            Vector2 outward = (Vector2)target.transform.position - center;
            if (outward != Vector2.zero) target.TryKnockback(outward.normalized, explosionKnockbackDistance);
            break; // 玩家只有一個,命中一次就夠
        }

        float startAngle = Random.Range(0f, 360f);
        float step = 360f / explosionRockCount;
        for (int i = 0; i < explosionRockCount; i++) {
            float rad = (startAngle + step * i) * Mathf.Deg2Rad;
            SpawnRock(center, new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)));
        }

        ufo.ForceDie();
    }

    void SpawnExplosionEffect(Vector2 center) {
        if (explosionEffectPrefab != null) {
            Instantiate(explosionEffectPrefab, center, Quaternion.identity);
            return;
        }

        var go = new GameObject("UfoExplosion");
        go.transform.position = center;
        go.transform.localScale = Vector3.one * (explosionRadius * 2f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = PlaceholderSprite.Get(new Color(1f, 0.5f, 0f, 0.4f));
        sr.sortingOrder = 5;
        Destroy(go, 0.2f);
    }

    // ---------- 超級霰彈(二階段) ----------

    // 飛到地圖中央 -> 獲得護盾 -> 每輪朝隨機方向各射一顆(3 顆,難度 5 為 4 顆),間隔從 0.5 秒每輪遞減 0.03 秒,
    // 最低 0.2 秒後維持,一直射到護盾被打破為止(見 OnShieldBroken)。全程不可被打斷。
    IEnumerator SuperShotgunRoutine() {
        isPerformingAction = true;
        isUninterruptible = true;
        isShotgunning = true;

        yield return FlyTo((mapMin + mapMax) * 0.5f);

        shieldMax = MaxHealth * shieldPercent;
        shield = shieldMax;

        int bulletsPerRound = difficulty == MaxDifficulty ? shotgunBulletsPerRoundMaxDifficulty : shotgunBulletsPerRound;
        float interval = shotgunStartInterval;
        while (true) {
            // 這招沒有自然結束點,玩家死亡/戰鬥結算暫停 AI 時要自己停火,不然會在結算畫面後面一直射
            if (IsPaused) {
                yield return null;
                continue;
            }

            SetAnimTrigger("Attack");
            FacePlayer();
            for (int i = 0; i < bulletsPerRound; i++) {
                float rad = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                Vector2 targetPos = rb.position + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                Bullet.Spawn(bulletPrefab, this, rb.position, targetPos, baseAttack * bulletDamagePercent,
                    targetTag: "Player", speed: bulletSpeed);
            }
            AudioManager.Instance.PlaySFX(shootSfxName);

            yield return new WaitForSeconds(interval);
            interval = Mathf.Max(shotgunMinInterval, interval - shotgunIntervalDecrement);
        }
    }

    // 護盾先吸收傷害,打破那一發的溢出傷害直接捨棄;墜地拳隱藏/下墜期間完全不吃傷害
    public override void TakeDamage(float amount, bool fromEnemyAttack = true) {
        if (IsDead || amount <= 0f || isUntargetable) return;

        if (shield > 0f) {
            AudioManager.Instance.PlaySFX(shieldHitSfxName); // 打在護盾上用專屬音效,不播一般受傷的 hurt
            if (hitEffectPrefab != null) Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);

            shield -= amount;
            if (shield <= 0f) OnShieldBroken();
            return;
        }

        base.TakeDamage(amount, fromEnemyAttack);
    }

    // 停止超級霰彈 -> 解除不可打斷 -> 重設二階段計時器 -> 對自己套用暈眩(不加易傷,破盾本身就是輸出窗口)
    void OnShieldBroken() {
        StopAllCoroutines();
        shield = 0f;
        isShotgunning = false;
        isUninterruptible = false; // 先解除,TryApplyStun 才不會被 CanBeKnockedBack 這道免疫閘門擋掉
        isPerformingAction = false;
        idleTimer = IdleInterval;
        phase2Timer = superShotgunInterval;
        TryApplyStun(shieldBreakStunDuration);
    }

    // ---------- 召喚物 ----------

    void TickSummonTimers(float dt) {
        rockTimer -= dt;
        if (rockTimer <= 0f) {
            rockTimer = Random.Range(rockIntervalMin, rockIntervalMaxBase - difficulty * rockIntervalMaxPerDifficulty);
            int count = Random.Range(rockCountMin, rockCountMax + 1);
            for (int i = 0; i < count; i++) {
                bool fromLeft = Random.value < 0.5f;
                Vector2 spawnPos = new Vector2(fromLeft ? mapMin.x + summonSpawnWallInset : mapMax.x - summonSpawnWallInset, RandomSummonY());
                Vector2 horizontal = fromLeft ? Vector2.right : Vector2.left;
                Vector2 direction = Quaternion.Euler(0f, 0f, Random.Range(-rockAngleJitterDegrees, rockAngleJitterDegrees)) * horizontal;
                SpawnRock(spawnPos, direction);
            }
        }

        ufoTimer -= dt;
        if (ufoTimer <= 0f) {
            ufoTimer = Random.Range(ufoIntervalMin, ufoIntervalMaxBase - difficulty * ufoIntervalMaxPerDifficulty);
            if (CountAliveUfos() < ufoMaxAlive) SpawnUfo(); // 場上滿了就跳過這次,直接等下一個週期
        }
    }

    float RandomSummonY() => Random.Range(mapMin.y + summonSpawnYInset, mapMax.y - summonSpawnYInset);

    int CountAliveUfos() {
        int count = 0;
        foreach (CatHeroSummon summon in summons) {
            if (summon is Ufo && !summon.IsDead) count++;
        }
        return count;
    }

    void SpawnRock(Vector2 position, Vector2 direction) {
        RollingRock rock = SpawnSummon(rockPrefab, position);
        rock.Launch(direction, rockSpeed);
    }

    void SpawnUfo() {
        bool fromLeft = Random.value < 0.5f;
        Vector2 spawnPos = new Vector2(fromLeft ? mapMin.x + summonSpawnWallInset : mapMax.x - summonSpawnWallInset, RandomSummonY());
        Ufo ufo = SpawnSummon(ufoPrefab, spawnPos);
        ufo.Launch(fromLeft ? 1 : -1, ufoSpeed);
    }

    // 比照 LevelManager 生成敵人的順序:Instantiate 後立刻套用難度(搶在召喚物自己的 Start 抓 MaxHealth 之前),再設定範圍/傷害
    T SpawnSummon<T>(T prefab, Vector2 position) where T : CatHeroSummon {
        T summon = Instantiate(prefab, position, Quaternion.identity);
        summon.ApplyDifficulty(difficulty);
        summon.Init(mapMin, mapMax, baseAttack);
        summons.Add(summon);
        return summon;
    }

    // ---------- 打斷 / 死亡 ----------

    // 擊退命中、或眩暈第一次從無到有生效時呼叫;墜地拳/超級霰彈期間 CanBeKnockedBack 已經擋住,只有光彈/站位移動會被打斷到。
    // 召喚計時器跑在 Update 不是協程,StopAllCoroutines 不會影響召喚。
    protected override void InterruptCurrentAction() {
        StopAllCoroutines();
        isPerformingAction = false;
        isUninterruptible = false;
        isShotgunning = false;
        shield = 0f;
        SetBodyHidden(false);
        ClearWarningColumn();
        idleTimer = IdleInterval;
    }

    public override bool TryKnockback(Vector2 direction, float distance) {
        if (!base.TryKnockback(direction, distance)) return false;
        InterruptCurrentAction();
        return true;
    }

    void ClearWarningColumn() {
        if (activeWarningColumn == null) return;
        Destroy(activeWarningColumn);
        activeWarningColumn = null;
    }

    // 貓俠死亡:清掉警示柱、恢復本體顯示(死亡淡出要看得到),場上召喚物一起死亡消失
    protected override void Die() {
        ClearWarningColumn();
        SetBodyHidden(false);
        shield = 0f;
        foreach (CatHeroSummon summon in summons) {
            if (summon != null) summon.ForceDie();
        }
        base.Die();
    }
}
