using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleCatsImport
{
    // 把從 BCU 抽出來的 Battle Cats 原生美術（xxx.png + xxx.imgcut + xxx.mamodel + xxx0N.maanim）
    // 轉成 Unity 的零件階層 Prefab + AnimationClip。
    //
    // 用法：Tools > Battle Cats Import > Import Model From PNG...，把來源 png 拖進去，
    // 同資料夾底下的 xxx.imgcut / xxx.mamodel / xxx*.maanim 會依檔名自動找到。
    public class BcModelImporterWindow : EditorWindow
    {
        private Texture2D sourcePng;
        private float pixelsPerUnit = 100f;
        private float frameRate = 30f;
        private bool invertRotation = true;
        private string outputFolder = "Assets/Prefabs";

        [MenuItem("Tools/Battle Cats Import/Import Model From PNG...")]
        private static void Open()
        {
            GetWindow<BcModelImporterWindow>("BC Model Import");
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "拖入 BCU 解包出的 xxx.png，同資料夾需有同名的 xxx.imgcut / xxx.mamodel / xxx0N.maanim。",
                MessageType.Info);

            sourcePng = (Texture2D)EditorGUILayout.ObjectField("Source PNG", sourcePng, typeof(Texture2D), false);
            pixelsPerUnit = EditorGUILayout.FloatField("Pixels Per Unit", pixelsPerUnit);
            frameRate = EditorGUILayout.FloatField("Frame Rate", frameRate);
            invertRotation = EditorGUILayout.Toggle("Invert Rotation (Y 軸翻轉補償)", invertRotation);

            EditorGUILayout.BeginHorizontal();
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                var abs = EditorUtility.OpenFolderPanel("Output Folder", outputFolder, "");
                if (!string.IsNullOrEmpty(abs))
                {
                    if (abs.StartsWith(Application.dataPath))
                        outputFolder = "Assets" + abs.Substring(Application.dataPath.Length);
                    else
                        Debug.LogWarning("請選擇專案 Assets 資料夾內的路徑。");
                }
            }
            EditorGUILayout.EndHorizontal();

            GUI.enabled = sourcePng != null;
            if (GUILayout.Button("Import"))
            {
                try
                {
                    Import();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BcModelImporter] 匯入失敗：{e}");
                }
            }
            GUI.enabled = true;
        }

        private void Import()
        {
            string pngPath = AssetDatabase.GetAssetPath(sourcePng);
            string dir = Path.GetDirectoryName(pngPath)?.Replace('\\', '/');
            string baseName = Path.GetFileNameWithoutExtension(pngPath);
            string imgcutPath = $"{dir}/{baseName}.imgcut";
            string mamodelPath = $"{dir}/{baseName}.mamodel";

            if (!File.Exists(imgcutPath)) throw new FileNotFoundException($"找不到 {imgcutPath}");
            if (!File.Exists(mamodelPath)) throw new FileNotFoundException($"找不到 {mamodelPath}");

            var maanimPaths = Directory.GetFiles(dir, $"{baseName}*.maanim")
                .Select(p => p.Replace('\\', '/'))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();

            if (!AssetDatabase.IsValidFolder(outputFolder))
                Directory.CreateDirectory(outputFolder);

            var imgcut = BcImgcut.Parse(imgcutPath);
            var model = BcModel.Parse(mamodelPath);

            var spriteByCutIndex = SliceTexture(pngPath, imgcut);
            BuildPrefab(baseName, model, spriteByCutIndex, maanimPaths);

            Debug.Log($"[BcModelImporter] 完成：{outputFolder}/{baseName}.prefab（{model.Parts.Count} 個零件，{maanimPaths.Length} 段動畫）");
        }

        // 依 imgcut 把來源 png 切成具名子 Sprite，並回傳以 cutIndex 對應的陣列。
        // Sprite 的 pivot 固定放在左上角（(0,1)），實際的「錨點」偏移（mamodel 的 PivotX/PivotY）
        // 改由零件階層裡子物件的 local position 承擔——因為同一張 cut 圖可能被不同 part 用不同
        // pivot 參照，Unity 的 Sprite pivot 是資產層級、共用的，沒辦法表達「同一張圖、不同錨點」。
        private Sprite[] SliceTexture(string pngPath, BcImgcut imgcut)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            int texHeight = texture.height;

            var usedNames = new HashSet<string>();
