# Historique des versions du site

Les nouveautés de la version en ligne, de la plus récente à la plus ancienne.
Chaque nouvelle version s'installe d'elle-même : la mise à jour est
obligatoire.

## 22 octobre 2026 (slogan sur la page de connexion)

- Page de connexion : la phrase « Membres, calendrier, documents, finances,
  votes et messagerie, partagés en toute sécurité. » est remplacée par le
  slogan de l'application, sous son nom. Il se règle dans Paramètres ›
  « Configuration carte de membre » › « Slogan de la carte de membre » (le
  même que sur la carte). Sans slogan, rien n'est affiché à cet endroit.
- Sur téléphone, le slogan s'affiche sous le nom sur l'onglet « Connexion »
  (pas sur « Inscription », qui doit tenir sur un écran).
- Administrateur : lancer une fois le script
  `supabase/migrations/0007_slogan_connexion.sql` dans Supabase pour que le
  slogan s'affiche aussi sur les appareils qui ne se sont jamais connectés.

## 22 octobre 2026 (tableaux personnalisables)

- Paramètres › « Tableaux » (gestionnaires) : créez autant de tableaux que
  vous voulez, avec vos colonnes et vos lignes (ex : « Rubrique » et
  « Montant (Ar) »). Le bouton « Barème des famangiana (modèle) » prépare un
  tableau avec une ligne par type de famangiana : il reste à écrire les
  montants.
- Un tableau peut être lié à un champ de montant : participation remise des
  famangiana, montant des cotisations (paiement et impayés) ou montant des
  mouvements de trésorerie. Le formulaire propose alors la liste des lignes :
  en choisir une remplit le montant (et le motif dans Finances), que l'on
  peut encore changer.
- Famangiana : choisir le type (ex. « Naissance ») propose la ligne du même
  nom dans le barème, et la rubrique choisie s'affiche avec la participation.

## 22 octobre 2026 (présence par QR code)

- Présence et réunions : dans une réunion, « 📷 Scanner les QR codes des
  membres » ouvre la caméra du téléphone. Chaque carte de membre scannée est
  pointée « présent » (le membre est ajouté aux participants s'il n'y était
  pas). La caméra reste ouverte pour la personne suivante ; le nom s'affiche
  en vert, ou en orange s'il était déjà pointé.
- Les cartes déjà imprimées marchent : le membre est reconnu par son
  matricule, son e-mail, son téléphone ou son nom. Les nouvelles cartes
  portent en plus un code unique du membre, qui reste valable même si son
  identifiant change.
- Autorisez la caméra la première fois que le navigateur le demande.

## 21 octobre 2026 (histoire : frise, dates souples, photos)

- Histoire de la communauté : un onglet « Frise » montre tous les chapitres
  avec leur année et leur première photo. Touchez un chapitre pour le lire.
- La date d'un chapitre peut être un jour exact, un mois (« Mars 2001 ») ou
  seulement une année (« 1993 ») : pratique pour les débuts de la communauté.
- Un chapitre peut avoir jusqu'à 10 photos, chacune avec sa légende. En
  lecture, touchez une vignette ou glissez sur la photo pour passer à la
  suivante. L'ancienne image d'un chapitre devient sa première photo.

## 21 octobre 2026 (messagerie plus claire, ajout de membres)

- Nouvelle présentation de la messagerie : en-tête compact (retour, photo,
  nom et participants) avec un menu ⋮, bulles colorées pour vos messages,
  photo et nom de l'auteur dans les groupes, séparateurs « Aujourd'hui »,
  « Hier », heure dans chaque bulle et bouton d'envoi ➤. La conversation
  s'ouvre sur ses derniers messages.
- Touchez un de vos messages pour le modifier ou le supprimer : les boutons
  ne recouvrent plus le texte.
- Liste des conversations : photo de la personne ou icône du groupe, heure
  du dernier message, « Vous : … » pour vos messages, non lus en gras.
