using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Vie spirituelle de la communauté : groupes et ministères, planning des
/// services (qui fait quoi à chaque culte) et intentions de prière.
/// </summary>
public partial class DataStore
{
    private void ChargerVieSpirituelle()
    {
        _groupes.Clear();
        _groupes.AddRange(Deserialize<List<Groupe>>(_storage.Get(K_GROUPES)) ?? new List<Groupe>());
        foreach (var g in _groupes)
        {
            g.Membres ??= new List<string>();
            g.Responsables ??= new List<string>();
        }
        _services.Clear();
        _services.AddRange(Deserialize<List<ServiceCulte>>(_storage.Get(K_SERVICES)) ?? new List<ServiceCulte>());
        foreach (var s in _services) s.Affectations ??= new List<AffectationService>();
        _prieres.Clear();
        _prieres.AddRange(Deserialize<List<IntentionPriere>>(_storage.Get(K_PRIERES)) ?? new List<IntentionPriere>());
        _prieresPrivees.Clear();
        _prieresPrivees.AddRange(Deserialize<List<IntentionPriere>>(_storage.Get(K_PRIERES_PRIVEES)) ?? new List<IntentionPriere>());
        foreach (var p in _prieres.Concat(_prieresPrivees)) p.Prieurs ??= new List<string>();
    }

    private string MonIdentifiant => GetCurrentUser()?.Identifiant ?? "";

