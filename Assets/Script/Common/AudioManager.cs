using System.Collections.Generic;
using UnityEngine;

// 全域音效/BGM 管理器:單例,首次被存取時自動建立並跨場景保留,不需要手動放進場景
// 音效/BGM 可用檔名透過 Resources 讀取,分別放在 Assets/Resources/Audio/SFX、Assets/Resources/Audio/BGM 底下即可
// 玩家音量設定(總音量 Audio / BGM / SFX,皆 0~1)也存在這裡並寫進 PlayerPrefs:
// BGM 實際音量 = Audio × BGM(× 固定的 BgmMixLevel),音效實際音量 = Audio × SFX
public class AudioManager : MonoBehaviour {
    static readonly string[] hurtClipNames = { "hurt1", "hurt2" }; // 受傷音效隨機池,玩家/敵人共用

    // BGM 素材本身比音效大聲,固定壓在 -10dB(10^(-10/20))當混音基準,玩家的音量設定再乘上去;
    // AudioSource.volume 是線性倍率不是分貝
    const float BgmMixLevel = 0.316f;

    const string PrefKeyMasterVolume = "Settings_MasterVolume";
    const string PrefKeyBgmVolume = "Settings_BgmVolume";
    const string PrefKeySfxVolume = "Settings_SfxVolume";

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

    public float MasterVolume { get; private set; } = 1f;
    public float BgmVolume { get; private set; } = 1f;
    public float SfxVolume { get; private set; } = 1f;

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

        MasterVolume = PlayerPrefs.GetFloat(PrefKeyMasterVolume, 1f);
        BgmVolume = PlayerPrefs.GetFloat(PrefKeyBgmVolume, 1f);
        SfxVolume = PlayerPrefs.GetFloat(PrefKeySfxVolume, 1f);
        ApplyBgmVolume();
    }

    // 播放 BGM,clipName 對應 Assets/Resources/Audio/BGM/{clipName}
    public void PlayBGM(string clipName, bool loop = true) => PlayBGM(LoadClip(bgmCache, "Audio/BGM", clipName), loop);

    // 播放 BGM,同一首正在播放時不會重新播放。loop 預設為 true
    public void PlayBGM(AudioClip clip, bool loop = true) {
        if (clip == null) return;
        if (bgmSource.isPlaying && bgmSource.clip == clip) return;

        bgmSource.clip = clip;
        bgmSource.loop = loop;
        bgmSource.Play();
    }

    public void StopBGM() {
        bgmSource.Stop();
        bgmSource.clip = null;
    }

    public void PauseBGM() => bgmSource.Pause();
    public void ResumeBGM() => bgmSource.UnPause();

    // 播放一次性音效,clipName 對應 Assets/Resources/Audio/SFX/{clipName}
    public void PlaySFX(string clipName, float volumeScale = 1f) => PlaySFX(LoadClip(sfxCache, "Audio/SFX", clipName), volumeScale);

    // 播放一次性音效,volumeScale 可為個別音效額外調整音量,支援多個音效同時疊加播放
    public void PlaySFX(AudioClip clip, float volumeScale = 1f) {
        if (clip == null) return;
        sfxSource.PlayOneShot(clip, MasterVolume * SfxVolume * volumeScale);
    }

    // 受傷音效:玩家/敵人共用,隨機播放 hurt1 或 hurt2
    public void PlayRandomHurtSfx() {
        PlaySFX(hurtClipNames[Random.Range(0, hurtClipNames.Length)]);
    }

    // 音量設定立刻生效(正在播的 BGM 也會馬上變),並寫進 PlayerPrefs;
    // 拖滑桿時會連續呼叫,這裡不呼叫 PlayerPrefs.Save()(寫硬碟),由呼叫端在適當時機(例如關閉設定面板)存檔
    public void SetMasterVolume(float volume) {
        MasterVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKeyMasterVolume, MasterVolume);
        ApplyBgmVolume();
    }

    public void SetBGMVolume(float volume) {
        BgmVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKeyBgmVolume, BgmVolume);
        ApplyBgmVolume();
    }

    public void SetSFXVolume(float volume) {
        SfxVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKeySfxVolume, SfxVolume);
    }

    void ApplyBgmVolume() => bgmSource.volume = BgmMixLevel * MasterVolume * BgmVolume;

    static AudioClip LoadClip(Dictionary<string, AudioClip> cache, string folder, string clipName) {
        if (cache.TryGetValue(clipName, out AudioClip cached)) return cached;

        AudioClip clip = Resources.Load<AudioClip>($"{folder}/{clipName}");
        if (clip == null) Debug.LogWarning($"找不到音效檔:Resources/{folder}/{clipName}");

        cache[clipName] = clip;
        return clip;
    }
}
