using System.Collections.Generic;
using System.Linq;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Shared.Models;

/// <summary>Une rubrique d'aide (titre + points clés). Les textes sont traduits à la lecture.</summary>
public sealed record HelpSection(string Title, string[] Points)
{
    private readonly string _title = Title;
    private readonly string[] _points = Points;
    public string Title { get => L.T(_title); init => _title = value; }
    public string[] Points { get => _points.Select(L.T).ToArray(); init => _points = value; }
}

/// <summary>Fiche d'aide d'une fonctionnalité (utilisée par le ⓘ d'aide et par le guide utilisateur).
/// Titre et résumé sont traduits à la lecture (la langue peut changer après l'initialisation).</summary>
public sealed record HelpEntry(string Key, string Title, string Icon, string Summary, HelpSection[] Sections)
{
    private readonly string _title = Title;
    private readonly string _summary = Summary;
    public string Title { get => L.T(_title); init => _title = value; }
    public string Summary { get => L.T(_summary); init => _summary = value; }
}

/// <summary>
/// Catalogue central des fiches d'aide de l'application.
/// Utilisé pour : la bannière contextuelle au premier usage, le bouton ⓘ
/// de la barre du haut et la page « Guide utilisateur » complète.
/// </summary>
public static class HelpCatalog
{
    public static IReadOnlyList<HelpEntry> Entries { get; } = Build();

    private static readonly Dictionary<string, HelpEntry> ByKey = Entries.ToDictionary(e => e.Key, System.StringComparer.OrdinalIgnoreCase);

