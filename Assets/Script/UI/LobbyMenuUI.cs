using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 正式大廳(Lobby.unity)的總控:標題畫面 ↔ 選關畫面(主線地圖 / Stage 地圖 + 右側詳細面板)之間的切換、BGM、Esc 返回、出發。
// 標題畫面內有兩組按鈕:開始選單(遊戲開始/角色升級/收藏)與關卡類型選單(冒險故事/Stage…/返回),
// 兩組之間用左右滑動切換(不走黑幕);標題 ↔ 選關畫面才走 ScreenFader 黑幕轉場,全黑時才換面板與 BGM。
// 第一次進遊戲停在開始選單;從選關畫面返回標題時停在關卡類型選單;
// 從戰鬥回來(GameFlow.CurrentBattle 不是 null)則直接回到剛才那關所在的地圖並選中它。
// 整個場景由 Tools > Lobby > Build Lobby Scene 生成並接線,這支腳本本身不生成 UI
public class LobbyMenuUI : MonoBehaviour {
    [Header("標題畫面")]
    [SerializeField] GameObject titlePanel;
    [SerializeField] LobbyCatDecorationUI catDecoration; // 跟著目前選定的角色換動畫
    [SerializeField] RectTransform startMenuGroup; // 遊戲開始/角色升級/收藏,撐滿 titlePanel
    [SerializeField] RectTransform stageSelectGroup; // 冒險故事/Stage…/返回,撐滿 titlePanel
    [SerializeField] Button gameStartButton;
    [SerializeField] Button stageSelectBackButton;
    [SerializeField] float menuSlideOutDuration = 0.5f; // 舊的一組滑出畫面
    [SerializeField] float menuSlideInDuration = 0.5f; // 滑出完才接著滑入新的一組
    [SerializeField] Button mainButton;
    [SerializeField] Button stageButton;
    [SerializeField] Button titleSettingsButton;
    [SerializeField] Button quitButton;

    [Header("選關畫面")]
    [SerializeField] GameObject mapScreen;
    [SerializeField] LevelMapUI mainMap;
    [SerializeField] LevelMapUI stageMap;
    [SerializeField] LevelDetailPanelUI detailPanel;
    [SerializeField] CharacterSelectPopupUI characterPopup;
    [SerializeField] Button backButton;
    [SerializeField] Button mapSettingsButton;
    [SerializeField] string battleStartSfx = "battle_start"; // 按下出發後先播完這個音效,才進劇情/戰鬥

    [Header("共用")]
    [SerializeField] SettingsPanelUI settingsPanel;
    [SerializeField] string titleBgm = "lobby";
    [SerializeField] string mapBgm = "map";
    [SerializeField] string buttonClickSfx = "button";

    LevelMapUI activeMap;
    bool isMenuSliding;
    bool isStartingBattle;

    void Start() {
        gameStartButton.onClick.AddListener(() => { PlayClickSfx(); SlideToStageSelect(); });
        stageSelectBackButton.onClick.AddListener(() => { PlayClickSfx(); SlideToStartMenu(); });
        mainButton.onClick.AddListener(() => { PlayClickSfx(); TransitionToMap(mainMap); });
        stageButton.onClick.AddListener(() => { PlayClickSfx(); TransitionToMap(stageMap); });
        titleSettingsButton.onClick.AddListener(() => { PlayClickSfx(); settingsPanel.Open(); });
        mapSettingsButton.onClick.AddListener(() => { PlayClickSfx(); settingsPanel.Open(); });
        quitButton.onClick.AddListener(() => { PlayClickSfx(); GameFlow.QuitGame(); });
        backButton.onClick.AddListener(() => { PlayClickSfx(); TransitionToTitle(); });

        mainMap.LevelClicked += level => { PlayClickSfx(); SelectLevel(level); };
        stageMap.LevelClicked += level => { PlayClickSfx(); SelectLevel(level); };

        characterPopup.Selected += detailPanel.SetCharacter;
        characterPopup.Selected += catDecoration.SetCharacter;
        detailPanel.SetCharacter(characterPopup.Current);
        catDecoration.SetCharacter(characterPopup.Current);
        detailPanel.CharacterButtonClicked += () => { PlayClickSfx(); characterPopup.Open(); };
        detailPanel.StartClicked += StartBattle;

        BattleSelection last = GameFlow.CurrentBattle;
        if (last != null && mainMap.Contains(last.Level)) ShowMap(mainMap, last.Level);
        else if (last != null && stageMap.Contains(last.Level)) ShowMap(stageMap, last.Level);
        else ShowTitle(stageSelect: false);
    }

    void Update() {
        if (!Input.GetKeyDown(KeyCode.Escape) || ScreenFader.Instance.IsTransitioning || isMenuSliding || isStartingBattle) return;

        // 由最上層往下關:角色彈窗 → 設定面板 → 選關畫面回標題 → 關卡類型選單回開始選單
        if (characterPopup.IsOpen) characterPopup.Close();
        else if (settingsPanel.IsOpen) settingsPanel.Close();
        else if (mapScreen.activeSelf) TransitionToTitle();
        else if (stageSelectGroup.gameObject.activeSelf) SlideToStartMenu();
    }

