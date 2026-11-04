using System;
using System.Collections.Generic;
using UnityEngine;

// BCU（Battle Cats Ultimate）動畫核心的 C# 移植版。
// 對照 Docs/BCU-java-PC/src/main/java/common/util/anim/（BCU_java_util_common）：
//   BcTrack        ← Part.java   單一 track 的關鍵影格插值
//   BcClip         ← MaAnim.java 一段動畫，負責每條 track 各自的循環
//   BcPart         ← EPart.java  零件的即時狀態與階層變換
//   BcAnimInstance ← EAnimD.java / EAnimI.java 目前影格、零件陣列、繪製順序
// 原則是逐行照搬 BCU 的計算，不做「看起來比較合理」的改寫——前一版匯入器就是用猜的格式語意
// （ease=1、縮放/透明度是否乘基礎值、循環旗標範圍）才失敗。要修行為時先回頭對照 Java 原始碼。
// 座標全部維持 BCU 的像素空間（y 朝下），換算成 Unity 座標是 BcAnimPlayer 的工作。

// java.awt 風格的 2D 仿射矩陣：x' = a*x + c*y + tx，y' = b*x + d*y + ty。
// Translate/Scale/Rotate 都是「右乘」（作用在目前的局部座標系上），跟 FakeGraphics 的語意一致。
public struct BcAffine
{
    public float a, b, c, d, tx, ty;

    public static BcAffine Identity => new BcAffine { a = 1f, d = 1f };

    public void Translate(float x, float y)
    {
        tx += a * x + c * y;
        ty += b * x + d * y;
    }

    public void Scale(float sx, float sy)
    {
        a *= sx; b *= sx;
        c *= sy; d *= sy;
    }

    public void Rotate(float radians)
    {
        float cs = Mathf.Cos(radians), sn = Mathf.Sin(radians);
        float na = a * cs + c * sn;
        float nb = b * cs + d * sn;
        float nc = -a * sn + c * cs;
        float nd = -b * sn + d * cs;
        a = na; b = nb; c = nc; d = nd;
    }

    public Vector2 Apply(Vector2 p) => new Vector2(a * p.x + c * p.y + tx, b * p.x + d * p.y + ty);
    public Vector2 ApplyLinear(Vector2 v) => new Vector2(a * v.x + c * v.y, b * v.x + d * v.y);
}

// mamodel 的執行期版本（int 陣列直接沿用 BCU 的欄位排列）。
// parts[i]：0 parent, 1 id, 2 img, 3 z, 4 x, 5 y, 6 pivX, 7 pivY, 8 scaX, 9 scaY, 10 angle, 11 opacity, 12 glow, 13（保留，恆為 0）
// ints：0 縮放單位（通常 1000）、1 角度單位（通常 3600）、2 透明度單位（通常 1000）
public sealed class BcModel
{
    public readonly int[] ints;
    public readonly int[][] parts;
    public readonly int[][] confs;
    public readonly string[] names;
    public int N => parts.Length;

    public BcModel(int[] ints, int[][] parts, int[][] confs, string[] names)
    {
        this.ints = ints;
        this.parts = parts;
        this.confs = confs;
        this.names = names;
    }
}

// Part.java：一條 track。ints：0 零件編號、1 修改類型、2 循環次數（-1 無限、1 一次、N 次）、3/4 未用。
// moves[i]：0 影格、1 數值、2 ease、3 ease 參數。
public sealed class BcTrack
{
    public readonly int[] ints;
    public readonly int[][] moves;
    public readonly int n;
    public int off, fir, max;

    public BcTrack(int[] ints, int[][] moves)
    {
        this.ints = (int[])ints.Clone();
        n = moves.Length;
        this.moves = new int[n][];
        for (int i = 0; i < n; i++)
            this.moves[i] = (int[])moves[i].Clone();
        Validate();
    }

