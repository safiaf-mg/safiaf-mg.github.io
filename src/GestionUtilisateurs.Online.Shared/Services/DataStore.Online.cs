using System.Text.Json;
using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Extensions de la version en ligne : la connexion est gérée par Supabase
/// (comptes personnels validés par un administrateur), les données sont
/// partagées entre les membres et rechargées quand un autre membre les modifie.
/// </summary>
public partial class DataStore
{
    /// <summary>
    /// Version en ligne : pas de comptes de démonstration, connexion par compte
    /// Supabase et rechargement des données modifiées par les autres membres.
    /// </summary>
    public bool OnlineMode { get; set; }

    /// <summary>
    /// Préfixe des préférences personnelles (thème, conseils, guide déjà vu).
    /// En ligne, elles restent dans le navigateur du membre : les Paramètres
    /// partagés ne sont modifiables que par les gestionnaires.
    /// </summary>
    public const string K_PREFS_PREFIX = "gu2:prefs:";

    private sealed class OnlinePrefs
    {
        public bool GuideVu { get; set; }
        public List<string> AideVu { get; set; } = new();
        public string? Theme { get; set; }
        public bool? Conseils { get; set; }
        public string? Langue { get; set; }
    }

    /// <summary>Langue choisie par le membre (null tant qu'il n'a rien choisi).</summary>
    public string? GetLangue(string? identifiant)
        => string.IsNullOrWhiteSpace(identifiant) ? null : LoadPrefs(identifiant).Langue;

    /// <summary>Enregistre la langue personnelle du membre (dans son navigateur, comme le thème).</summary>
    public void SetLangue(string? identifiant, string langue)
    {
        if (string.IsNullOrWhiteSpace(identifiant)) return;
        UpdatePrefs(identifiant, p => p.Langue = L.Normaliser(langue));
    }

    private static string PrefsKey(string identifiant) => K_PREFS_PREFIX + identifiant.Trim().ToLowerInvariant();

    private OnlinePrefs LoadPrefs(string identifiant)
    {
        var brut = _storage.Get(PrefsKey(identifiant));
        if (string.IsNullOrEmpty(brut))
        {
            // Identifiant renommé : les préférences rangées sous l'ancien suivent le membre.
            var membre = _users.FirstOrDefault(u => u.Identifiant.Equals(identifiant.Trim(), StringComparison.OrdinalIgnoreCase));
            foreach (var ancien in membre?.AnciensIdentifiants ?? new List<string>())
            {
                brut = _storage.Get(PrefsKey(ancien));
                if (string.IsNullOrEmpty(brut)) continue;
                _storage.Set(PrefsKey(identifiant), brut);
                break;
            }
        }
        var prefs = Deserialize<OnlinePrefs>(brut) ?? new OnlinePrefs();
        prefs.AideVu ??= new List<string>();
        return prefs;
    }

    private void UpdatePrefs(string identifiant, Action<OnlinePrefs> change)
    {
        var prefs = LoadPrefs(identifiant);
        change(prefs);
        _storage.Set(PrefsKey(identifiant), JsonSerializer.Serialize(prefs, JsonOpts));
        DataChanged?.Invoke();
    }

    /// <summary>Base en ligne vierge : aucun compte local, le premier compte inscrit devient administrateur.</summary>
    private void InitializeEmptyOnline()
    {
        _users.Clear();
        _fields.Clear();
        _fields.Add(SystemField(FieldDefinition.FIELD_IDENTIFIANT, "Identifiant", false));
        _community = new CommunityStory
        {
            Titre = "Histoire de notre communauté",
            SousTitre = "",
            Chapitres = new List<CommunityChapter>()
        };
        SaveUsers();
        SaveFields();
        SaveCommunity();
    }

    /// <summary>Réinitialisation en ligne : vide les données mais garde le compte connecté.</summary>
    private void ResetAllOnline()
    {
        var self = GetCurrentUser();
        var session = _session;
        SeedDemoData();
        _users.Clear();
        if (self != null) _users.Add(self);
        _session = session;
        SaveUsers();
        SaveSession();
        DataChanged?.Invoke();
    }