    private static bool MemeId(string? a, string? b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private List<string>? Identifiants(IEnumerable<string>? liste, out string? inconnu)
    {
        inconnu = null;
        var propres = (liste ?? Enumerable.Empty<string>())
            .Select(x => (x ?? "").Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var x in propres)
        {
            if (FindUserByIdentifier(x) == null) { inconnu = x; return null; }
        }
        return propres;
    }

    // ---------- Groupes et ministères ----------

    public IReadOnlyList<Groupe> Groupes => _groupes.OrderBy(g => g.Nom, StringComparer.CurrentCultureIgnoreCase).ToList();

    public Groupe? FindGroupe(string id) => _groupes.FirstOrDefault(g => g.Id == id);

    /// <summary>Groupes dont le membre fait partie (comme membre ou responsable).</summary>
    public IReadOnlyList<Groupe> GroupesDe(string? identifiant)
        => Groupes.Where(g => g.Membres.Any(m => MemeId(m, identifiant)) || g.Responsables.Any(r => MemeId(r, identifiant))).ToList();

    /// <summary>Tous les participants du groupe : responsables puis membres, sans doublon.</summary>
    public static List<string> ParticipantsGroupe(Groupe g)
        => g.Responsables.Concat(g.Membres).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public OperationResult EnregistrerGroupe(Groupe draft, string? id = null)
    {
        if (!Can(Permissions.GroupesManage)) return OperationResult.Fail(L.T("Action réservée à la gestion des groupes."));
        var nom = (draft.Nom ?? "").Trim();
        if (nom.Length == 0) return OperationResult.Fail(L.T("Le nom du groupe est obligatoire."));
        if (_groupes.Any(g => g.Id != id && g.Nom.Trim().Equals(nom, StringComparison.CurrentCultureIgnoreCase)))
            return OperationResult.Fail(L.T("Un groupe porte déjà ce nom."));
        var responsables = Identifiants(draft.Responsables, out var inconnu);
        if (responsables == null) return OperationResult.Fail(L.F("Membre introuvable : « {0} ».", inconnu!));
        var membres = Identifiants(draft.Membres, out inconnu);
        if (membres == null) return OperationResult.Fail(L.F("Membre introuvable : « {0} ».", inconnu!));
        // Un responsable fait partie du groupe : on ne le garde qu'une fois, parmi les responsables.
        membres.RemoveAll(m => responsables.Contains(m, StringComparer.OrdinalIgnoreCase));

        Groupe g;
        if (id == null)
        {
            g = new Groupe
            {
                Id = Guid.NewGuid().ToString("N"),
                DateCreation = DateTimeOffset.Now.ToString("O"),
                CreatedBy = MonIdentifiant
            };
            _groupes.Add(g);
        }
        else
        {
            g = FindGroupe(id) ?? throw new InvalidOperationException();
        }
        g.Nom = nom;
        g.Description = (draft.Description ?? "").Trim();
        g.Rencontre = (draft.Rencontre ?? "").Trim();
        g.Responsables = responsables;
        g.Membres = membres;
        SaveGroupes();
        LogActivity(ActivityTypes.GroupeChange, (id == null ? "Création du groupe « " : "Modification du groupe « ") + nom + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult AjouterGroupe(Groupe draft) => EnregistrerGroupe(draft);

    public OperationResult ModifierGroupe(string id, Groupe draft)
        => FindGroupe(id) == null ? OperationResult.Fail(L.T("Groupe introuvable.")) : EnregistrerGroupe(draft, id);

    public OperationResult SupprimerGroupe(string id)
    {
        if (!Can(Permissions.GroupesManage)) return OperationResult.Fail(L.T("Action réservée à la gestion des groupes."));
        var g = FindGroupe(id);
        if (g == null) return OperationResult.Fail(L.T("Groupe introuvable."));
        _groupes.Remove(g);
        SaveGroupes();
        // Les réunions du groupe restent, comme réunions générales.
        var reunions = _reunions.Where(r => r.GroupeId == id).ToList();
        if (reunions.Count > 0 && Can(Permissions.PresenceManage))
        {
            foreach (var r in reunions) r.GroupeId = "";
            SaveReunions();
        }
        LogActivity(ActivityTypes.GroupeChange, "Suppression du groupe « " + g.Nom + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Réunions du groupe, la plus récente d'abord.</summary>
    public IReadOnlyList<Reunion> ReunionsDuGroupe(string groupeId)
        => _reunions.Where(r => r.GroupeId == groupeId).OrderByDescending(r => r.Date, StringComparer.Ordinal).ToList();

    /// <summary>Taux de présence aux réunions passées du groupe (null : aucune feuille remplie).</summary>
    public double? TauxPresenceGroupe(string groupeId)
    {
        var aujourdhui = DateTime.Today.ToString("yyyy-MM-dd");
        var marques = _reunions
            .Where(r => r.GroupeId == groupeId && string.CompareOrdinal(r.Date, aujourdhui) <= 0)
            .SelectMany(r => r.Presences.Values).ToList();
        if (marques.Count == 0) return null;
        return 100.0 * marques.Count(v => v == PresenceStatuts.Present) / marques.Count;
    }

    /// <summary>Prépare une réunion du groupe (participants = responsables et membres).</summary>
    public OperationResult NouvelleReunionGroupe(string groupeId, string date, string? heure = null, string? lieu = null)
    {
        if (!Can(Permissions.PresenceManage)) return OperationResult.Fail(L.T("Action réservée à la gestion de la présence."));
        var g = FindGroupe(groupeId);
        if (g == null) return OperationResult.Fail(L.T("Groupe introuvable."));
        return AddReunion(new Reunion
        {
            Titre = L.F("Rencontre : {0}", g.Nom),
            Date = date,
            HeureDebut = string.IsNullOrWhiteSpace(heure) ? null : heure.Trim(),
            Lieu = lieu ?? "",
            Participants = ParticipantsGroupe(g),
            GroupeId = g.Id
        });
    }

    /// <summary>Envoie un message à tout le groupe par la messagerie.</summary>
    public OperationResult EcrireAuGroupe(string groupeId, string texte)
    {
        if (!Can(Permissions.MessagerieUse)) return OperationResult.Fail(L.T("Votre rôle ne permet pas d'utiliser la messagerie."));
        var g = FindGroupe(groupeId);
        if (g == null) return OperationResult.Fail(L.T("Groupe introuvable."));
        var moi = MonIdentifiant;
        var destinataires = ParticipantsGroupe(g).Where(x => !MemeId(x, moi)).ToList();
        if (destinataires.Count == 0) return OperationResult.Fail(L.T("Ce groupe n'a pas encore d'autres membres."));
        return SendMessage(destinataires, texte);
    }

    // ---------- Planning des services ----------

    public IReadOnlyList<ServiceCulte> Services => _services
        .OrderBy(s => s.Date, StringComparer.Ordinal).ThenBy(s => s.Heure, StringComparer.Ordinal).ToList();

    public ServiceCulte? FindService(string id) => _services.FirstOrDefault(s => s.Id == id);

    /// <summary>Services à venir (aujourd'hui compris) où le membre a une tâche.</summary>
    public IReadOnlyList<(ServiceCulte Service, AffectationService Tache)> MesServicesAVenir(string? identifiant = null)
    {
        identifiant ??= MonIdentifiant;
        var aujourdhui = DateTime.Today.ToString("yyyy-MM-dd");
        return Services
            .Where(s => string.CompareOrdinal(s.Date, aujourdhui) >= 0)
            .SelectMany(s => s.Affectations.Where(a => MemeId(a.Identifiant, identifiant)).Select(a => (s, a)))
            .ToList();
    }

    public OperationResult EnregistrerService(ServiceCulte draft, string? id = null)
    {
        if (!Can(Permissions.ServicesManage)) return OperationResult.Fail(L.T("Action réservée à la préparation du planning."));
        var titre = (draft.Titre ?? "").Trim();
        if (titre.Length == 0) return OperationResult.Fail(L.T("Le titre du service est obligatoire."));
        if (string.IsNullOrWhiteSpace(draft.Date) || !TryParseYmd(draft.Date, out _))
            return OperationResult.Fail(L.T("La date doit être au format JJ/MM/AAAA."));
        var existant = id == null ? null : FindService(id);
        if (id != null && existant == null) return OperationResult.Fail(L.T("Service introuvable."));

        var affectations = new List<AffectationService>();
        foreach (var a in draft.Affectations ?? new List<AffectationService>())
        {
            var ident = (a.Identifiant ?? "").Trim();
            if (ident.Length == 0 && string.IsNullOrWhiteSpace(a.Role)) continue;
            if (string.IsNullOrWhiteSpace(a.Role) || !ListesChoix.Contient(ListesChoix.RolesService, a.Role))
                return OperationResult.Fail(L.T("Choisissez la tâche de chaque ligne."));
            if (ident.Length > 0 && FindUserByIdentifier(ident) == null)
                return OperationResult.Fail(L.F("Membre introuvable : « {0} ».", ident));
            // La personne change : sa réponse précédente ne vaut plus.
            var avant = existant?.Affectations.FirstOrDefault(x => x.Id == a.Id);
            var memePersonne = avant != null && MemeId(avant.Identifiant, ident);
            affectations.Add(new AffectationService
            {
                Id = string.IsNullOrWhiteSpace(a.Id) ? Guid.NewGuid().ToString("N") : a.Id,
                Role = a.Role,
                Identifiant = ident,
                Statut = memePersonne ? avant!.Statut : AffectationStatuts.Prevu,
                Remarque = memePersonne ? avant!.Remarque : ""
            });
        }

        var s = existant ?? new ServiceCulte
        {
            Id = Guid.NewGuid().ToString("N"),
            DateCreation = DateTimeOffset.Now.ToString("O"),
            CreatedBy = MonIdentifiant
        };
        s.Titre = titre;
        s.Date = ToYmd(draft.Date);
        s.Heure = (draft.Heure ?? "").Trim();
        s.Lieu = (draft.Lieu ?? "").Trim();
        s.Notes = (draft.Notes ?? "").Trim();
        s.Affectations = affectations;
        if (existant == null) _services.Add(s);
        SaveServices();
        LogActivity(ActivityTypes.ServiceChange, (existant == null ? "Ajout du service « " : "Modification du service « ") + titre + " » du " + s.Date);
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult AjouterService(ServiceCulte draft) => EnregistrerService(draft);

    public OperationResult ModifierService(string id, ServiceCulte draft) => EnregistrerService(draft, id);

    public OperationResult SupprimerService(string id)
    {
        if (!Can(Permissions.ServicesManage)) return OperationResult.Fail(L.T("Action réservée à la préparation du planning."));
        var s = FindService(id);
        if (s == null) return OperationResult.Fail(L.T("Service introuvable."));
        _services.Remove(s);
        SaveServices();
        LogActivity(ActivityTypes.ServiceChange, "Suppression du service « " + s.Titre + " » du " + s.Date);
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Copie un service une semaine plus tard, avec les mêmes tâches (à confirmer de nouveau).</summary>
    public OperationResult DupliquerService(string id, int jours = 7)
    {
        var s = FindService(id);
        if (s == null) return OperationResult.Fail(L.T("Service introuvable."));
        if (!TryParseYmd(s.Date, out var date)) return OperationResult.Fail(L.T("La date doit être au format JJ/MM/AAAA."));
        return EnregistrerService(new ServiceCulte
        {
            Titre = s.Titre,
            Date = date.AddDays(jours).ToString("yyyy-MM-dd"),
            Heure = s.Heure,
            Lieu = s.Lieu,
            Notes = "",
            Affectations = s.Affectations.Select(a => new AffectationService { Role = a.Role, Identifiant = a.Identifiant }).ToList()
        });
    }

    /// <summary>Le membre confirme sa tâche, ou demande un remplaçant (avec un mot facultatif).</summary>
    public OperationResult RepondreAffectation(string serviceId, string affectationId, bool present, string? remarque = null)
    {
        var s = FindService(serviceId);
        var a = s?.Affectations.FirstOrDefault(x => x.Id == affectationId);
        if (s == null || a == null) return OperationResult.Fail(L.T("Service introuvable."));
        if (!MemeId(a.Identifiant, MonIdentifiant) && !Can(Permissions.ServicesManage))
            return OperationResult.Fail(L.T("Seule la personne prévue peut répondre pour cette tâche."));
        a.Statut = present ? AffectationStatuts.Confirme : AffectationStatuts.Remplacement;
        a.Remarque = (remarque ?? "").Trim();
        SaveServices();
        LogActivity(ActivityTypes.ServiceChange, (present ? "Tâche confirmée : " : "Remplaçant demandé : ")
            + RolesService.LabelOrigine(a.Role) + ", « " + s.Titre + " » du " + s.Date);
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Tâches pour lesquelles un remplaçant est demandé (services à venir).</summary>
    public int RemplacementsDemandes()
    {
        var aujourdhui = DateTime.Today.ToString("yyyy-MM-dd");
        return _services.Where(s => string.CompareOrdinal(s.Date, aujourdhui) >= 0)
            .Sum(s => s.Affectations.Count(a => a.Statut == AffectationStatuts.Remplacement));
    }

    // ---------- Intentions de prière ----------

    /// <summary>Intentions partagées, les plus récentes d'abord.</summary>
    public IReadOnlyList<IntentionPriere> Prieres => _prieres.OrderByDescending(p => p.Date, StringComparer.Ordinal).ToList();

    /// <summary>Intentions privées (lisibles seulement avec « Lire les intentions de prière privées »).</summary>
    public IReadOnlyList<IntentionPriere> PrieresPrivees => Can(Permissions.PrieresManage)
        ? _prieresPrivees.OrderByDescending(p => p.Date, StringComparer.Ordinal).ToList()
        : new List<IntentionPriere>();

    private IntentionPriere? TrouverPriere(string id, out bool privee)
    {
        var p = _prieres.FirstOrDefault(x => x.Id == id);
        privee = p == null;
        return p ?? (Can(Permissions.PrieresManage) ? _prieresPrivees.FirstOrDefault(x => x.Id == id) : null);
    }

    private void SauverPriere(bool privee)
    {
        if (privee) SavePrieresPrivees(); else SavePrieres();
    }

    /// <summary>Confie une intention partagée avec les membres.</summary>
    public OperationResult PartagerIntention(string texte, bool anonyme)
    {
        if (!Can(Permissions.PrieresUse)) return OperationResult.Fail(L.T("Votre rôle ne permet pas de confier une intention de prière."));
        texte = (texte ?? "").Trim();
        if (texte.Length == 0) return OperationResult.Fail(L.T("Écrivez votre intention de prière."));
        if (texte.Length > 2000) return OperationResult.Fail(L.T("L'intention ne doit pas dépasser 2 000 caractères."));
        _prieres.Add(new IntentionPriere
        {
            Id = Guid.NewGuid().ToString("N"),
            Texte = texte,
            Auteur = MonIdentifiant,
            Anonyme = anonyme,
            Date = DateTimeOffset.Now.ToString("O")
        });
        SavePrieres();
        LogActivity(ActivityTypes.PriereChange, "Nouvelle intention de prière partagée");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>« Je prie » : ajoute ou retire le membre connecté de ceux qui prient.</summary>
    public OperationResult BasculerJePrie(string id)
    {
        if (!Can(Permissions.PrieresUse)) return OperationResult.Fail(L.T("Votre rôle ne permet pas de confier une intention de prière."));
        var p = _prieres.FirstOrDefault(x => x.Id == id);
        if (p == null) return OperationResult.Fail(L.T("Intention introuvable."));
        var moi = MonIdentifiant;
        if (p.Prieurs.RemoveAll(x => MemeId(x, moi)) == 0) p.Prieurs.Add(moi);
        SavePrieres();
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>L'auteur peut modifier ou retirer son intention ; les responsables aussi.</summary>
    public bool PeutGererIntention(IntentionPriere p)
        => Can(Permissions.PrieresManage) || (p.Auteur.Length > 0 && MemeId(p.Auteur, MonIdentifiant));

    /// <summary>Marque l'intention comme exaucée (ou non), avec un témoignage facultatif.</summary>
    public OperationResult MarquerExaucee(string id, bool exaucee, string? temoignage = null)
    {
        var p = TrouverPriere(id, out var privee);
        if (p == null) return OperationResult.Fail(L.T("Intention introuvable."));
        if (!PeutGererIntention(p)) return OperationResult.Fail(L.T("Seuls l'auteur et les responsables peuvent changer cette intention."));
        p.Exaucee = exaucee;
        p.DateExaucee = exaucee ? DateTimeOffset.Now.ToString("O") : "";
        p.Temoignage = exaucee ? (temoignage ?? "").Trim() : "";
        SauverPriere(privee);
        LogActivity(ActivityTypes.PriereChange, exaucee ? "Intention de prière exaucée" : "Intention de prière rouverte");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Note de suivi d'une intention privée (responsables).</summary>
    public OperationResult NoterSuiviPriere(string id, string suivi)
    {
        if (!Can(Permissions.PrieresManage)) return OperationResult.Fail(L.T("Action réservée aux responsables des intentions de prière."));
        var p = _prieresPrivees.FirstOrDefault(x => x.Id == id);
        if (p == null) return OperationResult.Fail(L.T("Intention introuvable."));
        p.Suivi = (suivi ?? "").Trim();
        SavePrieresPrivees();
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult SupprimerIntention(string id)
    {
        var p = TrouverPriere(id, out var privee);
        if (p == null) return OperationResult.Fail(L.T("Intention introuvable."));
        if (!PeutGererIntention(p)) return OperationResult.Fail(L.T("Seuls l'auteur et les responsables peuvent changer cette intention."));
        (privee ? _prieresPrivees : _prieres).Remove(p);
        SauverPriere(privee);
        LogActivity(ActivityTypes.PriereChange, "Suppression d'une intention de prière");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Nom affiché de l'auteur (« Anonyme » si demandé).</summary>
    public string AuteurIntention(IntentionPriere p)
    {
        if (p.Anonyme) return L.T("Anonyme");
        if (p.Auteur.Length > 0) return FindUserByIdentifier(p.Auteur)?.NomComplet ?? p.Auteur;
        return string.IsNullOrWhiteSpace(p.AuteurNom) ? L.T("Membre") : p.AuteurNom;
    }
}
