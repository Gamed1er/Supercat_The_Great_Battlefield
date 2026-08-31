using UnityEngine;

// 共用的擊退狀態:PlayerBase/EnemyBase 各自持有一份實例並委派(組合,不改動兩邊各自的繼承鏈)。
// 硬直期間用 Rigidbody2D.MovePosition 把角色往外推開一段距離,速度抓得剛好讓 distance 在 duration 內走完。
public class KnockbackState {
    readonly Rigidbody2D rb;

    public bool IsKnockedBack { get; private set; }

    float timer;
    Vector2 velocity;

    public KnockbackState(Rigidbody2D rb) {
        this.rb = rb;
    }

    // 觸發擊退,硬直中不能重複觸發。distance 給 0 時只有硬直沒有位移。
    public bool TryApply(Vector2 direction, float distance, float duration) {
        if (IsKnockedBack || direction == Vector2.zero || duration <= 0f) return false;

        IsKnockedBack = true;
        timer = duration;
        velocity = direction.normalized * (distance / duration);
        return true;
    }

    // 由擁有者的 FixedUpdate 呼叫,處理位移與硬直倒數
    public void Tick(float fixedDeltaTime) {
        if (!IsKnockedBack) return;

        rb.MovePosition(rb.position + velocity * fixedDeltaTime);

        timer -= fixedDeltaTime;
        if (timer <= 0f) IsKnockedBack = false;
    }
}
