# NavMesh (NavMeshPlus) 配置参考 — 本项目专用

> 本笔记基于 NavMeshPlus 官方 HOW TO 教程 + 本项目踩过的坑整理。
> 配置 2D NavMesh 时以本笔记为准。插件：NavMeshPlus by h8man
> 安装：Package Manager → Add package from git URL → https://github.com/h8man/NavMeshPlus.git

---

## 一、基本流程（最小可用）

1. 建空物体命名 `NavMesh`，Position (0,0,0)。
2. 给它加组件 `NavigationSurface`（Navigation Surface）。
3. 再加组件 `NavigationCollectSources2d`。
4. 在 NavigationCollectSources2d 上点 **Rotate Surface to XY**（2D 必须这一步！否则方向错，烘不出）。
5. 点 **Bake**，Scene 里出现蓝色可行走区域 = 成功。

- 可行走区（地面）：物体加 `NavigationModifier`，Override Area 不勾（默认可走）。
- 障碍（墙/水）：物体加 `NavigationModifier`，勾 `Override Area`，Area Type 设 `Not Walkable`。

---

## 二、本项目用 Tilemap 方案（重点，我们就是这个）

1. 场景建 Grid + 多个 Tilemap，命名 "Ground"、"Walls"、"Pond"。
2. 用调色板分别画：Ground 画地面、Walls 画障碍、Pond 画水。
3. 每个 Tilemap 加 `NavigationModifier`：
   - Ground → 可走（Override Area 不勾，或设 Walkable）
   - Walls / Pond → 勾 Override Area + Not Walkable
4. 在 NavMesh 物体上 Bake。

> 进阶：也可用单个 Tilemap + NavigationModifierTilemap，给不同 tile 分别设可走/不可走（Tile Modifier 方案）。本项目先用多 Tilemap 分层方案，简单清晰。

---

## 三、⚠️ Unity 6 / 2022.3+ 关键坑（必看，否则烘不出来）

1. **必须先建一个带 NavMeshAgent 的物体，再 Bake。**
   新版 Unity 的 AI Navigation 后端：如果场景里没有任何 Agent / 未定义 Agent Type，
   Navigation Surface 可能烘焙为空、或蓝色可视化根本不显示（即使其它都对）。
   解决：先建一个 dummy Sprite/GameObject，加 `Nav Mesh Agent` 组件，再点 Bake。

2. **烘焙出 0 / 没有蓝色区域** → 最常见原因：把地面误标成了 Not Walkable。
   检查地面的 NavigationModifier：Override Area 应【不勾】（或 Walkable）。

3. **2D 必须点 Rotate Surface to XY**（见上）。Collider 方案下烘焙运行时要先 Physics2D.SyncTransforms()。

---

## 四、⚠️ NPC（Agent）必做设置（否则2D角色会被转没）

NavMeshAgent 默认会旋转物体，2D 下会把精灵转得看不见。每个 Agent 的脚本 Start() 里必须：

```csharp
void Start() {
    var agent = GetComponent<NavMeshAgent>();
    agent.updateRotation = false;  // 不让它旋转物体
    agent.updateUpAxis = false;    // 2D 不用 up 轴
}
```

> 本项目的 NpcMover 若用 NavMeshAgent，应包含上面两行。

---

## 五、Use Geometry 两种模式（按需）

- `Render Meshes`（默认）：用精灵的 Mesh 建导航面，贴合精灵轮廓。
- `Physics Colliders`：用 2D 碰撞体多边形建面。两者互斥。
- 本项目 Tilemap 方案用默认 Render Meshes 即可。

---

## 六、运行时烘焙（以后程序化生成地图才需要，现在不用）

```csharp
public NavMeshSurface Surface2D;
void Start() { Surface2D.BuildNavMeshAsync(); }
```
注意：碰撞体 bounds 到 LateUpdate 才更新，运行时烘焙前要先 Physics2D.SyncTransforms()。

---

## 验证成功的标准
- Scene 视图：蓝色覆盖在可走区域，墙/水处是空的（被挖掉）。
- Play：NPC（带正确设置的 NavMeshAgent）能绕开障碍走到目标，不穿墙、不被转没。
