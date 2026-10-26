using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static LobbyBuildUtil;

// 一鍵生成正式大廳場景(Assets/Scenes/Lobby.unity):標題畫面、選關畫面(主線/Stage 地圖 + 詳細面板)、角色選擇彈窗、設定面板,
// 並用 SerializedObject 把 LobbyMenuUI 等元件的欄位全部接好,最後把 Lobby 排到 Build Settings 第一個。
// 地圖節點:每個 LevelData 依 mapCategory/chapter 分到對應地圖的章節頁,位置 = mapPosition(地圖區塊內 0~1 比例),
// 前置關卡(prerequisiteLevel)在同一頁時自動畫連線。角色清單 = Assets/Prefabs/Player 底下有 PlayerBase 的 prefab。
// 新增角色/關卡、或改了 LevelData 的地圖欄位後重跑一次即可;會覆蓋整個場景,版面要調就改這支腳本。
// 美術目前全是佔位(內建 UI 素材 + 純色),換圖時改這裡生成的 Image.sprite
public static class LobbySceneBuilder {
    const string ScenePath = "Assets/Scenes/Lobby.unity";
    const string DebugLobbyScenePath = "Assets/Scenes/DebugLobby.unity";
    const int StarCount = EnemyBase.MaxDifficulty + 1;

    static readonly Color BackgroundColor = new Color(0.12f, 0.13f, 0.18f);
    static readonly Color PanelColor = new Color(0.2f, 0.21f, 0.27f);
    static readonly Color MapAreaColor = new Color(0.16f, 0.17f, 0.22f);
    static readonly Color HighlightColor = new Color(1f, 0.82f, 0.2f);
    static readonly Color DimOverlayColor = new Color(0f, 0f, 0f, 0.7f);

    [MenuItem("Tools/Lobby/Build Lobby Scene")]
    static void Build() {
        if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("重建大廳場景", $"{ScenePath} 已存在,要整個覆蓋嗎?", "覆蓋", "取消")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Init();
        List<GameObject> characters = FindPlayerPrefabs().ToList();
        List<LevelData> levels = FindLevels().ToList();
        if (characters.Count == 0) throw new System.InvalidOperationException($"{PlayerPrefabFolder} 底下找不到任何有 PlayerBase 的角色 prefab");

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateCamera();
        CreateEventSystem();
        Transform canvas = CreateCanvas();

        LobbyMenuUI menu = canvas.gameObject.AddComponent<LobbyMenuUI>();
        List<LevelData> stageLevels = levels.Where(level => level.mapCategory == LevelMapCategory.Stage).ToList();

        BuildTitlePanel(canvas, menu, PreviewCharacter(characters), hasStageLevels: stageLevels.Count > 0);
        BuildMapScreen(canvas, menu, levels);
        BuildCharacterPopup(canvas, menu, characters);
        SettingsPanelUI settings = BuildSettingsPanel(canvas); // 最後建,蓋在所有畫面上面
        Wire(menu, ("settingsPanel", settings));

        ApplyFont(canvas);

        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        PutLobbyFirstInBuildSettings();

        Debug.Log($"已生成 {ScenePath} 並設為 Build Settings 第一個場景");
    }

    // ===== 標題畫面 =====

    // 只重建 Lobby.unity 裡的 TitlePanel,選關畫面/彈窗/設定面板等其他部分(含手動改過的地方)保持不動
    [MenuItem("Tools/Lobby/Rebuild Title Panel Only")]
    static void RebuildTitlePanelOnly() {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        LobbyMenuUI menu = Object.FindObjectOfType<LobbyMenuUI>(true);
        Transform canvas = menu.transform;

        Transform oldPanel = canvas.Find("TitlePanel");
        if (oldPanel != null) Object.DestroyImmediate(oldPanel.gameObject);

        Init();
        GameObject panel = BuildTitlePanel(canvas, menu, PreviewCharacter(FindPlayerPrefabs().ToList()),
            hasStageLevels: FindLevels().Any(level => level.mapCategory == LevelMapCategory.Stage));
        panel.transform.SetAsFirstSibling(); // 墊在選關畫面/彈窗/設定面板下面
        ApplyFont(panel.transform);

        EditorSceneManager.SaveScene(scene);
        Debug.Log($"已重建 {ScenePath} 的 TitlePanel");
    }

