using System.Collections.Generic;
using UnityEngine;
using TMPro;

// NPC 头顶的对话气泡（世界空间，跟着 NPC 走）。纯表现层。
// 用法：NpcSpeechBubble.Show(npc的transform, "台词", 秒数); NpcSpeechBubble.Hide(npc的transform);
// 每个 NPC 只有一个气泡，重复 Show 会直接换字。背景和小尾巴的贴图在运行时生成，不需要美术资源。
// 字体、字号、宽度、高度、层级读 NpcIdleManager 上的设置（没有管理器就用默认值）。
public class NpcSpeechBubble : MonoBehaviour
{
    static readonly Dictionary<Transform, NpcSpeechBubble> _bubbles = new Dictionary<Transform, NpcSpeechBubble>();
    static Sprite _bodySprite;
    static Sprite _tailSprite;

    static readonly Color BodyColor = new Color(0.07f, 0.08f, 0.11f, 0.88f); // 深色半透明，和状态卡同一风格
    static readonly Color TextColor = new Color(0.93f, 0.95f, 1f, 1f);

    const float PadX = 0.14f;   // 文字左右留白（世界单位）
    const float PadY = 0.09f;   // 文字上下留白
    const float TailH = 0.08f;  // 小尾巴高度

    Transform _target;
    SpriteRenderer _body;
    SpriteRenderer _tail;
    TextMeshPro _text;
    float _hideAt;
    float _alpha;
    bool _showing;

    // —— 对外接口 ——

    // 在 target 头顶显示一句话，持续 seconds 秒后自动淡出。
    public static void Show(Transform target, string content, float seconds)
    {
        if (target == null || string.IsNullOrEmpty(content)) return;
        if (!_bubbles.TryGetValue(target, out NpcSpeechBubble b) || b == null)
        {
            b = Create(target);
            _bubbles[target] = b;
        }
        b.SetContent(content, seconds);
    }

    // 立刻收起 target 的气泡（淡出）。
    public static void Hide(Transform target)
    {
        if (target == null) return;
        if (_bubbles.TryGetValue(target, out NpcSpeechBubble b) && b != null)
            b._showing = false;
    }

    // —— 内部实现 ——

    static NpcSpeechBubble Create(Transform target)
    {
        GameObject go = new GameObject("SpeechBubble_" + target.name);
        NpcSpeechBubble b = go.AddComponent<NpcSpeechBubble>();
        b._target = target;
        b.Build();
        return b;
    }

    void Build()
    {
        NpcIdleManager cfg = NpcIdleManager.Instance;
        int order = cfg != null ? cfg.bubbleSortingOrder : 500;

        // 背景（圆角矩形，九宫格拉伸）
        GameObject bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(transform, false);
        _body = bodyGo.AddComponent<SpriteRenderer>();
        _body.sprite = BodySprite();
        _body.drawMode = SpriteDrawMode.Sliced;
        _body.color = BodyColor;
        _body.sortingOrder = order;

        // 小尾巴（指向说话的人）
        GameObject tailGo = new GameObject("Tail");
        tailGo.transform.SetParent(transform, false);
        _tail = tailGo.AddComponent<SpriteRenderer>();
        _tail.sprite = TailSprite();
        _tail.color = BodyColor;
        _tail.sortingOrder = order;

        // 文字（3D 版 TextMeshPro，画在背景上面一层）
        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(transform, false);
        _text = textGo.AddComponent<TextMeshPro>();
        TMP_FontAsset font = cfg != null ? cfg.BubbleFont : null;
        if (font != null) _text.font = font; // 中日文字体（没有的话中文会变方块）
        _text.fontSize = cfg != null ? cfg.bubbleFontSize : 2.4f;
        _text.alignment = TextAlignmentOptions.Center;
        _text.textWrappingMode = TextWrappingModes.Normal;
        _text.overflowMode = TextOverflowModes.Overflow;
        _text.richText = false;
        _text.color = TextColor;
        MeshRenderer mr = textGo.GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = order + 1;
    }

