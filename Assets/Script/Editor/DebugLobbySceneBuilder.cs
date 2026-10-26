using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static LobbyBuildUtil;

// 一鍵生成測試用大廳場景(Assets/Scenes/DebugLobby.unity,調數值用,正式大廳見 LobbySceneBuilder):建好 Canvas/按鈕/滑桿/勾選框/設定面板,
// 並用 SerializedObject 把 DebugLobbyUI/SettingsPanelUI 的欄位全部接好。這個場景不放進 Build Settings,只在 Editor 直接開啟。
// 角色清單 = Assets/Prefabs/Player 底下有 PlayerBase 的 prefab;關卡清單 = 專案內所有 LevelData(依 levelId 排序)。
// 新增角色/關卡後重跑一次即可(會覆蓋整個 DebugLobby 場景,手動改過的版面會被蓋掉)
public static class DebugLobbySceneBuilder {
    const string ScenePath = "Assets/Scenes/DebugLobby.unity";
    const float EnemyInfoPanelLeft = 0.6f; // 敵人資訊面板從畫面寬度 60% 處開始,左邊是大廳操作

    [MenuItem("Tools/Lobby/Build Debug Lobby Scene")]
    static void Build() {
        if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("重建大廳場景", $"{ScenePath} 已存在,要整個覆蓋嗎?", "覆蓋", "取消")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        LobbyBuildUtil.Init();

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateCamera();
        CreateEventSystem();
        Transform canvas = CreateCanvas();

        DebugLobbyUI lobby = BuildLobbyPanel(canvas, out LobbyWidgets widgets);
        widgets.enemyInfoPanel = BuildEnemyInfoPanel(canvas);
        SettingsPanelUI settings = BuildSettingsPanel(canvas); // 最後建,蓋在大廳/敵人資訊上面

        ApplyFont(canvas);

        WireLobby(lobby, widgets, settings);

        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        RemoveFromBuildSettings();

        Debug.Log($"已生成 {ScenePath}");
    }

    class LobbyWidgets {
        public List<(Button button, GameObject prefab)> characters = new List<(Button, GameObject)>();
        public List<(Button button, LevelData level)> levels = new List<(Button, LevelData)>();
        public Slider difficultySlider;
        public Text difficultyLabel;
        public Toggle skipStoryToggle;
        public Button settingsButton;
        public Button quitButton;
        public DebugLobbyEnemyInfoPanel enemyInfoPanel;
    }

    static DebugLobbyUI BuildLobbyPanel(Transform canvas, out LobbyWidgets widgets) {
        widgets = new LobbyWidgets();

        // 左邊 60% 放大廳操作,右邊留給敵人資訊面板(見 BuildEnemyInfoPanel)
        GameObject root = CreateStretched("Lobby", canvas);
        ((RectTransform)root.transform).anchorMax = new Vector2(EnemyInfoPanelLeft, 1f);
        DebugLobbyUI lobby = root.AddComponent<DebugLobbyUI>();
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
        GameObject sliderObj = DefaultControls.CreateSlider(UiResources);
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
    static DebugLobbyEnemyInfoPanel BuildEnemyInfoPanel(Transform canvas) {
        GameObject scrollObj = DefaultControls.CreateScrollView(UiResources);
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

        DebugLobbyEnemyInfoPanel panel = scrollObj.AddComponent<DebugLobbyEnemyInfoPanel>();
        SerializedObject so = new SerializedObject(panel);
        so.FindProperty("infoText").objectReferenceValue = text;
        so.ApplyModifiedPropertiesWithoutUndo();
        return panel;
    }

    static void WireLobby(DebugLobbyUI lobby, LobbyWidgets widgets, SettingsPanelUI settings) {
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

    // 測試大廳只在 Editor 直接開來用,不進正式 build(GameFlow 在 Editor 會改用路徑載入不在 Build Settings 的場景)
    static void RemoveFromBuildSettings() {
        EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(scene => scene.path != ScenePath).ToArray();
    }
}
