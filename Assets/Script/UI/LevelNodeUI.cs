using UnityEngine;
using UnityEngine.UI;

// 選關地圖上的一個關卡節點:鎖住時顯示灰色剪影且不能點,解鎖後顯示通關星星。點擊事件由 LevelMapUI 統一接
public class LevelNodeUI : MonoBehaviour {
    [SerializeField] LevelData level;
    [SerializeField] Button button;
    [SerializeField] Image circleImage;
    [SerializeField] Text label;
    [SerializeField] StarRowUI stars;
    [SerializeField] GameObject selectedFrame; // 目前選中的節點外框
    [SerializeField] Color unlockedColor = Color.white;
    [SerializeField] Color lockedColor = new Color(0.2f, 0.2f, 0.22f);
    [SerializeField] Color unlockedLabelColor = new Color(0.15f, 0.15f, 0.15f);
    [SerializeField] Color lockedLabelColor = new Color(0.4f, 0.4f, 0.42f);

    public LevelData Level => level;
    public Button Button => button;
    public bool IsUnlocked { get; private set; }

    public void Refresh() {
        IsUnlocked = LevelProgressRules.IsUnlocked(level);
        button.interactable = IsUnlocked;
        circleImage.color = IsUnlocked ? unlockedColor : lockedColor;
        label.color = IsUnlocked ? unlockedLabelColor : lockedLabelColor;
        stars.gameObject.SetActive(IsUnlocked);
        stars.SetLitCount(LevelProgressRules.LitStarCount(level));
    }

    public void SetSelected(bool selected) => selectedFrame.SetActive(selected);
}
