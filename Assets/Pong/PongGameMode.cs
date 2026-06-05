using UnityEngine;

/// <summary>
/// Modes de jeu sélectionnables depuis la scène de lancement.
/// </summary>
public enum PongGameMode
{
    /// <summary>Aucun choix : la scène de jeu se comporte comme avant (les deux launchers restent actifs).</summary>
    None = 0,

    /// <summary>Pong classique en réseau (gauche vs droite).</summary>
    Classic = 1,

    /// <summary>Circle Pong multijoueur (4 à 10 joueurs).</summary>
    Circle = 2,
}

/// <summary>
/// Petit conteneur statique qui transporte le choix du joueur entre la scène
/// de lancement et la scène de jeu (les statics survivent à un chargement de scène).
/// </summary>
public static class PongSession
{
    /// <summary>Nom de la scène de jeu (doit être présente dans le Build Settings).</summary>
    public const string GameSceneName = "Pong";

    /// <summary>Nom de la scène de lancement (doit être présente dans le Build Settings).</summary>
    public const string LaunchSceneName = "Launch";

    /// <summary>Mode sélectionné dans le menu. None = lancement direct de la scène de jeu.</summary>
    public static PongGameMode SelectedMode = PongGameMode.None;
}
