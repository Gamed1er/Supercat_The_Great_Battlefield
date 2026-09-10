using System;
using UnityEngine;

// 子彈分類:一般(可被破壞性摧毀)/破壞性(會摧毀撞到的非免疫子彈,兩顆破壞性互相摧毀)/免疫破壞(不受破壞性影響,也不會主動摧毀任何子彈)
public enum BulletType { Normal, Destructive, Immune }

// 直線飛向發射當下鎖定位置的子彈:命中 targetTag 造成傷害,命中 Wall 直接消失,子彈之間依 BulletType 規則互相影響
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Bullet : MonoBehaviour {
    public const float defaultSpeed = 20f; // public 供其他技能算相對速度用(例如赤井大招的追蹤彈是這個速度的一半)
    const float defaultLifeTime = 3f;

    float damage;
    Vector2 direction;
    float speed;
    MonoBehaviour owner;
    string targetTag;
    BulletType bulletType;
    Action onHit; // 命中目標或摧毀敵方子彈時呼叫,給發射端(技能)掛自訂效果用,預設不掛(null)
    bool destroyed;

    // 追蹤用(選用):homingTarget 非 null 時,飛行方向每個 FixedUpdate 依 homingTurnRateDegrees 朝目標修正,
    // 超過 homingDuration 秒未命中就放棄追蹤(homingTarget 設回 null),之後維持當下方向直線飛行
    Transform homingTarget;
    float homingTurnRateDegrees;
    float homingDuration;
    float homingElapsed;

    public static void Spawn(GameObject prefab, MonoBehaviour owner, Vector3 origin, Vector3 targetPosition, float damage, string targetTag, BulletType bulletType = BulletType.Normal, Action onHit = null,
        float speed = defaultSpeed, float lifeTime = defaultLifeTime, Transform homingTarget = null, float homingTurnRateDegrees = 0f, float homingDuration = 0f) {
        GameObject go = prefab != null
            ? UnityEngine.Object.Instantiate(prefab, origin, Quaternion.identity)
            : CreateFallback(origin);

        if (!go.TryGetComponent(out Bullet bullet)) bullet = go.AddComponent<Bullet>();
        bullet.damage = damage;
        bullet.owner = owner;
        bullet.targetTag = targetTag;
        bullet.bulletType = bulletType;
        bullet.onHit = onHit;
        bullet.speed = speed;
        bullet.direction = ((Vector2)targetPosition - (Vector2)origin).normalized;
        bullet.homingTarget = homingTarget;
        bullet.homingTurnRateDegrees = homingTurnRateDegrees;
        bullet.homingDuration = homingDuration;

        Destroy(go, lifeTime);
    }

    // 還沒指定 Bullet Prefab 時的備用視覺,避免子彈完全看不到
    static GameObject CreateFallback(Vector3 origin) {
        var go = new GameObject("Bullet");
        go.transform.position = origin;
        go.transform.localScale = Vector3.one * 0.3f;

        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;

        CircleCollider2D col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;

        go.AddComponent<SpriteRenderer>().sprite = PlaceholderSprite.Get(Color.yellow);

        return go;
    }

    void FixedUpdate() {
        if (homingTarget != null) {
            homingElapsed += Time.fixedDeltaTime;
            if (homingElapsed > homingDuration) {
                homingTarget = null; // 追蹤超時,放棄追蹤,之後維持目前方向直線飛行
            } else {
                Vector2 toTarget = (Vector2)homingTarget.position - (Vector2)transform.position;
                if (toTarget != Vector2.zero) {
                    float maxRadiansDelta = homingTurnRateDegrees * Mathf.Deg2Rad * Time.fixedDeltaTime;
                    Vector3 rotated = Vector3.RotateTowards(direction, toTarget.normalized, maxRadiansDelta, 0f); // 借 Vector3 版本的 RotateTowards,z 分量恆為 0
                    direction = ((Vector2)rotated).normalized;
                }
            }
        }

        transform.Translate(direction * speed * Time.fixedDeltaTime, Space.World);
    }

    void OnTriggerEnter2D(Collider2D other) {
        if (destroyed) return;

        if (other.TryGetComponent(out Bullet otherBullet)) {
            HandleBulletCollision(otherBullet);
            return;
        }

        if (other.CompareTag(targetTag)) {
            if (other.TryGetComponent(out IDamageable target)) {
                target.TakeDamage(damage);
                if (owner is PlayerBase player) player.stats.AddCharge(1f); // 普通攻擊命中 +1 充能,玩家專屬
                onHit?.Invoke();
            }
            DestroySelf();
        } else if (other.CompareTag("Wall")) {
            DestroySelf();
        }
    }

    // 破壞性子彈會摧毀撞到的非免疫子彈;兩顆破壞性子彈各自觸發這條規則,結果就是互毀
    // targetTag 相同代表同陣營(例如同一次大招齊射的 20 顆子彈都打 "Enemy"),彼此不互相影響,
    // 避免同陣營的破壞性子彈在同一位置生成時,還沒飛開就先自相殘殺
    void HandleBulletCollision(Bullet other) {
        if (targetTag == other.targetTag) return;
        if (bulletType == BulletType.Destructive && other.bulletType != BulletType.Immune) {
            other.DestroySelf();
            onHit?.Invoke(); // 摧毀敵方子彈也算命中,通知發射端(例如赤井的被動)
        }
    }

    void DestroySelf() {
        if (destroyed) return;
        destroyed = true;
        Destroy(gameObject);
    }
}
