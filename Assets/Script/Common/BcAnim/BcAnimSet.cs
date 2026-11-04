using System;
using UnityEngine;

// 一隻 BCU 角色的完整動畫資料：切好的 Sprite（imgcut）、骨架（mamodel）、各段動畫（maanim）。
// 由 Tools > Battle Cats Import 產生，數值原封不動保留 BCU 的整數格式，計算交給 BcAnimCore。
public class BcAnimSet : ScriptableObject
{
    [Tooltip("BCU 像素換算成 Unity 單位的比例（幾像素 = 1 單位）")]
    public float pixelsPerUnit = 100f;

    [Tooltip("依 imgcut 編號排列的 Sprite，pivot 一律在左上角")]
    public Sprite[] sprites;

    public BcModelData model;
    public BcClipData[] clips;

    [NonSerialized] private BcModel modelCache;
    [NonSerialized] private BcClip[] clipCache;

    public BcModel GetModel()
    {
        if (modelCache == null)
            modelCache = model.ToRuntime();
        return modelCache;
    }

    // BcClip 建好之後就不會再變動，同一個 asset 的所有實例共用一份。
    public BcClip GetClip(int index)
    {
        if (clipCache == null || clipCache.Length != clips.Length)
            clipCache = new BcClip[clips.Length];
        return clipCache[index] ??= clips[index].ToRuntime();
    }

    public int FindClip(string clipName)
    {
        for (int i = 0; i < clips.Length; i++)
            if (clips[i].name == clipName) return i;
        return -1;
    }

    // 匯入器覆寫資料後呼叫，丟掉舊的執行期快取。
    public void InvalidateCache()
    {
        modelCache = null;
        clipCache = null;
    }
}

// Unity 不能序列化 int[][]，用一層包裝。
[Serializable]
public class BcIntRow
{
    public int[] v;

    public BcIntRow(int[] v)
    {
        this.v = v;
    }
}

[Serializable]
public class BcModelData
{
    public int[] ints;
    public BcIntRow[] parts;
    public string[] partNames;
    public BcIntRow[] confs;

    public BcModel ToRuntime()
    {
        var p = new int[parts.Length][];
        for (int i = 0; i < p.Length; i++) p[i] = (int[])parts[i].v.Clone();
        var c = new int[confs.Length][];
        for (int i = 0; i < c.Length; i++) c[i] = (int[])confs[i].v.Clone();
        return new BcModel((int[])ints.Clone(), p, c, partNames);
    }
}

[Serializable]
public class BcTrackData
{
    public string name;
    public int[] ints;
    public BcIntRow[] moves;
}

[Serializable]
public class BcClipData
{
    public string name;
    public BcTrackData[] tracks;

    public BcClip ToRuntime()
    {
        var t = new BcTrack[tracks.Length];
        for (int i = 0; i < t.Length; i++)
        {
            var src = tracks[i];
            var moves = new int[src.moves.Length][];
            for (int j = 0; j < moves.Length; j++) moves[j] = src.moves[j].v;
            t[i] = new BcTrack(src.ints, moves); // BcTrack 建構子會自己複製陣列
        }
        return new BcClip(name, t);
    }
}
