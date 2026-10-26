using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.UI;

// 大廳標題畫面的裝飾貓:播放目前選定角色(CharacterSelectPopupUI.Current)的待機動畫,玩家點牠時播 KB,kbHoldDuration 秒後回到待機。
// 角色動畫是 Animator 切換 SpriteRenderer.sprite,UI Image 不能直接被同一份動畫驅動;而且角色控制器裡 KB 播完一輪就會自動轉回 Move,
// 撐不到這裡要的長度。所以不套角色的控制器,而是從控制器取出 Move/KB 兩個片段,用 Playables 直接把單一片段播在一個隱藏的
// Animator + SpriteRenderer 上(沒有狀態轉換干擾),每幀再把那個 SpriteRenderer 的 sprite 複製給 Image
public class LobbyCatDecorationUI : MonoBehaviour, IPointerDownHandler {
    [SerializeField] Image image;
    [SerializeField] string idleClipName = "Move"; // 角色控制器的預設狀態,戰鬥中站著不動也是播這個
    [SerializeField] string kbClipName = "KB";
    [SerializeField] float kbHoldDuration = 0.5f;
    [SerializeField] string kbSfx = "meow";

    SpriteRenderer sampleTarget;
    Animator sampleAnimator;
    PlayableGraph graph;
    AnimationPlayableOutput output;
    AnimationClipPlayable currentPlayable;
    AnimationClip idleClip;
    AnimationClip kbClip;
    float kbTimeLeft;

    void Awake() {
        GameObject target = new GameObject("AnimationTarget", typeof(SpriteRenderer), typeof(Animator));
        target.transform.SetParent(transform, false);
        sampleTarget = target.GetComponent<SpriteRenderer>();
        sampleTarget.color = Color.clear; // 只用來接動畫寫入的 sprite,不畫出來(元件保持啟用,確保 Animator 照常寫值)
        sampleAnimator = target.GetComponent<Animator>();
        sampleAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        graph = PlayableGraph.Create(nameof(LobbyCatDecorationUI));
        graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
        output = AnimationPlayableOutput.Create(graph, "Cat", sampleAnimator);
        graph.Play();
    }

    void OnDestroy() {
        if (graph.IsValid()) graph.Destroy();
    }

    public void SetCharacter(PlayerBase character) {
        AnimationClip[] clips = character.GetComponent<Animator>().runtimeAnimatorController.animationClips;
        idleClip = clips.First(clip => clip.name == idleClipName);
        kbClip = clips.First(clip => clip.name == kbClipName);
        kbTimeLeft = 0f;
        PlayClip(idleClip);
    }

    // 每點一次都從頭播 KB、重新計時,並叫一聲
    public void OnPointerDown(PointerEventData eventData) {
        kbTimeLeft = kbHoldDuration;
        PlayClip(kbClip);
        AudioManager.Instance.PlaySFX(kbSfx);
    }

    void Update() {
        if (kbTimeLeft <= 0f) return;

        kbTimeLeft -= Time.unscaledDeltaTime;
        if (kbTimeLeft <= 0f) PlayClip(idleClip);
    }

    // Animator 在 Update 之後、LateUpdate 之前寫入 sprite
    void LateUpdate() => image.sprite = sampleTarget.sprite;

    // 一次只接一個片段;片段本身勾了 Loop Time,會自己循環
    void PlayClip(AnimationClip clip) {
        if (currentPlayable.IsValid()) currentPlayable.Destroy();
        currentPlayable = AnimationClipPlayable.Create(graph, clip);
        output.SetSourcePlayable(currentPlayable);
    }
}
