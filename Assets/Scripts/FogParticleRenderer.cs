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
        // z も必ず同じ「二定数モード」で設定する。x/y だけ設定して z を既定
        // （一定数モード）のまま残すと、Unity が毎フレーム
        // 「Particle Velocity curves must all be in the same mode」を出す。
        // 2Dゲームなので奥行きには動かさない → 0～0。
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sortingOrder = sortingOrder;
        renderer.material = new Material(Shader.Find("Particles/Standard Unlit"));

        _buffer = new ParticleSystem.Particle[maxParticles];
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

        float r2 = revealRadius * revealRadius;

        // 2. 各粒子について、最も近いNPCとの距離を見て透明度を決める。
        int count = _ps.GetParticles(_buffer);
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = _buffer[i].position;

            // いずれかのNPCの円の中に入っているか？
            bool revealed = false;
            for (int n = 0; n < _npcPositions.Count; n++)
            {
                float dx = pos.x - _npcPositions[n].x;
                float dy = pos.y - _npcPositions[n].y;
                if (dx * dx + dy * dy <= r2) { revealed = true; break; }
            }

            // 円の中＝薄い、外＝濃い（ハード境界）。
            Color c = fogColor;
            c.a = revealed ? revealAlpha : fogAlpha;
            _buffer[i].startColor = c;
        }
        _ps.SetParticles(_buffer, count);
    }
}
