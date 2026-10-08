using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 一鍵生成臨時測試用大廳場景(Assets/Scenes/Lobby.unity):建好 Canvas/按鈕/滑桿/勾選框/設定面板,
// 並用 SerializedObject 把 LobbyUI/SettingsPanelUI 的欄位全部接好,最後把 Lobby 排到 Build Settings 第一個。
// 角色清單 = Assets/Prefabs/Player 底下有 PlayerBase 的 prefab;關卡清單 = 專案內所有 LevelData(依 levelId 排序)。
// 新增角色/關卡後重跑一次即可(會覆蓋整個 Lobby 場景,手動改過的版面會被蓋掉)
public static class LobbySceneBuilder {
    const string ScenePath = "Assets/Scenes/Lobby.unity";
    const string PlayerPrefabFolder = "Assets/Prefabs/Player";
    const float EnemyInfoPanelLeft = 0.6f; // 敵人資訊面板從畫面寬度 60% 處開始,左邊是大廳操作

    static DefaultControls.Resources uiResources;
    static Font font;

    [MenuItem("Tools/Lobby/Build Test Lobby Scene")]
    static void Build() {
        if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("重建大廳場景", $"{ScenePath} 已存在,要整個覆蓋嗎?", "覆蓋", "取消")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        uiResources = new DefaultControls.Resources {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
            mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
        };
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateCamera();
        CreateEventSystem();
        Transform canvas = CreateCanvas();

        LobbyUI lobby = BuildLobbyPanel(canvas, out LobbyWidgets widgets);
        widgets.enemyInfoPanel = BuildEnemyInfoPanel(canvas);
        SettingsPanelUI settings = BuildSettingsPanel(canvas); // 最後建,蓋在大廳/敵人資訊上面

        foreach (Text text in canvas.GetComponentsInChildren<Text>(true)) text.font = font;

        WireLobby(lobby, widgets, settings);

        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        PutLobbyFirstInBuildSettings();

        Debug.Log($"已生成 {ScenePath} 並設為 Build Settings 第一個場景");
    }

    class LobbyWidgets {
        public List<(Button button, GameObject prefab)> characters = new List<(Button, GameObject)>();
        public List<(Button button, LevelData level)> levels = new List<(Button, LevelData)>();
        public Slider difficultySlider;
        public Text difficultyLabel;
        public Toggle skipStoryToggle;
        public Button settingsButton;
        public Button quitButton;
        public LobbyEnemyInfoPanel enemyInfoPanel;
    }

