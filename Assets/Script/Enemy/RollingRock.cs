using UnityEngine;

// 貓俠的滾石:朝固定方向直線滾動(1-3 是無重力設定,可以浮空滾),離開地圖範圍(碰到牆)就死亡;
// 碰到玩家造成貓俠攻擊力 100% 的傷害 + 沿前進方向的小幅擊退,然後自身死亡(玩家免傷中也一樣會死,規則單純化)。
// 一般召喚是水平方向,飛碟爆炸放出的放射狀滾石則是任意方向,兩者共用「離開地圖範圍就死」這條規則。
public class RollingRock : CatHeroSummon {
    [Header("滾動")]
    public float wallMargin = 0.3f; // 中心點離牆小於這個距離就視為碰到牆,生成時要放在這個距離以內側(見 CatHero.SpawnRock)
    public float playerKnockbackDistance = 0.5f;

    Vector2 direction;
    float speed;

    public void Launch(Vector2 direction, float speed) {
        this.direction = direction.normalized;
        this.speed = speed;
    }

    protected override void FixedUpdate() {
        base.FixedUpdate();
        if (IsDead || IsPaused) return;

        Vector2 next = rb.position + direction * speed * Time.fixedDeltaTime;
        rb.MovePosition(next);

        bool hitWall = next.x < mapMin.x + wallMargin || next.x > mapMax.x - wallMargin
                    || next.y < mapMin.y + wallMargin || next.y > mapMax.y - wallMargin;
        if (hitWall) Die();
    }

    void OnTriggerStay2D(Collider2D other) {
        if (IsDead || IsPaused) return;
        if (!other.CompareTag("Player")) return;
        if (!other.TryGetComponent(out PlayerBase player)) return;

        player.TakeDamage(contactDamage);
        player.TryKnockback(direction, playerKnockbackDistance);
        Die();
    }
}
