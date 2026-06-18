# NavMeshPlus 2D 配置指南（h8man 官方教程整理版）

> 本项目使用 NavMeshPlus 扩展实现 2D 导航。凡是涉及 NavMesh 的操作，**必须先读这份指南再动手**。

---

## 一、三个核心组件（缺一不可）

| 组件 | 挂在哪里 | 作用 |
|------|----------|------|
| `NavigationSurface` | 空物体 `NavMesh` 上 | 执行烘焙，生成导航网格 |
| `NavigationCollectSources2d` | 同一个 `NavMesh` 物体上 | **2D 必需**。告诉 Surface 如何收集 2D 来源 |
| `NavigationModifier` | 地面/障碍物体上 | 标记该物体是可走还是不可走 |

---

## 二、正确配置流程（Tilemap 地图，本项目用这个）

### 步骤 1 — 建 NavMesh 空物体

1. 在场景里新建空物体，命名 `NavMesh`，Position 设为 `(0, 0, 0)`。
2. 给它添加两个组件：
   - `NavigationSurface`
   - `NavigationCollectSources2d`
3. 点击 `NavigationCollectSources2d` 组件上的 **"Rotate Surface to XY"** 按钮。
   - **这一步必须做**，否则烘焙平面朝向错误，烘不出任何东西。

### 步骤 2 — 建 Tilemap 地图结构

在场景里建一个 `Grid`，下面挂三个子 Tilemap：

| Tilemap 名称 | 用途 | NavigationModifier 设置 |
|------------|------|------------------------|
| `Ground` | 画地面（可以走的区域） | 加 NavigationModifier，**不勾 Override Area**（默认 = Walkable） |
| `Walls` | 画墙（不能走） | 加 NavigationModifier，**勾 Override Area**，Area Type = `Not Walkable` |
| `Pond` | 画水/障碍（不能走） | 加 NavigationModifier，**勾 Override Area**，Area Type = `Not Walkable` |

### 步骤 3 — 烘焙前确认 Agent Type 占位物体

> Unity 2022.3+ 已知问题：如果场景里没有带 `NavMeshAgent` 的物体，点 Bake 会烘出空网格，看不到蓝色区域。

在场景里**临时**放一个带 `NavMeshAgent` 组件的空物体（或者把 NPC 物体提前放进场景）。只要有一个带 Agent 的物体定义了 Agent Type，Bake 就能正常识别。

### 步骤 4 — 烘焙

1. 选中 `NavMesh` 物体，在 Inspector 里找到 `NavigationSurface`。
2. 点击 **Bake** 按钮。
3. 烘焙成功后，应看到：地面区域被蓝色半透明网格覆盖，墙和水池处是空的（没有蓝色）。

---

## 三、NavMeshAgent 在 2D 里的必做设置

每个带 `NavMeshAgent` 的 NPC 脚本，在 `Start()` 里必须加这两行，否则 Sprite 会旋转乱掉：

```csharp
var agent = GetComponent<NavMeshAgent>();
agent.updateRotation = false;  // 不让 Agent 自动旋转物体
agent.updateUpAxis = false;    // 不让 Agent 对齐 Z 轴（2D 用 XY 平面）
```

---

## 四、运行时重新烘焙（建造模式改变地图后）

如果游戏运行中玩家改变了地图（放置/拆除建筑），需要重新烘焙：

```csharp
// 先同步物理，否则碰撞体边界未更新，烘出错误结果
Physics2D.SyncTransforms();

// 然后重新烘焙（异步，不卡帧）
var surface = GetComponent<NavigationSurface>();
surface.BuildNavMeshAsync();
// 或者用 UpdateNavMesh(surface.navMeshData) 增量更新
```

**注意**：物理体在 `FixedUpdate` 里更新。如果在 `Awake` / `Start` 里动态生成了物理体，不能立刻烘焙，要等 `LateUpdate` 之后再调。

---

## 五、必须避开的坑（官方 KNOWN ISSUES）

### 坑 1：只加 NavigationSurface，没加 NavigationCollectSources2d
- **现象**：点 Bake 完全没反应，没有蓝色区域。
- **修复**：必须同时挂这两个组件，且点了 Rotate Surface to XY。

### 坑 2：Unity 2022.3+ 烘焙出空网格
- **现象**：点 Bake 没报错，但地面上没有蓝色区域。
- **修复**：在场景里放至少一个带 `NavMeshAgent` 组件的物体，再点 Bake。

### 坑 3：Sources = 0 / 烘不出可走区域
- **现象**：Bake 完成，但蓝色区域全是空的，连地面都没有。
- **修复**：检查 `Ground` Tilemap 的 `NavigationModifier`，确认**没有**勾 Override Area 或 Area Type 是 `Walkable`；最常见错误是把地面误设成了 `Not Walkable`。

### 坑 4：Use Geometry 设置错误
- 默认值 `Render Meshes`：用 Sprite 的 Mesh 烘焙（适合 Sprite/Tilemap）。
- `Physics Colliders`：用 2D 碰撞体烘焙。
- 两者互斥，根据项目选一个，不要混用。

---

## 六、2D 烘焙不出来的排查三连（速查）

1. **有没有同时挂 NavigationCollectSources2d，并点了 Rotate Surface to XY？**
2. **场景里有没有至少一个带 NavMeshAgent 的物体？**（Unity 2022.3+ 必需）
3. **Ground 的 NavigationModifier 是不是被误设成了 Not Walkable？**

---

> 最后更新：2026-06-18
> 来源：NavMeshPlus (h8man) 官方教程 + 官方 KNOWN ISSUES
