using UnityEngine;

// 玩家受到的妨害效果狀態(敵方施加於我方):眩暈/麻痺/詛咒各自獨立計時,疊加只延長剩餘時間、倍率固定不疊乘;
// 擊退不走這裡的計時器,只負責圖示顯示——實際硬直/位移仍由 PlayerBase 既有的 KnockbackState/TryKnockback 處理,
// 這裡只在硬直開始/結束時被通知一聲。比照 KnockbackState 的分工:這個類別不直接碰 Rigidbody2D/ISkill,
// PlayerBase 每個 FixedUpdate 呼叫 Tick 後,自己讀取這裡曝露的唯讀狀態去套用到移動/技能上。
public class PlayerDebuffState {
    const float slowMoveSpeedMultiplier = 0.5f;
    const float slowCooldownMultiplier = 1.5f;

    const float curseFallAcceleration = 6f; // 詛咒下墜加速度(單位/秒^2),先給合理預設值,可在這裡微調
    const float curseMaxFallSpeed = 5f; // 詛咒下墜終端速度
    const float curseUpwardInputMultiplier = 0.15f; // 詛咒期間向上輸入的殘留比例(大幅減弱,但不是完全歸零)

    readonly DebuffIconStack iconStack;

    float stunTimer;
    float slowTimer;
    float curseTimer;
    float curseFallSpeed; // 詛咒期間目前的下墜速度,隨時間加速到 curseMaxFallSpeed,效果結束歸零

    public bool IsStunned => stunTimer > 0f;
    public bool IsCursed => curseTimer > 0f;
    public bool IsSkillLocked => IsCursed; // 詛咒封鎖 dash/ultimate,普攻不受影響(PlayerBase.Update 讀這個)
    public float MoveSpeedMultiplier => slowTimer > 0f ? slowMoveSpeedMultiplier : 1f;
    public float SkillCooldownMultiplier => slowTimer > 0f ? slowCooldownMultiplier : 1f;

    public PlayerDebuffState(GameObject ownerObject, GameObject iconPrefab, Vector3 iconLocalOffset) {
        iconStack = new DebuffIconStack(ownerObject, iconPrefab, iconLocalOffset);
    }

    // 疊加只延長時間:回傳這次呼叫是不是「從無到有」的第一次觸發,PlayerBase 用來決定要不要順便打斷手上的技能
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

    public void ApplyCurse(float duration) {
        if (duration <= 0f) return;
        if (curseTimer <= 0f) iconStack.NotifyApplied(DebuffType.Curse);
        curseTimer += duration;
    }

    // 擊退不可疊加、也不走時間疊層:硬直觸發/結束時各呼叫一次,單純負責圖示顯示
    public void NotifyKnockbackApplied() => iconStack.NotifyApplied(DebuffType.Knockback);
    public void NotifyKnockbackEnded() => iconStack.NotifyExpired(DebuffType.Knockback);

    // 由 PlayerBase.FixedUpdate 每幀呼叫:倒數各效果計時器,並更新詛咒下墜速度
    public void Tick(float deltaTime) {
        if (stunTimer > 0f) {
            stunTimer -= deltaTime;
            if (stunTimer <= 0f) { stunTimer = 0f; iconStack.NotifyExpired(DebuffType.Stun); }
        }

        if (slowTimer > 0f) {
            slowTimer -= deltaTime;
            if (slowTimer <= 0f) { slowTimer = 0f; iconStack.NotifyExpired(DebuffType.Slow); }
        }

        if (curseTimer > 0f) {
            curseTimer -= deltaTime;
            curseFallSpeed = Mathf.Min(curseFallSpeed + curseFallAcceleration * deltaTime, curseMaxFallSpeed);
            if (curseTimer <= 0f) { curseTimer = 0f; curseFallSpeed = 0f; iconStack.NotifyExpired(DebuffType.Curse); }
        } else {
            curseFallSpeed = 0f;
        }
    }

    // 詛咒套用在這一幀移動速度上的修正:水平與向下不受影響,向上大幅折減,並疊加持續下墜的速度。
    // 非詛咒狀態時原樣回傳,呼叫端不需要另外判斷 IsCursed。
    public Vector2 ApplyCurseToVelocity(Vector2 rawVelocity) {
        if (!IsCursed) return rawVelocity;

        float y = rawVelocity.y > 0f ? rawVelocity.y * curseUpwardInputMultiplier : rawVelocity.y;
        return new Vector2(rawVelocity.x, y - curseFallSpeed);
    }
}