    // 標題、裝飾貓、左下角設定/離開,加上兩組左右滑動切換的按鈕(見 LobbyMenuUI.SlideMenuRoutine):
    // 開始選單(遊戲開始/角色升級/收藏)與關卡類型選單(特殊關卡/冒險故事/活動關卡/返回)
    static GameObject BuildTitlePanel(Transform canvas, LobbyMenuUI menu, PlayerBase decorationCharacter, bool hasStageLevels) {
        GameObject panel = CreateStretched("TitlePanel", canvas);
        AddImage(panel, BackgroundColor);

        Text title = CreateFloatingText("Title", panel.transform, "Super Cat Remake", 120,
            new Vector2(0.08f, 0.68f), new Vector2(0.8f, 0.95f));
        title.alignment = TextAnchor.MiddleLeft;

        // 裝飾用的貓:執行時播目前選定角色的待機動畫,可以用滑鼠搓(見 LobbyCatDecorationUI);這裡的頭像只是編輯時的預覽
        GameObject decoration = new GameObject("CatDecoration", typeof(RectTransform));
        decoration.transform.SetParent(panel.transform, false);
        Place(decoration, new Vector2(0.84f, 0.8f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 240f));
        Image decorationImage = AddImage(decoration, Color.white, decorationCharacter.Portrait);
        decorationImage.preserveAspect = true; // raycastTarget 保持開著,才收得到拖曳
        LobbyCatDecorationUI catDecoration = decoration.AddComponent<LobbyCatDecorationUI>();
        Wire(catDecoration, ("image", decorationImage));

        // 開始選單:置中直排三顆
        RectTransform startMenu = CreateMenuGroup("StartMenu", panel.transform);
        GameObject column = new GameObject("Column", typeof(RectTransform));
        column.transform.SetParent(startMenu, false);
        Place(column, new Vector2(0.5f, 0.33f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 500f));
        AddVerticalLayout(column, 24, new RectOffset(0, 0, 0, 0));
        Button gameStartButton = CreateMenuButton("GameStartButton", column.transform, "遊戲開始");
        CreateMenuButton("CharacterUpgradeButton", column.transform, "角色升級").interactable = false; // 佔位,升級系統還沒做
        CreateMenuButton("CollectionButton", column.transform, "收藏").interactable = false; // 佔位,收藏系統還沒做

        // 關卡類型選單:橫排三顆 + 右下角返回。還沒有任何 Stage 關卡時「活動關卡」顯示但不能按
        RectTransform stageSelect = CreateMenuGroup("StageSelect", panel.transform);
        Button specialStageButton = CreateStageTypeButton("SpecialStageButton", stageSelect, "特殊關卡\n<size=28>Coming soon</size>", 0.25f);
        specialStageButton.interactable = false; // 佔位,還沒有特殊關卡
        Button mainButton = CreateStageTypeButton("MainButton", stageSelect, "冒險故事", 0.5f);
        string stageLabel = hasStageLevels ? "活動關卡" : "活動關卡\n<size=28>Coming soon</size>";
        Button stageButton = CreateStageTypeButton("StageButton", stageSelect, stageLabel, 0.75f);
        stageButton.interactable = hasStageLevels;
        Button backButton = CreateButton("BackButton", stageSelect, "返回", 200, 80);
        Place(backButton.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(200f, 80f));
        stageSelect.gameObject.SetActive(false);

        // 左下角:設定 / 離開(不跟著選單滑動)
        Button settingsButton = CreateButton("SettingsButton", panel.transform, "設定", 200, 80);
        Place(settingsButton.gameObject, Vector2.zero, Vector2.zero, new Vector2(40f, 40f), new Vector2(200f, 80f));
        Button quitButton = CreateButton("QuitButton", panel.transform, "離開", 200, 80);
        Place(quitButton.gameObject, Vector2.zero, Vector2.zero, new Vector2(260f, 40f), new Vector2(200f, 80f));

        Wire(menu,
            ("titlePanel", panel),
            ("catDecoration", catDecoration),
            ("startMenuGroup", startMenu),
            ("stageSelectGroup", stageSelect),
            ("gameStartButton", gameStartButton),
            ("stageSelectBackButton", backButton),
            ("mainButton", mainButton),
            ("stageButton", stageButton),
            ("titleSettingsButton", settingsButton),
            ("quitButton", quitButton));
        return panel;
    }

