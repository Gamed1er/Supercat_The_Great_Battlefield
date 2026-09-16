using UnityEngine;

public interface ISkill {
    float Cooldown { get; }
    float CooldownCurrent { get; } // 距離下次可施放還剩多少進度, UI 顯示用
    // 外部效果(例如麻痺 debuff)拿來縮放冷卻時間用,預設 1,由持有者(PlayerBase)每幀寫入;
    // 沒有實體時間冷卻的技能(例如充能制的 ChargeRamSkill)可以忽略這個值,Cooldown 不乘它
    float CooldownMultiplier { get; set; }
    bool TryExecute();

    bool IsActive { get; } // 目前是否有進行中的位移,PlayerBase 用來決定要不要略過 WASD 移動
    void Tick(); // 進行中時每個 FixedUpdate 呼叫一次,技能自己處理位移
    void Interrupt(); // 被牆壁打斷時呼叫
    void OnHitEnemy(Collider2D enemyCollider); // 角色本體撞到敵人時呼叫(例如衝撞命中)
    void ReduceCooldown(float seconds); // 外部縮短冷卻(例如被動效果),沒有實體冷卻可縮的技能(如充能制)給空實作
}
