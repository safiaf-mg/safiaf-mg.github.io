using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Shared.Models;

public enum FieldType
{
    Texte,
    TexteMultiligne,
    Nombre,
    Liste,
    Date,
    Email,
    Booleen,
    PieceJointe,
    Image,
    ChoixMultiple
}

public static class FieldTypeInfo
{
    public static string Label(FieldType t) => t switch
    {
        FieldType.Texte => L.T("Texte libre"),
        FieldType.TexteMultiligne => L.T("Zone de texte"),
        FieldType.Nombre => L.T("Nombre"),
        FieldType.Liste => L.T("Liste"),
        FieldType.Date => L.T("Date"),
        FieldType.Email => L.T("E-mail"),
        FieldType.Booleen => L.T("Oui / Non"),
        FieldType.PieceJointe => L.T("Pièce jointe"),
        FieldType.Image => L.T("Image"),
        FieldType.ChoixMultiple => L.T("Choix par cochage"),
        _ => t.ToString()
    };

    public static string Icon(FieldType t) => t switch
    {
        FieldType.Texte => "Aa",
        FieldType.TexteMultiligne => "¶",
        FieldType.Nombre => "123",
        FieldType.Liste => "☰",
        FieldType.Date => "◷",
        FieldType.Email => "@",
        FieldType.Booleen => "◉",
        FieldType.PieceJointe => "📎",
        FieldType.Image => "🖼",
        FieldType.ChoixMultiple => "☑",
        _ => "?"
    };

    public static bool SupportsOptions(FieldType t) => t == FieldType.Liste || t == FieldType.ChoixMultiple;
}

public class FieldDefinition
{
    /// <summary>Identifiant du champ système lié au login du compte.</summary>
    public const string FIELD_IDENTIFIANT = "__identifiant";

    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public FieldType Type { get; set; } = FieldType.Texte;
    public bool Requis { get; set; }
    public List<string> Options { get; set; } = new();
    public int Ordre { get; set; }
    public bool Actif { get; set; } = true;
    /// <summary>Champ lié au compte (identifiant) : ni supprimable, ni de type modifiable.</summary>
    public bool SystemField { get; set; }
}

public class FileRef
{
    public string Key { get; set; } = "";
    public string Nom { get; set; } = "";
    public string Mime { get; set; } = "";
    public long Taille { get; set; }
    /// <summary>Contenu du fichier en data URL, stocké en local (photo/pièce jointe choisie dans le formulaire).</summary>
    public string? Data { get; set; }
}

public class FieldValue
{
    public string Texte { get; set; } = "";
    public double? Nombre { get; set; }
    public bool Booleen { get; set; }
    public FileRef? Fichier { get; set; }
}

/// <summary>Lien de famille vu depuis la fiche qui le porte : « Identifiant est mon parent ».</summary>
public class LienFamille
{
    /// <summary>Identifiant du membre lié.</summary>
    public string Identifiant { get; set; } = "";
    /// <summary>Ce que ce membre est pour moi (voir <see cref="LiensFamille"/>).</summary>
    public string Type { get; set; } = "";
}

public static class LiensFamille
{
    public const string Parent = "parent";
    public const string Enfant = "enfant";
    public const string Conjoint = "conjoint";
    public const string Fratrie = "fratrie";
    public const string DemiFratrie = "demi-fratrie";
    public const string GrandParent = "grand-parent";
    public const string PetitEnfant = "petit-enfant";
    public const string ArriereGrandParent = "arriere-grand-parent";
    public const string ArrierePetitEnfant = "arriere-petit-enfant";
    public const string OncleTante = "oncle-tante";
    public const string NeveuNiece = "neveu-niece";
    public const string Cousin = "cousin";
    /// <summary>Parent du conjoint (beau-père, belle-mère).</summary>
    public const string BeauParent = "beau-parent";
    /// <summary>Conjoint de l'enfant (gendre, belle-fille).</summary>
    public const string GendreBru = "gendre-bru";
    public const string BeauFrereSoeur = "beau-frere-soeur";
    /// <summary>Conjoint d'un parent (famille recomposée).</summary>
    public const string ConjointDuParent = "conjoint-du-parent";
    /// <summary>Enfant du conjoint (famille recomposée).</summary>
    public const string EnfantDuConjoint = "enfant-du-conjoint";
    public const string ParrainMarraine = "parrain-marraine";
    public const string Filleul = "filleul";
    public const string Tuteur = "tuteur";
    public const string Pupille = "pupille";
    public const string Autre = "autre";

    /// <summary>Tous les liens, dans l'ordre d'affichage de la fiche.</summary>
    public static readonly string[] Tous =
    {
        Parent, Conjoint, Enfant, Fratrie, DemiFratrie,
        GrandParent, PetitEnfant, ArriereGrandParent, ArrierePetitEnfant,
        OncleTante, NeveuNiece, Cousin,
        BeauParent, GendreBru, BeauFrereSoeur, ConjointDuParent, EnfantDuConjoint,
        ParrainMarraine, Filleul, Tuteur, Pupille, Autre
    };

    /// <summary>Liens regroupés pour la liste de choix.</summary>
    public static readonly (string Groupe, string[] Types)[] Groupes =
    {
        ("Famille proche", new[] { Parent, Conjoint, Enfant, Fratrie, DemiFratrie }),
        ("Ascendants et descendants", new[] { GrandParent, PetitEnfant, ArriereGrandParent, ArrierePetitEnfant }),
        ("Famille élargie", new[] { OncleTante, NeveuNiece, Cousin }),
        ("Par alliance", new[] { BeauParent, GendreBru, BeauFrereSoeur, ConjointDuParent, EnfantDuConjoint }),
        ("Autres liens", new[] { ParrainMarraine, Filleul, Tuteur, Pupille, Autre })
    };

    /// <summary>Le lien vu depuis l'autre fiche (mon parent → je suis son enfant).</summary>
    public static string Inverse(string type) => ListesChoix.InversePerso(type) ?? type switch
    {
        Parent => Enfant,
        Enfant => Parent,
        GrandParent => PetitEnfant,
        PetitEnfant => GrandParent,
        ArriereGrandParent => ArrierePetitEnfant,
        ArrierePetitEnfant => ArriereGrandParent,
        OncleTante => NeveuNiece,
        NeveuNiece => OncleTante,
        BeauParent => GendreBru,
        GendreBru => BeauParent,
        ConjointDuParent => EnfantDuConjoint,
        EnfantDuConjoint => ConjointDuParent,
        ParrainMarraine => Filleul,
        Filleul => ParrainMarraine,
        Tuteur => Pupille,
        Pupille => Tuteur,
        _ => type
    };

