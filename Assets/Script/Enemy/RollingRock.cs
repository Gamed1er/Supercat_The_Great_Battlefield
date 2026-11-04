using UnityEngine;

// 貓俠的滾石:朝固定方向直線滾動(1-3 是無重力設定,可以浮空滾),離開地圖範圍(碰到牆)就死亡;
// 碰到玩家造成貓俠攻擊力 100% 的傷害 + 沿前進方向的小幅擊退,然後自身死亡(玩家免傷中也一樣會死,規則單純化)。
// 一般召喚是水平方向,飛碟爆炸放出的放射狀滾石則是任意方向,兩者共用「離開地圖範圍就死」這條規則。
// 動畫(BCU,子物件的 BcAnimPlayer):滾動中播 00;不管怎麼死(被打死/撞到玩家/碰牆/貓俠死亡)都播 02 碎裂,播完才移除。01/03 不使用。
public class RollingRock : CatHeroSummon {
    [Header("滾動")]
    public float wallMargin = 0.3f; // 中心點離牆小於這個距離就視為碰到牆,生成時要放在這個距離以內側(見 CatHero.SpawnRock)
    public float playerKnockbackDistance = 0.5f;

    Vector2 direction;
    float speed;

    public void Launch(Vector2 direction, float speed) {
        this.direction = direction.normalized;
        this.speed = speed;
        bcAnim.Play(BcClipWalk);
        // 原圖是往左滾;直直往上/下的放射狀滾石維持原圖方向
        if (Mathf.Abs(this.direction.x) > 0.01f) bcAnim.SetFacing(this.direction.x > 0f);
    }

    // 02 對滾石來說是碎裂動畫(不是攻擊),死亡時播完就移除,不走預設的 KB + 淡出
    protected override void PlayDeathPresentation() {
        bcAnim.Play(BcClipAttack, loop: false);
        StartCoroutine(DestroyAfterBreak());
    }

    System.Collections.IEnumerator DestroyAfterBreak() {
        yield return new WaitUntil(() => bcAnim.IsDone);
        Destroy(gameObject);
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