- ⋮ > « Ajouter des membres » ajoute des personnes à une conversation. Elles
  reçoivent les messages à partir de leur arrivée, sans l'historique. Une
  conversation de groupe et ministère suit toujours les membres du groupe.

## 21 octobre 2026 (âges, conversations, exports et badge)

- Âges : le calendrier range chaque anniversaire à sa prochaine date, avec
  l'âge atteint ce jour-là (Ange, née le 01/12/1998 : « 28 ans » le
  01/12/2026, comme dans la vue du mois). La fiche, le calendrier et les
  rappels utilisent le même calcul ; un né le 29 février fête ses ans le
  28 février les autres années.
- Messagerie : une conversation à trois personnes ou plus peut recevoir un
  nom ou en changer (✏️ en haut), même si elle a été commencée sans nom.
  Celle d'un groupe et ministère garde le nom du groupe.
- Export Excel des membres : en plus des champs personnalisés, il donne
  l'âge, la date de décès, le responsable légal, la famille et les groupes.
  Les champs personnalisés accentués (« Baptisé »…) se réimportent bien.
- Carte de membre : l'export PDF donne bien 2 pages (recto, verso) au lieu
  de 4, au format badge standard 85,6 × 54 mm (format carte bancaire, celui
  des porte-badges). Nouveau : « Planche A4 à découper », recto et verso
  côte à côte avec traits de coupe et pliage. Une taille personnalisée reste
  possible dans Paramètres.

## 20 octobre 2026 (discussions de groupe et doublons)

- Messagerie : « Nouveau groupe » crée une discussion nommée avec plusieurs
  membres. « Écrire au groupe » d'un groupe et ministère écrit toujours dans la
  même discussion, au nom du groupe.
- Une conversation peut être supprimée (🗑 en haut) : elle quitte votre liste
  et revient seulement si quelqu'un y écrit de nouveau. Un administrateur peut
  aussi la supprimer pour tout le monde.
- Intentions de prière : « Seulement à mes groupes » confie l'intention à un
  ou plusieurs de vos groupes ; seuls leurs membres la voient.
- Ajouts en double refusés partout : un événement, une réunion, une tâche, un
  matériel, un vote, un service, une opération de trésorerie… identique à un
  élément existant n'est plus ajouté, avec un message qui le signale.
- Calendrier : un bandeau signale les événements en double déjà présents et
  « Supprimer les doublons » ne garde que le premier.
- Correction : quand un identifiant change, les groupes et les intentions de
  prière suivent.

## 20 octobre 2026 (vie spirituelle)

- **Planning des services** (menu « … ») : qui préside, prêche, lit, chante
  ou accueille à chaque culte. Chacun voit « Mes prochains services » et
  répond « Je confirme » ou « Je ne peux pas » (un remplaçant est alors
  demandé aux responsables). « Copier à la semaine suivante » reprend le même
  culte. Les tâches se règlent dans Paramètres › Listes de choix.
- **Intentions de prière** : une intention se partage avec les membres
  (« Je prie aussi ») ou se confie seulement aux responsables. Elle peut être
  anonyme, et marquée « Exaucée » avec un témoignage.
- **Groupes et ministères** : chorale, jeunesse, école du dimanche…, avec
  responsables et membres, « Écrire au groupe », « Nouvelle rencontre » et la
  présence moyenne.
- Quatre nouveaux droits dans « Rôles et permissions », groupe « Vie
  spirituelle ». Par défaut, les membres confient des intentions ; les
  gestionnaires préparent le planning, gèrent les groupes et lisent les
  intentions privées.

## 20 octobre 2026 (identifiant et matricule au format)

- Fiche d'un membre : l'« Identifiant » est un champ par défaut, dans la
  partie « Compte », et n'apparaît plus parmi les champs personnalisés.
- Matricule : quand un format est réglé (ex. M-{NNNN}), la partie fixe
  « M- » est figée et on ne remplit que les cases, sans dépasser leur
  longueur (4 chiffres pour {NNNN}). « 7 » devient « 0007 ». Un ancien
  matricule hors format reste accepté tant qu'on ne le change pas.
