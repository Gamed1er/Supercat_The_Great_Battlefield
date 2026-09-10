using UnityEngine;

// 大狗叫的音波:寬度固定的弧形彈體,弧心固定在狗仔身上(局部座標原點),沿著發射當下鎖定的方向整體平移飛行
// (弧形是固定形狀,飛行中不會變形/不會繼續繞著狗仔轉)。內側長邊(貼近狗仔的那一側)面向狗仔,兩端的短邊則面向切線方向。
// 對重疊的玩家每 0.05 秒造成一次傷害、直接穿透不會消失。
// 蓄力期間(isLaunched=false)只會淡入、停在原地不動,Launch() 之後才開始飛行並造成傷害,飛行 lifeTime 秒後不管有沒有飛出畫面都會自行消失。
// 弧形用程式產生的 Mesh(幾段短直線逼近弧線)+ PolygonCollider2D 呈現,不依賴預先切好的弧形貼圖:
// 貼圖(waveTexture)只要是一般長方形圖即可,由 UV 自動撐開貼合弧形(U 沿著弧長方向,V 是內外側的寬度方向)。
[RequireComponent(typeof(Rigidbody2D))]
public class SonicWave : MonoBehaviour {
    const float damageTickInterval = 1f;
    const float damageMultiplier = 1.5f; // 攻擊力 60%
    const float speed = 12f; // 飛行速度,刻意比玩家子彈(20)慢很多
    const float lifeTime = 2f; // 從 Launch() 起算,純計時銷毀,允許飛出畫面外
    const float maxAngleSpanDeg = 90f; // 弧形張角上限,避免 gap 太小、length 太長時繞成一大圈包住狗仔
    const int curveSegments = 12; // 用幾段短直線逼近弧線,段數越多越平滑

    public Texture2D waveTexture; // 美術貼圖,留空時用半透明純色佔位(見 Awake)

    Rigidbody2D rb;
    MeshRenderer meshRenderer;
    Vector2 direction;
    float attack;
    float damageTimer;
    bool isLaunched;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;

        if (!TryGetComponent(out MeshFilter meshFilter)) meshFilter = gameObject.AddComponent<MeshFilter>();
        if (!TryGetComponent(out meshRenderer)) meshRenderer = gameObject.AddComponent<MeshRenderer>();
        if (!TryGetComponent(out PolygonCollider2D polygonCollider)) polygonCollider = gameObject.AddComponent<PolygonCollider2D>();
        polygonCollider.isTrigger = true;

        Texture2D tex = waveTexture != null ? waveTexture : PlaceholderSprite.Get(new Color(0.4f, 0.8f, 1f, 0.6f)).texture;
        var material = new Material(Shader.Find("Sprites/Default"));
        material.mainTexture = tex;
        meshRenderer.material = material;
    }

    // aimDirection:這顆音波要飛的方向(呼叫端已經算好瞄準玩家 + 隨機偏移,不必是單位向量),決定弧形對稱軸朝哪個世界方向。
    // gap 是內側(貼近狗仔)那條邊離狗仔的距離,width 是內外側之間的厚度,length 是弧長(外側弧長,張角太大時會被限制,見 BuildShape);
    // attack 是目前攻擊力(乘上 damageMultiplier 才是實際每次 tick 的傷害)。
    public void Init(Vector2 aimDirection, float gap, float length, float width, float attack) {
        this.attack = attack;
        direction = aimDirection.normalized;

        float angleDeg = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angleDeg);

        BuildShape(gap, length, width);
        SetChargingVisual(0f);
    }

    // 弧心固定在 local 原點(=狗仔位置),對稱於發射方向(local +x)左右展開;
    // innerR(=gap)到 outerR(=gap+width)之間是彈體厚度,張角由「想要的弧長 length ÷ 中線半徑」反推,
    // 並限制在 maxAngleSpanDeg 以內,避免半徑太小時繞成一大圈。內側弧是長邊(面向狗仔),兩端的直邊是短邊(面向切線方向)。
    void BuildShape(float gap, float length, float width) {
        float halfWidth = width * 0.5f;
        float innerR = gap;
        float outerR = gap + width;
        float midR = gap + halfWidth;

        float angleSpanRad = Mathf.Min(length / midR, maxAngleSpanDeg * Mathf.Deg2Rad);

        int pointCount = curveSegments + 1;
        float angleStep = angleSpanRad / curveSegments;
        float rad = -angleSpanRad * 0.5f;

        var inner = new Vector2[pointCount];
        var outer = new Vector2[pointCount];
        for (int i = 0; i < pointCount; i++) {
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            inner[i] = dir * innerR;
            outer[i] = dir * outerR;
            rad += angleStep;
        }

        var vertices = new Vector3[pointCount * 2];
        var uvs = new Vector2[pointCount * 2];
        var triangles = new int[curveSegments * 6];
        for (int i = 0; i < pointCount; i++) {
            vertices[i * 2] = inner[i];
            vertices[i * 2 + 1] = outer[i];
            float u = i / (float)curveSegments; // 沿弧長方向
            uvs[i * 2] = new Vector2(u, 0f); // 內側(面向狗仔)
            uvs[i * 2 + 1] = new Vector2(u, 1f); // 外側
        }
        for (int i = 0; i < curveSegments; i++) {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            triangles[i * 6] = a;
            triangles[i * 6 + 1] = c;
            triangles[i * 6 + 2] = b;
            triangles[i * 6 + 3] = b;
            triangles[i * 6 + 4] = c;
            triangles[i * 6 + 5] = d;
        }

        var mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        GetComponent<MeshFilter>().mesh = mesh;

        var colliderPoints = new Vector2[pointCount * 2];
        for (int i = 0; i < pointCount; i++) colliderPoints[i] = inner[i];
        for (int i = 0; i < pointCount; i++) colliderPoints[pointCount + i] = outer[pointCount - 1 - i];
        GetComponent<PolygonCollider2D>().points = colliderPoints;
    }

    // 蓄力期間呼叫,t 從 0 到 1:音波逐漸淡入,停在原地不動、不造成傷害
    public void SetChargingVisual(float t) {
        Color c = meshRenderer.material.color;
        c.a = Mathf.Clamp01(t);
        meshRenderer.material.color = c;
    }

    // 蓄力結束,開始沿著 Init 時鎖定的方向飛行並造成傷害,lifeTime 秒後自動銷毀
    public void Launch() {
        isLaunched = true;
        meshRenderer.material.color = Color.white;
        Destroy(gameObject, lifeTime);
    }

    void FixedUpdate() {
        if (!isLaunched) return;

        rb.MovePosition(rb.position + direction * speed * Time.fixedDeltaTime);
        if (damageTimer > 0f) damageTimer -= Time.fixedDeltaTime;
    }

    void OnTriggerStay2D(Collider2D other) {
        if (!isLaunched) return;
        if (damageTimer > 0f) return;
        if (!other.CompareTag("Player")) return;

        if (other.TryGetComponent(out IDamageable target)) {
            target.TakeDamage(attack * damageMultiplier);
            damageTimer = damageTickInterval;
        }
    }
}
