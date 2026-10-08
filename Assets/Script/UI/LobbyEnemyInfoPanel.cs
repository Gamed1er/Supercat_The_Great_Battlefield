using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// 測試大廳專用:依目前選的難度,列出每個關卡出怪池裡敵人的血量/基本攻擊力/技能,方便調數值時對照。
// 血量/攻擊力直接讀 prefab 上的難度 0 數值 + 倍率,套 EnemyBase.ScaleForDifficulty 算出(跟實際進場時同一條公式,改 prefab 會自動跟上);
// 技能說明與傷害倍率則是照各敵人腳本/prefab 目前的設定寫死在下面的表裡——正式版不會用到這個面板,改了敵人設計記得手動同步。
public class LobbyEnemyInfoPanel : MonoBehaviour {
    [SerializeField] Text infoText;

    // damagePercent:傷害 = 基本攻擊力 × 這個倍率;null 表示不是傷害技能(被動/機制說明)
    struct SkillInfo {
        public readonly string Name;
        public readonly float? DamagePercent;
        public readonly string Description;

        public SkillInfo(string name, float? damagePercent, string description) {
            Name = name;
            DamagePercent = damagePercent;
            Description = description;
        }
    }

    // key = 敵人 prefab 名稱
    static readonly Dictionary<string, SkillInfo[]> skillsByPrefabName = new Dictionary<string, SkillInfo[]> {
        ["1_Doge"] = new[] {
            new SkillInfo("接觸傷害", 1f, "跳躍撲擊/貼地咬/遊走時碰到玩家都算,附帶小幅水平擊退"),
            new SkillInfo("跳躍撲擊", null, "朝玩家跳躍落下,傷害走接觸傷害"),
            new SkillInfo("貼地咬", null, "玩家躲在低處、跳躍打不到時改用水平追咬,傷害走接觸傷害"),
            new SkillInfo("大狗叫", 0.5f, "[二階段:血量 ≤ 65%] 到場地底部中央發射多輪扇形彈幕,全程不可打斷/暈眩"),
        },
        ["2_PhotoCat"] = new[] {
            new SkillInfo("拍照", 0.5f, "跳到半空朝場地另一側發射 5 顆扇形子彈,命中暈眩 0.5 秒"),
            new SkillInfo("閃光燈", 0.5f, "原地旋轉後對半徑 3 內造成傷害 + 暈眩 0.5 秒"),
            new SkillInfo("被動", null, "與侏儸貓共用一條血量;正面/背面受擊免傷不同,被暈眩時免傷失效;攻擊會爆擊(2 倍)"),
        },
        ["2_JurassicCat"] = new[] {
            new SkillInfo("槌擊", 1f, "位於玩家正下方時垂直跳起攻擊"),
            new SkillInfo("泰山隕石墜", 2.5f, "離場蓄力,警告區域追蹤玩家 5 秒後砸落"),
            new SkillInfo("被動", null, "與攝影貓共用一條血量;固定 66% 免傷,被暈眩時失效;攝影貓打中玩家會清空槌擊冷卻;攻擊會爆擊(2 倍)"),
        },
        ["3_CatHero"] = new[] {
            new SkillInfo("光彈", 0.45f, "朝玩家連續射擊"),
            new SkillInfo("墜地拳", 2f, "從上方砸落;砸到飛碟會引爆"),
            new SkillInfo("飛碟爆炸", 1f, "墜地拳引爆飛碟:範圍傷害 + 擊退,並放出放射狀滾石"),
            new SkillInfo("召喚物", 1f, "滾石/飛碟碰到玩家的傷害(不算進怪池血條)"),
            new SkillInfo("超級霰彈", 0.45f, "[二階段:血量 ≤ 60%] 飛到中央獲得護盾(血量上限 20%),隨機方向連射到護盾被打破;破盾後自身暈眩 5 秒"),
        },
    };

    // 這兩隻的攻擊會擲爆擊(見 EnemyCritRoll)
    static readonly HashSet<string> critEnemies = new HashSet<string> { "2_PhotoCat", "2_JurassicCat" };

    public void Refresh(IEnumerable<LevelData> levels, int difficulty) {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"<size=36><b>敵人資訊(難度 {difficulty})</b></size>");

        foreach (LevelData level in levels) {
            sb.AppendLine();
            sb.AppendLine($"<size=32><b>【{level.levelId} {level.levelName}】</b></size>");
            foreach (EnemySpawnEntry entry in level.enemyPool) AppendEnemy(sb, entry.enemyPrefab.GetComponent<EnemyBase>(), difficulty);
        }

        infoText.text = sb.ToString();
    }

    static void AppendEnemy(StringBuilder sb, EnemyBase enemy, int difficulty) {
        string prefabName = enemy.gameObject.name;
        float health = EnemyBase.ScaleForDifficulty(enemy.health, enemy.healthMultiplier, difficulty);
        float attack = EnemyBase.ScaleForDifficulty(enemy.baseAttack, enemy.attackMultiplier, difficulty);

        sb.AppendLine($"<color=#FFD966><b>{prefabName}</b></color>  血量 {health:0}  攻擊力 {attack:0.#}");
        if (critEnemies.Contains(prefabName)) sb.AppendLine($"  爆擊率 {EnemyCritRoll.ChanceAt(difficulty) * 100f:0}%");

        if (!skillsByPrefabName.TryGetValue(prefabName, out SkillInfo[] skills)) {
            sb.AppendLine("  (技能說明尚未填寫)");
            return;
        }

        foreach (SkillInfo skill in skills) {
            string damage = skill.DamagePercent.HasValue ? $" <color=#FF8080>{attack * skill.DamagePercent.Value:0.#}</color>({skill.DamagePercent.Value * 100f:0}%)" : "";
            sb.AppendLine($"  • <b>{skill.Name}</b>{damage}:{skill.Description}");
        }
    }
}
