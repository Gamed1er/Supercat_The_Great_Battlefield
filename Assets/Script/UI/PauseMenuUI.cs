using UnityEngine;
using UnityEngine.SceneManagement;

// 戰鬥中的暫停選單:只負責戰鬥專屬的行為——Esc 開關設定面板、開著時凍結戰鬥(Time.timeScale = 0,BGM 不受影響照常播)、
// 套用設定後重載場景。面板本身(解析度/全螢幕 UI)用共用的 SettingsPanelUI,跟大廳是同一套。
// 之後暫停選單要加戰鬥專屬按鈕(例如回大廳)也放在這裡,不要塞進共用的 SettingsPanelUI
public class PauseMenuUI : MonoBehaviour {
    [SerializeField] SettingsPanelUI settingsPanel;

    void Awake() {
        settingsPanel.Opened += () => Time.timeScale = 0f;
        settingsPanel.Closed += () => Time.timeScale = 1f;
        settingsPanel.Applied += ReloadScene;
    }

    void Update() {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        if (settingsPanel.IsOpen) settingsPanel.Close();
        else settingsPanel.Open();
    }

    // 重載場景讓 LevelManager.Awake() 用新的 aspect 重算牆壁/背景/鏡頭範圍(牆壁位置等是進場時一次性算好,不會自動跟著改)
    static void ReloadScene() {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
