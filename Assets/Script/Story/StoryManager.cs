using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Story.unity 的場景入口:依序播放 LevelData.preBattleStoryScript 解析出的 StoryStep,
// 驅動場景裡手動接好的 UI(背景/左右立繪/底部對話框/中間強調文字),播完或按 Skip 後直接進 Battle 場景。
// levelData 目前是 Inspector 手動接一筆固定值(比照 LevelManager.difficulty 的暫時做法),之後選關介面做好再換成動態傳入。
public class StoryManager : MonoBehaviour {
    const string SeenKeyPrefix = "Story_Seen_";

    [Header("關卡資料 (暫時手動指定,之後由選關畫面傳入)")]
    [SerializeField] LevelData levelData;

    [Header("背景")]
    [SerializeField] Image backgroundImage;

    [Header("底部對話框 (Down 底下的角色立繪/名稱/台詞)")]
    [SerializeField] GameObject downRoot;
    [SerializeField] Image leftCharacterImage;
    [SerializeField] Image rightCharacterImage;
    [SerializeField] Text characterNameText;
    [SerializeField] Text dialogueText;

    [Header("中間強調文字")]
    [SerializeField] GameObject middleRoot;
    [SerializeField] Text middleText;

    [Header("操作按鈕 (Inspector 手動接,本腳本不生成任何 UI 物件)")]
    [SerializeField] Button autoPlayButton; // 第一次進來就有,純開關,不跨場次記憶
    [SerializeField] Button skipButton;     // 只有這關已經看過一次才顯示,按下直接跳到底

    [Header("立繪版面 (寬度固定,依原圖比例縮放,底部對齊這個 Y)")]
    [SerializeField] float characterImageWidth = 300f;
    [SerializeField] float characterImageBottomY = 400f;

    [Header("節奏調整")]
    [SerializeField] float typewriterCharsPerSecond = 30f;
    [SerializeField] float enterExitDuration = 0.3f;
    [SerializeField] float slideOffset = 800f; // 立繪滑入/滑出的位移量(UI 座標,像素)
    [SerializeField] float middleFadeDuration = 1f;
    [SerializeField] float autoPlayDelay = 1f; // 自動播放時,一行打完字後等幾秒才自動推進

    [Header("顏色")]
    [SerializeField] Color activeSpeakerColor = Color.white;
    [SerializeField] Color dimmedSpeakerColor = new Color(0.35f, 0.35f, 0.35f, 1f);
    [SerializeField] Color middleBlackColor = Color.black;
    [SerializeField] Color autoPlayOnColor = new Color(0.4f, 1f, 0.4f);
    [SerializeField] Color autoPlayOffColor = Color.white;

    List<StoryStep> steps;
    Image autoPlayButtonImage;

    Color backgroundOriginalColor;
    float leftRestX;
    float rightRestX;
    string leftCharacterId;
    string rightCharacterId;

    bool isTyping;
    bool skipTypingRequested;
    bool waitingForAdvance;
    bool autoPlayEnabled;

    Coroutine autoAdvanceRoutine;

    void Awake() {
        string scriptText = levelData != null && levelData.preBattleStoryScript != null ? levelData.preBattleStoryScript.text : "";
        steps = StoryScriptParser.Parse(scriptText);
    }

    void Start() {
        backgroundOriginalColor = backgroundImage.color;
        leftRestX = leftCharacterImage.rectTransform.anchoredPosition.x;
        rightRestX = rightCharacterImage.rectTransform.anchoredPosition.x;

        leftCharacterImage.gameObject.SetActive(false);
        rightCharacterImage.gameObject.SetActive(false);
        middleRoot.SetActive(false);
        characterNameText.text = "";
        dialogueText.text = "";

        bool hasSeenBefore = levelData != null && PlayerPrefs.GetInt(SeenKeyPrefix + levelData.levelId, 0) == 1;
        skipButton.gameObject.SetActive(hasSeenBefore);
        skipButton.onClick.AddListener(OnSkipClicked);

        autoPlayButtonImage = autoPlayButton.GetComponent<Image>();
        autoPlayEnabled = false; // 每次進入劇情都重置,不跨場次記憶
        UpdateAutoPlayButtonVisual();
        autoPlayButton.onClick.AddListener(OnAutoPlayToggleClicked);

        // 背景/對話框/立繪這些純顯示用的 Graphic 預設都會擋 raycast,導致點畫面任何地方
        // EventSystem.IsPointerOverGameObject() 永遠回傳 true、點擊永遠被當成「點到 UI」而吃掉。
        // 這裡全部關掉,讓「點擊推進劇情」只被 Auto/Skip 這種真正的按鈕擋下來
        DisableRaycastBlocking(backgroundImage.gameObject);
        DisableRaycastBlocking(downRoot);
        DisableRaycastBlocking(middleRoot);

        StartCoroutine(PlayScript());
    }

