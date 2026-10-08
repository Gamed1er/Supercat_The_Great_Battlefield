using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 畫面設定(視窗解析度/全螢幕)的實際邏輯與存檔,不含任何 UI:
// PauseMenuUI(戰鬥中)跟 SettingsPanelUI(大廳等其他介面)都只負責把自己的 Dropdown/Toggle 接到這裡。
// 存過的設定在遊戲啟動時(第一個場景載入前)自動套用一次,沒存過就維持 Player Settings 的預設值,不強制覆蓋
public static class DisplaySettings {
    static readonly Vector2Int[] ResolutionPresets = {
        new Vector2Int(1280, 720),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080),
        new Vector2Int(2560, 1440),
    };

    const string PrefKeyResolutionIndex = "Settings_ResolutionIndex";
    const string PrefKeyFullscreen = "Settings_Fullscreen";

    static List<Vector2Int> availableResolutions;

    // 過濾掉比螢幕原生解析度還大的預設選項,避免在小螢幕上選到放不下的視窗大小
    public static IReadOnlyList<Vector2Int> AvailableResolutions {
        get {
            if (availableResolutions == null) {
                Resolution native = Screen.currentResolution;
                availableResolutions = ResolutionPresets.Where(r => r.x <= native.width && r.y <= native.height).ToList();
                if (availableResolutions.Count == 0) availableResolutions = ResolutionPresets.ToList();
            }
            return availableResolutions;
        }
    }

    public static List<string> ResolutionLabels => AvailableResolutions.Select(r => $"{r.x} x {r.y}").ToList();

    public static bool IsFullscreen => Screen.fullScreenMode == FullScreenMode.FullScreenWindow;

    // 目前視窗大小在 AvailableResolutions 裡的索引,不在清單內時回傳 0
    public static int CurrentResolutionIndex {
        get {
            for (int i = 0; i < AvailableResolutions.Count; i++) {
                if (AvailableResolutions[i].x == Screen.width && AvailableResolutions[i].y == Screen.height) return i;
            }
            return 0;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplySavedSettingsOnLaunch() {
        if (!PlayerPrefs.HasKey(PrefKeyFullscreen)) return;

        bool fullscreen = PlayerPrefs.GetInt(PrefKeyFullscreen) == 1;
        ApplyScreenSettings(fullscreen, ClampIndex(PlayerPrefs.GetInt(PrefKeyResolutionIndex, 0)));
    }

    // 存 PlayerPrefs 並立刻套用。注意 Screen.SetResolution 要到下一幀才真正生效,
    // 依賴螢幕比例、只在進場時算一次的東西(例如 LevelManager 的牆壁/鏡頭範圍)要由呼叫端自己決定要不要重載場景
    public static void Apply(bool fullscreen, int resolutionIndex) {
        int index = ClampIndex(resolutionIndex);

        PlayerPrefs.SetInt(PrefKeyFullscreen, fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(PrefKeyResolutionIndex, index);
        PlayerPrefs.Save();

        ApplyScreenSettings(fullscreen, index);
    }

    static int ClampIndex(int index) => Mathf.Clamp(index, 0, AvailableResolutions.Count - 1);

    static void ApplyScreenSettings(bool fullscreen, int resolutionIndex) {
        if (fullscreen) {
            Resolution native = Screen.currentResolution;
            Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
        } else {
            Vector2Int windowed = AvailableResolutions[resolutionIndex];
            Screen.SetResolution(windowed.x, windowed.y, FullScreenMode.Windowed);
        }
    }
}
