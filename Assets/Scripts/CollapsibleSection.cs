using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

// 通用折叠节控制器：点击标题按钮，平滑展开/折叠内容区域。
// 用 LayoutElement.preferredHeight + RectMask2D 实现动画，不用 SetActive。
// 挂在任何需要折叠功能的面板上（整体对话框、对话栏、事件栏、日志栏各挂一个）。
public class CollapsibleSection : MonoBehaviour
{
    [Header("引用（Inspector 拖进来）")]
    public Button headerButton;       // 点击触发折叠/展开的按钮
    public LayoutElement bodyWrapper; // 内容区外层，挂了 LayoutElement + RectMask2D

    [Header("动画")]
    public float animDuration = 0.2f; // 动画时长，0.2 秒在 60fps 下约 12 帧，平滑不卡

    [Header("箭头图标（可选）")]
    public TMP_Text toggleIcon;        // 展开时显示 ▼，折叠时显示 ▶

    [Header("备用高度")]
    public float defaultHeight = 200f; // 内容为空时的占位高度，Step 2 测试用

    bool isExpanded = true;
    float naturalHeight = -1f;  // 展开时的高度，Start 里自动测量
    Coroutine currentAnim;

    void Start()
    {
        // 注册点击事件
        headerButton.onClick.AddListener(Toggle);

        // 强制刷新布局后，读取内容区域的实际高度作为"自然高度"
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)bodyWrapper.transform);
        naturalHeight = ((RectTransform)bodyWrapper.transform).rect.height;

        // 内容为空时（Step 2 测试阶段），用 defaultHeight 备用，保证动画可见
        if (naturalHeight < 1f)
            naturalHeight = defaultHeight;

        // 把 preferredHeight 锁到自然高度，后续动画在 naturalHeight ↔ 0 之间
        bodyWrapper.preferredHeight = naturalHeight;
    }

    // 切换展开/折叠状态
    public void Toggle()
    {
        isExpanded = !isExpanded;
        float target = isExpanded ? naturalHeight : 0f;

        // 同步更新箭头图标文字
        if (toggleIcon != null) toggleIcon.text = isExpanded ? "▼" : "▶";

        // 如果上一个动画还没结束，直接中断它，从当前高度开始新动画
        if (currentAnim != null) StopCoroutine(currentAnim);
        currentAnim = StartCoroutine(Animate(target));
    }

    IEnumerator Animate(float targetHeight)
    {
        float startHeight = bodyWrapper.preferredHeight;
        float elapsed = 0f;

        while (elapsed < animDuration)
        {
            elapsed += Time.deltaTime;
            // SmoothStep 让动画头尾有缓动（先加速后减速），比 Lerp 线性更自然
            float t = Mathf.SmoothStep(0f, 1f, elapsed / animDuration);
            bodyWrapper.preferredHeight = Mathf.Lerp(startHeight, targetHeight, t);
            yield return null;
        }

        // 动画结束后精确设到目标值，避免浮点误差
        bodyWrapper.preferredHeight = targetHeight;
        currentAnim = null;
    }
}
