using UnityEngine;

// 鏡頭跟隨玩家移動,並被夾在 LevelManager 算好的地圖邊界內,不會看到地圖外。
// 由 LevelManager 在關卡開始時對 Camera.main 呼叫 Init(...) 設定目標與邊界。
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour {
    Camera cam;
    Transform target;
    Vector2 boundsMin;
    Vector2 boundsMax;

    void Awake() {
        cam = GetComponent<Camera>();
    }

    public void Init(Transform target, Vector2 boundsMin, Vector2 boundsMax) {
        this.target = target;
        this.boundsMin = boundsMin;
        this.boundsMax = boundsMax;
    }

    void LateUpdate() {
        if (target == null) return;

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        float x = ClampAxis(target.position.x, boundsMin.x, boundsMax.x, halfWidth);
        float y = ClampAxis(target.position.y, boundsMin.y, boundsMax.y, halfHeight);

        transform.position = new Vector3(x, y, transform.position.z);
    }

    // 地圖比畫面小的那個軸,鏡頭固定在地圖中心,不跟著跑
    static float ClampAxis(float value, float min, float max, float halfScreenSize) {
        if (max - min < halfScreenSize * 2f) return (min + max) * 0.5f;
        return Mathf.Clamp(value, min + halfScreenSize, max - halfScreenSize);
    }
}
