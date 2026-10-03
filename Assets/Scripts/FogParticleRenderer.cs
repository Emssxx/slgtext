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

    private ParticleSystem _ps;
    private ParticleSystem.Particle[] _buffer;
    private GridManager _grid;

    // NPC位置を毎フレーム集める用。
    private readonly List<Vector3> _npcPositions = new List<Vector3>();

    void Start()
    {
        _grid = GridManager.Instance;
        if (_grid == null) { Debug.LogError("[FogParticleRenderer] GridManager が無い"); return; }
        BuildParticleSystem();
    }

    void BuildParticleSystem()
    {
        GameObject go = new GameObject("FogParticles");
        go.transform.parent = transform;

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
        main.startColor = fogColor;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;

        var shape = _ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(w, h, 0.1f);

        var emission = _ps.emission;
        int cellCount = _grid.gridWidth * _grid.gridHeight;
        emission.rateOverTime = cellCount * emissionPerCell;

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

    void LateUpdate()
    {
        if (_ps == null) return;

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
    }
}
