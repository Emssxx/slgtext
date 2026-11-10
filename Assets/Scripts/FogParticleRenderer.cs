using UnityEngine;
using System.Collections.Generic;

// 战争迷雾 · 粒子雾气版（NPCを中心に円形に晴らす方式）。
//
// 効果：地図全体に暗紫の霧パーティクルが漂う。
//   各NPCを中心とした半径 revealRadius の円の中では、霧が半透明になり、
//   円の外では濃い霧のまま（ハードな境界）。
//
// なぜこの方式か：
//   霧の本体はパーティクル（数千粒）。マスごとに状態を持たせて重ねると、
//   流れる際に「半透明であるべき霧が、不透明の霧に覆われる」問題が起きる。
//   そこで、透明度を「マスの状態」ではなく「最も近いNPCからの距離」で直接決める。
//   これなら霧同士が覆い隠し合うことがなく、NPCの周りだけが確実に晴れる。
//
// データ層 FogManager には依存しない（距離だけで判定するため）。
//
// 【用法】場面に空物体を作り本スクリプトを付ける。ParticleSystem は自動生成。
//   既存の FogRenderer / FogParticleRenderer(旧) は外す。
//
// 【大きいマップ（followCamera）】霧の粒はカメラの周り（＋viewMargin）にだけ置く。
//   カメラの外に出た粒は反対側へ回り込ませるので、地図が何倍になっても粒の数・密度は変わらない。
//   ズームアウトして粒が足りないときは、粒を大きくして同じ面積を覆う。
public class FogParticleRenderer : MonoBehaviour
{
    [Header("霧の外観")]
    public Color fogColor = new Color(0.10f, 0.09f, 0.16f, 1f); // 暗い紫の霧
    public float particleSize = 1.6f;
    public float lifetime = 6f;
    public float driftSpeed = 0.25f;

    [Header("濃さ")]
    [Range(0f, 1f)] public float fogAlpha = 0.85f;     // 円の外（濃い霧）の濃さ
    [Range(0f, 1f)] public float revealAlpha = 0.15f;  // 円の中（晴れた所）の濃さ

    [Header("NPCの周りを晴らす円")]
    public float revealRadius = 3.5f;   // NPCを中心に、この半径の中の霧を晴らす

    [Header("AI端末の周りを晴らす円（拠点の中核なので広め）")]
    public float terminalRevealRadius = 6.5f;   // AI端末を中心に晴らす半径（NPCより大きめ）
    public bool terminalNeedsOnline = true;     // 端末が起動中(IsOnline)のときだけ晴らすか

    [Header("密度")]
    public float emissionPerCell = 2.5f;
    public int maxParticles = 4000;
    public int sortingOrder = 100;

    [Header("大きいマップ：カメラの周りにだけ霧を置く（粒の数が地図の大きさに比例しない）")]
    public bool followCamera = true;
    [Tooltip("カメラの外側にどれだけ余分に霧を置くか（世界単位）。カメラを動かしても端が欠けない")]
    public float viewMargin = 2f;

    private ParticleSystem _ps;
    private ParticleSystem.Particle[] _buffer;
    private GridManager _grid;

    // —— カメラ追従 ——
    private GameObject _psGo;
    private float _boxX, _boxY, _boxW, _boxH;
    private int _target;
    private float _sizeMul = 1f;

    // NPC位置を毎フレーム集める用。
    private readonly List<Vector3> _npcPositions = new List<Vector3>();

    // FogOfWarRenderer（贴图迷雾）启用时为 true：粒子雾不再生成。
    public static bool Suppressed;

    void Start()
    {
        if (Suppressed) { enabled = false; return; }
        _grid = GridManager.Instance;
        if (_grid == null) { Debug.LogError("[FogParticleRenderer] GridManager が無い"); return; }
        BuildParticleSystem();
    }

