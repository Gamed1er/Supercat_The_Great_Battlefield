using UnityEngine;

// 簡單的放大+淡出特效:整段 lifeTime 從原本大小放大到 growTo 倍,透明度在 fadeDelay 秒後才開始淡到 0,結束後自動銷毀。
// growTo = 1 就是不放大(例如侏儸貓的地板裂痕)。
public class SmokePuff : MonoBehaviour {
    public float lifeTime = 0.5f;
    public float growTo = 1.5f;
    public float fadeDelay = 0f; // 前這麼多秒維持不透明,之後在剩下的時間內淡出

    SpriteRenderer spriteRenderer;
    float elapsed;
    Vector3 startScale;

    void Awake() {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer.sprite == null) spriteRenderer.sprite = PlaceholderSprite.Get(new Color(0.85f, 0.85f, 0.85f, 0.9f));
    }

    // 起始大小在 Start 才記下(不是 Awake):Awake 在 Instantiate 當下就跑完了,呼叫端 Instantiate 之後才改 localScale
    // (例如侏儸貓槌擊用縮小版的落地特效)會來不及;Start 在第一次 Update 之前才跑,能吃到呼叫端改過的大小
    void Start() {
        startScale = transform.localScale;
    }

    void Update() {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / lifeTime);

        transform.localScale = Vector3.Lerp(startScale, startScale * growTo, t);

        float fadeT = lifeTime > fadeDelay ? Mathf.Clamp01((elapsed - fadeDelay) / (lifeTime - fadeDelay)) : t;
        Color c = spriteRenderer.color;
        c.a = Mathf.Lerp(1f, 0f, fadeT);
        spriteRenderer.color = c;

        if (elapsed >= lifeTime) Destroy(gameObject);
    }
}
