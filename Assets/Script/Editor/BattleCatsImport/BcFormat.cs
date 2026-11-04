using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BattleCatsImport
{
    // BCU 文字格式的解析，照搬 ImgCut.java / MaModel.java / MaAnim.java / Part.java 的讀法：
    // 檔頭固定先跳過兩行，接著是數量，再來每筆一行（maanim 每條 track 是「標頭行 + 影格數 + 影格行」）。
    // 只負責把整數原封不動讀進來，語意全部交給執行期的 BcAnimCore。
    public static class BcFormat
    {
        public struct Cut
        {
            public int X, Y, W, H;
            public string Name;
        }

        public static List<Cut> ParseImgcut(string path)
        {
            var qs = ReadLines(path);
            qs.Dequeue();
            qs.Dequeue();
            qs.Dequeue(); // png 檔名
            int n = int.Parse(qs.Dequeue());
            var cuts = new List<Cut>(n);
            for (int i = 0; i < n; i++)
            {
                var ss = qs.Count > 0 ? qs.Dequeue().Split(',') : new[] { "0", "0", "1", "1" };
                var v = ParseInts(ss, 4);
                cuts.Add(new Cut { X = v[0], Y = v[1], W = v[2], H = v[3], Name = ss.Length > 4 ? ss[4].Trim() : "" });
            }
            return cuts;
        }

        public static BcModelData ParseModel(string path)
        {
            var qs = ReadLines(path);
            qs.Dequeue();
            qs.Dequeue();
            int n = int.Parse(qs.Dequeue());
            var parts = new BcIntRow[n];
            var names = new string[n];
            for (int i = 0; i < n; i++)
            {
                var ss = qs.Dequeue().Split(',');
                var row = new int[14]; // 第 13 欄保留不用，恆為 0
                Array.Copy(ParseInts(ss, 13), row, 13);
                parts[i] = new BcIntRow(row);
                names[i] = ss.Length > 13 ? ss[13].Trim() : "";
            }
            var ints = ParseInts(qs.Dequeue().Split(','), 3);
            int m = int.Parse(qs.Dequeue());
            var confs = new BcIntRow[m];
            for (int i = 0; i < m; i++)
                confs[i] = new BcIntRow(ParseInts(qs.Dequeue().Split(','), 6));
            return new BcModelData { ints = ints, parts = parts, partNames = names, confs = confs };
        }

        public static BcClipData ParseAnim(string path, string clipName)
        {
            var qs = ReadLines(path);
            qs.Dequeue();
            qs.Dequeue();
            int n = int.Parse(qs.Dequeue());
            var tracks = new BcTrackData[n];
            for (int i = 0; i < n; i++)
            {
                var ss = qs.Dequeue().Split(',');
                var track = new BcTrackData
                {
                    ints = ParseInts(ss, 5),
                    name = ss.Length > 5 ? ss[5].Trim() : "",
                };
                int k = int.Parse(qs.Dequeue());
                track.moves = new BcIntRow[k];
                for (int j = 0; j < k; j++)
                    track.moves[j] = new BcIntRow(ParseInts(qs.Dequeue().Split(','), 4));
                tracks[i] = track;
            }
            return new BcClipData { name = clipName, tracks = tracks };
        }

        // MaModel.check + Part.check：把超出範圍的編號歸零、拆掉 parent 迴圈。正常資料不會有任何改動。
        public static void Sanitize(BcModelData model, IEnumerable<BcClipData> clips, int cutCount)
        {
            int n = model.parts.Length;
            foreach (var row in model.parts)
            {
                var p = row.v;
                if (p[2] >= cutCount) p[2] = 0;
                if (p[0] > n) p[0] = 0;
            }
            var temp = new int[n];
            for (int i = 0; i < n; i++) CheckLoop(model, temp, i);
            for (int i = 0; i < n; i++)
                if (temp[i] == 2) model.parts[i].v[0] = -1;

            foreach (var clip in clips)
            {
                foreach (var t in clip.tracks)
                {
                    if (t.ints[0] >= n || t.ints[0] < 0) t.ints[0] = 0;
                    if (t.ints[1] != 2) continue;
                    foreach (var move in t.moves)
                        if (move.v[1] >= cutCount || move.v[1] < 0) move.v[1] = 0;
                }
            }
        }

        private static int CheckLoop(BcModelData model, int[] temp, int p)
        {
            if (temp[p] > 0) return temp[p];
            var parts = model.parts;
            if (parts[p].v[0] == -1) return temp[p] = 1;
            temp[p] = 2;
            if (parts[p].v[0] >= parts.Length) parts[p].v[0] = 0;
            return temp[p] = CheckLoop(model, temp, parts[p].v[0]);
        }

        // BCU 用 readLine 逐行讀；這裡另外去掉 BOM、略過空行（原始檔正常不會有空行）。
        private static Queue<string> ReadLines(string path)
        {
            var lines = File.ReadAllLines(path)
                .Select(l => l.Trim().TrimStart('﻿').Trim())
                .Where(l => l.Length > 0);
            return new Queue<string>(lines);
        }

        private static int[] ParseInts(string[] ss, int count)
        {
            if (ss.Length < count)
                throw new FormatException($"欄位不足：需要 {count} 個，只有 {ss.Length} 個（{string.Join(",", ss)}）");
            var result = new int[count];
            for (int i = 0; i < count; i++)
                result[i] = int.Parse(ss[i].Trim());
            return result;
        }
    }
}
