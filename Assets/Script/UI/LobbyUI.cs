using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 臨時測試用大廳(Lobby.unity):選角色、選難度、勾選是否跳過劇情,按關卡按鈕直接出發;另有設定與離開遊戲按鈕。
// 這支腳本只把按鈕操作翻譯成 GameFlow / SettingsPanelUI 的呼叫,出戰流程與設定邏輯都不在這裡——
// 之後做正式大廳時換掉這支腳本跟場景即可,GameFlow/DisplaySettings/SettingsPanelUI 照用。
// UI 物件在 Inspector 手動接線(可用 Tools > Lobby > Build Test Lobby Scene 自動生成場景並接好)
public class LobbyUI : MonoBehaviour {
    [System.Serializable]
    public class CharacterOption {
        public Button button;
        public GameObject playerPrefab;
    }

    [System.Serializable]
    public class LevelOption {
        public Button button;
        public LevelData level;
    }

    [Header("角色 (選中的按鈕會換顏色)")]
    [SerializeField] List<CharacterOption> characterOptions = new List<CharacterOption>();
    [SerializeField] Color selectedCharacterColor = new Color(1f, 0.85f, 0.3f);
    [SerializeField] Color unselectedCharacterColor = Color.white;

    [Header("關卡 (按下直接出發)")]
    [SerializeField] List<LevelOption> levelOptions = new List<LevelOption>();

    [Header("難度")]
    [SerializeField] Slider difficultySlider; // 範圍在 Start() 依 EnemyBase.MaxDifficulty 設定,不用在 Inspector 調
    [SerializeField] Text difficultyLabel;
    [SerializeField] int defaultDifficulty = 2;

    [Header("敵人資訊 (測試版專用,跟著難度滑桿即時更新)")]
    [SerializeField] LobbyEnemyInfoPanel enemyInfoPanel;

    [Header("劇情")]
    [SerializeField] Toggle skipStoryToggle; // 勾選 = 有劇情的關卡也直接進戰鬥

    [Header("其他")]
    [SerializeField] Button settingsButton;
    [SerializeField] SettingsPanelUI settingsPanel;
    [SerializeField] Button quitButton;

    [SerializeField] string buttonClickSfx = "button";

    // 跳過劇情的勾選狀態跨場景記住(角色/難度則從 GameFlow.CurrentBattle 還原);只活在這次遊戲執行期間,不存檔
    static bool rememberedSkipStory;

    int selectedCharacterIndex;

    void Start() {
        for (int i = 0; i < characterOptions.Count; i++) {
            int index = i;
            characterOptions[i].button.onClick.AddListener(() => { PlayClickSfx(); SelectCharacter(index); });
        }

        foreach (LevelOption option in levelOptions) {
            LevelData level = option.level;
            option.button.onClick.AddListener(() => { PlayClickSfx(); StartLevel(level); });
        }

        difficultySlider.wholeNumbers = true;
        difficultySlider.minValue = 0;
        difficultySlider.maxValue = EnemyBase.MaxDifficulty;
        difficultySlider.onValueChanged.AddListener(_ => UpdateDifficultyLabel());

        skipStoryToggle.onValueChanged.AddListener(isOn => rememberedSkipStory = isOn);

        settingsButton.onClick.AddListener(() => { PlayClickSfx(); settingsPanel.Open(); });
        quitButton.onClick.AddListener(() => { PlayClickSfx(); GameFlow.QuitGame(); });

        RestoreLastSelection();
    }

    void Update() {
        if (Input.GetKeyDown(KeyCode.Escape) && settingsPanel.IsOpen) settingsPanel.Close();
    }

    // 從戰鬥回到大廳時沿用上一場的角色/難度,第一次進來則用第一個角色 + defaultDifficulty
    void RestoreLastSelection() {
        BattleSelection last = GameFlow.CurrentBattle;

        int characterIndex = last != null ? characterOptions.FindIndex(o => o.playerPrefab == last.PlayerPrefab) : -1;
        SelectCharacter(Mathf.Max(0, characterIndex));

        difficultySlider.SetValueWithoutNotify(last != null ? last.Difficulty : defaultDifficulty);
        UpdateDifficultyLabel();

        skipStoryToggle.SetIsOnWithoutNotify(rememberedSkipStory);
    }

    void SelectCharacter(int index) {
        selectedCharacterIndex = index;
        for (int i = 0; i < characterOptions.Count; i++) {
            characterOptions[i].button.image.color = i == index ? selectedCharacterColor : unselectedCharacterColor;
        }
    }

    void UpdateDifficultyLabel() {
        difficultyLabel.text = $"難度 {(int)difficultySlider.value} / {EnemyBase.MaxDifficulty}";
        enemyInfoPanel.Refresh(levelOptions.ConvertAll(option => option.level), (int)difficultySlider.value);
    }

    void StartLevel(LevelData level) {
        BattleSelection selection = new BattleSelection(characterOptions[selectedCharacterIndex].playerPrefab, level, (int)difficultySlider.value);
        GameFlow.StartBattle(selection, skipStoryToggle.isOn);
    }

    void PlayClickSfx() => AudioManager.Instance.PlaySFX(buttonClickSfx);
}
