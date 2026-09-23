using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 對手戰績存檔的讀寫:整份存檔是單一 JSON 檔案,放在 Application.persistentDataPath(Unity 標準存檔位置,
// Editor/正式build共用,不會被 git 追蹤)。內容是「levelId -> RivalProgress」的清單——
// JsonUtility 不支援直接序列化 Dictionary,所以包成 List 再自己用 levelId 找。
// 可以直接手動打開這個檔案改數值來測試各種戰績分支(例如把 winCountTotal 改成 6、lastResult 改成 Lose),
// 不需要真的打過對應場次;Editor 每次按 Play 都會重新讀檔。
public static class RivalProgressStore {
    const string FileName = "RivalProgress.json";

    [System.Serializable]
    class SaveFile {
        public List<RivalProgress> entries = new List<RivalProgress>();
    }

    static SaveFile cache;

    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    // 回傳指定關卡的戰績記錄;第一次呼叫(存檔裡還沒有這筆)會建立一筆全新的預設值記錄,
    // 但不會立刻寫檔——寫檔只在明確呼叫 Save() 時發生。
    public static RivalProgress Get(string levelId) {
        EnsureLoaded();

        foreach (RivalProgress entry in cache.entries) {
            if (entry.levelId == levelId) return entry;
        }

        var created = new RivalProgress(levelId);
        cache.entries.Add(created);
        return created;
    }

    public static void Save() {
        EnsureLoaded();
        File.WriteAllText(FilePath, JsonUtility.ToJson(cache, true));
        Debug.Log($"對手戰績存檔已寫入:{FilePath}");
    }

    static void EnsureLoaded() {
        if (cache != null) return;

        if (File.Exists(FilePath)) cache = JsonUtility.FromJson<SaveFile>(File.ReadAllText(FilePath));
        cache ??= new SaveFile();
    }
}
