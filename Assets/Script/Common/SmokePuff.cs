using UnityEngine;

// 簡單的煙霧特效佔位:放大並淡出後自動銷毀
public class SmokePuff : MonoBehaviour {
    public float lifeTime = 0.5f;
    public float growTo = 1.5f;

    SpriteRenderer spriteRenderer;
    float elapsed;
    Vector3 startScale;

    void Awake() {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer.sprite == null) spriteRenderer.sprite = PlaceholderSprite.Get(new Color(0.85f, 0.85f, 0.85f, 0.9f));
        startScale = transform.localScale;
    }

    void Update() {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / lifeTime);

        transform.localScale = Vector3.Lerp(startScale, startScale * growTo, t);

        Color c = spriteRenderer.color;
        c.a = Mathf.Lerp(1f, 0f, t);
        spriteRenderer.color = c;

        if (elapsed >= lifeTime) Destroy(gameObject);
    }
}
