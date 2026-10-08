using System;
using UnityEngine;
using UnityEngine.UI;

// 可重複使用的設定面板(解析度/全螢幕/音量),給大廳或任何非戰鬥介面用:呼叫 Open() 打開,按套用或關閉就收起來。
// 真正的設定邏輯/存檔在 DisplaySettings,這裡只做 UI 同步;UI 物件在 Inspector 手動接線,本腳本不生成任何 UI。
// 本元件所在的物件在場景裡被關掉也沒關係(例如編輯版面時順手關掉忘了開):Open() 會先把自己打開,
// 觸發延後的 Awake(建選項/掛 listener/先把 panelRoot 關掉),再打開 panelRoot
public class SettingsPanelUI : MonoBehaviour {
    [SerializeField] GameObject panelRoot; // 面板根物件,預設關閉
    [SerializeField] Dropdown resolutionDropdown; // 選項在 Awake() 依螢幕原生解析度動態產生,不用在 Inspector 預先填
    [SerializeField] Toggle fullscreenToggle;
    [SerializeField] Button applyButton;
    [SerializeField] Button closeButton; // 關閉:不套用解析度/全螢幕的變更(音量是拖動當下就生效,不受影響)

    [Header("音量 (拖動當下立刻生效並記錄,不需要按套用;滑桿的 min/max 在 Inspector 設幾都行,這裡換算成 0~1)")]
    [SerializeField] Slider masterVolumeSlider; // 總音量 Audio
    [SerializeField] Slider bgmVolumeSlider;
    [SerializeField] Slider sfxVolumeSlider;

    [SerializeField] string buttonClickSfx = "button";

    // 開/關面板時通知呼叫端,例如戰鬥中要跟著暫停/恢復 Time.timeScale(按套用也會先觸發 Closed 再觸發 Applied)
    public event Action Opened;
    public event Action Closed;
    // 套用完成後通知呼叫端,例如戰鬥中需要重載場景重算牆壁/鏡頭範圍;大廳這種純 UI 畫面不用管
    public event Action Applied;

    public bool IsOpen => panelRoot.activeInHierarchy;

    void Awake() {
        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(DisplaySettings.ResolutionLabels);

        panelRoot.SetActive(false);

        applyButton.onClick.AddListener(OnApply);
        closeButton.onClick.AddListener(OnCloseClicked);
        fullscreenToggle.onValueChanged.AddListener(isFullscreen => resolutionDropdown.interactable = !isFullscreen);

        masterVolumeSlider.onValueChanged.AddListener(_ => AudioManager.Instance.SetMasterVolume(ToVolume(masterVolumeSlider)));
        bgmVolumeSlider.onValueChanged.AddListener(_ => AudioManager.Instance.SetBGMVolume(ToVolume(bgmVolumeSlider)));
        sfxVolumeSlider.onValueChanged.AddListener(_ => AudioManager.Instance.SetSFXVolume(ToVolume(sfxVolumeSlider)));
    }

    public void Open() {
        gameObject.SetActive(true);
        SyncUIToCurrentScreenState();
        panelRoot.SetActive(true);
        Opened?.Invoke();
    }

    public void Close() {
        panelRoot.SetActive(false);
        PlayerPrefs.Save(); // 音量拖動時只寫進 PlayerPrefs 記憶體,關面板時統一寫硬碟
        Closed?.Invoke();
    }

    // 把 UI 顯示同步成螢幕目前實際的狀態,上次打開時改了沒套用的值會被丟掉
    void SyncUIToCurrentScreenState() {
        bool fullscreen = DisplaySettings.IsFullscreen;
        fullscreenToggle.SetIsOnWithoutNotify(fullscreen);
        resolutionDropdown.SetValueWithoutNotify(DisplaySettings.CurrentResolutionIndex);
        resolutionDropdown.interactable = !fullscreen;

        AudioManager audioManager = AudioManager.Instance;
        SetSliderVolume(masterVolumeSlider, audioManager.MasterVolume);
        SetSliderVolume(bgmVolumeSlider, audioManager.BgmVolume);
        SetSliderVolume(sfxVolumeSlider, audioManager.SfxVolume);
    }

    static float ToVolume(Slider slider) => Mathf.InverseLerp(slider.minValue, slider.maxValue, slider.value);

    static void SetSliderVolume(Slider slider, float volume) {
        slider.SetValueWithoutNotify(Mathf.Lerp(slider.minValue, slider.maxValue, volume));
    }

    void OnApply() {
        AudioManager.Instance.PlaySFX(buttonClickSfx);
        DisplaySettings.Apply(fullscreenToggle.isOn, resolutionDropdown.value);
        Close();
        Applied?.Invoke();
    }

    void OnCloseClicked() {
        AudioManager.Instance.PlaySFX(buttonClickSfx);
        Close();
    }
}