- L'icône de « Quitter l'aperçu » est maintenant toujours visible.

## 20 octobre 2026 (accès selon le rôle)

- Les menus, les pages, l'accueil et le guide suivent la grille « Rôles et
  permissions » : un menu dont le rôle n'a pas le droit (par exemple
  « Gestion documentaire » pour un membre) n'apparaît plus, ni sa fiche dans
  le guide ni dans la visite de découverte.
- Dans « Rôles et permissions », une case grisée signale un droit qui ne
  s'applique pas à ce rôle.
- L'export et l'import dans Paramètres suivent les droits « Exporter les
  données » et « Importer des données ».
- « Quitter l'aperçu » a de nouveau son icône sur téléphone.
- À l'accueil, la carte « À traiter » propose « Tous les rappels (n) »
  seulement s'il y a des rappels (les doublons se traitent dans
  Utilisateurs).

## 20 octobre 2026 (téléphone +261 et mots de passe)

- Téléphone : « +261 » est fixe, on saisit seulement les 9 chiffres du
  numéro (ex. 341234567), à l'inscription comme dans la fiche d'un membre.
  Un numéro collé en « 034 12 345 67 » ou « +261 34 12 345 67 » est remis
  en forme tout seul.
- Chaque champ mot de passe a son propre œil pour afficher ou masquer ce qui
  est saisi, y compris « Confirmer le mot de passe ».

## 20 octobre 2026 (inscription sans e-mail et avec Google)

- Inscription sans adresse e-mail : cochez « Je n'ai pas d'adresse e-mail »,
  donnez votre téléphone. Après la validation, l'administrateur vous donne
  votre identifiant pour vous connecter. Demande l'exécution du script
  `0005` (voir le guide de mise en ligne).
- « Mot de passe oublié ? » accepte aussi l'identifiant.
- L'adresse e-mail saisie à l'inscription doit avoir une forme valide
  (nom@domaine.extension).
- « Continuer avec Google » à la connexion et à l'inscription, dès que
  Google est activé dans Supabase (guide de mise en ligne).
- Un compte en attente peut compléter ou corriger son prénom, son nom et son
  téléphone.

## 18 octobre 2026 (famadihana retiré)

- Le type « Famadihana » est retiré des types de famangiana et situations,
  définitivement : il n'est plus proposé et ne peut pas être remis.

## 18 octobre 2026 (listes de choix : supprimer et traduire)

- « Listes de choix » : le bouton 🗑 Supprimer est aussi sur les choix
  d'origine (types, liens de famille, priorités, catégories, états). Un choix
  encore utilisé ne se supprime pas. Les choix d'origine supprimés restent
  listés en bas, avec « ↺ » pour les remettre.
- Chaque choix ajouté ou modifié a un libellé en français, et si on le
  souhaite en anglais et en malgache. Chacun voit le libellé dans sa langue ;
  sans traduction, le texte français s'affiche.

## 18 octobre 2026 (listes de choix)

- Nouveau dans Paramètres : « Listes de choix » (gestionnaires et
  administrateurs). Les listes proposées dans les formulaires se règlent
  sans attendre une nouvelle version : types de famangiana et situations,
  liens de famille, types d'événement, priorités, catégories de documents,
  états du matériel.
- Pour chaque liste : ajouter un choix, le renommer, changer son icône, le
  monter ou le descendre, le masquer ou le supprimer. Un choix masqué n'est
  plus proposé, mais les fiches qui l'utilisent le gardent. Un choix encore
  utilisé ne se supprime pas : on le masque.
- Un lien de famille ajouté indique comment il se lit depuis l'autre fiche
  (le même lien, ou un autre lien).
- Les statuts (tâches, suivis, cotisations, présence) et les mouvements de
  trésorerie se renomment et se réordonnent ; l'application s'appuie sur eux,
  on ne peut donc pas en ajouter ni en retirer.
- « Rétablir les choix d'origine » remet une liste comme au départ, en gardant
  les choix ajoutés.

