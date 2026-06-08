# Réseau — protocole, connexion et optimisations (Circle Pong)

Ce document décrit le **protocole de communication personnalisé** du mode Circle Pong,
la **phase de connexion initiale** (synchronisation de l'état) et les **techniques de
réduction de latence et de bande-passante** mises en place.

Fichiers concernés :
- Serveur : [`server/pong-udp-server.js`](../server/pong-udp-server.js) (Node.js, autoritatif)
- Client : [`Assets/Pong/PongCircleUdpClient.cs`](../Assets/Pong/PongCircleUdpClient.cs)
- DTO : [`Assets/Pong/PongCircleNetworkState.cs`](../Assets/Pong/PongCircleNetworkState.cs)
- Application au jeu : [`Assets/Pong/PongCircleGame.cs`](../Assets/Pong/PongCircleGame.cs)

---

## 1. Architecture

Modèle **client-serveur autoritatif** sur **UDP** :
- Le **serveur** simule le jeu (physique de la balle, collisions, secteurs) à **60 Hz** et
  diffuse l'état à **30 Hz**. Il fait foi.
- Les **clients** envoient leurs **entrées** et **affichent** l'état reçu. Ils ne décident
  de rien (anti-triche, cohérence multijoueur).
- UDP est choisi pour la **faible latence** (pas de retransmission bloquante comme TCP) ;
  en contrepartie il faut gérer la perte de paquets (voir §4).

---

## 2. Protocole personnalisé de communication

### 2.1 Enveloppe

Chaque message client est encapsulé dans une **enveloppe JSON** commune :

```json
{
  "seq": 42,
  "deviceId": "a1b2c3...",
  "deviceName": "MacBook Pro de Estelle",
  "displayName": "Dhiki",
  "color": "58e6c8",
  "payload": { "type": "input", "direction": 1 }
}
```

| Champ | Rôle |
|---|---|
| `seq` | numéro de séquence croissant (diagnostic / ordre) |
| `deviceId` | identifiant stable de l'appareil (GUID persistant côté client) |
| `deviceName` | nom machine (auto-détecté) |
| `displayName` | **identité choisie** par le joueur (nom affiché) |
| `color` | **couleur de zone choisie** (hex 6 caractères) |
| `payload` | le message réel (voir 2.2) |

Le serveur extrait `payload` (et tolère un payload envoyé « nu », sans enveloppe).

### 2.2 Types de messages

| Type | Sens | Champs | Rôle |
|---|---|---|---|
| `hello` | client → serveur | `deviceId`, `deviceName` | annonce de présence / maintien de connexion |
| `join` | client → serveur | — | demande à entrer en jeu (lobby) |
| `input` | client → serveur | `direction` ∈ [-1, 1] | déplacement de la raquette |
| `restart` | client → serveur | — | relancer le lobby |
| `replay` | client → serveur | — | voter pour rejouer |
| `lobby` | client → serveur | — | retour au lobby |
| `state` | serveur → client | (voir §3) | **snapshot** de l'état de jeu |

### 2.3 Snapshot `state`

Champs principaux : `localPlayerId`, drapeaux (`lobbyOpen`, `gameStarted`, `gameOver`),
compteurs (`connectedPlayerCount`, `readyPlayerCount`, `playerCount`, `alivePlayerCount`),
`winnerId`, `status`, position/direction de balle (`ballX/Y`, `ballDirX/Y`), et trois
tableaux : `players[]` (id, alive, paddleAngle, name, color), `devices[]` (joueurs en
jeu) et `lobbyDevices[]` (en attente).

---

## 3. Phase de connexion initiale & synchronisation de l'état

```
Client                              Serveur
  | --- hello (deviceId, name) --->  | registerClient()
  | <------ state (snapshot) ------- | sendSnapshot(full)   ← sync immédiate de l'état
  |                                  |
  | --- join --------------------->  | joinGame() : place le joueur
  | <------ state (snapshot) ------- | broadcast à tous
  | ... hello toutes les 1 s ......  | (maintien de présence)
```

1. À la connexion, le client crée son socket UDP et envoie immédiatement un `hello`.
2. Le serveur enregistre l'appareil et **répond aussitôt par un snapshot complet** : le
   nouvel arrivant est **synchronisé sur l'état courant** sans attendre le prochain tick.
3. Le client renvoie un `hello` **toutes les secondes** (maintien de présence : le serveur
   expire un client silencieux après 10 s).
4. `join` est **réémis toutes les 0,5 s pendant 10 s** jusqu'à obtenir un `localPlayerId > 0`
   (UDP n'ayant pas d'accusé de réception, la fiabilité passe par la répétition bornée).
5. La synchronisation de l'identité (nom + couleur) voyage dans **chaque enveloppe** et est
   réappliquée par le serveur puis rediffusée à tous.

---

## 4. Techniques de réduction de latence et de bande-passante

> C'est le cœur du travail réseau. Deux familles : **latence ressentie** (côté client) et
> **bande-passante** (montante côté client, descendante côté serveur).

### 4.1 Interpolation des entités distantes — *latence/fluidité* (client)

**Problème :** les snapshots arrivent à 30 Hz mais le rendu est à 60 fps → mouvements
saccadés si on cale brutalement les positions.

**Solution** ([`PongCircleGame.UpdateNetworkInterpolation`](../Assets/Pong/PongCircleGame.cs)) :
chaque snapshot fournit une **cible** (`PaddleAngleTarget`, position de balle). Entre deux
snapshots, on **interpole** chaque frame vers cette cible (lissage exponentiel
`1 - exp(-k·dt)`). Résultat : 60 fps fluides à partir de 30 Hz réseau.

### 4.2 Dead reckoning de la balle — *latence/fluidité* (client)

La balle se déplace vite et en ligne droite. Le snapshot envoie sa **position ET sa
direction**. Entre deux snapshots, le client **extrapole** la position le long de la
direction reçue (`position += direction × vitesse × dt`), puis **corrige** doucement vers
la dernière position autoritative. La balle bouge donc en continu, sans attendre le serveur.

### 4.3 Prédiction du joueur local — *latence d'input* (client)

**Problème :** si la raquette du joueur attend l'aller-retour serveur (~50–80 ms), elle
paraît « molle ».

**Solution :** l'input local est appliqué **immédiatement** à la raquette du joueur (même
modèle physique que le serveur : `PaddleAngle += dir × vitesse × dt`, bornage identique).
Le client pousse sa direction au jeu **chaque frame** (indépendamment de la cadence
d'envoi réseau). Une **réconciliation** douce ramène en continu la raquette prédite vers la
valeur serveur, corrigeant toute dérive (ex. paquet perdu). La raquette du joueur répond
donc instantanément.

### 4.4 Envoi d'input par delta — *bande-passante montante* (client)

**Avant :** input envoyé à 30 Hz en continu (~30 paquets/s même immobile).

**Après** ([`PongCircleUdpClient.Update`](../Assets/Pong/PongCircleUdpClient.cs)) : on
n'émet que **lorsque la direction change**, plus **2 renvois redondants** espacés pour
survivre à la perte UDP. Comme le serveur **conserve la dernière direction reçue**, le
silence vaut « continue ». En jeu réel (on tient une touche ou on est immobile) on tombe à
**~0 paquet/s** au lieu de 30 → quasi-suppression du trafic montant hors changements.

### 4.5 Snapshots keyframe / delta — *bande-passante descendante* (serveur)

**Avant :** snapshot **complet** à chaque tick (30 Hz), incluant les listes lourdes
`devices`/`lobbyDevices`, les noms et couleurs — qui ne changent quasiment jamais en jeu.

**Après** ([`server/pong-udp-server.js`](../server/pong-udp-server.js)) :
- **Frames légères** (par défaut) : seulement ce qui change vite — balle, angles de
  raquette, drapeaux, compteurs.
- **Keyframes** (frames complètes) : incluent en plus `devices`, `lobbyDevices`, noms,
  couleurs et `status`. Émises **uniquement quand ces données changent** (détecté par une
  *signature*), ou **1×/s** en filet de sécurité (un client ayant perdu une keyframe se
  resynchronise en ≤ 1 s).
- Le client **conserve les dernières valeurs reçues** quand ces champs sont absents (une
  clé absente après désérialisation vaut `null` → on garde l'ancienne liste/identité).

**Mesure réelle** (banc de test UDP local, ~5 joueurs en partie) :

| | Frame complète | Frame légère |
|---|---|---|
| Taille moyenne | ~905 o | ~595 o |

→ **~30 % de bande-passante descendante économisée** dans ce scénario, et l'économie
**croît avec le nombre de joueurs** (les listes omises sont alors plus volumineuses).
En régime stable (peu de changements), seules ~1 keyframe/s subsiste, le reste en léger.

### 4.6 Récapitulatif

| Technique | Type | Côté | Fichier |
|---|---|---|---|
| Interpolation des entités distantes | Latence/fluidité | Client | `PongCircleGame.cs` |
| Dead reckoning de la balle | Latence/fluidité | Client | `PongCircleGame.cs` |
| Prédiction + réconciliation du joueur local | Latence d'input | Client | `PongCircleGame.cs` + `PongCircleUdpClient.cs` |
| Envoi d'input par delta + redondance | Bande-passante ↑ | Client | `PongCircleUdpClient.cs` |
| Snapshots keyframe / delta | Bande-passante ↓ | Serveur | `pong-udp-server.js` |

### 4.7 Pistes complémentaires (non implémentées)

- **Quantification** : encoder angles/positions sur 1–2 octets plutôt qu'en texte JSON.
- **Enveloppe allégée** : n'envoyer `deviceName`/`displayName`/`color` que dans `hello`
  (le serveur les met déjà en cache), pas dans chaque `input`.
- **Format binaire** (au lieu de JSON) pour supprimer le surcoût textuel.

Ces optimisations apporteraient un gain supplémentaire mais imposeraient un protocole
binaire des deux côtés ; elles sont laissées en évolution possible.

---

## 5. Réglages (inspecteur Unity)

Sur `PongCircleGame` (section *Réseau — lissage*) :
`NetworkSmoothing`, `LocalPaddlePrediction`, `PaddleSmoothingSpeed`,
`PaddleReconcileSpeed`, `BallSmoothingSpeed`. Désactiver `NetworkSmoothing` rétablit le
calage direct (comportement d'origine), utile pour comparer.

Sur `PongCircleUdpClient` : `InputSendRate` règle l'espacement des renvois redondants d'input.
