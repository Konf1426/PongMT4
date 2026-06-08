using System;

[Serializable]
public class PongCircleNetworkSnapshot
{
    public string type;
    public int localPlayerId;
    public bool lobbyOpen;
    public int connectedPlayerCount;
    public int readyPlayerCount;
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
    public PongCircleNetworkDeviceState[] devices;
    public PongCircleNetworkDeviceState[] lobbyDevices;
    public PongCircleNetworkPlayerState[] players;
}

[Serializable]
public class PongCircleNetworkDeviceState
{
    public int playerId;
    public string name;
    public bool ready;
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
    public string name;
    public string color;
    public int lives;
    public int points;
}