    // 裝飾貓在編輯器裡顯示的預覽角色:跟執行時沒有存檔的預設一樣用 SuperCat,找不到就用第一個
    static PlayerBase PreviewCharacter(List<GameObject> characters) =>
        (characters.FirstOrDefault(prefab => prefab.name == "SuperCat") ?? characters[0]).GetComponent<PlayerBase>();

    // 撐滿標題畫面的一組按鈕,滑動時整組平移;CanvasGroup 讓滑動期間整組不接受點擊
    static RectTransform CreateMenuGroup(string name, Transform panel) {
        GameObject group = CreateStretched(name, panel);
        group.AddComponent<CanvasGroup>();
        return (RectTransform)group.transform;
    }

    // 開始選單的大按鈕(佔位配色:黃底白字黑邊,對照 Docs/lobby_ref_2.jpg)
    static Button CreateMenuButton(string name, Transform parent, string label) {
        Button button = CreateButton(name, parent, label, 760, 150);
        button.image.color = new Color(1f, 0.74f, 0.02f);
        Text text = button.GetComponentInChildren<Text>();
        text.fontSize = 64;
        text.color = Color.white;
        Outline outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(3f, -3f);
        return button;
    }

    static Button CreateStageTypeButton(string name, Transform parent, string label, float anchorX) {
        Button button = CreateButton(name, parent, label, 380, 240);
        Place(button.gameObject, new Vector2(anchorX, 0.33f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(380f, 240f));
        button.GetComponentInChildren<Text>().fontSize = 50;
        return button;
    }

    // ===== 選關畫面 =====

    static void BuildMapScreen(Transform canvas, LobbyMenuUI menu, List<LevelData> levels) {
        GameObject screen = CreateStretched("MapScreen", canvas);
        AddImage(screen, BackgroundColor);

        // 上方列:返回 / XP(佔位) / 設定
        RectTransform topBar = CreateAnchored("TopBar", screen.transform, new Vector2(0f, 1f), new Vector2(1f, 1f));
        topBar.pivot = new Vector2(0.5f, 1f);
        topBar.sizeDelta = new Vector2(0f, 120f);
        AddImage(topBar.gameObject, PanelColor);

        Button backButton = CreateButton("BackButton", topBar, "返回", 180, 80);
        Place(backButton.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(180f, 80f));

        Text xpText = CreateFloatingText("XpText", topBar, "XP 0", 52, new Vector2(0f, 0f), new Vector2(0.5f, 1f));
        xpText.rectTransform.offsetMin = new Vector2(250f, 0f);
        xpText.alignment = TextAnchor.MiddleLeft;

        Button settingsButton = CreateButton("SettingsButton", topBar, "設定", 180, 80);
        Place(settingsButton.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(180f, 80f));

        // 地圖區塊(主線 / Stage 兩張地圖疊在同一塊,同時只開一張)
        RectTransform mapArea = CreateAnchored("MapArea", screen.transform, new Vector2(0.02f, 0.16f), new Vector2(0.62f, 0.86f));
        AddImage(mapArea.gameObject, MapAreaColor);
        LevelMapUI mainMap = BuildLevelMap("MainMap", mapArea, levels.Where(level => level.mapCategory == LevelMapCategory.Main));
        LevelMapUI stageMap = BuildLevelMap("StageMap", mapArea, levels.Where(level => level.mapCategory == LevelMapCategory.Stage));

        // 升級(佔位,升級系統還沒做)
        Button upgradeButton = CreateButton("UpgradeButton", screen.transform, "Upgrade", 280, 100);
        Place(upgradeButton.gameObject, new Vector2(0.02f, 0.03f), Vector2.zero, Vector2.zero, new Vector2(280f, 100f));
        upgradeButton.interactable = false;

        LevelDetailPanelUI detailPanel = BuildDetailPanel(screen.transform);

        Wire(menu,
            ("mapScreen", screen),
            ("mainMap", mainMap),
            ("stageMap", stageMap),
            ("detailPanel", detailPanel),
            ("backButton", backButton),
            ("mapSettingsButton", settingsButton));
    }

    static LevelMapUI BuildLevelMap(string name, Transform mapArea, IEnumerable<LevelData> levels) {
        GameObject mapObj = CreateStretched(name, mapArea);
        LevelMapUI map = mapObj.AddComponent<LevelMapUI>();

        // 章節頁:地圖區塊下方 12% 留給章節切換列
        RectTransform pagesRoot = CreateAnchored("Pages", mapObj.transform, new Vector2(0f, 0.12f), Vector2.one);

        var pages = new List<(int chapter, GameObject root, List<LevelNodeUI> nodes, List<MapLinkUI> links)>();
        foreach (IGrouping<int, LevelData> chapter in levels.GroupBy(level => level.chapter).OrderBy(group => group.Key)) {
            GameObject pageRoot = CreateStretched($"Chapter{chapter.Key}", pagesRoot);

            var nodeByLevel = new Dictionary<LevelData, LevelNodeUI>();
            foreach (LevelData level in chapter) nodeByLevel[level] = BuildLevelNode(pageRoot.transform, level);

            var links = new List<MapLinkUI>();
            foreach (LevelData level in chapter) {
                if (level.prerequisiteLevel == null || !nodeByLevel.TryGetValue(level.prerequisiteLevel, out LevelNodeUI fromNode)) continue;
                MapLinkUI link = BuildLink(pageRoot.transform, fromNode, nodeByLevel[level]);
                link.transform.SetAsFirstSibling(); // 連線畫在節點下面
                links.Add(link);
            }

            pages.Add((chapter.Key, pageRoot, chapter.Select(level => nodeByLevel[level]).ToList(), links));
        }

        // 章節切換列
        RectTransform navigation = CreateAnchored("ChapterNavigation", mapObj.transform, Vector2.zero, new Vector2(1f, 0.12f));
        HorizontalLayoutGroup navigationLayout = navigation.gameObject.AddComponent<HorizontalLayoutGroup>();
        navigationLayout.spacing = 30f;
        navigationLayout.childAlignment = TextAnchor.MiddleCenter;
        navigationLayout.childControlWidth = true;
        navigationLayout.childControlHeight = true;
        navigationLayout.childForceExpandWidth = false;
        navigationLayout.childForceExpandHeight = false;
        Button previousButton = CreateButton("PreviousChapterButton", navigation, "◀", 80, 70);
        Text chapterLabel = CreateText("ChapterLabel", navigation, "第 1 章", 36, 220, 70);
        Button nextButton = CreateButton("NextChapterButton", navigation, "▶", 80, 70);

        SerializedObject so = new SerializedObject(map);
        SerializedProperty pagesProp = so.FindProperty("pages");
        pagesProp.arraySize = pages.Count;
        for (int i = 0; i < pages.Count; i++) {
            SerializedProperty page = pagesProp.GetArrayElementAtIndex(i);
            page.FindPropertyRelative("chapter").intValue = pages[i].chapter;
            page.FindPropertyRelative("root").objectReferenceValue = pages[i].root;
            SetObjectArray(page.FindPropertyRelative("nodes"), pages[i].nodes);
            SetObjectArray(page.FindPropertyRelative("links"), pages[i].links);
        }
        so.FindProperty("chapterNavigation").objectReferenceValue = navigation.gameObject;
        so.FindProperty("previousChapterButton").objectReferenceValue = previousButton;
        so.FindProperty("nextChapterButton").objectReferenceValue = nextButton;
        so.FindProperty("chapterLabel").objectReferenceValue = chapterLabel;
        so.ApplyModifiedPropertiesWithoutUndo();

        return map;
    }

    static LevelNodeUI BuildLevelNode(Transform page, LevelData level) {
        const float circleSize = 150f;

        GameObject nodeObj = new GameObject($"Node_{level.levelId}", typeof(RectTransform));
        nodeObj.transform.SetParent(page, false);
        Place(nodeObj, level.mapPosition, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(circleSize, circleSize));
        LevelNodeUI node = nodeObj.AddComponent<LevelNodeUI>();

        GameObject frame = new GameObject("SelectedFrame", typeof(RectTransform));
        frame.transform.SetParent(nodeObj.transform, false);
        Place(frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(circleSize + 26f, circleSize + 26f));
        AddImage(frame, HighlightColor, UiResources.knob).raycastTarget = false;

        GameObject circle = CreateStretched("Circle", nodeObj.transform);
        Image circleImage = AddImage(circle, Color.white, UiResources.knob);
        Button button = circle.AddComponent<Button>();
        button.targetGraphic = circleImage;
        ColorBlock colors = button.colors;
        colors.disabledColor = Color.white; // 鎖住的外觀由 LevelNodeUI 自己換色,不要再被 Button 疊一層半透明
        button.colors = colors;

        Text label = CreateFloatingText("Label", circle.transform, level.levelId, 44, Vector2.zero, Vector2.one);

        StarRowUI stars = BuildStarRow(nodeObj.transform, 24f, 4f);
        Place(stars.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(StarCount * 28f, 24f));

        Wire(node,
            ("level", level),
            ("button", button),
            ("circleImage", circleImage),
            ("label", label),
            ("stars", stars),
            ("selectedFrame", frame));
        frame.SetActive(false);
        return node;
    }

    static MapLinkUI BuildLink(Transform page, LevelNodeUI fromNode, LevelNodeUI toNode) {
        GameObject linkObj = CreateStretched($"Link_{fromNode.Level.levelId}_{toNode.Level.levelId}", page);
        MapLinkUI link = linkObj.AddComponent<MapLinkUI>();

        var segments = new List<Image>();
        for (int i = 0; i < 3; i++) {
            GameObject segment = new GameObject($"Segment{i}", typeof(RectTransform));
            segment.transform.SetParent(linkObj.transform, false);
            Place(segment, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Image image = AddImage(segment, Color.white);
            image.raycastTarget = false;
            segments.Add(image);
        }

        SerializedObject so = new SerializedObject(link);
        so.FindProperty("fromNode").objectReferenceValue = fromNode;
        so.FindProperty("toNode").objectReferenceValue = toNode;
        SetObjectArray(so.FindProperty("segments"), segments);
        so.ApplyModifiedPropertiesWithoutUndo();
        return link;
    }

    static StarRowUI BuildStarRow(Transform parent, float starSize, float spacing) {
        Transform row = CreateRow("Stars", parent, spacing);
        StarRowUI starRow = row.gameObject.AddComponent<StarRowUI>();

        var stars = new List<Image>();
        for (int i = 0; i < StarCount; i++) {
            GameObject star = new GameObject($"Star{i}", typeof(RectTransform));
            star.transform.SetParent(row, false);
            SetPreferredSize(star, starSize, starSize);
            Image image = AddImage(star, Color.white, UiResources.knob);
            image.raycastTarget = false;
            stars.Add(image);
        }

        SerializedObject so = new SerializedObject(starRow);
        SetObjectArray(so.FindProperty("stars"), stars);
        so.FindProperty("litSprite").objectReferenceValue = UiResources.knob; // 佔位圖
        so.FindProperty("unlitSprite").objectReferenceValue = UiResources.knob;
        so.ApplyModifiedPropertiesWithoutUndo();
        return starRow;
    }

    // ===== 右側詳細面板 =====

    static LevelDetailPanelUI BuildDetailPanel(Transform screen) {
        RectTransform panel = CreateAnchored("DetailPanel", screen, new Vector2(0.65f, 0.05f), new Vector2(0.98f, 0.86f));
        AddImage(panel.gameObject, PanelColor);
        LevelDetailPanelUI detail = panel.gameObject.AddComponent<LevelDetailPanelUI>();
        AddVerticalLayout(panel.gameObject, 20, new RectOffset(36, 36, 36, 36), TextAnchor.UpperLeft);
        panel.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;

        // 標題 + 劇情勾選
        Transform headerRow = CreateRow("HeaderRow", panel, 16f, TextAnchor.MiddleLeft);
        Text titleText = CreateText("Title", headerRow, "1-1", 44, 300, 70);
        titleText.alignment = TextAnchor.MiddleLeft;
        titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
        titleText.resizeTextForBestFit = true;
        titleText.resizeTextMinSize = 24;
        titleText.resizeTextMaxSize = 44;
        titleText.GetComponent<LayoutElement>().flexibleWidth = 1f;
        Toggle storyToggle = CreateToggle("PlayStoryToggle", headerRow, "Story", 170);

        Text descriptionText = CreateText("Description", panel, "", 28, 500, 260);
        descriptionText.alignment = TextAnchor.UpperLeft;
        descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
        descriptionText.verticalOverflow = VerticalWrapMode.Truncate;
        descriptionText.GetComponent<LayoutElement>().flexibleHeight = 1f;

        // 星星 + 無傷標記
        Transform starsRow = CreateRow("StarsRow", panel, 20f, TextAnchor.MiddleLeft);
        BuildStarRow(starsRow, 40f, 8f);
        StarRowUI stars = starsRow.GetComponentInChildren<StarRowUI>();
        GameObject noDamageBadge = new GameObject("NoDamageBadge", typeof(RectTransform));
        noDamageBadge.transform.SetParent(starsRow, false);
        SetPreferredSize(noDamageBadge, 110, 44);
        AddImage(noDamageBadge, HighlightColor).raycastTarget = false;
        Text badgeText = CreateFloatingText("Label", noDamageBadge.transform, "無傷", 28, Vector2.zero, Vector2.one);
        badgeText.color = new Color(0.15f, 0.15f, 0.15f);

        Text bestTimeText = CreateText("BestTime", panel, "最佳時間  --:--.--", 30, 500, 50);
        bestTimeText.alignment = TextAnchor.MiddleLeft;

        // 難度 ◀ n ▶
        Transform difficultyRow = CreateRow("DifficultyRow", panel, 16f, TextAnchor.MiddleLeft);
        Text difficultyLabel = CreateText("Label", difficultyRow, "Difficulty", 36, 190, 70);
        difficultyLabel.alignment = TextAnchor.MiddleLeft;
        Button difficultyDownButton = CreateButton("DifficultyDownButton", difficultyRow, "◀", 70, 70);
        Text difficultyText = CreateText("Value", difficultyRow, "0", 48, 80, 70);
        Button difficultyUpButton = CreateButton("DifficultyUpButton", difficultyRow, "▶", 70, 70);

        // 出戰角色 + 出發
        Transform bottomRow = CreateRow("BottomRow", panel, 24f, TextAnchor.MiddleLeft);
        Button characterButton = CreateButton("CharacterButton", bottomRow, "", 140, 140);
        GameObject portrait = CreateStretched("Portrait", characterButton.transform);
        ((RectTransform)portrait.transform).offsetMin = new Vector2(10f, 10f);
        ((RectTransform)portrait.transform).offsetMax = new Vector2(-10f, -10f);
        Image portraitImage = AddImage(portrait, Color.white);
        portraitImage.preserveAspect = true;
        portraitImage.raycastTarget = false;
        Button startButton = CreateButton("StartButton", bottomRow, "Start", 300, 120);
        startButton.GetComponentInChildren<Text>().fontSize = 52;

        Wire(detail,
            ("titleText", titleText),
            ("descriptionText", descriptionText),
            ("playStoryToggle", storyToggle),
            ("stars", stars),
            ("noDamageBadge", noDamageBadge),
            ("bestTimeText", bestTimeText),
            ("difficultyDownButton", difficultyDownButton),
            ("difficultyUpButton", difficultyUpButton),
            ("difficultyText", difficultyText),
            ("characterButton", characterButton),
            ("characterPortraitImage", portraitImage),
            ("startButton", startButton));
        return detail;
    }

    // ===== 角色選擇彈窗 =====

    static void BuildCharacterPopup(Transform canvas, LobbyMenuUI menu, List<GameObject> characters) {
        // 掛 CharacterSelectPopupUI 的物件一直保持啟用(它的 Awake 要先還原選中的角色),真正開關的是底下的 Panel
        GameObject host = CreateStretched("CharacterPopup", canvas);
        CharacterSelectPopupUI popup = host.AddComponent<CharacterSelectPopupUI>();

        GameObject panel = CreateStretched("Panel", host.transform);
        AddImage(panel, DimOverlayColor); // 同時擋住後面選關畫面的點擊

        GameObject box = new GameObject("Box", typeof(RectTransform));
        box.transform.SetParent(panel.transform, false);
        Place(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 800f));
        AddImage(box, PanelColor);
        HorizontalLayoutGroup boxLayout = box.AddComponent<HorizontalLayoutGroup>();
        boxLayout.padding = new RectOffset(40, 40, 40, 40);
        boxLayout.spacing = 40f;
        boxLayout.childAlignment = TextAnchor.UpperLeft;
        boxLayout.childControlWidth = true;
        boxLayout.childControlHeight = true;
        boxLayout.childForceExpandWidth = false;
        boxLayout.childForceExpandHeight = false;

        // 左:角色清單
        GameObject list = new GameObject("CharacterList", typeof(RectTransform));
        list.transform.SetParent(box.transform, false);
        AddVerticalLayout(list, 24, new RectOffset(12, 12, 12, 12), TextAnchor.UpperCenter);
        SetPreferredSize(list, 260, 720);

        var entries = new List<(PlayerBase character, Button button, GameObject frame)>();
        foreach (GameObject prefab in characters) {
            PlayerBase character = prefab.GetComponent<PlayerBase>();

            GameObject entry = new GameObject(prefab.name, typeof(RectTransform));
            entry.transform.SetParent(list.transform, false);
            SetPreferredSize(entry, 200, 200);

            GameObject frame = CreateStretched("SelectedFrame", entry.transform);
            ((RectTransform)frame.transform).offsetMin = new Vector2(-8f, -8f);
            ((RectTransform)frame.transform).offsetMax = new Vector2(8f, 8f);
            AddImage(frame, HighlightColor).raycastTarget = false;

            Button button = CreateButton("Button", entry.transform, "", 200, 200);
            Stretch((RectTransform)button.transform);
            GameObject portrait = CreateStretched("Portrait", button.transform);
            ((RectTransform)portrait.transform).offsetMin = new Vector2(12f, 12f);
            ((RectTransform)portrait.transform).offsetMax = new Vector2(-12f, -12f);
            Image portraitImage = AddImage(portrait, Color.white, character.Portrait);
            portraitImage.preserveAspect = true;
            portraitImage.raycastTarget = false;

            frame.SetActive(false);
            entries.Add((character, button, frame));
        }

        // 右:角色介紹
        GameObject info = new GameObject("Info", typeof(RectTransform));
        info.transform.SetParent(box.transform, false);
        AddVerticalLayout(info, 20, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);
        SetPreferredSize(info, 1000, 720);

        Transform headerRow = CreateRow("HeaderRow", info.transform, 30f, TextAnchor.MiddleLeft);
        GameObject bigPortrait = new GameObject("Portrait", typeof(RectTransform));
        bigPortrait.transform.SetParent(headerRow, false);
        SetPreferredSize(bigPortrait, 200, 200);
        Image bigPortraitImage = AddImage(bigPortrait, Color.white);
        bigPortraitImage.preserveAspect = true;
        bigPortraitImage.raycastTarget = false;
        Text nameText = CreateText("Name", headerRow, "", 60, 700, 200);
        nameText.alignment = TextAnchor.MiddleLeft;

        (Image icon, Text text) normalAttack = BuildSkillRow(info.transform, "NormalAttack");
        (Image icon, Text text) s1 = BuildSkillRow(info.transform, "Skill1");
        (Image icon, Text text) s2 = BuildSkillRow(info.transform, "Skill2");

        Transform buttonRow = CreateRow("ButtonRow", info.transform, 0f, TextAnchor.MiddleRight);
        SetPreferredSize(buttonRow.gameObject, 1000, 80);
        Button closeButton = CreateButton("CloseButton", buttonRow, "關閉", 220, 80);

        SerializedObject so = new SerializedObject(popup);
        SerializedProperty entriesProp = so.FindProperty("entries");
        entriesProp.arraySize = entries.Count;
        for (int i = 0; i < entries.Count; i++) {
            SerializedProperty element = entriesProp.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("character").objectReferenceValue = entries[i].character;
            element.FindPropertyRelative("button").objectReferenceValue = entries[i].button;
            element.FindPropertyRelative("selectedFrame").objectReferenceValue = entries[i].frame;
        }
        so.FindProperty("panelRoot").objectReferenceValue = panel;
        so.FindProperty("portraitImage").objectReferenceValue = bigPortraitImage;
        so.FindProperty("nameText").objectReferenceValue = nameText;
        so.FindProperty("normalAttackIconImage").objectReferenceValue = normalAttack.icon;
        so.FindProperty("normalAttackText").objectReferenceValue = normalAttack.text;
        so.FindProperty("s1IconImage").objectReferenceValue = s1.icon;
        so.FindProperty("s1Text").objectReferenceValue = s1.text;
        so.FindProperty("s2IconImage").objectReferenceValue = s2.icon;
        so.FindProperty("s2Text").objectReferenceValue = s2.text;
        so.FindProperty("closeButton").objectReferenceValue = closeButton;
        so.ApplyModifiedPropertiesWithoutUndo();

        panel.SetActive(false);
        Wire(menu, ("characterPopup", popup));
    }

    static (Image icon, Text text) BuildSkillRow(Transform parent, string name) {
        Transform row = CreateRow(name + "Row", parent, 24f, TextAnchor.MiddleLeft);

        // 圖示格:底框一直顯示,圖示沒指定時只藏圖示本身(見 CharacterSelectPopupUI.SetSkill)
        GameObject iconFrame = new GameObject("IconFrame", typeof(RectTransform));
        iconFrame.transform.SetParent(row, false);
        SetPreferredSize(iconFrame, 100, 100);
        AddImage(iconFrame, MapAreaColor).raycastTarget = false;
        GameObject icon = CreateStretched("Icon", iconFrame.transform);
        Image iconImage = AddImage(icon, Color.white);
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        Text text = CreateText("Description", row, "", 28, 860, 110);
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        return (iconImage, text);
    }

    // ===== 小工具 =====

    // 不在 LayoutGroup 底下的元件:錨點/pivot 設在同一點,再用 offset/size 擺位置
    static void Place(GameObject obj, Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 size) {
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    static void Stretch(RectTransform rect) {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // 不在 LayoutGroup 底下、用錨點區塊決定大小的文字
    static Text CreateFloatingText(string name, Transform parent, string content, int fontSize, Vector2 anchorMin, Vector2 anchorMax) {
        Text text = CreateText(name, parent, content, fontSize, 0, 0);
        Object.DestroyImmediate(text.GetComponent<LayoutElement>());
        RectTransform rect = text.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return text;
    }

    static void SetObjectArray<T>(SerializedProperty array, IList<T> values) where T : Object {
        array.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    static void PutLobbyFirstInBuildSettings() {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes
            .Where(scene => scene.path != ScenePath && scene.path != DebugLobbyScenePath && File.Exists(scene.path))
            .ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
