using System.Collections.Generic;
using System.Text;
using UnityEngine;

// ============================================================================
// 格子信息（左下角一直显示的位置浮窗「location」，HoverInfo 负责显示）。
// 浮窗里的每一行由一个「信息提供者」(ICellInfoProvider) 写：第一行 xy 坐标（在标签下面），然后是地形 / 资源（TerrainCellInfo），
// 以后加地形 / 资源 / 危险度……只要新写一个提供者并注册，不用改浮窗本身：
//
//   public class TerrainInfoProvider : ICellInfoProvider
//   {
//       public int Order => 10;                                   // 排在坐标（0）后面
//       public void Append(Vector2Int cell, CellInfoBuilder b, bool jp)
//       {
//           TerrainType t = TerrainMap.Get(cell);                // 你的地形数据
//           b.Line(jp ? "地形" : "地形", t.Name(jp));               // 一行：标签 + 内容
//           if (t.moveCost > 1f) b.Line(jp ? "移動" : "移动", "-" + ((t.moveCost - 1f) * 100f).ToString("0") + "%", "#FFB060");
//       }
//   }
//   // 游戏开始时注册一次（例如地形系统的 Awake 里）：
//   CellInfo.Register(new TerrainInfoProvider());
//
// 坐标系：x = 列（从左往右 0 起），y = 行（从下往上 0 起），和 GridManager 的格子坐标一致。
// 玩家在对话里说 "去 6,7" / "(6,7)" / "x6 y7" 都能认出来（GridManager.LabelToCell）。
// ============================================================================
public interface ICellInfoProvider
{
    int Order { get; }                                          // 越小越靠上
    void Append(Vector2Int cell, CellInfoBuilder b, bool jp);   // 往浮窗里写这一格的信息（没有就什么都不写）
}

// 写浮窗内容的小工具：统一"标签 + 内容"的排版。
public class CellInfoBuilder
{
    readonly StringBuilder _sb = new StringBuilder();

    public bool IsEmpty => _sb.Length == 0;

    // 一行：灰色标签 + 内容（可指定内容颜色，如 "#FFB060"）。
    public void Line(string label, string value, string valueColor = null)
    {
        if (_sb.Length > 0) _sb.Append('\n');
        if (!string.IsNullOrEmpty(label)) _sb.Append("<color=#9AA5B8>").Append(label).Append("</color>  ");
        if (!string.IsNullOrEmpty(valueColor)) _sb.Append("<color=").Append(valueColor).Append('>').Append(value).Append("</color>");
        else _sb.Append(value);
    }

    // 一行自由文字（已带富文本也可以）。
    public void Raw(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (_sb.Length > 0) _sb.Append('\n');
        _sb.Append(text);
    }

    public override string ToString() => _sb.ToString();
}

public static class CellInfo
{
    static readonly List<ICellInfoProvider> _providers = new List<ICellInfoProvider>();
    static bool _defaults;

    public static void Register(ICellInfoProvider p)
    {
        EnsureDefaults();
        if (p == null || _providers.Contains(p)) return;
        _providers.Add(p);
        _providers.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    public static void Unregister(ICellInfoProvider p) { _providers.Remove(p); }

    // 这一格的浮窗内容（地图外返回空字符串）。
    public static string Describe(Vector2Int cell, bool jp)
    {
        EnsureDefaults();
        GridManager g = GridManager.Instance;
        if (g == null || !g.InBounds(cell.x, cell.y)) return "";
        CellInfoBuilder b = new CellInfoBuilder();
        foreach (ICellInfoProvider p in _providers)
        {
            try { p.Append(cell, b, jp); }
            catch (System.Exception e) { Debug.LogWarning("[CellInfo] " + p.GetType().Name + " 出错：" + e.Message); }
        }
        return b.ToString();
    }

    static void EnsureDefaults()
    {
        if (_defaults) return;
        _defaults = true;
        _providers.Add(new CoordinateInfoProvider());   // 第一行：xy 坐标（显示在「location」标签下面）
        _providers.Sort((a, b) => a.Order.CompareTo(b.Order));
    }
}

// 默认提供者：xy 坐标。
public class CoordinateInfoProvider : ICellInfoProvider
{
    public int Order => 0;

    public void Append(Vector2Int cell, CellInfoBuilder b, bool jp)
    {
        b.Raw("<color=#9AA5B8>X</color> <b>" + cell.x + "</b>　<color=#9AA5B8>Y</color> <b>" + cell.y + "</b>");
    }
}
