using UnityEngine;

// 戰技瞬移:左鍵點擊時瞬間移動到滑鼠鼠標的世界座標,無距離限制,沒有移動過程(不同於「位移」,一幀內直接完成)。
public class DashSkill : ISkill {
    readonly Rigidbody2D rb;
    readonly float cooldown;
    readonly Camera mainCamera;

    public float Cooldown => cooldown;

    public float CooldownCurrent => Mathf.Max(0f, Time.time - lastTriggerTime);
    public bool IsActive => false; // 瞬間完成,沒有移動過程需要暫停 WASD 移動或被中斷

    float lastTriggerTime = 0f;
    bool wasReady = false; // 冷卻剛好轉為就緒時播放提示音

    public DashSkill(PlayerBase owner, float cooldown) {
        this.cooldown = cooldown;
        rb = owner.GetComponent<Rigidbody2D>();
        mainCamera = Camera.main;
    }

    public bool TryExecute() {
        if (!Input.GetMouseButtonDown(0)) return false;
        if (Time.time - lastTriggerTime < Cooldown) {
            AudioManager.Instance.PlaySFX("no"); // 冷卻還沒好,放技能失敗
            return false;
        }

        Vector2 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        if (mouseWorldPos == rb.position) return false;

        lastTriggerTime = Time.time;
        rb.position = mouseWorldPos;
        AudioManager.Instance.PlaySFX("teleport");
        return true;
    }

    public void Tick() {
        bool isReady = CooldownCurrent >= Cooldown;
        if (isReady && !wasReady) AudioManager.Instance.PlaySFX("skill_done");
        wasReady = isReady;
    }

    public void Interrupt() { }

    public void OnHitEnemy(Collider2D enemyCollider) { }
}
