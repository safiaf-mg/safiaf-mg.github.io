# Gestion communautaire — version en ligne

Version web de l'application *Gestion des utilisateurs*, avec un compte
personnel pour chaque membre. L'application mobile hors ligne reste dans le
dépôt [GestionUtilisateurs2](https://github.com/lucasRAN01/GestionUtilisateurs2)
et n'est pas modifiée.

🌐 **Site en ligne : <https://safiaf-mg.github.io/>**

- **Sur ordinateur** : mise en page web (menu latéral, contenu élargi).
- **Sur téléphone** : le même design que l'application mobile, installable
  sur l'écran d'accueil depuis le navigateur.
- **Fonctionnalités** : les mêmes que l'application hors ligne jusqu'au
  Lot 17 (utilisateurs, calendrier, documents, finances, matériel, réunions,
  votes, messagerie, carte de membre, journal multimédia, mini-guide et aide
  ⓘ sur chaque page, guide utilisateur complet…).
- **Comptes** : inscription, connexion, validation manuelle par un
  administrateur, rôles et permissions appliqués aussi par le serveur.
- **Hébergement gratuit** : Supabase (comptes, données, fichiers) et GitHub
  Pages (site), sans carte bancaire, avec alerte de saturation pour les
  administrateurs, veille quotidienne et sauvegardes hebdomadaires chiffrées.
- **Mises à jour** : chaque nouvelle version s'annonce d'elle-même par un
  bandeau « Nouvelle version disponible » dans le site.

👉 **Pour mettre en ligne : [docs/MISE-EN-LIGNE.md](docs/MISE-EN-LIGNE.md).**
👉 **Pour l'entretien courant : [Entretien](docs/MISE-EN-LIGNE.md#entretien--ce-qui-vous-prévient-tout-seul).**
👉 **Historique des versions : [docs/NOUVEAUTES.md](docs/NOUVEAUTES.md).**

## Contenu du dépôt

| Dossier | Contenu |
|---|---|
| `src/GestionUtilisateurs.Online.Web` | Le site (Blazor WebAssembly, .NET 9) |
| `src/GestionUtilisateurs.Online.Shared` | Logique métier, reprise de l'application hors ligne |
| `tests/GestionUtilisateurs.Online.Tests` | Tests automatiques |
| `supabase/migrations` | Script SQL à exécuter une fois dans Supabase |
| `.github/workflows` | Vérification, déploiement GitHub Pages, veille Supabase (alertes et rappels), sauvegarde chiffrée |
| `docs` | Guide de mise en ligne et d'entretien, historique des versions |

## Développement

```bash
dotnet build GestionCommunautaireOnline.sln
dotnet test tests/GestionUtilisateurs.Online.Tests
dotnet run --project src/GestionUtilisateurs.Online.Web
```

Le site a besoin de l'adresse et de la clé publique Supabase dans
`src/GestionUtilisateurs.Online.Web/wwwroot/appsettings.json` (voir le guide).
