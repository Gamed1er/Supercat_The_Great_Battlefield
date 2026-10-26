using UnityEngine;
using UnityEngine.SceneManagement;

// 一場戰鬥的出戰設定:由大廳(或之後的正式選關介面)組好交給 GameFlow.StartBattle,跨場景帶到 Story/Battle。
// 建好後不可修改,避免某個場景中途改掉別的場景還要讀的值
public class BattleSelection {
    public GameObject PlayerPrefab { get; }
    public LevelData Level { get; }
    public int Difficulty { get; } // 0~EnemyBase.MaxDifficulty,同 LevelManager.difficulty 的意義

    public BattleSelection(GameObject playerPrefab, LevelData level, int difficulty) {
        PlayerPrefab = playerPrefab;
        Level = level;
        Difficulty = Mathf.Clamp(difficulty, 0, EnemyBase.MaxDifficulty);
    }
}

// 場景之間的流程切換(大廳 → 劇情 → 戰鬥 → 大廳 / 離開遊戲)集中在這裡,
// 任何介面(臨時大廳、正式大廳、結算畫面、暫停選單…)要換場景都呼叫這裡,不要各自寫 SceneManager.LoadScene。
// 用靜態類別而不是 DontDestroyOnLoad 物件:這裡只存一份出戰設定,不需要 Update/協程,也不用擔心場景裡重複放兩份
// (轉場動畫需要協程,交給 ScreenFader;轉場進行中再要求換場景會直接被忽略)
public static class GameFlow {
    public const string LobbySceneName = "Lobby";
    public const string StorySceneName = "Story";
    public const string BattleSceneName = "Battle";

    // 目前(或上一場)戰鬥的出戰設定。直接在 Editor 開 Story/Battle 場景按 Play 時是 null,
    // 這時 LevelManager/StoryManager 會退回用各自 Inspector 上的測試值
    public static BattleSelection CurrentBattle { get; private set; }

    // 出戰時所在的大廳場景路徑,打完回到這裡:從正式大廳出發回正式大廳,從 DebugLobby 出發回 DebugLobby
    static string returnScenePath;

    public static bool HasPreBattleStory(LevelData level) =>
        level.preBattleStoryScript != null && !string.IsNullOrWhiteSpace(level.preBattleStoryScript.text);

    // 出戰:關卡有戰前劇情且沒選擇跳過 → 先進 Story 場景(播完由 StoryManager 呼叫 EnterBattle);否則直接進戰鬥
    public static void StartBattle(BattleSelection selection, bool skipStory) {
        if (ScreenFader.Instance.IsTransitioning) return;
        CurrentBattle = selection;
        returnScenePath = SceneManager.GetActiveScene().path;
        LoadScene(!skipStory && HasPreBattleStory(selection.Level) ? StorySceneName : BattleSceneName);
    }

    public static void EnterBattle() => LoadScene(BattleSceneName);

    public static void ReturnToLobby() => LoadScene(string.IsNullOrEmpty(returnScenePath) ? LobbySceneName : returnScenePath);

    // 重載目前場景(例如戰鬥中改解析度後,讓 LevelManager 依新的 aspect 重算牆壁/鏡頭範圍)
    public static void ReloadCurrentScene() => LoadScene(SceneManager.GetActiveScene().path);

    public static void QuitGame() {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // 換場景一律經過 ScreenFader 的黑幕轉場,並在全黑時把 timeScale 復原:暫停選單等介面可能在 timeScale = 0 的狀態下觸發換場景
    static void LoadScene(string sceneNameOrPath) {
        ScreenFader.Instance.TryLoadScene(() => {
            Time.timeScale = 1f;
            return LoadSceneAsync(sceneNameOrPath);
        });
    }

    static AsyncOperation LoadSceneAsync(string sceneNameOrPath) {
#if UNITY_EDITOR
        // 不在 Build Settings 裡的場景(只在 Editor 用的 DebugLobby)SceneManager 載不到,改用 Editor 專用的 API 依路徑載入
        if (sceneNameOrPath.EndsWith(".unity") && SceneUtility.GetBuildIndexByScenePath(sceneNameOrPath) < 0) {
            return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                sceneNameOrPath, new LoadSceneParameters(LoadSceneMode.Single));
        }
#endif
        return SceneManager.LoadSceneAsync(sceneNameOrPath);
    }
}
