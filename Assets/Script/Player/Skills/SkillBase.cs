using UnityEngine;

public interface ISkill {
    float Cooldown { get; }
    float CooldownRemaining { get; } // 距離下次可施放還剩幾秒,給 UI 顯示用
    bool TryExecute();

    bool IsActive { get; } // 目前是否有進行中的位移,PlayerBase 用來決定要不要略過 WASD 移動
    void Tick(); // 進行中時每個 FixedUpdate 呼叫一次,技能自己處理位移
    void Interrupt(); // 被牆壁打斷時呼叫
    void OnHitEnemy(Collider2D enemyCollider); // 角色本體撞到敵人時呼叫(例如衝撞命中)
}