#pragma warning disable 0618 // SpriteMetaData/TextureImporter.spritesheet 在 2022.3 仍可用，只是標記過時
            var metas = new List<SpriteMetaData>(imgcut.Cuts.Count);
            for (int i = 0; i < imgcut.Cuts.Count; i++)
            {
                var cut = imgcut.Cuts[i];
                string spriteName = MakeUniqueSpriteName(i, cut.Name, usedNames);
                metas.Add(new SpriteMetaData
                {
                    name = spriteName,
                    // imgcut 的 Y 是從貼圖頂端算起，Unity 的 Rect 是從底部算起，需要翻轉。
                    rect = new Rect(cut.X, texHeight - cut.Y - cut.H, cut.W, cut.H),
                    pivot = new Vector2(0f, 1f),
                    alignment = (int)SpriteAlignment.Custom,
                });
            }
            importer.spritesheet = metas.ToArray();
#pragma warning restore 0618
            importer.SaveAndReimport();

            var sprites = AssetDatabase.LoadAllAssetsAtPath(pngPath).OfType<Sprite>().ToArray();
            var result = new Sprite[imgcut.Cuts.Count];
            foreach (var sprite in sprites)
            {
                int underscoreIdx = sprite.name.IndexOf('_');
                if (underscoreIdx > 0 && int.TryParse(sprite.name.Substring(0, underscoreIdx), out var idx) && idx < result.Length)
                    result[idx] = sprite;
            }
            return result;
        }

        private static string MakeUniqueSpriteName(int index, string rawName, HashSet<string> usedNames)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(rawName.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            if (string.IsNullOrEmpty(cleaned)) cleaned = "cut";
            string name = $"{index:00}_{cleaned}";
            while (!usedNames.Add(name)) name += "_";
            return name;
        }

        private void BuildPrefab(string baseName, BcModel model, Sprite[] spriteByCutIndex, string[] maanimPaths)
        {
            var root = new GameObject(baseName);
            root.AddComponent<SortingGroup>();

            var partTransforms = new Transform[model.Parts.Count];
            var spriteChildTransforms = new Transform[model.Parts.Count];
            var rigNodes = new BcRigNode[model.Parts.Count];
            // BC 的零件名稱常常同一個 parent 底下有好幾個一樣的名字（例如殘影用的 <color=3> 變體，
            // 清掉顏色標籤後跟本體同名）。AnimationUtility 用「路徑字串」綁定動畫曲線，同名手足會
            // 讓路徑字串重複、綁到錯的節點上，所以這裡強制同一個 parent 底下的子物件名稱互不相同。
            var usedNamesByParent = new Dictionary<Transform, HashSet<string>>();

            for (int i = 0; i < model.Parts.Count; i++)
            {
                var part = model.Parts[i];
                var parentTf = part.ParentIndex >= 0 && part.ParentIndex < partTransforms.Length
                    ? partTransforms[part.ParentIndex]
                    : root.transform;

                if (!usedNamesByParent.TryGetValue(parentTf, out var usedNames))
                {
                    usedNames = new HashSet<string>();
                    usedNamesByParent[parentTf] = usedNames;
                }
                string baseName2 = CleanPartName(part.Name);
                string uniqueName = baseName2;
                int dupSuffix = 2;
                while (!usedNames.Add(uniqueName)) uniqueName = $"{baseName2}_{dupSuffix++}";

                var go = new GameObject(uniqueName);
                go.transform.SetParent(parentTf, false);

                go.transform.localPosition = new Vector3(part.X / pixelsPerUnit, -part.Y / pixelsPerUnit, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, ToDegrees(part.Angle, model.MaxAngle) * (invertRotation ? -1f : 1f));
                go.transform.localScale = new Vector3(part.ScaleX / (float)model.MaxScale, part.ScaleY / (float)model.MaxScale, 1f);
                partTransforms[i] = go.transform;

                var rigNode = go.AddComponent<BcRigNode>();
                rigNode.Alpha = part.Opacity / (float)model.MaxOpacity;
                rigNodes[i] = rigNode;

                if (part.CutIndex >= 0 && part.CutIndex < spriteByCutIndex.Length && spriteByCutIndex[part.CutIndex] != null)
                {
                    var spriteGo = new GameObject("Sprite");
                    spriteGo.transform.SetParent(go.transform, false);
                    spriteGo.transform.localPosition = new Vector3(-part.PivotX / pixelsPerUnit, part.PivotY / pixelsPerUnit, 0f);

                    var sr = spriteGo.AddComponent<SpriteRenderer>();
                    sr.sprite = spriteByCutIndex[part.CutIndex];
                    sr.sortingOrder = part.ZOrder;
                    sr.color = new Color(1f, 1f, 1f, ComputeBaseEffectiveOpacity(i, model));

                    spriteChildTransforms[i] = spriteGo.transform;
                    rigNode.SpriteRenderer = sr;
                }
            }

            Animation animComp = maanimPaths.Length > 0 ? root.AddComponent<Animation>() : null;
            AnimationClip defaultClip = null;

            foreach (var maanimPath in maanimPaths)
            {
                var anim = BcAnim.Parse(maanimPath);
                string clipName = Path.GetFileNameWithoutExtension(maanimPath);
                var clip = BuildClip(clipName, anim, model, root.transform, partTransforms, spriteChildTransforms, rigNodes, spriteByCutIndex);

                string clipAssetPath = $"{outputFolder}/{clipName}.anim";
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(clipAssetPath) != null)
                    AssetDatabase.DeleteAsset(clipAssetPath);
                AssetDatabase.CreateAsset(clip, clipAssetPath);

                animComp.AddClip(clip, clipName);
                if (defaultClip == null) defaultClip = clip;
            }

            if (animComp != null) animComp.clip = defaultClip;

            // mamodel 的原始姿勢兩套骨架 (idle/attack) 預設都是 100% 不透明，遊戲裡永遠有動畫在播、
            // 從沒人會看到這個「原始」姿勢；但存出來的 Prefab 在 Editor 沒按 Play 前就是長這樣，會看起來
            // 兩套骨架疊在一起。這裡把預設 clip（idle）的第 0 幀直接烤進階層的靜態姿勢，讓 Prefab 打開
            // 就是正確的待機姿勢，而不用等進 Play 模式才會套用動畫。
            if (defaultClip != null)
            {
                defaultClip.SampleAnimation(root, 0f);
                foreach (var node in rigNodes)
                {
                    if (node == null || node.SpriteRenderer == null) continue;
                    var c = node.SpriteRenderer.color;
                    c.a = node.EffectiveAlpha;
                    node.SpriteRenderer.color = c;
                }
            }

            // Unity 的 PrefabUtility.SaveAsPrefabAsset 對已存在的路徑不會「替換」，而是照樣寫入同路徑
            // 覆蓋資產內容——但如果路徑已被佔用又剛好是別的資產型別，或者先前用 GenerateUniqueAssetPath
            // 產生過 "xxx 1.prefab" 這種分身，會越疊越多份、animation 卻是直接複寫，導致新舊 prefab/clip
            // 對不起來要手動重新綁定。這裡固定用同一個路徑，匯入前先砍掉舊資產，確保每次匯入都是乾淨覆蓋。
            string prefabPath = $"{outputFolder}/{baseName}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                AssetDatabase.DeleteAsset(prefabPath);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static float ToDegrees(int raw, int maxAngle) => raw / (maxAngle / 360f);

        // 靜態組裝階段用的初始透明度：沿 parentIndex 一路往上把每一層的 Opacity 乘起來，
        // 讓沒有播放任何動畫的 Prefab（例如在 Scene 檢視、非 Play 模式）也能正確顯示成該顯示/該隱藏的樣子。
        // 播放時則交給 BcRigNode.LateUpdate 即時計算（因為那時候 Alpha 會被動畫曲線持續改動）。
        private static float ComputeBaseEffectiveOpacity(int partIndex, BcModel model)
        {
            float result = 1f;
            int index = partIndex;
            while (index >= 0 && index < model.Parts.Count)
            {
                var part = model.Parts[index];
                result *= part.Opacity / (float)model.MaxOpacity;
                if (part.ParentIndex == index) break; // 安全防呆，避免資料異常造成無窮迴圈
                index = part.ParentIndex;
            }
            return result;
        }

        private static string CleanPartName(string raw)
        {
            string name = raw;
            int colorTagIdx = name.IndexOf("<color", StringComparison.Ordinal);
            if (colorTagIdx >= 0) name = name.Substring(0, colorTagIdx);
            name = name.TrimStart('-').Trim();
            return string.IsNullOrEmpty(name) ? "part" : name;
        }

        // X/Y/PivotX/PivotY/Angle 這五個欄位在 maanim 裡是「疊加」在 mamodel 基礎姿勢上的差值
        // （例如臉部基礎 X=-18，動畫關鍵影格卻是 -2~1 這種接近 0 的小幅擺動，明顯是相對量而非絕對座標）；
        // Scale/Opacity 則是「取代」基礎值的絕對量（關鍵影格直接是 900~1050 這種接近 1000=100% 的數字）。
        private AnimationClip BuildClip(
            string clipName, BcAnim anim, BcModel model, Transform root,
            Transform[] partTransforms, Transform[] spriteChildTransforms,
            BcRigNode[] rigNodes, Sprite[] spriteByCutIndex)
        {
            var clip = new AnimationClip { name = clipName, legacy = true, frameRate = frameRate };
            bool looping = false;

            foreach (var track in anim.Tracks)
            {
                if (track.PartIndex < 0 || track.PartIndex >= partTransforms.Length) continue;
                var partTf = partTransforms[track.PartIndex];
                if (partTf == null) continue;
                // LoopFrame 是整段動畫共用的循環旗標（見 BcFormat.cs 的 BcTrack.LoopFrame 註解），
                // 不是逐 track 的值，但每個 track 都重複帶著同一個值，取哪個都一樣。
                if (track.LoopFrame == -1) looping = true;

                switch (track.ModifierType)
                {
                    case BcModifierType.PosX:
                    {
                        int baseVal = model.Parts[track.PartIndex].X;
                        SetFloatCurve(clip, root, partTf, typeof(Transform), "m_LocalPosition.x", track, v => (baseVal + v) / pixelsPerUnit, ensureFrameZero: true);
                        break;
                    }
                    case BcModifierType.PosY:
                    {
                        int baseVal = model.Parts[track.PartIndex].Y;
                        SetFloatCurve(clip, root, partTf, typeof(Transform), "m_LocalPosition.y", track, v => -(baseVal + v) / pixelsPerUnit, ensureFrameZero: true);
                        break;
                    }
                    case BcModifierType.Angle:
                    {
                        int baseVal = model.Parts[track.PartIndex].Angle;
                        float sign = invertRotation ? -1f : 1f;
                        SetFloatCurve(clip, root, partTf, typeof(Transform), "localEulerAnglesRaw.z", track, v => ToDegrees((int)(baseVal + v), model.MaxAngle) * sign, ensureFrameZero: true);
                        break;
                    }
                    case BcModifierType.ScaleX:
                        SetFloatCurve(clip, root, partTf, typeof(Transform), "m_LocalScale.x", track, v => v / (float)model.MaxScale);
                        break;
                    case BcModifierType.ScaleY:
                        SetFloatCurve(clip, root, partTf, typeof(Transform), "m_LocalScale.y", track, v => v / (float)model.MaxScale);
                        break;
                    case BcModifierType.ScaleUniform:
                        SetFloatCurve(clip, root, partTf, typeof(Transform), "m_LocalScale.x", track, v => v / (float)model.MaxScale);
                        SetFloatCurve(clip, root, partTf, typeof(Transform), "m_LocalScale.y", track, v => v / (float)model.MaxScale);
                        break;

                    case BcModifierType.PivotX:
                    {
                        var spriteTf = spriteChildTransforms[track.PartIndex];
                        if (spriteTf == null) break;
                        int baseVal = model.Parts[track.PartIndex].PivotX;
                        SetFloatCurve(clip, root, spriteTf, typeof(Transform), "m_LocalPosition.x", track, v => -(baseVal + v) / pixelsPerUnit, ensureFrameZero: true);
                        break;
                    }
                    case BcModifierType.PivotY:
                    {
                        var spriteTf = spriteChildTransforms[track.PartIndex];
                        if (spriteTf == null) break;
                        int baseVal = model.Parts[track.PartIndex].PivotY;
                        SetFloatCurve(clip, root, spriteTf, typeof(Transform), "m_LocalPosition.y", track, v => (baseVal + v) / pixelsPerUnit, ensureFrameZero: true);
                        break;
                    }
                    case BcModifierType.Opacity:
                    {
                        // 透明度不是直接改這個零件自己的 SpriteRenderer alpha —— 改成驅動 BcRigNode.Alpha，
                        // 由它在 LateUpdate 往上乘整條 parent chain，這樣父節點淡出才會連帶淡出所有子零件
                        // （對應原始格式裡「隱藏 idle 整組」「隱藏 attack 整組」這種父節點級別的顯示切換）。
                        var rigNode = rigNodes[track.PartIndex];
                        if (rigNode == null) break;
                        SetFloatCurve(clip, root, partTf, typeof(BcRigNode), "Alpha", track, v => v / (float)model.MaxOpacity);
                        break;
                    }
                    case BcModifierType.ZOrder:
                    {
                        var spriteTf = spriteChildTransforms[track.PartIndex];
                        if (spriteTf == null) break;
                        SetFloatCurve(clip, root, spriteTf, typeof(SpriteRenderer), "m_SortingOrder", track, v => v);
                        break;
                    }
                    case BcModifierType.FlipH:
                    {
                        var spriteTf = spriteChildTransforms[track.PartIndex];
                        if (spriteTf == null) break;
                        SetFloatCurve(clip, root, spriteTf, typeof(SpriteRenderer), "m_FlipX", track, v => v);
                        break;
                    }
                    case BcModifierType.FlipV:
                    {
                        var spriteTf = spriteChildTransforms[track.PartIndex];
                        if (spriteTf == null) break;
                        SetFloatCurve(clip, root, spriteTf, typeof(SpriteRenderer), "m_FlipY", track, v => v);
                        break;
                    }
                    case BcModifierType.CutIndex:
                    {
                        var spriteTf = spriteChildTransforms[track.PartIndex];
                        if (spriteTf == null) break;
                        SetSpriteCurve(clip, root, spriteTf, track, spriteByCutIndex);
                        break;
                    }
                    case BcModifierType.Parent:
                        Debug.LogWarning($"[BcModelImporter] {clipName}: 不支援動畫中途換 parent（part {track.PartIndex}），已略過。");
                        break;
                }
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = looping;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            clip.wrapMode = looping ? WrapMode.Loop : WrapMode.Once;
            return clip;
        }

        // maanim 每個關鍵影格自帶一個 Ease 欄位。0=線性沒有疑問，但 1 的意義連 BCU 社群自己的教學都寫得
        // 很模糊（原文："No easing - Basically skips that line. Don't use it. It's stupid ** it does have
        // a purpose for specific effects**"）。先前當作「維持不變、下一格瞬間跳過去」試了兩種寫法都沒解決
        // 實際觀察到的問題，這裡改試字面上最直接的讀法：ease=1 的那一行整個當作沒寫過，插值直接從前一個
        // 「非 1」的 keyframe 接到下一個「非 1」的 keyframe，中間被跳過的 frame/value 完全不影響曲線形狀。
        //
        // 副作用：PhotoCat 資料裡大量 track 只有「第 0 幀、ease=1」單一筆（例如 149_s02 的
        // キャラクターダミー PosX：只有 "0,-1,1,0" 一筆）。這種 track 在跳過 ease=1 後整條曲線是空的，
        // 之前的做法是整條不寫曲線（見下面 curve.length == 0 return），代表這個屬性在這個 clip 播放期間
        // 完全不會被觸碰、維持上一個 clip / 上次取樣殘留的值——這正是「相對」屬性 (Pos/Angle/Pivot)
        // 才會出現的整段跑歪問題。ensureFrameZero=true 時，如果曲線在 0 秒沒有關鍵影格（包含整條被跳空的
        // 情況），就先補一個 convert(0)（＝mamodel 的基礎姿勢，delta=0）當保底起點，確保這個屬性至少在
        // 第 0 幀有明確定義的值，不會整段沿用別的 clip 殘留的姿勢。只用在相對量屬性；Scale/Opacity 等絕對量
        // 屬性的「0」沒有意義（等於縮放到 0 / 完全透明），不能套用同一招。
        private void SetFloatCurve(AnimationClip clip, Transform root, Transform target, Type componentType, string property, BcTrack track, Func<float, float> convert, bool ensureFrameZero = false)
        {
            string path = AnimationUtility.CalculateTransformPath(target, root);
            var curve = new AnimationCurve();
            foreach (var kf in track.Keyframes)
            {
                if (kf.Ease == 1) continue;
                curve.AddKey(new Keyframe(kf.Frame / frameRate, convert(kf.Value)));
            }
            if (ensureFrameZero && (curve.length == 0 || curve.keys[0].time > 0f))
            {
                curve.AddKey(new Keyframe(0f, convert(0)));
            }
            if (curve.length == 0) return;
            ApplyLinearTangents(curve);
            clip.SetCurve(path, componentType, property, curve);
        }

        private static void ApplyLinearTangents(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
        }

        private void SetSpriteCurve(AnimationClip clip, Transform root, Transform target, BcTrack track, Sprite[] spriteByCutIndex)
        {
            string path = AnimationUtility.CalculateTransformPath(target, root);
            var keys = new List<ObjectReferenceKeyframe>();
            foreach (var kf in track.Keyframes)
            {
                if (kf.Value < 0 || kf.Value >= spriteByCutIndex.Length || spriteByCutIndex[kf.Value] == null) continue;
                keys.Add(new ObjectReferenceKeyframe { time = kf.Frame / frameRate, value = spriteByCutIndex[kf.Value] });
            }
            if (keys.Count == 0) return;
            var binding = EditorCurveBinding.PPtrCurve(path, typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys.ToArray());
        }

    }
}
