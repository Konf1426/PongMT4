using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Contrôleur de la scène de lancement (UI Toolkit).
/// Lit le menu défini dans MainMenu.uxml, câble les boutons, mémorise le mode
/// choisi dans <see cref="PongSession"/> puis charge la scène de jeu.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class PongMainMenu : MonoBehaviour
{
    UIDocument document;

    // Start (et pas OnEnable) pour garantir que UIDocument a déjà construit son
    // arbre visuel : sinon rootVisualElement peut être vide selon l'ordre d'exécution.
    void Start()
    {
        document = GetComponent<UIDocument>();
        if (document == null)
        {
            Debug.LogError("PongMainMenu: aucun UIDocument sur ce GameObject.");
            return;
        }

        VisualElement root = document.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("PongMainMenu: rootVisualElement null (UXML non assigné ?).");
            return;
        }

        BindButton(root, "play-circle", () => StartGame(PongGameMode.Circle));
        BindButton(root, "quit", Quit);

        Label version = root.Q<Label>("version");
        if (version != null)
        {
            version.text = "v" + Application.version;
        }
    }

    void BindButton(VisualElement root, string name, System.Action onClick)
    {
        Button button = root.Q<Button>(name);
        if (button == null)
        {
            Debug.LogWarning("PongMainMenu: bouton introuvable dans l'UXML : " + name);
            return;
        }

        button.clicked += onClick;
    }

    void StartGame(PongGameMode mode)
    {
        PongSession.SelectedMode = mode;
        SceneManager.LoadScene(PongSession.GameSceneName);
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
