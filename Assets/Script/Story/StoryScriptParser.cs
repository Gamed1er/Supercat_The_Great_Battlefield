using System;
using System.Collections.Generic;
using UnityEngine;

// 把純文字劇本解析成 StoryStep 序列。語法:
//   #bg <背景檔名>                       切換背景(Resources/Images/BackGround/{檔名})
//   #bgm <音樂檔名>                      切換 BGM(Resources/Audio/BGM/{檔名});跟目前正在播的同一首時不會重播
//   #enter <left|right> <角色ID> <表情>   角色從該側滑入(Resources/Images/Characters/{角色ID}/{表情})
//   #exit <left|right>                   角色從該側滑出
//   #speak <left|right|none>             之後的對話行歸屬於哪一側(影響顯示名稱與亮暗),持續到下一個 #speak
//   #middle <文字>                       顯示中間強調文字(黑幕淡入 -> 打字 -> 點擊/自動收回)
//   #wait <秒數>                         上一個動作播完後,原地等待這麼多秒才繼續下一步(不需要點擊)
//   // 開頭的行是註解,整行忽略;空白行忽略
//   其餘非空白行 = 對話文字,歸屬於目前 #speak 設定的那一側
public static class StoryScriptParser {
    public static List<StoryStep> Parse(string scriptText) {
        var steps = new List<StoryStep>();
        if (string.IsNullOrEmpty(scriptText)) return steps;

        StorySide currentSpeaker = StorySide.None;

        foreach (string rawLine in scriptText.Split('\n')) {
            string line = rawLine.Trim('\r', ' ', '\t');
            if (line.Length == 0 || line.StartsWith("//")) continue;

            if (line[0] == '#') ParseCommand(line, steps, ref currentSpeaker);
            else steps.Add(new StoryStep { Type = StoryStepType.Say, Side = currentSpeaker, Text = line });
        }

        return steps;
    }

    static void ParseCommand(string line, List<StoryStep> steps, ref StorySide currentSpeaker) {
        string[] tokens = line.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return;

        switch (tokens[0].ToLowerInvariant()) {
            case "bg":
                if (tokens.Length < 2) { Debug.LogWarning($"劇情腳本 #bg 缺少參數:{line}"); return; }
                steps.Add(new StoryStep { Type = StoryStepType.Background, BackgroundName = tokens[1] });
                break;

            case "bgm":
                if (tokens.Length < 2) { Debug.LogWarning($"劇情腳本 #bgm 缺少參數:{line}"); return; }
                steps.Add(new StoryStep { Type = StoryStepType.Bgm, AudioClipName = tokens[1] });
                break;

            case "enter":
                if (tokens.Length < 4 || !TryParseSide(tokens[1], out StorySide enterSide) || enterSide == StorySide.None) {
                    Debug.LogWarning($"劇情腳本 #enter 參數錯誤:{line}");
                    return;
                }
                steps.Add(new StoryStep { Type = StoryStepType.Enter, Side = enterSide, CharacterId = tokens[2], Expression = tokens[3] });
                break;

            case "exit":
                if (tokens.Length < 2 || !TryParseSide(tokens[1], out StorySide exitSide) || exitSide == StorySide.None) {
                    Debug.LogWarning($"劇情腳本 #exit 參數錯誤:{line}");
                    return;
                }
                steps.Add(new StoryStep { Type = StoryStepType.Exit, Side = exitSide });
                break;

            case "speak":
                if (tokens.Length < 2 || !TryParseSide(tokens[1], out StorySide speakSide)) {
                    Debug.LogWarning($"劇情腳本 #speak 參數錯誤:{line}");
                    return;
                }
                currentSpeaker = speakSide;
                break;

            case "wait":
                if (tokens.Length < 2 || !float.TryParse(tokens[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float duration)) {
                    Debug.LogWarning($"劇情腳本 #wait 參數錯誤:{line}");
                    return;
                }
                steps.Add(new StoryStep { Type = StoryStepType.Wait, Duration = duration });
                break;

            case "middle":
                // 取 "#middle " 之後的原始內容(保留內部空白),而不是用 token 重組
                int textStart = tokens[0].Length + 2; // '#' + "middle" + 一個空格
                steps.Add(new StoryStep { Type = StoryStepType.Middle, Text = line.Length > textStart ? line.Substring(textStart) : "" });
                break;

            default:
                Debug.LogWarning($"劇情腳本未知指令:{line}");
                break;
        }
    }

    static bool TryParseSide(string token, out StorySide side) {
        switch (token.ToLowerInvariant()) {
            case "left": side = StorySide.Left; return true;
            case "right": side = StorySide.Right; return true;
            case "none": side = StorySide.None; return true;
            default: side = StorySide.None; return false;
        }
    }
}
