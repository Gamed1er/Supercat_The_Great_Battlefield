using System.Collections.Generic;
using UnityEngine;

// 出怪池的一筆資料:要生成的敵人 prefab,以及它的出生位置
[System.Serializable]
public class EnemySpawnEntry {
    public GameObject enemyPrefab;
    public Vector2 spawnPosition;
}

// 一個關卡的靜態設定:出怪池、出生點、地圖大小、地板範圍、BGM、背景。
// 由 LevelManager 在進場時讀取並動態組出整個關卡,難度則不在這裡設定(由玩家在選關時另外選擇,見 LevelManager)。
[CreateAssetMenu(fileName = "LevelData", menuName = "Level/LevelData")]
public class LevelData : ScriptableObject {
    [Header("顯示 (怪池血條名稱)")]
    public string levelName = "關卡";

    [Header("地圖大小 (倍率, 基準為 LevelManager.BaseHalfWidth/BaseHalfHeight)")]
    public float mapWidthMultiplier = 1f;
    public float mapHeightMultiplier = 1f;

    [Header("地板範圍 (地圖最下面 n% 視為地面,給地面系敵人遊走用)")]
    [Range(0f, 1f)] public float groundHeightPercent = 0.4f;

    [Header("出生點")]
    public Vector2 playerSpawnPosition = Vector2.zero;

    [Header("出怪池 (一次全部生成)")]
    public List<EnemySpawnEntry> enemyPool = new List<EnemySpawnEntry>();

    [Header("音樂")]
    public string bgmName = "normal_battle";

    [Header("背景 (貼圖 prefab,每張對應一份地圖倍率單位大小)")]
    public GameObject backgroundTilePrefab;
}
