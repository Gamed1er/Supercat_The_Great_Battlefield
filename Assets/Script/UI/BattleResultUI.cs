using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 戰鬥結算(勝利/失敗)畫面:Update() 輪詢 LevelManager.LevelCleared / PlayerBase.IsDead 來觸發,
// 觸發後播對應的過場流程,最後導向「回大廳」按鈕——大廳場景還沒做,目前先 Debug.Log。
// 面板/文字/按鈕在 Inspector 手動接線,比照 PauseMenuUI 的風格;
// 唯獨失敗流程用到的「螢幕變暗」圖層是執行期動態生成(用 PlaceholderSprite,跟 Bullet/SmokePuff 同一套暫時素材),
// 場景裡不需要預先放這個物件。
public class BattleResultUI : MonoBehaviour {
    [System.Serializable]
    struct RewardEntry {
        public string label;
        public int amount;
    }

    const float VictoryStartDelay = 1f; // 整個勝利流程延後這麼久才開始,等最後一隻敵人的死亡淡出動畫先播(見 EnemyBase.DeathFadeRoutine)
    const float HudSlideDuration = 1f; // UI 隱藏動畫時間,勝利/失敗共用
    const float ResultTextSlideDuration = 1.5f;
    const float DetailsRevealDelay = 2f; // 黑框/回大廳按鈕最早出現的時間,從結算動畫一開始算起(不是從前面動畫播完才開始算)
    const float InfoBoxScaleDuration = 0.2f; // 黑框從小放大進場的時間
    const float DeathMoveDuration = 0.5f; // 死亡瞬間位移到定位點的時間
    const int ScreenDarkenSortingOrder = 1; // 場上其他 SpriteRenderer 目前都是 0,只要比 0 大就會蓋過去
    const int PlayerSortingOrderOverDarken = 2; // 必須比 ScreenDarkenSortingOrder 大,玩家才不會被黑幕蓋住

    [Header("HUD (結算時滑出;分別對應「血條」「貓咪狀態」兩個既有 UI 父物件)")]
    // 注意:群組本身的 RectTransform 常常是 size (0,0) 的純掛載節點(子物件各自用 offset 決定位置),
    // 不能拿群組自己的 rect.width/height 當滑出距離(算出來是 0,滑了也看不出來)——改成直接指定像素距離
    [SerializeField] RectTransform healthBarGroup; // 往上滑出(距離)
    [SerializeField] float healthBarSlideDistance = 300f;
    [SerializeField] RectTransform statusGroup; // 往左滑到指定的絕對 X 座標(不是用距離,起始位置不是 0 時用距離會滑不夠遠、露出一半)
    [SerializeField] float statusGroupTargetX = -400f;

    [Header("結果文字 (勝利/失敗共用同一組,只換文字/顏色)")]
    [SerializeField] RectTransform resultTextRoot; // 錨點/軸心需為正中央,從畫面上方滑入
    [SerializeField] Text resultText;
    [SerializeField] Color victoryColor = Color.white;
    [SerializeField] Color defeatColor = Color.white;
    [SerializeField] float resultTextOffsetY = 250f; // 相對畫面中心的錨點 Y 座標(像素),正值往上

    [Header("提示黑框 (勝利:獎勵清單 / 失敗:敵方剩餘血量,共用同一個物件)")]
    [SerializeField] GameObject infoBoxRoot; // 錨點/軸心需為正中央
    [SerializeField] Text infoBoxText;
    [SerializeField] float infoBoxOffsetY = -250f;

    [Header("回大廳按鈕 (大廳場景尚未製作,目前先 Debug.Log)")]
    [SerializeField] GameObject returnButtonRoot; // 錨點/軸心需為正中央
    [SerializeField] Button returnButton;
    [SerializeField] float returnButtonOffsetY = -350f;

    [Header("失敗:死亡定位")]
    [SerializeField] float deathPositionOffsetY = 0f; // 相對鏡頭定格畫面中心的世界座標高度(world unit);0 = 正中間

    [Header("勝利獎勵清單 (先寫死數字,還沒接存檔系統;複數項目會依序顯示)")]
    [SerializeField] List<RewardEntry> victoryRewards = new List<RewardEntry> { new RewardEntry { label = "XP", amount = 100 } };

