using UnityEngine;

// 直線飛向發射當下鎖定位置的子彈:命中 Enemy 造成傷害,命中 Wall 直接消失
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Bullet : MonoBehaviour {
    const float speed = 20f;
    const float lifeTime = 3f;

    float damage;
    Vector2 direction;
    PlayerBase owner;

    public static void Spawn(GameObject prefab, PlayerBase owner, Vector3 origin, Vector3 targetPosition, float damage) {
        GameObject go = prefab != null
            ? Object.Instantiate(prefab, origin, Quaternion.identity)
            : CreateFallback(origin);

        if (!go.TryGetComponent(out Bullet bullet)) bullet = go.AddComponent<Bullet>();
        bullet.damage = damage;
        bullet.owner = owner;
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
        if (other.CompareTag("Enemy")) {
            if (other.TryGetComponent(out IDamageable target)) {
                target.TakeDamage(damage);
                if (owner != null) owner.stats.AddCharge(1f); // 普通攻擊命中 +1 充能
            }
            Destroy(gameObject);
        } else if (other.CompareTag("Wall")) {
            Destroy(gameObject);
        }
    }
}