## 18 octobre 2026 (famangiana)

- Le menu « Situations particulières » s'appelle maintenant « Famangiana et
  situations ». Les suivis existants et leurs 4 types sont conservés.
- Nouveaux types de suivi : naissance, baptême, mariage, maladie ou
  hospitalisation, accident ou sinistre, décès, famadihana.
- Un suivi indique la personne concernée quand ce n'est pas le membre
  lui-même (« sa mère »), et montre la famille du membre à prévenir ou visiter.
- La visite (date, heure, lieu, membres qui y vont) s'ajoute au Calendrier de
  ces membres.
- La participation remise (fanomezana) peut être enregistrée comme dépense
  dans Finances.
- Décès d'un membre : depuis le suivi ou sa fiche, « Enregistrer le décès ».
  La fiche devient inactive, sans cotisation attendue ni rappel
  d'anniversaire. « Annuler le décès » corrige une erreur.
- Naissance : « Créer la fiche du bébé » crée sa fiche, déjà reliée à ses
  parents.

## 18 octobre 2026 (tous les liens de famille)

- Section « Famille » d'une fiche : en plus des parents, enfants, conjoint(e)
  et frères et sœurs, on peut relier un gendre ou une belle-fille, des
  beaux-parents, un beau-frère ou une belle-sœur, des grands-parents et
  petits-enfants, arrière-grands-parents, oncles et tantes, neveux et nièces,
  cousins, demi-frères et demi-sœurs, le conjoint d'un parent (famille
  recomposée), parrain et marraine, tuteur ou tutrice, et « autre membre de la
  famille ». Chaque lien s'enregistre des deux côtés (gendre ⇔ beau-père).
- La fiche déduit aussi le reste de la famille : le frère du père apparaît
  comme oncle, ses enfants comme cousins, le conjoint d'une fille comme gendre.
- Un tuteur relié à la fiche d'un mineur compte comme responsable légal.

## 18 octobre 2026 (nouvelle adresse)

- Le site est maintenant à l'adresse **https://safiaf-mg.github.io/**
  (organisation GitHub « safiaf-mg »). L'ancienne adresse ne fonctionne plus :
  réinstallez l'application depuis la nouvelle et reconnectez-vous. Comptes et
  données sont conservés.
- La veille quotidienne ne s'arrête plus en erreur quand aucun ticket n'est
  ouvert.

## 17 octobre 2026 (nouveau logo SAFIAF 1993)

- Nouvelle version du logo SAFIAF 1993, plus nette, découpée en cercle :
  logo de l'application, icônes de l'écran d'accueil et icône d'onglet.

## 17 octobre 2026 (logo net)

- Le logo livré avec le site est maintenant en haute résolution (1024 px) :
  il reste net sur les écrans de téléphone, même en grand dans « Histoire ».
  Le fichier est aussi plus léger qu'avant.

## 17 octobre 2026 (logo par défaut)

- Sans logo dans Paramètres › Apparence, l'application affiche partout le logo
  SAFIAF livré avec le site (en-tête, histoire, carte de membre, aperçu dans
  Apparence) au lieu des initiales « SA ».
- Retirer le logo dans Apparence efface aussi l'ancien logo gardé sur
  l'appareil : l'écran de chargement repasse au logo par défaut.

## 17 octobre 2026 (mise à jour obligatoire)

- La mise à jour du site ne peut plus être ignorée. À l'ouverture, la
  nouvelle version s'installe aussitôt. Si l'application est déjà ouverte, un
  écran « Nouvelle version disponible » la bloque : **Mettre à jour**, ou mise
  à jour automatique après 15 secondes.
- Le site cherche aussi une nouvelle version quand on revient sur
  l'application installée.

## 17 octobre 2026 (aperçu par rôle)

- Un administrateur peut voir l'application comme un Gestionnaire ou un
  Utilisateur : menu du compte (photo en haut à droite) › « Voir
  l'application en tant que ». Menus, pages et boutons suivent ce rôle et la
  matrice Rôles et permissions. Un bandeau rappelle l'aperçu et permet d'en
  sortir ; il s'arrête aussi au rechargement ou à la déconnexion. Ce qui est
  fait pendant l'aperçu est réellement enregistré.

