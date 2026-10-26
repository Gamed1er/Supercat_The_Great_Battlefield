using UnityEngine;

// 選關畫面用的進度規則(解鎖、可選難度上限、星星數、劇情是否看過),全部從既有存檔推導:
// 通關紀錄讀 RivalProgressStore(每場戰鬥結束由 RivalDialogueManager 寫回),劇情已讀旗標讀 StoryManager 的 PlayerPrefs key,
// 這裡不另外存任何東西
public static class LevelProgressRules {
    // 還沒用高難度通關過時,一開始就能選到的最高難度;再往上要先用前一個難度通關這關
    public const int FirstPlayMaxDifficulty = 4;

    public static RivalProgress GetProgress(LevelData level) => RivalProgressStore.Get(level.levelId);

    public static int HighestDifficultyCleared(LevelData level) => GetProgress(level).highestDifficultyCleared;

    public static bool IsUnlocked(LevelData level) =>
        level.prerequisiteLevel == null || HighestDifficultyCleared(level.prerequisiteLevel) >= level.prerequisiteMinDifficulty;

    public static bool IsCleared(LevelData level) => HighestDifficultyCleared(level) >= 0;

    public static int MaxSelectableDifficulty(LevelData level) =>
        Mathf.Min(EnemyBase.MaxDifficulty, Mathf.Max(FirstPlayMaxDifficulty, HighestDifficultyCleared(level) + 1));

    // 星星 = 難度 0~MaxDifficulty 各一顆,用難度 d 通關點亮 d+1 顆
    public static int LitStarCount(LevelData level) => HighestDifficultyCleared(level) + 1;

    public static bool HasSeenStory(LevelData level) => PlayerPrefs.GetInt(StoryManager.SeenKeyPrefix + level.levelId, 0) == 1;
}
