using UnityEngine;
using UnityEngine.Rendering;

// ============================================================================
// 2D 光照用的材质小工具（两套渲染管线都能用）。
//
// 【为什么要它】换成 URP 的 2D 渲染器以后，代码里生成的图默认都会"受光"（光照不到就是黑的）。
//   角色、建筑、地面这样正好；但迷雾、血条、气泡、子弹、枪火、建造预览这些提示类的东西必须一直看得清，
//   要换成"不受光"材质。软管这种用网格画的身体部件则要换成"受光"材质，不然黑暗里只有它亮着。
// 【旧管线（内置管线）下】这里什么都不做（返回 null / 原来的材质），画面和以前一样。
// ============================================================================
public static class RenderMats
{
    static Material _unlit, _lit;      // 不受光 / 受光的精灵材质（所有地方共用）

    // 现在是不是 URP（项目设置里指定了渲染管线资源）
    public static bool IsURP => GraphicsSettings.currentRenderPipeline != null;

    // 不受光的精灵材质（一直按原色显示）；旧管线返回 null
    public static Material SpriteUnlit
    {
        get
        {
            if (!IsURP) return null;
            if (_unlit == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (sh != null) _unlit = new Material(sh) { name = "Sprite-Unlit (RenderMats)" };
            }
            return _unlit;
        }
    }

    // 受光的精灵材质；旧管线返回 null
    public static Material SpriteLit
    {
        get
        {
            if (!IsURP) return null;
            if (_lit == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
                if (sh != null) _lit = new Material(sh) { name = "Sprite-Lit (RenderMats)" };
            }
            return _lit;
        }
    }

    // 让一个渲染器不受光（一直看得清）。旧管线下不变
    public static void MakeUnlit(Renderer r)
    {
        Material m = SpriteUnlit;
        if (r != null && m != null) r.sharedMaterial = m;
    }

    // 网格画的身体部件用的材质：URP = 受光；旧管线 = 原来的 Sprites/Default
    public static Material MeshSpriteMaterial()
    {
        Material lit = SpriteLit;
        return lit != null ? new Material(lit) : new Material(Shader.Find("Sprites/Default"));
    }
}
