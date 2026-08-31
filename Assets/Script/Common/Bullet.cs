using UnityEngine;

// 子彈分類:一般(可被破壞性摧毀)/破壞性(會摧毀撞到的非免疫子彈,兩顆破壞性互相摧毀)/免疫破壞(不受破壞性影響,也不會主動摧毀任何子彈)
public enum BulletType { Normal, Destructive, Immune }

// 直線飛向發射當下鎖定位置的子彈:命中 targetTag 造成傷害,命中 Wall 直接消失,子彈之間依 BulletType 規則互相影響
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Bullet : MonoBehaviour {
    const float speed = 20f;
    const float lifeTime = 3f;

    float damage;
    Vector2 direction;
    MonoBehaviour owner;
    string targetTag;
    BulletType bulletType;
    bool destroyed;

    public static void Spawn(GameObject prefab, MonoBehaviour owner, Vector3 origin, Vector3 targetPosition, float damage, string targetTag, BulletType bulletType = BulletType.Normal) {
        GameObject go = prefab != null
            ? Object.Instantiate(prefab, origin, Quaternion.identity)
            : CreateFallback(origin);

        if (!go.TryGetComponent(out Bullet bullet)) bullet = go.AddComponent<Bullet>();
        bullet.damage = damage;
        bullet.owner = owner;
        bullet.targetTag = targetTag;
        bullet.bulletType = bulletType;
        bullet.direction = ((Vector2)targetPosition - (Vector2)origin).normalized;

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
            }
            DestroySelf();
        } else if (other.CompareTag("Wall")) {
            DestroySelf();
        }
    }

    // 破壞性子彈會摧毀撞到的非免疫子彈;兩顆破壞性子彈各自觸發這條規則,結果就是互毀
    void HandleBulletCollision(Bullet other) {
        if (bulletType == BulletType.Destructive && other.bulletType != BulletType.Immune) {
            other.DestroySelf();
        }
    }

    void DestroySelf() {
        if (destroyed) return;
        destroyed = true;
        Destroy(gameObject);
    }
}