    static void CreateCamera() {
        GameObject camObj = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camObj.tag = "MainCamera";
        camObj.transform.position = new Vector3(0f, 0f, -10f);

        Camera cam = camObj.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.12f, 0.16f);
    }

    static void CreateEventSystem() {
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    static Transform CreateCanvas() {
        GameObject canvasObj = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObj.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvasObj.transform;
    }

    static LobbyUI BuildLobbyPanel(Transform canvas, out LobbyWidgets widgets) {
        widgets = new LobbyWidgets();

        // 左邊 60% 放大廳操作,右邊留給敵人資訊面板(見 BuildEnemyInfoPanel)
        GameObject root = CreateStretched("Lobby", canvas);
        ((RectTransform)root.transform).anchorMax = new Vector2(EnemyInfoPanelLeft, 1f);
        LobbyUI lobby = root.AddComponent<LobbyUI>();
        AddVerticalLayout(root, 40, new RectOffset(40, 40, 60, 60));

        CreateText("Title", root.transform, "測試大廳", 72, 900, 100);

        // 角色
        Transform characterRow = CreateRow("CharacterRow", root.transform);
        CreateText("Label", characterRow, "角色", 40, 120, 80);
        foreach (GameObject prefab in FindPlayerPrefabs()) {
            widgets.characters.Add((CreateButton(prefab.name, characterRow, prefab.name, 320, 80), prefab));
        }

        // 難度
        Transform difficultyRow = CreateRow("DifficultyRow", root.transform);
        widgets.difficultyLabel = CreateText("DifficultyLabel", difficultyRow, "難度", 40, 260, 80);
        GameObject sliderObj = DefaultControls.CreateSlider(uiResources);
        sliderObj.name = "DifficultySlider";
        sliderObj.transform.SetParent(difficultyRow, false);
        SetPreferredSize(sliderObj, 500, 40);
        widgets.difficultySlider = sliderObj.GetComponent<Slider>();

        // 跳過劇情
        widgets.skipStoryToggle = CreateToggle("SkipStoryToggle", root.transform, "跳過劇情", 400);

        // 關卡(按下直接出發)
        Transform levelRow = CreateRow("LevelRow", root.transform);
        CreateText("Label", levelRow, "關卡", 40, 120, 80);
        foreach (LevelData level in FindLevels()) {
            string label = GameFlow.HasPreBattleStory(level) ? $"{level.levelId} {level.levelName}\n(有劇情)" : $"{level.levelId} {level.levelName}";
            Button levelButton = CreateButton(level.levelId, levelRow, label, 280, 110);
            levelButton.GetComponentInChildren<Text>().fontSize = 26; // 關卡名稱較長,縮小字級塞進左側 60% 的寬度
            widgets.levels.Add((levelButton, level));
        }

        // 設定 / 離開
        Transform bottomRow = CreateRow("BottomRow", root.transform);
        widgets.settingsButton = CreateButton("SettingsButton", bottomRow, "設定", 260, 80);
        widgets.quitButton = CreateButton("QuitButton", bottomRow, "離開遊戲", 260, 80);

        return lobby;
    }

    // 右側可捲動的敵人資訊:ScrollView 的 Content 掛 VerticalLayoutGroup + ContentSizeFitter,讓高度跟著文字長度自動撐開
    static LobbyEnemyInfoPanel BuildEnemyInfoPanel(Transform canvas) {
        GameObject scrollObj = DefaultControls.CreateScrollView(uiResources);
        scrollObj.name = "EnemyInfoPanel";
        scrollObj.transform.SetParent(canvas, false);

        RectTransform scrollRectTransform = (RectTransform)scrollObj.transform;
        scrollRectTransform.anchorMin = new Vector2(EnemyInfoPanelLeft, 0f);
        scrollRectTransform.anchorMax = Vector2.one;
        scrollRectTransform.offsetMin = new Vector2(0f, 40f);
        scrollRectTransform.offsetMax = new Vector2(-40f, -40f);
        scrollObj.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

        ScrollRect scrollRect = scrollObj.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.scrollSensitivity = 30f;
        Object.DestroyImmediate(scrollRect.horizontalScrollbar.gameObject);
        scrollRect.horizontalScrollbar = null;

        RectTransform content = scrollRect.content;
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject textObj = new GameObject("InfoText", typeof(RectTransform), typeof(Text));
        textObj.transform.SetParent(content, false);
        Text text = textObj.GetComponent<Text>();
        text.fontSize = 24;
        text.color = Color.white;
        text.alignment = TextAnchor.UpperLeft;
        text.supportRichText = true;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        LobbyEnemyInfoPanel panel = scrollObj.AddComponent<LobbyEnemyInfoPanel>();
        SerializedObject so = new SerializedObject(panel);
        so.FindProperty("infoText").objectReferenceValue = text;
        so.ApplyModifiedPropertiesWithoutUndo();
        return panel;
    }

    static SettingsPanelUI BuildSettingsPanel(Transform canvas) {
        // 掛 SettingsPanelUI 的物件本身一直保持啟用(沒有 Graphic,不會擋點擊),真正開關的是底下的 Panel
        GameObject host = CreateStretched("SettingsPanel", canvas);
        SettingsPanelUI settings = host.AddComponent<SettingsPanelUI>();

        GameObject panel = CreateStretched("Panel", host.transform);
        panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f); // 半透明黑幕,同時擋住後面大廳按鈕的點擊

        GameObject box = new GameObject("Box", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(panel.transform, false);
        ((RectTransform)box.transform).sizeDelta = new Vector2(800f, 760f);
        box.GetComponent<Image>().color = new Color(0.22f, 0.22f, 0.28f);
        AddVerticalLayout(box, 30, new RectOffset(40, 40, 40, 40));

        CreateText("Title", box.transform, "設定", 56, 600, 80);

        Transform resolutionRow = CreateRow("ResolutionRow", box.transform);
        CreateText("Label", resolutionRow, "解析度", 36, 200, 60);
        GameObject dropdownObj = DefaultControls.CreateDropdown(uiResources);
        dropdownObj.name = "ResolutionDropdown";
        dropdownObj.transform.SetParent(resolutionRow, false);
        SetPreferredSize(dropdownObj, 360, 60);
        foreach (Text text in dropdownObj.GetComponentsInChildren<Text>(true)) text.fontSize = 28;

        Toggle fullscreenToggle = CreateToggle("FullscreenToggle", box.transform, "全螢幕", 400);

        Slider masterVolumeSlider = CreateLabeledSlider("MasterVolume", box.transform, "總音量");
        Slider bgmVolumeSlider = CreateLabeledSlider("BgmVolume", box.transform, "音樂");
        Slider sfxVolumeSlider = CreateLabeledSlider("SfxVolume", box.transform, "音效");

        Transform buttonRow = CreateRow("ButtonRow", box.transform);
        Button applyButton = CreateButton("ApplyButton", buttonRow, "套用", 220, 70);
        Button closeButton = CreateButton("CloseButton", buttonRow, "關閉", 220, 70);

        SerializedObject so = new SerializedObject(settings);
        so.FindProperty("panelRoot").objectReferenceValue = panel;
        so.FindProperty("resolutionDropdown").objectReferenceValue = dropdownObj.GetComponent<Dropdown>();
        so.FindProperty("fullscreenToggle").objectReferenceValue = fullscreenToggle;
        so.FindProperty("applyButton").objectReferenceValue = applyButton;
        so.FindProperty("closeButton").objectReferenceValue = closeButton;
        so.FindProperty("masterVolumeSlider").objectReferenceValue = masterVolumeSlider;
        so.FindProperty("bgmVolumeSlider").objectReferenceValue = bgmVolumeSlider;
        so.FindProperty("sfxVolumeSlider").objectReferenceValue = sfxVolumeSlider;
        so.ApplyModifiedPropertiesWithoutUndo();

        panel.SetActive(false);
        return settings;
    }

    static void WireLobby(LobbyUI lobby, LobbyWidgets widgets, SettingsPanelUI settings) {
        SerializedObject so = new SerializedObject(lobby);

        SerializedProperty characters = so.FindProperty("characterOptions");
        characters.arraySize = widgets.characters.Count;
        for (int i = 0; i < widgets.characters.Count; i++) {
            SerializedProperty element = characters.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("button").objectReferenceValue = widgets.characters[i].button;
            element.FindPropertyRelative("playerPrefab").objectReferenceValue = widgets.characters[i].prefab;
        }

        SerializedProperty levels = so.FindProperty("levelOptions");
        levels.arraySize = widgets.levels.Count;
        for (int i = 0; i < widgets.levels.Count; i++) {
            SerializedProperty element = levels.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("button").objectReferenceValue = widgets.levels[i].button;
            element.FindPropertyRelative("level").objectReferenceValue = widgets.levels[i].level;
        }

        so.FindProperty("difficultySlider").objectReferenceValue = widgets.difficultySlider;
        so.FindProperty("difficultyLabel").objectReferenceValue = widgets.difficultyLabel;
        so.FindProperty("skipStoryToggle").objectReferenceValue = widgets.skipStoryToggle;
        so.FindProperty("settingsButton").objectReferenceValue = widgets.settingsButton;
        so.FindProperty("settingsPanel").objectReferenceValue = settings;
        so.FindProperty("quitButton").objectReferenceValue = widgets.quitButton;
        so.FindProperty("enemyInfoPanel").objectReferenceValue = widgets.enemyInfoPanel;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static IEnumerable<GameObject> FindPlayerPrefabs() {
        return AssetDatabase.FindAssets("t:Prefab", new[] { PlayerPrefabFolder })
            .Select(guid => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(prefab => prefab != null && prefab.GetComponent<PlayerBase>() != null)
            .OrderBy(prefab => prefab.name);
    }

    static IEnumerable<LevelData> FindLevels() {
        return AssetDatabase.FindAssets("t:LevelData")
            .Select(guid => AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(level => level != null)
            .OrderBy(level => level.levelId);
    }

    static void PutLobbyFirstInBuildSettings() {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        scenes.RemoveAll(s => s.path == ScenePath);
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // ===== 通用 UI 建構小工具 =====

    static GameObject CreateStretched(string name, Transform parent) {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return obj;
    }

    static void AddVerticalLayout(GameObject obj, float spacing, RectOffset padding) {
        VerticalLayoutGroup layout = obj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = padding;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    static Transform CreateRow(string name, Transform parent) {
        GameObject row = new GameObject(name, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 24f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row.transform;
    }

    static void SetPreferredSize(GameObject obj, float width, float height) {
        LayoutElement element = obj.GetComponent<LayoutElement>();
        if (element == null) element = obj.AddComponent<LayoutElement>();
        element.preferredWidth = width;
        element.preferredHeight = height;
    }

    static Text CreateText(string name, Transform parent, string content, int fontSize, float width, float height) {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text));
        obj.transform.SetParent(parent, false);
        Text text = obj.GetComponent<Text>();
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        SetPreferredSize(obj, width, height);
        return text;
    }

    static Slider CreateLabeledSlider(string name, Transform parent, string label) {
        Transform row = CreateRow(name + "Row", parent);
        CreateText("Label", row, label, 36, 200, 60);

        GameObject sliderObj = DefaultControls.CreateSlider(uiResources);
        sliderObj.name = name + "Slider";
        sliderObj.transform.SetParent(row, false);
        SetPreferredSize(sliderObj, 360, 40);

        Slider slider = sliderObj.GetComponent<Slider>();
        slider.value = 1f;
        return slider;
    }

    static Button CreateButton(string name, Transform parent, string label, float width, float height) {
        GameObject obj = DefaultControls.CreateButton(uiResources);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        SetPreferredSize(obj, width, height);

        Text text = obj.GetComponentInChildren<Text>();
        text.text = label;
        text.fontSize = 32;
        text.color = new Color(0.15f, 0.15f, 0.15f);
        return obj.GetComponent<Button>();
    }

    // DefaultControls 的 Toggle 是 20px 小方框,這裡把方框/勾勾/文字一起放大到跟其他元件差不多的尺寸
    static Toggle CreateToggle(string name, Transform parent, string label, float width) {
        const float boxSize = 44f;

        GameObject obj = DefaultControls.CreateToggle(uiResources);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        SetPreferredSize(obj, width, boxSize);

        RectTransform background = (RectTransform)obj.transform.Find("Background");
        background.sizeDelta = new Vector2(boxSize, boxSize);
        background.anchoredPosition = new Vector2(boxSize * 0.5f, -boxSize * 0.5f);

        RectTransform checkmark = (RectTransform)background.Find("Checkmark");
        checkmark.sizeDelta = new Vector2(boxSize, boxSize);

        Text text = obj.transform.Find("Label").GetComponent<Text>();
        text.text = label;
        text.fontSize = 36;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        RectTransform labelRect = text.rectTransform;
        labelRect.offsetMin = new Vector2(boxSize + 16f, labelRect.offsetMin.y);

        Toggle toggle = obj.GetComponent<Toggle>();
        toggle.isOn = false;
        return toggle;
    }
}