    private void Validate()
    {
        int doff = 0;
        if (n != 0 && (moves[0][0] - off < 0 || ints[2] != 1))
            doff -= moves[0][0];
        for (int i = 0; i < n; i++)
            moves[i][0] += doff;
        off += doff;
        fir = moves.Length == 0 ? 0 : moves[0][0];
        max = n > 0 ? moves[n - 1][0] : 0;
    }

    public int GetMax()
    {
        if (ints[2] != -1)
            return ints[2] > 1 ? fir + (max - fir) * ints[2] - off : max - off;
        return max - Math.Min(off, 0);
    }

    public void EnsureLast(BcPart[] es)
    {
        if (n == 0)
            return;
        es[ints[0]].Alter(ints[1], moves[n - 1][1]);
    }

    public void Update(float frame, BcPart[] es)
    {
        for (int i = 0; i < n; i++)
        {
            if (frame == moves[i][0])
            {
                es[ints[0]].Alter(ints[1], moves[i][1]);
            }
            else if (i < n - 1 && frame > moves[i][0] && frame < moves[i + 1][0])
            {
                if (ints[1] > 1)
                {
                    int f0 = moves[i][0];
                    int v0 = moves[i][1];
                    int f1 = moves[i + 1][0];
                    int v1 = moves[i + 1][1];

                    float realFrame = frame;
                    if (f1 - f0 == 1)
                        realFrame = (int)frame;

                    float ti;
                    // ease 1 = 維持 v0 直到下一個關鍵影格（階梯）；翻轉（13/14）永遠是階梯。
                    if (moves[i][2] == 1 || ints[1] == 13 || ints[1] == 14)
                    {
                        ti = 0;
                    }
                    else if (moves[i][2] == 0)
                    {
                        ti = (realFrame - f0) / (f1 - f0);
                    }
                    else if (moves[i][2] == 2)
                    {
                        ti = (realFrame - f0) / (f1 - f0);

                        float easePower = moves[i][3] != 0 ? moves[i][3] : 1f;
                        float tiClamped = Math.Min(1f, Math.Max(0f, ti));

                        float easeFactor;
                        if (easePower >= 0)
                            easeFactor = (float)(1.0 - Math.Sqrt(1.0 - Math.Pow(tiClamped, easePower)));
                        else
                            easeFactor = (float)Math.Sqrt(1.0 - Math.Pow(1.0 - tiClamped, -easePower));

                        if (!float.IsNaN(easeFactor))
                            ti = easeFactor;
                    }
                    else if (moves[i][2] == 3)
                    {
                        es[ints[0]].Alter(ints[1], Ease3(i, realFrame));
                        break;
                    }
                    else if (moves[i][2] == 4)
                    {
                        ti = (realFrame - f0) / (f1 - f0);

                        if (moves[i][3] > 0)
                            ti = (float)(1 - Math.Cos(ti * Math.PI / 2));
                        else if (moves[i][3] < 0)
                            ti = (float)Math.Sin(ti * Math.PI / 2);
                        else
                            ti = (float)((1 - Math.Cos(ti * Math.PI)) / 2);
                    }
                    else
                    {
                        ti = 1f;
                    }

                    float vd;
                    if (ints[1] == 2)
                    {
                        if (v1 - v0 < 0)
                            vd = (int)Math.Ceiling((v1 - v0) * ti + v0);
                        else
                            vd = (int)((v1 - v0) * ti + v0);
                    }
                    else
                    {
                        vd = v0 + (int)((v1 - v0) * ti);
                    }

                    es[ints[0]].Alter(ints[1], vd);
                    break;
                }
                else if (ints[1] == 0)
                {
                    es[ints[0]].Alter(ints[1], moves[i][1]);
                }
            }
        }

        if (n > 0 && frame > moves[n - 1][0])
            EnsureLast(es);
    }

