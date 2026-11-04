using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleCatsImport
{
    // 把從 BCU 抽出來的 Battle Cats 原生美術（xxx.png + xxx.imgcut + xxx.mamodel + xxx*.maanim）
    // 轉成一個 BcAnimSet 資料檔 + 掛好 BcAnimPlayer 的 Prefab。
    // 不再烤成 AnimationClip：動畫在執行期由 BcAnimCore（BCU 原始碼的移植）逐幀計算。
    //
    // 用法：Tools > Battle Cats Import > Import Model From PNG...，把來源 png 拖進去，
    // 同資料夾底下的 xxx.imgcut / xxx.mamodel / xxx*.maanim 會依檔名自動找到。
    // 重複匯入會覆寫同一路徑的 asset/prefab（保留 GUID，場景和其他 prefab 的參照不會斷）。
    public class BcModelImporterWindow : EditorWindow
    {
        private Texture2D sourcePng;
        private float pixelsPerUnit = 100f;
        private string outputFolder = "Assets/Prefabs/BcAnim";

        [MenuItem("Tools/Battle Cats Import/Import Model From PNG...")]
        private static void Open()
        {
            GetWindow<BcModelImporterWindow>("BC Model Import");
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "拖入 BCU 解包出的 xxx.png，同資料夾需有同名的 xxx.imgcut / xxx.mamodel / xxx*.maanim。",
                MessageType.Info);

            sourcePng = (Texture2D)EditorGUILayout.ObjectField("Source PNG", sourcePng, typeof(Texture2D), false);
            pixelsPerUnit = EditorGUILayout.FloatField("Pixels Per Unit", pixelsPerUnit);

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
            if (maanimPaths.Length == 0) throw new FileNotFoundException($"{dir} 底下找不到 {baseName}*.maanim");

            if (!AssetDatabase.IsValidFolder(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
                AssetDatabase.Refresh();
            }

            var cuts = BcFormat.ParseImgcut(imgcutPath);
            var model = BcFormat.ParseModel(mamodelPath);
            var clips = maanimPaths
                .Select(p => BcFormat.ParseAnim(p, ClipNameFromPath(p, baseName)))
                .ToArray();
            BcFormat.Sanitize(model, clips, cuts.Count);

            var sprites = SliceTexture(pngPath, cuts);
            var set = SaveAnimSet(baseName, model, clips, sprites);
            BuildPrefab(baseName, set, DefaultClipIndex(clips));

            Debug.Log($"[BcModelImporter] 完成：{outputFolder}/{baseName}.prefab（{model.parts.Length} 個零件，{clips.Length} 段動畫：{string.Join(", ", clips.Select(c => c.name))}）");
        }

        // 「149_s02.maanim」→「02」。BC 慣例：00 走路、01 待機、02 攻擊、03 被擊退。
        private static string ClipNameFromPath(string path, string baseName)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            string suffix = file.Substring(baseName.Length).TrimStart('_');
            return suffix.Length > 0 ? suffix : file;
        }

        // 預設播待機（01），沒有就播第一段。
        private static int DefaultClipIndex(BcClipData[] clips)
        {
            int idle = Array.FindIndex(clips, c => c.name == "01");
            return idle >= 0 ? idle : 0;
        }

        // 依 imgcut 把來源 png 切成具名子 Sprite，並回傳以 cut 編號對應的陣列。
        // pivot 一律放左上角：BCU 的 pivot 是零件層級（同一張圖可以被不同零件用不同 pivot 參照），
        // 由 BcAnimPlayer 每幀自己算位置，不靠 Sprite 的 pivot。
        private Sprite[] SliceTexture(string pngPath, List<BcFormat.Cut> cuts)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            // 零件常常會旋轉、縮放，用雙線性過濾比 Point 平滑，接近 BCU（OpenGL）的顯示效果。
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            int texHeight = texture.height;

            var usedNames = new HashSet<string>();
#pragma warning disable 0618 // SpriteMetaData/TextureImporter.spritesheet 在 2022.3 仍可用，只是標記過時
            var metas = new List<SpriteMetaData>(cuts.Count);
            for (int i = 0; i < cuts.Count; i++)
            {
                var cut = cuts[i];
                // Unity 不接受寬或高為 0 的 Sprite
                int w = Mathf.Max(1, cut.W), h = Mathf.Max(1, cut.H);
                metas.Add(new SpriteMetaData
                {
                    name = MakeUniqueSpriteName(i, cut.Name, usedNames),
                    // imgcut 的 Y 是從貼圖頂端算起，Unity 的 Rect 是從底部算起，需要翻轉。
                    rect = new Rect(cut.X, texHeight - cut.Y - h, w, h),
                    pivot = new Vector2(0f, 1f),
                    alignment = (int)SpriteAlignment.Custom,
                });
            }
            importer.spritesheet = metas.ToArray();
#pragma warning restore 0618
            importer.SaveAndReimport();

            var result = new Sprite[cuts.Count];
            foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(pngPath).OfType<Sprite>())
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

        private BcAnimSet SaveAnimSet(string baseName, BcModelData model, BcClipData[] clips, Sprite[] sprites)
        {
            string path = $"{outputFolder}/{baseName}_anim.asset";
            var set = AssetDatabase.LoadAssetAtPath<BcAnimSet>(path);
            bool isNew = set == null;
            if (isNew) set = CreateInstance<BcAnimSet>();

            set.pixelsPerUnit = pixelsPerUnit;
            set.sprites = sprites;
            set.model = model;
            set.clips = clips;
            set.InvalidateCache();

            if (isNew)
                AssetDatabase.CreateAsset(set, path);
            else
                EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return set;
        }

        // Prefab：root（SortingGroup + BcAnimPlayer）底下每個零件一個 SpriteRenderer，全部平鋪不巢狀。
        private void BuildPrefab(string baseName, BcAnimSet set, int defaultClip)
        {
            var root = new GameObject(baseName);
            try
            {
                root.AddComponent<SortingGroup>();

                var names = set.model.partNames;
                var renderers = new SpriteRenderer[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    var go = new GameObject($"{i:00}_{CleanPartName(names[i])}");
                    go.transform.SetParent(root.transform, false);
                    renderers[i] = go.AddComponent<SpriteRenderer>();
                }

                var player = root.AddComponent<BcAnimPlayer>();
                player.EditorSetup(set, renderers, defaultClip);
                // 把預設動畫第 0 幀的姿勢烤進 Prefab，Edit mode 打開就是正確的樣子。
                player.ResetToDefault();

                string prefabPath = $"{outputFolder}/{baseName}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        // 去掉 BC 零件名稱裡的 <color=N> 標籤。
        private static string CleanPartName(string raw)
        {
            string name = System.Text.RegularExpressions.Regex.Replace(raw ?? "", "<[^>]*>", "").Trim();
            return string.IsNullOrEmpty(name) ? "part" : name;
        }
    }
}