    /// <summary>Vrai si l'autre membre est un ascendant (sert à éviter les boucles).</summary>
    public static bool EstAscendant(string type) => type is Parent or GrandParent or ArriereGrandParent;

    /// <summary>Libellé affiché (réglable dans Paramètres › Listes de choix).</summary>
    public static string Libelle(string type) => ListesChoix.LibellePerso(ListesChoix.LiensDeFamille, type) ?? LibelleOrigine(type);

    public static string LibelleOrigine(string type) => type switch
    {
        Parent => L.T("Parent"),
        Enfant => L.T("Enfant"),
        Conjoint => L.T("Conjoint(e)"),
        Fratrie => L.T("Frère ou sœur"),
        DemiFratrie => L.T("Demi-frère ou demi-sœur"),
        GrandParent => L.T("Grand-parent"),
        PetitEnfant => L.T("Petit-enfant"),
        ArriereGrandParent => L.T("Arrière-grand-parent"),
        ArrierePetitEnfant => L.T("Arrière-petit-enfant"),
        OncleTante => L.T("Oncle ou tante"),
        NeveuNiece => L.T("Neveu ou nièce"),
        Cousin => L.T("Cousin(e)"),
        BeauParent => L.T("Beau-père ou belle-mère"),
        GendreBru => L.T("Gendre ou belle-fille"),
        BeauFrereSoeur => L.T("Beau-frère ou belle-sœur"),
        ConjointDuParent => L.T("Conjoint(e) d'un parent"),
        EnfantDuConjoint => L.T("Enfant du conjoint"),
        ParrainMarraine => L.T("Parrain ou marraine"),
        Filleul => L.T("Filleul(e)"),
        Tuteur => L.T("Tuteur ou tutrice"),
        Pupille => L.T("Pupille"),
        Autre => L.T("Autre membre de la famille"),
        _ => type
    };

    /// <summary>Titre de la ligne sur la fiche (au pluriel).</summary>
    public static string Titre(string type) => ListesChoix.LibellePerso(ListesChoix.LiensDeFamille, type) ?? type switch
    {
        Parent => L.T("Parents"),
        Enfant => L.T("Enfants"),
        Conjoint => L.T("Conjoint(e)"),
        Fratrie => L.T("Frères et sœurs"),
        DemiFratrie => L.T("Demi-frères et demi-sœurs"),
        GrandParent => L.T("Grands-parents"),
        PetitEnfant => L.T("Petits-enfants"),
        ArriereGrandParent => L.T("Arrière-grands-parents"),
        ArrierePetitEnfant => L.T("Arrière-petits-enfants"),
        OncleTante => L.T("Oncles et tantes"),
        NeveuNiece => L.T("Neveux et nièces"),
        Cousin => L.T("Cousins et cousines"),
        BeauParent => L.T("Beaux-parents"),
        GendreBru => L.T("Gendres et belles-filles"),
        BeauFrereSoeur => L.T("Beaux-frères et belles-sœurs"),
        ConjointDuParent => L.T("Conjoint(e) du père ou de la mère"),
        EnfantDuConjoint => L.T("Enfants du conjoint"),
        ParrainMarraine => L.T("Parrain et marraine"),
        Filleul => L.T("Filleuls"),
        Tuteur => L.T("Tuteur ou tutrice"),
        Pupille => L.T("Pupilles"),
        Autre => L.T("Autres membres de la famille"),
        _ => type
    };

    /// <summary>Texte de la liste de choix : « Son gendre ou sa belle-fille (conjoint de son enfant) ».</summary>
    public static string Choix(string type) => ListesChoix.LibellePerso(ListesChoix.LiensDeFamille, type) ?? type switch
    {
        Parent => L.T("Son parent (père, mère)"),
        Enfant => L.T("Son enfant"),
        Conjoint => L.T("Son conjoint ou sa conjointe"),
        Fratrie => L.T("Son frère ou sa sœur"),
        DemiFratrie => L.T("Son demi-frère ou sa demi-sœur"),
        GrandParent => L.T("Son grand-parent (grand-père, grand-mère)"),
        PetitEnfant => L.T("Son petit-enfant (petit-fils, petite-fille)"),
        ArriereGrandParent => L.T("Son arrière-grand-parent"),
        ArrierePetitEnfant => L.T("Son arrière-petit-enfant"),
        OncleTante => L.T("Son oncle ou sa tante"),
        NeveuNiece => L.T("Son neveu ou sa nièce"),
        Cousin => L.T("Son cousin ou sa cousine"),
        BeauParent => L.T("Son beau-père ou sa belle-mère (parent de son conjoint)"),
        GendreBru => L.T("Son gendre ou sa belle-fille (conjoint de son enfant)"),
        BeauFrereSoeur => L.T("Son beau-frère ou sa belle-sœur"),
        ConjointDuParent => L.T("Le conjoint ou la conjointe de son père ou de sa mère"),
        EnfantDuConjoint => L.T("L'enfant de son conjoint ou de sa conjointe"),
        ParrainMarraine => L.T("Son parrain ou sa marraine"),
        Filleul => L.T("Son filleul ou sa filleule"),
        Tuteur => L.T("Son tuteur ou sa tutrice (responsable légal)"),
        Pupille => L.T("Son pupille (sous sa tutelle)"),
        Autre => L.T("Un autre membre de sa famille"),
        _ => type
    };
}

