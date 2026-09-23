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
    public float bestClearTimeSeconds = -1f;         // 0.01 秒精度,-1 代表尚未有紀錄;只有更快才覆寫
    public int highestDifficultyCleared = -1;        // 單調取最大值(0~EnemyBase.MaxDifficulty),-1 代表尚未通關過
    public bool hasAchievedNoDamageMaxDifficulty;    // 單調旗標:是否曾在最高難度無傷通關過;純存檔欄位,尚未接成就系統/UI

    public RivalProgress(string levelId) {
        this.levelId = levelId;
    }
}

public enum RivalBattleResult { None, Win, Lose }
