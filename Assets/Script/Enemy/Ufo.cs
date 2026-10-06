using UnityEngine;

// 貓俠的飛碟:水平來回移動(碰到牆反向)+上下漂浮,不會主動攻擊,只有接觸傷害(貓俠本體沒有接觸傷害,改由飛碟/滾石負責),
// 主要用途是場上的「資源」——
// 被貓俠的墜地拳碰到時由 CatHero 引爆(爆炸/放射狀滾石的邏輯都在 CatHero.DetonateUfo,這裡只負責移動)。
// 玩家可以先把飛碟打掉,阻止貓俠打出這個連段。
public class Ufo : CatHeroSummon {
    [Header("移動")]
    public float wallMargin = 0.8f; // 中心點離牆小於這個距離就反向,生成時要放在這個距離以內側(見 CatHero.SpawnUfo)
    public float bobAmplitude = 0.3f;
    public float bobFrequency = 2f; // 每秒來回幾次(Hz)

    [Header("接觸傷害 (比照狗仔:頻率限制 + 只取水平分量的微幅擊退,傷害 = 貓俠攻擊力 100%)")]
    public float bodyContactDamageInterval = 0.5f;
    public float contactKnockbackDistance = 0.5f;

    float speed;
    float bodyContactDamageTimer;
    int horizontalDirection = 1; // +1 右, -1 左
    float baseY;
    float bobTime;

    public void Launch(int horizontalDirection, float speed) {
        this.horizontalDirection = horizontalDirection >= 0 ? 1 : -1;
        this.speed = speed;
        baseY = transform.position.y;
        bobTime = Random.Range(0f, 1f / bobFrequency); // 多台同時在場時漂浮相位錯開,看起來比較自然
    }

    protected override void FixedUpdate() {
        base.FixedUpdate();
        if (bodyContactDamageTimer > 0f) bodyContactDamageTimer -= Time.fixedDeltaTime;
        if (IsDead || IsPaused) return;

        bobTime += Time.fixedDeltaTime;
        float x = rb.position.x + horizontalDirection * speed * Time.fixedDeltaTime;

        float minX = mapMin.x + wallMargin;
        float maxX = mapMax.x - wallMargin;
        if (x <= minX) { x = minX; horizontalDirection = 1; }
        else if (x >= maxX) { x = maxX; horizontalDirection = -1; }

        float y = baseY + Mathf.Sin(bobTime * bobFrequency * 2f * Mathf.PI) * bobAmplitude;
        rb.MovePosition(new Vector2(x, y));
    }

    void OnTriggerStay2D(Collider2D other) {
        if (IsDead || IsPaused) return;
        if (bodyContactDamageTimer > 0f) return;
        if (!other.CompareTag("Player")) return;
        if (!other.TryGetComponent(out PlayerBase player)) return;

        player.TakeDamage(contactDamage);
        bodyContactDamageTimer = bodyContactDamageInterval;

        Vector2 direction = new Vector2(other.transform.position.x - rb.position.x, 0f);
        if (direction != Vector2.zero) player.TryKnockback(direction, contactKnockbackDistance);
    }
}
