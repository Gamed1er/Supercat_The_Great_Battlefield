using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 全遊戲共用的黑幕轉場:漸暗 0.5 秒(BGM 同時淡出)→ 全黑至少 0.5 秒(期間切換畫面/載入場景)→ 漸顯 0.2 秒。
// 單例,首次被存取時自動建立並跨場景保留(比照 AudioManager),不需要放進場景。
// 換場景一律經過 GameFlow(它會呼叫 TryLoadScene),同一場景內的畫面切換(例如大廳的標題 ↔ 地圖)直接呼叫 TryTransition。
// 轉場期間黑幕擋住所有 UI 點擊,再要求轉場會直接被忽略(避免連點按鈕觸發兩次換場景);
// 時間一律用不受 Time.timeScale 影響的時間,暫停中(timeScale = 0)觸發的轉場也能正常跑完
public class ScreenFader : MonoBehaviour {
    const float FadeOutDuration = 0.5f;
    const float MinBlackDuration = 0.5f;
    const float FadeInDuration = 0.2f;

    static ScreenFader instance;

    public static ScreenFader Instance {
        get {
            if (instance == null) {
                var go = new GameObject(nameof(ScreenFader));
                instance = go.AddComponent<ScreenFader>();
            }
            return instance;
        }
    }

    public bool IsTransitioning { get; private set; }

    CanvasGroup canvasGroup;

    void Awake() {
        if (instance != null && instance != this) {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue; // 蓋在所有 UI 上面
        gameObject.AddComponent<GraphicRaycaster>(); // 黑幕要能吃掉點擊,才擋得住後面的按鈕

        canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;

        GameObject curtain = new GameObject("Curtain", typeof(RectTransform), typeof(Image));
        curtain.transform.SetParent(transform, false);
        RectTransform rect = (RectTransform)curtain.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        curtain.GetComponent<Image>().color = Color.black;
    }

    // 同一場景內的轉場:onBlack 在全黑時執行(切換畫面/播新 BGM)。正在轉場中則忽略並回傳 false
    public bool TryTransition(Action onBlack) => TryRun(onBlack, null);

    // 換場景的轉場:startLoad 在全黑時呼叫並回傳非同步載入,載完且全黑滿 MinBlackDuration 才開始漸顯
    public bool TryLoadScene(Func<AsyncOperation> startLoad) => TryRun(null, startLoad);

    bool TryRun(Action onBlack, Func<AsyncOperation> startLoad) {
        if (IsTransitioning) return false;
        StartCoroutine(TransitionRoutine(onBlack, startLoad));
        return true;
    }

    IEnumerator TransitionRoutine(Action onBlack, Func<AsyncOperation> startLoad) {
        IsTransitioning = true;
        canvasGroup.blocksRaycasts = true;

        AudioManager.Instance.FadeOutBGM(FadeOutDuration);
        yield return FadeRoutine(0f, 1f, FadeOutDuration);

        float blackStartTime = Time.unscaledTime;
        onBlack?.Invoke();
        if (startLoad != null) {
            AsyncOperation load = startLoad();
            while (!load.isDone) yield return null;
        }
        while (Time.unscaledTime - blackStartTime < MinBlackDuration) yield return null;

        yield return FadeRoutine(1f, 0f, FadeInDuration);

        canvasGroup.blocksRaycasts = false;
        IsTransitioning = false;
    }

    IEnumerator FadeRoutine(float from, float to, float duration) {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime) {
            canvasGroup.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        canvasGroup.alpha = to;
    }
}
