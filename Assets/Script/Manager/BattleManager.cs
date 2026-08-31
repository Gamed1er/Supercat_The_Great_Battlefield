using UnityEngine;

// 戰鬥場景(Battle.unity)的進場控制:播放對應 BGM,並套用狗仔的難度數值
public class BattleManager : MonoBehaviour {
    [SerializeField] string battleBgmName = "normal_battle";
    [SerializeField] DogeDifficulty dogeDifficulty = DogeDifficulty.Normal;

    void Awake() {
        // 必須在 Awake 套用,搶在 DogeEnemy.Start() 抓 maxHealth/設定初始攻擊週期之前完成
        // (所有物件的 Awake 一定會先跑完,才會進到任何一個物件的 Start)
        DogeEnemy doge = FindObjectOfType<DogeEnemy>();
        if (doge != null) doge.ApplyDifficulty(dogeDifficulty);
    }

    void Start() {
        AudioManager.Instance.PlayBGM(battleBgmName);
    }
}
