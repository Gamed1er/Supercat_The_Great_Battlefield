using System;
using System.Collections.Generic;
using UnityEngine;

// Battle 場景的對手短台詞觸發點:戰前開場白在 Start() 自己播;戰後結果由 BattleResultUI 在顯示勝負畫面
// 之前呼叫 PlayPostBattleBark()。台詞內容/挑選規則分別見 StoryScriptParser.ParseSections 與
// RivalDialogueSelector,實際播放/暫停遊戲由 RivalBarkUI 負責。
// 用 Start() 而不是 Awake():要讀 LevelManager.Instance,LevelManager 在自己的 Awake() 生成玩家/敵人,
// 不同物件的 Awake 順序不保證,但所有物件的 Awake 一定在任何 Start() 之前跑完(比照 UIManager 的作法)。
public class RivalDialogueManager : MonoBehaviour {
    [SerializeField] RivalBarkUI barkUI;

    RivalProgress progress;
    LevelData levelData;
    int difficulty;

    void Start() {
        levelData = LevelManager.Instance.LevelData;
        difficulty = LevelManager.Instance.Difficulty;
        progress = RivalProgressStore.Get(levelData.levelId);

        // 第一次進這關:長篇開場已經在 Story.unity 播過(見 StoryManager.SeenKeyPrefix),這裡不用再播短台詞
        bool hasSeenOpeningStory = PlayerPrefs.GetInt(StoryManager.SeenKeyPrefix + levelData.levelId, 0) == 1;
        if (!hasSeenOpeningStory) return;

        PlaySection(RivalDialogueSelector.SelectPreBattle(progress, difficulty), null);
    }

    // 由 BattleResultUI 在顯示勝負畫面之前呼叫;結束後(不論有沒有播到台詞)一定會呼叫 onComplete。
    // 同時把這場戰鬥的結果寫回存檔並立刻存檔(見 RivalProgressStore.Save)。
    public void PlayPostBattleBark(bool won, bool noDamageThisRun, float clearTimeSeconds, Action onComplete) {
        string sectionId = RivalDialogueSelector.SelectPostBattle(progress, difficulty, won, noDamageThisRun);

        ApplyOutcome(won, noDamageThisRun, clearTimeSeconds);
        RivalProgressStore.Save();

        PlaySection(sectionId, onComplete);
    }

    void ApplyOutcome(bool won, bool noDamageThisRun, float clearTimeSeconds) {
        if (!won) {
            progress.lastResult = RivalBattleResult.Lose;
            return;
        }

        progress.winCountTotal++;
        progress.hasWonBefore = true;
        progress.lastResult = RivalBattleResult.Win;

        if (difficulty > progress.highestDifficultyCleared) progress.highestDifficultyCleared = difficulty;

        if (progress.bestClearTimeSeconds < 0f || clearTimeSeconds < progress.bestClearTimeSeconds) {
            progress.bestClearTimeSeconds = Mathf.Round(clearTimeSeconds * 100f) / 100f; // 0.01 秒精度
        }

        if (difficulty >= EnemyBase.MaxDifficulty && noDamageThisRun && !progress.hasAchievedNoDamageMaxDifficulty) {
            progress.hasAchievedNoDamageMaxDifficulty = true;
            Debug.Log("成就解鎖:最高難度無傷通關(尚未接成就系統/UI,先記錄存檔旗標)");
        }
    }

    // rivalDialogueScript 沒接是合法情況(這關沒有對手台詞系統,見 LevelData 的欄位註解),優雅跳過;
    // 但腳本裡確實漏了某個 #section,或 barkUI 沒接線,都是設定錯誤,故意直接丟例外讓它爆出來
    // (見 CLAUDE.md「Inspector-wired references」),不要吞掉繼續往下跑。
    void PlaySection(string sectionId, Action onComplete) {
        if (levelData.rivalDialogueScript == null) { onComplete?.Invoke(); return; }

        Dictionary<string, List<StoryStep>> sections = StoryScriptParser.ParseSections(levelData.rivalDialogueScript.text);
        if (!sections.TryGetValue(sectionId, out List<StoryStep> steps)) {
            throw new Exception($"{levelData.rivalDialogueScript.name} 裡找不到 #section {sectionId}");
        }

        barkUI.Play(steps, levelData.rivalCharacterId, levelData.rivalPortraitExpression, onComplete);
    }
}
