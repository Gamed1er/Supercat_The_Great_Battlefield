using UnityEngine;

// 原地揮擊式近戰的判定範圍:在攻擊者前方(沿著攻擊方位)生成一個固定形狀,立即做一次重疊判定,
// 對範圍內每個目標各造成一次傷害+擊退。用即時 Overlap 判定,不用等 trigger 跨影格,天生不會重複命中同一目標。
public static class MeleeHitbox {
    public static void Hit(Vector2 origin, Vector2 direction, float offset, Vector2 size, string targetTag, float damage, float knockbackDistance) {
        direction = direction.normalized;
        Vector2 center = origin + direction * offset;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        foreach (Collider2D hit in Physics2D.OverlapBoxAll(center, size, angle)) {
            if (!hit.CompareTag(targetTag)) continue;

            if (hit.TryGetComponent(out IDamageable damageable)) damageable.TakeDamage(damage);

            if (knockbackDistance > 0f && hit.TryGetComponent(out IKnockbackable knockbackTarget)) {
                knockbackTarget.TryKnockback(direction, knockbackDistance);
            }
        }
    }
}
