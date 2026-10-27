using UnityEngine;
using UnityEngine.AI; // NavMeshAgent 在这里

// NPC 移动控制（NavMesh 寻路版）：给一个目标点，NPC 用导航网格自动绕开墙走过去。
// 移动时切走路动画 + 按左右翻转朝向；停下播待机。
[RequireComponent(typeof(NavMeshAgent))]
public class NpcMover : MonoBehaviour
{
    public float arriveDistance = 0.1f; // 离目标多近算到达

    private NavMeshAgent _agent;   // 导航代理，负责算路和移动
    private Animator _animator;
    private SpriteRenderer _sprite;
    private bool _hasTarget = false;
    private float _baseSpeed = 3f; // NavMeshAgent 上原本设置的速度（闲逛调慢后用它恢复）
    private float _speedMult = 1f; // 行为给的倍率（闲逛慢走等）
    private float _tempFactor = 1f; // 气温给的倍率（寒冷时变慢，ClimateSystem 设置）
    private float _healthFactor = 1f; // 健康状态给的倍率（疲惫 / 受伤 / 生病…，NpcHealth 设置；濒死 = 0 走不动）
    private float _terrainFactor = 1f; // 脚下地形给的倍率（土地 0.9 / 沼泽 0.2 / 营地地砖 1，TerrainSystem）
    private float _terrainTimer;
    private NpcView _view;
    private Vector3 _lastPos;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _baseSpeed = _agent.speed;

        // 2D 关键设置：代码里再保险地关掉 3D 旋转/抬升，防止 Sprite 乱转。
        _agent.updateRotation = false;
        _agent.updateUpAxis = false;

        _animator = GetComponent<Animator>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>();

        _sprite = GetComponent<SpriteRenderer>();
        if (_sprite == null) _sprite = GetComponentInChildren<SpriteRenderer>();
        _view = GetComponent<NpcView>();
        _lastPos = transform.position;
    }

    // 外部调用：立刻停止移动（清除路径、重置标志、停走路动画）。
    public void Stop()
    {
        _agent.ResetPath();   // 清除导航路径，agent 速度变0
        _hasTarget = false;   // 标志位置false，Update不再轮询
        SetMovingAnim(false); // 立刻切回待机动画
    }

    // 外部调用：命令 NPC 走到某个世界坐标（会自动寻路绕障碍）。
    public void MoveTo(Vector3 targetPos)
    {
        targetPos.z = transform.position.z;
        _agent.SetDestination(targetPos); // 交给导航系统算路
        _hasTarget = true;
    }

    public bool IsMoving => _hasTarget;

    // 原本的走路速度。
    public float BaseSpeed => _baseSpeed;

    // 外部调用：按倍率调整走路速度（闲逛时慢悠悠地走；用完调回 1）。
    public void SetSpeedMultiplier(float multiplier)
    {
        _speedMult = Mathf.Max(0.05f, multiplier);
        ApplySpeed();
    }

    // 外部调用：气温造成的速度倍率（寒冷时 < 1）。和上面的倍率相乘，互不覆盖。
    public void SetTemperatureFactor(float factor)
    {
        factor = Mathf.Max(0.05f, factor);
        if (Mathf.Approximately(factor, _tempFactor)) return;
        _tempFactor = factor;
        ApplySpeed();
    }

    // 外部调用：健康状态造成的速度倍率（NpcHealth 每 0.25 秒设置一次）。0 = 走不动（濒死）。
    public void SetHealthFactor(float factor)
    {
        factor = Mathf.Max(0f, factor);
        if (Mathf.Approximately(factor, _healthFactor)) return;
        _healthFactor = factor;
        ApplySpeed();
    }

    void ApplySpeed()
    {
        if (_agent == null) return;
        _agent.speed = _baseSpeed * _speedMult * _tempFactor * _healthFactor * _terrainFactor;
    }

    // 每 0.2 秒看一眼脚下的地形（走进沼泽就慢下来，走上营地地砖恢复原速）。
    void UpdateTerrainFactor()
    {
        _terrainTimer -= Time.deltaTime;
        if (_terrainTimer > 0f) return;
        _terrainTimer = 0.2f;
        float f = Mathf.Max(0.05f, TerrainSystem.SpeedFactorAt(transform.position));
        if (Mathf.Approximately(f, _terrainFactor)) return;
        _terrainFactor = f;
        ApplySpeed();
    }

    // 走了多远就扣多少体力（每格 1 点，NpcHealth 结算）。瞬移（空间扭曲）一帧跳很远，不算。
    void TrackMovement()
    {
        Vector3 p = transform.position;
        float d = Vector2.Distance(p, _lastPos);
        _lastPos = p;
        if (d > 0.0001f && d < 2f && _view != null) NpcHealth.OnMoved(_view.npcId, d);
    }

    // 外部调用：原地转身面朝某个点（闲聊时面对面用）。左右翻转规则和走路时一致。
    public void FaceTowards(Vector3 worldPos)
    {
        if (_sprite == null) return;
        float dx = worldPos.x - transform.position.x;
        if (dx < -0.01f) _sprite.flipX = true;       // 朝左
        else if (dx > 0.01f) _sprite.flipX = false;  // 朝右
        // 若角色原图本来朝左，和 Update 里一样把这两行 true/false 对调。
    }

    void Update()
    {
        TrackMovement();
        UpdateTerrainFactor();
        if (!_hasTarget)
        {
            SetMovingAnim(false);
            return;
        }

        // 用代理的速度判断在不在移动。
        Vector3 vel = _agent.velocity;
        bool moving = vel.sqrMagnitude > 0.01f;
        SetMovingAnim(moving);

        // 按水平速度方向翻转朝向。
        if (_sprite != null && moving)
        {
            if (vel.x < -0.01f) _sprite.flipX = true;       // 朝左
            else if (vel.x > 0.01f) _sprite.flipX = false;  // 朝右
            // 若角色原图本来朝左，把这两行 true/false 对调。
        }

        // 到达判定：路径算完 且 离目标足够近。
        if (!_agent.pathPending && _agent.remainingDistance <= arriveDistance)
        {
            _hasTarget = false;
            SetMovingAnim(false);
        }
    }

    void SetMovingAnim(bool moving)
    {
        if (_animator != null)
            _animator.SetBool("isMoving", moving);
    }
}
