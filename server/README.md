# Pong WebSocket server

This server hosts the Unity WebGL build from `../build-web` and exposes the realtime game socket on `/ws`.

## Local run

```powershell
cd server
npm install
npm start
```

Then open `http://localhost:8080`.

## Production

Run the Node process behind HTTPS and proxy `pong.becop.fr/ws` to the same process with WebSocket upgrade enabled.

Example nginx location:

```nginx
location /ws {
  proxy_pass http://127.0.0.1:8080/ws;
  proxy_http_version 1.1;
  proxy_set_header Upgrade $http_upgrade;
  proxy_set_header Connection "upgrade";
  proxy_set_header Host $host;
}

location / {
  proxy_pass http://127.0.0.1:8080;
  proxy_set_header Host $host;
}
```

Example Apache virtual host config:

```apache
ProxyPreserveHost On
ProxyPass "/ws" "ws://127.0.0.1:8080/ws"
ProxyPassReverse "/ws" "ws://127.0.0.1:8080/ws"
ProxyPass "/health" "http://127.0.0.1:8080/health"
ProxyPassReverse "/health" "http://127.0.0.1:8080/health"
```

Enable the required modules:

```bash
sudo a2enmod proxy proxy_http proxy_wstunnel
sudo systemctl reload apache2
```

After deploying, verify:

```bash
curl http://127.0.0.1:8080/health
curl http://pong.becop.fr/health
```
