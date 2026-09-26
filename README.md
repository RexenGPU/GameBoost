# GameBoost

Assistant local d'analyse et d'optimisation de PC pour jouer, sous Windows.
Tout fonctionne **sans compte, sans connexion, sans envoi de données** : les mesures
sont réelles, lues depuis votre matériel.

## Ce que fait GameBoost

- **Mon PC** : fiche complète du matériel (processeur, mémoire, carte mère, écrans, DirectX, périphériques).
- **GPU** : pilote, âge du pilote, températures et technologies compatibles (DLSS, FSR, ray tracing…).
- **Stockage** : espace libre, état SMART, températures, type de disque, débits observés.
- **Jeux** : détection automatique des jeux installés (Steam, Epic, Ubisoft, Xbox, GOG, Battle.net, Riot, dossiers personnalisés) avec icônes réelles.
- **Profils par jeu** : réglages graphiques enregistrés et réappliqués (141 réglages réversibles testés).
- **Applications** : processus en cours, tri par consommation, fermeture ciblée (processus critiques protégés).
- **Analyse** : 18 vérifications de votre configuration avec explication, impact et solution ; corrections automatiques quand elles sont sûres.
- **Boost** : assistant en 3 étapes (configuration, résumé, exécution). Chaque modification est
  expliquée avant d'être appliquée, sauvegardée, et **totalement réversible** en un clic
  (« Terminer la session et restaurer »).
- **Monitoring** : FPS réels (via les événements de présentation Windows), frametime, CPU, GPU, RAM, températures, overlay pendant le jeu.
- **Historique** : sessions enregistrées localement, comparaison A/B de deux sessions.
- **Rapports** : export HTML lisible dans n'importe quel navigateur (imprimable en PDF).

## Principes respectés

- Aucun gain inventé : si une valeur n'est pas mesurable, l'interface affiche « Non disponible ».
- Rien n'est modifié sans résumé préalable et confirmation.
- Toutes les modifications sont réversibles et sauvegardées dans `%LOCALAPPDATA%\GameBoost\backups`.
- Jamais de toucher à Defender, au pare-feu, aux fichiers système ou aux processus critiques.
- Aucune donnée ne quitte votre PC (la base et les journaux sont locaux).

## Prérequis

- Windows 10 ou 11, 64 bits.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) si vous utilisez
  la version compilée ; rien à installer si vous compilez avec le SDK.

## Compiler

Avec le script :

```powershell
.\build.ps1
```

Ou directement :

```powershell
dotnet build GameBoost.slnx -c Release
```

L'exécutable est produit dans `artifacts\build\GameBoost.App\release\GameBoost.exe`
(ou `src\GameBoost.App\bin\Release\net10.0-windows\GameBoost.exe` selon la méthode).

Pour publier une version autonome dans `publish\` :

```powershell
.\build.ps1 -Publish
```

## Droits administrateur

GameBoost démarre normalement. Sans administrateur, tout fonctionne **sauf** :
- les températures CPU et certains capteurs matériels ;
- l'état SMART complet des disques.

Deux options (page Paramètres) :
- « Relancer en administrateur maintenant » ;
- « Démarrer en administrateur » (l'app relance alors elevated au prochain lancement,
  Windows affichera la demande d'autorisation habituelle).

## Données locales

Tout est stocké dans `%LOCALAPPDATA%\GameBoost` :

| Dossier / fichier | Contenu |
|---|---|
| `gameboost.db` | jeux, profils, sessions, analyses, réglages overlay |
| `settings.json` | préférences de l'application |
| `logs\` | journal détaillé (utile en cas de problème) |
| `exports\` | rapports HTML générés |
| `backups\` | sauvegardes faites avant chaque modification système |
| `cache\` | icônes des jeux et des processus |

Pour tout supprimer : fermer GameBoost puis supprimer ce dossier.

## Limites connues (affichées honnêtement dans l'application)

- Les FPS ne sont mesurables que pour un jeu **en cours d'exécution** suivi par le monitoring ;
  sans jeu suivi, la page affiche « — ».
- L'overlay ne s'affiche pas par-dessus certains jeux en plein écran exclusif.
- Les capteurs matériels dépendent de votre carte mère ; certaines valeurs peuvent être absentes
  même en administrateur.

## Organisation du code

```
src\GameBoost.Core      logique métier (matériel, disques, monitoring, processus,
                        jeux, profils, analyse, boost, historique, rapports, overlay)
src\GameBoost.App       interface WPF (12 pages, thème clair/sombre, overlay)
tools\make-icon.ps1     régénère l'icône de l'application
tools\dbinspect         petit utilitaire d'inspection de la base locale
```

Projet personnel : utilisez, modifiez, compilez librement.
