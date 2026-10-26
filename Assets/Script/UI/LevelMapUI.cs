using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 一張選關地圖(主線或 Stage 各一張,見 LevelData.mapCategory):每一章一頁,底下切換章節。
// 節點/連線由 LobbySceneBuilder 依 LevelData 生成並接好,這裡只負責換頁、刷新解鎖狀態、轉發節點點擊
public class LevelMapUI : MonoBehaviour {
    [Serializable]
    public class ChapterPage {
        public int chapter;
        public GameObject root;
        public List<LevelNodeUI> nodes = new List<LevelNodeUI>(); // 依 levelId 排序
        public List<MapLinkUI> links = new List<MapLinkUI>();
    }

    [SerializeField] List<ChapterPage> pages = new List<ChapterPage>(); // 依 chapter 排序
    [SerializeField] GameObject chapterNavigation; // 只有一頁時整列隱藏
    [SerializeField] Button previousChapterButton;
    [SerializeField] Button nextChapterButton;
    [SerializeField] Text chapterLabel;

    public event Action<LevelData> LevelClicked;

    int pageIndex;

    void Awake() {
        foreach (ChapterPage page in pages) {
            foreach (LevelNodeUI node in page.nodes) {
                LevelNodeUI clicked = node;
                node.Button.onClick.AddListener(() => LevelClicked?.Invoke(clicked.Level));
            }
        }
        previousChapterButton.onClick.AddListener(() => ShowPage(pageIndex - 1));
        nextChapterButton.onClick.AddListener(() => ShowPage(pageIndex + 1));
        chapterNavigation.SetActive(pages.Count > 1);
    }

    public void Refresh() {
        foreach (ChapterPage page in pages) {
            foreach (LevelNodeUI node in page.nodes) node.Refresh();
            foreach (MapLinkUI link in page.links) link.Refresh(); // 要在節點之後:連線顏色看終點節點是否解鎖
        }
    }

    public bool Contains(LevelData level) => FindPageIndex(level) >= 0;

    // 預設選中的關卡:依章節、關卡順序找第一個「已解鎖但還沒通關」的,全部通關過就選最後一個已解鎖的;沒有任何關卡回傳 null
    public LevelData DefaultSelection() {
        LevelData lastUnlocked = null;
        foreach (ChapterPage page in pages) {
            foreach (LevelNodeUI node in page.nodes) {
                if (!node.IsUnlocked) continue;
                if (!LevelProgressRules.IsCleared(node.Level)) return node.Level;
                lastUnlocked = node.Level;
            }
        }
        return lastUnlocked;
    }

    // 選中某關:切到它所在的章節並標示外框;level 為 null 時只清掉外框
    public void Select(LevelData level) {
        int index = level != null ? FindPageIndex(level) : -1;
        ShowPage(index >= 0 ? index : pageIndex);

        foreach (ChapterPage page in pages) {
            foreach (LevelNodeUI node in page.nodes) node.SetSelected(node.Level == level);
        }
    }

    void ShowPage(int index) {
        if (pages.Count == 0) return;

        pageIndex = Mathf.Clamp(index, 0, pages.Count - 1);
        for (int i = 0; i < pages.Count; i++) pages[i].root.SetActive(i == pageIndex);

        chapterLabel.text = $"第 {pages[pageIndex].chapter} 章";
        previousChapterButton.interactable = pageIndex > 0;
        nextChapterButton.interactable = pageIndex < pages.Count - 1;
    }

    int FindPageIndex(LevelData level) => pages.FindIndex(page => page.nodes.Exists(node => node.Level == level));
}
