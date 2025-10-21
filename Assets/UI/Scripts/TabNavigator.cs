using UnityEngine;
using UnityEngine.UIElements;

// Small helper to provide left/right arrow navigation and snap-to-center behavior
// Attach this to the same GameObject as UIManager (or another GameObject with a UIDocument)
public class TabNavigator : MonoBehaviour
{
    public UIDocument uiDocument;
    private ScrollView tabsScroll;

    // How fast to animate the scroll (pixels per second)
    public float snapSpeed = 800f;
    // Enable paging (snap to nearest tab)
    public bool enablePaging = true;
    // Enable looping (infinite carousel)
    public bool loop = true;
    // How many items to clone at each end when looping
    public int cloneEdgeCount = 2;
    // Velocity threshold (pixels/sec) to consider a fling
    public float flingVelocityThreshold = 600f;

    void Awake()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null) return;

        var root = uiDocument.rootVisualElement;
        tabsScroll = root.Q<ScrollView>("tabs");

        // Ensure buttons snap to center when clicked
        if (tabsScroll != null)
        {
            // build and optionally clone buttons for looping
            BuildButtonsAndClones();

            // register pointer events for fling detection and paging
            RegisterPointerHandlers();

            // wire arrow buttons if present
            var left = root.Q<Button>("tab-left");
            var right = root.Q<Button>("tab-right");
            if (left != null) left.clicked += ScrollLeft;
            if (right != null) right.clicked += ScrollRight;
        }
    }

    // Internal state
    private System.Collections.Generic.List<Button> buttons = new System.Collections.Generic.List<Button>();
    private int originalCount = 0;
    private bool isDragging = false;
    private float lastPointerX = 0f;
    private float lastPointerTime = 0f;
    private float lastMoveDelta = 0f;

    private void BuildButtonsAndClones()
    {
        var content = tabsScroll.contentContainer;
        // capture original buttons
        var originals = content.Query<Button>().ToList();
        originalCount = originals.Count;

        buttons = new System.Collections.Generic.List<Button>();

        // If looping, prepend clones of last cloneEdgeCount and append clones of first cloneEdgeCount
        if (loop && originalCount > 0)
        {
            int c = Mathf.Min(cloneEdgeCount, originalCount);
            // prepend clones
            for (int i = c - 1; i >= 0; i--)
            {
                var src = originals[originalCount - 1 - i];
                var clone = CloneButton(src);
                content.Insert(0, clone);
            }
            // append originals
            foreach (var o in originals)
            {
                buttons.Add(o);
            }
            foreach (var o in originals)
                content.Add(o); // ensure originals are present (they already are but safe)
            // append clones
            for (int i = 0; i < c; i++)
            {
                var src = originals[i];
                var clone = CloneButton(src);
                content.Add(clone);
            }

            // rebuild buttons list from content now (includes clones)
            buttons = content.Query<Button>().ToList();
        }
        else
        {
            buttons = originals;
        }

        // Wire click handlers and set userData to logical index (0..originalCount-1)
        for (int i = 0; i < buttons.Count; i++)
        {
            var btn = buttons[i];
            // compute logical index
            int logicalIndex = i;
            if (loop && originalCount > 0)
            {
                logicalIndex = (i - cloneEdgeCount) % originalCount;
                if (logicalIndex < 0) logicalIndex += originalCount;
            }
            btn.userData = logicalIndex;
            btn.clicked += () =>
            {
                ScrollToCenter(btn);
                // set active class on logical originals
                SetActiveForLogicalIndex((int)btn.userData);
            };
        }
    }

    private Button CloneButton(Button src)
    {
        var b = new Button();
        b.text = src.text;
        // copy essential class for styling (avoid classList API which may not exist in some Unity versions)
        b.AddToClassList("tab-button");
        return b;
    }

    private void SetActiveForLogicalIndex(int logicalIndex)
    {
        // remove active from all logical originals
        foreach (var btn in tabsScroll.contentContainer.Query<Button>().ToList())
        {
            // compare userData if present
            if (btn.userData is int li)
            {
                if (li == logicalIndex) btn.AddToClassList("active");
                else btn.RemoveFromClassList("active");
            }
        }
    }

    // running average velocity filter parameters
    private float velocity = 0f;
    private float velocityAlpha = 0.2f; // smoothing factor, lower = smoother

    private void RegisterPointerHandlers()
    {
        var content = tabsScroll.contentContainer;
        content.RegisterCallback<PointerDownEvent>(evt =>
        {
            isDragging = true;
            lastPointerX = evt.position.x;
            lastPointerTime = Time.time;
            lastMoveDelta = 0f;
            velocity = 0f;
        });

        content.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!isDragging) return;
            float dx = evt.position.x - lastPointerX;
            float now = Time.time;
            float dt = now - lastPointerTime;
            lastMoveDelta = dx;
            lastPointerX = evt.position.x;
            lastPointerTime = now;
            if (dt > 0)
            {
                float instVel = dx / dt; // px/sec
                // low-pass filter
                velocity = velocity * (1f - velocityAlpha) + instVel * velocityAlpha;
            }
        });

        content.RegisterCallback<PointerUpEvent>(evt =>
        {
            isDragging = false;
            if (enablePaging)
            {
                int current = GetNearestIndex();
                int target = current;
                if (Mathf.Abs(velocity) > flingVelocityThreshold)
                {
                    int dir = velocity > 0 ? -1 : 1;
                    target = Mathf.Clamp(current + dir, 0, buttons.Count - 1);
                }
                SnapToIndex(target);
            }
        });
    }

    // Public methods to be wired to left/right UI arrows if desired
    public void ScrollLeft()
    {
        if (tabsScroll == null) return;
        if (!enablePaging) {
            var newPos = Mathf.Max(0, tabsScroll.scrollOffset.x - (Screen.width / 3));
            SmoothScrollTo(newPos);
            return;
        }
        int idx = GetNearestIndex();
        idx = Mathf.Max(0, idx - 1);
        SnapToIndex(idx);
    }

    public void ScrollRight()
    {
        if (tabsScroll == null) return;
        if (!enablePaging) {
            var max = Mathf.Max(0, tabsScroll.contentContainer.layout.width - tabsScroll.layout.width);
            var newPos = Mathf.Min(max, tabsScroll.scrollOffset.x + (Screen.width / 3));
            SmoothScrollTo(newPos);
            return;
        }
        int idx = GetNearestIndex();
        idx = Mathf.Min(buttons.Count - 1, idx + 1);
        SnapToIndex(idx);
    }

    private void ScrollToCenter(VisualElement element)
    {
        if (tabsScroll == null || element == null) return;

        var content = tabsScroll.contentContainer;
        // target center position so element is centered in the ScrollView viewport
        float target = element.layout.x + (element.layout.width / 2f) - (tabsScroll.layout.width / 2f);
        var max = Mathf.Max(0, content.layout.width - tabsScroll.layout.width);
        target = Mathf.Clamp(target, 0, max);
        SmoothScrollTo(target);
    }

    private int GetNearestIndex()
    {
        if (buttons == null || buttons.Count == 0) return 0;
        float center = tabsScroll.scrollOffset.x + (tabsScroll.layout.width / 2f);
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < buttons.Count; i++)
        {
            var b = buttons[i];
            float bCenter = b.layout.x + (b.layout.width / 2f);
            float dist = Mathf.Abs(bCenter - center);
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        return best;
    }

    private float lastSnapDuration = 0.25f;

    private void SnapToIndex(int index)
    {
        index = Mathf.Clamp(index, 0, buttons.Count - 1);
        var targetElement = buttons[index];
        if (targetElement != null)
        {
            // compute target offset and duration so we can wait precisely
            var content = tabsScroll.contentContainer;
            float target = targetElement.layout.x + (targetElement.layout.width / 2f) - (tabsScroll.layout.width / 2f);
            var max = Mathf.Max(0, content.layout.width - tabsScroll.layout.width);
            target = Mathf.Clamp(target, 0, max);
            var start = tabsScroll.scrollOffset.x;
            var dist = Mathf.Abs(target - start);
            var duration = Mathf.Max(0.02f, dist / snapSpeed);
            lastSnapDuration = duration;
            StartCoroutine(SnapAndMaybeAdjust(target, index, duration));
        }
    }

    private System.Collections.IEnumerator SnapAndMaybeAdjust(float target, int index, float duration)
    {
        // animate manually to ensure we know exact duration
        var start = tabsScroll.scrollOffset.x;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            var x = Mathf.Lerp(start, target, t);
            tabsScroll.scrollOffset = new Vector2(x, tabsScroll.scrollOffset.y);
            yield return null;
        }
        tabsScroll.scrollOffset = new Vector2(target, tabsScroll.scrollOffset.y);

        // Wait a frame and force a layout to reduce visual jump when looping
        yield return null;
        tabsScroll.contentContainer.MarkDirtyRepaint();
        tabsScroll.MarkDirtyRepaint();

        // after snap completes, if looping we may need to jump to logical counterpart
        if (loop && originalCount > 0)
            StartCoroutine(AdjustAfterSnap(index));
    }

    private System.Collections.IEnumerator AdjustAfterSnap(int index)
    {
        // wait until the snap animation finishes (approx)
        var content = tabsScroll.contentContainer;
        float waitTime = 0.3f; // approximate
        yield return new WaitForSeconds(waitTime);

        if (!loop || originalCount == 0) yield break;

        int logicalIndex = (int)buttons[index].userData;
        // find the first occurrence of this logical index within the original range (after clones)
        // target logical position index should be cloneEdgeCount + logicalIndex
        int targetIndex = cloneEdgeCount + logicalIndex;
        if (index != targetIndex)
        {
            // jump instantly to the target counterpart to preserve loop illusion
            var targetElement = buttons[targetIndex];
            if (targetElement != null)
            {
                float target = targetElement.layout.x + (targetElement.layout.width / 2f) - (tabsScroll.layout.width / 2f);
                var max = Mathf.Max(0, content.layout.width - tabsScroll.layout.width);
                target = Mathf.Clamp(target, 0, max);
                tabsScroll.scrollOffset = new Vector2(target, tabsScroll.scrollOffset.y);
            }
        }
    }

    private void SmoothScrollTo(float target)
    {
        if (tabsScroll == null) return;
        StopAllCoroutines();
        StartCoroutine(ScrollCoroutine(target));
    }

    private System.Collections.IEnumerator ScrollCoroutine(float target)
    {
        var start = tabsScroll.scrollOffset.x;
        var distance = Mathf.Abs(target - start);
        if (distance < 1f) yield break;

        var duration = Mathf.Max(0.01f, distance / snapSpeed);
        var elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            var x = Mathf.Lerp(start, target, t);
            tabsScroll.scrollOffset = new Vector2(x, tabsScroll.scrollOffset.y);
            yield return null;
        }

        tabsScroll.scrollOffset = new Vector2(target, tabsScroll.scrollOffset.y);
    }
}