    void BuildParticleSystem()
    {
        GameObject go = new GameObject("FogParticles");
        go.transform.parent = transform;
        _psGo = go;

        float w = _grid.gridWidth * _grid.cellSize;
        float h = _grid.gridHeight * _grid.cellSize;
        Vector3 center = new Vector3(_grid.origin.x + w * 0.5f, _grid.origin.y + h * 0.5f, 0f);
        go.transform.position = center;

        _ps = go.AddComponent<ParticleSystem>();
        var main = _ps.main;
        main.loop = true;
        main.startLifetime = lifetime;
        main.startSpeed = 0f;
        main.startSize = particleSize;
        // カメラ追従：新しい粒は最初から「濃い霧」の濃さ（1フレームだけ真っ黒に光るのを防ぐ）
        main.startColor = followCamera ? new Color(fogColor.r, fogColor.g, fogColor.b, fogAlpha) : fogColor;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;

        var shape = _ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(w, h, 0.1f);

        var emission = _ps.emission;
        // 按地图面积（世界单位²，= 细分前的格数）算发射量：格子细分后雾的浓度不变
        float area = w * h;
        emission.rateOverTime = area * emissionPerCell;
        if (followCamera)
        {
            // カメラ追従：自動発生はやめて、LateUpdate で必要な数だけ Emit。粒は死なせず、カメラの周りで回り込ませる。
            emission.rateOverTime = 0f;
            main.startLifetime = 100000f;
        }

        var vel = _ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-driftSpeed, driftSpeed);
        vel.y = new ParticleSystem.MinMaxCurve(-driftSpeed, driftSpeed);
        // ★この行を消さないこと★
        // x/y を「二定数モード」にしたら z も必ず同じモードにする。
        // z を既定（一定数モード）のまま残すと、Unity が毎フレーム
        // 「Particle Velocity curves must all be in the same mode」エラーを出し続ける。
        // 2Dゲームなので奥行きには動かさない → 0～0。
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sortingOrder = sortingOrder;
        renderer.material = BuildFogMaterial();

