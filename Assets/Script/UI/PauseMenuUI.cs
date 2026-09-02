using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Esc 開關的暫停/設定面板:凍結戰鬥(Time.timeScale = 0,BGM 不受影響照常播)、
// 切換視窗解析度(固定 16:9 預設清單)/全螢幕(FullScreenWindow,固定用螢幕原生解析度)。
// 面板內 UI 物件(Panel/Dropdown/Toggle/Button)在 Inspector 手動接線,本腳本不生成任何 UI 物件——
// 跟 UIManager 的 Image/Text 欄位是同一套手動接線模式。
public class PauseMenuUI : MonoBehaviour {
    static readonly Vector2Int[] ResolutionPresets = {
        new Vector2Int(1280, 720),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080),
        new Vector2Int(2560, 1440),
    };

    const string PrefKeyResolutionIndex = "Settings_ResolutionIndex";
    const string PrefKeyFullscreen = "Settings_Fullscreen";

    [Header("面板")]
    [SerializeField] GameObject panelRoot; // 暫停/設定面板的根物件,預設關閉

    [Header("設定 UI")]
    [SerializeField] Dropdown resolutionDropdown; // 選項在 Awake() 依螢幕原生解析度動態產生,不用在 Inspector 預先填
    [SerializeField] Toggle fullscreenToggle; // 開 = 全螢幕(FullScreenWindow,用螢幕原生解析度);關 = 視窗模式(用上面選的解析度)
    [SerializeField] Button applyButton; // 套用:存 PlayerPrefs + Screen.SetResolution + 重新載入本場景
    [SerializeField] Button resumeButton; // 返回:單純關閉面板,不套用任何變更

    List<Vector2Int> availableResolutions;

    void Awake() {
        BuildResolutionOptions();
        ApplySavedSettingsIfAny();
        SyncUIToCurrentScreenState();

        if (panelRoot != null) panelRoot.SetActive(false);

        if (applyButton != null) applyButton.onClick.AddListener(OnApply);
        if (resumeButton != null) resumeButton.onClick.AddListener(ClosePanel);
        if (fullscreenToggle != null) fullscreenToggle.onValueChanged.AddListener(OnFullscreenToggleChanged);
    }

    void Update() {
        if (Input.GetKeyDown(KeyCode.Escape)) TogglePanel();
    }

    // 過濾掉比螢幕原生解析度還大的預設選項,避免在小螢幕上選到放不下的視窗大小
    void BuildResolutionOptions() {
        Resolution native = Screen.currentResolution;
        availableResolutions = ResolutionPresets.Where(r => r.x <= native.width && r.y <= native.height).ToList();
        if (availableResolutions.Count == 0) availableResolutions = ResolutionPresets.ToList();

        if (resolutionDropdown == null) return;

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(availableResolutions.Select(r => $"{r.x} x {r.y}").ToList());
    }

    // 只有存過設定(PlayerPrefs)才套用——沒存過就維持 Player Settings 裡的預設值,不強制覆蓋
    void ApplySavedSettingsIfAny() {
        if (!PlayerPrefs.HasKey(PrefKeyFullscreen)) return;

        bool fullscreen = PlayerPrefs.GetInt(PrefKeyFullscreen) == 1;
        int index = Mathf.Clamp(PlayerPrefs.GetInt(PrefKeyResolutionIndex, 0), 0, availableResolutions.Count - 1);

        ApplyScreenSettings(fullscreen, availableResolutions[index]);
    }

    // 把面板 UI 顯示的選項同步成螢幕目前實際的狀態(剛啟動的預設值,或上面套用完的存檔值)
    void SyncUIToCurrentScreenState() {
        bool fullscreen = Screen.fullScreenMode == FullScreenMode.FullScreenWindow;

        if (fullscreenToggle != null) fullscreenToggle.SetIsOnWithoutNotify(fullscreen);

        if (resolutionDropdown != null) {
            resolutionDropdown.SetValueWithoutNotify(FindClosestResolutionIndex(new Vector2Int(Screen.width, Screen.height)));
            resolutionDropdown.interactable = !fullscreen;
        }
    }

    int FindClosestResolutionIndex(Vector2Int current) {
        int index = availableResolutions.FindIndex(r => r.x == current.x && r.y == current.y);
        return index >= 0 ? index : 0;
    }

    void OnFullscreenToggleChanged(bool isFullscreen) {
        if (resolutionDropdown != null) resolutionDropdown.interactable = !isFullscreen;
    }

    void TogglePanel() {
        if (panelRoot == null) return;

        if (panelRoot.activeSelf) ClosePanel();
        else OpenPanel();
    }

    void OpenPanel() {
        SyncUIToCurrentScreenState();
        panelRoot.SetActive(true);
        Time.timeScale = 0f;
    }

    void ClosePanel() {
        if (panelRoot != null) panelRoot.SetActive(false);
        Time.timeScale = 1f;
    }

    void OnApply() {
        bool fullscreen = fullscreenToggle != null && fullscreenToggle.isOn;
        int index = Mathf.Clamp(resolutionDropdown != null ? resolutionDropdown.value : 0, 0, availableResolutions.Count - 1);

        PlayerPrefs.SetInt(PrefKeyFullscreen, fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(PrefKeyResolutionIndex, index);
        PlayerPrefs.Save();

        ApplyScreenSettings(fullscreen, availableResolutions[index]);

        // 重載場景讓 LevelManager.Awake() 用新的 aspect 重算牆壁/背景/鏡頭範圍(牆壁位置等是進場時一次性算好,不會自動跟著改)
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    static void ApplyScreenSettings(bool fullscreen, Vector2Int windowedResolution) {
        if (fullscreen) {
            Resolution native = Screen.currentResolution;
            Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
        } else {
            Screen.SetResolution(windowedResolution.x, windowedResolution.y, FullScreenMode.Windowed);
        }
    }
}
