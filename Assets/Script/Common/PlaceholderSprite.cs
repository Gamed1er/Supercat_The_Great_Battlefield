using System.Collections.Generic;
using UnityEngine;

// 尚未有美術資源前的暫時佔位圖形:純色方塊,1x1 world unit
public static class PlaceholderSprite {
    static readonly Dictionary<Color, Sprite> cache = new Dictionary<Color, Sprite>();

    public static Sprite Get(Color color) {
        if (cache.TryGetValue(color, out Sprite sprite)) return sprite;

        const int size = 32;
        var texture = new Texture2D(size, size);
        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        texture.SetPixels(pixels);
        texture.Apply();

        sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        cache[color] = sprite;
        return sprite;
    }
}