        _buffer = new ParticleSystem.Particle[maxParticles];
    }

    // 造一个「真正半透明」的雾粒子材质。
    //
    // 【为什么不能直接 new Material(Shader.Find(...))】
    //   在 Inspector 里给材质选 Fade / Transparent 时，是编辑器的材质面板
    //   在背后帮你设好一整套混合参数和 shader 关键字。
    //   用代码 new 出来的材质不走那套流程，拿到的是 shader 的裸默认值：
    //     _SrcBlend=One、_DstBlend=Zero、_ZWrite=1、renderQueue=2000
    //   这是「完全不透明」，粒子的 alpha 改了也没有任何效果。
    //   所以下面这些必须一项一项手动设。
    Material BuildFogMaterial()
    {
        if (RenderMats.IsURP)                                    // URP：旧管线的粒子 shader 会变粉红，换成精灵 shader（同样是按 alpha 半透明叠上去）
        {
            Material u = new Material(Shader.Find("Sprites/Default"));
            u.mainTexture = CreateSoftCircleTexture(64);
            return u;
        }
        Shader sh = Shader.Find("Particles/Standard Unlit");
        if (sh == null)
        {
            Debug.LogError("[FogParticleRenderer] 找不到 shader「Particles/Standard Unlit」。");
            return null;
        }

        Material m = new Material(sh);

        m.SetFloat("_Mode", 2f);   // 2 = Fade（半透明模式）
        // 标准的半透明混合：新画的颜色按自己的 alpha 混到已有画面上。
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        // 半透明物体不能写深度，否则前面的粒子会把后面的粒子剔掉，雾就叠不起来。
        m.SetFloat("_ZWrite", 0f);
        // 打开 shader 内部的透明混合分支（光设参数不够，关键字也要开）。
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        // 排进透明队列（3000），在所有不透明物体画完之后再画。
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        // 软圆贴图。没有贴图时粒子就是一个实心正方形——这正是「方块感」的来源。
        m.mainTexture = CreateSoftCircleTexture(64);

        return m;
    }

    // 用代码生成一张「中心实、边缘虚」的圆形贴图。
    // Unity 内置的 Default-Particle 贴图在本工程里取不到，所以自己画一张。
    Texture2D CreateSoftCircleTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;   // 边缘不重复采样，避免出现接缝

        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 这个像素离中心有多远：0 = 正中心，1 = 到达圆边。
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                // 1-d：越靠中心越不透明。再平方一次，让边缘过渡更柔和（更像雾，不像硬边圆点）。
                float a = Mathf.Clamp01(1f - d);
                a = a * a;

                // RGB 留白，实际颜色由粒子的 startColor 决定；这里只用 alpha 造出圆形轮廓。
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        tex.Apply();
        return tex;
    }

    // カメラの周り（地図からはみ出さない範囲）に霧を置く箱を更新。必要な粒の数と大きさもここで決める。
    bool UpdateViewBox()
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic || _psGo == null) return false;
        float halfH = cam.orthographicSize + viewMargin, halfW = cam.orthographicSize * cam.aspect + viewMargin;
        Vector3 c = cam.transform.position;
        float mapX0 = _grid.origin.x - 1f, mapY0 = _grid.origin.y - 1f;
        float mapX1 = _grid.origin.x + _grid.gridWidth * _grid.cellSize + 1f, mapY1 = _grid.origin.y + _grid.gridHeight * _grid.cellSize + 1f;
        float x0 = Mathf.Max(c.x - halfW, mapX0), x1 = Mathf.Min(c.x + halfW, mapX1);
        float y0 = Mathf.Max(c.y - halfH, mapY0), y1 = Mathf.Min(c.y + halfH, mapY1);
        if (x1 - x0 < 0.5f || y1 - y0 < 0.5f) return false;
        _boxX = x0; _boxY = y0; _boxW = x1 - x0; _boxH = y1 - y0;
        _psGo.transform.position = new Vector3(x0 + _boxW * 0.5f, y0 + _boxH * 0.5f, 0f);
        var shape = _ps.shape;
        shape.scale = new Vector3(_boxW, _boxH, 0.1f);
        // 元の密度 = 1平方単位あたり emissionPerCell × lifetime 粒。上限を超えるときは粒を大きくして同じ面積を覆う。
        float needed = _boxW * _boxH * emissionPerCell * lifetime;
        _target = Mathf.Max(1, Mathf.Min(maxParticles, Mathf.CeilToInt(needed)));
        _sizeMul = needed > _target ? Mathf.Sqrt(needed / _target) : 1f;
        var main = _ps.main;
        main.startSize = particleSize * _sizeMul;
        return true;
    }

    // 箱の外に出た値を反対側へ回り込ませる。
    static float Wrap(float v, float min, float size)
    {
        if (v >= min && v < min + size) return v;
        float t = (v - min) % size;
        if (t < 0f) t += size;
        return min + t;
    }

    void LateUpdate()
    {
        if (_ps == null) return;
        bool follow = followCamera && UpdateViewBox();

        // 1. すべてのNPCの現在位置を集める。
        _npcPositions.Clear();
        List<NpcView> views = NpcView.GetAllViews();
        if (views != null)
            foreach (var v in views)
                if (v != null) _npcPositions.Add(v.transform.position);

        float rNpc2 = revealRadius * revealRadius;

        // 2. AI端末の円（拠点の中核。NPCより広い半径）。
        //    端末が存在し、（設定により）起動中のときだけ晴らす。
        bool hasTerminal = false;
        Vector3 terminalPos = Vector3.zero;
        float rTerm2 = terminalRevealRadius * terminalRevealRadius;
        if (TerminalController.Instance != null)
        {
            bool online = !terminalNeedsOnline || TerminalController.Instance.IsOnline();
            if (online)
            {
                hasTerminal = true;
                terminalPos = TerminalController.Instance.transform.position;
            }
        }

        // 3. 各粒子について、NPCの円 or 端末の円 に入っていれば晴らす。
        int count = _ps.GetParticles(_buffer);
        for (int i = 0; i < count; i++)
        {
            if (follow)
            {
                if (i >= _target) { _buffer[i].remainingLifetime = 0f; continue; } // 多すぎる分は消す（ズームインしたとき）
                Vector3 p0 = _buffer[i].position;
                _buffer[i].position = new Vector3(Wrap(p0.x, _boxX, _boxW), Wrap(p0.y, _boxY, _boxH), p0.z);
                _buffer[i].startSize = particleSize * _sizeMul;
            }
            Vector3 pos = _buffer[i].position;
            bool revealed = false;

            // NPCの円（半径 revealRadius）
            for (int n = 0; n < _npcPositions.Count; n++)
            {
                float dx = pos.x - _npcPositions[n].x;
                float dy = pos.y - _npcPositions[n].y;
                if (dx * dx + dy * dy <= rNpc2) { revealed = true; break; }
            }

            // AI端末の円（半径 terminalRevealRadius・広め）
            if (!revealed && hasTerminal)
            {
                float dx = pos.x - terminalPos.x;
                float dy = pos.y - terminalPos.y;
                if (dx * dx + dy * dy <= rTerm2) revealed = true;
            }

            Color c = fogColor;
            c.a = revealed ? revealAlpha : fogAlpha;
            _buffer[i].startColor = c;
        }
        _ps.SetParticles(_buffer, count);
        if (follow && count < _target) _ps.Emit(Mathf.Min(_target - count, 2000)); // 足りない分を箱の中に追加（1フレーム最大2000）
    }
}
