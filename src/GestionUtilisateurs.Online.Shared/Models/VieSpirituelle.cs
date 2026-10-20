using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Shared.Models;

/// <summary>Groupe ou ministère de la communauté (jeunesse, chorale, mpiandry…).</summary>
public class Groupe
{
    public string Id { get; set; } = "";
    public string Nom { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Jour et heure de rencontre, en texte libre (ex. « Samedi 15 h »).</summary>
    public string Rencontre { get; set; } = "";
    /// <summary>Identifiants des responsables (ils font aussi partie des membres).</summary>
    public List<string> Responsables { get; set; } = new();
    /// <summary>Identifiants des membres du groupe.</summary>
    public List<string> Membres { get; set; } = new();
    public string DateCreation { get; set; } = "";
    public string CreatedBy { get; set; } = "";
}

/// <summary>Un culte ou un service, avec qui fait quoi.</summary>
public class ServiceCulte
{
    public string Id { get; set; } = "";
    public string Titre { get; set; } = "";
    /// <summary>Date au format yyyy-MM-dd.</summary>
    public string Date { get; set; } = "";
    public string Heure { get; set; } = "";
    public string Lieu { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<AffectationService> Affectations { get; set; } = new();
    public string DateCreation { get; set; } = "";
    public string CreatedBy { get; set; } = "";
}

/// <summary>Une tâche du service confiée à un membre (prédication, lecture, accueil…).</summary>
public class AffectationService
{
    public string Id { get; set; } = "";
    /// <summary>Clé de la liste « Tâches des services » (voir <see cref="ListesChoix.RolesService"/>).</summary>
    public string Role { get; set; } = "";
    public string Identifiant { get; set; } = "";
    public string Statut { get; set; } = AffectationStatuts.Prevu;
    /// <summary>Mot laissé par le membre (ex. raison d'un empêchement).</summary>
    public string Remarque { get; set; } = "";
}

public static class AffectationStatuts
{
    public const string Prevu = "prevu";
    public const string Confirme = "confirme";
    public const string Remplacement = "remplacement";

    public static string Label(string statut) => statut switch
    {
        Confirme => L.T("Confirmé"),
        Remplacement => L.T("Remplaçant demandé"),
        _ => L.T("Prévu")
    };
}

/// <summary>Tâches proposées dans le planning des services (liste réglable).</summary>
public static class RolesService
{
    public const string Predication = "predication";
    public const string Presidence = "presidence";
    public const string Lecture = "lecture";
    public const string Priere = "priere";
    public const string Chants = "chants";
    public const string Accueil = "accueil";
    public const string Offrandes = "offrandes";
    public const string Sonorisation = "sonorisation";
    public const string EcoleDimanche = "ecole_dimanche";

    public static readonly string[] Tous = { Presidence, Predication, Lecture, Priere, Chants, Accueil, Offrandes, Sonorisation, EcoleDimanche };

    public static string LabelOrigine(string role) => role switch
    {
        Predication => L.T("Prédication"),
        Presidence => L.T("Présidence du culte"),
        Lecture => L.T("Lecture biblique"),
        Priere => L.T("Prière"),
        Chants => L.T("Chants et chorale"),
        Accueil => L.T("Accueil des fidèles"),
        Offrandes => L.T("Offrandes"),
        Sonorisation => L.T("Sonorisation"),
        EcoleDimanche => L.T("École du dimanche"),
        _ => role
    };

    public static string IconOrigine(string role) => role switch
    {
        Predication => "📖",
        Presidence => "🎙",
        Lecture => "📜",
        Priere => "🙏",
        Chants => "🎶",
        Accueil => "🤝",
        Offrandes => "🧺",
        Sonorisation => "🔊",
        EcoleDimanche => "🧒",
        _ => "•"
    };

    public static string Label(string role) => ListesChoix.LibellePerso(ListesChoix.RolesService, role) ?? LabelOrigine(role);
    public static string Icon(string role) => ListesChoix.IconePerso(ListesChoix.RolesService, role) ?? IconOrigine(role);
}

/// <summary>Intention de prière. Partagée : visible des membres ; privée : réservée aux responsables.</summary>
public class IntentionPriere
{
    public string Id { get; set; } = "";
    public string Texte { get; set; } = "";
    /// <summary>Identifiant de l'auteur (vide pour une intention privée déposée par le serveur).</summary>
    public string Auteur { get; set; } = "";
    /// <summary>Nom affiché de l'auteur (intentions privées, rempli par le serveur).</summary>
    public string AuteurNom { get; set; } = "";
    /// <summary>Compte en ligne de l'auteur (intentions privées, rempli par le serveur).</summary>
    public string AuteurUid { get; set; } = "";
    public bool Anonyme { get; set; }
    public string Date { get; set; } = "";
    /// <summary>Identifiants des membres qui ont dit « Je prie ».</summary>
    public List<string> Prieurs { get; set; } = new();
    public bool Exaucee { get; set; }
    public string DateExaucee { get; set; } = "";
    public string Temoignage { get; set; } = "";
    /// <summary>Suivi noté par les responsables (intentions privées).</summary>
    public string Suivi { get; set; } = "";
}