    static void DisableRaycastBlocking(GameObject root) {
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
    }

    void Update() {
        if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject()) OnAdvanceRequested();
    }

    void OnAdvanceRequested() {
        if (isTyping) skipTypingRequested = true;
        else if (waitingForAdvance) waitingForAdvance = false;
    }

    void OnAutoPlayToggleClicked() {
        autoPlayEnabled = !autoPlayEnabled;
        UpdateAutoPlayButtonVisual();

        // 開啟當下若正好在等待推進,補上計時器,不用等下一句才生效
        if (autoPlayEnabled && waitingForAdvance && autoAdvanceRoutine == null) autoAdvanceRoutine = StartCoroutine(AutoAdvanceAfterDelay());
    }

    void UpdateAutoPlayButtonVisual() {
        if (autoPlayButtonImage != null) autoPlayButtonImage.color = autoPlayEnabled ? autoPlayOnColor : autoPlayOffColor;
    }

    void OnSkipClicked() {
        StopAllCoroutines();
        CompleteStory();
    }

    IEnumerator PlayScript() {
        foreach (StoryStep step in steps) {
            switch (step.Type) {
                case StoryStepType.Background: ApplyBackground(step.BackgroundName); break;
                case StoryStepType.Bgm: AudioManager.Instance.PlayBGM(step.AudioClipName); break;
                case StoryStepType.Enter: yield return EnterCharacter(step); break;
                case StoryStepType.Exit: yield return ExitCharacter(step); break;
                case StoryStepType.Say: yield return PlaySayStep(step); break;
                case StoryStepType.Middle: yield return PlayMiddleStep(step); break;
                case StoryStepType.Wait: yield return new WaitForSeconds(step.Duration); break;
            }
        }

        CompleteStory();
    }

    void CompleteStory() {
        if (levelData != null && !string.IsNullOrEmpty(levelData.levelId)) {
            PlayerPrefs.SetInt(SeenKeyPrefix + levelData.levelId, 1);
            PlayerPrefs.Save();
        }
        SceneManager.LoadScene("Battle");
    }

    void ApplyBackground(string name) {
        Sprite sprite = Resources.Load<Sprite>($"Images/BackGround/{name}");
        if (sprite == null) { Debug.LogWarning($"找不到背景圖:Resources/Images/BackGround/{name}"); return; }
        backgroundImage.sprite = sprite;
    }

    IEnumerator EnterCharacter(StoryStep step) {
        Image image = step.Side == StorySide.Left ? leftCharacterImage : rightCharacterImage;
        float restX = step.Side == StorySide.Left ? leftRestX : rightRestX;

        image.sprite = Resources.Load<Sprite>($"Images/Characters/{step.CharacterId}/{step.Expression}");
        if (step.Side == StorySide.Left) leftCharacterId = step.CharacterId; else rightCharacterId = step.CharacterId;

        Vector2 restPosition = new Vector2(restX, ResizeToFitAndGetBottomAlignedY(image));
        Vector2 offscreen = restPosition + new Vector2(step.Side == StorySide.Left ? -slideOffset : slideOffset, 0f);
        image.gameObject.SetActive(true);
        yield return SlidePosition(image.rectTransform, offscreen, restPosition);
    }

    // 依原圖比例把寬度縮放成 characterImageWidth,並回傳「讓底部對齊 characterImageBottomY」所需的 anchoredPosition.y——
    // 用 pivot 換算,所以不管這個 Image 的 pivot 設在哪裡(置中/置底都行)結果都正確
    float ResizeToFitAndGetBottomAlignedY(Image image) {
        RectTransform rect = image.rectTransform;
        if (image.sprite == null) return rect.anchoredPosition.y;

        float aspect = image.sprite.rect.height / image.sprite.rect.width;
        float height = characterImageWidth * aspect;
        rect.sizeDelta = new Vector2(characterImageWidth, height);

        return characterImageBottomY + height * rect.pivot.y;
    }

    IEnumerator ExitCharacter(StoryStep step) {
        Image image = step.Side == StorySide.Left ? leftCharacterImage : rightCharacterImage;
        Vector2 restPosition = image.rectTransform.anchoredPosition;
        Vector2 offscreen = restPosition + new Vector2(step.Side == StorySide.Left ? -slideOffset : slideOffset, 0f);

        if (step.Side == StorySide.Left) leftCharacterId = null; else rightCharacterId = null;

        yield return SlidePosition(image.rectTransform, restPosition, offscreen);
        image.gameObject.SetActive(false);
    }

    IEnumerator SlidePosition(RectTransform rect, Vector2 from, Vector2 to) {
        float t = 0f;
        while (t < 1f) {
            t += Time.deltaTime / Mathf.Max(0.0001f, enterExitDuration);
            rect.anchoredPosition = Vector2.Lerp(from, to, Mathf.Clamp01(t));
            yield return null;
        }
        rect.anchoredPosition = to;
    }

    IEnumerator PlaySayStep(StoryStep step) {
        characterNameText.text = GetCurrentCharacterId(step.Side);
        leftCharacterImage.color = step.Side == StorySide.Left ? activeSpeakerColor : dimmedSpeakerColor;
        rightCharacterImage.color = step.Side == StorySide.Right ? activeSpeakerColor : dimmedSpeakerColor;

        yield return TypeText(dialogueText, step.Text);
        yield return WaitForAdvance();
    }

    string GetCurrentCharacterId(StorySide side) {
        if (side == StorySide.Left) return leftCharacterId ?? "";
        if (side == StorySide.Right) return rightCharacterId ?? "";
        return "";
    }

    IEnumerator PlayMiddleStep(StoryStep step) {
        yield return FadeMiddleMode(true);
        yield return TypeText(middleText, step.Text);
        yield return WaitForAdvance();
        yield return FadeMiddleMode(false);
    }

    IEnumerator FadeMiddleMode(bool enteringMiddle) {
        CanvasGroup downGroup = GetOrAddCanvasGroup(downRoot);
        CanvasGroup middleGroup = GetOrAddCanvasGroup(middleRoot);

        if (enteringMiddle) {
            middleRoot.SetActive(true);
            middleGroup.alpha = 1f; // 淡入的是背景變黑+對話框消失,文字要等黑幕淡完才開始打字,這裡不用淡
            middleText.text = "";
        }

        Color fromBg = backgroundImage.color;
        Color toBg = enteringMiddle ? middleBlackColor : backgroundOriginalColor;
        float fromDownAlpha = downGroup.alpha;
        float toDownAlpha = enteringMiddle ? 0f : 1f;
        float fromMiddleAlpha = middleGroup.alpha;
        float toMiddleAlpha = enteringMiddle ? 1f : 0f;

        float t = 0f;
        while (t < middleFadeDuration) {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / middleFadeDuration);
            backgroundImage.color = Color.Lerp(fromBg, toBg, p);
            downGroup.alpha = Mathf.Lerp(fromDownAlpha, toDownAlpha, p);
            middleGroup.alpha = Mathf.Lerp(fromMiddleAlpha, toMiddleAlpha, p);
            yield return null;
        }

        backgroundImage.color = toBg;
        downGroup.alpha = toDownAlpha;
        middleGroup.alpha = toMiddleAlpha;

        if (!enteringMiddle) middleRoot.SetActive(false);
    }

    static CanvasGroup GetOrAddCanvasGroup(GameObject go) {
        CanvasGroup group = go.GetComponent<CanvasGroup>();
        if (group == null) group = go.AddComponent<CanvasGroup>();
        return group;
    }

    IEnumerator TypeText(Text label, string text) {
        isTyping = true;
        skipTypingRequested = false;
        label.text = "";

        float interval = 1f / Mathf.Max(1f, typewriterCharsPerSecond);
        for (int i = 0; i < text.Length; i++) {
            if (skipTypingRequested) break;
            label.text += text[i];
            yield return new WaitForSeconds(interval);
        }

        label.text = text;
        isTyping = false;
        skipTypingRequested = false;
    }

    IEnumerator WaitForAdvance() {
        waitingForAdvance = true;
        if (autoPlayEnabled) autoAdvanceRoutine = StartCoroutine(AutoAdvanceAfterDelay());

        while (waitingForAdvance) yield return null;

        if (autoAdvanceRoutine != null) { StopCoroutine(autoAdvanceRoutine); autoAdvanceRoutine = null; }
    }

    IEnumerator AutoAdvanceAfterDelay() {
        yield return new WaitForSeconds(autoPlayDelay);
        waitingForAdvance = false;
        autoAdvanceRoutine = null;
    }
}
