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

    const float HudSlideDuration = 0.3f;
    const float ResultTextSlideDuration = 0.5f;
    const float VictoryRewardDelay = 1f; // 「完全勝利」文字滑入完成後,等這麼久才跳出第一個獎勵黑框
    const float DeathMoveDuration = 0.5f; // 死亡瞬間位移到定位點的時間
    const int ScreenDarkenSortingOrder = 1; // 場上其他 SpriteRenderer 目前都是 0,只要比 0 大就會蓋過去
    const int PlayerSortingOrderOverDarken = 2; // 必須比 ScreenDarkenSortingOrder 大,玩家才不會被黑幕蓋住

    [Header("HUD (結算時滑出;分別對應「血條」「貓咪狀態」兩個既有 UI 父物件)")]
    [SerializeField] RectTransform healthBarGroup; // 往上滑出(距離 = 自己的高度)
    [SerializeField] RectTransform statusGroup; // 往左滑出(距離 = 自己的寬度)

    [Header("結果文字 (勝利/失敗共用同一組,只換文字/顏色)")]
    [SerializeField] RectTransform resultTextRoot; // 錨點/軸心需為正中央,從畫面右側滑入
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
    [SerializeField] float deathPositionOffsetY = 1f; // 相對鏡頭定格畫面中心的世界座標高度(world unit),正值往上

    [Header("勝利獎勵清單 (先寫死數字,還沒接存檔系統;複數項目會依序顯示)")]
    [SerializeField] List<RewardEntry> victoryRewards = new List<RewardEntry> { new RewardEntry { label = "XP", amount = 100 } };

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
    }

    void Update() {
        if (resultTriggered || player == null) return;

        if (player.IsDead) {
            resultTriggered = true;
            StartCoroutine(DefeatSequence());
        } else if (levelManager != null && levelManager.LevelCleared) {
            resultTriggered = true;
            StartCoroutine(VictorySequence());
        }
    }

    IEnumerator VictorySequence() {
        player.SetSuppressNormalAttack(true);
        player.SetForcedInvincible(true);

        yield return StartCoroutine(SlideHudOut());
        yield return StartCoroutine(ShowResultText("完全勝利", victoryColor));

        yield return new WaitForSeconds(VictoryRewardDelay);

        foreach (RewardEntry reward in victoryRewards) {
            yield return StartCoroutine(ShowInfoBoxUntilClicked($"獲得 {reward.label} +{reward.amount}"));
        }

        ShowReturnButton();
    }

    IEnumerator DefeatSequence() {
        levelManager?.PauseAllEnemies(true);

        Vector3 cameraPosition = FreezeCameraAndGetPosition();
        float screenHeight = Camera.main != null ? Camera.main.orthographicSize * 2f : 0f;
        Vector3 deathTarget = cameraPosition + Vector3.up * deathPositionOffsetY;
        deathTarget.z = player.transform.position.z;

        player.BeginDeathSequence(deathTarget, DeathMoveDuration);
        SpawnScreenDarken(cameraPosition, screenHeight);

        yield return new WaitForSeconds(DeathMoveDuration);

        yield return StartCoroutine(SlideHudOut());
        yield return StartCoroutine(ShowResultText("慘敗...", defeatColor));

        int enemyPercent = levelManager != null ? Mathf.CeilToInt(levelManager.EnemyGroupHealthRatio * 100f) : 0;
        ShowInfoBoxPersistent($"敵人剩餘血量 {enemyPercent}%");

        ShowReturnButton();
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
        Coroutine health = healthBarGroup != null ? StartCoroutine(SlideRect(healthBarGroup, Vector2.up)) : null;
        Coroutine status = statusGroup != null ? StartCoroutine(SlideRect(statusGroup, Vector2.left)) : null;

        if (health != null) yield return health;
        if (status != null) yield return status;
    }

    IEnumerator SlideRect(RectTransform rect, Vector2 direction) {
        Vector2 start = rect.anchoredPosition;
        float distance = direction.y != 0f ? rect.rect.height : rect.rect.width;
        Vector2 end = start + direction * distance;

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
        float startX = CanvasRect().rect.width; // 畫面右側外面
        Vector2 start = new Vector2(startX, restY);
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

    // 勝利獎勵用:顯示黑框,等畫面任意位置(非其他 UI)點擊一下才隱藏,換下一筆
    IEnumerator ShowInfoBoxUntilClicked(string text) {
        if (infoBoxRoot == null) yield break;

        PositionInfoBox();
        if (infoBoxText != null) infoBoxText.text = text;
        infoBoxRoot.SetActive(true);

        yield return null; // 跳過這一幀,避免觸發這次結算的那次點擊被立刻算成確認
        yield return new WaitUntil(() => Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject());

        infoBoxRoot.SetActive(false);
    }

    // 失敗提示用:顯示黑框後直接常駐,不需要點擊隱藏
    void ShowInfoBoxPersistent(string text) {
        if (infoBoxRoot == null) return;

        PositionInfoBox();
        if (infoBoxText != null) infoBoxText.text = text;
        infoBoxRoot.SetActive(true);
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
