using System.Collections.Generic;
using UnityEngine;

// 全域音效/BGM 管理器:單例,首次被存取時自動建立並跨場景保留,不需要手動放進場景
// 音效/BGM 可用檔名透過 Resources 讀取,分別放在 Assets/Audio/Resources/SFX、Assets/Audio/Resources/BGM 底下即可
public class AudioManager : MonoBehaviour {
    static readonly string[] hurtClipNames = { "hurt1", "hurt2" }; // 受傷音效隨機池,玩家/敵人共用

    static AudioManager instance;

    public static AudioManager Instance {
        get {
            if (instance == null) {
                var go = new GameObject(nameof(AudioManager));
                instance = go.AddComponent<AudioManager>();
            }
            return instance;
        }
    }

    [Range(0f, 1f)] public float bgmVolume = 0.316f; // -10dB(10^(-10/20)),AudioSource.volume 是線性倍率不是分貝
    [Range(0f, 1f)] public float sfxVolume = 1f;

    AudioSource bgmSource;
    AudioSource sfxSource;

    readonly Dictionary<string, AudioClip> sfxCache = new Dictionary<string, AudioClip>();
    readonly Dictionary<string, AudioClip> bgmCache = new Dictionary<string, AudioClip>();

    void Awake() {
        if (instance != null && instance != this) {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.playOnAwake = false;
        bgmSource.loop = true;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
    }

    // 播放 BGM,clipName 對應 Assets/Audio/Resources/BGM/{clipName}
    public void PlayBGM(string clipName, bool loop = true) => PlayBGM(LoadClip(bgmCache, "BGM", clipName), loop);

    // 播放 BGM,同一首正在播放時不會重新播放。loop 預設為 true
    public void PlayBGM(AudioClip clip, bool loop = true) {
        if (clip == null) return;
        if (bgmSource.isPlaying && bgmSource.clip == clip) return;

        bgmSource.clip = clip;
        bgmSource.loop = loop;
        bgmSource.volume = bgmVolume;
        bgmSource.Play();
    }

    public void StopBGM() {
        bgmSource.Stop();
        bgmSource.clip = null;
    }

    public void PauseBGM() => bgmSource.Pause();
    public void ResumeBGM() => bgmSource.UnPause();

    // 播放一次性音效,clipName 對應 Assets/Audio/Resources/SFX/{clipName}
    public void PlaySFX(string clipName, float volumeScale = 1f) => PlaySFX(LoadClip(sfxCache, "SFX", clipName), volumeScale);

    // 播放一次性音效,volumeScale 可為個別音效額外調整音量,支援多個音效同時疊加播放
    public void PlaySFX(AudioClip clip, float volumeScale = 1f) {
        if (clip == null) return;
        sfxSource.PlayOneShot(clip, sfxVolume * volumeScale);
    }

    // 受傷音效:玩家/敵人共用,隨機播放 hurt1 或 hurt2
    public void PlayRandomHurtSfx() {
        PlaySFX(hurtClipNames[Random.Range(0, hurtClipNames.Length)]);
    }

    public void SetBGMVolume(float volume) {
        bgmVolume = Mathf.Clamp01(volume);
        bgmSource.volume = bgmVolume;
    }

    public void SetSFXVolume(float volume) {
        sfxVolume = Mathf.Clamp01(volume);
    }

    static AudioClip LoadClip(Dictionary<string, AudioClip> cache, string folder, string clipName) {
        if (cache.TryGetValue(clipName, out AudioClip cached)) return cached;

        AudioClip clip = Resources.Load<AudioClip>($"{folder}/{clipName}");
        if (clip == null) Debug.LogWarning($"找不到音效檔:Resources/{folder}/{clipName}");

        cache[clipName] = clip;
        return clip;
    }
}
