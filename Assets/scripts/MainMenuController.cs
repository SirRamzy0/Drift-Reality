using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

public sealed class MainMenuController : MonoBehaviour
{
    [SerializeField] private string raceSceneName = "SampleScene";

    private void Start()
    {
        Time.timeScale = 1f;
        EnsureCamera();
        EnsureEventSystem();
        BuildMenu();
    }

    private void Play()
    {
        SceneManager.LoadScene(raceSceneName);
    }

    private void EnsureCamera()
    {
        Camera camera = Camera.main;
        if (camera != null)
        {
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.13f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            return;
        }

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        camera = cameraObject.AddComponent<Camera>();
        camera.backgroundColor = new Color(0.08f, 0.1f, 0.13f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        cameraObject.AddComponent<AudioListener>();
    }

    private void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
#else
        eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
    }

    private void BuildMenu()
    {
        GameObject canvasObject = new GameObject("Main Menu Canvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject titleObject = new GameObject("Title");
        titleObject.transform.SetParent(canvasObject.transform, false);
        Text title = titleObject.AddComponent<Text>();
        title.text = "Drift Reality";
        title.alignment = TextAnchor.MiddleCenter;
        title.fontSize = 58;
        title.fontStyle = FontStyle.Bold;
        title.color = Color.white;
        title.font = GetBuiltinFont();
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.sizeDelta = new Vector2(620f, 90f);
        titleRect.anchoredPosition = new Vector2(0f, 115f);

        GameObject buttonObject = new GameObject("Play Button");
        buttonObject.transform.SetParent(canvasObject.transform, false);
        Image buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = new Color(0.92f, 0.12f, 0.08f, 1f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        button.onClick.AddListener(Play);

        RectTransform buttonRect = buttonImage.rectTransform;
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(240f, 72f);
        buttonRect.anchoredPosition = Vector2.zero;

        GameObject labelObject = new GameObject("Play Label");
        labelObject.transform.SetParent(buttonObject.transform, false);
        Text label = labelObject.AddComponent<Text>();
        label.text = "\u0418\u0433\u0440\u0430\u0442\u044c";
        label.alignment = TextAnchor.MiddleCenter;
        label.fontSize = 32;
        label.fontStyle = FontStyle.Bold;
        label.color = Color.white;
        label.font = GetBuiltinFont();

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
    }

    private static Font GetBuiltinFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        return font;
    }
}
