using System;
using UnityEngine;
using UnityEngine.UI;

// 選關畫面右側的關卡詳細面板:標題/簡介、通關星星與無傷標記、目前難度的最佳時間、難度箭頭、劇情勾選、出戰角色按鈕、出發按鈕。
// 只負責顯示與收集選項,按下角色/出發按鈕時丟事件給 LobbyMenuUI 處理
public class LevelDetailPanelUI : MonoBehaviour {
    [SerializeField] Text titleText;
    [SerializeField] Text descriptionText;
    [SerializeField] Toggle playStoryToggle; // 勾選 = 出發前播劇情
    [SerializeField] StarRowUI stars;
    [SerializeField] GameObject noDamageBadge; // 曾在最高難度無傷通關才顯示
    [SerializeField] Text bestTimeText;
    [SerializeField] Button difficultyDownButton;
    [SerializeField] Button difficultyUpButton;
    [SerializeField] Text difficultyText;
    [SerializeField] Button characterButton;
    [SerializeField] Image characterPortraitImage;
    [SerializeField] Button startButton;
    [SerializeField] int defaultDifficulty = 2;

    public event Action CharacterButtonClicked;
    public event Action StartClicked;

    // 難度全域共用一個值(不分關卡),跨場景記住、不存檔;切換關卡時依那關的上限夾住
    static int? selectedDifficulty;

    public LevelData Level { get; private set; }
    public int Difficulty => selectedDifficulty.Value;
    public bool PlayStory => playStoryToggle.isOn;

    void Awake() {
        selectedDifficulty ??= GameFlow.CurrentBattle != null ? GameFlow.CurrentBattle.Difficulty : defaultDifficulty;

        difficultyDownButton.onClick.AddListener(() => ChangeDifficulty(-1));
        difficultyUpButton.onClick.AddListener(() => ChangeDifficulty(+1));
        characterButton.onClick.AddListener(() => CharacterButtonClicked?.Invoke());
        startButton.onClick.AddListener(() => StartClicked?.Invoke());
    }

    public void Show(LevelData level) {
        Level = level;
        gameObject.SetActive(level != null);
        if (level == null) return;

        titleText.text = level.mapTitle;
        descriptionText.text = level.description;

        RivalProgress progress = LevelProgressRules.GetProgress(level);
        stars.SetLitCount(LevelProgressRules.LitStarCount(level));
        noDamageBadge.SetActive(progress.hasAchievedNoDamageMaxDifficulty);

        // 沒有劇情:勾不了;劇情還沒看過:強制播放;看過:預設不播,可自己勾
        bool hasStory = GameFlow.HasPreBattleStory(level);
        bool hasSeenStory = LevelProgressRules.HasSeenStory(level);
        playStoryToggle.SetIsOnWithoutNotify(hasStory && !hasSeenStory);
        playStoryToggle.interactable = hasStory && hasSeenStory;

        SetDifficulty(selectedDifficulty.Value);
    }

    public void SetCharacter(PlayerBase character) => characterPortraitImage.sprite = character.Portrait;

    void ChangeDifficulty(int delta) {
        AudioManager.Instance.PlaySFX("button");
        SetDifficulty(selectedDifficulty.Value + delta);
    }

    void SetDifficulty(int difficulty) {
        int max = LevelProgressRules.MaxSelectableDifficulty(Level);
        selectedDifficulty = Mathf.Clamp(difficulty, 0, max);

        difficultyText.text = selectedDifficulty.Value.ToString();
        difficultyDownButton.interactable = selectedDifficulty.Value > 0;
        difficultyUpButton.interactable = selectedDifficulty.Value < max;

        float bestTime = LevelProgressRules.GetProgress(Level).GetOrInitBestClearTimes()[selectedDifficulty.Value];
        bestTimeText.text = bestTime < 0f ? "最佳時間  --:--.--" : $"最佳時間  {FormatTime(bestTime)}";
    }

    static string FormatTime(float seconds) {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 10:00}";
    }
}
