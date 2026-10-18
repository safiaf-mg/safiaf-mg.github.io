using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Shared.Models;

public class CalendarEvent
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = CalendarEventTypes.Autre;
    public string Titre { get; set; } = "";
    public string Date { get; set; } = "";
    public string? HeureDebut { get; set; }
    public string? HeureFin { get; set; }
    public bool TouteLaJournee { get; set; }
    public string Description { get; set; } = "";
    public string Lieu { get; set; } = "";
    public string? PersonneConcernee { get; set; }
    /// <summary>Personne(s) concernée(s) (sélection multiple). Scinde « PersonneConcernee » si vide.</summary>
    public List<string> PersonnesConcernees { get; set; } = new();
    public string Priorite { get; set; } = CalendarEventTypes.PrioriteNormale;
    public string Repetition { get; set; } = CalendarEventTypes.RepetitionAucune;
    public string Statut { get; set; } = CalendarEventTypes.StatutAFaire;
    public int RappelAvant { get; set; }
    public string Notes { get; set; } = "";
    public string DateCreation { get; set; } = "";
    public string CreatedBy { get; set; } = "";
}

public static class CalendarEventTypes
{
    public const string Anniversaire = "anniversaire";
    public const string Reunion = "reunion";
    public const string Rappel = "rappel";
    public const string Tache = "tache";
    public const string Evenement = "evenement";
    public const string Conge = "conge";
    public const string Echeance = "echeance";
    public const string Autre = "autre";

    public const string PrioriteNormale = "normale";
    public const string PrioriteImportante = "importante";
    public const string PrioriteUrgente = "urgente";

    public const string RepetitionAucune = "aucune";
    public const string RepetitionQuotidienne = "quotidienne";
    public const string RepetitionHebdomadaire = "hebdomadaire";
    public const string RepetitionMensuelle = "mensuelle";
    public const string RepetitionAnnuelle = "annuelle";

    public const string StatutAFaire = "a_faire";
    public const string StatutEnCours = "en_cours";
    public const string StatutTerminee = "terminee";

    public static readonly string[] Tous = { Anniversaire, Reunion, Rappel, Tache, Evenement, Conge, Echeance, Autre };
    public static readonly string[] PrioritesOrigine = { PrioriteNormale, PrioriteImportante, PrioriteUrgente };

    /// <summary>Libellé affiché (réglable dans Paramètres › Listes de choix).</summary>
    public static string Label(string type) => ListesChoix.LibellePerso(ListesChoix.TypesEvenement, type) ?? LabelOrigine(type);

    /// <summary>Icône affichée (réglable dans Paramètres › Listes de choix).</summary>
    public static string Icon(string type) => ListesChoix.IconePerso(ListesChoix.TypesEvenement, type)
        ?? (ListesChoix.EstAjoute(ListesChoix.TypesEvenement, type) ? "📌" : IconOrigine(type));

    /// <summary>Libellé d'une priorité (réglable dans Paramètres › Listes de choix).</summary>
    public static string PrioriteLabel(string priorite) => ListesChoix.LibellePerso(ListesChoix.Priorites, priorite) ?? PrioriteLabelOrigine(priorite);

    public static string PrioriteLabelOrigine(string priorite) => priorite switch
    {
        PrioriteUrgente => L.T("Urgente"),
        PrioriteImportante => L.T("Importante"),
        _ => L.T("Normale")
    };

    public static string LabelOrigine(string type) => type switch
    {
        Anniversaire => L.T("Anniversaire"),
        Reunion => L.T("Réunion"),
        Rappel => L.T("Rappel"),
        Tache => L.T("Tâche"),
        Evenement => L.T("Événement"),
        Conge => L.T("Congé / Absence"),
        Echeance => L.T("Échéance"),
        _ => L.T("Autre")
    };

    public static string IconOrigine(string type) => type switch
    {
        Anniversaire => "🎂",
        Reunion => "📅",
        Rappel => "🔔",
        Tache => "📝",
        Evenement => "🎉",
        Conge => "🏖️",
        Echeance => "⏰",
        _ => "➕"
    };

    /// <summary>Couleur d'accent associée au type d'événement.</summary>
    public static string Color(string type) => type switch
    {
        Anniversaire => "#d97706",
        Reunion => "#0891b2",
        Rappel => "#0ea5e9",
        Tache => "#4f46e5",
        Evenement => "#7c3aed",
        Conge => "#059669",
        Echeance => "#dc2626",
        _ => "#64748b"
    };
}