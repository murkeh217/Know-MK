using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;
using UnityEngine.UIElements.Experimental;

public class UIManager : MonoBehaviour
{
    private VisualElement loader;
    private Label loaderText;
    private ScrollView tabs;
    private VisualElement slideContainer;

    void Awake()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        loader = root.Q<VisualElement>("loader");
        loaderText = root.Q<Label>("loaderText");
        tabs = root.Q<ScrollView>("tabs");
        slideContainer = root.Q<VisualElement>("slideContainer");

        // Start the loading animation
        StartCoroutine(HideLoader());

        // Setup button events once (capture loop variable safely)
        var tabButtons = tabs.contentContainer.Query<Button>().ToList();
        foreach (var b in tabButtons)
        {
            var btn = b; // capture
            btn.clicked += () =>
            {
                // Remove "active" class from all buttons
                foreach (var other in tabButtons)
                    other.RemoveFromClassList("active");

                // Add "active" to clicked one
                btn.AddToClassList("active");

                // Handle tab change
                OnTabClicked(btn);
            };
        }
    }

    private IEnumerator HideLoader()
    {
        for (int i = 0; i <= 100; i += 10)
        {
            loaderText.text = i + "%";
            yield return new WaitForSeconds(0.05f);
        }

        loader.style.display = DisplayStyle.None;
    }

    private void OnTabClicked(Button btn)
    {
        // Fade out current content
        slideContainer.experimental.animation.Start(
            new StyleValues { opacity = 0 },
            200 // duration in ms
        ).OnCompleted(() =>
        {
            slideContainer.Clear();

            // Create new label for selected tab
            var label = new Label("You selected: " + btn.text);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.fontSize = 20;
            label.style.color = Color.black;
            label.style.marginTop = 40;
            slideContainer.Add(label);

            // Fade in
            slideContainer.experimental.animation.Start(
                new StyleValues { opacity = 1 },
                250
            );
        });
    }
}