    // 連續多個 ease=3 的關鍵影格一起做拉格朗日插值。high 的找法照搬原始碼（會包含第一個非 3 的那格）。
    private int Ease3(int i, float frame)
    {
        int low = i;
        int high = i;
        for (int j = i - 1; j >= 0; j--)
        {
            if (moves[j][2] == 3)
                low = j;
            else
                break;
        }
        for (int j = i + 1; j < moves.Length; j++)
        {
            if (moves[high = j][2] != 3)
                break;
        }
        double sum = 0;
        for (int j = low; j <= high; j++)
        {
            double val = moves[j][1] * 4096.0;
            for (int k = low; k <= high; k++)
            {
                if (j != k)
                    val *= 1.0 * (frame - moves[k][0]) / (moves[j][0] - moves[k][0]);
            }
            sum += val;
        }
        return (int)(sum / 4096);
    }
}

// MaAnim.java：一段動畫。
public sealed class BcClip
{
    public readonly string name;
    public readonly BcTrack[] parts;
    public int max = 1, len = 1;

    public BcClip(string name, BcTrack[] parts)
    {
        this.name = name;
        this.parts = parts;
        Validate();
    }

    private void Validate()
    {
        foreach (var p in parts)
        {
            if (p.ints[1] != 2) continue;
            foreach (var move in p.moves)
                if (move[1] < 0) move[1] = 0;
        }

        max = 1;
        foreach (var p in parts)
            if (p.GetMax() > max) max = p.GetMax();
        len = 1;
        foreach (var p in parts)
            len = Math.Max(len, p.GetMax());
    }

    public void Update(float f, BcAnimInstance eAnim, bool rotate)
    {
        if (rotate)
            f %= max + 1;

        if (f == 0)
        {
            foreach (var e in eAnim.ent)
                e.SetValue();
        }

        foreach (var part in parts)
        {
            int loop = part.ints[2];
            int smax = part.max;
            int fir = part.fir;
            int lmax = smax - fir;

            bool prot = rotate || loop == -1;

            float frame;
            if (prot)
            {
                int mf = loop == -1 ? smax : max + 1;
                frame = mf == 0 ? 0 : (f + part.off) % mf;
            }
            else
            {
                frame = f + part.off;
            }

            if (loop > 0 && lmax != 0)
            {
                if (frame > fir + loop * lmax)
                {
                    part.EnsureLast(eAnim.ent);
                    continue;
                }
                if (frame <= fir)
                {
                }
                else if (frame < fir + loop * lmax)
                {
                    frame = fir + (frame - fir) % lmax;
                }
                else
                {
                    frame = smax;
                }
            }

            part.Update(frame, eAnim.ent);
        }

        eAnim.Sort();
    }
}

// EPart.java：零件的即時狀態。
public sealed class BcPart
{
    // CommonStatic.Config 的預設值：透明度低於 deadOpa% 不畫，高於 fullOpa% 視為完全不透明。
    private const float DeadOpa = 10f;
    private const float FullOpa = 90f;

    private readonly BcAnimInstance owner;
    private readonly BcModel model;
    private readonly int[] args;
    private readonly int ind;

    private BcPart fa, para;
    private int id, img, z, glow, hf, vf;
    private Vector2 pos, piv, sca;
    private float angle, opacity, extendX, extendY, gsca;

    public int Ind => ind;
    public int Img => img;
    public int Z => z;
    public int Glow => glow;

    public BcPart(BcAnimInstance owner, BcModel model, int ind)
    {
        this.owner = owner;
        this.model = model;
        this.ind = ind;
        args = (int[])model.parts[ind].Clone();
    }

    private BcPart[] Ent => owner.ent;

    private static bool IsParentValid(BcPart part, List<int> parents)
    {
        if (parents == null)
            parents = new List<int>();
        if (parents.Contains(part.ind))
            return false;
        parents.Add(part.ind);
        if (part.fa == null || part.fa.ind == 0)
            return true;
        return IsParentValid(part.fa, parents);
    }

