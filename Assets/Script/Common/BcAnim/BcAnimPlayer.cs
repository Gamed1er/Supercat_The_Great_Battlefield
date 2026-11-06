using UnityEngine;

// 播放 BcAnimSet 的元件：每個 BCU 影格（30 fps）用 BcAnimInstance 算出每個零件的狀態，
// 再寫進「扁平排列」的 SpriteRenderer——每個零件一個，全部是本物件的直接子物件，不建 Transform 階層。
// 原因：BCU 的縮放是沿 parent 鏈逐元件相乘、不進變換矩陣，Unity 巢狀 Transform 在「非等比縮放 + 旋轉」
// 時會產生歪斜，對不上。改成每幀直接算出每張圖的最終位置/旋轉/縮放，就能跟 BCU 一模一樣。
//
// 子物件與 partRenderers 由匯入器建好並接線；第 i 個對應 mamodel 第 i 個零件。
// 畫面左右翻轉請翻轉本物件（或其父物件）的 localScale.x，不要動子物件。
// 尚未實作：glow（加法/乘法等混合模式）與 extend（平鋪延伸，修改類型 50~52），遇到時會照一般方式畫一張。
[ExecuteAlways]
[DisallowMultipleComponent]
public class BcAnimPlayer : MonoBehaviour
{
    public const float FramesPerSecond = 30f;
    // 單次 Update 最多補幾幀，避免卡頓後一口氣跑太多
    private const int MaxStepsPerUpdate = 30;

    [SerializeField] private BcAnimSet animSet;
    [SerializeField] private SpriteRenderer[] partRenderers;
    [Tooltip("一開始播放的動畫（BC 慣例：00 走路、01 待機、02 攻擊、03 被擊退）")]
    [SerializeField] private int defaultClipIndex;
    [SerializeField] private bool loopDefaultClip = true;
    [SerializeField] private float speed = 1f;
    [Tooltip("原圖面向左邊（BC 的我方貓）就勾選；BC 的敵方角色原圖面向右邊")]
    [SerializeField] private bool artFacesLeft = true;

    private BcAnimInstance instance;
    private BcModel instanceModel;
    private Vector2[] imageSizes;
    private float alphaMultiplier = 1f;
    private float playbackRate = 1f;
    private int holdFrame = -1;
    private int clipIndex = -1;
    private bool looping;
    private float frameAccumulator;

    public BcAnimSet AnimSet => animSet;
    public int CurrentClipIndex => clipIndex;
    public string CurrentClipName => clipIndex >= 0 ? animSet.clips[clipIndex].name : null;
    // 目前的 BCU 影格（從 0 起算）
    public int CurrentFrame => instance == null ? 0 : (int)instance.f;
    // 整段的最後一幀（EAnimD.done 的判定點）
    public int ClipMaxFrame => instance?.Clip == null ? 0 : instance.Clip.max;
    // 非循環播放時，播到最後一幀之後為 true
    public bool IsDone => instance != null && instance.Done;

    public float Speed
    {
        get => speed;
        set => speed = value;
    }

    // 這一次播放的速率倍率（跟 Speed 相乘）。Speed 留給暈眩停格這類外部控制，
    // 這個給「讓動畫配合位移長度伸縮」用。每次 Play 都會重設回 1。
    public float PlaybackRate
    {
        get => playbackRate;
        set => playbackRate = value;
    }

    // 播到這一幀就停住不再前進，設成 -1 解除。每次 Play 都會重設回 -1。
    public int HoldFrame
    {
        get => holdFrame;
        set => holdFrame = value;
    }

    // 整體透明度，乘在每個零件自己的透明度上（例如死亡淡出）。
    public float Alpha
    {
        get => alphaMultiplier;
        set
        {
            alphaMultiplier = value;
            if (instance != null) ApplyPose();
        }
    }

    // 轉向：以父物件的原點（敵人本體中心）為軸鏡像，localScale.x 和 localPosition.x 一起翻。
    // 只翻 scale 的話會以本物件自己的原點翻轉，為了對齊碰撞圈而加的位置偏移不會跟著翻到另一側，轉向時整隻會橫移。
    // Prefab 裡擺的位置/大小視為「原圖朝向」（scale.x 為正）的版本，大小和偏移量都會保留。
    public void SetFacing(bool faceRight)
    {
        var s = transform.localScale;
        var p = transform.localPosition;
        float naturalX = s.x >= 0f ? p.x : -p.x;
        bool mirrored = faceRight == artFacesLeft;
        s.x = mirrored ? -Mathf.Abs(s.x) : Mathf.Abs(s.x);
        p.x = mirrored ? -naturalX : naturalX;
        transform.localScale = s;
        transform.localPosition = p;
    }

