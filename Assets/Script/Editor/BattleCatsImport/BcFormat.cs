using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BattleCatsImport
{
    // Battle Cats 原生美術格式的純資料解析（.imgcut / .mamodel / .maanim）。
    // 三個檔案互相靠「索引」對應，不靠檔名字串比對：
    //   imgcut 的第 N 筆（0-based）＝ mamodel 每個 part 的 CutIndex；
    //   mamodel 的第 N 個 part（0-based，含開頭的 dummy 根節點）＝ maanim 每條 track 的 PartIndex。

    internal struct BcCut
    {
        public int X, Y, W, H;
        public string Name;
    }

    internal class BcImgcut
    {
        public string PngName;
        public readonly List<BcCut> Cuts = new List<BcCut>();

        public static BcImgcut Parse(string path)
        {
            var lines = File.ReadAllLines(path);
            var result = new BcImgcut();
            // lines[0] = "[imgcut]", lines[1] = 版本(略), lines[2] = png檔名, lines[3] = 切割數量
            result.PngName = lines[2].Trim();
            int count = int.Parse(lines[3].Trim());
            for (int i = 0; i < count; i++)
            {
                var f = lines[4 + i].Split(new[] { ',' }, 5);
                result.Cuts.Add(new BcCut
                {
                    X = int.Parse(f[0]),
                    Y = int.Parse(f[1]),
                    W = int.Parse(f[2]),
                    H = int.Parse(f[3]),
                    Name = f.Length > 4 ? f[4] : string.Empty,
                });
            }
            return result;
        }
    }

    internal struct BcPart
    {
        public int ParentIndex;
        public int UnitId;
        public int CutIndex;
        public int ZOrder;
        public int X, Y;
        public int PivotX, PivotY;
        public int ScaleX, ScaleY;
        public int Angle;
        public int Opacity;
        public int Glow;
        public string Name;
    }

    internal class BcModel
    {
        public readonly List<BcPart> Parts = new List<BcPart>();

        // 檔尾 "maxScale,maxAngle,maxOpacity" —— part/track 裡的縮放、角度、透明度都是整數，
        // 要除以這三個值才是實際倍率/角度/透明度（例：maxAngle=3600 代表角度單位是 0.1 度）。
        public int MaxScale = 1000;
        public int MaxAngle = 3600;
        public int MaxOpacity = 1000;

        public static BcModel Parse(string path)
        {
            var lines = File.ReadAllLines(path);
            var result = new BcModel();
            // lines[0] = "[modelanim:model]", lines[1] = 版本(略), lines[2] = part 數量
            int count = int.Parse(lines[2].Trim());
            for (int i = 0; i < count; i++)
            {
                var f = lines[3 + i].Split(new[] { ',' }, 14);
                if (f.Length < 13) continue;
                result.Parts.Add(new BcPart
                {
                    ParentIndex = int.Parse(f[0]),
                    UnitId = int.Parse(f[1]),
                    CutIndex = int.Parse(f[2]),
                    ZOrder = int.Parse(f[3]),
                    X = int.Parse(f[4]),
                    Y = int.Parse(f[5]),
                    PivotX = int.Parse(f[6]),
                    PivotY = int.Parse(f[7]),
                    ScaleX = int.Parse(f[8]),
                    ScaleY = int.Parse(f[9]),
                    Angle = int.Parse(f[10]),
                    Opacity = int.Parse(f[11]),
                    Glow = int.Parse(f[12]),
                    Name = f.Length > 13 ? f[13] : string.Empty,
                });
            }

            int footerLine = 3 + count;
            if (footerLine < lines.Length)
            {
                var f = lines[footerLine].Split(',');
                if (f.Length >= 3 &&
                    int.TryParse(f[0], out var maxScale) &&
                    int.TryParse(f[1], out var maxAngle) &&
                    int.TryParse(f[2], out var maxOpacity))
                {
                    result.MaxScale = maxScale;
                    result.MaxAngle = maxAngle;
                    result.MaxOpacity = maxOpacity;
                }
            }

            return result;
        }
    }

    internal struct BcKeyframe
    {
        public int Frame;
        public int Value;
        public int Ease;
        public int EaseParam;
    }

    internal struct BcTrack
    {
        public int PartIndex;
        public int ModifierType;
        // 這欄一開始猜是「循環回到的影格」，但實際資料顯示同一個檔案裡所有 track 這欄都相同：
        // 會首尾銜接循環的動畫（如待機）整檔都是 -1，一次性動畫（攻擊/切姿勢）整檔都是非 -1（目前看到都是 1）。
        // 所以這其實是「整段動畫」的循環旗標，不是逐 track 的影格數：-1 = 循環播放，其餘 = 播放一次不循環。
        public int LoopFrame;
        public string Name;
        public List<BcKeyframe> Keyframes;
    }

    internal class BcAnim
    {
        public readonly List<BcTrack> Tracks = new List<BcTrack>();

        public static BcAnim Parse(string path)
        {
            var lines = File.ReadAllLines(path);
            var result = new BcAnim();
            // lines[0] = "[modelanim:animation]", lines[1] = 版本(略), lines[2] = track 數量
            int trackCount = int.Parse(lines[2].Trim());
            int cursor = 3;
            for (int t = 0; t < trackCount; t++)
            {
                var header = lines[cursor++].Split(new[] { ',' }, 6);
                var track = new BcTrack
                {
                    PartIndex = int.Parse(header[0]),
                    ModifierType = int.Parse(header[1]),
                    LoopFrame = int.Parse(header[2]),
                    Name = header.Length > 5 ? header[5] : string.Empty,
                };

                int keyCount = int.Parse(lines[cursor++].Trim());
                var keys = new List<BcKeyframe>(keyCount);
                for (int k = 0; k < keyCount; k++)
                {
                    var kf = lines[cursor++].Split(',');
                    keys.Add(new BcKeyframe
                    {
                        Frame = int.Parse(kf[0]),
                        Value = int.Parse(kf[1]),
                        Ease = kf.Length > 2 ? int.Parse(kf[2]) : 0,
                        EaseParam = kf.Length > 3 ? int.Parse(kf[3]) : 0,
                    });
                }
                track.Keyframes = keys;
                result.Tracks.Add(track);
            }
            return result;
        }
    }

    // maanim 的「修改類型」列舉（第二欄）。
    internal static class BcModifierType
    {
        public const int Parent = 0;
        public const int CutIndex = 2;
        public const int ZOrder = 3;
        public const int PosX = 4;
        public const int PosY = 5;
        public const int PivotX = 6;
        public const int PivotY = 7;
        public const int ScaleUniform = 8;
        public const int ScaleX = 9;
        public const int ScaleY = 10;
        public const int Angle = 11;
        public const int Opacity = 12;
        public const int FlipH = 13;
        public const int FlipV = 14;
    }
}
