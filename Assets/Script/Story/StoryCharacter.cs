using System;
using System.Collections.Generic;
using UnityEngine;

// 劇情角色定義:顯示名稱 + 各種狀態(表情)對應的立繪。
// 資產放在 Resources/StoryCharacters/{角色ID}.asset,檔名就是劇情腳本裡寫的角色 ID
// (例如 #enter left SuperCat idle → Resources/StoryCharacters/SuperCat.asset 的 "idle" 狀態),
// 用 Load(id) 依名稱載入,腳本作者不需要碰 Inspector。
[CreateAssetMenu(fileName = "NewStoryCharacter", menuName = "Story/Story Character")]
public class StoryCharacter : ScriptableObject {
    const string ResourcesFolder = "StoryCharacters";

    [Serializable]
    public class Expression {
        public string name;   // 腳本裡用的狀態名稱,例如 idle / angry
        public Sprite sprite;
    }

    public string displayName; // 對話框顯示的角色名稱
    public List<Expression> expressions = new List<Expression>();

    // 找不到狀態時回傳 null(並警告),呼叫端照常顯示沒有圖的立繪,不讓一行寫錯的腳本卡死整段劇情
    public Sprite GetSprite(string expressionName) {
        foreach (Expression expression in expressions) {
            if (expression.name == expressionName) return expression.sprite;
        }
        Debug.LogWarning($"劇情角色 {name} 沒有狀態「{expressionName}」");
        return null;
    }

    // 角色 ID 寫錯/資產還沒建是腳本內容的問題,不是 Inspector 接線錯誤,所以警告後回傳 null 讓呼叫端優雅降級
    public static StoryCharacter Load(string characterId) {
        if (string.IsNullOrEmpty(characterId)) return null;
        StoryCharacter character = Resources.Load<StoryCharacter>($"{ResourcesFolder}/{characterId}");
        if (character == null) Debug.LogWarning($"找不到劇情角色:Resources/{ResourcesFolder}/{characterId}");
        return character;
    }
}
