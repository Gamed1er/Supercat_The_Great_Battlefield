using UnityEngine;

// 貓俠(1-3)召喚物的共用基底(滾石/飛碟):由 CatHero 自己 Instantiate 並管理,不經過 LevelManager 的出怪池,
// 所以不算進怪池血條、也不影響通關判定。血量的難度倍率走 EnemyBase.ApplyDifficulty,但用召喚物 prefab 自己的
// healthMultiplier(刻意跟貓俠本體分開調,避免召喚物在高難度太硬);傷害則直接沿用貓俠的攻擊力(見 Init)。
// 召喚物是固定軌跡的機關,擊退/暈眩/麻痺/失焦一律免疫。
public abstract class CatHeroSummon : EnemyBase {
    const float DestroyDelayAfterDeath = 1.5f; // 比 EnemyBase 的死亡淡出(延遲 1 秒 + 淡出 0.3 秒)稍長,淡出播完才移除物件

    [Header("佔位圖 (還沒有美術時,SpriteRenderer 沒指定 sprite 就用這個顏色的方塊)")]
    [SerializeField] Color placeholderColor = Color.gray;

    protected Vector2 mapMin;
    protected Vector2 mapMax;
    protected float contactDamage;

    protected override bool CanBeKnockedBack => false;
    protected override bool PlaysDeathSfx => false;

    protected override void Awake() {
        base.Awake();
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer.sprite == null) spriteRenderer.sprite = PlaceholderSprite.Get(placeholderColor);
    }

    // 由 CatHero 在 Instantiate + ApplyDifficulty 之後立刻呼叫:地圖範圍(邊界判定用)與造成的傷害(貓俠攻擊力的 100%)
    public void Init(Vector2 mapMin, Vector2 mapMax, float contactDamage) {
        this.mapMin = mapMin;
        this.mapMax = mapMax;
        this.contactDamage = contactDamage;
    }

    public override bool TryApplySlow(float duration) => false;
    public override bool TryApplyTargetLock(Vector2 position, float duration) => false;

    protected override void Die() {
        base.Die();
        Destroy(gameObject, DestroyDelayAfterDeath);
    }
}