    public void Alter(int m, float v)
    {
        var ent = Ent;
        if (m == 0)
        {
            if (v < ent.Length && v >= 0 && v != ind)
            {
                fa = ent[(int)v];
                if (!IsParentValid(this, null))
                    fa = ent[0];
            }
            else
            {
                fa = ent[0];
            }
        }
        else if (m == 1)
            id = (int)v;
        else if (m == 2)
            img = (int)v; // extType==1（詛咒圖示）的隨機圖序列沒有移植，角色動畫用不到
        else if (m == 3)
            z = (int)(v * ent.Length + ind);
        else if (m == 4)
            pos.x = args[4] + v;
        else if (m == 5)
            pos.y = args[5] + v;
        else if (m == 6)
            piv.x = args[6] + v;
        else if (m == 7)
            piv.y = args[7] + v;
        else if (m == 8)
        {
            sca.x = 1f * args[8] * v / model.ints[0];
            sca.y = 1f * args[9] * v / model.ints[0];
        }
        else if (m == 53)
            gsca = v;
        else if (m == 9)
            sca.x = 1f * args[8] * v / model.ints[0];
        else if (m == 10)
            sca.y = 1f * args[9] * v / model.ints[0];
        else if (m == 11)
            angle = args[10] + v;
        else if (m == 12)
            opacity = v * args[11] / model.ints[2];
        else if (m == 13)
            hf = v == 0 ? 1 : -1;
        else if (m == 14)
            vf = v == 0 ? 1 : -1;
        else if (m == 50 || m == 51)
            extendX = v; // 平鋪延伸（extend）目前沒有實作繪製，見 BcAnimPlayer
        else if (m == 52)
            extendY = v;
        else
            Debug.LogWarning($"[BcAnim] 未處理的修改類型 {m}");
    }

    public void SetValue()
    {
        var ent = Ent;
        if (args[0] >= ent.Length)
            args[0] = 0;
        fa = args[0] <= -1 ? null : ent[args[0]];
        id = args[1];
        img = args[2];
        z = args[3] * ent.Length + ind;
        pos = new Vector2(args[4], args[5]);
        piv = new Vector2(args[6], args[7]);
        sca = new Vector2(args[8], args[9]);
        angle = args[10];
        opacity = args[11];
        glow = args[12];
        gsca = model.ints[0];
        hf = vf = 1;
        extendX = extendY = 0;
    }

    // 從這個零件一路乘到 root 的累積縮放（逐元件相乘，不放進變換矩陣）。
    private Vector2 GetSize()
    {
        float mi = 1f / model.ints[0];
        float k = gsca * mi * mi;
        if (fa == null)
            return sca * k;
        return Vector2.Scale(fa.GetSize(), sca) * k;
    }

    private Vector2 GetBaseSize(bool parent)
    {
        var mp = model.parts;
        if (model.confs.Length > 0)
        {
            float mi = 1f / model.ints[0];
            if (parent)
            {
                var self = new Vector2(Math.Sign(mp[ind][8]), Math.Sign(mp[ind][9]));
                return fa != null ? Vector2.Scale(fa.GetBaseSize(true), self) : self;
            }

            int c = model.confs[0][0];
            if (c == -1)
                return new Vector2(mp[0][8] * mi, mp[0][9] * mi);
            var cs = new Vector2(mp[c][8] * mi, mp[c][9] * mi);
            if (c == ind)
                return cs;
            return Vector2.Scale(Ent[c].GetBaseSize(true), cs);
        }
        return Vector2.one;
    }

    public float Opa()
    {
        if (opacity == 0)
            return 0;
        if (fa != null)
            return fa.Opa() * opacity / model.ints[2];
        return opacity / model.ints[2];
    }

