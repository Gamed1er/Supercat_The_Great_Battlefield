using UnityEngine;

// 大狗叫的音波:長方形物件以敵人位置為圓心旋轉,對重疊的玩家每 0.05 秒造成一次傷害。
// 蓄力期間(isSweeping=false)只會淡入、停在起始角度,不旋轉也不造成傷害;BeginSweep() 之後才開始轉動掃過去。
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class SonicWave : MonoBehaviour {
    const float damageTickInterval = 0.05f;
    const float damageMultiplier = 0.4f; // 攻擊力 40%

    Rigidbody2D rb;
    SpriteRenderer sr;
    Transform pivot;
    float currentAngleDeg;
    float rotationSpeedDeg; // 含方向,負值代表順時針
    float halfLength;
    float gap;
    float attack;
    float damageTimer;

    bool isSweeping;
    Color baseColor;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;
        sr = GetComponent<SpriteRenderer>();
    }

    public void Init(Transform pivot, bool aimRight, float sweepAngle, float rotationSpeedDegPerSec, float length, float width, float gap, float attack) {
        this.pivot = pivot;
        this.attack = attack;
        this.gap = gap;
        halfLength = length * 0.5f;

        // 水平於地面(+x 或 -x 方向)開始,往中間掃過並溢出一段,共 sweepAngle 度
        currentAngleDeg = aimRight ? 0f : 180f;
        rotationSpeedDeg = aimRight ? rotationSpeedDegPerSec : -rotationSpeedDegPerSec;

        if (sr.sprite == null) sr.sprite = PlaceholderSprite.Get(new Color(0.4f, 0.8f, 1f, 0.6f));
        baseColor = sr.color;

        // 依照 sprite 原生大小換算縮放,不管指定的是佔位方塊還是美術圖,最後世界大小都會是 length x width
        Vector2 nativeSize = sr.sprite.bounds.size;
        transform.localScale = new Vector3(length / nativeSize.x, width / nativeSize.y, 1f);

        BoxCollider2D box = GetComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = nativeSize;

        isSweeping = false;
        SetChargingVisual(0f);
        ApplyTransform();
    }

    // 蓄力期間呼叫,t 從 0 到 1:音波逐漸淡入,停在起始角度不動、不造成傷害
    public void SetChargingVisual(float t) {
        Color c = baseColor;
        c.a = baseColor.a * Mathf.Clamp01(t);
        sr.color = c;
    }

    // 蓄力結束,開始旋轉掃過去並造成傷害
    public void BeginSweep() {
        isSweeping = true;
        sr.color = baseColor;
    }

    void FixedUpdate() {
        if (pivot == null) return;

        if (isSweeping) {
            currentAngleDeg += rotationSpeedDeg * Time.fixedDeltaTime;
            if (damageTimer > 0f) damageTimer -= Time.fixedDeltaTime;
        }

        ApplyTransform();
    }

    // 長方形沿目前角度往外延伸,近端離 pivot 保持 gap 的距離、不貼在敵人身上
    void ApplyTransform() {
        float rad = currentAngleDeg * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        rb.MovePosition((Vector2)pivot.position + direction * (gap + halfLength));
        rb.MoveRotation(currentAngleDeg);
    }

    void OnTriggerStay2D(Collider2D other) {
        if (!isSweeping) return;
        if (damageTimer > 0f) return;
        if (!other.CompareTag("Player")) return;

        if (other.TryGetComponent(out IDamageable target)) {
            target.TakeDamage(attack * damageMultiplier);
            damageTimer = damageTickInterval;
        }
    }
}