    // 「原圖朝向」下的位置偏移（跟 Prefab 裡填的值同一套座標）。設定時會依目前朝向自動鏡像 x，
    // 用來在特定招式中暫時改變對齊（例如原版動畫本身會往前位移，導致圖跟碰撞圈錯開）。
    public Vector2 NaturalLocalOffset
    {
        get
        {
            var p = transform.localPosition;
            return new Vector2(transform.localScale.x >= 0f ? p.x : -p.x, p.y);
        }
        set
        {
            var p = transform.localPosition;
            p.x = transform.localScale.x >= 0f ? value.x : -value.x;
            p.y = value.y;
            transform.localPosition = p;
        }
    }

    // 目前畫面上所有零件合起來的世界座標範圍（取代單張 SpriteRenderer.bounds）。
    // 會隨動畫姿勢變動，要拿來當固定身形（例如腳底高度）的話，請在站立姿勢時量一次存起來。
    public Bounds GetBounds()
    {
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        bool any = false;
        foreach (var sr in partRenderers)
        {
            if (!sr.enabled || sr.sprite == null) continue;
            if (any) bounds.Encapsulate(sr.bounds);
            else { bounds = sr.bounds; any = true; }
        }
        return bounds;
    }

    // 目前播放位置（含未滿一幀的小數部分），給要跟動畫同步的平滑移動用。
    public float PlaybackFrame => instance == null ? 0f : Mathf.Max(instance.f, 0f) + frameAccumulator;

    // 已經在播這段就不重播（給「每幀決定要播什麼」的循環動畫用，例如走路/待機）。
    public void PlayIfNotPlaying(string clipName, bool loop = true)
    {
        if (CurrentClipName == clipName) return;
        Play(clipName, loop);
    }

    private void OnEnable()
    {
        // Edit mode 下剛加上元件、還沒接資料時不報錯；Play mode 沒接 animSet 就讓它直接丟例外。
        if (!Application.isPlaying && animSet == null)
            return;
        ResetToDefault();
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;
        Tick(Time.deltaTime);
    }

    // 回到預設動畫的第 0 幀。匯入器用它把初始姿勢烤進 Prefab，Edit mode 下 OnEnable 也會呼叫，
    // 兩邊算出來的值相同，所以場景裡的實例不會出現一堆 Prefab override。
    public void ResetToDefault()
    {
        Play(defaultClipIndex, loopDefaultClip);
    }

    public void Play(string clipName, bool loop = true)
    {
        int index = animSet.FindClip(clipName);
        if (index < 0)
        {
            Debug.LogError($"[BcAnimPlayer] {name}：找不到動畫 {clipName}", this);
            return;
        }
        Play(index, loop);
    }

    // 從第 0 幀開始播放（就算是同一段也會重播）。
    public void Play(int index, bool loop = true)
    {
        EnsureInstance();
        clipIndex = index;
        looping = loop;
        frameAccumulator = 0f;
        playbackRate = 1f;
        holdFrame = -1;
        instance.ChangeAnim(animSet.GetClip(index));
        instance.Step();
        ApplyPose();
    }

    // 依經過的時間推進影格。Edit mode 預覽也走這裡。
    public void Tick(float deltaTime)
    {
        if (clipIndex < 0)
            return;
        frameAccumulator += deltaTime * FramesPerSecond * speed * playbackRate;
        int steps = 0;
        while (frameAccumulator >= 1f && steps < MaxStepsPerUpdate)
        {
            frameAccumulator -= 1f;
            StepFrame();
            steps++;
        }
        if (frameAccumulator >= 1f || IsHolding)
            frameAccumulator = 0f;
        if (steps > 0)
            ApplyPose();
    }

    // 直接跳到某一幀。從第 0 幀逐幀推進，因為 BCU 有些狀態（例如 id 只在剛好命中關鍵影格時設定）
    // 是路徑相依的，直接算目標幀可能跟實際播放結果不同。
    public void Seek(int clip, int frame)
    {
        PlayFrom(clip, frame, looping);
    }

    // 從指定影格開始播放（例如只播一段動畫的後半）。
    public void PlayFrom(string clipName, int frame, bool loop = false)
    {
        int index = animSet.FindClip(clipName);
        if (index < 0)
        {
            Debug.LogError($"[BcAnimPlayer] {name}：找不到動畫 {clipName}", this);
            return;
        }
        PlayFrom(index, frame, loop);
    }