    private void Transform(ref BcAffine g, float sizer)
    {
        if (fa != null)
            fa.Transform(ref g, sizer);

        if (Ent[0] != this)
        {
            Vector2 scaledPosition = fa != null
                ? Vector2.Scale(fa.GetSize() * sizer, pos)
                : sizer * pos;
            g.Translate(scaledPosition.x, scaledPosition.y);
            g.Scale(hf, vf);
        }
        else
        {
            if (model.confs.Length > 0)
            {
                int[] data = model.confs[0];
                Vector2 p0 = GetBaseSize(false);
                p0 = new Vector2(p0.x * data[2] * sizer * hf, p0.y * data[3] * sizer * vf);
                g.Translate(-p0.x, -p0.y);
            }

            Vector2 p1 = Vector2.Scale(GetSize() * sizer, piv);
            g.Translate(p1.x * hf, p1.y * vf);
            g.Scale(hf, vf);
        }

        if (angle != 0)
            g.Rotate((float)(Math.PI * 2 * angle / model.ints[1]));
    }

    // drawPart 的前半段：算出這個零件這一幀要不要畫、畫在哪。
    // 圖片在 matrix 的局部座標系裡是一個軸對齊矩形：左上角 rectPos、大小 rectSize（BCU 像素，y 朝下）。
    // imageSizes[i] 是第 i 張 imgcut 的像素寬高；沒有圖（BCU 的 a.parts(img) == null）由呼叫端另外判斷。
    public bool TryGetDraw(float sizer, Vector2[] imageSizes, out BcAffine matrix, out Vector2 rectPos, out Vector2 rectSize, out float alpha)
    {
        matrix = default;
        rectPos = rectSize = default;
        alpha = 0;

        float opa = Opa();
        if (img < 0 || id < 0 || opa < DeadOpa * 0.01f + 1e-5f || img >= imageSizes.Length)
            return false;

        matrix = BcAffine.Identity;
        Transform(ref matrix, sizer);

        Vector2 size = GetSize();
        Vector2 tpiv = Vector2.Scale(piv, size) * sizer;
        rectPos = -tpiv;
        rectSize = Vector2.Scale(imageSizes[img], size) * sizer;

        // drawImg：低於 fullOpa 用 TRANS 模式（透明度量化成 1/256），否則完全不透明。
        alpha = opa < FullOpa * 0.01f - 1e-5f ? (int)(opa * 256) / 256f : 1f;
        return true;
    }
}

// EAnimD.java + EAnimI.java：一個正在播放的動畫實例。
public sealed class BcAnimInstance
{
    public readonly BcModel model;
    public readonly BcPart[] ent;
    public readonly BcPart[] order;

    private BcClip ma;
    public float f = -1f;

    public BcClip Clip => ma;
    // EAnimD.done()
    public bool Done => ma != null && f >= ma.max;

    public BcAnimInstance(BcModel model)
    {
        this.model = model;
        ent = new BcPart[model.N];
        for (int i = 0; i < ent.Length; i++)
            ent[i] = new BcPart(this, model, i);
        // Java 版在建構子裡逐一 setValue，那時排在後面的 parent 還是 null；
        // 不過第一次 update(0) 一定會再全部 setValue 一次，所以這裡建完再統一設定，結果相同。
        foreach (var e in ent)
            e.SetValue();
        order = (BcPart[])ent.Clone();
        Sort();
    }

    // EAnimD.changeAnim(t, skip=false)：下一次 Step 會從第 0 幀開始（並重設所有零件）。
    public void ChangeAnim(BcClip clip)
    {
        f = -1;
        ma = clip;
    }

    // EAnimD.update(false)。BCU 戰鬥中所有實體都用 rotate=false，整段不會自動循環，
    // 循環交給每條 track 自己的循環次數；要整段重播由呼叫端在 Done 之後 ChangeAnim。
    public void Step()
    {
        f++;
        ma.Update(f, this, false);
    }

    public void Sort()
    {
        Array.Sort(order, (x, y) => x.Z != y.Z ? x.Z.CompareTo(y.Z) : x.Ind.CompareTo(y.Ind));
    }
}
