using UnityEngine;

// 戰技位移:左鍵點擊時朝滑鼠鼠標方向位移
public class DashSkill : ISkill {
    const float dashSpeed = 50f;
    const float dashDuration = 0.1f;

    readonly Rigidbody2D rb;
    readonly float cooldown;
    readonly Camera mainCamera;

    public float Cooldown => cooldown;
    public float CooldownRemaining => Mathf.Max(0f, Cooldown - (Time.time - lastTriggerTime));
    public bool IsActive { get; private set; }

    float lastTriggerTime = -Mathf.Infinity;
    float dashElapsed;
    Vector2 dashDirection;
    bool wasReady = true; // 冷卻剛好轉為就緒時播放提示音,一開始就是就緒狀態不用播

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
        Vector2 direction = mouseWorldPos - rb.position;
        if (direction == Vector2.zero) return false;
        direction.Normalize();

        lastTriggerTime = Time.time;
        IsActive = true;
        dashElapsed = 0f;
        dashDirection = direction;
        AudioManager.Instance.PlaySFX("teleport");
        return true;
    }

    public void Tick() {
        bool isReady = CooldownRemaining <= 0f;
        if (isReady && !wasReady) AudioManager.Instance.PlaySFX("skill_done");
        wasReady = isReady;

        if (!IsActive) return;

        dashElapsed += Time.fixedDeltaTime;
        rb.MovePosition(rb.position + dashDirection * dashSpeed * Time.fixedDeltaTime);

        if (dashElapsed >= dashDuration) {
            IsActive = false;
        }
    }

    public void Interrupt() {
        IsActive = false;
    }

    public void OnHitEnemy(Collider2D enemyCollider) {
        IsActive = false;
    }
}
