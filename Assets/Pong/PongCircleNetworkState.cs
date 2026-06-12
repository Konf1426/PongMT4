using System;

[Serializable]
public class PongCircleNetworkSnapshot
{
    public string type;
    public int localPlayerId;
    public bool localIsSpectator;
    public bool lobbyOpen;
    public int connectedPlayerCount;
    public int readyPlayerCount;
    public int spectatorCount;
    public int playerCount;
    public int alivePlayerCount;
    public int winnerId;
    public bool gameStarted;
    public bool gameOver;
    public int replayVoteCount;
    public int postGameRemainingSeconds;
    public int startCountdownSeconds;
    public string status;
    public float ballX;
    public float ballY;
    public float ballDirX;
    public float ballDirY;
    public float ballSpeed;
    public bool ballDeadly;
    public bool raceActive;
    public int raceWinnerId;
    public string raceWinnerName;
    public int raceRemainingMs;
    public PongCircleNetworkDeviceState[] devices;
    public PongCircleNetworkDeviceState[] lobbyDevices;
    public PongCircleNetworkPlayerState[] players;
    public PongCircleChatMessageState[] chat;
    public PongCircleHighScoreEntry[] highScores;
}

[Serializable]
public class PongCircleHighScoreEntry
{
    public string name;
    public int score;
}

[Serializable]
public class PongCircleChatMessageState
{
    public int id;
    public string name;
    public string text;
}

[Serializable]
public class PongCircleNetworkDeviceState
{
    public int playerId;
    public string name;
    public bool ready;
    public bool spectator;
    public string color;
    public int lives;
    public int points;
}

[Serializable]
public class PongCircleNetworkPlayerState
{
    public int id;
    public bool alive;
    public float paddleAngle;
    public float input;
    public string name;
    public string color;
    public int lives;
    public int points;
}
