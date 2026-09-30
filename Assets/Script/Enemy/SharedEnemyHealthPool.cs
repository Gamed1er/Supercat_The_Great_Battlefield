// PhotoCat/JurassicCat 共用同一包血量(見 PairedBossEnemy):純資料類別,不認識任何一隻的細節,
// 只負責「扣血、夾在 0 以上、回報是否見底」,實際要不要死亡/怎麼同步回兩隻的 health 欄位由 PairedBossEnemy 處理。
public class SharedEnemyHealthPool {
    public float Max { get; }
    public float Current { get; private set; }
    public bool IsDepleted => Current <= 0f;

    public SharedEnemyHealthPool(float maxHealth) {
        Max = maxHealth;
        Current = maxHealth;
    }

    public void ApplyDamage(float amount) {
        if (amount <= 0f || IsDepleted) return;
        Current = UnityEngine.Mathf.Max(0f, Current - amount);
    }
}
