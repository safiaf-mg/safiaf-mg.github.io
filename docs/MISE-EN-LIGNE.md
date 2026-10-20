# Mise en ligne (Supabase + GitHub Pages)

Ce guide met l'application en ligne gratuitement, en une heure environ, sans
écrire de code et sans carte bancaire. Tout se fait depuis deux sites :
**Supabase** (comptes, données, fichiers) et **GitHub** (le site web,
la surveillance et les sauvegardes automatiques).

| Service | Rôle | Offre gratuite |
|---|---|---|
| Supabase | Comptes, base de données, photos et documents | 500 Mo de base, 1 Go de fichiers, 5 Go de transfert par mois |
| GitHub Pages | Héberge le site | 100 Go de transfert par mois, certificat HTTPS inclus (dépôt public) |
| GitHub Actions | Publie le site, surveille Supabase, sauvegarde | Gratuit et illimité pour un dépôt public |

---

## 1. Créer le projet Supabase

1. Créez un compte sur <https://supabase.com> puis **New project**.
   - Choisissez la région la plus proche des membres (par ex. *West EU (Paris)*
     ou *Central EU (Frankfurt)*).
   - Notez le **mot de passe de la base** : il servira pour les sauvegardes.
2. Ouvrez **SQL Editor > New query**, collez tout le contenu de
   [`supabase/migrations/0001_initialisation.sql`](../supabase/migrations/0001_initialisation.sql)
   puis cliquez **Run**. Le script peut être relancé sans danger (après une
   mise à jour de l'application par exemple).
3. **Désactivez la confirmation par e-mail** (la validation est faite par
   l'administrateur, et l'offre gratuite n'envoie que quelques e-mails par
   heure) : **Authentication > Sign In / Providers > Email**, décochez
   **Confirm email**, puis **Save**.
4. Récupérez les informations de connexion :
   - **Project Settings > API** (ou *API Keys*) :
     - l'**URL du projet** (`https://xxxx.supabase.co`) ;
     - la clé **anon** (publique, elle ira dans le site) ;
     - la clé **service_role** (secrète, elle ne sert qu'à GitHub).

     Si votre projet propose les nouvelles clés, utilisez l'onglet
     *Legacy API keys* pour obtenir `anon` et `service_role`.
   - **Connect** (en haut de la page) **> Connection string > Session pooler** :
     copiez l'adresse `postgresql://postgres.xxxx:[YOUR-PASSWORD]@…pooler.supabase.com:5432/postgres`
     et remplacez `[YOUR-PASSWORD]` par le mot de passe noté à l'étape 1.
     Prenez bien le *Session pooler* : la connexion directe ne fonctionne pas
     depuis GitHub.

> La clé **service_role** et l'adresse de la base donnent un accès complet :
> ne les mettez jamais dans le code ni dans un message, uniquement dans les
> secrets GitHub (étape 3).

## 2. Activer GitHub Pages

1. Dans le dépôt GitHub : **Settings > Pages**.
2. Sous **Build and deployment**, choisissez **Source : GitHub Actions**.
   Il n'y a rien d'autre à régler.
3. L'adresse du site est `https://safiaf-mg.github.io/` : le dépôt s'appelle
   `safiaf-mg.github.io` dans l'organisation `safiaf-mg`, ce qui place le site
   à la racine (adresse affichée en haut de cette même page après la
   publication). Un dépôt au nom ordinaire serait servi depuis
   `https://<compte>.github.io/<dépôt>/`. Un nom de domaine
   personnel peut être ajouté plus tard dans **Custom domain**, gratuitement.

GitHub Pages est gratuit pour un dépôt public et ne demande pas de carte
bancaire. Le code du site est donc visible de tous, ce qui ne pose pas de
problème : les données des membres restent dans Supabase, protégées par les
comptes et les droits.

## 3. Renseigner GitHub

Dans le dépôt GitHub : **Settings > Secrets and variables > Actions**.

Onglet **Variables** :

| Nom | Valeur |
|---|---|
| `SUPABASE_URL` | URL du projet Supabase |
| `SUPABASE_ANON_KEY` | clé `anon` |

Onglet **Secrets** :

| Nom | Valeur |
|---|---|
| `SUPABASE_SERVICE_ROLE_KEY` | clé `service_role` |
| `SUPABASE_DB_URL` | adresse *Session pooler* avec le mot de passe |
| `SAUVEGARDE_MOT_DE_PASSE` | un mot de passe de votre choix (12 caractères minimum) pour chiffrer les sauvegardes |

Le dépôt est public : sans `SAUVEGARDE_MOT_DE_PASSE`, la tâche **Sauvegarde**
refuse de s'exécuter plutôt que de publier des données lisibles par tous.
**Notez ce mot de passe en lieu sûr** : sans lui, les sauvegardes ne peuvent
pas être ouvertes, et personne (pas même GitHub) ne peut le retrouver.

Tant que `SUPABASE_URL` n'est pas renseignée, les tâches automatiques
attendent sans erreur.

## 4. Publier

- Le site est publié automatiquement à chaque modification de la branche
  `main` (tâche **Déploiement du site**). Pour le publier tout de suite :
  **Actions > Déploiement du site > Run workflow**.
- Lancez aussi une fois à la main **Actions > Veille Supabase** et
  **Actions > Sauvegarde** pour vérifier que les secrets sont bons.

### Après une mise à jour du site

Le navigateur garde l'application en cache pour qu'elle s'ouvre vite. La
mise à jour est obligatoire et ne peut pas être ignorée :

- si la nouvelle version est trouvée pendant l'écran de chargement, elle
  s'installe aussitôt ;
- si l'application est déjà ouverte, un écran **« Nouvelle version
  disponible »** bloque l'application : un clic sur **Mettre à jour**
  recharge le site, sinon la mise à jour se fait seule après 15 secondes.

Le site vérifie s'il y a du nouveau à chaque ouverture, quand on revient sur
l'application, puis toutes les 30 minutes.

Si un membre a encore une version d'avant cette règle, il suffit de fermer
tous les onglets du site, ou l'application installée sur le téléphone, puis
de le rouvrir.

### Mises à jour de la base (scripts SQL)

Certaines mises à jour demandent d'exécuter une fois un petit script dans
Supabase (**SQL Editor > New query**, coller le contenu, **Run**). Chaque
script peut être relancé sans danger.

- `supabase/migrations/0002_marque_publique.sql` (16 octobre 2026) : affiche
  le nom et le logo de la communauté sur l'écran de connexion, même sur un
  appareil qui ne s'est jamais connecté. Seuls ce nom et ce logo deviennent
  lisibles sans compte. Sans ce script, l'écran de connexion montre le nom et
  le logo gardés lors de la dernière connexion sur l'appareil.
- `supabase/migrations/0003_mot_de_passe_oublie.sql` (16 octobre 2026) :
  « Mot de passe oublié » sur l'écran de connexion, envoi de la demande aux
  administrateurs et changement obligatoire du mot de passe provisoire. Sans ce
  script, la demande ne peut pas être envoyée (le membre est invité à contacter
  un administrateur).
- `supabase/migrations/0004_connexion_identifiant.sql` (16 octobre 2026) :
  connexion avec l'identifiant (« lucas », « jean.pierre »…) en plus de
  l'e-mail. Le serveur ne révèle l'e-mail d'un compte que si le mot de passe
  est juste, et bloque un identifiant 15 minutes après 5 essais faux. Sans ce
  script, l'écran de connexion demande d'utiliser l'e-mail.
- `supabase/migrations/0005_inscription_sans_email_et_google.sql` (20 octobre
  2026) : inscription sans adresse e-mail (étudiants, membres sans e-mail),
  « Mot de passe oublié » avec l'identifiant, prénom et nom repris du compte
  Google, et correction de ses informations par le membre en attente. Sans ce
  script, la case « Je n'ai pas d'adresse e-mail » affiche un message
  « pas encore activée ». Pour Google, voir aussi
  [Connexion avec Google](#connexion-avec-google-facultatif).
- `supabase/migrations/0006_vie_spirituelle.sql` (20 octobre 2026) :
  planning des services, intentions de prière et groupes et ministères. Le
  serveur garde les intentions confiées aux seuls responsables hors de portée
  des membres. Sans ce script, ces trois pages ne peuvent rien enregistrer.

### Installer le site sur un téléphone

Ouvrez l'adresse du site dans Chrome (Android) ou Safari (iPhone), puis
**Ajouter à l'écran d'accueil** (ou **Installer l'application**). Le site
s'ouvre alors comme une application, avec les mêmes comptes et les mêmes
données que sur ordinateur. Il a besoin d'Internet pour enregistrer.

Le nom et l'icône de l'application installée sont ceux de Paramètres ›
Apparence (nom et logo de l'application). Le workflow « Déploiement du site »
vérifie chaque heure s'ils ont changé et republie alors le site ; il faut que
le script `0002_marque_publique.sql` soit installé.

L'application mobile hors ligne (fichier APK du dépôt *GestionUtilisateurs2*)
est une application différente : ses données restent dans le téléphone et ne
sont pas reliées au site. Si vous l'utilisez encore, activez son export
automatique mensuel (**Paramètres > Actions**) et copiez de temps en temps
les fichiers exportés hors du téléphone.

## 5. Premier administrateur

1. Ouvrez le site et choisissez **Créer un compte**.
2. **Le tout premier compte devient automatiquement Administrateur** et
   actif. Créez-le vous-même, immédiatement après la mise en ligne.
3. Les inscriptions suivantes restent **en attente** : elles apparaissent dans
   **Comptes en ligne** (avec un bandeau et un badge). L'administrateur choisit
   le rôle puis **Valider** ou **Refuser**.
4. Ensuite, le rôle d'un membre ou la désactivation de son compte se gèrent
   depuis sa fiche dans **Utilisateurs**, et les droits dans
   **Rôles et permissions** : ces droits sont aussi appliqués par le serveur.
5. Mot de passe oublié (script `0003_mot_de_passe_oublie.sql`) : il n'y a pas
   d'envoi d'e-mail aux membres.
   - Sur l'écran de connexion, le membre touche **Mot de passe oublié ?**, saisit
     son e-mail et envoie la demande.
   - Les administrateurs la voient dans **Comptes en ligne > Mots de passe
     oubliés** (avec un bandeau et un badge). La veille quotidienne ouvre aussi un
     ticket GitHub, donc un e-mail.
   - L'administrateur choisit **Donner un mot de passe provisoire** et le
     transmet lui-même (téléphone, SMS, de vive voix).
   - À la connexion suivante, l'application oblige le membre à choisir son
     propre mot de passe.
   - **Nouveau mot de passe** reste disponible pour tout compte actif.
6. Membre sans adresse e-mail (script `0005_inscription_sans_email_et_google.sql`) :
   - À l'inscription, il coche **Je n'ai pas d'adresse e-mail** ; son
     téléphone devient obligatoire.
   - Sa demande arrive en attente comme les autres, avec la mention
     « Sans e-mail ».
   - À la validation, l'application affiche son **identifiant** (par exemple
     « fara ») : transmettez-le-lui. Il se connecte ensuite avec cet
     identifiant et son mot de passe, sur n'importe quel appareil.
   - S'il oublie son mot de passe, il saisit son identifiant dans
     **Mot de passe oublié ?**.

## Connexion avec Google (facultatif)

Le bouton **Continuer avec Google** permet de s'inscrire et de se connecter
avec un compte Gmail, sans créer de mot de passe. C'est gratuit (Google et
Supabase), sans carte bancaire. Le bouton reste caché tant que Google n'est
pas activé dans Supabase. Les comptes Google arrivent **en attente** comme les
autres : l'administrateur les valide dans **Comptes en ligne**.

1. Dans Supabase, ouvrez **Authentication > Sign In / Providers > Google** et
   copiez l'adresse **Callback URL (for OAuth)**
   (`https://xxxx.supabase.co/auth/v1/callback`). Ne fermez pas cette page.
2. Ouvrez <https://console.cloud.google.com> avec un compte Google et créez
   un projet (par exemple « SAFIAF »).
3. Menu **Google Auth Platform** (ou *API et services > Écran de
   consentement OAuth*) :
   - **Branding** : nom de l'application (SAFIAF), e-mail d'assistance, logo
     facultatif ;
   - **Audience** : *External* (Externe), puis **Publish app** (Publier
     l'application). Sans cette publication, seuls les comptes ajoutés comme
     testeurs peuvent se connecter. Les informations demandées (nom, e-mail)
     ne demandent pas de vérification par Google.
4. **Clients > Create client** (Créer un client) :
   - type **Web application** (Application Web) ;
   - **Authorized JavaScript origins** : `https://safiaf-mg.github.io` ;
   - **Authorized redirect URIs** : l'adresse copiée à l'étape 1.

   Google affiche alors un **Client ID** et un **Client secret**.
5. De retour dans Supabase (**Google**, étape 1) : activez **Enable Sign in
   with Google**, collez le **Client ID** et le **Client secret**, puis
   **Save**.
6. **Authentication > URL Configuration** :
   - **Site URL** : `https://safiaf-mg.github.io` ;
   - **Redirect URLs > Add URL** : `https://safiaf-mg.github.io/**`.
7. Rechargez le site : le bouton **Continuer avec Google** apparaît sous
   **Se connecter** et sous **Créer mon compte**.

> Le **Client secret** reste dans Supabase : ne le mettez ni dans le code, ni
> dans GitHub, ni dans un message.

Un membre qui a déjà un compte avec la même adresse Gmail retrouve son compte
en passant par Google. Après une inscription avec Google, l'écran d'attente
lui demande son téléphone et lui permet de corriger son prénom et son nom
(script 0005).

---

## Alerte de saturation Supabase

L'offre gratuite est limitée (500 Mo de base, 1 Go de fichiers). L'application
surveille cet espace et prévient les administrateurs bien avant la limite.

- **Mesure quotidienne** par la tâche GitHub *Veille Supabase* (6 h 17 UTC),
  plus une mesure à chaque ouverture de la page **Capacité du serveur**.
- **Deux seuils réglables** : *attention* (70 % par défaut) et *critique*
  (90 %), modifiables dans la page Capacité.
- **Où l'administrateur est prévenu :**
  - un bandeau en haut de chaque page, orange (attention) ou rouge
    (critique), avec un bouton vers la page Capacité ;
  - un badge sur **Capacité du serveur** dans le menu ;
  - un ticket GitHub « Alerte capacité Supabase » (GitHub envoie un e-mail
    aux personnes qui suivent le dépôt), complété à chaque nouvelle mesure
    tant qu'il reste ouvert.
- Une alerte n'est créée qu'une fois par niveau : tant qu'elle n'est pas
  marquée **Traitée**, aucune alerte en double n'est ajoutée. Un passage de
  *attention* à *critique* crée une nouvelle alerte.
- **Page Capacité du serveur** (administrateurs) :
  - jauges base, fichiers et comptes, avec les seuils ;
  - estimation du nombre de jours avant la limite (d'après les 30 derniers
    jours) ;
  - historique sur 90 jours ;
  - détail de la place prise par chaque partie de l'application ;
  - **Libérer de l'espace** : recherche et suppression des fichiers qui ne
    sont plus utilisés par aucune fiche ;
  - lien vers le tableau de bord Supabase pour le transfert mensuel, que
    l'application ne peut pas mesurer elle-même.

### Que faire en cas d'alerte

1. Ouvrir **Capacité du serveur** et regarder ce qui grossit.
2. **Libérer de l'espace** : supprimer les fichiers inutilisés.
3. Archiver ou supprimer les anciennes données (anciennes activités,
   messages, documents périmés).
4. Une fois l'espace libéré, cliquer **Traitée** sur l'alerte et fermer le
   ticket GitHub.
5. Si la communauté a durablement besoin de plus, l'offre payante Supabase
   (25 $/mois) multiplie les limites ; rien n'est à changer dans le code.

## Les autres parades de l'offre gratuite

| Risque | Parade mise en place |
|---|---|
| Projet Supabase mis en pause après 7 jours sans activité | La veille quotidienne GitHub interroge Supabase chaque jour. |
| Aucune sauvegarde automatique dans l'offre gratuite | Tâche **Sauvegarde** chaque dimanche : copie complète de la base (comptes compris, conservée 90 jours) et des fichiers (conservés 14 jours), chiffrées avec votre mot de passe et téléchargeables dans **Actions > Sauvegarde**. |
| Tâches programmées suspendues par GitHub après 60 jours sans activité du dépôt | La veille modifie elle-même un petit fichier (`.github/derniere-activite.txt`) si rien n'a changé depuis 50 jours. |
| Photos trop lourdes | Compression dans le navigateur avant l'envoi (1600 px maximum, JPEG). |
| Base gonflée par les fichiers | Photos et documents rangés dans le stockage de fichiers, pas dans la base ; un même fichier n'est stocké qu'une fois. |
| Transfert mensuel (5 Go) | Cache dans le navigateur : seules les parties modifiées sont retéléchargées, et un fichier n'est téléchargé qu'une fois. |
| Fichiers orphelins | Recherche et suppression depuis la page Capacité. |
| Une donnée trop grosse | Le serveur refuse tout enregistrement de plus de 5 Mo, avec un message clair. |

> **Si GitHub suspend quand même les tâches** (il prévient par e-mail) :
> ouvrez **Actions**, choisissez *Veille Supabase* puis **Enable workflow**,
> et faites de même pour *Sauvegarde*. Sans la veille, Supabase mettra le
> projet en pause au bout de 7 jours ; il se relance alors depuis le tableau
> de bord Supabase (**Restore project**), sans perte de données.

### Restaurer une sauvegarde

1. Téléchargez l'archive `sauvegarde-base` dans **Actions > Sauvegarde**
   (choisir une exécution, rubrique *Artifacts*). Elle contient un fichier
   `.7z` : ouvrez-le avec [7-Zip](https://www.7-zip.org) (gratuit) et
   tapez le mot de passe `SAUVEGARDE_MOT_DE_PASSE`.
2. Dans un nouveau projet Supabase, restaurez-la avec
   `psql "<adresse Session pooler>" -f base-AAAA-MM-JJ.sql`.
3. Les fichiers de `sauvegarde-fichiers` (même mot de passe) se renvoient dans le compartiment
   `fichiers`, dossier `f/`, depuis **Storage** dans Supabase.

En cas de doute, faites la première restauration avec de l'aide : c'est
l'opération la plus délicate.

---

## Entretien : ce qui vous prévient tout seul

GitHub vous envoie un e-mail (et une notification sur github.com) dans ces
cas, sans que vous ayez rien à surveiller :

| Quand | Ce que vous recevez | Ce qu'il faut faire |
| --- | --- | --- |
| Une tâche automatique échoue | E-mail « Run failed » de GitHub | Ouvrir l'exécution en rouge et lire le message |
| Supabase dépasse 70 % (puis 90 %) | Ticket « Alerte capacité Supabase » | Voir *Que faire en cas d'alerte* plus haut |
| Quelqu'un s'inscrit | Ticket « N compte(s) en attente de validation » | Menu > **Comptes en ligne** : accepter ou refuser |
| 1er janvier, avril, juillet, octobre | Ticket « Vérification trimestrielle » | Suivre la liste de contrôle du ticket |

Pour recevoir ces e-mails, gardez les réglages par défaut de GitHub :
**Settings > Notifications**, « Watching » et « Actions » cochés avec
**Email**, et le dépôt en **Watch > All Activity** (c'est le cas d'un
dépôt que vous avez créé).

---

## Corriger une traduction

Le site existe en français, en anglais et en malgache ; chaque membre choisit
sa langue. Les textes traduits sont dans
`src/GestionUtilisateurs.Online.Shared/I18n/en/` (anglais) et
`src/GestionUtilisateurs.Online.Shared/I18n/mg/` (malgache), un fichier par
partie de l'application (`shell.json` pour les menus et la connexion,
`membres.json`, `communaute.json` pour les finances, les votes et la
messagerie, `modeles.json` pour l'aide, `messages.json` pour les messages
d'erreur, etc.).

Chaque ligne a la forme `"Texte français": "traduction"` :

1. Sur GitHub, ouvrez le fichier, cliquez sur le crayon (**Edit**).
2. Modifiez **seulement la partie à droite** des deux-points. Le texte français
   à gauche doit rester identique, au caractère près.
3. Gardez les codes `{0}`, `{1}`… : ils sont remplacés par un nom, un nombre
   ou une date.
4. **Commit changes** : le site est republié tout seul quelques minutes plus
   tard.

Un texte absent du fichier s'affiche simplement en français.

Les textes qui parlent de « la communauté » citent le nom de l'application
(Paramètres › Apparence) dès qu'il est choisi. Leurs versions avec le nom sont
dans `src/GestionUtilisateurs.Online.Shared/I18n/nom/` (`fr.json`, `en.json`,
`mg.json`) : `{app}` y est remplacé par le nom, et `{de_app}` (en français
seulement) par « de Nom » ou « d'Nom ».

---

## Limites connues de cette première version

- **Données partagées par blocs.** Chaque partie de l'application (membres,
  calendrier, finances…) est enregistrée d'un seul bloc. Si deux personnes
  modifient la même partie au même moment, la seconde est prévenue que les
  données viennent de changer : elles sont rechargées et elle refait sa
  dernière modification. Rien n'est écrasé sans avertissement.
- **Parties collaboratives.** Le journal d'activité, la messagerie et les
  votes peuvent être modifiés par tout membre qui y a accès. Le serveur
  contrôle le droit d'écrire dans une partie, pas chaque ligne à l'intérieur.
- **Paramètres communs.** Les réglages (nom de l'application, couleur, logo,
  format du matricule, ordre du menu…) sont ceux de la communauté, pas de
  chaque membre. Seuls quelques choix restent personnels et sont gardés dans
  le navigateur de chacun : le thème (clair par défaut, ou sombre), les
  conseils au premier usage et le mini-guide déjà vu.
- **Messages privés.** Toutes les conversations sont enregistrées dans une
  seule partie partagée : l'application n'affiche à chacun que les siennes,
  mais un membre technique pourrait lire les autres. N'y échangez rien de
  confidentiel.
- **Mise à jour toutes les 60 secondes.** Les modifications des autres
  membres apparaissent au plus tard une minute après.
- **Pas d'export automatique mensuel** dans la version en ligne : il est
  remplacé par les sauvegardes hebdomadaires du serveur, et l'option n'est
  plus proposée dans les paramètres. L'export Excel manuel reste disponible.
- **Fichiers enregistrés par le navigateur.** Les exports arrivent dans le
  dossier de téléchargement du navigateur. Les cartes de membre et les PDF
  passent par la fenêtre d'impression : choisissez **Enregistrer au format
  PDF**.
- **Transfert mensuel non mesuré** par l'application : il se consulte dans
  le tableau de bord Supabase (lien sur la page Capacité).
- **Pas d'e-mail** : ni confirmation d'inscription, ni réinitialisation du mot
  de passe par lien (voir *Premier administrateur*, point 5).
- L'application mobile hors ligne (dépôt *GestionUtilisateurs2*) reste
  indépendante : ses données ne sont pas synchronisées avec la version en
  ligne.

## Lancer le site sur un ordinateur (développement)

1. Installez le SDK .NET 9.
2. Renseignez `Url` et `AnonKey` dans
   `src/GestionUtilisateurs.Online.Web/wwwroot/appsettings.json` (ne publiez
   pas ce changement : le déploiement remplit ce fichier lui-même).
3. Lancez `dotnet run --project src/GestionUtilisateurs.Online.Web` et ouvrez
   l'adresse affichée.

Sans configuration, le site affiche un écran expliquant que Supabase n'est pas
encore relié.
