using System;

// 一個關卡/對手的戰績存檔記錄,以 LevelData.levelId 為 key,見 RivalProgressStore。
// 除了 lastResult 以外都是單調的(只會往「更達成」的方向變,不會因為輸了就被清掉),
// 這是為了配合 RivalDialogueSelector 判斷「贏過但後來又輸」這類需要區分歷史與最近一次結果的台詞。
[Serializable]
public class RivalProgress {
    public string levelId;

    public int winCountTotal;                      // 不分難度累加的總勝場數
    public bool hasWonBefore;                       // 單調旗標:是否曾經贏過(任何難度)
    public RivalBattleResult lastResult = RivalBattleResult.None; // 最近一次戰鬥結果,每場戰鬥後覆寫
    // 每個難度各自的最快通關時間,索引 = 難度(0~EnemyBase.MaxDifficulty),0.01 秒精度,-1 代表這個難度還沒紀錄;只有更快才覆寫
    public float[] bestClearTimeSecondsByDifficulty;
    public int highestDifficultyCleared = -1;        // 單調取最大值(0~EnemyBase.MaxDifficulty),-1 代表尚未通關過
    public bool hasAchievedNoDamageMaxDifficulty;    // 單調旗標:是否曾在最高難度無傷通關過;純存檔欄位,尚未接成就系統/UI

    public RivalProgress(string levelId) {
        this.levelId = levelId;
        bestClearTimeSecondsByDifficulty = NewEmptyBestTimes();
    }

    static float[] NewEmptyBestTimes() {
        var times = new float[EnemyBase.MaxDifficulty + 1];
        for (int i = 0; i < times.Length; i++) times[i] = -1f;
        return times;
    }

    // JsonUtility 反序列化不會呼叫建構子(用底層機制直接配置物件、只灌欄位值),所以舊存檔或缺這個欄位的 JSON
    // 讀出來這裡會是 null 而不是建構子裡初始化的樣子——讀寫都要透過這個方法拿陣列,而不是直接碰欄位,
    // 才能保證拿到的一定是合法大小、預設值正確(-1)的陣列,對舊存檔做到位的相容而不是直接炸掉
    // (這跟 CLAUDE.md「Inspector-wired references」講的接線疏忽是兩回事,這裡是存檔格式演進的正常情況)。
    public float[] GetOrInitBestClearTimes() {
        if (bestClearTimeSecondsByDifficulty == null || bestClearTimeSecondsByDifficulty.Length != EnemyBase.MaxDifficulty + 1) {
            bestClearTimeSecondsByDifficulty = NewEmptyBestTimes();
        }
        return bestClearTimeSecondsByDifficulty;
    }
}

public enum RivalBattleResult { None, Win, Lose }
