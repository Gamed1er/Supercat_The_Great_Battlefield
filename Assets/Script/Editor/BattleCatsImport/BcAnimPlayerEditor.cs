using UnityEditor;
using UnityEngine;

namespace BattleCatsImport
{
    // BcAnimPlayer 的 Inspector：選動畫、拖影格、在 Edit mode 直接播放預覽。
    // 用來逐幀對照 BCU 的動畫檢視器，確認移植結果一致。
    [CustomEditor(typeof(BcAnimPlayer))]
    public class BcAnimPlayerEditor : Editor
    {
        private int previewClip;
        private int previewFrame;
        private bool previewing;
        private double lastTime;

        private void OnDisable()
        {
            StopPreview();
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var player = (BcAnimPlayer)target;
            var set = player.AnimSet;
            if (set == null || set.clips == null || set.clips.Length == 0)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("預覽", EditorStyles.boldLabel);

            // 在 Project 視窗直接選 Prefab 資產時，改動會寫進資產本身，所以不提供預覽。
            if (EditorUtility.IsPersistent(player))
            {
                EditorGUILayout.HelpBox("把 Prefab 拖進場景或打開 Prefab Mode 才能預覽。", MessageType.Info);
                return;
            }

            if (player.CurrentClipIndex >= 0)
                previewClip = player.CurrentClipIndex;

            var clipNames = new string[set.clips.Length];
            for (int i = 0; i < clipNames.Length; i++) clipNames[i] = set.clips[i].name;

            int newClip = EditorGUILayout.Popup("動畫", previewClip, clipNames);
            if (newClip != previewClip)
            {
                previewClip = newClip;
                player.Seek(previewClip, 0);
                SceneView.RepaintAll();
            }

            int maxFrame = Mathf.Max(player.ClipMaxFrame, 0);
            previewFrame = player.CurrentFrame;
            int newFrame = EditorGUILayout.IntSlider("影格", previewFrame, 0, maxFrame);
            if (newFrame != previewFrame && !previewing)
            {
                player.Seek(previewClip, newFrame);
                SceneView.RepaintAll();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(previewing ? "停止" : "播放"))
            {
                if (previewing) StopPreview();
                else StartPreview();
            }
            if (GUILayout.Button("上一幀") && player.CurrentFrame > 0)
            {
                player.Seek(previewClip, player.CurrentFrame - 1);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("下一幀"))
            {
                player.Seek(previewClip, player.CurrentFrame + 1);
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void StartPreview()
        {
            if (Application.isPlaying) return; // Play mode 下元件自己會播
            previewing = true;
            lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += PreviewUpdate;
        }

        private void StopPreview()
        {
            if (!previewing) return;
            previewing = false;
            EditorApplication.update -= PreviewUpdate;
        }

        private void PreviewUpdate()
        {
            if (target == null)
            {
                StopPreview();
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            ((BcAnimPlayer)target).Tick((float)(now - lastTime));
            lastTime = now;
            SceneView.RepaintAll();
            Repaint();
        }
    }
}
