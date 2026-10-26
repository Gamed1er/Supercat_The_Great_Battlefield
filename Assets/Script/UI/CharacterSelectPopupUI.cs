using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 選關畫面的角色選擇彈窗:左邊角色清單,右邊顯示選中角色的頭像/名字/三個技能說明(都讀角色 prefab 上 PlayerBase 的欄位)。
// 點清單就直接選定;選定的角色全域共用,用 prefab 名稱存進 PlayerPrefs,重開遊戲也會還原(沒有存檔時預設 SuperCat)
public class CharacterSelectPopupUI : MonoBehaviour {
    const string PrefKeySelectedCharacter = "Lobby_SelectedCharacter";
    const string DefaultCharacterName = "SuperCat";

    [Serializable]
    public class CharacterEntry {
        public PlayerBase character; // 角色 prefab
        public Button button;
        public GameObject selectedFrame;
    }

    [SerializeField] GameObject panelRoot;
    [SerializeField] List<CharacterEntry> entries = new List<CharacterEntry>();
    [SerializeField] Image portraitImage;
    [SerializeField] Text nameText;
    [SerializeField] Image normalAttackIconImage;
    [SerializeField] Text normalAttackText;
    [SerializeField] Image s1IconImage;
    [SerializeField] Text s1Text;
    [SerializeField] Image s2IconImage;
    [SerializeField] Text s2Text;
    [SerializeField] Button closeButton;

    public event Action<PlayerBase> Selected;

    public PlayerBase Current { get; private set; }
    public bool IsOpen => panelRoot.activeSelf;

    void Awake() {
        for (int i = 0; i < entries.Count; i++) {
            int index = i;
            entries[i].button.onClick.AddListener(() => { AudioManager.Instance.PlaySFX("button"); Select(index); });
        }
        closeButton.onClick.AddListener(() => { AudioManager.Instance.PlaySFX("button"); Close(); });

        string savedName = PlayerPrefs.GetString(PrefKeySelectedCharacter, DefaultCharacterName);
        int selectedIndex = entries.FindIndex(entry => entry.character.name == savedName);
        if (selectedIndex < 0) selectedIndex = entries.FindIndex(entry => entry.character.name == DefaultCharacterName);
        Select(Mathf.Max(0, selectedIndex));
        panelRoot.SetActive(false);
    }

    public void Open() => panelRoot.SetActive(true);
    public void Close() => panelRoot.SetActive(false);

    void Select(int index) {
        Current = entries[index].character;
        PlayerPrefs.SetString(PrefKeySelectedCharacter, Current.name);
        PlayerPrefs.Save();

        for (int i = 0; i < entries.Count; i++) entries[i].selectedFrame.SetActive(i == index);

        portraitImage.sprite = Current.Portrait;
        nameText.text = Current.DisplayName;
        SetSkill(normalAttackIconImage, normalAttackText, Current.NormalAttackIcon, Current.NormalAttackDescription);
        SetSkill(s1IconImage, s1Text, Current.S1_Icon, Current.S1_Description);
        SetSkill(s2IconImage, s2Text, Current.S2_Icon, Current.S2_Description);

        Selected?.Invoke(Current);
    }

    // 技能圖示是角色內容,可以還沒做(例如普攻目前都沒有圖示):沒指定就把圖示格藏起來,不要顯示一塊白方塊
    static void SetSkill(Image iconImage, Text descriptionText, Sprite icon, string description) {
        iconImage.sprite = icon;
        iconImage.enabled = icon != null;
        descriptionText.text = description;
    }
}