    public void PlayFrom(int clip, int frame, bool loop = false)
    {
        Play(clip, loop);
        for (int i = 0; i < frame; i++)
            instance.Step();
        ApplyPose();
    }

    private bool IsHolding => holdFrame >= 0 && instance.f >= holdFrame;

    private void StepFrame()
    {
        if (IsHolding)
            return;
        if (looping && instance.Done)
            instance.ChangeAnim(instance.Clip);
        instance.Step();
    }

    private void EnsureInstance()
    {
        var model = animSet.GetModel();
        if (instance != null && instanceModel == model)
            return;
        if (partRenderers.Length != model.N)
            Debug.LogError($"[BcAnimPlayer] {name}：partRenderers 有 {partRenderers.Length} 個，但 mamodel 有 {model.N} 個零件，請重新匯入", this);
        instance = new BcAnimInstance(model);
        instanceModel = model;
        imageSizes = null;
    }

    // 把 BCU 的計算結果寫進 SpriteRenderer。
    // BCU 的像素座標（y 朝下）→ Unity 局部座標（y 朝上，單位 = 像素 / pixelsPerUnit）。
    private void ApplyPose()
    {
        var sprites = animSet.sprites;
        if (imageSizes == null || imageSizes.Length != sprites.Length)
        {
            imageSizes = new Vector2[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
                imageSizes[i] = sprites[i] == null ? Vector2.zero : sprites[i].rect.size;
        }
        float k = animSet.pixelsPerUnit;
        var order = instance.order;

        for (int rank = 0; rank < order.Length; rank++)
        {
            var part = order[rank];
            var sr = partRenderers[part.Ind];
            sr.sortingOrder = rank;

            if (!part.TryGetDraw(1f, imageSizes, out var m, out var rectPos, out var rectSize, out float alpha)
                || sprites[part.Img] == null)
            {
                sr.enabled = false;
                continue;
            }

            var sprite = sprites[part.Img];
            sr.enabled = true;
            if (sr.sprite != sprite)
                sr.sprite = sprite;
            sr.color = new Color(1f, 1f, 1f, alpha * alphaMultiplier);

            // Sprite 的 pivot 在左上角：圖上比例 (a, b)（b 朝下）在 Sprite 局部座標是 (a*w/ppu, -b*h/ppu)。
            // 要讓它落在 BCU 的 m * (rectPos + (a*rectSize.x, b*rectSize.y))，
            // 位置 = 左上角，線性部分的兩個軸向分別是下面的 axisX、axisY。
            Rect r = sprite.rect;
            float spritePpu = sprite.pixelsPerUnit;
            Vector2 origin = m.Apply(rectPos);
            Vector2 axisX = m.ApplyLinear(new Vector2(rectSize.x * spritePpu / r.width, 0f));
            Vector2 axisY = m.ApplyLinear(new Vector2(0f, -rectSize.y * spritePpu / r.height));
            origin = new Vector2(origin.x / k, -origin.y / k);
            axisX = new Vector2(axisX.x / k, -axisX.y / k);
            axisY = new Vector2(axisY.x / k, -axisY.y / k);

            // 兩個軸互相垂直（BCU 的矩陣只有旋轉和翻轉），拆成「旋轉 θ + 縮放 (sx, sy)」。
            float sx = axisX.magnitude;
            float theta;
            if (sx > 1e-6f)
                theta = Mathf.Atan2(axisX.y, axisX.x);
            else if (axisY.sqrMagnitude > 1e-12f)
                theta = Mathf.Atan2(-axisY.x, axisY.y);
            else
                theta = 0f;
            float sy = Vector2.Dot(axisY, new Vector2(-Mathf.Sin(theta), Mathf.Cos(theta)));

            var tf = sr.transform;
            tf.localPosition = new Vector3(origin.x, origin.y, 0f);
            tf.localRotation = Quaternion.Euler(0f, 0f, theta * Mathf.Rad2Deg);
            tf.localScale = new Vector3(sx, sy, 1f);
        }
    }

#if UNITY_EDITOR
    // 匯入器用：接上資料與子物件。
    public void EditorSetup(BcAnimSet set, SpriteRenderer[] renderers, int defaultClip)
    {
        animSet = set;
        partRenderers = renderers;
        defaultClipIndex = defaultClip;
        instance = null;
    }
#endif
}