    [Header("音效 (檔名對應 Assets/Resources/Audio/SFX/{名稱}.ogg)")]
    [SerializeField] string victorySfx = "level_pass";
    [SerializeField] string defeatSfx = "level_fail";
    [SerializeField] string rewardBoxSfx = "reward"; // 只有勝利的獎勵框跳出時播,失敗的黑框不用
    [SerializeField] string buttonClickSfx = "button";

    [Header("對手短台詞 (選填;戰後結果先播這個,播完/沒有符合的台詞才顯示下面的勝負畫面)")]
    [SerializeField] RivalDialogueManager rivalDialogueManager;

    PlayerBase player;
    LevelManager levelManager;
    CameraFollow cameraFollow;
    RectTransform canvasRect;
    bool resultTriggered;

    // 用 Start() 而不是 Awake():LevelManager 在自己的 Awake() 動態生成玩家/敵人,
    // 不同物件的 Awake 順序不保證,但所有物件的 Awake 一定會在任何 Start() 之前跑完(比照 UIManager 的作法)
    void Start() {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.GetComponent<PlayerBase>();

        levelManager = LevelManager.Instance;
        if (Camera.main != null) cameraFollow = Camera.main.GetComponent<CameraFollow>();

        if (returnButton != null) returnButton.onClick.AddListener(OnReturnToLobby);

        if (resultTextRoot != null) resultTextRoot.gameObject.SetActive(false);
        if (infoBoxRoot != null) infoBoxRoot.SetActive(false);
        if (returnButtonRoot != null) returnButtonRoot.SetActive(false);

        // 黑框/結果文字本身的 Image/Text 預設會擋 raycast,導致點在它們「上面」時
        // IsPointerOverGameObject() 判定為真、反而不算數——比照 StoryManager 的做法,
        // 把這兩個純顯示用物件底下所有 Graphic 的 raycastTarget 關掉,讓「點畫面任意位置」
        // 真的涵蓋點在它們本身上面的情況(它們都不需要接收點擊,只有 returnButton 才需要)
        DisableRaycastTarget(infoBoxRoot);
        if (resultTextRoot != null) DisableRaycastTarget(resultTextRoot.gameObject);
    }

