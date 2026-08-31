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

    [Header("Skill (戰技)")]
    public Image skillFillImage; // SkillCenter,Fill Amount = 冷卻完成度(1 就緒 / 0 剛用)
    public Text skillText; // 剩餘冷卻秒數

    [Header("Ultimate (終結技)")]
    public Image ultimateFillImage; // SkillCenter,Fill Amount = 充能比例
    public Text ultimateText; // 充能 n / m

    [Header("Enemy")]
    public Image enemyFillImage; // Fill Amount = 血量比例,1 滿血 / 0 死亡
    public Text enemyNameText; // 敵人名稱,固定顯示,不會變
    public Text enemyHealthText; // 血量百分比,n% (無條件進位)

    PlayerBase player;
    DogeEnemy enemy;

    void Awake() {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.GetComponent<PlayerBase>();

        GameObject enemyObj = GameObject.FindGameObjectWithTag("Enemy");
        if (enemyObj != null) enemy = enemyObj.GetComponent<DogeEnemy>();
        if (enemy != null && enemyNameText != null) enemyNameText.text = enemy.enemyName;
    }

    void Update() {
        if (player != null) {
            UpdateHeart();
            UpdateSkill();
            UpdateUltimate();
        }

        if (enemy != null) UpdateEnemy();
    }

    void UpdateHeart() {
        float ratio = player.MaxHealth > 0f ? Mathf.Clamp01(player.Health / player.MaxHealth) : 0f;

        if (heartFillImage != null) {
            heartFillImage.fillAmount = ratio;
            heartFillImage.color = ratio >= 0.66f ? HealthHighColor : ratio >= 0.33f ? HealthMidColor : HealthLowColor;
        }

        if (heartText != null) heartText.text = Mathf.CeilToInt(player.Health).ToString();
    }

    void UpdateSkill() {
        float ratio = player.DashCooldown > 0f ? 1f - Mathf.Clamp01(player.DashCooldownRemaining / player.DashCooldown) : 1f;

        if (skillFillImage != null) skillFillImage.fillAmount = ratio;
        if (skillText != null) skillText.text = $"{player.DashCooldownRemaining:F1}秒";
    }

    void UpdateUltimate() {
        float ratio = player.UltimateMaxCharge > 0f ? Mathf.Clamp01(player.UltimateCharge / player.UltimateMaxCharge) : 0f;

        if (ultimateFillImage != null) ultimateFillImage.fillAmount = ratio;
        if (ultimateText != null) ultimateText.text = $"{Mathf.FloorToInt(player.UltimateCharge)} / {Mathf.FloorToInt(player.UltimateMaxCharge)}";
    }

    void UpdateEnemy() {
        float ratio = enemy.HealthRatio;

        if (enemyFillImage != null) enemyFillImage.fillAmount = ratio;
        if (enemyHealthText != null) enemyHealthText.text = $"{Mathf.CeilToInt(ratio * 100f)}%";
    }

    static Color ParseColor(string hex) {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        return color;
    }
}
