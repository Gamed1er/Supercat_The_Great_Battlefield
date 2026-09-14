using System.Collections.Generic;
using UnityEngine;

// 戰鬥場景(Battle.unity)的進場控制:依 LevelData 動態組出整個關卡——
// 設定地圖邊界(牆)與鏡頭範圍、鋪背景、生成玩家與怪物並套用難度、播 BGM,並維護怪池的血量加總/全滅判定給 UI 用。
public class LevelManager : MonoBehaviour {
    const float BackgroundZ = 10f; // 攝影機在 z=-10 朝 +z 看,背景放在比玩家/敵人(z=0)更遠的 z 才不會擋到他們

    [SerializeField] LevelData levelData;

    [Header("難度 (暫時手動指定,之後由選關/選難度畫面傳入;連續值 0~5,越大越難)")]
    [SerializeField, Range(0, EnemyBase.MaxDifficulty)] int difficulty = 2;

    [SerializeField] GameObject playerPrefab;

    // 玩家自動回血的關卡倍率,依難度(0~5)直接對應,難度越高倍率越低;索引 = difficulty,見 RegenLevelMultiplier
    static readonly float[] regenLevelMultiplierByDifficulty = { 1.6f, 1.45f, 1.3f, 1.15f, 1f, 0.75f };

    public static LevelManager Instance { get; private set; }

    readonly List<EnemyBase> enemies = new List<EnemyBase>();
    float totalStartingHealth;
    bool levelCleared;

    public string LevelName => levelData.levelName;
    public float RegenLevelMultiplier => regenLevelMultiplierByDifficulty[Mathf.Clamp(difficulty, 0, EnemyBase.MaxDifficulty)];
    // 給 BattleResultUI 輪詢用:是否已通關(所有敵人已死亡),見 Update()
    public bool LevelCleared => levelCleared;

    // 怪池目前總血量 / 關卡開始時的總血量,分母固定,打死小怪時血條會明顯掉一塊
    public float EnemyGroupHealthRatio {
        get {
            if (totalStartingHealth <= 0f) return 0f;

            float currentTotal = 0f;
            foreach (EnemyBase enemy in enemies) currentTotal += Mathf.Max(0f, enemy.health);

            return Mathf.Clamp01(currentTotal / totalStartingHealth);
        }
    }

    void Awake() {
        Instance = this;

        // 「1 倍率單位 = 1 個螢幕大小」:半寬高直接從攝影機當下的 orthographicSize/aspect 算,
        // 不寫死解析度數字,不管遊戲實際跑在什麼比例的螢幕上,倍率的意義都不會跑掉
        Vector2 baseHalfExtent = GetBaseHalfExtent();
        Vector2 halfExtent = new Vector2(baseHalfExtent.x * levelData.mapWidthMultiplier, baseHalfExtent.y * levelData.mapHeightMultiplier);

        SetupWalls(halfExtent);
        SetupBackground(baseHalfExtent);

        GameObject playerObj = Instantiate(playerPrefab, levelData.playerSpawnPosition, Quaternion.identity);

        SpawnEnemyPool(halfExtent);
        SetupCamera(playerObj.transform, halfExtent);
    }

    static Vector2 GetBaseHalfExtent() {
        Camera cam = Camera.main;
        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;
        return new Vector2(halfWidth, halfHeight);
    }

    void Start() {
        AudioManager.Instance.PlayBGM(levelData.bgmName);
    }

    void Update() {
        if (levelCleared || enemies.Count == 0) return;

        foreach (EnemyBase enemy in enemies) {
            if (!enemy.IsDead) return;
        }

        levelCleared = true;
        Debug.Log("關卡通關");
    }

    // 依地圖邊界移動場景裡既有的四面牆(Up/Down/Left/Right,用 Wall tag 找),牆本身的碰撞箱已經夠大,只需要改位置
    void SetupWalls(Vector2 halfExtent) {
        foreach (GameObject wallObj in GameObject.FindGameObjectsWithTag("Wall")) {
            Transform wall = wallObj.transform;
            Vector3 pos = wall.localPosition;

            if (Mathf.Abs(pos.x) > Mathf.Abs(pos.y)) {
                pos.x = pos.x > 0f ? halfExtent.x : -halfExtent.x;
            } else {
                pos.y = pos.y > 0f ? halfExtent.y : -halfExtent.y;
            }

            wall.localPosition = pos;
        }
    }

    // 背景貼圖鋪滿地圖:貼圖數量依倍率無條件進位(避免地圖邊緣露出場外),每張對應一份基準地圖大小,置中排列
    void SetupBackground(Vector2 baseHalfExtent) {
        if (levelData.backgroundTilePrefab == null) return;

        float tileWidth = baseHalfExtent.x * 2f;
        float tileHeight = baseHalfExtent.y * 2f;

        int columns = Mathf.Max(1, Mathf.CeilToInt(levelData.mapWidthMultiplier));
        int rows = Mathf.Max(1, Mathf.CeilToInt(levelData.mapHeightMultiplier));

        for (int x = 0; x < columns; x++) {
            for (int y = 0; y < rows; y++) {
                float posX = (x - (columns - 1) * 0.5f) * tileWidth;
                float posY = (y - (rows - 1) * 0.5f) * tileHeight;
                Instantiate(levelData.backgroundTilePrefab, new Vector3(posX, posY, BackgroundZ), Quaternion.identity, transform);
            }
        }
    }

    void SpawnEnemyPool(Vector2 halfExtent) {
        Vector2 groundMin = new Vector2(-halfExtent.x, -halfExtent.y);
        Vector2 groundMax = new Vector2(halfExtent.x, -halfExtent.y + halfExtent.y * 2f * levelData.groundHeightPercent);

        totalStartingHealth = 0f;

        foreach (EnemySpawnEntry entry in levelData.enemyPool) {
            if (entry.enemyPrefab == null) continue;

            GameObject enemyObj = Instantiate(entry.enemyPrefab, entry.spawnPosition, Quaternion.identity);
            if (!enemyObj.TryGetComponent(out EnemyBase enemy)) continue;

            enemy.ApplyDifficulty(difficulty); // 立刻套用,搶在敵人自己的 Start() 抓 MaxHealth 之前完成
            enemy.SetGroundBounds(groundMin, groundMax);
            enemies.Add(enemy);
            totalStartingHealth += enemy.health; // 不用 enemy.MaxHealth:那要等敵人自己的 Start() 才會設定,時機不保證早於這裡
        }
    }

    // 戰鬥結算(失敗)流程用:暫停/恢復所有已生成的敵人 AI
    public void PauseAllEnemies(bool paused) {
        foreach (EnemyBase enemy in enemies) enemy.SetPaused(paused);
    }

    // 鏡頭跟隨玩家,夾在地圖邊界內;CameraFollow 不需要在場景裡手動掛,這裡自動加上去
    void SetupCamera(Transform playerTransform, Vector2 halfExtent) {
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;

        if (!mainCamera.TryGetComponent(out CameraFollow cameraFollow)) {
            cameraFollow = mainCamera.gameObject.AddComponent<CameraFollow>();
        }

        cameraFollow.Init(playerTransform, -halfExtent, halfExtent);
    }
}