    static void DisableRaycastTarget(GameObject root) {
        if (root == null) return;
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) {
            graphic.raycastTarget = false;
        }
    }

    void Update() {
        if (resultTriggered || player == null) return;

        if (player.IsDead) {
            resultTriggered = true;
            StartCoroutine(PlayRivalBarkThenContinue(false, () => StartCoroutine(DefeatSequence())));
        } else if (levelManager != null && levelManager.LevelCleared) {
            resultTriggered = true;
            StartCoroutine(PlayRivalBarkThenContinue(true, () => StartCoroutine(VictorySequence())));
        }
    }

    // 顯示勝負畫面之前,先讓對手的短台詞播完(沒有符合的台詞就直接繼續,見 RivalDialogueManager.PlaySection)。
    // rivalDialogueManager 沒接線是設定錯誤,故意不做 null 檢查,讓它直接丟 NullReferenceException(見 CLAUDE.md「Inspector-wired references」)。
    // 無傷通關的判定用 !player.TookEnemyDamageThisRun,最快通關時間用 levelManager.ClearTimeSeconds
    // (輸的那場 ClearTimeSeconds 還是 0,但反正 won=false 時 RivalDialogueManager 不會拿這個值去比較最快紀錄)。
    IEnumerator PlayRivalBarkThenContinue(bool won, Action continuation) {
        bool barkDone = false;
        rivalDialogueManager.PlayPostBattleBark(won, !player.TookEnemyDamageThisRun,
            levelManager != null ? levelManager.ClearTimeSeconds : 0f, () => barkDone = true);

        yield return new WaitUntil(() => barkDone);
        continuation();
    }

    IEnumerator VictorySequence() {
        yield return new WaitForSeconds(VictoryStartDelay); // 等最後一隻敵人的死亡淡出動畫先播完,整個流程才開始

        float startTime = Time.time;

        levelManager?.PauseAllEnemies(true);
        player.SetSuppressNormalAttack(true);
        player.SetForcedInvincible(true);

        AudioManager.Instance.StopBGM();
        AudioManager.Instance.PlaySFX(victorySfx);

        Coroutine resultTextCoroutine = StartCoroutine(ShowResultText("完全勝利", victoryColor)); // 一開始就進場,跟 HUD 滑出同時進行
        yield return StartCoroutine(SlideHudOut());
        yield return resultTextCoroutine; // 確保文字動畫也跑完(通常比 HUD 滑出久)

        yield return WaitUntilElapsedSince(startTime, DetailsRevealDelay); // 獎勵黑框最早要在動畫開始後 2 秒才出現

        foreach (RewardEntry reward in victoryRewards) {
            yield return StartCoroutine(ShowInfoBoxUntilClicked($"獲得 {reward.label} +{reward.amount}"));
        }

        ShowReturnButton();
    }

    IEnumerator DefeatSequence() {
        float startTime = Time.time;

        levelManager?.PauseAllEnemies(true);

        AudioManager.Instance.StopBGM();
        AudioManager.Instance.PlaySFX(defeatSfx);

        Vector3 cameraPosition = FreezeCameraAndGetPosition();
        float screenHeight = Camera.main != null ? Camera.main.orthographicSize * 2f : 0f;
        Vector3 deathTarget = cameraPosition + Vector3.up * deathPositionOffsetY;
        deathTarget.z = player.transform.position.z;

        player.BeginDeathSequence(deathTarget, DeathMoveDuration);
        SpawnScreenDarken(cameraPosition, screenHeight);
        Coroutine resultTextCoroutine = StartCoroutine(ShowResultText("慘敗...", defeatColor)); // 不等死亡位移跑完,跟黑屏一起進來

        yield return new WaitForSeconds(DeathMoveDuration);

        yield return StartCoroutine(SlideHudOut());
        yield return resultTextCoroutine; // 確保文字動畫也跑完(通常比死亡位移+HUD滑出久)

        yield return WaitUntilElapsedSince(startTime, DetailsRevealDelay); // 黑框/按鈕最早要在動畫開始後 2 秒才出現

        int enemyPercent = levelManager != null ? Mathf.CeilToInt(levelManager.EnemyGroupHealthRatio * 100f) : 0;
        ShowInfoBoxPersistent($"敵人剩餘血量 {enemyPercent}%");

        ShowReturnButton();
    }

    // 確保從 since 算起至少過了 duration 秒才繼續往下走;前面的動畫如果已經花了比 duration 還久,就不會再多等
    static IEnumerator WaitUntilElapsedSince(float since, float duration) {
        float remaining = duration - (Time.time - since);
        if (remaining > 0f) yield return new WaitForSeconds(remaining);
    }

    Vector3 FreezeCameraAndGetPosition() {
        Camera cam = Camera.main;
        if (cam == null) return player.transform.position;

        cameraFollow?.Freeze();
        return cam.transform.position;
    }

    // 場上其他 sprite 都在 z=0,sortingOrder=0(見各 prefab),這裡用比較大的 sortingOrder 蓋過背景/敵人,
    // 再把玩家的 sortingOrder 拉得更高,確保黑幕蓋不到玩家自己
    void SpawnScreenDarken(Vector3 cameraPosition, float screenHeight) {
        Camera cam = Camera.main;
        if (cam == null) return;

        var darkenObj = new GameObject("ScreenDarken(結算生成)");
        var sr = darkenObj.AddComponent<SpriteRenderer>();
        sr.sprite = PlaceholderSprite.Get(Color.black);
        sr.sortingOrder = ScreenDarkenSortingOrder;

        float screenWidth = screenHeight * cam.aspect;
        darkenObj.transform.position = new Vector3(cameraPosition.x, cameraPosition.y, player.transform.position.z);
        darkenObj.transform.localScale = new Vector3(screenWidth, screenHeight, 1f);

        if (player.TryGetComponent(out SpriteRenderer playerSprite)) {
            playerSprite.sortingOrder = PlayerSortingOrderOverDarken;
        }
    }

    IEnumerator SlideHudOut() {
        Coroutine health = healthBarGroup != null ? StartCoroutine(SlideRect(healthBarGroup, Vector2.up * healthBarSlideDistance)) : null;
        Coroutine status = statusGroup != null
            ? StartCoroutine(SlideRect(statusGroup, new Vector2(statusGroupTargetX - statusGroup.anchoredPosition.x, 0f)))
            : null;

        if (health != null) yield return health;
        if (status != null) yield return status;
    }

    IEnumerator SlideRect(RectTransform rect, Vector2 offset) {
        Vector2 start = rect.anchoredPosition;
        Vector2 end = start + offset;

        float elapsed = 0f;
        while (elapsed < HudSlideDuration) {
            elapsed += Time.deltaTime;
            rect.anchoredPosition = Vector2.Lerp(start, end, elapsed / HudSlideDuration);
            yield return null;
        }

        rect.anchoredPosition = end;
    }

    IEnumerator ShowResultText(string text, Color color) {
        if (resultTextRoot == null) yield break;

        if (resultText != null) {
            resultText.text = text;
            resultText.color = color;
        }

        float restY = resultTextOffsetY;
        float startY = CanvasRect().rect.height; // 畫面上方外面
        Vector2 start = new Vector2(0f, startY);
        Vector2 end = new Vector2(0f, restY);

        resultTextRoot.anchoredPosition = start;
        resultTextRoot.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < ResultTextSlideDuration) {
            elapsed += Time.deltaTime;
            resultTextRoot.anchoredPosition = Vector2.Lerp(start, end, elapsed / ResultTextSlideDuration);
            yield return null;
        }

        resultTextRoot.anchoredPosition = end; // 停留在畫面上,不需要再隱藏
    }

    // 勝利獎勵用:顯示黑框(從小放大進場),等畫面任意位置(非其他 UI)點擊一下才隱藏,換下一筆
    IEnumerator ShowInfoBoxUntilClicked(string text) {
        if (infoBoxRoot == null) yield break;

        PositionInfoBox();
        if (infoBoxText != null) infoBoxText.text = text;
        infoBoxRoot.SetActive(true);
        AudioManager.Instance.PlaySFX(rewardBoxSfx); // 只有勝利的獎勵框跳出時播,失敗的 ShowInfoBoxPersistent 不會呼叫到這裡

        yield return StartCoroutine(ScaleInfoBoxIn()); // 進場動畫本身已經跨了好幾幀,不用再額外跳一幀擋觸發結算的那次點擊
        yield return new WaitUntil(() => Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject());

        infoBoxRoot.SetActive(false);
    }

    // 失敗提示用:顯示黑框(從小放大進場)後直接常駐,不需要點擊隱藏
    void ShowInfoBoxPersistent(string text) {
        if (infoBoxRoot == null) return;

        PositionInfoBox();
        if (infoBoxText != null) infoBoxText.text = text;
        infoBoxRoot.SetActive(true);
        StartCoroutine(ScaleInfoBoxIn());
    }

    IEnumerator ScaleInfoBoxIn() {
        if (!infoBoxRoot.TryGetComponent(out RectTransform rect)) yield break;

        rect.localScale = Vector3.zero;

        float elapsed = 0f;
        while (elapsed < InfoBoxScaleDuration) {
            elapsed += Time.deltaTime;
            rect.localScale = Vector3.Lerp(Vector3.zero, Vector3.one, elapsed / InfoBoxScaleDuration);
            yield return null;
        }

        rect.localScale = Vector3.one;
    }

    void PositionInfoBox() {
        if (!infoBoxRoot.TryGetComponent(out RectTransform rect)) return;
        rect.anchoredPosition = new Vector2(0f, infoBoxOffsetY);
    }

    void ShowReturnButton() {
        if (returnButtonRoot == null) return;

        if (returnButtonRoot.TryGetComponent(out RectTransform rect)) {
            rect.anchoredPosition = new Vector2(0f, returnButtonOffsetY);
        }

        returnButtonRoot.SetActive(true);
    }

    void OnReturnToLobby() {
        AudioManager.Instance.PlaySFX(buttonClickSfx);

        // 大廳場景還沒做,先印出來確認流程有跑到這一步
        Debug.Log("回大廳(大廳場景尚未製作)");
    }

    RectTransform CanvasRect() {
        if (canvasRect == null) {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null) canvasRect = canvas.rootCanvas.GetComponent<RectTransform>();
        }

        return canvasRect;
    }
}
