using UnityEngine;
using UnityEngine.UI;

// 一排難度星星(難度 0~MaxDifficulty 各一顆),選關地圖的節點和詳細面板共用。
// 星星圖目前是佔位圖,換正式美術時在 Inspector 換 litSprite/unlitSprite(顏色改成白色即可顯示原圖色)
public class StarRowUI : MonoBehaviour {
    [SerializeField] Image[] stars; // 索引 = 難度
    [SerializeField] Sprite litSprite;
    [SerializeField] Sprite unlitSprite;
    [SerializeField] Color litColor = new Color(1f, 0.82f, 0.2f);
    [SerializeField] Color unlitColor = new Color(0.25f, 0.25f, 0.28f);

    public void SetLitCount(int count) {
        for (int i = 0; i < stars.Length; i++) {
            bool lit = i < count;
            stars[i].sprite = lit ? litSprite : unlitSprite;
            stars[i].color = lit ? litColor : unlitColor;
        }
    }
}
