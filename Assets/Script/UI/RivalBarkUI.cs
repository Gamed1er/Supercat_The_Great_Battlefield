using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 輕量版對話元件(閹割版 Story):疊在任何場景上顯示一小段連續台詞,只支援 StoryStep 裡的 Say/Wait
// (沒有背景/配樂/進出場),整段對話固定用同一張立繪(呼叫端指定角色 ID/表情)。顯示期間 Time.timeScale = 0
// 暫停遊戲,跟 PauseMenuUI 共用同一個開關,優先層級比設定選單低——如果玩家在對話播放中途按 Esc 開啟設定,
// 關閉設定面板時 PauseMenuUI 會直接把 timeScale 設回 1,這裡不特別處理這個邊界情況(對話文字會在設定面板
// 背後繼續播完,不影響正確性,只是體驗上不是最完美)。左鍵推進台詞,沒有台詞排隊時整個面板隱藏。
public class RivalBarkUI : MonoBehaviour {
    [Header("UI (Inspector 手動接線,本腳本不生成任何 UI 物件)")]
    [SerializeField] GameObject root;
    [SerializeField] Image portraitImage;
    [SerializeField] Text nameText;
    [SerializeField] Text dialogueText;

    [Header("節奏")]
    [SerializeField] float typewriterCharsPerSecond = 30f;

    bool isTyping;
    bool skipTypingRequested;
    bool waitingForAdvance;

    // root 沒接線是設定錯誤,故意不做 null 檢查(見 CLAUDE.md「Inspector-wired references」)。
    void Awake() {
        root.SetActive(false);
        // 立繪/文字這些純顯示用的 Graphic 預設會擋 raycast,導致點在它們上面時
        // EventSystem.IsPointerOverGameObject() 判定為真、反而不算「點畫面推進」——比照 StoryManager 的做法關掉。
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
    }

    void Update() {
        if (!waitingForAdvance && !isTyping) return;
        if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject()) OnAdvanceRequested();
    }

    void OnAdvanceRequested() {
        if (isTyping) skipTypingRequested = true;
        else if (waitingForAdvance) waitingForAdvance = false;
    }

    // steps 只會用到 Say/Wait,其餘型別(這個短句系統的腳本區塊本來就不該出現)直接略過。
    // steps 是空的是合法情況(這個區塊本來就沒有台詞),立刻呼叫 onComplete,不會暫停遊戲。
    public void Play(List<StoryStep> steps, string characterId, string expression, Action onComplete) {
        if (steps == null || steps.Count == 0) { onComplete?.Invoke(); return; }
        StartCoroutine(PlayRoutine(steps, characterId, expression, onComplete));
    }

    IEnumerator PlayRoutine(List<StoryStep> steps, string characterId, string expression, Action onComplete) {
        float previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        root.SetActive(true);
        if (nameText != null) nameText.text = characterId ?? "";
        if (portraitImage != null) {
            Sprite sprite = !string.IsNullOrEmpty(characterId) ? Resources.Load<Sprite>($"Images/Characters/{characterId}/{expression}") : null;
            portraitImage.sprite = sprite;
            portraitImage.enabled = sprite != null;
        }

        foreach (StoryStep step in steps) {
            switch (step.Type) {
                case StoryStepType.Say:
                    yield return TypeText(step.Text);
                    yield return WaitForAdvance();
                    break;
                case StoryStepType.Wait:
                    yield return new WaitForSecondsRealtime(step.Duration); // timeScale = 0 時一般 WaitForSeconds 永遠不會過
                    break;
            }
        }

        root.SetActive(false);
        Time.timeScale = previousTimeScale;
        onComplete?.Invoke();
    }

    IEnumerator TypeText(string text) {
        isTyping = true;
        skipTypingRequested = false;
        if (dialogueText != null) dialogueText.text = "";

        float interval = 1f / Mathf.Max(1f, typewriterCharsPerSecond);
        for (int i = 0; i < text.Length; i++) {
            if (skipTypingRequested) break;
            if (dialogueText != null) dialogueText.text += text[i];
            yield return new WaitForSecondsRealtime(interval);
        }

        if (dialogueText != null) dialogueText.text = text;
        isTyping = false;
        skipTypingRequested = false;
    }

    IEnumerator WaitForAdvance() {
        waitingForAdvance = true;
        while (waitingForAdvance) yield return null;
    }
}