## 16 octobre 2026 (identifiants au prénom, images, chargement)

- Les identifiants sont mis au prénom automatiquement (« lucas », puis
  « lucas1 », « lucas2 » pour les homonymes) dès qu'un administrateur ouvre
  l'application : plus besoin de passer par le bandeau d'Utilisateurs.
- Toute image de plus de 5 Mo est réduite automatiquement avant l'envoi, y
  compris les photos sans type reconnu ; une image encore trop lourde est
  réduite davantage au lieu d'être refusée.
- Au démarrage, un seul écran de chargement : le logo et le nom de
  l'application, jusqu'à l'ouverture de l'application.
- Le logo SAFIAF est livré avec le site : il sert d'icône, d'écran de
  démarrage et de logo de l'écran de connexion tant qu'aucun autre logo n'est
  choisi dans Paramètres › Apparence.
- Écran de connexion : le champ identifiant affiche « Votre identifiant ou
  votre e-mail ».

## 16 octobre 2026 (icône de l'application installée)

- Le nom et le logo réglés dans Paramètres › Apparence deviennent le nom et
  l'icône de l'application installée sur le téléphone ou l'ordinateur, et le
  titre de l'onglet. Sous l'icône, un nom long est raccourci à son premier mot
  (« Avotra »). Après un changement, le site est republié automatiquement dans
  l'heure ; une application déjà installée se met à jour quand Chrome vérifie
  le site (cela peut prendre quelques jours), ou en la réinstallant.
- Champ mot de passe : un seul œil pour afficher le mot de passe (Edge en
  ajoutait un second).
- L'icône et le nom sont pris à jour même sur un téléphone qui gardait
  l'ancienne version du site en mémoire.

## 16 octobre 2026 (identifiants, connexion, geste retour)

**Identifiants par prénom**
- Un nouveau membre reçoit son prénom comme identifiant : « lucas »,
  « jean.pierre » pour deux prénoms. Les homonymes reçoivent un numéro dans
  l'ordre d'inscription : lucas, lucas1, lucas2.
- Utilisateurs : un bandeau propose aux administrateurs de mettre les
  identifiants existants à cette règle. La liste « ancien → nouveau » est
  affichée avant de confirmer. Cotisations, prêts, tâches, messages, réunions,
  votes, documents, événements et liens de famille suivent le nouvel
  identifiant. L'historique d'activité ne change pas.

**Connexion par identifiant**
- L'écran de connexion accepte l'identifiant ou l'e-mail. Après 5 essais faux
  en 15 minutes, l'identifiant est bloqué 15 minutes ; l'e-mail reste
  utilisable. Nécessite le script `0004_connexion_identifiant.sql`.

**Geste retour d'Android**
- Dans l'application installée, le geste retour (ou le bouton Précédent du
  navigateur) ferme d'abord la fenêtre ou le menu ouvert, puis revient à la
  page précédente. Depuis l'accueil, il quitte l'application.

## 16 octobre 2026 (nom de l'application, ordre des menus)

**Nom de l'application partout**
- Dès qu'un nom est choisi (Paramètres › Apparence), les textes qui parlaient
  de « la communauté » citent ce nom : « Tableau de bord d'Avotra… »,
  « Histoire d'Avotra… », « Logo d'Avotra… », aide, guide, écran de
  chargement… dans les trois langues.
- Le titre par défaut de l'histoire (« Histoire de notre communauté ») devient
  aussi « Histoire de [nom] ».
- L'historique d'activité déjà enregistré ne change pas.

**Ordre des menus par défaut**
- D'abord ce que les membres utilisent chaque jour (Messagerie, Tâches,
  Rappels, Votes, Réunions, Documents, Journal, Histoire, Carte de membre),
  puis la gestion (Finances, Matériel, Situations), le Guide, et enfin les
  outils d'administration (Comptes en ligne, Rôles, Archives, Gestion de
  données, Capacité du serveur).
