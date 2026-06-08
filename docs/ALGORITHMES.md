# Algorithmes du projet PongMT4

Ce document décrit les algorithmes non triviaux du jeu, leur rôle, leur principe et
leur complexité. Il accompagne les commentaires présents directement dans le code.

Le jeu comporte deux modes : le **Pong classique** (gauche/droite, en réseau UDP) et le
**Circle Pong** (4 à 10 joueurs autour d'un cercle). La plupart des algorithmes
intéressants concernent le Circle Pong, implémenté dans
[`Assets/Pong/PongCircleGame.cs`](../Assets/Pong/PongCircleGame.cs).

Notation : `n` = nombre de joueurs (≤ 10).

---

## 1. Répartition équitable des secteurs

**Fichier :** `PongCircleGame.RedistributeAlivePlayers()`

Chaque joueur vivant défend une part égale du cercle. Quand un joueur est éliminé, on
recalcule la découpe : les secteurs restants s'agrandissent pour se repartager les 360°.

**Principe.** Le cercle (360°) est divisé en `nbVivants` parts égales. Le joueur `k`
(k-ième vivant) reçoit un secteur centré sur `k × sectorSize`.

```
sectorSize = 360 / nbVivants
k = 0
pour chaque joueur vivant :
    centre        = k × sectorSize
    secteur.début = centre − sectorSize / 2
    secteur.fin   = centre + sectorSize / 2
    si le joueur n'a pas encore de raquette : raquette = centre
    sinon : re-borner la raquette dans le nouveau secteur (cf. §4)
    k = k + 1
```

**Complexité :** O(n). Appelée à chaque élimination et à chaque snapshot réseau.

---

## 2. Détection de collision balle / raquette

**Fichier :** `PongCircleGame.UpdateBall()` + `FindPlayerAtAngle()` + `AngleInsideSector()`

La balle se déplace en ligne droite. La collision n'est testée qu'au bord du cercle.

```
déplacer la balle : position += direction × vitesse × Δt
si |position| < rayon :        # encore à l'intérieur
    ne rien faire
sinon :                        # la balle atteint le bord
    angle    = angle de la position de la balle
    défenseur = joueur dont le secteur contient cet angle
    si pas de défenseur vivant : remettre la balle au centre (point perdu)
    sinon :
        écart = |angle − angle_raquette_du_défenseur|
        si écart ≤ demi-largeur_raquette : REBOND (cf. §3)
        sinon : ÉLIMINATION du défenseur
```

On travaille en **coordonnées angulaires** plutôt qu'en collisions physiques : c'est
exact pour une arène circulaire et beaucoup plus léger (pas de moteur physique).

**Sous-routine `AngleInsideSector` :** teste l'appartenance d'un angle à un secteur en
restant robuste au passage par 0°/360°. On ne compare jamais `start <= angle <= end`
(qui casse au wraparound) mais l'écart au centre via `Mathf.DeltaAngle` :

```
centre   = milieu(start, end)
demiTaille = |DeltaAngle(start, end)| / 2
retourner |DeltaAngle(centre, angle)| ≤ demiTaille
```

**Complexité :** O(n) par frame (recherche linéaire du secteur ; n ≤ 10, négligeable).

---

## 3. Rebond avec effet de visée

**Fichier :** `PongCircleGame.BounceOnPaddle()`

La raquette est tangente au cercle ; sa **normale est radiale**. Le rebond combine une
réflexion physique et un effet contrôlé par le joueur.

```
normale   = direction radiale de la raquette
réfléchi  = Reflect(vitesse, normale)          # réflexion miroir classique
offset    = (angle_impact − angle_raquette) / (demi-largeur_raquette)   # ∈ [−1, 1]
tangente  = perpendiculaire à la normale
visée     = normaliser(réfléchi + tangente × offset × PaddleAimInfluence)
si visée ne repart pas assez vers l'intérieur :
    visée = incliner visée vers le centre (Slerp)   # garde-fou anti-rasement
direction = visée
```

- `offset` indique **où** la balle a frappé la raquette (centre = 0, bords = ±1).
- `PaddleAimInfluence` dose l'effet : le joueur oriente la balle selon le point de contact.
- Le garde-fou évite que la balle longe le bord sans jamais rentrer.

**Complexité :** O(1).

---

## 4. Bornage de la raquette dans son secteur

**Fichier :** `PongCircleGame.ClampPaddleAngle()`

La raquette a une largeur propre (`PaddleArcDegrees`). On veut qu'elle reste
**entièrement** dans son secteur, sans déborder sur les voisins.

```
demiSecteur  = |DeltaAngle(start, end)| / 2
demiRaquette = PaddleArcDegrees / 2
amplitude    = max(0, demiSecteur − demiRaquette)   # marge retirée aux deux bords
delta        = clamp(DeltaAngle(centre, angle), −amplitude, +amplitude)
retourner centre + delta
```

L'astuce clé : on **soustrait la demi-largeur de la raquette** à la demi-largeur du
secteur. Le centre de la raquette ne peut donc pas s'approcher du bord à moins d'une
demi-raquette. **Complexité :** O(1).

---

## 5. Recherche gloutonne d'une teinte libre

**Fichier :** `PongCircleGame.FindFreeHue()` + `IsHueFree()`

Quand un joueur change de couleur, on cherche une teinte (hue) suffisamment distincte
de celles déjà prises, pour que les zones restent lisibles.

```
hue = teinte de départ
répéter jusqu'à 32 fois :
    hue = (hue + step) modulo 1
    si hue est distante d'au moins MinHueDistance de TOUTES les autres : retourner hue
retourner (départ + step)        # repli si aucune teinte idéale trouvée
```

Algorithme **glouton borné** : on avance par pas fixe sur le cercle chromatique et on
s'arrête à la première teinte acceptable. La distance entre teintes est mesurée en
angulaire (`DeltaAngle` sur 360°) pour traiter le cercle des couleurs correctement.
La borne (32 essais) garantit la terminaison. **Complexité :** O(n) par essai, O(32·n) au pire.

---

## 6. Génération du maillage de secteur (triangle fan)

**Fichier :** `PongCircleGame.BuildSectorMesh()`

Chaque secteur coloré est une portion de disque générée à la volée (aucun asset 3D).

```
sommet[0] = centre
pour i de 0 à arcSteps :
    sommet[i+1] = direction(lerp(start, end, i / arcSteps)) × rayon
pour i de 0 à arcSteps−1 :
    triangle(centre, sommet[i+2], sommet[i+1])
```

C'est un **éventail de triangles** (triangle fan) : un sommet central relié à une
série de points sur l'arc, formant `arcSteps` triangles. `arcSteps = 16` suffit à
rendre la courbe lisse. **Complexité :** O(arcSteps).

---

## 7. File producteur/consommateur thread-safe (réseau UDP)

**Fichier :** `PongCircleUdpClient.cs` — `ReceiveLoop()` / `DrainMessages()`

L'API Unity n'est **pas thread-safe** : on ne peut toucher au jeu que sur le thread
principal. La réception UDP, elle, est bloquante. On découple les deux avec une file
protégée par un verrou.

```
PRODUCTEUR (thread réseau) :          CONSOMMATEUR (thread principal, chaque frame) :
  boucle :                              boucle :
    data = udp.Receive()  (bloquant)      verrou : msg = file.Dequeue() si non vide
    verrou : file.Enqueue(data)           si file vide : sortir
                                          traiter(msg)        # hors verrou
```

Le verrou n'est tenu que le temps de l'`Enqueue`/`Dequeue`, pas pendant le traitement,
pour ne pas bloquer la réception. C'est un patron **producteur/consommateur** classique.

---

## 8. Compte à rebours de lobby (machine à états)

**Fichier :** `PongCircleGame.UpdateLobbyCountdown()`

Démarrage automatique de la partie : tant que les conditions (assez de joueurs) ne sont
pas réunies, le compte à rebours est annulé. Dès qu'elles le sont, on décompte depuis
`StartCountdownDuration` et on lance la partie à 0. Simple automate à deux états
(attente / décompte) piloté par la condition `CanStart`.

---

## 9. Mode Pong classique (réseau) — pour mémoire

**Fichier :** `PongNetworkGame.cs`

- **Table de joueurs** `Dictionary<endpoint, PongNetworkPlayer>` : recherche O(1) du
  joueur émetteur d'un paquet par sa clé d'endpoint.
- **Purge par délai d'inactivité** : on parcourt la table et on retire les joueurs dont
  le dernier paquet date de plus que le timeout.
- **Agrégation des entrées** : on additionne les directions par équipe pour piloter la
  raquette correspondante.

> Le réseau est géré par l'équipe (branche websocket / serveur UDP) ; ces algorithmes
> sont décrits ici uniquement pour la complétude.

---

## Récapitulatif des complexités

| Algorithme | Fichier / méthode | Complexité |
|---|---|---|
| Répartition des secteurs | `RedistributeAlivePlayers` | O(n) |
| Collision balle/raquette | `UpdateBall` + `AngleInsideSector` | O(n) / frame |
| Rebond avec visée | `BounceOnPaddle` | O(1) |
| Bornage de la raquette | `ClampPaddleAngle` | O(1) |
| Recherche de teinte libre | `FindFreeHue` / `IsHueFree` | O(32·n) au pire |
| Maillage de secteur | `BuildSectorMesh` | O(arcSteps) |
| File réseau thread-safe | `PongCircleUdpClient` | O(1) par message |

`n ≤ 10`, donc toutes les opérations par frame sont en pratique constantes.