    void SetContent(string content, float seconds)
    {
        NpcIdleManager cfg = NpcIdleManager.Instance;
        float maxW = cfg != null ? cfg.bubbleMaxWidth : 3.2f;

        // 上一句淡出后气泡会被隐藏，先重新显示再算尺寸（隐藏状态下 TMP 量不准）。
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
            _alpha = 0f;
        }

        // 按文字实际大小算气泡尺寸：短句窄、长句自动换行。
        _text.text = content;
        Vector2 pref = _text.GetPreferredValues(content, maxW, 50f); // 宽度限制 maxW，高度不限
        float w = Mathf.Min(pref.x, maxW);
        float h = pref.y;
        _text.rectTransform.sizeDelta = new Vector2(w, h);

        float bw = w + PadX * 2f;
        float bh = h + PadY * 2f;
        float centerY = TailH + bh * 0.5f;
        _body.size = new Vector2(bw, bh);
        _body.transform.localPosition = new Vector3(0f, centerY, 0f);
        _text.transform.localPosition = new Vector3(0f, centerY, 0f);
        _tail.transform.localPosition = new Vector3(0f, TailH * 0.5f + 0.005f, 0f);

        _hideAt = Time.time + seconds;
        _showing = true;
        Follow();
        ApplyAlpha();
    }

    void LateUpdate()
    {
        if (_target == null) { Destroy(gameObject); return; } // NPC 没了（死亡淡出后销毁），气泡跟着消失

        if (_showing && Time.time >= _hideAt) _showing = false;
        _alpha = Mathf.MoveTowards(_alpha, _showing ? 1f : 0f, Time.deltaTime * 6f); // 约 0.15 秒淡入淡出
        Follow();
        ApplyAlpha();

        if (!_showing && _alpha <= 0f) gameObject.SetActive(false);
    }

    // 跟在 NPC 头顶。
    void Follow()
    {
        NpcIdleManager cfg = NpcIdleManager.Instance;
        float offsetY = cfg != null ? cfg.bubbleOffsetY : 0.55f;
        Vector3 p = _target.position;
        transform.position = new Vector3(p.x, p.y + offsetY, p.z);
    }

    // 透明度 + 一点点弹出缩放。
    void ApplyAlpha()
    {
        Color c = BodyColor;
        c.a *= _alpha;
        _body.color = c;
        _tail.color = c;
        _text.alpha = _alpha;
        float s = Mathf.Lerp(0.85f, 1f, _alpha);
        transform.localScale = new Vector3(s, s, 1f);
    }

    void OnDestroy()
    {
        if (!ReferenceEquals(_target, null)
            && _bubbles.TryGetValue(_target, out NpcSpeechBubble b)
            && ReferenceEquals(b, this))
            _bubbles.Remove(_target);
    }

    // —— 运行时生成的贴图 ——

    // 圆角矩形（白色，颜色由 SpriteRenderer.color 染）。带九宫格边框，可任意拉伸。
    static Sprite BodySprite()
    {
        if (_bodySprite != null) return _bodySprite;
        const int size = 32;
        const float r = 10f;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float cx = Mathf.Clamp(fx, r, size - r);
                float cy = Mathf.Clamp(fy, r, size - r);
                float d = Vector2.Distance(new Vector2(fx, fy), new Vector2(cx, cy));
                float a = Mathf.Clamp01(r - d + 0.5f); // 边缘抗锯齿
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        _bodySprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                                    0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        return _bodySprite;
    }

    // 向下的小三角。
    static Sprite TailSprite()
    {
        if (_tailSprite != null) return _tailSprite;
        const int w = 16, h = 8;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color32[] px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float half = (y + 0.5f) / h * (w * 0.5f); // 底部尖、顶部宽
            for (int x = 0; x < w; x++)
            {
                float a = Mathf.Clamp01(half - Mathf.Abs(x + 0.5f - w * 0.5f) + 0.5f);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        _tailSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        return _tailSprite;
    }
}