- Un ordre déjà réglé dans Paramètres › Ordre du menu est conservé. Pour
  adopter le nouvel ordre : « Réinitialiser l'ordre d'origine ».
- « Gestion de données » apparaît maintenant dans Paramètres › Ordre du menu
  (administrateurs) : on peut la déplacer ou la masquer comme les autres.

## 16 octobre 2026 (mot de passe oublié)

**Mot de passe oublié**
- Sur l'écran de connexion, « Mot de passe oublié ? » permet de saisir son
  e-mail et d'envoyer la demande à l'administrateur.
- L'administrateur la voit dans Comptes en ligne › « Mots de passe oubliés »,
  avec un bandeau, un badge et un ticket GitHub quotidien. Il donne un mot de
  passe provisoire (bouton « Générer ») et le transmet au membre.
- À la connexion suivante, le membre doit choisir son propre mot de passe
  avant d'utiliser l'application.
- Le bandeau « Espace serveur à surveiller » ne s'affiche plus quand aucune
  alerte n'est ouverte.
- Nécessite le script `supabase/migrations/0003_mot_de_passe_oublie.sql`
  (voir `docs/MISE-EN-LIGNE.md`).

## 16 octobre 2026 (accueil)

**Page d'accueil**
- Les cartes repliables Finances, Tâches & rappels et Matériel & médias
  ont disparu de l'accueil (ces informations restent dans leurs menus).
- Nouvelle carte « Pour vous » : messages non lus, votes qui attendent votre
  voix, vos tâches (et celles en retard), événements du jour et votre
  cotisation à régler. Chaque case ouvre le menu correspondant.
- « Les 7 prochains jours » : les événements du calendrier de la semaine.
- « À traiter » : les rappels les plus urgents et les fiches en double.
- Une case n'apparaît que si le menu est accessible au membre et n'est pas
  masqué dans les paramètres.

## 16 octobre 2026

**Écran de connexion**
- Le nom et le logo de la communauté (Paramètres › Apparence) remplacent le
  titre « Gestion communautaire » et « Votre communauté, en ligne ».
- Logo plus grand ; l'inscription tient sur un écran, sur ordinateur comme
  sur téléphone.
- Pour que le nom et le logo s'affichent aussi sur un appareil qui ne s'est
  jamais connecté, l'administrateur exécute une fois
  `supabase/migrations/0002_marque_publique.sql` (voir `docs/MISE-EN-LIGNE.md`).

**Menu**
- « Histoire de la communauté » porte le nom de l'application, par exemple
  « Histoire de Fikambanana Fanantenana » (dès que le nom est réglé dans
  Paramètres).

