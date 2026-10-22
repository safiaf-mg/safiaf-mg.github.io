using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Famangiana (visites lors d'une naissance, d'une maladie, d'un décès…) :
/// prolonge les situations particulières avec la visite (événement du
/// calendrier), la participation remise (sortie de trésorerie), le décès
/// d'un membre et la fiche d'un nouveau-né.
/// </summary>
public partial class DataStore
{
    /// <summary>Vérifie et normalise les champs famangiana d'un brouillon (null si tout va bien).</summary>
    private string? NormaliserFamangiana(Situation draft)
    {
        draft.PersonneConcernee = (draft.PersonneConcernee ?? "").Trim();
        draft.LieuVisite = (draft.LieuVisite ?? "").Trim();
        draft.DateVisite = (draft.DateVisite ?? "").Trim();
        if (draft.DateVisite.Length > 0)
        {
            if (!TryParseYmd(draft.DateVisite, out _)) return L.T("La date de visite doit être au format JJ/MM/AAAA.");
            draft.DateVisite = ToYmd(draft.DateVisite);
        }
        draft.HeureVisite = (draft.HeureVisite ?? "").Trim();
        if (draft.HeureVisite.Length > 0)
        {
            if (!TimeOnly.TryParse(draft.HeureVisite, System.Globalization.CultureInfo.InvariantCulture, out var heure))
                return L.T("L'heure de visite doit être au format HH:MM.");
            draft.HeureVisite = heure.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }
        if (draft.Participation < 0) return L.T("La participation ne peut pas être négative.");
        draft.ParticipationRubrique = (draft.ParticipationRubrique ?? "").Trim();
        draft.Visiteurs = (draft.Visiteurs ?? new List<string>())
            .Select(v => FindUserByIdentifier((v ?? "").Trim())?.Identifiant)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return null;
    }

    private static void CopierFamangiana(Situation source, Situation cible)
    {
        cible.PersonneConcernee = source.PersonneConcernee;
        cible.DateVisite = source.DateVisite;
        cible.HeureVisite = source.HeureVisite;
        cible.LieuVisite = source.LieuVisite;
        cible.Visiteurs = source.Visiteurs.ToList();
        cible.Participation = source.Participation;
        cible.ParticipationRubrique = source.ParticipationRubrique;
    }

    /// <summary>Crée, met à jour ou retire l'événement de la visite (droit « Calendrier » requis).</summary>
    private void SyncVisite(Situation s)
    {
        if (!Can(Permissions.CalendarUse)) return;
        var evt = string.IsNullOrEmpty(s.EvenementId) ? null : FindEvent(s.EvenementId);
        if (s.DateVisite.Length == 0)
        {
            if (evt == null) { s.EvenementId = null; return; }
            _events.Remove(evt);
            s.EvenementId = null;
            SaveEvents();
            LogActivity(ActivityTypes.CalendarDelete, "Suppression de l'événement « " + evt.Titre + " »");
            return;
        }

        var membre = FindUserByIdentifier(s.MembreIdentifiant);
        var concerne = (membre?.NomComplet ?? s.MembreIdentifiant)
            + (s.PersonneConcernee.Length > 0 ? " — " + s.PersonneConcernee : "");
        var nouveau = evt == null;
        evt ??= new CalendarEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            DateCreation = DateTimeOffset.Now.ToString("O"),
            CreatedBy = GetCurrentUser()?.Identifiant ?? "inconnu"
        };
        evt.Type = CalendarEventTypes.Evenement;
        evt.Titre = "Famangiana : " + s.Titre;
        evt.Date = s.DateVisite;
        evt.TouteLaJournee = s.HeureVisite.Length == 0;
        evt.HeureDebut = s.HeureVisite.Length == 0 ? null : s.HeureVisite;
        evt.Lieu = s.LieuVisite;
        evt.Description = L.EnFrancais(() => SituationTypes.Label(s.Type)) + " : " + concerne;
        evt.PersonnesConcernees = s.Visiteurs.ToList();
        evt.Priorite = s.Priorite;
        if (nouveau) _events.Add(evt);
        _events.Sort((a, b) => SortKey(a).CompareTo(SortKey(b)));
        s.EvenementId = evt.Id;
        SaveEvents();
        LogActivity(nouveau ? ActivityTypes.CalendarCreate : ActivityTypes.CalendarUpdate,
            (nouveau ? "Ajout" : "Modification") + " de l'événement « " + evt.Titre + " »");
    }

    /// <summary>
    /// Enregistre, met à jour ou retire la sortie de trésorerie de la participation
    /// (droit « Finances » requis). <paramref name="enFinances"/> null : inchangé.
    /// </summary>
    private void SyncParticipation(Situation s, bool? enFinances)
    {
        if (enFinances == null || !Can(Permissions.FinancesManage)) return;
        var tx = string.IsNullOrEmpty(s.TransactionId) ? null : _tresorerie.FirstOrDefault(t => t.Id == s.TransactionId);
        if (enFinances != true || s.Participation <= 0)
        {
            s.TransactionId = null;
            if (tx == null) return;
            _tresorerie.Remove(tx);
            SaveTresorerie();
            LogActivity(ActivityTypes.FinanceTresorerie, "Sortie retirée " + FormatMontant(tx.Montant) + " : " + tx.Motif);
            return;
        }

        var membre = FindUserByIdentifier(s.MembreIdentifiant);
        var motif = "Famangiana : " + s.Titre + " (" + (membre?.NomComplet ?? s.MembreIdentifiant) + ")"
            + (s.ParticipationRubrique.Length > 0 ? " · " + s.ParticipationRubrique : "");
        var date = s.DateVisite.Length > 0 ? s.DateVisite : DateTime.Today.ToString("yyyy-MM-dd");
        if (tx == null)
        {
            tx = new TransactionTresorerie
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = TresorerieTypes.Sortie,
                CreatedBy = GetCurrentUser()?.Identifiant ?? "inconnu"
            };
            _tresorerie.Add(tx);
        }
        else if (tx.Montant == s.Participation && tx.Motif == motif && tx.Date == date) return;
        tx.Montant = s.Participation;
        tx.Motif = motif;
        tx.Date = date;
        s.TransactionId = tx.Id;
        SaveTresorerie();
        LogActivity(ActivityTypes.FinanceTresorerie, "Sortie " + FormatMontant(tx.Montant) + " : " + tx.Motif);
    }

    /// <summary>Vrai si la participation de ce suivi est enregistrée dans Finances.</summary>
    public bool ParticipationEnFinances(Situation s)
        => !string.IsNullOrEmpty(s.TransactionId) && _tresorerie.Any(t => t.Id == s.TransactionId);

    /// <summary>Enregistre le décès d'un membre : la fiche est désactivée, ses anniversaires disparaissent.</summary>
    public OperationResult MarquerDeces(string userId, string dateDeces)
    {
        if (!Can(Permissions.UsersManage)) return OperationResult.Fail(L.T("Action réservée à la gestion des utilisateurs."));
        var u = FindUser(userId);
        if (u == null) return OperationResult.Fail(L.T("Utilisateur introuvable."));
        if (!TryParseYmd(dateDeces, out var date)) return OperationResult.Fail(L.T("La date du décès doit être au format JJ/MM/AAAA."));
        if (date.Date > DateTime.Today) return OperationResult.Fail(L.T("La date du décès ne peut pas être dans le futur."));
        if (u.Id == GetCurrentUser()?.Id) return OperationResult.Fail(L.T("Vous ne pouvez pas enregistrer votre propre décès."));
        if (LastAdminGuard(u, true) != null) return OperationResult.Fail(L.T("Le dernier administrateur ne peut pas être désactivé."));

        u.DateDeces = ToYmd(dateDeces);
        u.Actif = false;
        SaveUsers();
        if (Can(Permissions.CalendarUse))
        {
            var anniversaires = GetBirthdayEventsForUser(u.Identifiant, u.NomComplet).ToHashSet();
            if (_events.RemoveAll(anniversaires.Contains) > 0) SaveEvents();
        }
        LogActivity(ActivityTypes.Update, "Décès enregistré : " + u.Identifiant + " (" + u.DateDeces + ")");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Annule un décès saisi par erreur : la fiche redevient active.</summary>
    public OperationResult AnnulerDeces(string userId)
    {
        if (!Can(Permissions.UsersManage)) return OperationResult.Fail(L.T("Action réservée à la gestion des utilisateurs."));
        var u = FindUser(userId);
        if (u == null) return OperationResult.Fail(L.T("Utilisateur introuvable."));
        if (!u.EstDecede) return OperationResult.Fail(L.T("Aucun décès enregistré pour ce membre."));
        u.DateDeces = "";
        u.Actif = true;
        SaveUsers();
        if (Can(Permissions.CalendarUse)) { try { SyncUserDateEvents(u); } catch { } }
        LogActivity(ActivityTypes.Update, "Décès annulé : " + u.Identifiant);
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>
    /// Naissance : crée la fiche du bébé, reliée au membre comme parent
    /// (et à son conjoint ou sa conjointe si <paramref name="relierConjoint"/>).
    /// </summary>
    public UserOperationResult CreerFicheNaissance(string situationId, string prenom, string nom, string dateNaissance, bool relierConjoint)
    {
        static UserOperationResult Echec(string e) => new() { Success = false, Error = e };
        if (!Can(Permissions.UsersManage)) return Echec(L.T("Action réservée à la gestion des utilisateurs."));
        var s = FindSituation(situationId);
        if (s == null) return Echec(L.T("Situation introuvable."));
        if (!string.IsNullOrEmpty(s.EnfantIdentifiant) && FindUserByIdentifier(s.EnfantIdentifiant) != null)
            return Echec(L.T("La fiche du bébé existe déjà."));
        var parent = FindUserByIdentifier(s.MembreIdentifiant);
        if (parent == null) return Echec(L.F("Membre introuvable : « {0} ».", s.MembreIdentifiant));
        prenom = (prenom ?? "").Trim();
        if (prenom.Length == 0) return Echec(L.T("Le prénom du bébé est obligatoire."));
        if (!TryParseYmd(dateNaissance, out var naissance)) return Echec(L.T("La date de naissance doit être au format JJ/MM/AAAA."));
        if (naissance.Date > DateTime.Today) return Echec(L.T("La date de naissance ne peut pas être dans le futur."));

        var bebe = new UserAccount
        {
            Prenom = prenom,
            Nom = string.IsNullOrWhiteSpace(nom) ? parent.Nom : nom,
            DateNaissance = ToYmd(dateNaissance)
        };
        var res = AddUser(bebe);
        if (!res.Success) return res;
        AjouterLienFamille(bebe.Id, parent.Identifiant, LiensFamille.Parent);
        if (relierConjoint)
            foreach (var c in Lies(parent, LiensFamille.Conjoint))
                AjouterLienFamille(bebe.Id, c.Identifiant, LiensFamille.Parent);
        s.EnfantIdentifiant = bebe.Identifiant;
        SaveSituations();
        DataChanged?.Invoke();
        return res;
    }
}
