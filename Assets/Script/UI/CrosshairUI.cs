using UnityEngine;
using UnityEngine.UI;

// 戰鬥用準心:取代系統滑鼠游標,跟著滑鼠位置走,但整個準心圖不會超出畫面。
// 只在「戰鬥進行中」顯示——暫停(Time.timeScale == 0,包含 Esc 設定面板和對手台詞播放)、玩家死亡、關卡通關後都隱藏並還原系統游標,
// 讓結算畫面/設定面板的按鈕能正常用滑鼠點。
// 自己在 Awake 生一個最上層的 Screen Space Overlay Canvas,不依賴場景裡既有的 UI 階層,場景只需要放這個元件並指定 sprite
public class CrosshairUI : MonoBehaviour {
    [SerializeField] Sprite crosshairSprite;
    [SerializeField] float size = 48f;              // 以 1080p 高度為基準的像素大小,實際大小隨螢幕高度等比縮放
    [SerializeField] int sortingOrder = 1000;       // 蓋在所有其他 UI 上面

    RectTransform crosshairRect;
    GameObject crosshairObj;
    PlayerBase player;

    void Awake() {
        GameObject canvasObj = new GameObject("CrosshairCanvas", typeof(Canvas));
        canvasObj.transform.SetParent(transform, false);
        Canvas canvas = canvasObj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        crosshairObj = new GameObject("Crosshair", typeof(RectTransform), typeof(Image));
        crosshairObj.transform.SetParent(canvasObj.transform, false);
        Image image = crosshairObj.GetComponent<Image>();
        image.sprite = crosshairSprite;
        image.preserveAspect = true;
        image.raycastTarget = false; // 準心永遠在滑鼠底下,不能擋住 UI 點擊

        crosshairRect = (RectTransform)crosshairObj.transform;
        crosshairRect.anchorMin = crosshairRect.anchorMax = Vector2.zero; // 錨在左下角,anchoredPosition 就直接等於螢幕像素座標
        crosshairRect.pivot = new Vector2(0.5f, 0.5f);
    }

    // 跟 UIManager 一樣在 Start 找玩家:玩家是 LevelManager.Awake 裡生出來的
    void Start() {
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<PlayerBase>();
    }

    void Update() {
        bool show = Time.timeScale > 0f && !player.IsDead && !LevelManager.Instance.LevelCleared;
        SetVisible(show);
        if (!show) return;

        float pixelSize = size * Screen.height / 1080f;
        crosshairRect.sizeDelta = new Vector2(pixelSize, pixelSize);

        // 夾在 [半徑, 螢幕邊 - 半徑] 之間,讓整個準心圖都留在畫面內
        float half = pixelSize * 0.5f;
        Vector2 mouse = Input.mousePosition;
        crosshairRect.anchoredPosition = new Vector2(
            Mathf.Clamp(mouse.x, half, Screen.width - half),
            Mathf.Clamp(mouse.y, half, Screen.height - half));
    }

    void SetVisible(bool visible) {
        if (crosshairObj.activeSelf != visible) crosshairObj.SetActive(visible);
        Cursor.visible = !visible;
    }

    // 離開戰鬥場景(回大廳/重載)時一定要把系統游標還回來
    void OnDisable() {
        Cursor.visible = true;
    }
}
