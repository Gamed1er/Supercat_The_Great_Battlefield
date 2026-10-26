using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 大廳場景生成器(正式大廳 LobbySceneBuilder / 測試大廳 DebugLobbySceneBuilder)共用的 UI 建構小工具與資料搜尋。
// 使用前先呼叫 Init() 載入內建 UI 素材與字型
public static class LobbyBuildUtil {
    public const string PlayerPrefabFolder = "Assets/Prefabs/Player";

    public static DefaultControls.Resources UiResources { get; private set; }
    public static Font Font { get; private set; }

    public static void Init() {
        UiResources = new DefaultControls.Resources {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
            mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
        };
        Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    // ===== 資料搜尋 =====

    // Assets/Prefabs/Player 底下有 PlayerBase 的 prefab,依名稱排序
    public static IEnumerable<GameObject> FindPlayerPrefabs() {
        return AssetDatabase.FindAssets("t:Prefab", new[] { PlayerPrefabFolder })
            .Select(guid => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(prefab => prefab != null && prefab.GetComponent<PlayerBase>() != null)
            .OrderBy(prefab => prefab.name);
    }

    // 專案內所有 LevelData,依 levelId 排序
    public static IEnumerable<LevelData> FindLevels() {
        return AssetDatabase.FindAssets("t:LevelData")
            .Select(guid => AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(level => level != null)
            .OrderBy(level => level.levelId);
    }

    // ===== 場景骨架 =====

    public static void CreateCamera() {
        GameObject camObj = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camObj.tag = "MainCamera";
        camObj.transform.position = new Vector3(0f, 0f, -10f);

        Camera cam = camObj.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.12f, 0.16f);
    }

    public static void CreateEventSystem() {
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    public static Transform CreateCanvas() {
        GameObject canvasObj = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObj.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvasObj.transform;
    }

    // 所有 Text 統一換成內建字型(新建的 Text 預設字型在不同 Unity 版本不一定存在)
    public static void ApplyFont(Transform root) {
        foreach (Text text in root.GetComponentsInChildren<Text>(true)) text.font = Font;
    }

    // 共用設定面板(解析度/全螢幕/音量),最後建才會蓋在其他 UI 上面
    public static SettingsPanelUI BuildSettingsPanel(Transform canvas) {
        // 掛 SettingsPanelUI 的物件本身一直保持啟用(沒有 Graphic,不會擋點擊),真正開關的是底下的 Panel
        GameObject host = CreateStretched("SettingsPanel", canvas);
        SettingsPanelUI settings = host.AddComponent<SettingsPanelUI>();

        GameObject panel = CreateStretched("Panel", host.transform);
        panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f); // 半透明黑幕,同時擋住後面按鈕的點擊

        GameObject box = new GameObject("Box", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(panel.transform, false);
        ((RectTransform)box.transform).sizeDelta = new Vector2(800f, 760f);
        box.GetComponent<Image>().color = new Color(0.22f, 0.22f, 0.28f);
        AddVerticalLayout(box, 30, new RectOffset(40, 40, 40, 40));

        CreateText("Title", box.transform, "設定", 56, 600, 80);

        Transform resolutionRow = CreateRow("ResolutionRow", box.transform);
        CreateText("Label", resolutionRow, "解析度", 36, 200, 60);
        GameObject dropdownObj = DefaultControls.CreateDropdown(UiResources);
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

    // ===== 通用 UI 元件 =====

    public static GameObject CreateStretched(string name, Transform parent) {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return obj;
    }

    // 錨點設成父物件的某個比例區塊(anchorMin~anchorMax),offset 歸零
    public static RectTransform CreateAnchored(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax) {
        RectTransform rect = (RectTransform)CreateStretched(name, parent).transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        return rect;
    }

    public static Image AddImage(GameObject obj, Color color, Sprite sprite = null) {
        Image image = obj.AddComponent<Image>();
        image.color = color;
        image.sprite = sprite;
        return image;
    }

    public static void AddVerticalLayout(GameObject obj, float spacing, RectOffset padding, TextAnchor alignment = TextAnchor.MiddleCenter) {
        VerticalLayoutGroup layout = obj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = padding;
        layout.childAlignment = alignment;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    public static Transform CreateRow(string name, Transform parent, float spacing = 24f, TextAnchor alignment = TextAnchor.MiddleCenter) {
        GameObject row = new GameObject(name, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = alignment;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row.transform;
    }

    public static void SetPreferredSize(GameObject obj, float width, float height) {
        LayoutElement element = obj.GetComponent<LayoutElement>();
        if (element == null) element = obj.AddComponent<LayoutElement>();
        element.preferredWidth = width;
        element.preferredHeight = height;
    }

    public static Text CreateText(string name, Transform parent, string content, int fontSize, float width, float height) {
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

    public static Slider CreateLabeledSlider(string name, Transform parent, string label) {
        Transform row = CreateRow(name + "Row", parent);
        CreateText("Label", row, label, 36, 200, 60);

        GameObject sliderObj = DefaultControls.CreateSlider(UiResources);
        sliderObj.name = name + "Slider";
        sliderObj.transform.SetParent(row, false);
        SetPreferredSize(sliderObj, 360, 40);

        Slider slider = sliderObj.GetComponent<Slider>();
        slider.value = 1f;
        return slider;
    }

    public static Button CreateButton(string name, Transform parent, string label, float width, float height) {
        GameObject obj = DefaultControls.CreateButton(UiResources);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        SetPreferredSize(obj, width, height);
        ((RectTransform)obj.transform).sizeDelta = new Vector2(width, height); // 不在 LayoutGroup 底下時也是這個尺寸

        Text text = obj.GetComponentInChildren<Text>();
        text.text = label;
        text.fontSize = 32;
        text.color = new Color(0.15f, 0.15f, 0.15f);
        return obj.GetComponent<Button>();
    }

    // DefaultControls 的 Toggle 是 20px 小方框,這裡把方框/勾勾/文字一起放大到跟其他元件差不多的尺寸
    public static Toggle CreateToggle(string name, Transform parent, string label, float width) {
        const float boxSize = 44f;

        GameObject obj = DefaultControls.CreateToggle(UiResources);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        SetPreferredSize(obj, width, boxSize);
        ((RectTransform)obj.transform).sizeDelta = new Vector2(width, boxSize);

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

    // ===== SerializedObject 接線 =====

    public static void Wire(Object target, params (string property, Object value)[] fields) {
        SerializedObject so = new SerializedObject(target);
        foreach ((string property, Object value) in fields) {
            SerializedProperty prop = so.FindProperty(property);
            if (prop == null) throw new System.ArgumentException($"{target.GetType().Name} 沒有欄位 {property}");
            prop.objectReferenceValue = value;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
