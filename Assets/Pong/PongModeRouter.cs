using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Applique le mode choisi dans la scène de lancement à la scène de jeu, par code.
///
/// La scène de jeu (Pong.unity) contient les deux modes (réseau classique + circle).
/// Plutôt que d'éditer la scène, ce bootstrap désactive le mode non sélectionné
/// dès que la scène de jeu est chargée. Si aucun mode n'est choisi (lancement direct
/// de Pong.unity), rien n'est touché : comportement historique préservé.
/// </summary>
public static class PongModeRouter
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        // Évite les doubles abonnements si le domaine n'est pas rechargé entre deux Play.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        if (scene.name != PongSession.GameSceneName)
        {
            return;
        }

        ApplyMode(PongSession.SelectedMode);
    }

    static void ApplyMode(PongGameMode mode)
    {
        switch (mode)
        {
            case PongGameMode.Classic:
                SetActive<PongNetworkLauncher>(true);
                SetActive<PongNetworkGame>(true);
                SetActive<PongCircleLauncher>(false);
                SetActive<PongCircleGame>(false);
                break;

            case PongGameMode.Circle:
                SetActive<PongNetworkLauncher>(false);
                SetActive<PongNetworkGame>(false);
                SetActive<PongCircleLauncher>(true);
                SetActive<PongCircleGame>(true);
                break;

            case PongGameMode.None:
            default:
                // Lancement direct : on ne touche à rien.
                break;
        }
    }

    static void SetActive<T>(bool active) where T : Behaviour
    {
        T component = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        if (component != null)
        {
            component.enabled = active;
        }
    }
}
