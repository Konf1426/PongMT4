# Circle Pong UDP

This is the standalone UDP transport for Circle Pong.

It does not use a Unity networking module. Unity sends and receives UDP packets with `System.Net.Sockets.UdpClient`; the server uses Node's native `dgram`.

## Run on the VPS

```bash
cd /var/www/public/pong.becop.fr
npm install
npm run start:udp
```

With PM2:

```bash
pm2 start pong-udp-server.js --name pong-udp
pm2 save
```

Default ports:

- UDP game: `41234/udp`
- HTTP health: `8082/tcp`

Open the UDP port in the firewall/security group:

```bash
sudo ufw allow 41234/udp
```

Health check:

```bash
curl http://127.0.0.1:8082/health
```

## Unity client

Build a standalone desktop player, not WebGL. The launcher can pass:

- `PONG_UDP_HOST`
- `PONG_UDP_PORT`

The included `launcher/PongCircleLauncher.ps1` sets these variables before starting the game.
