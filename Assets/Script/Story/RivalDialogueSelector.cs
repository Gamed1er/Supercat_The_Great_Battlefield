// 純邏輯:依戰績存檔 + 這場戰鬥的難度/結果,決定要播哪一段短台詞(對應 RivalDialogueManager 用的腳本裡的
// #section id)。回傳 null 代表沒有符合的台詞,呼叫端應該跳過對話直接繼續。條件越特殊/越稀有優先權越高。
public static class RivalDialogueSelector {
    public const string ChallengeMax = "challenge_max";
    public const string ChallengeAfterLoss = "challenge_after_loss";
    public const string ChallengeAgain = "challenge_again";

    public const string VictoryMaxNoDamage = "victory_max_no_damage";
    public const string VictoryMax = "victory_max";
    public const string Victory7th = "victory_7th";
    public const string VictoryFirst = "victory_first";
    public const string VictoryAgain = "victory_again";

    public const string DefeatMax = "defeat_max";
    public const string DefeatAfterWin = "defeat_after_win";
    public const string Defeat = "defeat";

    // 戰前開場白。呼叫端要另外處理「玩家有史以來第一次進這關」的情況(不該呼叫這個方法,
    // 那次的開場改播 Story.unity 的長篇開場,見 RivalDialogueManager.Start())。
    public static string SelectPreBattle(RivalProgress progress, int difficulty) {
        if (difficulty >= EnemyBase.MaxDifficulty) return ChallengeMax;
        if (progress.lastResult == RivalBattleResult.Lose) return ChallengeAfterLoss;
        return ChallengeAgain;
    }

    // won/noDamage 是「這場戰鬥剛結束當下」的結果;progress 傳入時還沒套用這場的結果
    // (呼叫端要先用這個方法選台詞,再把結果寫回 progress,順序不能反過來,否則「初次戰勝」「第7場」會判斷錯)。
    public static string SelectPostBattle(RivalProgress progress, int difficulty, bool won, bool noDamage) {
        bool isMaxDifficulty = difficulty >= EnemyBase.MaxDifficulty;

        if (won) {
            if (isMaxDifficulty && noDamage) return VictoryMaxNoDamage;
            if (isMaxDifficulty) return VictoryMax;
            if (progress.winCountTotal + 1 == 7) return Victory7th;
            if (!progress.hasWonBefore) return VictoryFirst;
            return VictoryAgain;
        }

        if (isMaxDifficulty) return DefeatMax;
        if (progress.hasWonBefore) return DefeatAfterWin;
        return Defeat;
    }
}