    public static HelpEntry? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return ByKey.TryGetValue(key, out var entry) ? entry : null;
    }

    /// <summary>La fiche correspondant au code de page de l'application.</summary>
    public static HelpEntry? ForPage(string? currentPage) => Find(currentPage);

    private static List<HelpEntry> Build() => new()
    {
        new("dashboard", "Accueil", "🏠",
            "Votre tableau de bord personnel : ce qui vous attend, la semaine à venir, ce qui reste à traiter et les chiffres clés.",
            new[]
            {
                new HelpSection("Comprendre", new[]
                {
                    "« Pour vous » montre vos messages non lus, les votes qui attendent votre voix, vos tâches et les événements du jour : touchez une case pour ouvrir le menu.",
                    "La courbe « Répartition par rôle » s'appuie sur les statuts définis dans « Rôles et permissions ».",
                    "« Derniers utilisateurs ajoutés » liste les comptes créés le plus récemment.",
                    "« Les 7 prochains jours » liste les événements du calendrier ; « À traiter » reprend les rappels les plus urgents et les fiches en double."
                }),
                new HelpSection("Bon à savoir", new[]
                {
                    "Le logo (en haut à gauche) ouvre l'histoire de la communauté.",
                    "Les graphes sont volontairement compacts pour rester lisibles sur petit écran."
                })
            }),
        new("users", "Utilisateurs", "👥",
            "La liste des membres de la communauté : recherchez, ajoutez, modifiez ou consultez chaque compte.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "Recherchez immédiatement par nom, identifiant ou e-mail depuis le champ en haut.",
                    "Touchez un utilisateur pour ouvrir sa fiche détaillée (photo, signature, documents, cotisations…).",
                    "« ＋ Ajouter » crée une fiche (nom, contacts, matricule…). Pour se connecter, la personne crée elle-même son compte sur le site, puis un administrateur le valide.",
                    "Le badge « Mineur » signale les moins de 18 ans : leur fiche demande le responsable légal.",
                    "La section « Famille » d'une fiche relie tous les liens : parents, enfants, conjoint(e), frères et sœurs, grands-parents, oncles et tantes, cousins, beaux-parents, gendre ou belle-fille, beaux-frères, parrain, tuteur… Le reste s'en déduit (le conjoint de sa fille apparaît comme gendre, les enfants de son oncle comme cousins).",
                    "Le bandeau « fiches en double possibles » propose de fusionner deux fiches de la même personne, ou de les marquer comme différentes."
                }),
                new HelpSection("Bon à savoir", new[]
                {
                    "Un compte inactif ne peut plus se connecter mais reste consultable.",
                    "Avant d'ajouter une fiche, l'application prévient si un membre porte déjà le même nom, e-mail, téléphone ou matricule.",
                    "Archiver range le compte dans « Archives des utilisateurs ». Supprimer est définitif."
                })
            }),
        new("profile", "Mes informations", "👤",
            "Votre propre fiche : vos coordonnées, votre photo, votre signature et votre carte de membre.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "Touchez le bouton en haut à droite (votre avatar) puis « Mes informations ».",
                    "Pour corriger vos coordonnées, votre photo ou votre signature, demandez à un gestionnaire.",
                    "Changez votre mot de passe depuis votre avatar : « Changer mon mot de passe »."
                }),
                new HelpSection("Bon à savoir", new[]
                {
                    "Vos informations sont enregistrées sur le serveur de la communauté et visibles selon les permissions de chaque rôle.",
                    "La photo et la signature apparaissent sur votre carte de membre."
                })
            }),
        new("activity", "Journal d'activité", "📝",
            "Trace de tout ce qui se passe dans l'application : connexions, créations, modifications et suppressions.",
            new[]
            {
                new HelpSection("Comprendre", new[]
                {
                    "Chaque évènement indique qui, quoi et quand.",
                    "Passez d'une vue à l'autre selon le type d'activité (connexions, utilisateurs, documents…).",
                    "Les exports Excel, HTML ou CSV permettent une relecture hors application."
                }),
                new HelpSection("Bon à savoir", new[]
                {
                    "Le journal est partagé : il est enregistré sur le serveur et consultable selon les permissions.",
                    "Le journal garde les 500 dernières actions ; il se met à jour tout seul quand d'autres membres agissent."
                })
            }),
        new("calendar", "Calendrier", "📅",
            "Tous les événements de la communauté au même endroit : réunions, anniversaires, échéances.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "Naviguez entre les mois avec les flèches ◀ ▶.",
                    "Touchez un jour pour voir les événements et en créer un.",
                    "« ＋ Ajouter un événement » : intitulé, date, heure et description.",
                    "La pastille sur l'icône Calendrier (en bas) indique les événements du jour."
                })
            }),
        new("fields", "Champs personnalisés", "🧩",
            "Personnalisez les fiches utilisateurs avec vos propres champs (matricule, section, blouse…)",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "« ＋ Ajouter un champ » définit le libellé, le type (texte, choix, nombre…) et si le champ est obligatoire.",
                    "Les champs créés apparaissent dans la fiche et le formulaire de chaque utilisateur.",
                    "Un champ personnalisé peut être réordonné ou masqué."
                }),
                new HelpSection("Bon à savoir", new[]
                {
                    "Les valeurs saisies restent liées au champ : supprimer le champ efface aussi les valeurs.",
                    "Champ « Identifiant » est un champ système non supprimable."
                })
            }),
        new("settings", "Paramètres", "⚙️",
            "Généralités de l'application : nom, thème, carte de membre, ordre du menu et guide.",
            new[]
            {
                new HelpSection("Comprendre", new[]
                {
                    "« Apparence » : votre thème clair ou sombre (clair par défaut, choix personnel) et la couleur principale de la communauté.",
                    "« Configuration carte de membre » ajuste slogan, citation, taille et format du matricule (ex. M-{NNNN}).",
                    "Le thème et les conseils au premier usage sont personnels : chacun garde les siens, même l'administrateur.",
                    "« Ma langue » : français, anglais ou malgache, au choix de chacun ; elle se choisit aussi sur l'écran de connexion.",
                    "« Ordre du menu » (gestionnaires) réorganise les cartes du menu « … » pour toute la communauté.",
                    "« Listes de choix » (gestionnaires) règle les choix des formulaires : ajouter, renommer, réordonner, masquer ou supprimer un type, un lien de famille, une priorité, une catégorie…",
                    "Les données sont sauvegardées chaque semaine sur le serveur ; l'export Excel reste disponible à tout moment."
                }),
                new HelpSection("Bon à savoir", new[]
                {
                    "Le bouton ⓘ en haut de chaque écran ouvre l'aide du module courant.",
                    "« Guide utilisateur » redirige vers le guide complet de l'application."
                })
            }),
        new("documents", "Gestion documentaire", "📄",
            "Classez et retrouvez tous vos fichiers : comptes rendus, justificatifs, plans, photos…",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "« ＋ Nouveau dossier » organise vos documents par thème.",
                    "« ＋ Ajouter un document » choisit un fichier (généralement compressé automatiquement) et l'associe éventuellement à un utilisateur.",
                    "Touchez un document pour le visualiser, le renommer ou le déplacer vers un autre dossier.",
                    "La corbeille récupère les documents supprimés avant suppression définitive."
                })
            }),
        new("archive", "Archives des utilisateurs", "🗃",
            "Les comptes archivés : consultez-les, restaurez-les ou supprimez-les définitivement.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "Restaurer un compte le renvoie dans la liste des utilisateurs, avec ses données.",
                    "Supprimer définitivement efface la fiche et ses pièces associées : action irréversible."
                })
            }),
        new("community", "Histoire de la communauté", "📖",
            "Racontez et faites défiler l'histoire de votre groupe : titre, sous-titre et chapitres.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "Touchez le logo en haut de l'écran pour ouvrir l'histoire depuis n'importe où.",
                    "Dans l'écran Communauté, glissez un doigt horizontalement pour changer de chapitre.",
                    "« ＋ Chapitre » ajoute une étape datée ; le titre et le sous-titre se modifient en haut."
                })
            }),
        new("comptes", "Comptes en ligne", "👤",
            "Validez ou refusez les inscriptions et gérez l'accès de chaque membre.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "Chaque nouvelle inscription apparaît en haut de la liste avec le statut « en attente ».",
                    "Choisissez le rôle puis « Valider » pour ouvrir l'accès, ou « Refuser » pour le bloquer.",
                    "Un compte validé se désactive ou se réactive depuis sa fiche dans « Utilisateurs ».",
                    "Mot de passe oublié : la demande envoyée depuis l'écran de connexion apparaît dans « Mots de passe oubliés ». « Donner un mot de passe provisoire » le définit ; transmettez-le au membre, qui devra choisir le sien à sa connexion suivante."
                }),
                new HelpSection("Bon à savoir", new[]
                {
                    "Un e-mail GitHub prévient l'administrateur quand des comptes attendent une validation.",
                    "Le premier compte créé est automatiquement administrateur."
                })
            }),
        new("roles", "Rôles et permissions", "🛡",
            "Contrôlez ce que chaque rôle peut voir et faire dans l'application.",
            new[]
            {
                new HelpSection("Comprendre", new[]
                {
                    "Chaque rôle possède une liste de permissions activables/désactivables.",
                    "Les changements s'appliquent immédiatement aux comptes du rôle concerné.",
                    "Un compte peut cumuler un rôle applicatif et des permissions particulières."
                }),
                new HelpSection("Bon à savoir", new[]
                {
                    "Des verrous empêchent de bloquer l'accès des administrateurs.",
                    "L'« Administrateur de la base » a tous les droits ; les autres rôles se règlent ici."
                })
            }),
        new("messagerie", "Messagerie", "✉",
            "Échangez des messages entre membres, sur tous leurs appareils.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "« ＋ Nouveau message » choisit un ou plusieurs destinataires.",
                    "Le canal « Annonces » s'adresse à toute la communauté.",
                    "Touchez une conversation pour la lire et répondre.",
                    "Le compteur sur le menu « … » signale les conversations non lues."
                })
            }),
        new("taches", "Tâches et suivi", "☑",
            "Planifiez les choses à faire et suivez qui en est responsable.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "« ＋ Nouvelle tâche » : intitulé, assigné, échéance, priorité.",
                    "Dépliez les sections « À faire », « En cours », « Terminées ».",
                    "Une tâche avance de « À faire » à « En cours » puis « Terminée »."
                })
            }),
        new("finances", "Finances", "💰",
            "Cotisations et trésorerie : suivez les impayés et l'argent de la communauté.",
            new[]
            {
                new HelpSection("Comprendre", new[]
                {
                    "Trois cartes : Solde disponible, Cotisations et Impayés.",
                    "Les impayés ne sont pas automatiques : « ＋ Impayés » choisit les membres concernés, le montant, le motif et la période.",
                    "« Encaisser » règle une cotisation en attente et peut ajouter l'entrée dans la trésorerie.",
                    "« ＋ Paiement » enregistre directement un règlement ; « Trésorerie » saisit chaque mouvement de caisse."
                })
            }),
        new("materiel", "Matériel", "🧰",
            "Inventaire du matériel de la communauté et suivi des prêts.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "« ＋ Ajouter un matériel » : nom, référence, état.",
                    "Un prêt associe un matériel à un membre ; « Retour » le clôt.",
                    "Les prêts en cours apparaissent avec le membre concerné ; le compteur du menu « … » les signale."
                })
            }),
        new("reunions", "Présence et réunions", "👥",
            "Organisez des réunions, notez la présence et les participants.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "« ＋ Réunion » saisit la date, l'objet et les participants.",
                    "Les gestionnaires notent la présence de chaque participant dans la réunion.",
                    "L'historique des présences aide à préparer les prochaines rencontres."
                })
            }),
        new("rappels", "Rappels intelligents", "⏰",
            "Ne manquez plus une échéance : rappels de tâches, de cotisations, d'événements.",
            new[]
            {
                new HelpSection("Comprendre", new[]
                {
                    "L'application regroupe les échéances proches : tâches, prêts, cotisations, événements.",
                    "Un rappel disparaît quand l'échéance est réglée dans son module.",
                    "Les ajustements de rappel dépendent des dates saisies dans chaque module."
                })
            }),
        new("situations", "Famangiana et situations", "🤝",
            "Organisez les famangiana (naissance, maladie, décès…) et suivez discrètement les situations sensibles (santé, logement, aide…).",
            new[]
            {
                new HelpSection("Comprendre", new[]
                {
                    "Chaque situation est privée et visible uniquement par ce que les permissions autorisent.",
                    "Un suivi peut être marqué « résolu » et reste alors consultable en historique.",
                    "Pour organiser les famangiana, choisissez le type : naissance, baptême, mariage, maladie ou hospitalisation, accident ou sinistre, décès, ou visite, accompagnement, événement important."
                })
            }),
        new("carte", "Carte de membre", "🪪",
            "La carte d'adhérent : photo, QR code, signature et export PDF.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "La carte se compose d'une face avant (photo, identité, QR) et d'un dos (signature, citation).",
                    "Pour un membre de moins de 18 ans, la carte indique « Mineur » et le responsable légal.",
                    "Touchez le QR code pour l'agrandir et le faire scanner facilement.",
                    "« Exporter en PDF » ouvre la fenêtre d'impression : choisissez « Enregistrer au format PDF » pour obtenir une carte utilisable hors application.",
                    "Slogan et citation du dos se personnalisent dans les Paramètres."
                })
            }),
        new("services", "Planning des services", "📅",
            "Qui préside, prêche, lit, chante ou accueille à chaque culte.",
            new[]
            {
                new HelpSection("Pour chaque membre", new[]
                {
                    "« Mes prochains services » montre vos tâches à venir.",
                    "« Je confirme » prévient les responsables que vous serez là ; « Je ne peux pas » demande un remplaçant, avec un mot si vous le souhaitez.",
                    "Le point rouge du menu compte les tâches que vous n'avez pas encore confirmées."
                }),
                new HelpSection("Pour ceux qui préparent le planning", new[]
                {
                    "« ＋ » prépare un culte : titre, date, heure, lieu, puis une ligne par tâche avec la personne choisie.",
                    "« Copier à la semaine suivante » reprend le même service sept jours plus tard ; chacun confirme de nouveau.",
                    "Un bandeau signale les remplaçants à trouver : ouvrez le service et choisissez une autre personne.",
                    "La liste des tâches (prédication, lecture, chants…) se règle dans Paramètres › Listes de choix."
                })
            }),
        new("prieres", "Intentions de prière", "🙏",
            "Confiez vos intentions et priez les uns pour les autres.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "Écrivez votre intention, puis choisissez « Partager avec les membres » ou « Seulement aux responsables ».",
                    "« Ne pas afficher mon nom » cache votre nom aux autres membres.",
                    "« Je prie aussi » montre à l'auteur que la communauté porte son intention.",
                    "Quand la prière est exaucée, marquez-la « Exaucée » et partagez un témoignage.",
                    "Une intention privée n'est lue que par les responsables ; vous retrouvez les vôtres dans « Mes intentions privées »."
                })
            }),
        new("groupes", "Groupes et ministères", "⛪",
            "Jeunesse, chorale, école du dimanche, mpiandry : chaque groupe avec ses responsables et ses membres.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "« Mes groupes » montre les groupes dont vous faites partie ; « Tous » montre toute la communauté.",
                    "Touchez un groupe pour voir ses membres, ses rencontres et la présence moyenne.",
                    "« Écrire au groupe » envoie un message à tous ses membres par la messagerie.",
                    "« Nouvelle rencontre » crée une réunion avec les membres du groupe ; la présence se note dans « Présence et réunions ».",
                    "« ＋ » crée un groupe : nom, jour de rencontre, responsables et membres."
                })
            }),
        new("votes", "Votes et décisions", "🗳",
            "Soumettez une question à la communauté et recueillez les bulletins.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "« ＋ Créer un vote » : question, choix multiples, date de clôture.",
                    "Chaque membre disposant de la permission vote une seule fois ; les résultats s'actualisent.",
                    "Un vote est clos à sa date de clôture ou par un gestionnaire ; son résultat est conservé."
                })
            }),
        new("journal", "Journal multimédia", "🎞",
            "Les souvenirs en photos, vidéos et audio, classés par date.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "« ＋ Ajouter un média » importe une photo, une vidéo ou un son avec date et description.",
                    "La lecture s'ouvre dans l'application, ou dans un nouvel onglet du navigateur.",
                    "Les médias sont compressés automatiquement pour économiser le stockage."
                })
            }),
        new("data", "Gestion de données", "🗂",
            "Réservé aux administrateurs : consulter et modifier les données de la communauté sans JSON.",
            new[]
            {
                new HelpSection("Comprendre", new[]
                {
                    "Choisissez une collection (utilisateurs, événements, documents…) via les onglets.",
                    "Recherchez, ajoutez, modifiez ou supprimez des enregistrements directement.",
                    "Chaque modification est enregistrée instantanément et protégée contre les suppressions accidentelles."
                }),
                new HelpSection("Bon à savoir", new[]
                {
                    "Ce module est réservé aux administrateurs : une erreur ici touche toute la communauté.",
                    "Les mots de passe ne s'y trouvent pas : ils se gèrent dans « Comptes en ligne »."
                })
            }),
        new("capacite", "Capacité du serveur", "📊",
            "Suivez l'espace utilisé sur le serveur gratuit et libérez de la place avant qu'il soit plein.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "La page affiche l'espace de la base de données et celui des fichiers, en pourcentage de la limite gratuite.",
                    "Au-delà de 70 %, une alerte s'affiche et un e-mail GitHub prévient l'administrateur.",
                    "Videz la corbeille des documents et supprimez les médias inutiles pour libérer de l'espace."
                })
            }),
        new("guide", "Guide utilisateur", "🧭",
            "Tout le fonctionnement de l'application expliqué pas à pas, module par module.",
            new[]
            {
                new HelpSection("Comment faire", new[]
                {
                    "Le sommaire en haut permet d'aller directement au module recherché.",
                    "Chaque fiche explique le rôle du module puis les actions principales.",
                    "Quand une nouvelle version du site est publiée, elle s'installe d'elle-même : à l'ouverture, ou après un court message « Nouvelle version disponible » si l'application est déjà ouverte."
                })
            })
    };
}