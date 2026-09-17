# TCGCollector

Une app Unity pour ouvrir des boosters de cartes à collectionner virtuelles, construire une
collection à travers plusieurs licences, et échanger ses doublons avec des amis. Projet perso/de
test développé en solo, pas un produit commercial - les visuels des cartes appartiennent à leurs
propriétaires respectifs (Pokémon, Riftbound, Haikyuu, Solo Leveling) et ne servent ici qu'au suivi
personnel d'une collection.

## Ce que ça fait

- **Boosters** - ouvre des packs avec une structure de rareté propre à chaque set (slots garantis,
  paliers d'amélioration, tirage pondéré par rareté) configurée par licence/set via `BoosterConfig`.
- **Classeur** - une grille virtualisée et filtrable de toutes les cartes possédées, navigable par
  licence/set/rareté, avec un aperçu grand format.
- **Boutique**, trois onglets :
  - *Boutique du jour* - une offre d'achat direct par licence, renouvelée chaque jour.
  - *Marketplace* - échanges entre joueurs. Le vendeur fixe son prix, une commission de 10% est
    prélevée sur chaque vente, et n'importe quelle carte possédée (même le dernier exemplaire) peut
    être mise en vente. Entièrement géré côté serveur via Unity Cloud Code, pour qu'aucun client ne
    puisse créditer/débiter malhonnêtement le compte d'un autre joueur.
  - *Rachat* - rachat système instantané et à prix bas (15% sous la valeur de référence d'une
    carte) pour les doublons en trop dont personne ne veut, avec une action groupée "vendre tous
    mes doublons".
- **Quêtes** - quêtes quotidiennes plus un streak de connexion, récompensant boosters bonus et
  monnaie.
- **Profil** - statistiques de complétion de la collection, statut de synchronisation cloud,
  connexion Google optionnelle.

## Stack technique

- Unity 6000.3.22f1, URP.
- Unity Gaming Services : **Authentication** (anonyme, avec liaison optionnelle d'un compte
  Google), **Cloud Save** (données par joueur plus un espace "Custom Data" partagé pour les
  annonces de la marketplace), et **Cloud Code** (transactions de marketplace gérées côté serveur).
- Pas de backend tiers - le palier gratuit d'UGS suffit à l'échelle de ce projet (une poignée de
  joueurs).

## Organisation du projet

```
Assets/Scripts/
  Core/      GameManager - bootstrap de la scène, relie tous les systèmes entre eux
  Data/      Définitions ScriptableObject : CardData, CardRarity, CardSet, License
  Systems/   Logique de jeu/services (ouverture de boosters, collection, boutique, marketplace,
             synchro cloud, quêtes, auth Google) - classes C# simples, pas des MonoBehaviour,
             pour rester faciles à tester
  UI/        Écrans construits au runtime (UIFactory construit la plupart de l'UI en code plutôt
             que via des prefabs préfabriqués)
  Save/      Modèle de sauvegarde locale + lecture/écriture disque
  Editor/    Outillage Editor-only (générateur de base de cartes, aide à l'import d'artworks)
  Debug/     Runners de test manuels
Assets/Data/<Licence>/
  Cards/<Set>/       Un asset CardData par carte, plus un sous-dossier Artwork/ avec l'image de
                     la carte (Artwork/ est ignoré par git - voir plus bas)
  Rarities/          Assets CardRarity (rang, poids de tirage, prix de vente/boutique, par rareté)
  Sets/, BoosterConfig_*.asset
```

## Les images des cartes ne sont pas dans ce repo

Les visuels des cartes se trouvent sous `Assets/Data/<Licence>/Cards/<Set>/Artwork/` et pèsent
plusieurs Go au total, toutes licences confondues - le `.gitignore` les exclut pour que ce repo
reste un historique léger, limité au code et à la config. Les petits assets `CardData` qui
référencent chaque carte (nom, rareté, set, etc.) restent bien suivis ; seules les images en
elles-mêmes sont ignorées. Cloner ce repo seul ne suffira **pas** à obtenir un build jouable - il
faudra réinjecter les artworks toi-même dans ces dossiers.

## Scripts Cloud Code

Les quatre scripts serveur de la Marketplace (`marketplace_getListings`, `marketplace_listCard`,
`marketplace_buyListing`, `marketplace_cancelListing`) sont du JavaScript simple, écrit et publié
entièrement depuis l'outil Cloud Code "Scripts" du Dashboard Unity - ils ne font **pas** partie de
ce projet Unity sur le disque, donc ils ne sont pas non plus dans ce repo. Si tu repars de zéro sur
ce projet, il faudra les recréer dans ton propre projet Unity Gaming Services.

## Mise en place

1. Ouvre le projet dans Unity 6000.3.22f1 (ou une version 6000.3.x plus récente).
2. Lie le projet à un projet Unity Gaming Services (fenêtre Services), avec Authentication, Cloud
   Save et Cloud Code activés - tout gratuit à l'échelle de ce projet.
3. Publie les quatre scripts Cloud Code `marketplace_*` (voir ci-dessus) dans le Dashboard.
4. Optionnel : pour activer "Se connecter avec Google" sur l'écran Profil, crée un client OAuth
   Google Cloud (type "Application de bureau") et renseigne son Client ID dans les champs inspector
   de `GameManager`.
5. Réinjecte les artworks des cartes dans les dossiers `Artwork/` sous
   `Assets/Data/<Licence>/Cards/<Set>/` (voir ci-dessus - non inclus dans ce repo).

## Limitations connues

- La synchro cloud est du best-effort, pas entièrement gérée côté serveur : les changements normaux
  de monnaie/cartes mono-joueur (ouvrir un booster, acheter dans la boutique du jour) n'atteignent
  le cloud qu'au prochain cycle pull+push de l'app (actuellement juste au lancement, plus juste
  après une liaison de compte Google), pas instantanément. Seules les transactions de la
  Marketplace sont validées côté serveur. Acceptable à l'échelle pour laquelle c'est prévu (une
  poignée d'amis en train de tester ensemble) ; il faudrait une réécriture Cloud Code plus large
  pour fermer complètement cette fenêtre.
- La taille d'installation est actuellement grande car les visuels de chaque licence sont embarqués
  directement dans le build ; héberger les artworks à distance / les télécharger à la demande a été
  exploré mais pas encore implémenté.
