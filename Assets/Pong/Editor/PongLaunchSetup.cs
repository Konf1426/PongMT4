using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Assistant d'éditeur : construit la scène de lancement, le PanelSettings UI Toolkit
/// et met à jour le Build Settings — le tout depuis Unity, qui génère lui-même des
/// références/GUID valides.
///
/// Utilisation : menu « Tools > Pong > Setup Launch Scene ». Idempotent (réexécutable).
/// </summary>
public static class PongLaunchSetup
{
    const string UiFolder = "Assets/Pong/UI";
    const string ScenesFolder = "Assets/Pong/Scenes";

    const string ThemePath = UiFolder + "/PongTheme.tss";
    const string UxmlPath = UiFolder + "/MainMenu.uxml";
    const string PanelPath = UiFolder + "/PongPanelSettings.asset";
    const string LaunchScenePath = ScenesFolder + "/Launch.unity";
    const string GameScenePath = "Assets/Pong/Pong.unity";

    [MenuItem("Tools/Pong/Setup Launch Scene")]
    public static void Setup()
    {
        AssetDatabase.Refresh();

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        PanelSettings panel = CreateOrLoadPanelSettings();
        VisualTreeAsset menuAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        if (menuAsset == null)
        {
            Debug.LogError("PongLaunchSetup: MainMenu.uxml introuvable à " + UxmlPath);
            return;
        }

        BuildLaunchScene(panel, menuAsset);
        RegisterScenes();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("PongLaunchSetup: scène de lancement prête. Ouvre " + LaunchScenePath + " et lance le jeu.");
    }

    static PanelSettings CreateOrLoadPanelSettings()
    {
        PanelSettings panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
        if (panel != null)
        {
            return panel;
        }

        panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution = new Vector2Int(1920, 1080);

        ThemeStyleSheet theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        if (theme != null)
        {
            panel.themeStyleSheet = theme;
        }
        else
        {
            Debug.LogWarning("PongLaunchSetup: PongTheme.tss introuvable, PanelSettings sans thème.");
        }

        EnsureFolder(UiFolder);
        AssetDatabase.CreateAsset(panel, PanelPath);

        // Important : flush + recharge depuis le disque. Une référence vers un asset
        // tout juste créé n'est pas encore stable et se sérialiserait en {fileID: 0}
        // dans la scène (le PanelSettings n'apparaîtrait pas sur le UIDocument).
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
    }

    static void BuildLaunchScene(PanelSettings panel, VisualTreeAsset menuAsset)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject cameraGo = new GameObject("Main Camera");
        cameraGo.tag = "MainCamera";
        Camera camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.07f, 0.09f, 1f);
        cameraGo.AddComponent<AudioListener>();

        GameObject eventSystemGo = new GameObject("EventSystem");
        eventSystemGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
        eventSystemGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

        GameObject menuGo = new GameObject("MainMenu");
        UIDocument document = menuGo.AddComponent<UIDocument>();
        document.panelSettings = panel;
        document.visualTreeAsset = menuAsset;
        menuGo.AddComponent<PongMainMenu>();

        EnsureFolder(ScenesFolder);
        EditorSceneManager.SaveScene(scene, LaunchScenePath);
    }

    static void RegisterScenes()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>();
        scenes.Add(new EditorBuildSettingsScene(LaunchScenePath, true));

        if (System.IO.File.Exists(GameScenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(GameScenePath, true));
        }
        else
        {
            Debug.LogWarning("PongLaunchSetup: scène de jeu introuvable à " + GameScenePath);
        }

        // Conserve les autres scènes éventuelles (hors doublons des deux ci-dessus).
        foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
        {
            if (existing.path == LaunchScenePath || existing.path == GameScenePath)
            {
                continue;
            }

            scenes.Add(existing);
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, leaf);
    }
}
