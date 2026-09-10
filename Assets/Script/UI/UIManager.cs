using System;
using UnityEngine;
using UnityEngine.UI;

// 檢查玩家/敵人狀態,更新 PlayerUI:Heart(血量圖+文字)、Skill(戰技冷卻)、Ultimate(終結技充能)、Enemy(敵人血條+名稱+百分比)。
public class UIManager : MonoBehaviour {
    static readonly Color HealthHighColor = ParseColor("C0FF93");
    static readonly Color HealthMidColor = ParseColor("FFE456");
    static readonly Color HealthLowColor = ParseColor("F33D4A");

    [Header("Heart")]
    public Image heartFillImage; // Fill Amount = 血量比例,顏色依比例變化
    public Text heartText; // 原始血量數字

    [Header("S1 (技能槽1)")]
    public Image S1_FillImage; // SkillCenter,Fill Amount = 冷卻完成度(1 就緒 / 0 剛用)
    public Text S1_Text; // 剩餘冷卻秒數

    [Header("S2 (技能槽2)")]
    public Image S2_FillImage; // SkillCenter,Fill Amount = 充能比例
    public Text S2_Text; // 充能 n / m

    [Header("Enemy")]
    public Image enemyFillImage; // Fill Amount = 血量比例,1 滿血 / 0 死亡
    public Text enemyNameText; // 敵人名稱,固定顯示,不會變
    public Text enemyHealthText; // 血量百分比,n% (無條件進位)

    PlayerBase player;
    LevelManager levelManager;

    // 用 Start() 而不是 Awake():LevelManager 是在自己的 Awake() 裡動態生成玩家/敵人,
    // 不同物件的 Awake 執行順序不保證,但 Unity 保證所有物件的 Awake 都跑完後才會進到任何一個 Start()
    void Start() {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.GetComponent<PlayerBase>();

        levelManager = LevelManager.Instance;
        if (levelManager != null && enemyNameText != null) enemyNameText.text = levelManager.LevelName;
    }

    void Update() {
        if (player != null) {
            UpdateHeart();
            UpdateS1();
            UpdateS2();
        }

        if (levelManager != null) UpdateEnemy();
    }

    void UpdateHeart() {
        float ratio = player.MaxHealth > 0f ? Mathf.Clamp01(player.Health / player.MaxHealth) : 0f;

        if (heartFillImage != null) {
            heartFillImage.fillAmount = ratio;
            heartFillImage.color = ratio >= 0.66f ? HealthHighColor : ratio >= 0.33f ? HealthMidColor : HealthLowColor;
        }

        if (heartText != null) heartText.text = Mathf.CeilToInt(player.Health).ToString();
    }

    void UpdateS1(string chargeType = "time") {
        
        float ratio = player.S1_CooldownCurrent > 0f ? Mathf.Clamp01(player.S1_CooldownCurrent / player.S1_Cooldown) : 0f;

        if (S1_FillImage != null) S1_FillImage.fillAmount = ratio;
        if (S1_Text != null) S1_Text.text = $"{player.S1_Cooldown * (1f - ratio):F1}s";
    }

    void UpdateS2(string chargeType = "time") {
        float ratio = player.S2_CooldownCurrent > 0f ? Mathf.Clamp01(player.S2_CooldownCurrent / player.S2_Cooldown) : 0f;

        if (S2_FillImage != null) S2_FillImage.fillAmount = ratio;
        if (S2_Text != null) S2_Text.text = $"{Mathf.FloorToInt(player.S2_CooldownCurrent)} / {Mathf.FloorToInt(player.S2_Cooldown)}";
    }

    void UpdateEnemy() {
        float ratio = levelManager.EnemyGroupHealthRatio;

        if (enemyFillImage != null) enemyFillImage.fillAmount = ratio;
        if (enemyHealthText != null) enemyHealthText.text = $"{Mathf.CeilToInt(ratio * 100f)}%";
    }

    static Color ParseColor(string hex) {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        return color;
    }
}
