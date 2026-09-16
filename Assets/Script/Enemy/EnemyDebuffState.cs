using UnityEngine;

// 敵人受到的妨害效果狀態(我方施加於敵方):放在 EnemyBase 層,不寫死在 DogeEnemy,未來新敵人 subclass EnemyBase 就能直接用。
// 眩暈/麻痺各自獨立計時,疊加只延長剩餘時間、倍率固定不疊乘;擊退不走這裡的計時器,只負責圖示顯示,實際硬直/位移仍由
// EnemyBase 既有的 KnockbackState/TryKnockback 處理;失焦鎖定一個瞄準位置,不可疊加(生效中重複觸發直接忽略)。
// 比照 KnockbackState 的分工:這個類別不直接碰行為邏輯,EnemyBase/子類別自己讀取這裡曝露的唯讀狀態去套用。
public class EnemyDebuffState {
    const float slowMoveSpeedMultiplier = 0.5f;
    const float slowCooldownTimeMultiplier = 1.5f;

    readonly DebuffIconStack iconStack;

    float stunTimer;
    float slowTimer;
    float targetLockTimer;
    Vector2 targetLockPosition;

    public bool IsStunned => stunTimer > 0f;
    public float MoveSpeedMultiplier => slowTimer > 0f ? slowMoveSpeedMultiplier : 1f;
    public float CooldownTimeMultiplier => slowTimer > 0f ? slowCooldownTimeMultiplier : 1f;
    public bool HasTargetLock => targetLockTimer > 0f;
    public Vector2 TargetLockPosition => targetLockPosition;

    public EnemyDebuffState(GameObject ownerObject, GameObject iconPrefab, Vector3 iconLocalOffset) {
        iconStack = new DebuffIconStack(ownerObject, iconPrefab, iconLocalOffset);
    }

    // 疊加只延長時間:回傳這次呼叫是不是「從無到有」的第一次觸發,EnemyBase 用來決定要不要順便打斷手上的行為
    public bool ApplyStun(float duration) {
        if (duration <= 0f) return false;
        bool wasActive = stunTimer > 0f;
        if (!wasActive) iconStack.NotifyApplied(DebuffType.Stun);
        stunTimer += duration;
        return !wasActive;
    }

    public void ApplySlow(float duration) {
        if (duration <= 0f) return;
        if (slowTimer <= 0f) iconStack.NotifyApplied(DebuffType.Slow);
        slowTimer += duration;
    }

    // 不可疊加:生效中再次觸發直接忽略,位置/時間都不更新
    public void ApplyTargetLock(Vector2 position, float duration) {
        if (duration <= 0f || HasTargetLock) return;
        targetLockPosition = position;
        targetLockTimer = duration;
        iconStack.NotifyApplied(DebuffType.TargetLock);
    }

    // 擊退不可疊加、也不走時間疊層:硬直觸發/結束時各呼叫一次,單純負責圖示顯示
    public void NotifyKnockbackApplied() => iconStack.NotifyApplied(DebuffType.Knockback);
    public void NotifyKnockbackEnded() => iconStack.NotifyExpired(DebuffType.Knockback);

    // 由 EnemyBase.FixedUpdate 每幀呼叫:倒數各效果計時器
    public void Tick(float deltaTime) {
        if (stunTimer > 0f) {
            stunTimer -= deltaTime;
            if (stunTimer <= 0f) { stunTimer = 0f; iconStack.NotifyExpired(DebuffType.Stun); }
        }

        if (slowTimer > 0f) {
            slowTimer -= deltaTime;
            if (slowTimer <= 0f) { slowTimer = 0f; iconStack.NotifyExpired(DebuffType.Slow); }
        }

        if (targetLockTimer > 0f) {
            targetLockTimer -= deltaTime;
            if (targetLockTimer <= 0f) { targetLockTimer = 0f; iconStack.NotifyExpired(DebuffType.TargetLock); }
        }
    }
}