public class UserAccount
{
    public string Id { get; set; } = "";
    public string Prenom { get; set; } = "";
    public string Nom { get; set; } = "";
    public string Identifiant { get; set; } = "";
    /// <summary>Identifiants portés auparavant (préférences du navigateur, connexion).</summary>
    public List<string> AnciensIdentifiants { get; set; } = new();
    public string Email { get; set; } = "";
    public string Telephone { get; set; } = "";
    public string Role { get; set; } = "Utilisateur";
    public bool Actif { get; set; } = true;
    public string MotDePasse { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DateCreation { get; set; } = "";
    public string? DerniereConnexion { get; set; }
    /// <summary>Date de naissance au format yyyy-MM-dd (vide si inconnue), affichée JJ/MM/AAAA.</summary>
    public string DateNaissance { get; set; } = "";
    /// <summary>Matricule unique du membre (vide si non attribué), affiché sur la carte de membre.</summary>
    public string Matricule { get; set; } = "";
    /// <summary>Signature électronique du membre (data URL d'image), affichée au dos de la carte de membre.</summary>
    public string? Signature { get; set; }
    public string? Photo { get; set; }
    public Dictionary<string, FieldValue> Champs { get; set; } = new();

    public string NomComplet => Nom + " " + Prenom;
    public string Statut => EstDecede ? "Décédé(e)" : Actif ? "Actif" : "Inactif";

    /// <summary>Date du décès (yyyy-MM-dd), vide si le membre est en vie.</summary>
    public string DateDeces { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EstDecede => !string.IsNullOrWhiteSpace(DateDeces);

    /// <summary>Responsable légal (parent, tuteur) d'un membre de moins de 18 ans.</summary>
    public string ResponsableLegal { get; set; } = "";
    /// <summary>Téléphone du responsable légal.</summary>
    public string ResponsableTelephone { get; set; } = "";
    /// <summary>Liens de famille avec d'autres membres (parents, enfants, conjoint, frères et sœurs).</summary>
    public List<LienFamille> Famille { get; set; } = new();

    /// <summary>Âge en années révolues d'après la date de naissance (null si inconnue).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? Age => AgeAu(DateTime.Today);

    /// <summary>Membre de moins de 18 ans (date de naissance connue).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EstMineur => Age is < 18;

    public int? AgeAu(DateTime jour)
    {
        if (!DateTime.TryParseExact(DateNaissance ?? "", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var naissance) || naissance > jour)
            return null;
        var age = jour.Year - naissance.Year;
        if (naissance.Date > jour.AddYears(-age)) age--;
        return age;
    }

    public string? GetChampTexte(string fieldId)
        => Champs.TryGetValue(fieldId, out var v) ? v.Texte : null;

    public FieldValue? GetChamp(string fieldId)
        => Champs.TryGetValue(fieldId, out var v) ? v : null;
}

public class ActivityEntry
{
    public string Id { get; set; } = "";
    public string Date { get; set; } = "";
    public string User { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>Couleurs d'accent proposées (toutes lisibles avec un texte blanc).</summary>
public static class CouleursAccent
{
    /// <summary>Couleurs avec leur nom traduit dans la langue choisie (lu à chaque accès).</summary>
    public static (string Hex, string Nom)[] Toutes => Base.Select(c => (c.Hex, L.T(c.Nom))).ToArray();

    private static readonly (string Hex, string Nom)[] Base =
    {
        ("#4f46e5", "Indigo"), ("#1e3a8a", "Bleu marine"), ("#2563eb", "Bleu"), ("#0284c7", "Bleu ciel"),
        ("#0891b2", "Cyan"), ("#0d9488", "Sarcelle"), ("#059669", "Émeraude"), ("#16a34a", "Vert"),
        ("#4d7c0f", "Vert olive"), ("#b45309", "Ocre"), ("#d97706", "Ambre"), ("#ea580c", "Orange"),
        ("#dc2626", "Rouge"), ("#be123c", "Framboise"), ("#db2777", "Rose"), ("#c026d3", "Fuchsia"),
        ("#7c3aed", "Violet"), ("#92400e", "Brun"), ("#475569", "Ardoise"), ("#0f172a", "Nuit")
    };
}

public class AppSettings
{
    public string AppName { get; set; } = "Gestion des utilisateurs";
    /// <summary>Paires de fiches (« idA|idB ») confirmées comme deux personnes différentes (homonymes).</summary>
    public List<string> PasDoublons { get; set; } = new();
    /// <summary>Listes de choix réglées par les gestionnaires (clé : <see cref="ListesChoix"/>).</summary>
    public Dictionary<string, ListeConfig> Listes { get; set; } = new();
    public string Theme { get; set; } = "light";
    public string Color { get; set; } = "#4f46e5";
    public int PageSize { get; set; } = 10;
    /// <summary>Logo personnalisé (data URL) affiché sur l'écran de connexion.</summary>
    public string Logo { get; set; } = "";
    /// <summary>Durée de session avant déconnexion automatique (minutes).</summary>
    public int SessionTimeoutMinutes { get; set; } = 30;
    /// <summary>Slogan affiché sur la face avant de la carte de membre (modifiable côté admin).</summary>
    public string CarteSlogan { get; set; } = "";
    /// <summary>Citation affichée au dos de la carte de membre (modifiable côté admin).</summary>
    public string CarteCitation { get; set; } = "";
    /// <summary>Format des matricules attribués automatiquement (ex. « M-{NNNN} »). Vide : saisie manuelle.</summary>
    public string MatriculeFormat { get; set; } = "";
    /// <summary>Largeur réelle de la carte de membre à l'impression, en mm (défaut 85).</summary>
    public int CarteLargeurMm { get; set; } = 85;
    /// <summary>Hauteur réelle de la carte de membre à l'impression, en mm (défaut 55).</summary>
    public int CarteHauteurMm { get; set; } = 55;
    /// <summary>Export automatique mensuel de toutes les données (activé par défaut).</summary>
    public bool ExportAutoMensuel { get; set; } = true;
    /// <summary>Dernier mois exporté automatiquement (format yyyy-MM).</summary>
    public string DernierExportAuto { get; set; } = "";
    /// <summary>Quota de stockage surveillé par l'alerte de saturation, en Mo (données stockées sur le téléphone). Défaut : 5 Go.</summary>
    public int StorageQuotaMb { get; set; } = 5120;
    /// <summary>Ordre personnalisé des entrées du menu « … » (clés dans l'ordre d'affichage souhaité).</summary>
    public List<string> MenuOrder { get; set; } = new();
    /// <summary>Entrées du menu « … » masquées par l'administrateur (clés, réglage dans Paramètres › Ordre du menu).</summary>
    public List<string> MenuHidden { get; set; } = new();
    /// <summary>Si vrai, un conseil contextuel s'affiche la première fois qu'une fonctionnalité est ouverte.</summary>
    public bool AidesContextuelles { get; set; } = true;
    /// <summary>Choix individuel du conseil contextuel par identifiant (prioritaire sur le réglage global).</summary>
    public Dictionary<string, bool> ConseilsUtilisateurs { get; set; } = new();
    /// <summary>Thème individuel (« light » / « dark ») par identifiant (prioritaire sur le thème global).</summary>
    public Dictionary<string, string> ThemesUtilisateurs { get; set; } = new();
    /// <summary>Identifiants des comptes ayant déjà vu le mini-guide de démarrage.</summary>
    public List<string> GuideVu { get; set; } = new();
    /// <summary>Identifiants « compte|module » dont l'aide contextuelle a déjà été consultée.</summary>
    public List<string> AideVu { get; set; } = new();
}

/// <summary>Catalogue des entrées du menu « … » (utilisé pour le réordonnancement).</summary>
public static class AppMenus
{
    public sealed record MenuDef(string Key, string Label, string Icon)
    {
        private readonly string _label = Label;
        /// <summary>Libellé traduit à la lecture (la langue peut changer après l'initialisation).</summary>
        public string Label { get => L.T(_label); init => _label = value; }
    }

    /// <summary>Ordre par défaut : d'abord ce que les membres utilisent chaque jour, puis les outils d'administration.</summary>
    public static IReadOnlyList<MenuDef> All { get; } = new[]
    {
        new MenuDef("messagerie", "Messagerie", "✉"),
        new MenuDef("taches", "Tâches et suivi", "☑"),
        new MenuDef("rappels", "Rappels intelligents", "⏰"),
        new MenuDef("services", "Planning des services", "📅"),
        new MenuDef("prieres", "Intentions de prière", "🙏"),
        new MenuDef("groupes", "Groupes et ministères", "⛪"),
        new MenuDef("votes", "Votes et décisions", "🗳"),
        new MenuDef("reunions", "Présence et réunions", "👥"),
        new MenuDef("documents", "Gestion documentaire", "📄"),
        new MenuDef("journal", "Journal multimédia", "🎞"),
        new MenuDef("community", "Histoire de la communauté", "📖"),
        new MenuDef("carte", "Carte de membre", "🪪"),
        new MenuDef("finances", "Finances", "💰"),
        new MenuDef("materiel", "Matériel", "🧰"),
        new MenuDef("situations", "Famangiana et situations", "🤝"),
        new MenuDef("guide", "Guide utilisateur", "🧭"),
        new MenuDef("comptes", "Comptes en ligne", "👤"),
        new MenuDef("roles", "Rôles et permissions", "🛡"),
        new MenuDef("archive", "Archives des utilisateurs", "🗃"),
        new MenuDef("data", "Gestion de données", "🗄"),
        new MenuDef("capacite", "Capacité du serveur", "📊")
    };
}

/// <summary>Histoire interactive de la communauté (chapitres modifiables).</summary>
public class CommunityChapter
{
    public string Id { get; set; } = "";
    public string Titre { get; set; } = "";
    public string Texte { get; set; } = "";
    public string Date { get; set; } = "";
    public string? Image { get; set; }
}

public class CommunityStory
{
    public string Titre { get; set; } = "Histoire de notre communauté";
    public string SousTitre { get; set; } = "";
    public List<CommunityChapter> Chapitres { get; set; } = new();
}

/// <summary>Message interne : boîte locale partagée, synchronisée comme le reste.</summary>
public class Message
{
    public string Id { get; set; } = "";
    /// <summary>Identifiant de conversation : paire triée pour un duo, liste triée pour un groupe, « annonces » pour le canal général.</summary>
    public string ConversationId { get; set; } = "";
    public string Expediteur { get; set; } = "";
    /// <summary>Destinataires (identifiants). Vide = canal « Annonces » (tout le monde).</summary>
    public List<string> Destinataires { get; set; } = new();
    public string Texte { get; set; } = "";
    public string Date { get; set; } = "";
    /// <summary>Date de dernière modification au format ISO (vide = jamais modifié).</summary>
    public string DateModification { get; set; } = "";
    /// <summary>Message supprimé : le contenu est masqué mais un journal reste visible dans la conversation.</summary>
    public bool Supprime { get; set; }
    /// <summary>Identifiant de celui qui a supprimé le message.</summary>
    public string? SupprimePar { get; set; }
    /// <summary>Date de suppression au format ISO.</summary>
    public string? SupprimeLe { get; set; }
    /// <summary>Identifiants ayant lu le message.</summary>
    public List<string> LuPar { get; set; } = new();
}

/// <summary>Tâche avec suivi (statut, priorité, échéance, assignés).</summary>
public class Tache
{
    public string Id { get; set; } = "";
    public string Titre { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Assignés (identifiants de connexion).</summary>
    public List<string> Assignes { get; set; } = new();
    public string Statut { get; set; } = TacheStatuts.AFaire;
    public string Priorite { get; set; } = CalendarEventTypes.PrioriteNormale;
    /// <summary>Échéance au format yyyy-MM-dd (vide si aucune).</summary>
    public string Echeance { get; set; } = "";
    public string DateCreation { get; set; } = "";
    public string? TermineLe { get; set; }
    public string CreatedBy { get; set; } = "";
}

public static class TacheStatuts
{
    public const string AFaire = "a_faire";
    public const string EnCours = "en_cours";
    public const string Terminee = "terminee";

    public static string LabelOrigine(string statut) => statut switch
    {
        EnCours => L.T("En cours"),
        Terminee => L.T("Terminée"),
        _ => L.T("À faire")
    };

    public static string IconOrigine(string statut) => statut switch
    {
        EnCours => "🔄",
        Terminee => "✅",
        _ => "☑"
    };
    /// <summary>Choix d'origine (voir <see cref="ListesChoix"/>).</summary>
    public static readonly string[] Tous = { AFaire, EnCours, Terminee };

    public static string Label(string statut) => ListesChoix.LibellePerso(ListesChoix.StatutsTache, statut) ?? LabelOrigine(statut);

    public static string Icon(string statut) => ListesChoix.IconePerso(ListesChoix.StatutsTache, statut) ?? IconOrigine(statut);
}

/// <summary>Cotisation d'un membre (montant en Ariary).</summary>
public class Cotisation
{
    public string Id { get; set; } = "";
    public string MembreIdentifiant { get; set; } = "";
    public double Montant { get; set; }
    public string Motif { get; set; } = "";
    /// <summary>Période (ex. « 2026 »).</summary>
    public string Periode { get; set; } = "";
    /// <summary>Date de paiement au format yyyy-MM-dd.</summary>
    public string Date { get; set; } = "";
    public string Statut { get; set; } = CotisationStatuts.Payee;
    public string CreatedBy { get; set; } = "";
}

public static class CotisationStatuts
{
    public const string Payee = "payee";
    public const string EnAttente = "en_attente";

    public static string LabelOrigine(string statut) => statut switch
    {
        EnAttente => L.T("En attente"),
        _ => L.T("Payée")
    };
    /// <summary>Choix d'origine (voir <see cref="ListesChoix"/>).</summary>
    public static readonly string[] Tous = { Payee, EnAttente };

    public static string Label(string statut) => ListesChoix.LibellePerso(ListesChoix.StatutsCotisation, statut) ?? LabelOrigine(statut);
}

/// <summary>Mouvement de trésorerie (entrée ou sortie, montants en Ariary).</summary>
public class TransactionTresorerie
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = TresorerieTypes.Entree;
    public double Montant { get; set; }
    public string Motif { get; set; } = "";
    /// <summary>Date au format yyyy-MM-dd.</summary>
    public string Date { get; set; } = "";
    public string CreatedBy { get; set; } = "";
}

public static class TresorerieTypes
{
    public const string Entree = "entree";
    public const string Sortie = "sortie";

    public static string LabelOrigine(string type) => type switch
    {
        Sortie => L.T("Sortie"),
        _ => L.T("Entrée")
    };

    public static string IconOrigine(string type) => type switch
    {
        Sortie => "↗",
        _ => "↙"
    };
    /// <summary>Choix d'origine (voir <see cref="ListesChoix"/>).</summary>
    public static readonly string[] Tous = { Entree, Sortie };

    public static string Label(string type) => ListesChoix.LibellePerso(ListesChoix.TypesTresorerie, type) ?? LabelOrigine(type);

    public static string Icon(string type) => ListesChoix.IconePerso(ListesChoix.TypesTresorerie, type) ?? IconOrigine(type);
}

/// <summary>Matériel inventorié (quantité totale, disponibilité calculée des prêts actifs).</summary>
public class Materiel
{
    public string Id { get; set; } = "";
    public string Nom { get; set; } = "";
    public string Description { get; set; } = "";
    public string Categorie { get; set; } = "";
    public int QuantiteTotale { get; set; } = 1;
    public string Etat { get; set; } = MaterielEtats.Bon;
    public string Responsable { get; set; } = "";
}

public static class MaterielEtats
{
    public const string Bon = "bon";
    public const string Moyen = "moyen";
    public const string HorsService = "hors_service";

    public static string LabelOrigine(string etat) => etat switch
    {
        Moyen => L.T("Moyen"),
        HorsService => L.T("Hors service"),
        _ => L.T("Bon")
    };
    /// <summary>Choix d'origine (voir <see cref="ListesChoix"/>).</summary>
    public static readonly string[] Tous = { Bon, Moyen, HorsService };

    public static string Label(string etat) => ListesChoix.LibellePerso(ListesChoix.EtatsMateriel, etat) ?? LabelOrigine(etat);
}

/// <summary>Prêt de matériel (actif tant que DateRetour est vide).</summary>
public class PretMateriel
{
    public string Id { get; set; } = "";
    public string MaterielId { get; set; } = "";
    public string MembreIdentifiant { get; set; } = "";
    public int Quantite { get; set; } = 1;
    /// <summary>Date du prêt au format yyyy-MM-dd.</summary>
    public string DatePret { get; set; } = "";
    /// <summary>Date de retour au format yyyy-MM-dd (vide = prêt actif).</summary>
    public string DateRetour { get; set; } = "";
}

/// <summary>Réunion avec participants et feuille de présence.</summary>
public class Reunion
{
    public string Id { get; set; } = "";
    public string Titre { get; set; } = "";
    /// <summary>Date au format yyyy-MM-dd.</summary>
    public string Date { get; set; } = "";
    public string? HeureDebut { get; set; }
    public string? HeureFin { get; set; }
    public string Lieu { get; set; } = "";
    public List<string> Participants { get; set; } = new();
    /// <summary>Présence par identifiant : present / absent / excuse.</summary>
    public Dictionary<string, string> Presences { get; set; } = new();
    public string CompteRendu { get; set; } = "";
    /// <summary>Groupe ou ministère dont c'est la rencontre (vide : réunion générale).</summary>
    public string GroupeId { get; set; } = "";
    public string DateCreation { get; set; } = "";
    public string CreatedBy { get; set; } = "";
}

public static class PresenceStatuts
{
    public const string Present = "present";
    public const string Absent = "absent";
    public const string Excuse = "excuse";

    public static string LabelOrigine(string statut) => statut switch
    {
        Absent => L.T("Absent"),
        Excuse => L.T("Excusé"),
        _ => L.T("Présent")
    };

    public static string IconOrigine(string statut) => statut switch
    {
        Absent => "✗",
        Excuse => "◐",
        _ => "✓"
    };
    /// <summary>Choix d'origine (voir <see cref="ListesChoix"/>).</summary>
    public static readonly string[] Tous = { Present, Absent, Excuse };

    public static string Label(string statut) => ListesChoix.LibellePerso(ListesChoix.StatutsPresence, statut) ?? LabelOrigine(statut);

    public static string Icon(string statut) => ListesChoix.IconePerso(ListesChoix.StatutsPresence, statut) ?? IconOrigine(statut);
}

/// <summary>Situation particulière d'un membre (visite, accompagnement, événement important…).</summary>
public class Situation
{
    public string Id { get; set; } = "";
    public string MembreIdentifiant { get; set; } = "";
    public string Type { get; set; } = SituationTypes.Visite;
    public string Titre { get; set; } = "";
    public string Description { get; set; } = "";
    public string Statut { get; set; } = SituationStatuts.Ouvert;
    public string Priorite { get; set; } = CalendarEventTypes.PrioriteNormale;
    public string DateCreation { get; set; } = "";
    public string? DateResolution { get; set; }
    public string CreatedBy { get; set; } = "";

    // ---- Famangiana ----
    /// <summary>Personne concernée quand ce n'est pas le membre lui-même (ex. « sa mère, Rasoa Marie »).</summary>
    public string PersonneConcernee { get; set; } = "";
    /// <summary>Date de la visite (yyyy-MM-dd), vide si pas encore prévue.</summary>
    public string DateVisite { get; set; } = "";
    /// <summary>Heure de la visite (HH:mm), vide pour toute la journée.</summary>
    public string HeureVisite { get; set; } = "";
    public string LieuVisite { get; set; } = "";
    /// <summary>Identifiants des membres qui font la visite.</summary>
    public List<string> Visiteurs { get; set; } = new();
    /// <summary>Événement du calendrier créé pour la visite.</summary>
    public string? EvenementId { get; set; }
    /// <summary>Participation remise (fanomezana), en Ariary.</summary>
    public double Participation { get; set; }
    /// <summary>Sortie de trésorerie enregistrée pour la participation.</summary>
    public string? TransactionId { get; set; }
    /// <summary>Fiche créée pour le bébé (naissance).</summary>
    public string? EnfantIdentifiant { get; set; }
}

public static class SituationTypes
{
    public const string Visite = "visite";
    public const string Accompagnement = "accompagnement";
    public const string EvenementImportant = "evenement_important";
    public const string Naissance = "naissance";
    public const string Bapteme = "bapteme";
    public const string Mariage = "mariage";
    public const string Maladie = "maladie";
    public const string Accident = "accident";
    public const string Deces = "deces";
    public const string Autre = "autre";

    /// <summary>Tous les types, dans l'ordre de la liste de choix.</summary>
    public static readonly string[] Tous =
    {
        Visite, Accompagnement, EvenementImportant,
        Naissance, Bapteme, Mariage, Maladie, Accident, Deces,
        Autre
    };

    public static string LabelOrigine(string type) => type switch
    {
        Accompagnement => L.T("Accompagnement"),
        EvenementImportant => L.T("Événement important"),
        Naissance => L.T("Naissance"),
        Bapteme => L.T("Baptême"),
        Mariage => L.T("Mariage"),
        Maladie => L.T("Maladie ou hospitalisation"),
        Accident => L.T("Accident ou sinistre"),
        Deces => L.T("Décès"),
        Autre => L.T("Autre"),
        _ => L.T("Visite nécessaire")
    };

    public static string IconOrigine(string type) => type switch
    {
        Accompagnement => "🤝",
        EvenementImportant => "⭐",
        Naissance => "👶",
        Bapteme => "⛪",
        Mariage => "💍",
        Maladie => "🏥",
        Accident => "🚑",
        Deces => "🕊️",
        Autre => "📌",
        _ => "🏠"
    };
    public static string Label(string type) => ListesChoix.LibellePerso(ListesChoix.TypesSituation, type) ?? LabelOrigine(type);

    public static string Icon(string type) => ListesChoix.IconePerso(ListesChoix.TypesSituation, type) ?? (ListesChoix.EstAjoute(ListesChoix.TypesSituation, type) ? "📌" : IconOrigine(type));
}

public static class SituationStatuts
{
    public const string Ouvert = "ouvert";
    public const string EnCours = "en_cours";
    public const string Resolu = "resolu";

    public static string LabelOrigine(string statut) => statut switch
    {
        EnCours => L.T("En cours"),
        Resolu => L.T("Résolu"),
        _ => L.T("Ouvert")
    };

    public static string IconOrigine(string statut) => statut switch
    {
        EnCours => "🔄",
        Resolu => "✅",
        _ => "📬"
    };
    /// <summary>Choix d'origine (voir <see cref="ListesChoix"/>).</summary>
    public static readonly string[] Tous = { Ouvert, EnCours, Resolu };

    public static string Label(string statut) => ListesChoix.LibellePerso(ListesChoix.StatutsSituation, statut) ?? LabelOrigine(statut);

    public static string Icon(string statut) => ListesChoix.IconePerso(ListesChoix.StatutsSituation, statut) ?? IconOrigine(statut);
}

/// <summary>Vote interne : options, bulletins (un par membre, modifiable tant qu'ouvert).</summary>
public class Vote
{
    public string Id { get; set; } = "";
    public string Titre { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public string Statut { get; set; } = VoteStatuts.Ouvert;
    /// <summary>Date de clôture au format yyyy-MM-dd (vide = manuelle).</summary>
    public string DateCloture { get; set; } = "";
    public List<Bulletin> Bulletins { get; set; } = new();
    public string DateCreation { get; set; } = "";
    public string CreatedBy { get; set; } = "";

    /// <summary>Ouvert et date de clôture non dépassée : on peut encore voter.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EstOuvert => Statut == VoteStatuts.Ouvert
        && !(DateTime.TryParseExact(DateCloture, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var c) && c.Date < DateTime.Today);

    /// <summary>Statut affiché : « Clos » dès que la date de clôture est passée.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string StatutAffiche => EstOuvert ? VoteStatuts.Ouvert : VoteStatuts.Clos;
}

public class Bulletin
{
    public string VotantIdentifiant { get; set; } = "";
    public string Option { get; set; } = "";
    public string Date { get; set; } = "";
}

public static class VoteStatuts
{
    public const string Ouvert = "ouvert";
    public const string Clos = "clos";

    public static string Label(string statut) => statut switch
    {
        Clos => L.T("Clos"),
        _ => L.T("Ouvert")
    };
}

/// <summary>Élément du journal multimédia (photo, vidéo, audio — 100 % local).</summary>
public class MediaItem
{
    public string Id { get; set; } = "";
    public string Titre { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = MediaTypes.Photo;
    public string Mime { get; set; } = "";
    public long Taille { get; set; }
    /// <summary>Contenu en data URL.</summary>
    public string Data { get; set; } = "";
    /// <summary>Date au format yyyy-MM-dd.</summary>
    public string Date { get; set; } = "";
    public string DateCreation { get; set; } = "";
    public string CreatedBy { get; set; } = "";
}

public static class MediaTypes
{
    public const string Photo = "photo";
    public const string Video = "video";
    public const string Audio = "audio";

    public static string Label(string type) => type switch
    {
        Video => L.T("Vidéo"),
        Audio => L.T("Audio"),
        _ => L.T("Photo")
    };

    public static string Icon(string type) => type switch
    {
        Video => "🎬",
        Audio => "🎙",
        _ => "🖼"
    };
}

/// <summary>Rappel intelligent calculé (tâches, anniversaires, événements, cotisations).</summary>
public class Rappel
{
    public string Id { get; set; } = "";
    public string Categorie { get; set; } = "";
    public string Titre { get; set; } = "";
    public string Detail { get; set; } = "";
    /// <summary>normale / importante / urgente.</summary>
    public string Priorite { get; set; } = CalendarEventTypes.PrioriteNormale;
    /// <summary>Page de destination : taches, users, calendar, finances.</summary>
    public string PageCible { get; set; } = "";
}

public class SessionInfo
{
    public string UserId { get; set; } = "";
    public long ExpiryUtcTicks { get; set; }
}

public static class Roles
{
    public const string BaseAdmin = "Administrateur de la base";
    public const string Administrateur = "Administrateur";
    public const string Gestionnaire = "Gestionnaire";
    public const string Utilisateur = "Utilisateur";

    public static bool IsAdmin(string? role) => role == Administrateur || role == BaseAdmin;

    /// <summary>Rôles pouvant apparaître dans la matrice des permissions (le compte technique a tout, verrouillé).</summary>
    public static IReadOnlyList<string> MatrixRoles { get; } = new[]
    {
        Administrateur, Gestionnaire, Utilisateur
    };
}

/// <summary>
/// Clés de permissions vérifiées via <c>DataStore.Can()</c>.
/// Les clés futures (finances, matériel…) sont déclarées dès maintenant
/// pour que la vague 2 s'y branche sans migration.
/// </summary>
public static class Permissions
{
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string FieldsManage = "fields.manage";
    public const string ActivityView = "activity.view";
    public const string CalendarUse = "calendar.use";
    public const string DocumentsUse = "documents.use";
    public const string ArchiveView = "archive.view";
    public const string SettingsManage = "settings.manage";
    public const string CommunityUse = "community.use";
    public const string SyncUse = "sync.use";
    public const string RolesManage = "roles.manage";
    public const string ExportData = "export.data";
    public const string ImportData = "import.data";
    // Vague 2 (modules du menu « … »)
    public const string MessagerieUse = "messagerie.use";
    public const string TachesUse = "taches.use";
    public const string FinancesManage = "finances.manage";
    public const string MaterielManage = "materiel.manage";
    public const string PresenceManage = "presence.manage";
    public const string RappelsUse = "rappels.use";
    public const string SituationsManage = "situations.manage";
    public const string CarteMembreView = "carte_membre.view";
    public const string VotesManage = "votes.manage";
    public const string JournalMediaUse = "journal_media.use";
    // Vie spirituelle
    public const string GroupesManage = "groupes.manage";
    public const string ServicesManage = "services.manage";
    public const string PrieresUse = "prieres.use";
    public const string PrieresManage = "prieres.manage";

    public sealed record Definition(string Key, string Groupe, string Label)
    {
        private readonly string _groupe = Groupe;
        private readonly string _label = Label;
        /// <summary>Groupe traduit à la lecture (la langue peut changer après l'initialisation).</summary>
        public string Groupe { get => L.T(_groupe); init => _groupe = value; }
        /// <summary>Libellé traduit à la lecture (la langue peut changer après l'initialisation).</summary>
        public string Label { get => L.T(_label); init => _label = value; }
    }

    public static IReadOnlyList<Definition> Toutes { get; } = new[]
    {
        new Definition(UsersView, "Utilisateurs", "Voir la liste des utilisateurs"),
        new Definition(UsersManage, "Utilisateurs", "Ajouter / modifier / supprimer des utilisateurs"),
        new Definition(FieldsManage, "Utilisateurs", "Gérer les champs personnalisés"),
        new Definition(ArchiveView, "Utilisateurs", "Voir et restaurer les archives"),
        new Definition(ActivityView, "Suivi", "Voir le journal d'activité"),
        new Definition(CalendarUse, "Suivi", "Utiliser le calendrier"),
        new Definition(TachesUse, "Suivi", "Utiliser les tâches et leur suivi"),
        new Definition(DocumentsUse, "Contenus", "Utiliser la gestion documentaire"),
        new Definition(CommunityUse, "Contenus", "Voir l'histoire de la communauté"),
        new Definition(JournalMediaUse, "Contenus", "Utiliser le journal multimédia"),
        new Definition(SettingsManage, "Application", "Gérer les paramètres"),
        new Definition(SyncUse, "Application", "Synchroniser avec un autre appareil"),
        new Definition(ExportData, "Application", "Exporter les données"),
        new Definition(ImportData, "Application", "Importer des données"),
        new Definition(RolesManage, "Application", "Gérer les rôles et permissions"),
        new Definition(MessagerieUse, "Échanges", "Utiliser la messagerie interne"),
        new Definition(RappelsUse, "Échanges", "Utiliser les rappels intelligents"),
        new Definition(CarteMembreView, "Échanges", "Voir la carte de membre numérique"),
        new Definition(VotesManage, "Décisions", "Gérer les votes et décisions internes"),
        new Definition(FinancesManage, "Gestion", "Gérer les finances (cotisations, trésorerie)"),
        new Definition(MaterielManage, "Gestion", "Gérer le matériel"),
        new Definition(PresenceManage, "Gestion", "Gérer présence et réunions"),
        new Definition(SituationsManage, "Gestion", "Suivre les famangiana et situations particulières"),
        new Definition(GroupesManage, "Vie spirituelle", "Gérer les groupes et ministères"),
        new Definition(ServicesManage, "Vie spirituelle", "Préparer le planning des services"),
        new Definition(PrieresUse, "Vie spirituelle", "Confier et suivre les intentions de prière"),
        new Definition(PrieresManage, "Vie spirituelle", "Lire les intentions de prière privées"),
    };
}

/// <summary>Permissions accordées à un rôle (matrice modifiable, sauf compte technique).</summary>
public class RolePermissionSet
{
    public string Role { get; set; } = "";
    public Dictionary<string, bool> Allowed { get; set; } = new();
}

// ---------- Synchronisation locale (fichier d'échange .gusync.json) ----------

/// <summary>Contenu sélectionné pour un paquet d'échange entre appareils.</summary>
public class SyncOptions
{
    public bool Users { get; set; } = true;
    public bool Fields { get; set; } = true;
    public bool Events { get; set; } = true;
    public bool Documents { get; set; } = true;
    /// <summary>Inclure le contenu binaire des documents (sinon métadonnées seules).</summary>
    public bool DocumentFiles { get; set; } = true;
    public bool Folders { get; set; } = true;
    public bool Community { get; set; } = true;
    public bool Archives { get; set; } = true;
    public bool Activity { get; set; } = false;
    public bool Settings { get; set; } = true;
    public bool Permissions { get; set; } = true;
    public bool Messages { get; set; } = true;
    public bool Taches { get; set; } = true;
    public bool Cotisations { get; set; } = true;
    public bool Tresorerie { get; set; } = true;
    public bool Materiels { get; set; } = true;
    public bool Reunions { get; set; } = true;
    public bool Situations { get; set; } = true;
    public bool Votes { get; set; } = true;
    public bool JournalMedia { get; set; } = true;
    /// <summary>Inclure le contenu binaire du journal (sinon métadonnées seules).</summary>
    public bool JournalMediaFiles { get; set; } = true;
}

/// <summary>Paquet d'échange : état d'un appareil à un instant T, fusionné à l'import.</summary>
public class SyncPackage
{
    public int Version { get; set; } = 1;
    public string ExportedAt { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public SyncOptions Options { get; set; } = new();
    public List<UserAccount> Users { get; set; } = new();
    public List<FieldDefinition> Fields { get; set; } = new();
    public List<CalendarEvent> Events { get; set; } = new();
    public List<DocumentItem> Documents { get; set; } = new();
    public List<DocumentFolder> Folders { get; set; } = new();
    public CommunityStory Community { get; set; } = new();
    public List<ArchivedUser> Archives { get; set; } = new();
    public List<ActivityEntry> Activity { get; set; } = new();
    public AppSettings? Settings { get; set; }
    public List<RolePermissionSet> Permissions { get; set; } = new();
    public List<Message> Messages { get; set; } = new();
    public List<Tache> Taches { get; set; } = new();
    public List<Cotisation> Cotisations { get; set; } = new();
    public List<TransactionTresorerie> Tresorerie { get; set; } = new();
    public List<Materiel> Materiels { get; set; } = new();
    public List<PretMateriel> Prets { get; set; } = new();
    public List<Reunion> Reunions { get; set; } = new();
    public List<Situation> Situations { get; set; } = new();
    public List<Vote> Votes { get; set; } = new();
    public List<MediaItem> JournalMedia { get; set; } = new();
}

/// <summary>Un enregistrement modifié des deux côtés depuis la dernière synchro.</summary>
public class SyncConflict
{
    public string Collection { get; set; } = "";
    public string Key { get; set; } = "";
    /// <summary>Libellé lisible (nom d'utilisateur, titre…).</summary>
    public string Titre { get; set; } = "";
    public string DetailLocal { get; set; } = "";
    public string DetailDistant { get; set; } = "";
    /// <summary>Cas : modified (les deux), deletedHere, deletedPeer.</summary>
    public string Cas { get; set; } = "modified";
}

/// <summary>Résultat de l'analyse d'un paquet avant application.</summary>
public class SyncPreview
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string DeviceName { get; set; } = "";
    public string ExportedAt { get; set; } = "";
    public Dictionary<string, int> NewFromPeer { get; set; } = new();
    public Dictionary<string, int> UpdatedFromPeer { get; set; } = new();
    public Dictionary<string, int> DeletedOnPeer { get; set; } = new();
    public string? Warning { get; set; }
    public List<SyncConflict> Conflicts { get; set; } = new();
    public int TotalChanges => NewFromPeer.Values.Sum() + UpdatedFromPeer.Values.Sum()
        + DeletedOnPeer.Values.Sum() + Conflicts.Count;
}

/// <summary>Choix utilisateur pour un conflit : conserver le local ou prendre l'entrant.</summary>
public class SyncResolution
{
    public string Collection { get; set; } = "";
    public string Key { get; set; } = "";
    public bool UseIncoming { get; set; }
}

/// <summary>État de la dernière synchro (base commune pour la détection de conflits).</summary>
public class SyncSnapshot
{
    public string DeviceId { get; set; } = "";
    public string? LastSyncAt { get; set; }
    public string? LastPeerName { get; set; }
    public Dictionary<string, string> Hashes { get; set; } = new();
    /// <summary>Conflits tranchés « conserver le local » : clé → hash distant écarté (ne plus redemander).</summary>
    public Dictionary<string, string> Dismissed { get; set; } = new();
}

public class ArchivedUser
{
    public UserAccount User { get; set; } = new();
    public string DeletedAt { get; set; } = "";
    public string DeletedBy { get; set; } = "";
}

public static class ActivityTypes
{
    public const string Login = "login";
    public const string Logout = "logout";
    public const string Create = "create";
    public const string Update = "update";
    public const string Activate = "activate";
    public const string Deactivate = "deactivate";
    public const string Delete = "delete";
    public const string Import = "import";
    public const string Export = "export";
    public const string PasswordReset = "password_reset";
    public const string FieldsChange = "fields_change";
    public const string CalendarCreate = "calendar_create";
    public const string CalendarUpdate = "calendar_update";
    public const string CalendarDelete = "calendar_delete";
    public const string DossierCreate = "folder_create";
    public const string DossierUpdate = "folder_update";
    public const string DossierDelete = "folder_delete";
    public const string DocumentCreate = "document_create";
    public const string DocumentUpdate = "document_update";
    public const string DocumentDelete = "document_delete";
    public const string DocumentRestore = "document_restore";
    public const string DocumentShare = "document_share";
    public const string SyncExport = "sync_export";
    public const string SyncImport = "sync_import";
    public const string RolesChange = "roles_change";
    public const string MessageSend = "message_send";
    public const string MessageDelete = "message_delete";
    public const string MessageUpdate = "message_update";
    public const string TacheCreate = "tache_create";
    public const string TacheUpdate = "tache_update";
    public const string TacheDelete = "tache_delete";
    public const string FinanceCotisation = "finance_cotisation";
    public const string FinanceTresorerie = "finance_tresorerie";
    public const string MaterielChange = "materiel_change";
    public const string PretChange = "pret_change";
    public const string ReunionChange = "reunion_change";
    public const string PresenceMark = "presence_mark";
    public const string SituationChange = "situation_change";
    public const string VoteChange = "vote_change";
    public const string VoteCast = "vote_cast";
    public const string MediaChange = "media_change";
    public const string GroupeChange = "groupe_change";
    public const string ServiceChange = "service_change";
    public const string PriereChange = "priere_change";

    public static string Label(string type) => type switch
    {
        Login => L.T("Connexion"),
        Logout => L.T("Déconnexion"),
        Create => L.T("Création"),
        Update => L.T("Modification"),
        Activate => L.T("Activation"),
        Deactivate => L.T("Désactivation"),
        Delete => L.T("Suppression"),
        Import => L.T("Importation"),
        Export => L.T("Exportation"),
        PasswordReset => L.T("Réinitialisation MDP"),
        FieldsChange => L.T("Champs du formulaire"),
        CalendarCreate => L.T("Ajout d'événement"),
        CalendarUpdate => L.T("Modification d'événement"),
        CalendarDelete => L.T("Suppression d'événement"),
        DossierCreate => L.T("Création de dossier"),
        DossierUpdate => L.T("Modification de dossier"),
        DossierDelete => L.T("Suppression de dossier"),
        DocumentCreate => L.T("Ajout de document"),
        DocumentUpdate => L.T("Modification de document"),
        DocumentDelete => L.T("Suppression de document"),
        DocumentRestore => L.T("Restauration de document"),
        DocumentShare => L.T("Partage de document"),
        SyncExport => L.T("Export de synchronisation"),
        SyncImport => L.T("Import de synchronisation"),
        RolesChange => L.T("Rôles et permissions"),
        MessageSend => L.T("Envoi de message"),
        MessageDelete => L.T("Suppression de message"),
        MessageUpdate => L.T("Modification de message"),
        TacheCreate => L.T("Création de tâche"),
        TacheUpdate => L.T("Suivi de tâche"),
        TacheDelete => L.T("Suppression de tâche"),
        FinanceCotisation => L.T("Cotisation"),
        FinanceTresorerie => L.T("Trésorerie"),
        MaterielChange => L.T("Matériel"),
        PretChange => L.T("Prêt de matériel"),
        ReunionChange => L.T("Réunion"),
        PresenceMark => L.T("Feuille de présence"),
        SituationChange => L.T("Situation particulière"),
        VoteChange => L.T("Vote interne"),
        VoteCast => L.T("Vote exprimé"),
        MediaChange => L.T("Journal multimédia"),
        GroupeChange => L.T("Groupes et ministères"),
        ServiceChange => L.T("Planning des services"),
        PriereChange => L.T("Intentions de prière"),
        _ => type
    };
}

public class OperationResult
{
    public bool Success { get; set; }
    public string Error { get; set; } = "";

    public static OperationResult Ok() => new() { Success = true };
    public static OperationResult Fail(string error) => new() { Success = false, Error = error };
}

public class UserOperationResult : OperationResult
{
    public string? UserId { get; set; }
}

public class ImportResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<UserAccount>? Users { get; set; }
    public int Count => Users?.Count ?? 0;
}