    // 從選關畫面返回時,停在剛才選地圖的關卡類型選單
    void TransitionToTitle() => ScreenFader.Instance.TryTransition(() => ShowTitle(stageSelect: true));

    void TransitionToMap(LevelMapUI map) => ScreenFader.Instance.TryTransition(() => ShowMap(map, null));

    void ShowTitle(bool stageSelect) {
        characterPopup.Close();
        mapScreen.SetActive(false);
        titlePanel.SetActive(true);
        SetMenuGroupVisible(startMenuGroup, !stageSelect);
        SetMenuGroupVisible(stageSelectGroup, stageSelect);
        AudioManager.Instance.PlayBGM(titleBgm);
    }

    // ===== 標題畫面內兩組按鈕的滑動切換 =====

    // 開始選單往左滑出,關卡類型選單從右邊滑入
    void SlideToStageSelect() {
        if (!isMenuSliding) StartCoroutine(SlideMenuRoutine(startMenuGroup, stageSelectGroup, outDirection: -1f));
    }

    // 關卡類型選單往右滑出,開始選單從左邊滑入
    void SlideToStartMenu() {
        if (!isMenuSliding) StartCoroutine(SlideMenuRoutine(stageSelectGroup, startMenuGroup, outDirection: 1f));
    }

    // outDirection:舊的一組往哪邊滑出(-1 左 / +1 右);新的一組從另一邊滑進來。滑動期間兩組都不接受點擊
    IEnumerator SlideMenuRoutine(RectTransform outGroup, RectTransform inGroup, float outDirection) {
        isMenuSliding = true;
        float offscreenX = ((RectTransform)titlePanel.transform).rect.width;

        SetMenuGroupInteractable(outGroup, false);
        yield return SlideRoutine(outGroup, 0f, outDirection * offscreenX, menuSlideOutDuration, easeIn: true);
        outGroup.gameObject.SetActive(false);

        inGroup.gameObject.SetActive(true);
        SetMenuGroupInteractable(inGroup, false);
        yield return SlideRoutine(inGroup, -outDirection * offscreenX, 0f, menuSlideInDuration, easeIn: false);
        SetMenuGroupInteractable(inGroup, true);

        isMenuSliding = false;
    }

    // easeIn:開頭慢、結尾快(滑出用);否則開頭快、結尾慢(滑入用)
    static IEnumerator SlideRoutine(RectTransform group, float fromX, float toX, float duration, bool easeIn) {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime) {
            float p = t / duration;
            float eased = easeIn ? p * p : 1f - (1f - p) * (1f - p);
            group.anchoredPosition = new Vector2(Mathf.Lerp(fromX, toX, eased), 0f);
            yield return null;
        }
        group.anchoredPosition = new Vector2(toX, 0f);
    }

    static void SetMenuGroupVisible(RectTransform group, bool visible) {
        group.gameObject.SetActive(visible);
        group.anchoredPosition = Vector2.zero;
        SetMenuGroupInteractable(group, true);
    }

    static void SetMenuGroupInteractable(RectTransform group, bool interactable) =>
        group.GetComponent<CanvasGroup>().interactable = interactable;

    // level 為 null 時選這張地圖的預設關卡(最新解鎖、還沒通關的那關)
    void ShowMap(LevelMapUI map, LevelData level) {
        titlePanel.SetActive(false);
        mapScreen.SetActive(true);

        activeMap = map;
        mainMap.gameObject.SetActive(map == mainMap);
        stageMap.gameObject.SetActive(map == stageMap);
        map.Refresh();
        SelectLevel(level != null ? level : map.DefaultSelection());

        AudioManager.Instance.PlayBGM(mapBgm);
    }

    void SelectLevel(LevelData level) {
        activeMap.Select(level);
        detailPanel.Show(level);
    }

    void StartBattle() {
        if (!isStartingBattle) StartCoroutine(StartBattleRoutine());
    }

    // 播 battle_start,等它播完才出發(黑幕轉場 → 劇情/戰鬥)。等待期間關掉 EventSystem 擋住所有點擊,
    // 不用復原:接下來一定會換場景
    IEnumerator StartBattleRoutine() {
        isStartingBattle = true;
        EventSystem.current.enabled = false;

        BattleSelection selection = new BattleSelection(characterPopup.Current.gameObject, detailPanel.Level, detailPanel.Difficulty);
        bool skipStory = !detailPanel.PlayStory;

        AudioManager.Instance.PlaySFX(battleStartSfx);
        yield return new WaitForSecondsRealtime(AudioManager.Instance.GetSFXLength(battleStartSfx));

        GameFlow.StartBattle(selection, skipStory);
    }

    void PlayClickSfx() => AudioManager.Instance.PlaySFX(buttonClickSfx);
}
