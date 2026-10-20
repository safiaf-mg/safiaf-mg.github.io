using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Accès aux pages selon la grille « Rôles et permissions ». Une seule règle
/// sert au menu, à la barre du bas, à l'ouverture des pages et au guide, pour
/// qu'un rôle ne voie jamais une entrée qu'il ne peut pas ouvrir.
/// </summary>
public partial class DataStore
{
    /// <summary>Le compte connecté (ou l'aperçu de rôle) peut-il ouvrir cette page ?</summary>
    public bool PeutOuvrir(string page) => page switch
    {
        "dashboard" or "users" or "profile" or "votes" or "settings" => !IsBaseAdmin,
        "guide" => true,
        "calendar" => Can(Permissions.CalendarUse),
        "activity" => Can(Permissions.ActivityView),
        "documents" => !IsBaseAdmin && Can(Permissions.DocumentsUse),
        "community" => Can(Permissions.CommunityUse),
        "fields" => Can(Permissions.FieldsManage),
        "archive" => Can(Permissions.ArchiveView),
        "data" or "capacite" => IsAdmin,
        "comptes" => Can(Permissions.UsersManage),
        // La page des rôles reste réservée aux administrateurs (anti-blocage de la grille).
        "roles" => IsAdmin && Can(Permissions.RolesManage),
        "messagerie" => Can(Permissions.MessagerieUse),
        "taches" => Can(Permissions.TachesUse),
        "rappels" => Can(Permissions.RappelsUse),
        "finances" => Can(Permissions.FinancesManage),
        "materiel" => Can(Permissions.MaterielManage),
        "reunions" => Can(Permissions.PresenceManage),
        "situations" => Can(Permissions.SituationsManage),
        "carte" => Can(Permissions.CarteMembreView),
        "journal" => Can(Permissions.JournalMediaUse),
        "sync" => false,
        _ => true
    };

    /// <summary>Liste des membres (sinon « Mes informations ») : permission « Voir la liste des utilisateurs ».</summary>
    public bool VoitListeMembres => !IsBaseAdmin && Can(Permissions.UsersView);
}
