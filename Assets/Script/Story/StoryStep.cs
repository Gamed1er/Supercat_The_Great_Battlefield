// 劇情播放的最小單位,由 StoryScriptParser 從文字腳本解析出來,StoryManager 依序執行。
public enum StorySide { Left, Right, None }

public enum StoryStepType { Background, Bgm, Enter, Exit, Say, Middle, Wait }

public struct StoryStep {
    public StoryStepType Type;
    public StorySide Side;        // Enter/Exit/Say 用,標示左邊/右邊(旁白時為 None)
    public string CharacterId;    // Enter 用,同時是顯示名稱與 Resources/Images/Characters/{CharacterId} 資料夾名稱
    public string Expression;     // Enter 用,對應 Resources/Images/Characters/{CharacterId}/{Expression}
    public string Text;           // Say/Middle 用,要顯示的文字
    public string BackgroundName; // Background 用,對應 Resources/Images/BackGround/{BackgroundName}
    public string AudioClipName;  // Bgm 用,對應 Resources/Audio/BGM/{AudioClipName}(即 AudioManager.PlayBGM 的 clipName)
    public float Duration;        // Wait 用,單位秒
}