    /// <summary>
    /// Ouvre la session du membre connecté via Supabase (le mot de passe est
    /// vérifié par Supabase, jamais stocké localement).
    /// </summary>
    public OperationResult StartOnlineSession(string userId)
    {
        var user = _users.FirstOrDefault(u => u.Id == userId);
        if (user == null)
            return OperationResult.Fail(L.T("Votre fiche membre n'existe pas encore. Contactez un administrateur."));
        if (!user.Actif)
            return OperationResult.Fail(L.T("Ce compte est désactivé. Contactez un administrateur."));

        _session = new SessionInfo
        {
            UserId = user.Id,
            ExpiryUtcTicks = SessionTimeoutMinutes > 0
                ? DateTime.UtcNow.AddMinutes(SessionTimeoutMinutes).Ticks
                : long.MaxValue
        };
        SaveSession();
        LogActivity(ActivityTypes.Login, "Connexion de l'utilisateur " + user.Identifiant, user.Identifiant);
        SessionChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>
    /// Crée ou met à jour la fiche d'un membre dont le compte en ligne a été validé.
    /// L'identifiant de la fiche est celui du compte Supabase, ce qui relie les deux.
    /// </summary>
    public UserOperationResult UpsertOnlineMember(string accountId, string prenom, string nom, string email, string telephone, string role,
        string? relierAFicheId = null)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return new UserOperationResult { Success = false, Error = L.T("Compte introuvable.") };
        email = SansEmail.Reelle(email);

        // Relier le compte à une fiche déjà saisie (même personne) au lieu d'en créer une seconde.
        var cible = string.IsNullOrWhiteSpace(relierAFicheId) ? null : _users.FirstOrDefault(u => u.Id == relierAFicheId);
        if (cible != null && !EstCompteEnLigne(cible) && !_users.Any(u => u.Id == accountId))
        {
            cible.Id = accountId;
            cible.Role = string.IsNullOrWhiteSpace(role) ? Roles.Utilisateur : role;
            cible.Actif = true;
            if (string.IsNullOrWhiteSpace(cible.Email)) cible.Email = (email ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cible.Telephone)) cible.Telephone = (telephone ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cible.Matricule)) cible.Matricule = ProchainMatricule(cible);
            SaveUsers();
            LogActivity(ActivityTypes.Activate, "Compte en ligne relié à la fiche existante de " + cible.Identifiant);
            DataChanged?.Invoke();
            return new UserOperationResult { Success = true, UserId = cible.Id };
        }

        var existing = _users.FirstOrDefault(u => u.Id == accountId);
        if (existing != null)
        {
            existing.Role = role;
            existing.Actif = true;
            if (string.IsNullOrWhiteSpace(existing.Email)) existing.Email = email ?? "";
            SaveUsers();
            LogActivity(ActivityTypes.Activate, "Validation du compte en ligne de " + existing.Identifiant);
            DataChanged?.Invoke();
            return new UserOperationResult { Success = true, UserId = existing.Id };
        }

        var cleanNom = (nom ?? "").Trim().ToUpperInvariant();
        var cleanPrenom = (prenom ?? "").Trim();
        var identifiant = IdentifiantLibre(GenerateIdentifiant(cleanPrenom, cleanNom));

        var user = new UserAccount
        {
            Id = accountId,
            Prenom = cleanPrenom,
            Nom = cleanNom,
            Identifiant = identifiant,
            Email = (email ?? "").Trim(),
            Telephone = (telephone ?? "").Trim(),
            Role = string.IsNullOrWhiteSpace(role) ? Roles.Utilisateur : role,
            Actif = true,
            // Le mot de passe est géré par Supabase : aucune copie locale.
            MotDePasse = "",
            DateCreation = DateTimeOffset.Now.ToString("O")
        };
        user.Matricule = ProchainMatricule(user);
        _users.Add(user);
        SaveUsers();
        LogActivity(ActivityTypes.Create, "Validation du compte en ligne de " + user.Identifiant);
        DataChanged?.Invoke();
        return new UserOperationResult { Success = true, UserId = user.Id };
    }

    /// <summary>
    /// Fiches sans compte en ligne qui ressemblent à une inscription (même nom,
    /// e-mail, téléphone) : on propose de relier le compte à l'une d'elles.
    /// </summary>
    public IReadOnlyList<(UserAccount Fiche, string Raison)> FichesPourInscription(string prenom, string nom, string email, string telephone)
        => DoublonsPossibles(new UserAccount { Prenom = prenom ?? "", Nom = nom ?? "", Email = SansEmail.Reelle(email), Telephone = telephone ?? "" })
            .Where(x => !EstCompteEnLigne(x.Fiche))
            .OrderByDescending(x => x.Raison.Contains("e-mail"))
            .ToList();

    /// <summary>
    /// Recharge toutes les collections depuis le stockage, par exemple après une
    /// modification faite par un autre membre. La session en cours est conservée.
    /// </summary>
    public void Reload()
    {
        var session = _session;
        _users.Clear();
        _fields.Clear();
        _activity.Clear();
        _archive.Clear();
        _events.Clear();
        _documents.Clear();
        _folders.Clear();
        _messages.Clear();
        _taches.Clear();
        _cotisations.Clear();
        _tresorerie.Clear();
        _materiels.Clear();
        _prets.Clear();
        _reunions.Clear();
        _situations.Clear();
        _votes.Clear();
        _journalMedia.Clear();
        _groupes.Clear();
        _services.Clear();
        _prieres.Clear();
        _prieresPrivees.Clear();
        _initialized = false;
        EnsureInitialized();
        _session = session;
        DataChanged?.Invoke();
        SessionChanged?.Invoke();
    }
}