**Mini-guide**
- La dernière étape montre le menu « Guide utilisateur », quelle que soit sa
  place dans le menu (Paramètres seulement s'il a été masqué).

## 15 octobre 2026 (langues)

**Trois langues : français, anglais, malgache**
- Chacun choisit sa langue dans Paramètres › Apparence › « Ma langue », ou
  directement sur l'écran de connexion. Le choix est personnel : il ne change
  rien pour les autres membres, même pour l'administrateur.
- Tout l'écran est traduit : menus, pages, boutons, messages, aide et carte
  de membre. Le français reste la langue par défaut.
- Les données ne sont pas traduites : noms, motifs, historique d'activité et
  fichiers Excel restent tels qu'ils ont été saisis.
- Les traductions se corrigent dans de simples fichiers (voir
  `docs/MISE-EN-LIGNE.md`, « Corriger une traduction »).

## 15 octobre 2026 (soir)

**Finances**
- Les impayés ne sont plus créés automatiquement pour tous les membres :
  « ＋ Impayés » choisit les membres, le montant, le motif et la période.
- « Encaisser » règle une cotisation en attente, avec l'entrée de trésorerie
  en option ; une cotisation peut être modifiée.

**Membres**
- Doublons : l'application prévient avant d'ajouter une fiche qui existe
  peut-être déjà, propose de relier une inscription à la fiche existante, et
  la liste des utilisateurs signale les fiches en double à fusionner.
- Famille : chaque fiche peut relier parents, enfants, conjoint(e), frères et
  sœurs ; grands-parents et petits-enfants s'affichent tout seuls.
- Moins de 18 ans : badge « Mineur », responsable légal demandé (ou parent
  relié), et indiqué sur la carte de membre.
- Format du matricule réglable (ex. M-{NNNN}) dans Paramètres › Carte de
  membre, attribué automatiquement aux nouveaux membres.
- « Gestion de données » est ouverte aux administrateurs.

**Affichage**
- Thème clair par défaut ; chacun choisit le sien, même l'administrateur.
- Vingt couleurs d'accent, plus une couleur libre.
- « Administrateur » n'est plus coupé dans le tableau des rôles.

**Corrections**
- L'accueil ne plante plus quand un membre est né un 29 février.
- Les filtres du calendrier s'appliquent de nouveau ; l'âge affiché est celui
  fêté ce jour-là.
- Modifier un membre depuis « Gestion de données » n'efface plus sa photo, sa
  signature ni ses champs.
- Restaurer un membre archivé lui rend l'accès à son compte en ligne.
- Le journal d'activité ne s'efface plus quand un simple membre se connecte,
  et affiche l'heure.
- Un vote dont la date de clôture est passée apparaît clos ; corriger le texte
  d'une option garde ses voix.
- Un membre archivé ne bloque plus la modification des tâches, réunions et
  conversations où il figurait.
- Les mots de passe et la zone de réinitialisation hérités de l'application
  hors ligne ont été retirés : les mots de passe se gèrent dans « Comptes en
  ligne ».

## 15 octobre 2026 (suite)

**Mini-guide de démarrage**
- Sur ordinateur, le bouton d'aide ⓘ est de nouveau affiché en haut à
  droite, à côté de l'avatar.
- Sur ordinateur, le guide présente le menu de gauche au lieu de la barre
  du bas et du menu « ⋮ », qui n'existent que sur téléphone.
- « Passer » est toujours en haut à droite, et « Suivant » toujours à
  droite des boutons.

## 15 octobre 2026

**Mise à jour du site sans fermer les onglets**
- Un bandeau « Nouvelle version disponible » apparaît dès qu'une mise à jour
  est publiée ; le bouton **Mettre à jour** recharge le site.

**Fonctionnalités des Lots 11 à 17 de l'application hors ligne**
- Carte de membre premium : faces avant et arrière, slogan et citation
  réglables, signature du membre, export PDF.
- Signature électronique dessinée ou importée en image.
- Tableau de bord enrichi : finances, tâches et rappels, réunions et
  présence, matériel et médias.
- Mini-guide illustré au premier usage, aide ⓘ sur chaque page et guide
  utilisateur complet.
- Choix personnels gardés dans le navigateur de chacun : thème clair ou
  sombre, conseils au premier usage.
- Ordre du menu par glisser-déposer (gestionnaires).
- Documents en trois mises en page (téléphone, tablette, ordinateur),
  recherche sans accents, filtres rapides, sélection multiple et corbeille.
- Paramètres accessibles depuis le menu latéral sur ordinateur.

**Entretien automatique**
- Ticket GitHub quand des comptes attendent une validation.
- Ticket « Vérification trimestrielle » les 1er janvier, avril, juillet et
  octobre, avec une liste de contrôle.
- Sauvegardes hebdomadaires chiffrées par mot de passe (7-Zip).

## Première mise en ligne

- Site sur GitHub Pages, comptes et données sur Supabase, gratuitement.
- Comptes personnels validés par un administrateur, rôles et permissions
  appliqués par le serveur.
- Fonctionnalités de l'application hors ligne jusqu'au Lot 10.
- Alerte de saturation de l'espace gratuit, veille quotidienne de Supabase
  et sauvegarde hebdomadaire.
