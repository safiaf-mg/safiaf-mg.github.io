using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>Collection locale pouvant être administrée uniquement par le compte DBA.</summary>
public sealed record ManagedDataSet(string Key, string Label, string Description);

/// <summary>Détail du stockage utilisé pour une source de données locale.</summary>
public sealed record StorageUsageItem(string Key, string Label, string Description, long Bytes);

/// <summary>
/// Noyau métier : authentification locale, CRUD utilisateurs, champs flexibles,
/// journal d'activité, import/export, paramètres. Persiste via IStorageProvider
/// (localStorage dans le navigateur, mémoire dans les tests). Purement hors ligne.
/// </summary>
public partial class DataStore
{
    private const string K_USERS = "gu2:users";
    private const string K_FIELDS = "gu2:fields";
    private const string K_ACTIVITY = "gu2:activity";
    private const string K_SESSION = "gu2:session";
    private const string K_SETTINGS = "gu2:settings";
    private const string K_ARCHIVE = "gu2:archive";
    private const string K_EVENTS = "gu2:events";
    private const string K_DOCUMENTS = "gu2:documents";
    private const string K_FOLDERS = "gu2:folders";
    private const string K_COMMUNITY = "gu2:community";
    private const string K_PERMISSIONS = "gu2:permissions";
    private const string K_SYNC = "gu2:sync";
    private const string K_DEVICE = "gu2:device";
    private const string K_MESSAGES = "gu2:messages";
    private const string K_TACHES = "gu2:taches";
    private const string K_COTISATIONS = "gu2:cotisations";
    private const string K_TRESORERIE = "gu2:tresorerie";
    private const string K_MATERIELS = "gu2:materiels";
    private const string K_PRETS = "gu2:prets";
    private const string K_REUNIONS = "gu2:reunions";
    private const string K_SITUATIONS = "gu2:situations";
    private const string K_VOTES = "gu2:votes";
    private const string K_JOURNAL_MEDIA = "gu2:journal_media";

    public const int SESSION_DURATION_MINUTES = 30;
    public const string DEFAULT_PASSWORD = "motdepasse";
    /// <summary>Taille maximale d'une image ou d'une pièce jointe (5 Mo).</summary>
    public const long MAX_FILE_BYTES = 5L * 1024L * 1024L;
    /// <summary>Quota de stockage par défaut suivi par l'alerte de saturation (50 Mo).</summary>
    public const long StorageQuotaBytes = 50L * 1024L * 1024L;

    /// <summary>Quota effectif (configurable dans les paramètres, en Mo). Défaut : 5 Go.</summary>
    public long CurrentStorageQuotaBytes
    {
        get
        {
            var mb = _settings.StorageQuotaMb;
            if (mb <= 0) mb = 5120;
            return mb * 1024L * 1024L;
        }
    }

    /// <summary>Limite basse/haute du quota configurable (Mo).</summary>
    public const int StorageQuotaMbMin = 10;
    public const int StorageQuotaMbMax = 2048;
    /// <summary>Seuil d'avertissement (80 %) et critique (95 %) de saturation.</summary>
    public const double StorageWarningPercent = 80.0;
    public const double StorageCriticalPercent = 95.0;

    /// <summary>Enregistre l'ordre personnalisé des entrées du menu « … ».</summary>
    public void ReorderMenu(IReadOnlyList<string> order)
    {
        _settings.MenuOrder = order == null ? new List<string>() : order.Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
        SaveSettings();
        DataChanged?.Invoke();
    }

    /// <summary>Masque ou ré-affiche une entrée du menu « … » (Paramètres › Ordre du menu).</summary>
    public void SetMenuHidden(string key, bool hidden)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _settings.MenuHidden ??= new List<string>();
        var has = _settings.MenuHidden.Any(k => k.Equals(key, StringComparison.Ordinal));
        if (hidden && !has) _settings.MenuHidden.Add(key);
        if (!hidden && has) _settings.MenuHidden.RemoveAll(k => k.Equals(key, StringComparison.Ordinal));
        SaveSettings();
        DataChanged?.Invoke();
    }

    /// <summary>Entrée masquée dans le menu « … » ?</summary>
    public bool IsMenuHidden(string key)
        => !string.IsNullOrWhiteSpace(key)
            && (_settings.MenuHidden ?? new List<string>()).Any(k => k.Equals(key, StringComparison.Ordinal));

    /// <summary>Réinitialise l'ordre et l'affichage du menu « … » (ordre d'origine, tout visible).</summary>
    public void ResetMenu()
    {
        _settings.MenuOrder = new List<string>();
        _settings.MenuHidden = new List<string>();
        SaveSettings();
        DataChanged?.Invoke();
    }

    private readonly IStorageProvider _storage;
    private readonly List<UserAccount> _users = new();
    private readonly List<FieldDefinition> _fields = new();
    private readonly List<ActivityEntry> _activity = new();
    private readonly List<ArchivedUser> _archive = new();
    private readonly List<CalendarEvent> _events = new();
    private readonly List<DocumentItem> _documents = new();
    private readonly List<DocumentFolder> _folders = new();
    private CommunityStory _community = new();
    private List<RolePermissionSet> _permissions = new();
    private SyncSnapshot _sync = new();
    private readonly List<Message> _messages = new();
    private readonly List<Tache> _taches = new();
    private readonly List<Cotisation> _cotisations = new();
    private readonly List<TransactionTresorerie> _tresorerie = new();
    private readonly List<Materiel> _materiels = new();
    private readonly List<PretMateriel> _prets = new();
    private readonly List<Reunion> _reunions = new();
    private readonly List<Situation> _situations = new();
    private readonly List<Vote> _votes = new();
    private readonly List<MediaItem> _journalMedia = new();
    private AppSettings _settings = new();
    private SessionInfo? _session;
    private bool _initialized;

    public event Action? DataChanged;
    public event Action? SessionChanged;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        // Accents conservés tels quels (é/è/à…) dans le stockage local et les
        // paquets .gusync.json, au lieu d'échappements \u00e9.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Les collections stockées localement, exposées à l'éditeur DBA.</summary>
    public static IReadOnlyList<ManagedDataSet> ManagedDataSets { get; } = new[]
    {
        new ManagedDataSet("users", "Utilisateurs", "Comptes, coordonnées, rôles et mots de passe."),
        new ManagedDataSet("fields", "Champs personnalisés", "Définitions des champs visibles dans les formulaires."),
        new ManagedDataSet("activity", "Journal d'activité", "Historique local des opérations."),
new ManagedDataSet("archives", "Archives des utilisateurs", "Comptes supprimés et informations d'archivage."),
        new ManagedDataSet("events", "Calendrier", "Événements, réunions, anniversaires et rappels."),
        new ManagedDataSet("documents", "Documents", "Fichiers importés ou scannés (PDF, images, pièces jointes)."),
        new ManagedDataSet("folders", "Dossiers", "Dossiers de classement de la gestion documentaire."),
        new ManagedDataSet("community", "Histoire de la communauté", "Récit interactif modifiable depuis le menu « … »."),
        new ManagedDataSet("permissions", "Rôles et permissions", "Matrice des droits par rôle, modifiable depuis le menu « … »."),
        new ManagedDataSet("messages", "Messagerie", "Messages internes échangés sur l'appareil, depuis le menu « … »."),
        new ManagedDataSet("taches", "Tâches", "Tâches et suivi d'avancement, depuis le menu « … »."),
        new ManagedDataSet("cotisations", "Cotisations", "Cotisations des membres, depuis le menu « … »."),
        new ManagedDataSet("tresorerie", "Trésorerie", "Entrées et sorties de caisse, depuis le menu « … »."),
        new ManagedDataSet("materiels", "Matériel", "Inventaire du matériel, depuis le menu « … »."),
        new ManagedDataSet("prets", "Prêts de matériel", "Prêts en cours et retours, depuis le menu « … »."),
        new ManagedDataSet("reunions", "Réunions", "Réunions et feuilles de présence, depuis le menu « … »."),
        new ManagedDataSet("situations", "Famangiana et situations", "Famangiana, visites, accompagnements et suivis, depuis le menu « … »."),
        new ManagedDataSet("votes", "Votes internes", "Votes et décisions internes, depuis le menu « … »."),
        new ManagedDataSet("journal_media", "Journal multimédia", "Photos, vidéos et audios, depuis le menu « … »."),
        new ManagedDataSet("settings", "Paramètres", "Nom, thème, couleur, logo et préférences de l'application.")
    };

    public DataStore(IStorageProvider storage)
    {
        _storage = storage;
    }

    public string[] ClientStorageKeys => new[]
    {
        K_USERS, K_FIELDS, K_ACTIVITY, K_SESSION, K_SETTINGS, K_ARCHIVE, K_EVENTS, K_DOCUMENTS, K_FOLDERS, K_COMMUNITY, K_PERMISSIONS, K_SYNC, K_DEVICE, K_MESSAGES, K_TACHES, K_COTISATIONS, K_TRESORERIE, K_MATERIELS, K_PRETS, K_REUNIONS, K_SITUATIONS, K_VOTES, K_JOURNAL_MEDIA
    };

    // ---------- Persistance interne ----------

    public void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        // Paramètres
        var settingsRaw = _storage.Get(K_SETTINGS);
        _settings = settingsRaw == null ? new AppSettings() : Deserialize<AppSettings>(settingsRaw) ?? new AppSettings();
        _settings.MenuOrder ??= new List<string>();
        _settings.MenuHidden ??= new List<string>();
        _settings.GuideVu ??= new List<string>();
        _settings.AideVu ??= new List<string>();
        _settings.ThemesUtilisateurs ??= new Dictionary<string, string>();
        _settings.ConseilsUtilisateurs ??= new Dictionary<string, bool>();

        if (string.IsNullOrWhiteSpace(_settings.AppName) || _settings.AppName == "GestionUtilisateurs")
        {
            _settings.AppName = "Gestion des utilisateurs";
            SaveSettings();
        }

        // Session
        var sessionRaw = _storage.Get(K_SESSION);
        if (!string.IsNullOrEmpty(sessionRaw)) _session = Deserialize<SessionInfo>(sessionRaw);

        var usersRaw = _storage.Get(K_USERS);
        if (usersRaw != null)
        {
            _users.Clear();
            _users.AddRange(Deserialize<List<UserAccount>>(usersRaw) ?? new List<UserAccount>());
            _fields.AddRange(Deserialize<List<FieldDefinition>>(_storage.Get(K_FIELDS)) ?? new List<FieldDefinition>());
            _activity.AddRange(Deserialize<List<ActivityEntry>>(_storage.Get(K_ACTIVITY)) ?? new List<ActivityEntry>());
            _archive.AddRange(Deserialize<List<ArchivedUser>>(_storage.Get(K_ARCHIVE)) ?? new List<ArchivedUser>());
            _events.AddRange(Deserialize<List<CalendarEvent>>(_storage.Get(K_EVENTS)) ?? new List<CalendarEvent>());
            _documents.AddRange(Deserialize<List<DocumentItem>>(_storage.Get(K_DOCUMENTS)) ?? new List<DocumentItem>());
            _folders.AddRange(Deserialize<List<DocumentFolder>>(_storage.Get(K_FOLDERS)) ?? new List<DocumentFolder>());
            _community = Deserialize<CommunityStory>(_storage.Get(K_COMMUNITY)) ?? new CommunityStory();
            if (_community.Chapitres == null) _community.Chapitres = new List<CommunityChapter>();
            if (string.IsNullOrWhiteSpace(_community.Titre)) _community.Titre = "Histoire de notre communauté";
            _messages.Clear();
            _messages.AddRange(Deserialize<List<Message>>(_storage.Get(K_MESSAGES)) ?? new List<Message>());
            foreach (var m in _messages)
            {
                m.Destinataires ??= new List<string>();
                m.LuPar ??= new List<string>();
                m.DateModification ??= "";
            }
            _taches.Clear();
            _taches.AddRange(Deserialize<List<Tache>>(_storage.Get(K_TACHES)) ?? new List<Tache>());
            foreach (var t in _taches) t.Assignes ??= new List<string>();
            _cotisations.Clear();
            _cotisations.AddRange(Deserialize<List<Cotisation>>(_storage.Get(K_COTISATIONS)) ?? new List<Cotisation>());
            _tresorerie.Clear();
            _tresorerie.AddRange(Deserialize<List<TransactionTresorerie>>(_storage.Get(K_TRESORERIE)) ?? new List<TransactionTresorerie>());
            _materiels.Clear();
            _materiels.AddRange(Deserialize<List<Materiel>>(_storage.Get(K_MATERIELS)) ?? new List<Materiel>());
            _prets.Clear();
            _prets.AddRange(Deserialize<List<PretMateriel>>(_storage.Get(K_PRETS)) ?? new List<PretMateriel>());
            _reunions.Clear();
            _reunions.AddRange(Deserialize<List<Reunion>>(_storage.Get(K_REUNIONS)) ?? new List<Reunion>());
            foreach (var r in _reunions)
            {
                r.Participants ??= new List<string>();
                r.Presences ??= new Dictionary<string, string>();
            }
            _situations.Clear();
            _situations.AddRange(Deserialize<List<Situation>>(_storage.Get(K_SITUATIONS)) ?? new List<Situation>());
            _votes.Clear();
            _votes.AddRange(Deserialize<List<Vote>>(_storage.Get(K_VOTES)) ?? new List<Vote>());
            foreach (var v in _votes)
            {
                v.Options ??= new List<string>();
                v.Bulletins ??= new List<Bulletin>();
            }
            _journalMedia.Clear();
            _journalMedia.AddRange(Deserialize<List<MediaItem>>(_storage.Get(K_JOURNAL_MEDIA)) ?? new List<MediaItem>());
var usersNormalized = false;
            foreach (var user in _users)
            {
                var uppercaseName = (user.Nom ?? "").Trim().ToUpperInvariant();
                if (user.Nom != uppercaseName)
                {
                    user.Nom = uppercaseName;
                    usersNormalized = true;
                }
                if (!PasswordCipher.IsEncrypted(user.MotDePasse) && !string.IsNullOrEmpty(user.MotDePasse))
                {
                    user.MotDePasse = PasswordCipher.Encrypt(user.MotDePasse);
                    usersNormalized = true;
                }
            }
            if (usersNormalized) SaveUsers();
            EnsureSystemFields();
        }
        else if (OnlineMode)
        {
            InitializeEmptyOnline();
        }
        else
        {
            SeedDemoData();
        }

        // Permissions (matrice modifiable ; valeurs par défaut préservant le comportement actuel)
        _permissions = Deserialize<List<RolePermissionSet>>(_storage.Get(K_PERMISSIONS)) ?? new List<RolePermissionSet>();
        if (_permissions.Count == 0)
        {
            _permissions = DefaultPermissions();
            SavePermissions();
        }
        else
        {
            NormalizePermissionKeys();
        }

        // Identité de l'appareil pour la synchronisation locale
        var deviceId = _storage.Get(K_DEVICE);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            deviceId = Guid.NewGuid().ToString("N");
            _storage.Set(K_DEVICE, deviceId);
        }

        // État de la dernière synchro (base commune pour la détection de conflits)
        _sync = Deserialize<SyncSnapshot>(_storage.Get(K_SYNC)) ?? new SyncSnapshot();
        _sync.Hashes ??= new Dictionary<string, string>();
        _sync.Dismissed ??= new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(_sync.DeviceId)) _sync.DeviceId = deviceId;

        // Anniversaires et dates des fiches membres -> calendrier (création/mise à jour automatique).
        try { SyncAllUserDateEvents(); } catch { }
    }

    /// <summary>
    /// Garantit la présence du champ « compte » (identifiant) dans la liste des champs
    /// personnalisés pour compatibilité avec les installations existantes.
    /// Supprime l'ancien champ système « Département ».
    /// </summary>
    private void EnsureSystemFields()
    {
        const string oldDept = "__departement"; // ancien champ système, désormais supprimé
        var hasIdent = _fields.Any(f => f.Id == FieldDefinition.FIELD_IDENTIFIANT);
        if (hasIdent && _fields.All(f => f.Id != oldDept))
        {
            var ident = _fields.First(f => f.Id == FieldDefinition.FIELD_IDENTIFIANT);
            if (ident.Requis)
            {
                // L'identifiant est généré automatiquement pour un utilisateur simple.
                ident.Requis = false;
                SaveFields();
            }
            return;
        }

        _fields.RemoveAll(f => f.Id == FieldDefinition.FIELD_IDENTIFIANT || f.Id == oldDept);
        var ordered = new List<FieldDefinition>
        {
            SystemField(FieldDefinition.FIELD_IDENTIFIANT, "Identifiant", true)
        };
        ordered.AddRange(_fields);
        _fields.Clear();
        _fields.AddRange(ordered);
        ReindexFields();
        SaveFields();
    }

    private static FieldDefinition SystemField(string id, string label, bool requis) => new()
    {
        Id = id,
        Label = label,
        Type = FieldType.Texte,
        Requis = requis,
        Actif = true,
        SystemField = true,
        Ordre = 0
    };

    private void ReindexFields()
    {
        var idx = 0;
        foreach (var f in _fields) f.Ordre = idx++;
    }

    private T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrEmpty(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json, JsonOpts); }
        catch { return default; }
    }

    private string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOpts);

    private void SaveSettings() => _storage.Set(K_SETTINGS, Serialize(_settings));
    private void SaveUsers()
    {
        // En ligne, les mots de passe sont ceux des comptes Supabase : aucune copie
        // (même chiffrée) ne doit rester dans la collection lue par tous les membres.
        if (OnlineMode)
            foreach (var u in _users)
                if (!string.IsNullOrEmpty(u.MotDePasse)) u.MotDePasse = "";
        _storage.Set(K_USERS, Serialize(_users));
    }
    private void SaveFields() => _storage.Set(K_FIELDS, Serialize(_fields));
    private void SaveActivity() => _storage.Set(K_ACTIVITY, Serialize(_activity));
    private void SaveSession() =>
        _storage.Set(K_SESSION, _session == null ? "{}" : Serialize(_session));
    private void SaveArchive() => _storage.Set(K_ARCHIVE, Serialize(_archive));
    private void SaveEvents() => _storage.Set(K_EVENTS, Serialize(_events));
    private void SaveDocuments() => _storage.Set(K_DOCUMENTS, Serialize(_documents));
    private void SaveFolders() => _storage.Set(K_FOLDERS, Serialize(_folders));
    private void SaveCommunity() => _storage.Set(K_COMMUNITY, Serialize(_community));
    private void SavePermissions() => _storage.Set(K_PERMISSIONS, Serialize(_permissions));
    private void SaveSyncSnapshot() => _storage.Set(K_SYNC, Serialize(_sync));
    private void SaveMessages() => _storage.Set(K_MESSAGES, Serialize(_messages));
    private void SaveTaches() => _storage.Set(K_TACHES, Serialize(_taches));
    private void SaveCotisations() => _storage.Set(K_COTISATIONS, Serialize(_cotisations));
    private void SaveTresorerie() => _storage.Set(K_TRESORERIE, Serialize(_tresorerie));
    private void SaveMateriels() => _storage.Set(K_MATERIELS, Serialize(_materiels));
    private void SavePrets() => _storage.Set(K_PRETS, Serialize(_prets));
    private void SaveReunions() => _storage.Set(K_REUNIONS, Serialize(_reunions));
    private void SaveSituations() => _storage.Set(K_SITUATIONS, Serialize(_situations));
    private void SaveVotes() => _storage.Set(K_VOTES, Serialize(_votes));
    private void SaveJournalMedia() => _storage.Set(K_JOURNAL_MEDIA, Serialize(_journalMedia));

    public IReadOnlyList<FieldDefinition> Fields => _fields.OrderBy(f => f.Ordre).ThenBy(f => f.Label).ToList();

    // ---------- Journal ----------

    public void LogActivity(string type, string description, string? actor = null)
    {
        var current = GetCurrentUser();
        var username = actor ?? current?.Identifiant ?? "inconnu";
        _activity.Insert(0, new ActivityEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Date = DateTimeOffset.Now.ToString("O"),
            User = username,
            Type = type,
            Description = description
        });
        if (_activity.Count > 500) _activity.RemoveRange(500, _activity.Count - 500);
        SaveActivity();
        DataChanged?.Invoke();
    }

    public IReadOnlyList<ActivityEntry> Activity => _activity;

    public void ClearActivity(string? type = null)
    {
        if (string.IsNullOrEmpty(type))
        {
            _activity.Clear();
        }
        else
        {
            _activity.RemoveAll(a => a.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        }
        SaveActivity();
        DataChanged?.Invoke();
    }

    /// <summary>Supprime les entrées dont le type est dans la liste fournie (filtres « par famille »).</summary>
    public void ClearActivityByTypes(IEnumerable<string> types)
    {
        var set = new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);
        _activity.RemoveAll(a => set.Contains(a.Type));
        SaveActivity();
        DataChanged?.Invoke();
    }

    // ---------- Authentification ----------

    public OperationResult Login(string username, string password)
    {
        var user = _users.FirstOrDefault(u =>
            u.Identifiant.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase) &&
            PasswordCipher.Verify(u.MotDePasse, password));

        if (user == null)
            return OperationResult.Fail(L.T("Identifiant ou mot de passe incorrect."));

        if (!user.Actif)
            return OperationResult.Fail(L.T("Ce compte est inactif. Contactez un administrateur."));

        _session = new SessionInfo
        {
            UserId = user.Id,
            ExpiryUtcTicks = SessionTimeoutMinutes > 0
                ? DateTime.UtcNow.AddMinutes(SessionTimeoutMinutes).Ticks
                : long.MaxValue
        };
        SaveSession();
        user.DerniereConnexion = DateTimeOffset.Now.ToString("O");
        SaveUsers();
        LogActivity(ActivityTypes.Login, "Connexion de l'utilisateur " + user.Identifiant, user.Identifiant);
        SessionChanged?.Invoke();
        return OperationResult.Ok();
    }

    public void Logout()
    {
        var u = GetCurrentUser();
        if (u != null)
            LogActivity(ActivityTypes.Logout, "Déconnexion de l'utilisateur " + u.Identifiant, u.Identifiant);
        _session = null;
        RoleApercu = null;
        SaveSession();
        SessionChanged?.Invoke();
    }

    public bool IsAuthenticated => GetCurrentUser() != null;

    public UserAccount? GetCurrentUser()
    {
        if (_session == null) return null;
        if (DateTime.UtcNow.Ticks > _session.ExpiryUtcTicks)
        {
            _session = null;
            SaveSession();
            SessionChanged?.Invoke();
            return null;
        }
        return _users.FirstOrDefault(u => u.Id == _session.UserId && u.Actif);
    }

    /// <summary>
    /// Prolonge la session en cas d'activité (inactivité uniquement).
    /// Appelé à chaque interaction utilisateur ; la mise en pause / reprise de
    /// l'application (ouverture d'une autre app) ne déconnecte jamais.
    /// </summary>
    public void TouchSession()
    {
        if (_session == null) return;
        if (SessionTimeoutMinutes <= 0) return; // « Jamais » : pas d'expiration
        var now = DateTime.UtcNow;
        // Évite d'écrire à chaque frappe : ne prolonge que si l'échéance est dans
        // la seconde moitié du délai (ou déjà proche de l'expiration).
        var remaining = TimeSpan.FromTicks(_session.ExpiryUtcTicks - now.Ticks);
        var window = TimeSpan.FromMinutes(SessionTimeoutMinutes);
        if (remaining > window - TimeSpan.FromSeconds(30) && remaining < window) return;
        _session.ExpiryUtcTicks = now.AddMinutes(SessionTimeoutMinutes).Ticks;
        SaveSession();
    }

    public bool IsAdmin => Roles.IsAdmin(CurrentRole);

    public bool IsBaseAdmin => CurrentRole == Roles.BaseAdmin;

    public bool IsManager => IsAdmin || CurrentRole == Roles.Gestionnaire;

    /// <summary>Rôle qui décide de ce que l'on voit : celui du compte, ou celui de l'aperçu.</summary>
    public string? CurrentRole => RoleApercu ?? GetCurrentUser()?.Role;

/// <summary>Vérifie le mot de passe de l'utilisateur connecté (ex. déverrouiller la zone dangereuse).</summary>
    public bool VerifyPassword(string? password)
    {
        var u = GetCurrentUser();
        return u != null && !string.IsNullOrEmpty(password) && PasswordCipher.Verify(u.MotDePasse, password);
    }

    /// <summary>Durée de session configurée en minutes (0 = déconnexion automatique désactivée).</summary>
    public int SessionTimeoutMinutes => _settings.SessionTimeoutMinutes;

    /// <summary>Mot de passe réel d'un utilisateur, déchiffré (accessible au DBA).</summary>
    public string GetPlainPassword(string userId)
    {
        var u = FindUser(userId);
        return u == null ? "" : PasswordCipher.Decrypt(u.MotDePasse);
    }

    /// <summary>Forme chiffrée affichable (base64 sans le préfixe).</summary>
    public string GetEncryptedPasswordDisplay(string userId)
    {
        var u = FindUser(userId);
        return u == null ? "" : PasswordCipher.CipherText(u.MotDePasse);
    }

    /// <summary>Retourne la collection choisie en JSON pour l'éditeur de données du DBA.</summary>
    public string GetManagedDataJson(string key) => key switch
    {
        "users" => Serialize(_users),
        "fields" => Serialize(_fields),
        "activity" => Serialize(_activity),
"archives" => Serialize(_archive),
        "events" => Serialize(_events),
        "documents" => Serialize(_documents),
        "folders" => Serialize(_folders),
        "community" => Serialize(_community),
        "permissions" => Serialize(_permissions),
        "messages" => Serialize(_messages),
        "taches" => Serialize(_taches),
        "cotisations" => Serialize(_cotisations),
        "tresorerie" => Serialize(_tresorerie),
        "materiels" => Serialize(_materiels),
        "prets" => Serialize(_prets),
        "reunions" => Serialize(_reunions),
        "situations" => Serialize(_situations),
        "votes" => Serialize(_votes),
        "journal_media" => Serialize(_journalMedia),
        "settings" => Serialize(_settings),
        _ => ""
    };

    /// <summary>
    /// Enregistre directement une collection locale depuis l'éditeur DBA. La syntaxe et
    /// les invariants essentiels sont vérifiés pour éviter de rendre le stockage inutilisable.
    /// </summary>
    public OperationResult SaveManagedDataJson(string key, string json)
    {
        if (!IsBaseAdmin)
            return OperationResult.Fail(L.T("Seul le gestionnaire de la base peut modifier directement ces données."));
        if (string.IsNullOrWhiteSpace(json))
            return OperationResult.Fail(L.T("La donnée JSON ne peut pas être vide."));

        try
        {
            switch (key)
            {
                case "users":
                {
                    var users = Deserialize<List<UserAccount>>(json);
                    if (users == null) return OperationResult.Fail(L.T("La liste des utilisateurs est invalide."));
                    var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    var matricules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var user in users)
                    {
                        user.Id = string.IsNullOrWhiteSpace(user.Id) ? Guid.NewGuid().ToString("N") : user.Id.Trim();
                        user.Prenom = (user.Prenom ?? "").Trim();
                        user.Nom = (user.Nom ?? "").Trim().ToUpperInvariant();
                        user.Identifiant = (user.Identifiant ?? "").Trim();
                        user.Role = NormalizeRole(user.Role);
                        user.Email = (user.Email ?? "").Trim();
                        user.Telephone = (user.Telephone ?? "").Trim();
                        user.DateNaissance = NormalizeYmd(user.DateNaissance);
                        user.Matricule = (user.Matricule ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(user.Signature)) user.Signature = null;
                        user.Champs ??= new Dictionary<string, FieldValue>();
                        if (string.IsNullOrWhiteSpace(user.Prenom) || string.IsNullOrWhiteSpace(user.Nom) || string.IsNullOrWhiteSpace(user.Identifiant))
                            return OperationResult.Fail(L.T("Chaque utilisateur doit avoir un prénom, un nom et un identifiant."));
                        if (!ids.Add(user.Id) || !identifiers.Add(user.Identifiant))
                            return OperationResult.Fail(L.T("Les identifiants techniques et les identifiants de connexion doivent être uniques."));
                        if (user.Matricule.Length > 0 && !matricules.Add(user.Matricule))
                            return OperationResult.Fail(L.F("Les matricules doivent être uniques (« {0} » en double).", user.Matricule));
                        if (string.IsNullOrWhiteSpace(user.DateCreation)) user.DateCreation = DateTimeOffset.Now.ToString("O");
                    }
                    _users.Clear();
                    _users.AddRange(users);
                    SaveUsers();
                    break;
                }
                case "fields":
                {
                    var fields = Deserialize<List<FieldDefinition>>(json);
                    if (fields == null) return OperationResult.Fail(L.T("La liste des champs est invalide."));
                    foreach (var field in fields)
                    {
                        field.Id = string.IsNullOrWhiteSpace(field.Id) ? Guid.NewGuid().ToString("N") : field.Id.Trim();
                        field.Label = (field.Label ?? "").Trim();
                        field.Options ??= new List<string>();
                        if (string.IsNullOrWhiteSpace(field.Label)) return OperationResult.Fail(L.T("Chaque champ doit avoir un libellé."));
                    }
                    _fields.Clear();
                    _fields.AddRange(fields);
                    EnsureSystemFields();
                    break;
                }
                case "activity":
                {
                    var activity = Deserialize<List<ActivityEntry>>(json);
                    if (activity == null) return OperationResult.Fail(L.T("Le journal est invalide."));
                    foreach (var entry in activity)
                    {
                        entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id.Trim();
                        if (string.IsNullOrWhiteSpace(entry.Date)) entry.Date = DateTimeOffset.Now.ToString("O");
                    }
                    _activity.Clear();
                    _activity.AddRange(activity.Take(500));
                    SaveActivity();
                    break;
                }
                case "archives":
                {
                    var archive = Deserialize<List<ArchivedUser>>(json);
                    if (archive == null) return OperationResult.Fail(L.T("Les archives sont invalides."));
                    foreach (var item in archive)
                    {
                        item.User ??= new UserAccount();
                        item.User.Nom = (item.User.Nom ?? "").Trim().ToUpperInvariant();
                        item.User.Prenom = (item.User.Prenom ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(item.User.Id)) item.User.Id = Guid.NewGuid().ToString("N");
                        if (string.IsNullOrWhiteSpace(item.DeletedAt)) item.DeletedAt = DateTimeOffset.Now.ToString("O");
                    }
                    _archive.Clear();
                    _archive.AddRange(archive);
                    SaveArchive();
                    break;
                }
case "settings":
                {
                    var settings = Deserialize<AppSettings>(json);
                    if (settings == null) return OperationResult.Fail(L.T("Les paramètres sont invalides."));
                    settings.AppName = string.IsNullOrWhiteSpace(settings.AppName) ? "Gestion des utilisateurs" : settings.AppName.Trim();
                    settings.Theme = settings.Theme == "dark" ? "dark" : "light";
                    settings.Color = string.IsNullOrWhiteSpace(settings.Color) ? "#4f46e5" : settings.Color.Trim();
                    settings.PageSize = Math.Clamp(settings.PageSize, 10, 100);
                    _settings = settings;
                    SaveSettings();
                    break;
                }
                case "events":
                {
                    var events = Deserialize<List<CalendarEvent>>(json);
                    if (events == null) return OperationResult.Fail(L.T("La liste des événements est invalide."));
                    foreach (var item in events)
                    {
                        item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim();
                        item.Titre = (item.Titre ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(item.Date)) return OperationResult.Fail(L.T("Chaque événement doit avoir une date."));
                    }
                    _events.Clear();
                    _events.AddRange(events);
                    SaveEvents();
                    break;
                }
                case "documents":
                {
                    var documents = Deserialize<List<DocumentItem>>(json);
                    if (documents == null) return OperationResult.Fail(L.T("La liste des documents est invalide."));
                    foreach (var item in documents)
                    {
                        item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim();
                        item.Titre = (item.Titre ?? "").Trim();
                        item.Tags ??= new List<string>();
                        if (string.IsNullOrWhiteSpace(item.Titre)) return OperationResult.Fail(L.T("Chaque document doit avoir un titre."));
                    }
                    _documents.Clear();
                    _documents.AddRange(documents);
                    SaveDocuments();
                    break;
                }
                case "folders":
                {
                    var folders = Deserialize<List<DocumentFolder>>(json);
                    if (folders == null) return OperationResult.Fail(L.T("La liste des dossiers est invalide."));
                    foreach (var item in folders)
                    {
                        item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim();
                        item.Nom = (item.Nom ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(item.Nom)) return OperationResult.Fail(L.T("Chaque dossier doit avoir un nom."));
                    }
                    _folders.Clear();
                    _folders.AddRange(folders);
                    SaveFolders();
                    break;
                }
                case "community":
                {
                    var story = Deserialize<CommunityStory>(json);
                    if (story == null) return OperationResult.Fail(L.T("L'histoire de la communauté est invalide."));
                    story.Titre = string.IsNullOrWhiteSpace(story.Titre) ? "Histoire de notre communauté" : story.Titre.Trim();
                    story.SousTitre ??= "";
                    story.Chapitres ??= new List<CommunityChapter>();
                    foreach (var c in story.Chapitres)
                    {
                        c.Id = string.IsNullOrWhiteSpace(c.Id) ? Guid.NewGuid().ToString("N") : c.Id.Trim();
                        c.Titre = (c.Titre ?? "").Trim();
                        c.Texte ??= "";
                        c.Date ??= "";
                    }
                    _community = story;
                    SaveCommunity();
                    break;
                }
                case "permissions":
                {
                    var permissions = Deserialize<List<RolePermissionSet>>(json);
                    if (permissions == null) return OperationResult.Fail(L.T("Les permissions sont invalides."));
                    var check = ValidatePermissionSets(permissions);
                    if (!check.Success) return check;
                    _permissions = permissions;
                    SavePermissions();
                    break;
                }
                case "messages":
                {
                    var messages = Deserialize<List<Message>>(json);
                    if (messages == null) return OperationResult.Fail(L.T("La messagerie est invalide."));
                    foreach (var m in messages)
                    {
                        m.Id = string.IsNullOrWhiteSpace(m.Id) ? Guid.NewGuid().ToString("N") : m.Id.Trim();
                        m.ConversationId ??= "";
                        m.Expediteur ??= "";
                        m.Destinataires ??= new List<string>();
                        m.Texte ??= "";
                        m.LuPar ??= new List<string>();
                        m.DateModification ??= "";
                        if (string.IsNullOrWhiteSpace(m.Date)) m.Date = DateTimeOffset.Now.ToString("O");
                    }
                    _messages.Clear();
                    _messages.AddRange(messages);
                    SaveMessages();
                    break;
                }
                case "taches":
                {
                    var taches = Deserialize<List<Tache>>(json);
                    if (taches == null) return OperationResult.Fail(L.T("La liste des tâches est invalide."));
                    foreach (var t in taches)
                    {
                        t.Id = string.IsNullOrWhiteSpace(t.Id) ? Guid.NewGuid().ToString("N") : t.Id.Trim();
                        t.Titre = (t.Titre ?? "").Trim();
                        t.Description ??= "";
                        t.Assignes ??= new List<string>();
                        if (string.IsNullOrWhiteSpace(t.Titre)) return OperationResult.Fail(L.T("Chaque tâche doit avoir un titre."));
                    }
                    _taches.Clear();
                    _taches.AddRange(taches);
                    SaveTaches();
                    break;
                }
                case "cotisations":
                {
                    var cotisations = Deserialize<List<Cotisation>>(json);
                    if (cotisations == null) return OperationResult.Fail(L.T("Les cotisations sont invalides."));
                    _cotisations.Clear();
                    _cotisations.AddRange(cotisations);
                    SaveCotisations();
                    break;
                }
                case "tresorerie":
                {
                    var treso = Deserialize<List<TransactionTresorerie>>(json);
                    if (treso == null) return OperationResult.Fail(L.T("La trésorerie est invalide."));
                    _tresorerie.Clear();
                    _tresorerie.AddRange(treso);
                    SaveTresorerie();
                    break;
                }
                case "materiels":
                {
                    var materiels = Deserialize<List<Materiel>>(json);
                    if (materiels == null) return OperationResult.Fail(L.T("Le matériel est invalide."));
                    _materiels.Clear();
                    _materiels.AddRange(materiels);
                    SaveMateriels();
                    break;
                }
                case "prets":
                {
                    var prets = Deserialize<List<PretMateriel>>(json);
                    if (prets == null) return OperationResult.Fail(L.T("Les prêts sont invalides."));
                    _prets.Clear();
                    _prets.AddRange(prets);
                    SavePrets();
                    break;
                }
                case "reunions":
                {
                    var reunions = Deserialize<List<Reunion>>(json);
                    if (reunions == null) return OperationResult.Fail(L.T("Les réunions sont invalides."));
                    _reunions.Clear();
                    _reunions.AddRange(reunions);
                    SaveReunions();
                    break;
                }
                case "situations":
                {
                    var situations = Deserialize<List<Situation>>(json);
                    if (situations == null) return OperationResult.Fail(L.T("Les situations sont invalides."));
                    _situations.Clear();
                    _situations.AddRange(situations);
                    SaveSituations();
                    break;
                }
                case "votes":
                {
                    var votes = Deserialize<List<Vote>>(json);
                    if (votes == null) return OperationResult.Fail(L.T("Les votes sont invalides."));
                    _votes.Clear();
                    _votes.AddRange(votes);
                    SaveVotes();
                    break;
                }
                case "journal_media":
                {
                    var media = Deserialize<List<MediaItem>>(json);
                    if (media == null) return OperationResult.Fail(L.T("Le journal multimédia est invalide."));
                    _journalMedia.Clear();
                    _journalMedia.AddRange(media);
                    SaveJournalMedia();
                    break;
                }
                default:
                    return OperationResult.Fail(L.T("Collection de données inconnue."));
            }
        }
        catch (JsonException ex)
        {
            return OperationResult.Fail(L.F("JSON invalide : {0}", ex.Message));
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(L.F("Impossible d'enregistrer la donnée : {0}", ex.Message));
        }

        LogActivity(ActivityTypes.Update, "Modification directe de la collection « " + key + " » par le DBA.");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    // ---------- Utilisateurs ----------

    public IReadOnlyList<UserAccount> Users => _users;

    public UserAccount? FindUser(string id) => _users.FirstOrDefault(u => u.Id == id);

    /// <summary>Retrouve un utilisateur par identifiant de connexion (insensible à la casse).</summary>
    public UserAccount? FindUserByIdentifier(string? identifier)
        => _users.FirstOrDefault(u => string.Equals(u.Identifiant, identifier ?? "", StringComparison.OrdinalIgnoreCase));

    /// <summary>Garde fous : suppression, statut, rôle du dernier administrateur.</summary>
    private UserAccount? LastAdminGuard(UserAccount target, bool deactivateOrRoleChange = false)
    {
        if (target.Role is not (Roles.Administrateur or Roles.BaseAdmin)) return null;
        // Le DBA et l'administrateur applicatif ont des responsabilités distinctes :
        // l'un ne doit pas permettre de retirer le dernier compte de l'autre rôle.
        var admins = _users.Where(u => u.Role == target.Role).ToList();
        if (admins.Count <= 1)
            return target; // dernier admin : intouchable
        return null;
    }

    public UserOperationResult AddUser(UserAccount draft)
    {
        draft.Nom = (draft.Nom ?? "").Trim().ToUpperInvariant();
        draft.Prenom = (draft.Prenom ?? "").Trim();
        var autoIdentifiant = string.IsNullOrWhiteSpace(draft.Identifiant);
        if (autoIdentifiant)
            draft.Identifiant = GenerateIdentifiant(draft.Prenom, draft.Nom);
        if (_users.Any(u => u.Identifiant.Equals(draft.Identifiant.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            if (!autoIdentifiant)
                return new UserOperationResult { Success = false, Error = L.T("Cet identifiant existe déjà. Aucun utilisateur n'a été ajouté.") };
            draft.Identifiant = IdentifiantLibre(draft.Identifiant);
        }
        if (OnlineMode)
            draft.MotDePasse = ""; // géré par Supabase, jamais copié dans les données partagées
        else if (string.IsNullOrWhiteSpace(draft.MotDePasse))
            draft.MotDePasse = PasswordCipher.Encrypt(DEFAULT_PASSWORD);
        else
            draft.MotDePasse = PasswordCipher.Encrypt(draft.MotDePasse);

        draft.Id = Guid.NewGuid().ToString("N");
        draft.DateNaissance = NormalizeYmd(draft.DateNaissance);
        draft.Matricule = (draft.Matricule ?? "").Trim();
        if (draft.Matricule.Length > 0 && _users.Any(u => (u.Matricule ?? "").Trim().Equals(draft.Matricule, StringComparison.OrdinalIgnoreCase)))
            return new UserOperationResult { Success = false, Error = L.T("Ce matricule existe déjà. Aucun utilisateur n'a été ajouté.") };
        if (string.IsNullOrEmpty(draft.DateCreation))
            draft.DateCreation = DateTimeOffset.Now.ToString("O");
        // Matricule automatique quand un format est réglé et que rien n'a été saisi.
        if (draft.Matricule.Length == 0) draft.Matricule = ProchainMatricule(draft);
        _users.Add(draft);
        SaveUsers();
        try { SyncUserDateEvents(draft); } catch { }
        LogActivity(ActivityTypes.Create, "Création de l'utilisateur " + draft.Identifiant);
        DataChanged?.Invoke();
        return new UserOperationResult { Success = true, UserId = draft.Id };
    }

    /// <summary>Identifiant de base : le prénom (« jean.pierre » pour deux prénoms),
    /// ou le nom quand il n'y a pas de prénom. <see cref="IdentifiantLibre(string)"/> ajoute 1, 2… aux homonymes.</summary>
    public static string GenerateIdentifiant(string prenom, string nom)
    {
        static string Clean(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ch in (s ?? "").ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD))
            {
                var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_') sb.Append(ch);
                else if (char.IsWhiteSpace(ch)) sb.Append('.');
            }
            var t = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\.{2,}", ".").Replace("--", "-").Trim('.');
            return string.IsNullOrWhiteSpace(t) ? "utilisateur" : t;
        }

        if (!string.IsNullOrWhiteSpace(prenom)) return Clean(prenom);
        return Clean(nom);
    }

    public UserOperationResult UpdateUser(string userId, UserAccount draft, bool passwordChanged)
    {
        var idx = _users.FindIndex(u => u.Id == userId);
        if (idx < 0) return new UserOperationResult { Success = false, Error = L.T("Utilisateur introuvable.") };

        var existing = _users[idx];
        var current = GetCurrentUser();

        if (existing.Role == Roles.BaseAdmin && !IsBaseAdmin)
            return new UserOperationResult { Success = false, Error = L.T("Seul le gestionnaire de la base peut modifier ce compte.") };
        if (existing.Role == Roles.BaseAdmin)
        {
            // Le compte technique garde son rôle, son statut et son identifiant via
            // l'interface ordinaire ; le DBA peut les gérer depuis l'éditeur dédié.
            draft.Identifiant = existing.Identifiant;
            draft.Role = existing.Role;
            draft.Actif = existing.Actif;
        }
        if (current != null && CurrentRole == Roles.Utilisateur)
        {
            if (current.Id != userId)
                return new UserOperationResult { Success = false, Error = L.T("Vous pouvez modifier uniquement vos propres informations.") };
            draft.Identifiant = existing.Identifiant;
            draft.Role = existing.Role;
            draft.Actif = existing.Actif;
            passwordChanged = false;
        }

        var guard = LastAdminGuard(existing);
        if (guard != null)
        {
            if (!existing.Actif) return new UserOperationResult { Success = false, Error = L.T("Le dernier administrateur est inactif et ne peut être modifié.") };
            if (!draft.Actif) return new UserOperationResult { Success = false, Error = L.T("Impossible de désactiver le dernier administrateur.") };
            if (draft.Role != existing.Role) return new UserOperationResult { Success = false, Error = L.T("Impossible de changer le rôle du dernier administrateur.") };
        }

        existing.Prenom = (draft.Prenom ?? "").Trim();
        existing.Nom = (draft.Nom ?? "").Trim().ToUpperInvariant();
        existing.Email = draft.Email;
        existing.Telephone = draft.Telephone;
        existing.Role = draft.Role;
        existing.Actif = draft.Actif;
        existing.Notes = draft.Notes;
        existing.Champs = draft.Champs;
        existing.Photo = draft.Photo;
        existing.Signature = draft.Signature;
        existing.DateNaissance = NormalizeYmd(draft.DateNaissance);
        existing.ResponsableLegal = (draft.ResponsableLegal ?? "").Trim();
        existing.ResponsableTelephone = (draft.ResponsableTelephone ?? "").Trim();
        var matricule = (draft.Matricule ?? "").Trim();
        if (matricule.Length > 0 && _users.Any(u => u.Id != userId && (u.Matricule ?? "").Trim().Equals(matricule, StringComparison.OrdinalIgnoreCase)))
            return new UserOperationResult { Success = false, Error = L.T("Ce matricule existe déjà.") };
        existing.Matricule = matricule;
        if (passwordChanged && !string.IsNullOrEmpty(draft.MotDePasse))
        {
            existing.MotDePasse = PasswordCipher.Encrypt(draft.MotDePasse);
            LogActivity(ActivityTypes.PasswordReset, "Changement de mot de passe pour " + existing.Identifiant);
        }

        SaveUsers();
        try { SyncUserDateEvents(existing); } catch { }
        LogActivity(ActivityTypes.Update, "Modification de l'utilisateur " + existing.Identifiant);
        DataChanged?.Invoke();
        return new UserOperationResult { Success = true, UserId = userId };
    }

    public OperationResult DeleteUser(string userId, string? actorUserId = null)
    {
        var target = FindUser(userId);
        if (target == null) return OperationResult.Fail(L.T("Utilisateur introuvable."));
        if (actorUserId == userId)
            return OperationResult.Fail(L.T("Impossible de supprimer votre propre compte."));
        if (target.Role == Roles.BaseAdmin && !IsBaseAdmin)
            return OperationResult.Fail(L.T("Seul le gestionnaire de la base peut supprimer ce compte."));
        if (LastAdminGuard(target) != null)
            return OperationResult.Fail(L.T("Impossible de supprimer le dernier administrateur."));

        _users.Remove(target);
        _archive.Insert(0, new ArchivedUser
        {
            User = target,
            DeletedAt = DateTimeOffset.Now.ToString("O"),
            DeletedBy = GetCurrentUser()?.Identifiant ?? "inconnu"
        });
        SaveUsers();
        SaveArchive();
        LogActivity(ActivityTypes.Delete, "Suppression (archivage) de l'utilisateur " + target.Identifiant + " (ID: " + target.Id + ")");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

public OperationResult SetUserActive(string userId, bool active)
    {
        var target = FindUser(userId);
        if (target == null) return OperationResult.Fail(L.T("Utilisateur introuvable."));
        if (target.Role == Roles.BaseAdmin && !IsAdmin)
            return OperationResult.Fail(L.T("Seul l'administrateur ou le gestionnaire de la base peut modifier ce compte."));
        if (!active)
        {
            var guard = LastAdminGuard(target);
            if (guard != null) return OperationResult.Fail(L.T("Impossible de désactiver le dernier administrateur actif."));
        }
        target.Actif = active;
        SaveUsers();
        LogActivity(active ? ActivityTypes.Activate : ActivityTypes.Deactivate,
            (active ? "Activation" : "Désactivation") + " de l'utilisateur " + target.Identifiant);
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult ResetPassword(string userId)
    {
        var target = FindUser(userId);
        if (target == null) return OperationResult.Fail(L.T("Utilisateur introuvable."));
        if (target.Role == Roles.BaseAdmin && !IsBaseAdmin)
            return OperationResult.Fail(L.T("Seul le gestionnaire de la base peut réinitialiser ce mot de passe."));
        target.MotDePasse = PasswordCipher.Encrypt(DEFAULT_PASSWORD);
        SaveUsers();
        LogActivity(ActivityTypes.PasswordReset,
            "Réinitialisation du mot de passe de " + target.Identifiant + " (« " + DEFAULT_PASSWORD + " »)");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    // ---------- Archives ----------

    public IReadOnlyList<ArchivedUser> ArchivedUsers => _archive;

    public UserAccount? FindArchivedUser(string archiveUserId) => _archive.FirstOrDefault(a => a.User.Id == archiveUserId)?.User;

    public OperationResult PurgeUser(string archiveUserId)
    {
        var idx = _archive.FindIndex(a => a.User.Id == archiveUserId);
        if (idx < 0) return OperationResult.Fail(L.T("Utilisateur archivé introuvable."));
        var archived = _archive[idx];
        _archive.RemoveAt(idx);
        SaveArchive();
        LogActivity(ActivityTypes.Delete, "Suppression définitive de l'archivage de " + archived.User.Identifiant);
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult RestoreArchivedUser(string archiveUserId)
    {
        var idx = _archive.FindIndex(a => a.User.Id == archiveUserId);
        if (idx < 0) return OperationResult.Fail(L.T("Utilisateur archivé introuvable."));
        var archived = _archive[idx];
        var draft = CloneImportUser(archived.User);
        // Garder l'identifiant d'origine : une fiche reliée à un compte en ligne le
        // retrouve ainsi (le compte est relié à la fiche par cet Id).
        draft.Id = string.IsNullOrWhiteSpace(archived.User.Id) || _users.Any(u => u.Id == archived.User.Id)
            ? Guid.NewGuid().ToString("N")
            : archived.User.Id;

        if (_users.Any(u => u.Identifiant.Equals(draft.Identifiant.Trim(), StringComparison.OrdinalIgnoreCase)))
            return OperationResult.Fail(L.T("Un utilisateur actif porte déjà cet identifiant. Restauration impossible."));

        _users.Add(draft);
        _archive.RemoveAt(idx);
        SaveUsers();
        SaveArchive();
        LogActivity(ActivityTypes.Create, "Restauration de l'utilisateur " + draft.Identifiant + " depuis les archives");
        DataChanged?.Invoke();
        return new UserOperationResult { Success = true, UserId = draft.Id };
    }

    public OperationResult PurgeAllArchive()
    {
        var count = _archive.Count;
        _archive.Clear();
        SaveArchive();
        if (count > 0)
        {
            LogActivity(ActivityTypes.Delete, "Vidage des archives (" + count + " utilisateur(s) supprimé(s) définitivement).");
            DataChanged?.Invoke();
        }
        return OperationResult.Ok();
    }

    // ---------- Calendrier ----------

    public IReadOnlyList<CalendarEvent> Events => _events;

    public CalendarEvent? FindEvent(string eventId) => _events.FirstOrDefault(e => e.Id == eventId);

    public OperationResult AddEvent(CalendarEvent draft)
    {
        draft.Titre = (draft.Titre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Titre))
            return OperationResult.Fail(L.T("Le titre de l'événement est obligatoire."));
        if (string.IsNullOrWhiteSpace(draft.Date))
            return OperationResult.Fail(L.T("La date de l'événement est obligatoire."));

        draft.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrEmpty(draft.DateCreation))
            draft.DateCreation = DateTimeOffset.Now.ToString("O");
        draft.CreatedBy = GetCurrentUser()?.Identifiant ?? "inconnu";

        _events.Add(draft);
        _events.Sort((a, b) => SortKey(a).CompareTo(SortKey(b)));
        SaveEvents();
        LogActivity(ActivityTypes.CalendarCreate, "Ajout de l'événement « " + draft.Titre + " » (" + L.EnFrancais(() => CalendarEventTypes.Label(draft.Type)) + ")");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult UpdateEvent(string eventId, CalendarEvent draft)
    {
        var idx = _events.FindIndex(e => e.Id == eventId);
        if (idx < 0) return OperationResult.Fail(L.T("Événement introuvable."));

        draft.Titre = (draft.Titre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Titre))
            return OperationResult.Fail(L.T("Le titre de l'événement est obligatoire."));
        if (string.IsNullOrWhiteSpace(draft.Date))
            return OperationResult.Fail(L.T("La date de l'événement est obligatoire."));

        var existing = _events[idx];
        var oldTitle = existing.Titre;
        existing.Type = draft.Type;
        existing.Titre = draft.Titre;
        existing.Date = draft.Date;
        existing.HeureDebut = draft.HeureDebut;
        existing.HeureFin = draft.HeureFin;
        existing.TouteLaJournee = draft.TouteLaJournee;
        existing.Description = draft.Description;
        existing.Lieu = draft.Lieu;
        existing.PersonneConcernee = draft.PersonneConcernee;
        existing.PersonnesConcernees = draft.PersonnesConcernees ?? new List<string>();
        existing.Priorite = draft.Priorite;
        existing.Repetition = draft.Repetition;
        existing.Statut = draft.Statut;
        existing.RappelAvant = draft.RappelAvant;
        existing.Notes = draft.Notes;

        _events.Sort((a, b) => SortKey(a).CompareTo(SortKey(b)));
        SaveEvents();
        LogActivity(ActivityTypes.CalendarUpdate, "Modification de l'événement « " + oldTitle + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult DeleteEvent(string eventId)
    {
        var idx = _events.FindIndex(e => e.Id == eventId);
        if (idx < 0) return OperationResult.Fail(L.T("Événement introuvable."));
        var evt = _events[idx];
        _events.RemoveAt(idx);
        SaveEvents();
        LogActivity(ActivityTypes.CalendarDelete, "Suppression de l'événement « " + evt.Titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Clé de tri : date (au plus tôt), puis « toute la journée » en premier, puis heure de début.</summary>
    private static string SortKey(CalendarEvent e)
    {
        var day = e.TouteLaJournee ? "00:00" : e.HeureDebut ?? "00:00";
        return e.Date + "|" + day + "|" + (e.Type);
    }

    // ---------- Gestion documentaire ----------

    public IReadOnlyList<DocumentFolder> Folders => _folders.OrderBy(f => f.Nom).ToList();

    public IReadOnlyList<DocumentItem> Documents => _documents;

    public DocumentFolder? FindFolder(string folderId) => _folders.FirstOrDefault(f => f.Id == folderId);

    public DocumentItem? FindDocument(string documentId) => _documents.FirstOrDefault(d => d.Id == documentId);

    public OperationResult AddFolder(DocumentFolder draft)
    {
        draft.Nom = (draft.Nom ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Nom))
            return OperationResult.Fail(L.T("Le nom du dossier est obligatoire."));
        if (!string.IsNullOrWhiteSpace(draft.ParentId) && FindFolder(draft.ParentId) == null)
            return OperationResult.Fail(L.T("Le dossier parent est introuvable."));
        if (_folders.Any(f => string.Equals(f.ParentId ?? "", draft.ParentId ?? "", StringComparison.Ordinal)
            && f.Nom.Equals(draft.Nom, StringComparison.OrdinalIgnoreCase)))
            return OperationResult.Fail(L.T("Un dossier porte déjà ce nom à cet emplacement."));

        draft.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrEmpty(draft.DateCreation))
            draft.DateCreation = DateTimeOffset.Now.ToString("O");
        if (string.IsNullOrWhiteSpace(draft.Couleur))
            draft.Couleur = DocumentCategories.CouleurDefaut;
        draft.CreatedBy = GetCurrentUser()?.Identifiant ?? "inconnu";

        _folders.Add(draft);
        SaveFolders();
        LogActivity(ActivityTypes.DossierCreate, "Création du dossier « " + draft.Nom + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult RenameFolder(string folderId, string newName, string? color = null)
    {
        var folder = FindFolder(folderId);
        if (folder == null) return OperationResult.Fail(L.T("Dossier introuvable."));
        newName = (newName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(newName))
            return OperationResult.Fail(L.T("Le nom du dossier est obligatoire."));
        if (_folders.Any(f => f.Id != folderId && string.Equals(f.ParentId ?? "", folder.ParentId ?? "", StringComparison.Ordinal)
            && f.Nom.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            return OperationResult.Fail(L.T("Un dossier porte déjà ce nom à cet emplacement."));

        var old = folder.Nom;
        folder.Nom = newName;
        if (!string.IsNullOrWhiteSpace(color)) folder.Couleur = color.Trim();
        SaveFolders();
        LogActivity(ActivityTypes.DossierUpdate, "Dossier renommé : « " + old + " » → « " + newName + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>
    /// Supprime un dossier : son contenu (documents et sous-dossiers) remonte
    /// au dossier parent (ou à la racine). Les documents ne sont jamais perdus.
    /// </summary>
    public OperationResult DeleteFolder(string folderId)
    {
        var folder = FindFolder(folderId);
        if (folder == null) return OperationResult.Fail(L.T("Dossier introuvable."));

        foreach (var doc in _documents.Where(d => d.FolderId == folderId))
            doc.FolderId = folder.ParentId;
        foreach (var sub in _folders.Where(f => f.ParentId == folderId))
            sub.ParentId = folder.ParentId;

        _folders.Remove(folder);
        SaveFolders();
        SaveDocuments();
        LogActivity(ActivityTypes.DossierDelete, "Suppression du dossier « " + folder.Nom + " » (contenu conservé)");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult AddDocument(DocumentItem draft)
    {
        draft.Titre = (draft.Titre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Titre))
            return OperationResult.Fail(L.T("Le titre du document est obligatoire."));
        if (string.IsNullOrWhiteSpace(draft.Data))
            return OperationResult.Fail(L.T("Le fichier du document est manquant."));
        if (!string.IsNullOrWhiteSpace(draft.FolderId) && FindFolder(draft.FolderId) == null)
            return OperationResult.Fail(L.T("Le dossier de destination est introuvable."));
        if (!string.IsNullOrWhiteSpace(draft.UtilisateurId) && FindUserByIdentifier(draft.UtilisateurId) == null)
            return OperationResult.Fail(L.T("L'utilisateur associé est introuvable."));

        draft.Id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.Now.ToString("O");
        if (string.IsNullOrEmpty(draft.DateCreation)) draft.DateCreation = now;
        draft.DateModification = now;
        draft.Tags = (draft.Tags ?? new List<string>())
            .Select(t => (t ?? "").Trim()).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!ListesChoix.Contient(ListesChoix.CategoriesDocument, draft.Categorie, _settings.Listes)) draft.Categorie = DocumentCategories.Autre;
        draft.CreatedBy = GetCurrentUser()?.Identifiant ?? "inconnu";

        _documents.Add(draft);
        SaveDocuments();
        LogActivity(ActivityTypes.DocumentCreate, "Ajout du document « " + draft.Titre + " » (" + draft.FileName + ")");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult UpdateDocument(string documentId, DocumentItem draft)
    {
        var existing = FindDocument(documentId);
        if (existing == null) return OperationResult.Fail(L.T("Document introuvable."));
        draft.Titre = (draft.Titre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Titre))
            return OperationResult.Fail(L.T("Le titre du document est obligatoire."));
        if (!string.IsNullOrWhiteSpace(draft.FolderId) && FindFolder(draft.FolderId) == null)
            return OperationResult.Fail(L.T("Le dossier de destination est introuvable."));
        if (!string.IsNullOrWhiteSpace(draft.UtilisateurId) && FindUserByIdentifier(draft.UtilisateurId) == null)
            return OperationResult.Fail(L.T("L'utilisateur associé est introuvable."));

        var oldTitle = existing.Titre;
        existing.Titre = draft.Titre;
        existing.Description = draft.Description ?? "";
        existing.FolderId = draft.FolderId;
        existing.Categorie = ListesChoix.Contient(ListesChoix.CategoriesDocument, draft.Categorie, _settings.Listes) ? draft.Categorie : DocumentCategories.Autre;
        existing.Tags = (draft.Tags ?? new List<string>())
            .Select(t => (t ?? "").Trim()).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        existing.UtilisateurId = string.IsNullOrWhiteSpace(draft.UtilisateurId) ? null : draft.UtilisateurId.Trim();
        existing.DateModification = DateTimeOffset.Now.ToString("O");
        existing.ModifiePar = GetCurrentUser()?.Identifiant ?? "inconnu";

        SaveDocuments();
        LogActivity(ActivityTypes.DocumentUpdate, "Modification du document « " + oldTitle + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult SetDocumentFavorite(string documentId, bool favori)
    {
        var doc = FindDocument(documentId);
        if (doc == null) return OperationResult.Fail(L.T("Document introuvable."));
        doc.Favori = favori;
        doc.DateModification = DateTimeOffset.Now.ToString("O");
        SaveDocuments();
        LogActivity(ActivityTypes.DocumentUpdate, (favori ? "Ajout en favoris" : "Retrait des favoris") + " du document « " + doc.Titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult MoveDocuments(IEnumerable<string> documentIds, string? folderId)
    {
        if (!string.IsNullOrWhiteSpace(folderId) && FindFolder(folderId) == null)
            return OperationResult.Fail(L.T("Le dossier de destination est introuvable."));
        var count = 0;
        foreach (var id in documentIds.Distinct())
        {
            var doc = FindDocument(id);
            if (doc == null || doc.Supprime) continue;
            doc.FolderId = folderId;
            doc.DateModification = DateTimeOffset.Now.ToString("O");
            doc.ModifiePar = GetCurrentUser()?.Identifiant ?? "inconnu";
            count++;
        }
        if (count == 0) return OperationResult.Fail(L.T("Aucun document à déplacer."));
        SaveDocuments();
        var dest = string.IsNullOrWhiteSpace(folderId) ? "la racine" : "« " + (FindFolder(folderId)?.Nom ?? "?") + " »";
        LogActivity(ActivityTypes.DocumentUpdate, "Déplacement de " + count + " document(s) vers " + dest);
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Place des documents dans la corbeille (restaurables).</summary>
    public OperationResult TrashDocuments(IEnumerable<string> documentIds)
    {
        var count = 0;
        foreach (var id in documentIds.Distinct())
        {
            var doc = FindDocument(id);
            if (doc == null || doc.Supprime) continue;
            doc.Supprime = true;
            doc.DateSuppression = DateTimeOffset.Now.ToString("O");
            count++;
        }
        if (count == 0) return OperationResult.Fail(L.T("Aucun document à supprimer."));
        SaveDocuments();
        LogActivity(ActivityTypes.DocumentDelete, "Suppression de " + count + " document(s) (corbeille)");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult RestoreDocuments(IEnumerable<string> documentIds)
    {
        var count = 0;
        foreach (var id in documentIds.Distinct())
        {
            var doc = FindDocument(id);
            if (doc == null || !doc.Supprime) continue;
            // Si le dossier d'origine n'existe plus, le document revient à la racine.
            if (!string.IsNullOrWhiteSpace(doc.FolderId) && FindFolder(doc.FolderId) == null)
                doc.FolderId = null;
            doc.Supprime = false;
            doc.DateSuppression = null;
            doc.DateModification = DateTimeOffset.Now.ToString("O");
            count++;
        }
        if (count == 0) return OperationResult.Fail(L.T("Aucun document à restaurer."));
        SaveDocuments();
        LogActivity(ActivityTypes.DocumentRestore, "Restauration de " + count + " document(s) depuis la corbeille");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Supprime définitivement un document de la corbeille.</summary>
    public OperationResult PurgeDocument(string documentId)
    {
        var doc = FindDocument(documentId);
        if (doc == null) return OperationResult.Fail(L.T("Document introuvable."));
        _documents.Remove(doc);
        SaveDocuments();
        LogActivity(ActivityTypes.DocumentDelete, "Suppression définitive du document « " + doc.Titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Vide entièrement la corbeille (suppressions définitives).</summary>
    public OperationResult EmptyTrash()
    {
        var count = _documents.Count(d => d.Supprime);
        if (count == 0) return OperationResult.Fail(L.T("La corbeille est déjà vide."));
        _documents.RemoveAll(d => d.Supprime);
        SaveDocuments();
        LogActivity(ActivityTypes.DocumentDelete, "Vidage de la corbeille (" + count + " document(s) supprimé(s) définitivement)");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    // ---------- Champs flexibles ----------

    public (string id, string label)[] FieldTypeChoices()
    {
        var values = (FieldType[])Enum.GetValues(typeof(FieldType));
        return values.Select(v => (v.ToString(), FieldTypeInfo.Label(v))).ToArray();
    }

    public OperationResult AddField(FieldDefinition field)
    {
        if (string.IsNullOrWhiteSpace(field.Label))
            return OperationResult.Fail(L.T("Le libellé du champ est obligatoire."));
        if (FieldTypeInfo.SupportsOptions(field.Type) && field.Options.All(string.IsNullOrWhiteSpace))
            return OperationResult.Fail(field.Type == FieldType.ChoixMultiple
                ? L.T("Un choix doit contenir au moins une option.")
                : L.T("Une liste doit contenir au moins une option."));
        field.Id = Guid.NewGuid().ToString("N");
        field.Ordre = (_fields.Count == 0) ? 0 : _fields.Max(f => f.Ordre) + 1;
        _fields.Add(field);
        SaveFields();
        LogActivity(ActivityTypes.FieldsChange, "Ajout du champ « " + field.Label + " » (" + L.EnFrancais(() => FieldTypeInfo.Label(field.Type)) + ")");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult UpdateField(FieldDefinition field)
    {
        var idx = _fields.FindIndex(f => f.Id == field.Id);
        if (idx < 0) return OperationResult.Fail(L.T("Champ introuvable."));
        if (string.IsNullOrWhiteSpace(field.Label))
            return OperationResult.Fail(L.T("Le libellé du champ est obligatoire."));
        if (FieldTypeInfo.SupportsOptions(field.Type) && field.Options.All(string.IsNullOrWhiteSpace))
            return OperationResult.Fail(field.Type == FieldType.ChoixMultiple
                ? L.T("Un choix doit contenir au moins une option.")
                : L.T("Une liste doit contenir au moins une option."));
        var existing = _fields[idx];
        if (existing.SystemField)
        {
            existing.Label = field.Label.Trim();
            existing.Actif = field.Actif;
            if (existing.Id != FieldDefinition.FIELD_IDENTIFIANT)
                existing.Requis = field.Requis;
            _fields[idx] = existing;
        }
        else
        {
            _fields[idx] = field;
        }
        SaveFields();
        LogActivity(ActivityTypes.FieldsChange, "Modification du champ « " + _fields[idx].Label + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Supprime un champ du formulaire ; renvoie les clés de fichiers à purger dans IndexedDB.</summary>
    public (OperationResult result, List<string> fileKeys) DeleteField(string fieldId)
    {
        var field = _fields.FirstOrDefault(f => f.Id == fieldId);
        if (field == null) return (OperationResult.Fail(L.T("Champ introuvable.")), new List<string>());
        if (field.SystemField)
            return (OperationResult.Fail(L.T("Ce champ est lié au compte et ne peut pas être supprimé.")), new List<string>());

        var fileKeys = new List<string>();
        foreach (var user in _users)
        {
            if (user.Champs.TryGetValue(fieldId, out var val) && val.Fichier != null)
                fileKeys.Add(val.Fichier.Key);
            user.Champs.Remove(fieldId);
        }
        _fields.Remove(field);
        SaveFields();
        SaveUsers();
        LogActivity(ActivityTypes.FieldsChange, "Suppression du champ « " + field.Label + " »");
        DataChanged?.Invoke();
        return (OperationResult.Ok(), fileKeys);
    }

    public void MoveField(string fieldId, int direction)
    {
        var ordered = Fields.ToList();
        var idx = ordered.FindIndex(f => f.Id == fieldId);
        if (idx < 0) return;
        var targetIdx = idx + direction;
        if (targetIdx < 0 || targetIdx >= ordered.Count) return;

        (ordered[idx].Ordre, ordered[targetIdx].Ordre) = (ordered[targetIdx].Ordre, ordered[idx].Ordre);
        SaveFields();
        DataChanged?.Invoke();
    }

    // ---------- Validation ----------

    public bool IsEmailValid(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        return System.Net.Mail.MailAddress.TryCreate(email.Trim(), out _);
    }

    /// <summary>Supprime les espaces et conserve le préfixe « + » pour la validation du téléphone.</summary>
    public static string NormalizePhone(string? phone)
    {
        var p = (phone ?? "").Trim();
        var digits = new string(p.Where(char.IsDigit).ToArray());
        return p.StartsWith("+", StringComparison.Ordinal) ? "+" + digits : digits;
    }

    /// <summary>Téléphone malgache : +261 suivi de 7 à 10 chiffres.</summary>
    public static bool IsPhoneValid(string phone)
        => System.Text.RegularExpressions.Regex.IsMatch((phone ?? "").Trim(), @"^\+261\d{7,10}$");

    /// <summary>Valide le devenir d'un utilisateur. Retourne des erreurs (clé = champ virtuel, valeur = message).</summary>
    public Dictionary<string, string> ValidateUser(UserAccount draft, string? existingId, bool isNew, bool checkPassword = true, string confirmPassword = "", bool passwordProvided = false)
    {
        var errors = new Dictionary<string, string>();
        void Add(string key, string msg)
        {
            if (!errors.ContainsKey(key)) errors[key] = msg;
        }
if (string.IsNullOrWhiteSpace(draft.Prenom)) Add("prenom", L.T("Le prénom est obligatoire."));
        if (string.IsNullOrWhiteSpace(draft.Nom)) Add("nom", L.T("Le nom est obligatoire."));

        var isSimpleUser = draft.Role == "Utilisateur";

        // Identifiant : géré par l'administrateur pour les comptes admin/gestionnaire,
        // généré automatiquement pour les comptes « Utilisateur ».
        if (!isSimpleUser)
        {
            if (string.IsNullOrWhiteSpace(draft.Identifiant)) Add("identifiant", L.T("L'identifiant est obligatoire."));
            else if (_users.Any(u => u.Id != existingId && u.Identifiant.Equals(draft.Identifiant.Trim(), StringComparison.OrdinalIgnoreCase)))
                Add("identifiant", L.T("Cet identifiant existe déjà."));
        }

        // E-mail et téléphone facultatifs ; e-mail validé s'il est renseigné.
        if (!string.IsNullOrWhiteSpace(draft.Email))
        {
            if (!IsEmailValid(draft.Email)) Add("email", L.T("Le format de l'adresse e-mail est invalide."));
            else if (_users.Any(u => u.Id != existingId && u.Email.Equals(draft.Email.Trim(), StringComparison.OrdinalIgnoreCase)))
                Add("email", L.T("Cette adresse e-mail existe déjà."));
        }

        // Membre de moins de 18 ans : un responsable légal (nom) est demandé.
        if (NormalizeYmd(draft.DateNaissance) is { Length: > 0 } naissance
            && new UserAccount { DateNaissance = naissance }.EstMineur
            && string.IsNullOrWhiteSpace(draft.ResponsableLegal)
            && !(existingId != null && _users.FirstOrDefault(u => u.Id == existingId) is { } fiche
                 && AResponsableRelie(fiche)))
            Add("responsable", L.T("Moins de 18 ans : indiquez le responsable légal (parent ou tuteur), ou reliez un parent ou un tuteur dans la section Famille de sa fiche."));

        // Matricule facultatif mais unique s'il est renseigné.
        if (!string.IsNullOrWhiteSpace(draft.Matricule)
            && _users.Any(u => u.Id != existingId && (u.Matricule ?? "").Trim().Equals(draft.Matricule.Trim(), StringComparison.OrdinalIgnoreCase)))
            Add("matricule", L.T("Ce matricule existe déjà."));

        // Téléphone : format malgache « +261 » (7 à 10 chiffres, espaces éventuels).
        var phoneDigits = new string((draft.Telephone ?? "").Where(char.IsDigit).ToArray());
        if (phoneDigits.Length > 0)
        {
            var normalized = NormalizePhone(draft.Telephone);
            if (!IsPhoneValid(normalized))
                Add("telephone", L.T("Le numéro doit être au format « +261 xx xxx xx » (ex. +261 32 12 345 67)."));
        }

        // En ligne, le mot de passe est celui du compte Supabase : rien à saisir sur la fiche.
        if (isNew && !OnlineMode)
        {
            if (!isSimpleUser)
            {
                if (string.IsNullOrWhiteSpace(draft.MotDePasse)) Add("password", L.T("Le mot de passe est obligatoire."));
                else if (draft.MotDePasse.Length < 6) Add("password", L.T("Le mot de passe doit contenir au moins 6 caractères."));
                if (string.IsNullOrWhiteSpace(confirmPassword)) Add("confirmPassword", L.T("Veuillez confirmer le mot de passe."));
                else if (draft.MotDePasse != confirmPassword) Add("confirmPassword", L.T("Les mots de passe ne correspondent pas."));
            }
        }
        else if (passwordProvided && !isSimpleUser && !OnlineMode)
        {
            if (draft.MotDePasse.Length < 6) Add("password", L.T("Le mot de passe doit contenir au moins 6 caractères."));
            if (draft.MotDePasse != confirmPassword) Add("confirmPassword", L.T("Les mots de passe ne correspondent pas."));
        }

        // Champs personnalisés obligatoires + validation de type
        // (les champs système liés au compte sont gérés explicitement ci-dessus)
        foreach (var f in Fields.Where(f => f.Actif && !f.SystemField))
        {
            var val = draft.GetChamp(f.Id) ?? new FieldValue();
            var key = "field_" + f.Id;

            if (string.IsNullOrWhiteSpace(val.Texte) && !val.Booleen && val.Nombre == null && val.Fichier == null)
            {
                if (f.Requis) Add(key, L.F("Le champ « {0} » est obligatoire.", f.Label));
                continue;
            }

            switch (f.Type)
            {
                case FieldType.Nombre:
                    if (val.Nombre == null && string.IsNullOrWhiteSpace(val.Texte))
                    {
                        if (f.Requis) Add(key, L.T("Veuillez saisir un nombre."));
                    }
                    else if (val.Nombre != null && string.IsNullOrWhiteSpace(val.Texte) == false)
                    {
                        if (f.Requis) Add(key, L.T("Valeur de nombre invalide."));
                    }
                    break;
                case FieldType.Liste:
                    if (!string.IsNullOrWhiteSpace(val.Texte) && !f.Options.Contains(val.Texte))
                        Add(key, L.T("La valeur sélectionnée n'est pas valide."));
                    break;
                case FieldType.ChoixMultiple:
                    foreach (var opt in (val.Texte ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!f.Options.Contains(opt))
                        {
                            Add(key, L.T("Une valeur cochée n'est pas valide."));
                            break;
                        }
                    }
                    break;
                case FieldType.Email:
                    if (!string.IsNullOrWhiteSpace(val.Texte) && !IsEmailValid(val.Texte))
                        Add(key, L.T("Adresse e-mail invalide."));
                    break;
                case FieldType.PieceJointe:
                case FieldType.Image:
                    if (val.Fichier != null && val.Fichier.Taille > MAX_FILE_BYTES)
                        Add(key, L.F("Le fichier « {0} » dépasse la taille maximale de 5 Mo.", val.Fichier.Nom));
                    break;
            }
        }

        return errors;
    }

    // ---------- Import / Export ----------

    public string ExportJson() => Serialize(new
    {
        app = _settings.AppName,
        version = 2,
        exportedAt = DateTimeOffset.Now.ToString("O"),
        fields = _fields.Where(f => f.Actif).Select(f => f.Label).ToList(),
        users = _users.Select(u => new
        {
            u.Id,
            u.Prenom,
            u.Nom,
            u.Identifiant,
            u.Email,
            u.Telephone,
            u.Role,
            statut = u.Actif ? "Actif" : "Inactif",
            u.MotDePasse,
            u.Notes,
            u.DateCreation,
            u.DerniereConnexion,
            Champs = u.Champs
        }).ToList()
    });

    public string ExportCsv()
    {
        var activeFields = Fields.Where(f => f.Actif).ToList();
        static string Csv(string v)
        {
            var s = v ?? "";
            if (s.Contains(';') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                return '"' + s.Replace("\"", "\"\"") + '"';
            return s;
        }

        var headers = new List<string> { "nom", "prenom", "identifiant", "email", "telephone", "role", "statut", "dateCreation", "derniereConnexion", "notes" };
        foreach (var f in activeFields) headers.Add(f.Label);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(';', headers.Select(Csv)));

        foreach (var u in _users)
        {
            var row = new List<string>
            {
                u.Nom, u.Prenom, u.Identifiant, u.Email, u.Telephone,
                u.Role, u.Actif ? "Actif" : "Inactif",
                FormatExportDate(u.DateCreation), FormatExportDate(u.DerniereConnexion), u.Notes
            };
            foreach (var f in activeFields)
            {
                var val = u.GetChamp(f.Id);
                row.Add(FieldCsvValue(f, val));
            }
            sb.AppendLine(string.Join(';', row.Select(Csv)));
        }
        return sb.ToString();
    }

    public static string FormatExportDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "";
        if (DateTimeOffset.TryParse(iso, out var d)) return d.ToString("dd/MM/yyyy");
        if (DateTime.TryParse(iso, out var dt)) return dt.ToString("dd/MM/yyyy");
        return iso;
    }

    /// <summary>Formate une date ISO en JJ/MM/AAAA (ex. 05/09/2026). Retourne "—" si vide.</summary>
    public static string FormatDateCourte(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "—";
        if (DateTimeOffset.TryParse(iso, out var d)) return d.ToString("dd/MM/yyyy");
        if (DateTime.TryParse(iso, out var dt)) return dt.ToString("dd/MM/yyyy");
        // Date déjà au format yyyy-MM-dd (calendrier) : convertit en JJ/MM/AAAA.
        if (DateTime.TryParseExact(iso.Trim(), "yyyy-MM-dd",
                System.Globalization.CultureInfo.GetCultureInfo("fr-FR"),
                System.Globalization.DateTimeStyles.None, out var ymd))
            return ymd.ToString("dd/MM/yyyy");
        return iso;
    }

    /// <summary>Formate une date ISO en JJ/MM/AAAA à HH:mm (ex. 05/09/2026 à 14:30). Retourne "—" si vide.</summary>
    public static string FormatDateHeure(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "—";
        if (DateTimeOffset.TryParse(iso, out var d)) return L.F("{0} à {1}", d.ToString("dd/MM/yyyy"), d.ToString("HH:mm"));
        if (DateTime.TryParse(iso, out var dt)) return L.F("{0} à {1}", dt.ToString("dd/MM/yyyy"), dt.ToString("HH:mm"));
        return iso;
    }

    private static string FieldCsvValue(FieldDefinition f, FieldValue? val)
    {
        if (val == null) return "";
        return f.Type switch
        {
            FieldType.Nombre => val.Nombre?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            FieldType.Booleen => val.Booleen ? "Oui" : "Non",
            FieldType.Date => FormatExportDate(val.Texte),
            FieldType.PieceJointe or FieldType.Image => val.Fichier?.Nom ?? "",
            _ => val.Texte ?? ""
        };
    }

    /// <summary>Export Excel (formats de la liste des utilisateurs, colonnes personnalisées incluses).</summary>
    /// <param name="subset">Liste restreinte à exporter (filtres) ; null = tous les utilisateurs.</param>
    public byte[] ExportUsersExcel(IReadOnlyList<UserAccount>? subset = null)
    {
        var custom = Fields.Where(f => f.Actif && !f.SystemField).ToList();
        var headers = new List<string>
        {
            "Nom", "Prénom", "Identifiant", "Matricule", "Email", "Téléphone", "Rôle",
            "Statut", "Date de naissance", "Date de création", "Dernière connexion", "Notes"
        };
        headers.AddRange(custom.Select(f => f.Label));

        var rows = new List<string[]>();
        foreach (var u in (subset ?? _users).OrderBy(u => u.Nom).ThenBy(u => u.Prenom))
        {
            var row = new List<string>
            {
                u.Nom, u.Prenom, u.Identifiant, u.Matricule ?? "", u.Email, u.Telephone, u.Role,
                u.Actif ? "Actif" : "Inactif", FormatExportDate(u.DateNaissance), FormatExportDate(u.DateCreation), FormatExportDate(u.DerniereConnexion), u.Notes
            };
            foreach (var f in custom) row.Add(FieldExcelValue(f, u.GetChamp(f.Id)));
            rows.Add(row.ToArray());
        }
        return ExcelExporter.Build("Utilisateurs", headers, rows);
    }

    /// <summary>Export Excel du journal d'activité.</summary>
    public byte[] ExportJournalExcel()
    {
        var headers = new List<string> { "Date", "Utilisateur", "Type", "Description" };
        var rows = new List<string[]>();
        foreach (var e in _activity)
        {
            rows.Add(new[] { FormatExportDate(e.Date), e.User, ActivityTypes.Label(e.Type), e.Description });
        }
        return ExcelExporter.Build("Activite", headers, rows);
    }

    /// <summary>
    /// Export Excel complet : toutes les données locales dans un même classeur,
    /// une feuille par type de données (utilisé par l'export automatique mensuel).
    /// </summary>
    public byte[] ExportAllDataExcel()
    {
        string Name(string? identifiant)
        {
            if (string.IsNullOrWhiteSpace(identifiant)) return "";
            var u = FindUserByIdentifier(identifiant);
            return u?.NomComplet ?? identifiant;
        }

        string MatName(string id)
        {
            var m = _materiels.FirstOrDefault(x => x.Id == id);
            return m?.Nom ?? id;
        }

        var sheets = new List<ExcelExporter.Sheet>();

        // 1) Utilisateurs
        var usersHeaders = new List<string>
        {
            "Nom", "Prénom", "Identifiant", "Matricule", "Email", "Téléphone", "Rôle",
            "Statut", "Date de naissance", "Date de création", "Dernière connexion", "Notes"
        };
        var custom = Fields.Where(f => f.Actif && !f.SystemField).ToList();
        var usersHeadersFull = new List<string>(usersHeaders);
        usersHeadersFull.AddRange(custom.Select(f => f.Label));
        var usersRows = new List<string[]>();
        foreach (var u in _users.OrderBy(u => u.Nom).ThenBy(u => u.Prenom))
        {
            var row = new List<string>
            {
                u.Nom, u.Prenom, u.Identifiant, u.Matricule ?? "", u.Email, u.Telephone, u.Role,
                u.Actif ? "Actif" : "Inactif", FormatExportDate(u.DateNaissance), FormatExportDate(u.DateCreation), FormatExportDate(u.DerniereConnexion), u.Notes
            };
            foreach (var f in custom) row.Add(FieldExcelValue(f, u.GetChamp(f.Id)));
            usersRows.Add(row.ToArray());
        }
        sheets.Add(new ExcelExporter.Sheet("Utilisateurs", usersHeadersFull, usersRows));

        // 2) Champs personnalisés
        var fieldsSheets = new List<string[]>();
        foreach (var f in _fields.Where(f => !f.SystemField))
        {
            fieldsSheets.Add(new[] { f.Label, FieldTypeLabel(f.Type), f.Actif ? "Actif" : "Inactif", f.Requis ? "Oui" : "Non", string.Join(", ", f.Options), f.Ordre.ToString() });
        }
        sheets.Add(new ExcelExporter.Sheet("Champs", new[] { "Libellé", "Type", "Statut", "Obligatoire", "Options", "Ordre" }, fieldsSheets));

        // 3) Journal d'activité
        var activityRows = new List<string[]>();
        foreach (var e in _activity)
            activityRows.Add(new[] { FormatExportDate(e.Date), e.User, ActivityTypes.Label(e.Type), e.Description });
        sheets.Add(new ExcelExporter.Sheet("Activite", new[] { "Date", "Utilisateur", "Type", "Description" }, activityRows));

        // 4) Archives
        var archiveRows = new List<string[]>();
        foreach (var a in _archive)
            archiveRows.Add(new[] { a.User.NomComplet, a.User.Identifiant, a.User.Role, FormatExportDate(a.DeletedAt), a.DeletedBy });
        sheets.Add(new ExcelExporter.Sheet("Archives", new[] { "Nom", "Identifiant", "Rôle", "Supprimé le", "Supprimé par" }, archiveRows));

        // 5) Événements du calendrier
        var eventRows = new List<string[]>();
        foreach (var e in _events)
            eventRows.Add(new[] { e.Titre, CalendarEventTypes.Label(e.Type), FormatExportDate(e.Date), e.HeureDebut ?? "", e.HeureFin ?? "", e.Statut, string.Join(", ", e.PersonnesConcernees), e.Notes });
        sheets.Add(new ExcelExporter.Sheet("Calendrier", new[] { "Titre", "Type", "Date", "Début", "Fin", "Statut", "Personnes concernées", "Notes" }, eventRows));

        // 6) Dossiers documentaires
        var folderRows = new List<string[]>();
        foreach (var f in _folders)
        {
            var parent = string.IsNullOrWhiteSpace(f.ParentId) ? "" : _folders.FirstOrDefault(x => x.Id == f.ParentId)?.Nom ?? "";
            folderRows.Add(new[] { f.Nom, parent, FormatExportDate(f.DateCreation), f.CreatedBy });
        }
        sheets.Add(new ExcelExporter.Sheet("Dossiers", new[] { "Nom", "Dossier parent", "Créé le", "Créé par" }, folderRows));

        // 7) Documents (métadonnées, sans le contenu)
        var docRows = new List<string[]>();
        foreach (var d in _documents)
        {
            var folder = string.IsNullOrWhiteSpace(d.FolderId) ? "" : _folders.FirstOrDefault(x => x.Id == d.FolderId)?.Nom ?? "";
            docRows.Add(new[] { d.Titre, folder, DocumentCategories.Label(d.Categorie), d.FileName, d.Mime, FormatBytes(d.Taille), d.Supprime ? "Corbeille" : "En ligne", FormatExportDate(d.DateCreation), d.CreatedBy });
        }
        sheets.Add(new ExcelExporter.Sheet("Documents", new[] { "Titre", "Dossier", "Catégorie", "Fichier", "Type", "Taille", "État", "Créé le", "Créé par" }, docRows));

        // 8) Communauté
        var chapterRows = new List<string[]>();
        foreach (var ch in _community.Chapitres)
            chapterRows.Add(new[] { ch.Titre, ch.Date, ch.Texte });
        sheets.Add(new ExcelExporter.Sheet("Communaute", new[] { "Chapitre", "Date", "Texte" }, chapterRows));

        // 9) Messages
        var msgRows = new List<string[]>();
        foreach (var m in _messages)
            msgRows.Add(new[] { FormatExportDate(m.Date), m.Expediteur, string.Join(", ", m.Destinataires), m.Texte });
        sheets.Add(new ExcelExporter.Sheet("Messages", new[] { "Date", "Expéditeur", "Destinataires", "Texte" }, msgRows));

        // 10) Tâches
        var tacheRows = new List<string[]>();
        foreach (var t in _taches)
            tacheRows.Add(new[] { t.Titre, TacheStatuts.Label(t.Statut), t.Priorite, string.Join(", ", t.Assignes), FormatExportDate(t.Echeance), FormatExportDate(t.DateCreation), t.Description });
        sheets.Add(new ExcelExporter.Sheet("Taches", new[] { "Titre", "Statut", "Priorité", "Assignés", "Échéance", "Créée le", "Description" }, tacheRows));

        // 11) Cotisations
        var cotRows = new List<string[]>();
        foreach (var c in _cotisations)
            cotRows.Add(new[] { Name(c.MembreIdentifiant), c.Periode, c.Montant.ToString(System.Globalization.CultureInfo.InvariantCulture), c.Motif, FormatExportDate(c.Date), CotisationStatuts.Label(c.Statut), c.CreatedBy });
        sheets.Add(new ExcelExporter.Sheet("Cotisations", new[] { "Membre", "Période", "Montant (Ar)", "Motif", "Date", "Statut", "Saisi par" }, cotRows));

        // 12) Trésorerie
        var tresRows = new List<string[]>();
        foreach (var t in _tresorerie)
            tresRows.Add(new[] { TresorerieTypes.Label(t.Type), t.Montant.ToString(System.Globalization.CultureInfo.InvariantCulture), t.Motif, FormatExportDate(t.Date), t.CreatedBy });
        sheets.Add(new ExcelExporter.Sheet("Tresorerie", new[] { "Type", "Montant (Ar)", "Motif", "Date", "Saisi par" }, tresRows));

        // 13) Matériels
        var matRows = new List<string[]>();
        foreach (var m in _materiels)
            matRows.Add(new[] { m.Nom, m.Categorie, m.QuantiteTotale.ToString(), MaterielEtats.Label(m.Etat), m.Responsable, m.Description });
        sheets.Add(new ExcelExporter.Sheet("Materiels", new[] { "Nom", "Catégorie", "Quantité", "État", "Responsable", "Description" }, matRows));

        // 14) Prêts de matériel
        var pretRows = new List<string[]>();
        foreach (var p in _prets)
            pretRows.Add(new[] { MatName(p.MaterielId), Name(p.MembreIdentifiant), p.Quantite.ToString(), FormatExportDate(p.DatePret), FormatExportDate(p.DateRetour) });
        sheets.Add(new ExcelExporter.Sheet("Prets", new[] { "Matériel", "Membre", "Quantité", "Prêt le", "Retour le" }, pretRows));

        // 15) Réunions
        var reunionRows = new List<string[]>();
        foreach (var r in _reunions)
            reunionRows.Add(new[] { r.Titre, FormatExportDate(r.Date), r.HeureDebut ?? "", r.HeureFin ?? "", r.Lieu, r.Participants.Count.ToString(), string.Join(", ", r.Presences.Select(p => Name(p.Key) + "=" + PresenceStatuts.Label(p.Value))), r.CompteRendu });
        sheets.Add(new ExcelExporter.Sheet("Reunions", new[] { "Titre", "Date", "Début", "Fin", "Lieu", "Participants", "Présences", "Compte-rendu" }, reunionRows));

        // 16) Situations
        var sitRows = new List<string[]>();
        foreach (var s in _situations)
            sitRows.Add(new[] { Name(s.MembreIdentifiant), SituationTypes.Label(s.Type), s.Titre, SituationStatuts.Label(s.Statut), s.Priorite, FormatExportDate(s.DateCreation), FormatExportDate(s.DateResolution), s.Description });
        sheets.Add(new ExcelExporter.Sheet("Situations", new[] { "Membre", "Type", "Titre", "Statut", "Priorité", "Créée le", "Résolue le", "Description" }, sitRows));

        // 17) Votes
        var voteRows = new List<string[]>();
        foreach (var v in _votes)
            voteRows.Add(new[] { v.Titre, VoteStatuts.Label(v.Statut), string.Join(", ", v.Options), v.Bulletins.Count.ToString(), FormatExportDate(v.DateCloture), v.CreatedBy });
        sheets.Add(new ExcelExporter.Sheet("Votes", new[] { "Titre", "Statut", "Options", "Bulletins", "Clôture", "Créé par" }, voteRows));

        // 18) Journal multimédia
        var mediaRows = new List<string[]>();
        foreach (var md in _journalMedia)
            mediaRows.Add(new[] { md.Titre, MediaTypes.Label(md.Type), md.Mime, FormatBytes(md.Taille), FormatExportDate(md.Date), md.CreatedBy, md.Description });
        sheets.Add(new ExcelExporter.Sheet("Journal media", new[] { "Titre", "Type", "Format", "Taille", "Date", "Ajouté par", "Description" }, mediaRows));

        return ExcelExporter.BuildMulti(sheets);

        static string FieldTypeLabel(FieldType t) => t switch
        {
            FieldType.Texte => "Texte",
            FieldType.Nombre => "Nombre",
            FieldType.Booleen => "Case à cocher",
            FieldType.Date => "Date",
            FieldType.Liste => "Liste",
            FieldType.PieceJointe => "Pièce jointe",
            FieldType.Image => "Image",
            _ => "Texte"
        };

        static string FormatBytes(long obyte)
        {
            if (obyte < 1024) return obyte + " o";
            if (obyte < 1024 * 1024) return (obyte / 1024.0).ToString("0.0") + " Ko";
            return (obyte / (1024.0 * 1024)).ToString("0.0") + " Mo";
        }
    }

    /// <summary>Import depuis un classeur Excel exporté par l'application.</summary>
    public ImportResult ParseExcelImport(byte[] xlsx)
        => ExcelImporter.Parse(xlsx, _fields);

    private static string FieldExcelValue(FieldDefinition f, FieldValue? val)
    {
        if (val == null) return "";
        return f.Type switch
        {
            FieldType.Nombre => val.Nombre?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            FieldType.Booleen => val.Booleen ? "Oui" : "Non",
            FieldType.Date => FormatExportDate(val.Texte),
            FieldType.PieceJointe or FieldType.Image => val.Fichier?.Nom ?? "",
            _ => val.Texte ?? ""
        };
    }

    public ImportResult ParseImport(string jsonText)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object && root.ValueKind != JsonValueKind.Array)
                return new ImportResult { Success = false, Error = L.T("Le fichier ne contient pas un objet/tableau JSON valide.") };

            var elements = new List<JsonElement>();
            if (root.ValueKind == JsonValueKind.Array)
                elements.AddRange(root.EnumerateArray());
            else if (root.TryGetProperty("users", out var usersProp) && usersProp.ValueKind == JsonValueKind.Array)
                elements.AddRange(usersProp.EnumerateArray());
            else
                return new ImportResult { Success = false, Error = L.T("Impossible de trouver la liste « users » dans le fichier.") };

            if (elements.Count == 0)
                return new ImportResult { Success = false, Error = L.T("Le fichier ne contient aucun utilisateur.") };

            var users = new List<UserAccount>();
            var idx = 0;
            foreach (var el in elements)
            {
                idx++;
                if (el.ValueKind != JsonValueKind.Object)
                    return new ImportResult { Success = false, Error = L.F("Utilisateur n°{0}: entrée JSON invalide.", idx) };

                var u = new UserAccount
                {
                    Id = string.IsNullOrEmpty(Get(el, "id")) ? Guid.NewGuid().ToString("N") : Get(el, "id")!,
                    Prenom = (Get(el, "prenom") ?? "").Trim(),
                    Nom = (Get(el, "nom") ?? "").Trim().ToUpperInvariant(),
                    Identifiant = (Get(el, "identifiant") ?? "").Trim(),
                    Email = (Get(el, "email") ?? "").Trim(),
                    Telephone = (Get(el, "telephone") ?? "").Trim(),
                    Role = NormalizeRole(Get(el, "role")),
                    Notes = (Get(el, "notes") ?? "").Trim(),
                    MotDePasse = Get(el, "motDePasse") ?? Get(el, "password") ?? DEFAULT_PASSWORD,
                    DateNaissance = NormalizeYmd(Get(el, "dateNaissance")),
                    Matricule = (Get(el, "matricule") ?? "").Trim(),
                    Signature = Get(el, "signature"),
                    DateCreation = Get(el, "dateCreation") ?? DateTimeOffset.Now.ToString("O"),
                    DerniereConnexion = Get(el, "derniereConnexion"),
                    Photo = Get(el, "photo")
                };

                if (string.IsNullOrWhiteSpace(u.Prenom) || string.IsNullOrWhiteSpace(u.Nom))
                    return new ImportResult { Success = false, Error = L.F("Utilisateur n°{0}: prénom et nom sont obligatoires.", idx) };
                if (string.IsNullOrWhiteSpace(u.Identifiant))
                {
                    if (u.Role != Roles.Utilisateur)
                        return new ImportResult { Success = false, Error = L.F("Utilisateur n°{0}: l'identifiant est obligatoire pour ce rôle.", idx) };
                    u.Identifiant = GenerateIdentifiant(u.Prenom, u.Nom);
                }
                if (!string.IsNullOrWhiteSpace(u.Email) && !IsEmailValid(u.Email))
                    return new ImportResult { Success = false, Error = L.F("Utilisateur n°{0}: adresse e-mail « {1} » invalide.", idx, u.Email) };

                // Statut compatible ancien (string) + nouveau (bool)
                var statut = Get(el, "statut");
                if (!string.IsNullOrEmpty(statut)) u.Actif = statut.Equals("Actif", StringComparison.OrdinalIgnoreCase) || statut.Equals("true", StringComparison.OrdinalIgnoreCase);
                else u.Actif = el.TryGetProperty("actif", out var ab) && ab.ValueKind == JsonValueKind.True;

                // Champs personnalisés éventuels
                if (el.TryGetProperty("champs", out var champs) && champs.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in champs.EnumerateObject())
                    {
                        var fv = new FieldValue();
                        if (p.Value.ValueKind == JsonValueKind.String) fv.Texte = p.Value.GetString() ?? "";
                        else if (p.Value.ValueKind == JsonValueKind.Number) fv.Nombre = p.Value.GetDouble();
                        else if (p.Value.ValueKind == JsonValueKind.True) fv.Booleen = true;
                        else if (p.Value.ValueKind == JsonValueKind.Object)
                        {
                            fv.Texte = Get(p.Value, "texte") ?? "";
                            if (p.Value.TryGetProperty("nombre", out var nb) && nb.ValueKind == JsonValueKind.Number) fv.Nombre = nb.GetDouble();
                            fv.Booleen = p.Value.TryGetProperty("booleen", out var bb) && bb.ValueKind == JsonValueKind.True;
                        }
                        u.Champs[p.Name] = fv;
                    }
                }
                users.Add(u);
            }

            return new ImportResult { Success = true, Users = users };
        }
        catch (JsonException ex)
        {
            return new ImportResult { Success = false, Error = L.F("Le fichier JSON est invalide : {0}", ex.Message) };
        }
        catch (Exception ex)
        {
            return new ImportResult { Success = false, Error = L.F("Erreur de lecture du fichier : {0}", ex.Message) };
        }
    }

    private static string NormalizeRole(string? role) => role switch
    {
        Roles.BaseAdmin or "Administrateur de la base" or "base" => Roles.BaseAdmin,
        "Administrateur" or "administrateur" or "admin" => Roles.Administrateur,
        "Gestionnaire" or "gestionnaire" or "manager" => Roles.Gestionnaire,
        _ => Roles.Utilisateur
    };

    private static string? Get(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        return el.TryGetProperty(name, out var p) ? p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number => p.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => p.GetRawText()
        } : null;
    }

    /// <summary>Applique un import en fusion : ajoute les utilisateurs encore absents (contrôle des doublons par identifiant), sans jamais supprimer les utilisateurs existants.</summary>
    public (OperationResult result, int count) ApplyImport(List<UserAccount> imported)
    {
        if (imported == null || imported.Count == 0)
            return (OperationResult.Fail(L.T("Aucun utilisateur à importer.")), 0);

        var seen = new HashSet<string>(_users.Select(u => u.Identifiant.ToLowerInvariant()));
        var nomParIdentifiant = _users.GroupBy(u => u.Identifiant.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => CleNom(g.First().Prenom, g.First().Nom));
        // Même personne déjà présente (même e-mail, téléphone ou matricule) : pas de doublon.
        // Les homonymes sont importés ; ils apparaissent ensuite dans « Doublons possibles ».
        var contacts = new HashSet<string>(_users.SelectMany(ClesContact));
        var added = 0;
        foreach (var imp in imported)
        {
            var key = (imp.Identifiant ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(key)) continue;
            var siennes = ClesContact(imp).ToList();
            if (siennes.Any(contacts.Contains)) continue;
            var cleNom = CleNom(imp.Prenom, imp.Nom);
            if (seen.Contains(key))
            {
                // Même identifiant et même nom : c'est la même personne. Sinon c'est un
                // homonyme de prénom, qui reçoit le premier numéro libre (jean1, jean2…).
                if (nomParIdentifiant.TryGetValue(key, out var autreNom) && autreNom == cleNom) continue;
                key = IdentifiantLibre(key, seen.Contains);
                imp.Identifiant = key;
            }
            contacts.UnionWith(siennes);
            seen.Add(key);
            nomParIdentifiant[key] = cleNom;
            _users.Add(CloneImportUser(imp));
            added++;
        }

        if (added == 0)
            return (OperationResult.Fail(L.T("Aucun nouvel utilisateur à importer : ils existent déjà (même identifiant, e-mail, téléphone ou matricule).")), 0);

        // S'assurer qu'un administrateur actif existe après l'import
        if (!_users.Any(u => Roles.IsAdmin(u.Role) && u.Actif))
            return (OperationResult.Fail(L.T("L'import supprimerait le dernier administrateur actif : opération annulée.")), 0);

        SaveUsers();
        try { SyncAllUserDateEvents(); } catch { }
        LogActivity(ActivityTypes.Import, "Importation de " + added + " nouvel(aux) utilisateur(s) depuis un fichier Excel");
        DataChanged?.Invoke();
        return (OperationResult.Ok(), added);
    }

    private static UserAccount CloneImportUser(UserAccount src) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Prenom = src.Prenom,
        Nom = src.Nom,
        Identifiant = src.Identifiant,
        Email = src.Email,
        Telephone = src.Telephone,
        Role = src.Role,
        Actif = src.Actif,
        MotDePasse = src.MotDePasse,
        Notes = src.Notes,
        Photo = src.Photo,
        DateNaissance = src.DateNaissance,
        Matricule = src.Matricule,
        Signature = src.Signature,
        ResponsableLegal = src.ResponsableLegal,
        ResponsableTelephone = src.ResponsableTelephone,
        DateCreation = src.DateCreation,
        DerniereConnexion = src.DerniereConnexion,
        Champs = src.Champs.ToDictionary(kv => kv.Key, kv => kv.Value)
    };

    public string BackupJson()
    {
        return Serialize(new { app = _settings.AppName, version = 2, users = _users, activity = _activity });
    }

    /// <summary>Sauvegarde complète de toutes les données (export mensuel automatique).</summary>
    public string ExportFullBackupJson()
    {
        return Serialize(new
        {
            app = _settings.AppName,
            version = 3,
            exportedAt = DateTimeOffset.Now.ToString("O"),
            exportedMonth = DateTime.Now.ToString("yyyy-MM"),
            users = _users,
            fields = _fields,
            activity = _activity,
            archives = _archive,
            events = _events,
            documents = _documents,
            folders = _folders,
            community = _community,
            settings = _settings,
            messages = _messages,
            taches = _taches,
            cotisations = _cotisations,
            tresorerie = _tresorerie,
            materiels = _materiels,
            prets = _prets,
            reunions = _reunions,
            situations = _situations,
            votes = _votes,
            journalMedia = _journalMedia
        });
    }

    /// <summary>Un export automatique est-il dû pour le mois courant ?</summary>
    public bool IsAutoExportDue()
    {
        if (!_settings.ExportAutoMensuel) return false;
        var current = DateTime.Now.ToString("yyyy-MM");
        return !string.Equals(_settings.DernierExportAuto, current, StringComparison.Ordinal);
    }

    /// <summary>Marque l'export automatique du mois courant comme effectué.</summary>
    public void MarkAutoExportDone()
    {
        _settings.DernierExportAuto = DateTime.Now.ToString("yyyy-MM");
        SaveSettings();
        LogActivity(ActivityTypes.Export, "Export automatique mensuel de toutes les données (" + _settings.DernierExportAuto + ")");
    }

    // ---------- Paramètres ----------

    public AppSettings Settings => _settings;

    public void SaveSettings(AppSettings value)
    {
        // Les pages de réglages reconstruisent les paramètres champ par champ :
        // la liste des homonymes confirmés ne doit pas disparaître au passage.
        if ((value.PasDoublons == null || value.PasDoublons.Count == 0) && _settings.PasDoublons?.Count > 0)
            value.PasDoublons = _settings.PasDoublons;
        // Idem pour les listes de choix réglées (Paramètres › Listes de choix).
        if ((value.Listes == null || value.Listes.Count == 0) && _settings.Listes?.Count > 0)
            value.Listes = _settings.Listes;
        value.Listes ??= new Dictionary<string, ListeConfig>();
        // En ligne, aucun thème commun : chacun choisit le sien (clair par défaut).
        if (OnlineMode) value.Theme = "light";
        _settings = value;
        SaveSettings();
        LogActivity(ActivityTypes.Update, "Mise à jour des paramètres de l'application");
        DataChanged?.Invoke();
    }

    // ---------- Guide et aides contextuelles ----------

    /// <summary>Le mini-guide de démarrage a-t-il déjà été montré à ce compte ?</summary>
    public bool HasSeenOnboarding(string identifiant)
    {
        if (string.IsNullOrWhiteSpace(identifiant)) return true;
        if (OnlineMode) return LoadPrefs(identifiant).GuideVu;
        return _settings.GuideVu.Contains(identifiant, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Marque le mini-guide de démarrage comme vu pour ce compte.</summary>
    public void MarkOnboardingSeen(string identifiant)
    {
        if (string.IsNullOrWhiteSpace(identifiant)) return;
        if (OnlineMode)
        {
            UpdatePrefs(identifiant, p => p.GuideVu = true);
            return;
        }
        if (_settings.GuideVu.Contains(identifiant, StringComparer.OrdinalIgnoreCase)) return;
        _settings.GuideVu.Add(identifiant);
        SaveSettings();
        DataChanged?.Invoke();
    }

    /// <summary>L'aide contextuelle de ce module a-t-elle déjà été affichée à ce compte ?</summary>
    public bool HasSeenModuleHelp(string identifiant, string module)
    {
        if (string.IsNullOrWhiteSpace(identifiant) || string.IsNullOrWhiteSpace(module)) return true;
        var key = identifiant + "|" + module;
        if (OnlineMode) return LoadPrefs(identifiant).AideVu.Contains(module, StringComparer.OrdinalIgnoreCase);
        return _settings.AideVu.Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Marque l'aide contextuelle de ce module comme vue pour ce compte.</summary>
    public void MarkModuleHelpSeen(string identifiant, string module)
    {
        if (string.IsNullOrWhiteSpace(identifiant) || string.IsNullOrWhiteSpace(module)) return;
        var key = identifiant + "|" + module;
        if (OnlineMode)
        {
            UpdatePrefs(identifiant, p =>
            {
                if (!p.AideVu.Contains(module, StringComparer.OrdinalIgnoreCase)) p.AideVu.Add(module);
            });
            return;
        }
        if (_settings.AideVu.Contains(key, StringComparer.OrdinalIgnoreCase)) return;
        _settings.AideVu.Add(key);
        SaveSettings();
        DataChanged?.Invoke();
    }

    /// <summary>Thème effectif d'un compte : choix individuel, sinon thème global.</summary>
    public string GetEffectiveTheme(string? identifiant)
    {
        if (OnlineMode && !string.IsNullOrWhiteSpace(identifiant))
        {
            var theme = LoadPrefs(identifiant).Theme;
            if (theme == "dark" || theme == "light") return theme;
        }
        else if (!string.IsNullOrWhiteSpace(identifiant)
            && _settings.ThemesUtilisateurs.TryGetValue(identifiant, out var perso)
            && (perso == "dark" || perso == "light"))
            return perso;
        // En ligne, le thème est un choix personnel : clair pour tous par défaut.
        if (OnlineMode) return "light";
        return _settings.Theme == "dark" ? "dark" : "light";
    }

    /// <summary>Paramètres à appliquer à l'écran du compte connecté (avec son thème personnel).</summary>
    public AppSettings SettingsAffichees() => new()
    {
        AppName = _settings.AppName,
        Theme = GetEffectiveTheme(GetCurrentUser()?.Identifiant),
        Color = _settings.Color,
        Logo = _settings.Logo
    };

    /// <summary>Enregistre le thème individuel d'un compte (sans toucher au thème global).</summary>
    public void SetUserTheme(string identifiant, string theme)
    {
        if (string.IsNullOrWhiteSpace(identifiant)) return;
        if (OnlineMode)
        {
            UpdatePrefs(identifiant, p => p.Theme = theme == "dark" ? "dark" : "light");
            return;
        }
        _settings.ThemesUtilisateurs[identifiant] = theme == "dark" ? "dark" : "light";
        SaveSettings();
        DataChanged?.Invoke();
    }

    /// <summary>Conseil contextuel effectif d'un compte : choix individuel, sinon réglage global.</summary>
    public bool GetEffectiveConseils(string? identifiant)
    {
        if (OnlineMode && !string.IsNullOrWhiteSpace(identifiant))
        {
            var choix = LoadPrefs(identifiant).Conseils;
            if (choix.HasValue) return choix.Value;
        }
        else if (!string.IsNullOrWhiteSpace(identifiant)
            && _settings.ConseilsUtilisateurs.TryGetValue(identifiant, out var perso))
            return perso;
        return _settings.AidesContextuelles;
    }

    /// <summary>Enregistre le choix individuel du conseil contextuel (sans toucher aux autres comptes).</summary>
    public void SetUserConseils(string identifiant, bool afficher)
    {
        if (string.IsNullOrWhiteSpace(identifiant)) return;
        if (OnlineMode)
        {
            UpdatePrefs(identifiant, p => p.Conseils = afficher);
            return;
        }
        _settings.ConseilsUtilisateurs[identifiant] = afficher;
        SaveSettings();
        DataChanged?.Invoke();
    }

    public long StorageUsedBytes()
    {
        long total = 0;
        foreach (var key in ClientStorageKeys)
        {
            var v = _storage.Get(key);
            if (v != null) total += key.Length + v.Length;
        }
        return total;
    }

    /// <summary>Détail du stockage utilisé par source de données (collections locales).</summary>
    public IReadOnlyList<StorageUsageItem> StorageBreakdown()
    {
        string StorageKey(string dataSetKey) => dataSetKey switch
        {
            "users" => K_USERS,
            "fields" => K_FIELDS,
            "activity" => K_ACTIVITY,
            "archives" => K_ARCHIVE,
            "events" => K_EVENTS,
            "documents" => K_DOCUMENTS,
            "folders" => K_FOLDERS,
            "community" => K_COMMUNITY,
            "permissions" => K_PERMISSIONS,
            "messages" => K_MESSAGES,
            "taches" => K_TACHES,
            "cotisations" => K_COTISATIONS,
            "tresorerie" => K_TRESORERIE,
            "materiels" => K_MATERIELS,
            "prets" => K_PRETS,
            "reunions" => K_REUNIONS,
            "situations" => K_SITUATIONS,
            "votes" => K_VOTES,
            "journal_media" => K_JOURNAL_MEDIA,
            "settings" => K_SETTINGS,
            _ => ""
        };

        var list = new List<StorageUsageItem>();
        foreach (var ds in ManagedDataSets)
        {
            var key = StorageKey(ds.Key);
            if (key.Length == 0) continue;
            var v = _storage.Get(key);
            list.Add(new StorageUsageItem(ds.Key, L.T(ds.Label), L.T(ds.Description), v == null ? 0 : key.Length + v.Length));
        }
        // Session de connexion (clé interne) : comptabilisée dans le total.
        var session = _storage.Get(K_SESSION);
        list.Add(new StorageUsageItem("session", L.T("Session de connexion"),
            L.T("État de la session courante (expiration)."), session == null ? 0 : K_SESSION.Length + session.Length));
        return list;
    }

    /// <summary>Pourcentage du quota de stockage utilisé (0-100).</summary>
    public double StorageQuotaPercent()
    {
        return Math.Min((double)StorageUsedBytes() / CurrentStorageQuotaBytes * 100.0, 100.0);
    }

    /// <summary>Niveau d'alerte de saturation : ok, warning (≥80 %), critical (≥95 %).</summary>
    public string StorageAlertLevel()
    {
        var p = StorageQuotaPercent();
        if (p >= StorageCriticalPercent) return "critical";
        if (p >= StorageWarningPercent) return "warning";
        return "ok";
    }

    // ---------- Histoire de la communauté ----------

    public CommunityStory Community => _community;

    /// <summary>
    /// Nom du menu « Histoire de … » avec le vrai nom de l'application
    /// (« Histoire de la communauté » tant que le nom par défaut est gardé).
    /// </summary>
    public string LibelleHistoire()
    {
        var nom = (_settings.AppName ?? "").Trim();
        if (nom.Length == 0 || nom == new AppSettings().AppName) return L.T("Histoire de la communauté");
        return L.Elision(nom)
            ? L.F("Histoire d'{0}", nom)
            : L.F("Histoire de {0}", nom);
    }

    /// <summary>Titre de l'histoire à afficher : le titre par défaut est remplacé par « Histoire de [nom] ».</summary>
    public string TitreHistoireAffiche()
    {
        var titre = (_community.Titre ?? "").Trim();
        return titre.Length == 0 || titre == new CommunityStory().Titre ? LibelleHistoire() : titre;
    }

    public OperationResult SaveCommunityStory(string titre, string sousTitre)
    {
        _community.Titre = string.IsNullOrWhiteSpace(titre) ? "Histoire de notre communauté" : titre.Trim();
        _community.SousTitre = (sousTitre ?? "").Trim();
        SaveCommunity();
        LogActivity(ActivityTypes.Update, "Mise à jour de l'histoire de la communauté");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult AddCommunityChapter(string titre, string texte, string dateIso, string? imageDataUrl)
    {
        if (string.IsNullOrWhiteSpace(titre)) return OperationResult.Fail(L.T("Le titre du chapitre est obligatoire."));
        _community.Chapitres.Add(new CommunityChapter
        {
            Id = Guid.NewGuid().ToString("N"),
            Titre = titre.Trim(),
            Texte = (texte ?? "").Trim(),
            Date = (dateIso ?? "").Trim(),
            Image = string.IsNullOrWhiteSpace(imageDataUrl) ? null : imageDataUrl
        });
        SaveCommunity();
        LogActivity(ActivityTypes.Create, "Ajout d'un chapitre à l'histoire de la communauté : « " + titre.Trim() + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult UpdateCommunityChapter(string id, string titre, string texte, string dateIso, string? imageDataUrl, bool removeImage)
    {
        var c = _community.Chapitres.FirstOrDefault(x => x.Id == id);
        if (c == null) return OperationResult.Fail(L.T("Chapitre introuvable."));
        if (string.IsNullOrWhiteSpace(titre)) return OperationResult.Fail(L.T("Le titre du chapitre est obligatoire."));
        c.Titre = titre.Trim();
        c.Texte = (texte ?? "").Trim();
        c.Date = (dateIso ?? "").Trim();
        if (removeImage) c.Image = null;
        else if (!string.IsNullOrWhiteSpace(imageDataUrl)) c.Image = imageDataUrl;
        SaveCommunity();
        LogActivity(ActivityTypes.Update, "Modification du chapitre « " + c.Titre + " » de l'histoire de la communauté");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult DeleteCommunityChapter(string id)
    {
        var removed = _community.Chapitres.RemoveAll(x => x.Id == id);
        if (removed == 0) return OperationResult.Fail(L.T("Chapitre introuvable."));
        SaveCommunity();
        LogActivity(ActivityTypes.Delete, "Suppression d'un chapitre de l'histoire de la communauté");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public void MoveCommunityChapter(string id, int direction)
    {
        var idx = _community.Chapitres.FindIndex(x => x.Id == id);
        if (idx < 0) return;
        var target = idx + direction;
        if (target < 0 || target >= _community.Chapitres.Count) return;
        (_community.Chapitres[idx], _community.Chapitres[target]) = (_community.Chapitres[target], _community.Chapitres[idx]);
        SaveCommunity();
        DataChanged?.Invoke();
    }

    // ---------- Rôles et permissions (matrice modifiable, menu « … ») ----------

    /// <summary>Copies des jeux de permissions par rôle (le compte technique a tout, verrouillé).</summary>
    public IReadOnlyList<RolePermissionSet> RolePermissions => _permissions
        .Select(p => new RolePermissionSet { Role = p.Role, Allowed = new Dictionary<string, bool>(p.Allowed) })
        .ToList();

    /// <summary>Valeurs par défaut : strictement identiques aux anciens contrôles par rôle.</summary>
    public static List<RolePermissionSet> DefaultPermissions()
    {
        RolePermissionSet Make(string role, params string[] allowed)
        {
            var set = new RolePermissionSet { Role = role };
            foreach (var def in Models.Permissions.Toutes)
                set.Allowed[def.Key] = allowed.Contains(def.Key);
            return set;
        }
        var admin = new[]
        {
            Models.Permissions.UsersView, Models.Permissions.UsersManage, Models.Permissions.FieldsManage,
            Models.Permissions.ArchiveView, Models.Permissions.ActivityView, Models.Permissions.CalendarUse,
            Models.Permissions.TachesUse, Models.Permissions.DocumentsUse, Models.Permissions.CommunityUse,
            Models.Permissions.JournalMediaUse, Models.Permissions.SettingsManage, Models.Permissions.SyncUse,
            Models.Permissions.ExportData, Models.Permissions.ImportData, Models.Permissions.RolesManage,
            Models.Permissions.MessagerieUse, Models.Permissions.RappelsUse, Models.Permissions.CarteMembreView,
            Models.Permissions.VotesManage, Models.Permissions.FinancesManage, Models.Permissions.MaterielManage,
            Models.Permissions.PresenceManage, Models.Permissions.SituationsManage
        };
        var manager = new[]
        {
            Models.Permissions.UsersView, Models.Permissions.UsersManage,
            Models.Permissions.ActivityView, Models.Permissions.CalendarUse,
            Models.Permissions.TachesUse, Models.Permissions.DocumentsUse, Models.Permissions.CommunityUse,
            Models.Permissions.JournalMediaUse, Models.Permissions.SettingsManage, Models.Permissions.SyncUse,
            Models.Permissions.ExportData, Models.Permissions.ImportData,
            Models.Permissions.MessagerieUse, Models.Permissions.RappelsUse, Models.Permissions.CarteMembreView,
            Models.Permissions.VotesManage, Models.Permissions.FinancesManage, Models.Permissions.MaterielManage,
            Models.Permissions.PresenceManage, Models.Permissions.SituationsManage
        };
        var user = new[]
        {
            Models.Permissions.CalendarUse,
            Models.Permissions.TachesUse, Models.Permissions.DocumentsUse, Models.Permissions.CommunityUse,
            Models.Permissions.JournalMediaUse, Models.Permissions.SettingsManage,
            Models.Permissions.MessagerieUse, Models.Permissions.RappelsUse, Models.Permissions.CarteMembreView
        };
        return new List<RolePermissionSet>
        {
            Make(Models.Roles.Administrateur, admin),
            Make(Models.Roles.Gestionnaire, manager),
            Make(Models.Roles.Utilisateur, user)
        };
    }

    /// <summary>Le rôle possède-t-il la permission ? Le compte technique possède tout.</summary>
    public bool HasPermission(string? role, string permission)
    {
        if (role == Models.Roles.BaseAdmin) return true;
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(permission)) return false;
        var set = _permissions.FirstOrDefault(p => p.Role == role);
        return set != null && set.Allowed.TryGetValue(permission, out var v) && v;
    }

    /// <summary>L'utilisateur connecté possède-t-il la permission ?</summary>
    public bool Can(string permission) => HasPermission(CurrentRole, permission);

    /// <summary>Enregistre la matrice (réservé aux administrateurs, anti-blocage inclus).</summary>
    public OperationResult SaveRolePermissions(List<RolePermissionSet> sets)
    {
        if (!IsAdmin)
            return OperationResult.Fail(L.T("Seul un administrateur peut modifier les rôles et permissions."));
        var check = ValidatePermissionSets(sets);
        if (!check.Success) return check;
        var admin = sets.First(s => s.Role == Models.Roles.Administrateur);
        if (!admin.Allowed.GetValueOrDefault(Models.Permissions.RolesManage))
            return OperationResult.Fail(L.T("Impossible de retirer « Gérer les rôles et permissions » aux administrateurs (verrou anti-blocage)."));
        _permissions = sets.Select(s => new RolePermissionSet
        {
            Role = s.Role,
            Allowed = Models.Permissions.Toutes.ToDictionary(d => d.Key, d => s.Allowed.GetValueOrDefault(d.Key))
        }).ToList();
        SavePermissions();
        LogActivity(Models.ActivityTypes.RolesChange, "Mise à jour de la matrice des rôles et permissions");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    private static OperationResult ValidatePermissionSets(List<RolePermissionSet>? sets)
    {
        if (sets == null)
            return OperationResult.Fail(L.T("La matrice des permissions est invalide."));
        var knownRoles = new HashSet<string>(Models.Roles.MatrixRoles);
        var knownKeys = new HashSet<string>(Models.Permissions.Toutes.Select(d => d.Key));
        if (sets.Count != knownRoles.Count || !sets.All(s => knownRoles.Contains(s.Role)))
            return OperationResult.Fail(L.F("La matrice doit contenir exactement les rôles : {0}.",
                string.Join(", ", Models.Roles.MatrixRoles)));
        foreach (var s in sets)
        {
            if (s.Allowed == null)
                return OperationResult.Fail(L.F("Permissions manquantes pour le rôle « {0} ».", s.Role));
            var unknown = s.Allowed.Keys.Where(k => !knownKeys.Contains(k)).ToList();
            if (unknown.Count > 0)
                return OperationResult.Fail(L.F("Permission inconnue : {0}.", string.Join(", ", unknown)));
        }
        return OperationResult.Ok();
    }

    /// <summary>Complète les clés manquantes (montées de version) avec les valeurs par défaut.</summary>
    private void NormalizePermissionKeys()
    {
        var defaults = DefaultPermissions().ToDictionary(p => p.Role);
        var changed = false;
        foreach (var set in _permissions)
        {
            set.Allowed ??= new Dictionary<string, bool>();
            if (!defaults.TryGetValue(set.Role, out var def)) continue;
            foreach (var kv in def.Allowed)
            {
                if (!set.Allowed.ContainsKey(kv.Key)) { set.Allowed[kv.Key] = kv.Value; changed = true; }
            }
        }
        if (changed) SavePermissions();
    }

    // ---------- Synchronisation locale (fichier d'échange, fusion, conflits) ----------

    /// <summary>Identifiant stable de cet appareil pour la synchronisation.</summary>
    public string DeviceId => _storage.Get(K_DEVICE) ?? "";

    /// <summary>Date ISO de la dernière synchronisation appliquée (ou null).</summary>
    public string? LastSyncAt => _sync.LastSyncAt;

    /// <summary>Nom du dernier appareil partenaire (ou null).</summary>
    public string? LastPeerName => _sync.LastPeerName;

    /// <summary>Construit le paquet d'échange JSON à partager (Bluetooth, USB, PC…).</summary>
    public string BuildSyncPackageJson(SyncOptions options, string deviceName)
    {
        options ??= new SyncOptions();
        var package = new SyncPackage
        {
            Version = 1,
            ExportedAt = DateTimeOffset.Now.ToString("O"),
            DeviceId = DeviceId,
            DeviceName = (deviceName ?? "").Trim(),
            Options = options,
            Users = options.Users ? _users.ToList() : new List<UserAccount>(),
            Fields = options.Fields ? _fields.ToList() : new List<FieldDefinition>(),
            Events = options.Events ? _events.ToList() : new List<CalendarEvent>(),
            Documents = options.Documents ? StripDocumentFiles(_documents.ToList(), options.DocumentFiles) : new List<DocumentItem>(),
            Folders = options.Folders ? _folders.ToList() : new List<DocumentFolder>(),
            Community = options.Community
                ? new CommunityStory { Titre = _community.Titre, SousTitre = _community.SousTitre, Chapitres = _community.Chapitres.ToList() }
                : new CommunityStory(),
            Archives = options.Archives ? _archive.ToList() : new List<ArchivedUser>(),
            Activity = options.Activity ? _activity.Take(500).ToList() : new List<ActivityEntry>(),
            Settings = options.Settings ? _settings : null,
            Permissions = options.Permissions ? RolePermissions.ToList() : new List<RolePermissionSet>(),
            Messages = options.Messages ? _messages.ToList() : new List<Message>(),
            Taches = options.Taches ? _taches.ToList() : new List<Tache>(),
            Cotisations = options.Cotisations ? _cotisations.ToList() : new List<Cotisation>(),
            Tresorerie = options.Tresorerie ? _tresorerie.ToList() : new List<TransactionTresorerie>(),
            Materiels = options.Materiels ? _materiels.ToList() : new List<Materiel>(),
            Prets = options.Materiels ? _prets.ToList() : new List<PretMateriel>(),
            Reunions = options.Reunions ? _reunions.ToList() : new List<Reunion>(),
            Situations = options.Situations ? _situations.ToList() : new List<Situation>(),
            Votes = options.Votes ? _votes.ToList() : new List<Vote>(),
            JournalMedia = options.JournalMedia ? StripJournalFiles(_journalMedia.ToList(), options.JournalMediaFiles) : new List<MediaItem>()
        };
        RefreshSyncSnapshot();
        LogActivity(Models.ActivityTypes.SyncExport,
            "Export de synchronisation (" + CountPackageRecords(package) + " enregistrements) vers « " + package.DeviceName + " »");
        return Serialize(package);
    }

    private static List<MediaItem> StripJournalFiles(List<MediaItem> items, bool withFiles)
    {
        if (withFiles) return items;
        return items.Select(m => new MediaItem
        {
            Id = m.Id, Titre = m.Titre, Description = m.Description, Type = m.Type,
            Mime = m.Mime, Taille = m.Taille, Data = "", Date = m.Date,
            DateCreation = m.DateCreation, CreatedBy = m.CreatedBy
        }).ToList();
    }

    private static List<DocumentItem> StripDocumentFiles(List<DocumentItem> documents, bool withFiles)
    {
        if (withFiles) return documents;
        return documents.Select(d => new DocumentItem
        {
            Id = d.Id, Titre = d.Titre, Description = d.Description, FolderId = d.FolderId,
            Categorie = d.Categorie, Tags = d.Tags.ToList(), Favori = d.Favori,
            FileName = d.FileName, Mime = d.Mime, Taille = d.Taille, Data = "",
            UtilisateurId = d.UtilisateurId, DateCreation = d.DateCreation,
            DateModification = d.DateModification, CreatedBy = d.CreatedBy, ModifiePar = d.ModifiePar,
            Supprime = d.Supprime, DateSuppression = d.DateSuppression
        }).ToList();
    }

    private static int CountPackageRecords(SyncPackage p) =>
        p.Users.Count + p.Fields.Count + p.Events.Count + p.Documents.Count + p.Folders.Count
        + p.Community.Chapitres.Count + p.Archives.Count + p.Activity.Count
        + p.Messages.Count + p.Taches.Count + p.Cotisations.Count + p.Tresorerie.Count
        + p.Materiels.Count + p.Prets.Count + p.Reunions.Count
        + p.Situations.Count + p.Votes.Count + p.JournalMedia.Count;

    /// <summary>Lit et valide un paquet reçu (refuse les paquets de cet appareil).</summary>
    public (SyncPackage? Package, string? Error) ParseSyncPackage(string jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
            return (null, L.T("Le fichier est vide."));
        SyncPackage? package;
        try { package = JsonSerializer.Deserialize<SyncPackage>(jsonText, JsonOpts); }
        catch (JsonException ex) { return (null, L.F("Fichier illisible : {0}", ex.Message)); }
        if (package == null) return (null, L.T("Le fichier ne contient pas un paquet de synchronisation."));
        if (package.Version != 1) return (null, L.F("Version de paquet non prise en charge ({0}).", package.Version));
        package.Options ??= new SyncOptions();
        package.Users ??= new List<UserAccount>();
        package.Fields ??= new List<FieldDefinition>();
        package.Events ??= new List<CalendarEvent>();
        package.Documents ??= new List<DocumentItem>();
        package.Folders ??= new List<DocumentFolder>();
        package.Community ??= new CommunityStory();
        package.Community.Chapitres ??= new List<CommunityChapter>();
        package.Archives ??= new List<ArchivedUser>();
        package.Activity ??= new List<ActivityEntry>();
        package.Permissions ??= new List<RolePermissionSet>();
        package.Messages ??= new List<Message>();
        package.Taches ??= new List<Tache>();
        package.Cotisations ??= new List<Cotisation>();
        package.Tresorerie ??= new List<TransactionTresorerie>();
        package.Materiels ??= new List<Materiel>();
        package.Prets ??= new List<PretMateriel>();
        package.Reunions ??= new List<Reunion>();
        package.Situations ??= new List<Situation>();
        package.Votes ??= new List<Vote>();
        foreach (var v in package.Votes) { v.Options ??= new List<string>(); v.Bulletins ??= new List<Models.Bulletin>(); }
        package.JournalMedia ??= new List<MediaItem>();
        if (!string.IsNullOrWhiteSpace(package.DeviceId) && package.DeviceId == DeviceId)
            return (null, L.T("Ce paquet a été généré par cet appareil : rien à fusionner."));
        return (package, null);
    }

    /// <summary>Analyse un paquet sans rien modifier (aperçu + conflits).</summary>
    public SyncPreview PreviewSyncImport(SyncPackage package)
    {
        var plan = BuildSyncPlan(package);
        var preview = new SyncPreview
        {
            Success = true,
            DeviceName = package.DeviceName,
            ExportedAt = package.ExportedAt,
            Conflicts = plan.Conflicts
        };
        foreach (var a in plan.Actions)
        {
            var dict = a.DropLocal ? preview.DeletedOnPeer
                : a.TakePeer && plan.SnapshotHad(a.Collection, a.Key) ? preview.UpdatedFromPeer
                : preview.NewFromPeer;
            var label = CollectionLabel(a.Collection);
            dict[label] = dict.GetValueOrDefault(label) + 1;
        }
        preview.Warning = plan.Warning;
        return preview;
    }

    /// <summary>Applique un paquet avec les résolutions de conflits de l'utilisateur.</summary>
    public OperationResult ApplySyncImport(SyncPackage package, List<SyncResolution>? resolutions)
    {
        var plan = BuildSyncPlan(package);
        var choices = (resolutions ?? new List<SyncResolution>())
            .Where(r => r.UseIncoming)
            .Select(r => r.Collection + "\u0000" + r.Key)
            .ToHashSet();
        var dismissedChanged = false;
        foreach (var c in plan.Conflicts)
        {
            var actions = plan.Actions.Where(a => a.Collection == c.Collection && a.Key == c.Key).ToList();
            var take = choices.Contains(c.Collection + "\u0000" + c.Key);
            foreach (var a in actions) { a.TakePeer = take; a.DropLocal = take && a.DropOnIncoming; }
            if (!take)
            {
                // « Conserver le local » : ne plus redemander tant que l'entrant ne change pas.
                var mapKey = c.Collection + "\u0000" + c.Key;
                var peerHash = actions.FirstOrDefault()?.PeerHash ?? "";
                _sync.Dismissed[mapKey] = peerHash;
                dismissedChanged = true;
            }
        }
        if (dismissedChanged) SaveSyncSnapshot();

        var touchedUsers = false; var touchedFields = false; var touchedEvents = false;
        var touchedDocs = false; var touchedFolders = false; var touchedCommunity = false;
        var touchedArchives = false; var touchedActivity = false;
        var touchedSettings = false; var touchedPermissions = false;
        var touchedMessages = false; var touchedTaches = false;
        var touchedFinances = false; var touchedMateriels = false; var touchedReunions = false;
        var touchedVague4 = false;
        foreach (var a in plan.Actions.Where(a => a.TakePeer || a.DropLocal))
        {
            switch (a.Collection)
            {
                case "users":
                    if (a.DropLocal) { touchedUsers = true; touchedArchives = true; ArchiveLocalUser(a.Key, package.DeviceName); }
                    else if (a.Peer is UserAccount u) { touchedUsers = true; UpsertUser(u); }
                    break;
                case "fields":
                    if (a.DropLocal) { touchedFields = true; _fields.RemoveAll(f => f.Id == a.Key); }
                    else if (a.Peer is FieldDefinition f) { touchedFields = true; UpsertById(_fields, f.Id, f); }
                    break;
                case "events":
                    if (a.DropLocal) { touchedEvents = true; _events.RemoveAll(e => e.Id == a.Key); }
                    else if (a.Peer is CalendarEvent e) { touchedEvents = true; UpsertById(_events, e.Id, e); }
                    break;
                case "documents":
                    if (a.DropLocal) { touchedDocs = true; _documents.RemoveAll(d => d.Id == a.Key); }
                    else if (a.Peer is DocumentItem d) { touchedDocs = true; UpsertDocument(d); }
                    break;
                case "folders":
                    if (a.DropLocal) { touchedFolders = true; touchedDocs = true; DeleteFolderKeepContents(a.Key); }
                    else if (a.Peer is DocumentFolder f) { touchedFolders = true; UpsertById(_folders, f.Id, f); }
                    break;
                case "community":
                    if (a.Key == "__header" && a.Peer is CommunityStory h)
                    {
                        touchedCommunity = true;
                        _community.Titre = h.Titre; _community.SousTitre = h.SousTitre;
                    }
                    else if (a.DropLocal) { touchedCommunity = true; _community.Chapitres.RemoveAll(c => c.Id == a.Key); }
                    else if (a.Peer is CommunityChapter c) { touchedCommunity = true; UpsertById(_community.Chapitres, c.Id, c); }
                    break;
                case "archives":
                    if (a.DropLocal) { touchedArchives = true; _archive.RemoveAll(x => x.User.Id == a.Key); }
                    else if (a.Peer is ArchivedUser x) { touchedArchives = true; UpsertArchive(x); }
                    break;
                case "activity":
                    break; // fusionnée en bloc ci-dessous
                case "settings":
                    if (a.Peer is AppSettings s)
                    {
                        touchedSettings = true;
                        s.AppName = string.IsNullOrWhiteSpace(s.AppName) ? "Gestion des utilisateurs" : s.AppName.Trim();
                        s.Theme = s.Theme == "dark" ? "dark" : "light";
                        s.PageSize = Math.Clamp(s.PageSize, 10, 100);
                        _settings = s;
                    }
                    break;
                case "permissions":
                    if (a.Peer is RolePermissionSet perm)
                    {
                        touchedPermissions = true;
                        _permissions.RemoveAll(p => p.Role == perm.Role);
                        _permissions.Add(new RolePermissionSet { Role = perm.Role, Allowed = new Dictionary<string, bool>(perm.Allowed) });
                    }
                    break;
                case "messages":
                    if (a.DropLocal) { touchedMessages = true; _messages.RemoveAll(x => x.Id == a.Key); }
                    else if (a.Peer is Message msg) { touchedMessages = true; UpsertById(_messages, msg.Id, msg); }
                    break;
                case "taches":
                    if (a.DropLocal) { touchedTaches = true; _taches.RemoveAll(x => x.Id == a.Key); }
                    else if (a.Peer is Tache tach) { touchedTaches = true; UpsertById(_taches, tach.Id, tach); }
                    break;
                case "cotisations":
                    if (a.DropLocal) { touchedFinances = true; _cotisations.RemoveAll(x => x.Id == a.Key); }
                    else if (a.Peer is Cotisation cot) { touchedFinances = true; UpsertById(_cotisations, cot.Id, cot); }
                    break;
                case "tresorerie":
                    if (a.DropLocal) { touchedFinances = true; _tresorerie.RemoveAll(x => x.Id == a.Key); }
                    else if (a.Peer is TransactionTresorerie treso) { touchedFinances = true; UpsertById(_tresorerie, treso.Id, treso); }
                    break;
                case "materiels":
                    if (a.DropLocal) { touchedMateriels = true; _materiels.RemoveAll(x => x.Id == a.Key); }
                    else if (a.Peer is Materiel mat) { touchedMateriels = true; UpsertById(_materiels, mat.Id, mat); }
                    break;
                case "prets":
                    if (a.DropLocal) { touchedMateriels = true; _prets.RemoveAll(x => x.Id == a.Key); }
                    else if (a.Peer is PretMateriel pret) { touchedMateriels = true; UpsertById(_prets, pret.Id, pret); }
                    break;
                case "reunions":
                    if (a.DropLocal) { touchedReunions = true; _reunions.RemoveAll(x => x.Id == a.Key); }
                    else if (a.Peer is Reunion reun) { touchedReunions = true; UpsertById(_reunions, reun.Id, reun); }
                    break;
                case "situations":
                    if (a.DropLocal) { touchedVague4 = true; _situations.RemoveAll(x => x.Id == a.Key); }
                    else if (a.Peer is Situation sit) { touchedVague4 = true; UpsertById(_situations, sit.Id, sit); }
                    break;
                case "votes":
                    if (a.DropLocal) { touchedVague4 = true; _votes.RemoveAll(x => x.Id == a.Key); }
                    else if (a.Peer is Vote vote) { touchedVague4 = true; UpsertVote(vote); }
                    break;
                case "journal_media":
                    if (a.DropLocal) { touchedVague4 = true; _journalMedia.RemoveAll(x => x.Id == a.Key); }
                    else if (a.Peer is MediaItem med) { touchedVague4 = true; UpsertJournalMedia(med); }
                    break;
            }
        }

        // Journal d'activité : union par Id, les plus récents d'abord, plafond 500.
        if (package.Options.Activity && package.Activity.Count > 0)
        {
            touchedActivity = true;
            var known = new HashSet<string>(_activity.Select(e => e.Id));
            foreach (var e in package.Activity)
            {
                if (string.IsNullOrWhiteSpace(e.Id) || !known.Add(e.Id)) continue;
                _activity.Add(new ActivityEntry
                {
                    Id = e.Id, Date = string.IsNullOrWhiteSpace(e.Date) ? DateTimeOffset.Now.ToString("O") : e.Date,
                    User = e.User ?? "", Type = e.Type ?? "", Description = e.Description ?? ""
                });
            }
            var ordered = _activity.OrderByDescending(e => e.Date).Take(500).ToList();
            _activity.Clear();
            _activity.AddRange(ordered);
        }

        // Garde-fou : jamais sans administrateur actif.
        if (!_users.Any(u => Models.Roles.IsAdmin(u.Role) && u.Actif))
            return OperationResult.Fail(L.T("La synchronisation supprimerait le dernier administrateur actif : opération annulée."));

        if (touchedUsers) SaveUsers();
        if (touchedFields) { ReindexFields(); SaveFields(); }
        if (touchedEvents) { _events.Sort((a, b) => SortKey(a).CompareTo(SortKey(b))); SaveEvents(); }
        if (touchedDocs) SaveDocuments();
        if (touchedFolders) SaveFolders();
        if (touchedCommunity) SaveCommunity();
        if (touchedArchives) SaveArchive();
        if (touchedActivity) SaveActivity();
        if (touchedSettings) SaveSettings();
        if (touchedPermissions) SavePermissions();
        if (touchedMessages) SaveMessages();
        if (touchedTaches) SaveTaches();
        if (touchedFinances) { SaveCotisations(); SaveTresorerie(); }
        if (touchedMateriels) { SaveMateriels(); SavePrets(); }
        if (touchedReunions) SaveReunions();
        if (touchedVague4) { SaveSituations(); SaveVotes(); SaveJournalMedia(); }

        _sync.LastSyncAt = DateTimeOffset.Now.ToString("O");
        _sync.LastPeerName = string.IsNullOrWhiteSpace(package.DeviceName) ? "(appareil)" : package.DeviceName;
        RefreshSyncSnapshot();
        LogActivity(Models.ActivityTypes.SyncImport,
            "Import de synchronisation depuis « " + _sync.LastPeerName + " » ("
            + plan.Actions.Count(a => a.TakePeer || a.DropLocal) + " changements)");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    // ----- Moteur de fusion (privé) -----

    private sealed class PlanEntry
    {
        public string Key { get; init; } = "";
        public string Hash { get; init; } = "";
        public string Titre { get; init; } = "";
        public string Detail { get; init; } = "";
        public object? Record { get; init; }
    }

    private sealed class PlanAction
    {
        public string Collection { get; init; } = "";
        public string Key { get; init; } = "";
        public bool TakePeer { get; set; }
        public bool DropLocal { get; set; }
        /// <summary>En cas de conflit résolu « entrant », faut-il aussi supprimer le local ?</summary>
        public bool DropOnIncoming { get; init; }
        public object? Peer { get; init; }
        /// <summary>Hash distant au moment de l'analyse ("" si absent en face).</summary>
        public string PeerHash { get; init; } = "";
    }

    private sealed class SyncPlan
    {
        public List<PlanAction> Actions { get; } = new();
        public List<SyncConflict> Conflicts { get; } = new();
        public string? Warning { get; set; }
        private readonly HashSet<string> _snapKeys = new();
        public void MarkSnap(string collection, string key) => _snapKeys.Add(collection + "\u0000" + key);
        public bool SnapshotHad(string collection, string key) => _snapKeys.Contains(collection + "\u0000" + key);
    }

    /// <summary>Libellé français d'une collection pour l'aperçu.</summary>
    private static string CollectionLabel(string collection) => collection switch
    {
        "users" => "Utilisateurs",
        "fields" => "Champs",
        "events" => "Événements",
        "documents" => "Documents",
        "folders" => "Dossiers",
        "community" => "Communauté",
        "archives" => "Archives",
        "activity" => "Journal",
        "settings" => "Paramètres",
        "permissions" => "Permissions",
        "messages" => "Messages",
        "taches" => "Tâches",
        "cotisations" => "Cotisations",
        "tresorerie" => "Trésorerie",
        "materiels" => "Matériel",
        "prets" => "Prêts",
        "reunions" => "Réunions",
        "situations" => "Situations",
        "votes" => "Votes",
        "journal_media" => "Journal multimédia",
        _ => collection
    };

    private static string HashOf(string canonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    private string CanonUser(UserAccount u)
    {
        var champs = new SortedDictionary<string, FieldValue>(u.Champs ?? new Dictionary<string, FieldValue>(), StringComparer.Ordinal);
        return Serialize(new
        {
            u.Id, u.Prenom, u.Nom, u.Identifiant, u.Email, u.Telephone, u.Role, u.Actif,
            u.MotDePasse, u.Notes, u.DateCreation, u.DerniereConnexion, u.Photo, u.DateNaissance, u.Matricule, u.Signature, Champs = champs
        });
    }

    private string CanonMedia(MediaItem m, bool withFiles) => Serialize(new
    {
        m.Id, m.Titre, m.Description, m.Type, m.Mime, m.Taille,
        Data = withFiles ? m.Data : "", m.Date, m.DateCreation, m.CreatedBy
    });

    private string CanonDoc(DocumentItem d, bool withFiles) => Serialize(new    {
        d.Id, d.Titre, d.Description, d.FolderId, d.Categorie,
        Tags = d.Tags ?? new List<string>(), d.Favori, d.FileName, d.Mime, d.Taille,
        Data = withFiles ? d.Data : "", d.UtilisateurId, d.DateCreation, d.DateModification,
        d.CreatedBy, d.Supprime, d.DateSuppression
    });

    private SyncPlan BuildSyncPlan(SyncPackage package)
    {
        var plan = new SyncPlan();
        var snap = _sync.Hashes ?? new Dictionary<string, string>();
        string SnapHash(string collection, string key)
            => snap.TryGetValue(collection + "\u0000" + key, out var h) ? h : "";
        bool HasSnapKeys(string collection) => snap.Keys.Any(k =>
        {
            var sep = k.IndexOf('\u0000');
            return sep > 0 && k[..sep] == collection;
        });

        // Utilisateurs (clé = identifiant insensible à la casse) + propagation des suppressions via les archives.
        if (package.Options.Users)
        {
            var local = _users.ToDictionary(UserKey, u => new PlanEntry
            {
                Key = UserKey(u), Hash = HashOf(CanonUser(u)), Titre = u.NomComplet,
                Detail = u.Identifiant + " · " + u.Role + " · " + u.Statut, Record = u
            }, StringComparer.Ordinal);
            var peer = new Dictionary<string, PlanEntry>(StringComparer.Ordinal);
            foreach (var u in package.Users)
            {
                var key = UserKey(u);
                if (peer.ContainsKey(key)) continue;
                peer[key] = new PlanEntry
                {
                    Key = key, Hash = HashOf(CanonUser(u)), Titre = (u.Nom + " " + u.Prenom).Trim(),
                    Detail = u.Identifiant + " · " + u.Role + " · " + (u.Actif ? "Actif" : "Inactif"), Record = u
                };
            }
            var peerArchivedIds = new HashSet<string>(
                package.Archives.Select(a => a.User?.Id ?? "").Where(id => id.Length > 0));
            if (peer.Count == 0 && package.Archives.Count == 0 && local.Count > 0 && HasSnapKeys("users"))
            {
                // Paquet sans utilisateurs ni archives alors qu'on en suit : pas de vidage automatique.
                plan.Warning = L.T("Aucun utilisateur dans le paquet : suppressions ignorées par sécurité.");
            }
            else
            {
                RememberOnly(plan, "users", local);
                MergeKeyed(plan, "users", local, peer, SnapHash,
                    onPeerDeleted: (key, entry, peerHasArchive) =>
                    {
                        // Supprimé en face : propager (vers les archives) sauf modification locale.
                        if (entry == null) return;
                        if (peerHasArchive)
                            plan.Actions.Add(new PlanAction { Collection = "users", Key = key, DropLocal = true, PeerHash = "" });
                        else
                            AddConflict(plan, "users", key, entry.Titre, entry.Detail,
                                "Supprimé sur « " + PeerLabel(package) + " »", "deletedPeer", peerHash: "");
                    },
                    peerDeletedHint: key =>
                    {
                        var id = local.TryGetValue(key, out var e) && e.Record is UserAccount u ? u.Id : "";
                        return id.Length > 0 && peerArchivedIds.Contains(id);
                    },
                    peerLabel: PeerLabel(package));
            }
        }

        MergeRecordCollection(plan, package, "fields", package.Options.Fields,
            _fields.ToDictionary(f => f.Id, f => Entry(f.Id, HashOf(Serialize(f)), f.Label, FieldTypeInfo.Label(f.Type), f)),
            package.Fields.Where(f => !string.IsNullOrWhiteSpace(f.Id)).GroupBy(f => f.Id).ToDictionary(
                g => g.Key, g => Entry(g.Key, HashOf(Serialize(g.First())), g.First().Label, FieldTypeInfo.Label(g.First().Type), g.First())),
            SnapHash);

        MergeRecordCollection(plan, package, "events", package.Options.Events,
            _events.ToDictionary(e => e.Id, e => Entry(e.Id, HashOf(Serialize(e)), e.Titre, e.Date, e)),
            package.Events.Where(e => !string.IsNullOrWhiteSpace(e.Id)).GroupBy(e => e.Id).ToDictionary(
                g => g.Key, g => Entry(g.Key, HashOf(Serialize(g.First())), g.First().Titre, g.First().Date, g.First())),
            SnapHash);

        if (package.Options.Documents)
        {
            var withFiles = package.Options.DocumentFiles;
            var local = _documents.ToDictionary(d => d.Id, d => Entry(d.Id, HashOf(CanonDoc(d, true)), d.Titre, d.FileName, d));
            var peer = new Dictionary<string, PlanEntry>();
            foreach (var d in package.Documents.Where(d => !string.IsNullOrWhiteSpace(d.Id)))
            {
                if (peer.ContainsKey(d.Id)) continue;
                var h = HashOf(CanonDoc(d, withFiles));
                if (!withFiles && local.TryGetValue(d.Id, out var le) && le.Record is DocumentItem ld && !string.IsNullOrWhiteSpace(ld.Data))
                    h = le.Hash; // sans les binaires, un contenu identique ne doit pas créer de conflit
                peer[d.Id] = Entry(d.Id, h, d.Titre, d.FileName, d);
            }
            MergeMaps(plan, "documents", local, peer, SnapHash);
            RememberOnly(plan, "documents", local);
        }

        MergeRecordCollection(plan, package, "folders", package.Options.Folders,
            _folders.ToDictionary(f => f.Id, f => Entry(f.Id, HashOf(Serialize(f)), f.Nom, FolderPathLabel(f.ParentId), f)),
            package.Folders.Where(f => !string.IsNullOrWhiteSpace(f.Id)).GroupBy(f => f.Id).ToDictionary(
                g => g.Key, g => Entry(g.Key, HashOf(Serialize(g.First())), g.First().Nom, FolderPathLabel(g.First().ParentId), g.First())),
            SnapHash);

        // Communauté : chapitres + en-tête (titre / sous-titre).
        if (package.Options.Community)
        {
            var local = _community.Chapitres.ToDictionary(c => c.Id, c => Entry(c.Id, HashOf(Serialize(c)), c.Titre, c.Date, c));
            var peer = new Dictionary<string, PlanEntry>();
            foreach (var c in package.Community.Chapitres.Where(c => !string.IsNullOrWhiteSpace(c.Id)))
            {
                if (peer.ContainsKey(c.Id)) continue;
                peer[c.Id] = Entry(c.Id, HashOf(Serialize(c)), c.Titre, c.Date, c);
            }
            MergeMaps(plan, "community", local, peer, SnapHash);
            RememberOnly(plan, "community", local);
            var localHeader = HashOf(Serialize(new { _community.Titre, _community.SousTitre }));
            var peerHeader = HashOf(Serialize(new { package.Community.Titre, package.Community.SousTitre }));
            MergeSingle(plan, "community", "__header", localHeader, peerHeader,
                SnapHash("community", "__header"), "Titre et sous-titre",
                _community.Titre, package.Community.Titre ?? "",
                new CommunityStory { Titre = package.Community.Titre ?? "", SousTitre = package.Community.SousTitre ?? "" });
            plan.MarkSnap("community", "__header");
        }

        // Archives : union par Id d'utilisateur (une purge propage la suppression).
        if (package.Options.Archives)
        {
            var local = _archive.ToDictionary(a => a.User.Id, a => Entry(a.User.Id, HashOf(Serialize(a)),
                a.User.NomComplet, "Archivé le " + FormatExportDate(a.DeletedAt), a));
            var peer = new Dictionary<string, PlanEntry>();
            foreach (var a in package.Archives.Where(a => a.User != null && !string.IsNullOrWhiteSpace(a.User.Id)))
            {
                if (peer.ContainsKey(a.User.Id)) continue;
                peer[a.User.Id] = Entry(a.User.Id, HashOf(Serialize(a)),
                    a.User.NomComplet, "Archivé le " + FormatExportDate(a.DeletedAt), a);
            }
            MergeMaps(plan, "archives", local, peer, SnapHash);
            RememberOnly(plan, "archives", local);
        }

        // Paramètres : objet unique.
        if (package.Options.Settings && package.Settings != null)
        {
            var localH = HashOf(Serialize(_settings));
            var peerH = HashOf(Serialize(package.Settings));
            MergeSingle(plan, "settings", "app", localH, peerH, SnapHash("settings", "app"),
                "Paramètres de l'application", _settings.AppName, package.Settings.AppName ?? "",
                package.Settings);
            plan.MarkSnap("settings", "app");
        }

        // Permissions : un enregistrement par rôle.
        if (package.Options.Permissions && package.Permissions.Count > 0)
        {
            var local = _permissions.ToDictionary(p => p.Role, p => Entry(p.Role, HashOf(Serialize(NormalizeRoleSet(p))),
                "Rôle « " + p.Role + " »", p.Allowed.Count(kv => kv.Value) + " droits", p));
            var peer = new Dictionary<string, PlanEntry>();
            foreach (var p in package.Permissions.Where(p => Models.Roles.MatrixRoles.Contains(p.Role)))
            {
                if (peer.ContainsKey(p.Role)) continue;
                var norm = NormalizeRoleSet(p);
                peer[p.Role] = Entry(p.Role, HashOf(Serialize(norm)),
                    "Rôle « " + p.Role + " »", norm.Allowed.Count(kv => kv.Value) + " droits", norm);
            }
            MergeMaps(plan, "permissions", local, peer, SnapHash);
            RememberOnly(plan, "permissions", local);
        }

        // Messagerie : fusion par message (une suppression distante propage la suppression).
        if (package.Options.Messages)
        {
            var local = _messages.ToDictionary(m => m.Id, m => Entry(m.Id, HashOf(Serialize(m)),
                "Message de " + m.Expediteur, Truncate(m.Texte ?? "", 60), m));
            var peer = new Dictionary<string, PlanEntry>();
            foreach (var m in package.Messages.Where(m => !string.IsNullOrWhiteSpace(m.Id)))
            {
                if (peer.ContainsKey(m.Id)) continue;
                peer[m.Id] = Entry(m.Id, HashOf(Serialize(m)),
                    "Message de " + (m.Expediteur ?? ""), Truncate(m.Texte ?? "", 60), m);
            }
            MergeRecordCollection(plan, package, "messages", true, local, peer, SnapHash);
        }

        // Tâches : fusion par tâche (suivi d'avancement des deux côtés).
        if (package.Options.Taches)
        {
            var local = _taches.ToDictionary(t => t.Id, t => Entry(t.Id, HashOf(Serialize(t)),
                t.Titre, Models.TacheStatuts.Label(t.Statut), t));
            var peer = new Dictionary<string, PlanEntry>();
            foreach (var t in package.Taches.Where(t => !string.IsNullOrWhiteSpace(t.Id)))
            {
                if (peer.ContainsKey(t.Id)) continue;
                peer[t.Id] = Entry(t.Id, HashOf(Serialize(t)),
                    t.Titre, Models.TacheStatuts.Label(t.Statut), t);
            }
            MergeRecordCollection(plan, package, "taches", true, local, peer, SnapHash);
        }

        // Finances : cotisations et trésorerie fusionnées par enregistrement.
        if (package.Options.Cotisations)
        {
            var local = _cotisations.ToDictionary(c => c.Id, c => Entry(c.Id, HashOf(Serialize(c)),
                FormatMontant(c.Montant) + " — " + c.MembreIdentifiant, c.Periode, c));
            var peer = new Dictionary<string, PlanEntry>();
            foreach (var c in package.Cotisations.Where(c => !string.IsNullOrWhiteSpace(c.Id)))
            {
                if (peer.ContainsKey(c.Id)) continue;
                peer[c.Id] = Entry(c.Id, HashOf(Serialize(c)),
                    FormatMontant(c.Montant) + " — " + c.MembreIdentifiant, c.Periode, c);
            }
            MergeRecordCollection(plan, package, "cotisations", true, local, peer, SnapHash);
        }
        if (package.Options.Tresorerie)
        {
            var local = _tresorerie.ToDictionary(t => t.Id, t => Entry(t.Id, HashOf(Serialize(t)),
                (t.Type == TresorerieTypes.Sortie ? "−" : "+") + FormatMontant(t.Montant), t.Motif, t));
            var peer = new Dictionary<string, PlanEntry>();
            foreach (var t in package.Tresorerie.Where(t => !string.IsNullOrWhiteSpace(t.Id)))
            {
                if (peer.ContainsKey(t.Id)) continue;
                peer[t.Id] = Entry(t.Id, HashOf(Serialize(t)),
                    (t.Type == TresorerieTypes.Sortie ? "−" : "+") + FormatMontant(t.Montant), t.Motif, t);
            }
            MergeRecordCollection(plan, package, "tresorerie", true, local, peer, SnapHash);
        }

        // Matériel et prêts : fusion par enregistrement.
        if (package.Options.Materiels)
        {
            var local = _materiels.ToDictionary(m => m.Id, m => Entry(m.Id, HashOf(Serialize(m)),
                m.Nom, "×" + m.QuantiteTotale, m));
            var peer = new Dictionary<string, PlanEntry>();
            foreach (var m in package.Materiels.Where(m => !string.IsNullOrWhiteSpace(m.Id)))
            {
                if (peer.ContainsKey(m.Id)) continue;
                peer[m.Id] = Entry(m.Id, HashOf(Serialize(m)), m.Nom, "×" + m.QuantiteTotale, m);
            }
            MergeRecordCollection(plan, package, "materiels", true, local, peer, SnapHash);

            var localPrets = _prets.ToDictionary(p => p.Id, p => Entry(p.Id, HashOf(Serialize(p)),
                "Prêt ×" + p.Quantite, p.MembreIdentifiant, p));
            var peerPrets = new Dictionary<string, PlanEntry>();
            foreach (var p in package.Prets.Where(p => !string.IsNullOrWhiteSpace(p.Id)))
            {
                if (peerPrets.ContainsKey(p.Id)) continue;
                peerPrets[p.Id] = Entry(p.Id, HashOf(Serialize(p)),
                    "Prêt ×" + p.Quantite, p.MembreIdentifiant, p);
            }
            MergeRecordCollection(plan, package, "prets", true, localPrets, peerPrets, SnapHash);
        }

        // Réunions : fusion par réunion (présences incluses).
        if (package.Options.Reunions)
        {
            var local = _reunions.ToDictionary(r => r.Id, r => Entry(r.Id, HashOf(Serialize(r)),
                r.Titre, FormatDateCourte(r.Date), r));
            var peer = new Dictionary<string, PlanEntry>();
            foreach (var r in package.Reunions.Where(r => !string.IsNullOrWhiteSpace(r.Id)))
            {
                if (peer.ContainsKey(r.Id)) continue;
                peer[r.Id] = Entry(r.Id, HashOf(Serialize(r)),
                    r.Titre, FormatDateCourte(r.Date), r);
            }
            MergeRecordCollection(plan, package, "reunions", true, local, peer, SnapHash);
        }

        // Situations particulières : fusion par suivi.
        if (package.Options.Situations)
        {
            var local = _situations.ToDictionary(s => s.Id, s => Entry(s.Id, HashOf(Serialize(s)),
                s.Titre, Models.SituationStatuts.Label(s.Statut), s));
            var peer = new Dictionary<string, PlanEntry>();
            foreach (var s in package.Situations.Where(s => !string.IsNullOrWhiteSpace(s.Id)))
            {
                if (peer.ContainsKey(s.Id)) continue;
                peer[s.Id] = Entry(s.Id, HashOf(Serialize(s)),
                    s.Titre, Models.SituationStatuts.Label(s.Statut), s);
            }
            MergeRecordCollection(plan, package, "situations", true, local, peer, SnapHash);
        }

        // Votes : fusion par vote (bulletins inclus).
        if (package.Options.Votes)
        {
            var local = _votes.ToDictionary(v => v.Id, v => Entry(v.Id, HashOf(Serialize(v)),
                v.Titre, Models.VoteStatuts.Label(v.Statut), v));
            var peer = new Dictionary<string, PlanEntry>();
            foreach (var v in package.Votes.Where(v => !string.IsNullOrWhiteSpace(v.Id)))
            {
                if (peer.ContainsKey(v.Id)) continue;
                peer[v.Id] = Entry(v.Id, HashOf(Serialize(v)),
                    v.Titre, Models.VoteStatuts.Label(v.Statut), v);
            }
            MergeRecordCollection(plan, package, "votes", true, local, peer, SnapHash);
        }

        // Journal multimédia : fusion par élément (contenu binaire optionnel).
        if (package.Options.JournalMedia)
        {
            var withFiles = package.Options.JournalMediaFiles;
            var local = _journalMedia.ToDictionary(m => m.Id, m => Entry(m.Id, HashOf(CanonMedia(m, true)),
                m.Titre, Models.MediaTypes.Label(m.Type), m));
            var peer = new Dictionary<string, PlanEntry>();
            foreach (var m in package.JournalMedia.Where(m => !string.IsNullOrWhiteSpace(m.Id)))
            {
                if (peer.ContainsKey(m.Id)) continue;
                var h = HashOf(CanonMedia(m, withFiles));
                if (!withFiles && local.TryGetValue(m.Id, out var le) && le.Record is MediaItem lm && !string.IsNullOrWhiteSpace(lm.Data))
                    h = le.Hash;
                peer[m.Id] = Entry(m.Id, h, m.Titre, Models.MediaTypes.Label(m.Type), m);
            }
            MergeRecordCollection(plan, package, "journal_media", true, local, peer, SnapHash);
        }

        // Les suppressions distantes sont toujours détaillées dans l'aperçu
        // (DeletedOnPeer) : c'est l'interface qui exige une confirmation explicite
        // en cas de suppressions nombreuses. Le garde-fou ultime reste
        // l'interdiction de terminer sans administrateur actif (voir Apply).
        return plan;
    }

    private static string PeerLabel(SyncPackage package)
        => string.IsNullOrWhiteSpace(package.DeviceName) ? "l'autre appareil" : package.DeviceName;

    private static string UserKey(UserAccount u) => (u.Identifiant ?? "").Trim().ToLowerInvariant();

    private static PlanEntry Entry(string key, string hash, string titre, string? detail, object? record)
        => new() { Key = key, Hash = hash, Titre = titre ?? "", Detail = detail ?? "", Record = record };

    private string FolderPathLabel(string? folderId)
    {
        if (string.IsNullOrWhiteSpace(folderId)) return "Racine";
        return FindFolder(folderId)?.Nom ?? "Racine";
    }

    private static RolePermissionSet NormalizeRoleSet(RolePermissionSet p) => new()
    {
        Role = p.Role,
        Allowed = Models.Permissions.Toutes.ToDictionary(d => d.Key, d => p.Allowed.GetValueOrDefault(d.Key))
    };

    /// <summary>Fusion générique d'une collection à clés stables (champs, événements…).</summary>
    private void MergeRecordCollection(SyncPlan plan, SyncPackage package, string collection, bool included,
        Dictionary<string, PlanEntry> local, Dictionary<string, PlanEntry> peer,
        Func<string, string, string> snapHash)
    {
        if (!included) return;
        // Note : une liste entrante vide + base existante + local inchangé = suppression
        // distante légitime (propagée). Sans base (jamais synchronisé), on ne supprime jamais.
        // Les suppressions sont toujours listées dans l'aperçu pour validation humaine.
        MergeMaps(plan, collection, local, peer, snapHash);
        RememberOnly(plan, collection, local);
    }

    private void RememberOnly(SyncPlan plan, string collection, Dictionary<string, PlanEntry> local)
    {
        foreach (var k in local.Keys) plan.MarkSnap(collection, k);
        foreach (var k in (_sync.Hashes ?? new Dictionary<string, string>()).Keys)
        {
            var sep = k.IndexOf('\u0000');
            if (sep > 0 && k[..sep] == collection) plan.MarkSnap(collection, k[(sep + 1)..]);
        }
    }

    /// <summary>Cœur de la fusion 3 points (base = snapshot, local, entrant).</summary>
    private void MergeMaps(SyncPlan plan, string collection,
        Dictionary<string, PlanEntry> local, Dictionary<string, PlanEntry> peer,
        Func<string, string, string> snapHash)
    {
        var keys = new HashSet<string>(local.Keys);
        foreach (var k in peer.Keys) keys.Add(k);
        foreach (var key in keys)
        {
            var hasLocal = local.TryGetValue(key, out var le);
            var hasPeer = peer.TryGetValue(key, out var pe);
            var peerHash = hasPeer ? pe!.Hash : "";
            if (IsDismissed(collection, key, peerHash, hasLocal, hasPeer)) continue;
            var sh = snapHash(collection, key);
            var inSnap = sh.Length > 0;
            if (hasLocal && hasPeer)
            {
                if (le!.Hash == pe!.Hash) continue;
                if (!inSnap) { AddConflict(plan, collection, key, pe.Titre, le.Detail, pe.Detail, "modified", peerRecord: pe.Record, peerHash: pe.Hash); continue; }
                if (le.Hash == sh) plan.Actions.Add(new PlanAction { Collection = collection, Key = key, TakePeer = true, Peer = pe.Record, PeerHash = pe.Hash });
                else if (pe.Hash == sh) { /* conserver le local */ }
                else AddConflict(plan, collection, key, pe.Titre, le.Detail, pe.Detail, "modified", peerRecord: pe.Record, peerHash: pe.Hash);
            }
            else if (hasLocal && !hasPeer)
            {
                if (!inSnap) continue; // créé ici après la base : rien à faire
                if (le!.Hash == sh) plan.Actions.Add(new PlanAction { Collection = collection, Key = key, DropLocal = true, PeerHash = "" });
                else AddConflict(plan, collection, key, le.Titre, le.Detail,
                    "Supprimé sur l'autre appareil", "modified", dropOnIncoming: true, peerHash: "");
            }
            else if (!hasLocal && hasPeer)
            {
                if (!inSnap) plan.Actions.Add(new PlanAction { Collection = collection, Key = key, TakePeer = true, Peer = pe!.Record, PeerHash = pe.Hash });
                else if (pe!.Hash == sh) { /* déjà supprimé ici */ }
                else AddConflict(plan, collection, key, pe.Titre, "Supprimé sur cet appareil", pe.Detail,
                    "modified", dropOnIncoming: false, peerRecord: pe.Record, peerHash: pe.Hash);
            }
        }
    }

    /// <summary>Variante avec indice « supprimé en face » (utilisateurs ↔ archives).</summary>
    private void MergeKeyed(SyncPlan plan, string collection,
        Dictionary<string, PlanEntry> local, Dictionary<string, PlanEntry> peer,
        Func<string, string, string> snapHash,
        Action<string, PlanEntry?, bool> onPeerDeleted,
        Func<string, bool> peerDeletedHint,
        string peerLabel)
    {
        var keys = new HashSet<string>(local.Keys);
        foreach (var k in peer.Keys) keys.Add(k);
        foreach (var ss in (_sync.Hashes ?? new Dictionary<string, string>()).Keys)
        {
            var sep = ss.IndexOf('\u0000');
            if (sep > 0 && ss[..sep] == collection) keys.Add(ss[(sep + 1)..]);
        }
        foreach (var key in keys)
        {
            var hasLocal = local.TryGetValue(key, out var le);
            var hasPeer = peer.TryGetValue(key, out var pe);
            var peerHash = hasPeer ? pe!.Hash : "";
            if (IsDismissed(collection, key, peerHash, hasLocal, hasPeer)) continue;
            var sh = snapHash(collection, key);
            var inSnap = sh.Length > 0;
            if (hasLocal && hasPeer)
            {
                if (le!.Hash == pe!.Hash) continue;
                if (!inSnap) { AddConflict(plan, collection, key, pe.Titre, le.Detail, pe.Detail, "modified", peerRecord: pe.Record, peerHash: pe.Hash); continue; }
                if (le.Hash == sh) plan.Actions.Add(new PlanAction { Collection = collection, Key = key, TakePeer = true, Peer = pe.Record, PeerHash = pe.Hash });
                else if (pe.Hash == sh) { }
                else AddConflict(plan, collection, key, pe.Titre, le.Detail, pe.Detail, "modified", peerRecord: pe.Record, peerHash: pe.Hash);
            }
            else if (hasLocal && !hasPeer)
            {
                if (!inSnap) continue;
                if (le!.Hash == sh) onPeerDeleted(key, le, peerDeletedHint(key));
                else AddConflict(plan, collection, key, le.Titre, le.Detail,
                    "Supprimé sur « " + peerLabel + " »", "deletedPeer", peerHash: "");
            }
            else if (!hasLocal && hasPeer)
            {
                if (!inSnap) plan.Actions.Add(new PlanAction { Collection = collection, Key = key, TakePeer = true, Peer = pe!.Record, PeerHash = pe.Hash });
                else if (pe!.Hash == sh) { }
                else AddConflict(plan, collection, key, pe.Titre, "Supprimé sur cet appareil", pe.Detail, "deletedHere",
                    peerRecord: pe.Record, peerHash: pe.Hash);
            }
        }
    }

    private static void MergeSingle(SyncPlan plan, string collection, string key,
        string localHash, string peerHash, string snapHash,
        string titre, string localDetail, string peerDetail, object? peerRecord)
    {
        if (localHash == peerHash) return;
        if (snapHash.Length == 0) { AddConflict(plan, collection, key, titre, localDetail, peerDetail, "modified", peerRecord: peerRecord, peerHash: peerHash); return; }
        if (localHash == snapHash)
            plan.Actions.Add(new PlanAction { Collection = collection, Key = key, TakePeer = true, Peer = peerRecord, PeerHash = peerHash });
        else if (peerHash == snapHash) { }
        else AddConflict(plan, collection, key, titre, localDetail, peerDetail, "modified", peerRecord: peerRecord, peerHash: peerHash);
    }

    private static void AddConflict(SyncPlan plan, string collection, string key, string titre,
        string local, string distant, string cas, bool dropOnIncoming = false,
        object? peerRecord = null, string peerHash = "")
    {
        plan.Conflicts.Add(new SyncConflict
        {
            Collection = collection, Key = key, Titre = titre,
            DetailLocal = local, DetailDistant = distant, Cas = cas
        });
        // L'action sera tranchée à l'application selon la résolution (défaut : conserver le local).
        plan.Actions.Add(new PlanAction
        {
            Collection = collection, Key = key, TakePeer = false, DropLocal = false,
            DropOnIncoming = dropOnIncoming || cas is "deletedPeer" or "deletedHere",
            Peer = peerRecord, PeerHash = peerHash
        });
    }

    /// <summary>
    /// Un conflit déjà tranché « conserver le local » ne doit pas ressurgir
    /// tant que l'entrant n'a pas changé (convergence des synchronisations).
    /// </summary>
    private bool IsDismissed(string collection, string key, string peerHash, bool hasLocal, bool hasPeer)
    {
        _sync.Dismissed ??= new Dictionary<string, string>();
        var mapKey = collection + "\u0000" + key;
        if (_sync.Dismissed.TryGetValue(mapKey, out var dismissedHash))
        {
            if (dismissedHash == peerHash) return true;
            _sync.Dismissed.Remove(mapKey); // l'entrant a changé : on réexamine
        }
        else if (!hasLocal && !hasPeer)
        {
            _sync.Dismissed.Remove(mapKey); // plus rien des deux côtés : nettoyage
        }
        return false;
    }

    // ----- Application des fusions (privé) -----

    private static void UpsertById<T>(List<T> list, string id, T record) where T : class
    {
        var prop = typeof(T).GetProperty("Id");
        var idx = list.FindIndex(x => (prop?.GetValue(x) as string) == id);
        if (idx < 0) list.Add(record);
        else list[idx] = record;
    }

    private void UpsertUser(UserAccount u)
    {
        u.Champs ??= new Dictionary<string, FieldValue>();
        var key = UserKey(u);
        var idx = _users.FindIndex(x => UserKey(x) == key);
        if (idx < 0) _users.Add(u);
        else
        {
            // Conserver l'Id local si l'entrant n'en a pas (compatibilité anciens paquets).
            if (string.IsNullOrWhiteSpace(u.Id)) u.Id = _users[idx].Id;
            _users[idx] = u;
        }
    }

    private void UpsertDocument(DocumentItem d)
    {
        var idx = _documents.FindIndex(x => x.Id == d.Id);
        if (idx < 0) { _documents.Add(d); return; }
        // Sans les binaires, ne jamais écraser un contenu local existant par du vide.
        if (string.IsNullOrWhiteSpace(d.Data) && !string.IsNullOrWhiteSpace(_documents[idx].Data))
            d.Data = _documents[idx].Data;
        _documents[idx] = d;
    }

    private void UpsertArchive(ArchivedUser x)
    {
        var idx = _archive.FindIndex(a => a.User.Id == x.User.Id);
        if (idx < 0) _archive.Add(x);
        else _archive[idx] = x;
    }

    private void UpsertVote(Vote v)
    {
        v.Options ??= new List<string>();
        v.Bulletins ??= new List<Models.Bulletin>();
        var idx = _votes.FindIndex(x => x.Id == v.Id);
        if (idx < 0) _votes.Add(v);
        else _votes[idx] = v;
    }

    private void UpsertJournalMedia(MediaItem m)
    {
        var idx = _journalMedia.FindIndex(x => x.Id == m.Id);
        if (idx < 0) { _journalMedia.Add(m); return; }
        // Sans les binaires, ne jamais écraser un contenu local existant par du vide.
        if (string.IsNullOrWhiteSpace(m.Data) && !string.IsNullOrWhiteSpace(_journalMedia[idx].Data))
            m.Data = _journalMedia[idx].Data;
        _journalMedia[idx] = m;
    }

    /// <summary>Archive un compte local (propagation d'une suppression distante).</summary>
    private void ArchiveLocalUser(string identifiantKey, string deviceName)
    {
        var idx = _users.FindIndex(x => UserKey(x) == identifiantKey);
        if (idx < 0) return;
        var target = _users[idx];
        _users.RemoveAt(idx);
        if (_archive.Any(a => a.User.Id == target.Id)) return;
        _archive.Insert(0, new ArchivedUser
        {
            User = target,
            DeletedAt = DateTimeOffset.Now.ToString("O"),
            DeletedBy = "Synchronisation (« " + (string.IsNullOrWhiteSpace(deviceName) ? "autre appareil" : deviceName) + " »)"
        });
    }

    /// <summary>Supprime un dossier en conservant son contenu (remonté au parent).</summary>
    private void DeleteFolderKeepContents(string folderId)
    {
        var folder = FindFolder(folderId);
        if (folder == null) return;
        foreach (var doc in _documents.Where(d => d.FolderId == folderId))
            doc.FolderId = folder.ParentId;
        foreach (var sub in _folders.Where(f => f.ParentId == folderId))
            sub.ParentId = folder.ParentId;
        _folders.Remove(folder);
    }

    /// <summary>Recompose l'instantané après export ou import (nouvelle base commune).</summary>
    private void RefreshSyncSnapshot()
    {
        var hashes = new Dictionary<string, string>();
        foreach (var u in _users) hashes["users\u0000" + UserKey(u)] = HashOf(CanonUser(u));
        foreach (var f in _fields) hashes["fields\u0000" + f.Id] = HashOf(Serialize(f));
        foreach (var e in _events) hashes["events\u0000" + e.Id] = HashOf(Serialize(e));
        foreach (var d in _documents) hashes["documents\u0000" + d.Id] = HashOf(CanonDoc(d, true));
        foreach (var f in _folders) hashes["folders\u0000" + f.Id] = HashOf(Serialize(f));
        foreach (var c in _community.Chapitres) hashes["community\u0000" + c.Id] = HashOf(Serialize(c));
        hashes["community\u0000__header"] = HashOf(Serialize(new { _community.Titre, _community.SousTitre }));
        foreach (var a in _archive) hashes["archives\u0000" + a.User.Id] = HashOf(Serialize(a));
        hashes["settings\u0000app"] = HashOf(Serialize(_settings));
        foreach (var p in _permissions) hashes["permissions\u0000" + p.Role] = HashOf(Serialize(NormalizeRoleSet(p)));
        foreach (var m in _messages) hashes["messages\u0000" + m.Id] = HashOf(Serialize(m));
        foreach (var t in _taches) hashes["taches\u0000" + t.Id] = HashOf(Serialize(t));
        foreach (var c in _cotisations) hashes["cotisations\u0000" + c.Id] = HashOf(Serialize(c));
        foreach (var t in _tresorerie) hashes["tresorerie\u0000" + t.Id] = HashOf(Serialize(t));
        foreach (var m in _materiels) hashes["materiels\u0000" + m.Id] = HashOf(Serialize(m));
        foreach (var p in _prets) hashes["prets\u0000" + p.Id] = HashOf(Serialize(p));
        foreach (var r in _reunions) hashes["reunions\u0000" + r.Id] = HashOf(Serialize(r));
        foreach (var s in _situations) hashes["situations\u0000" + s.Id] = HashOf(Serialize(s));
        foreach (var v in _votes) hashes["votes\u0000" + v.Id] = HashOf(Serialize(v));
        foreach (var m in _journalMedia) hashes["journal_media\u0000" + m.Id] = HashOf(CanonMedia(m, true));
        _sync.Hashes = hashes;
        if (string.IsNullOrWhiteSpace(_sync.DeviceId)) _sync.DeviceId = DeviceId;
        SaveSyncSnapshot();
    }

    // ---------- Messagerie interne (boîte locale partagée) ----------

    /// <summary>Canal général visible par tous les comptes.</summary>
    public const string ConversationAnnonces = "annonces";

    public IReadOnlyList<Message> Messages => _messages;

    /// <summary>Identifiant de conversation pour un ensemble de participants (triés, insensibles à la casse).</summary>
    public static string ConversationIdFor(IEnumerable<string> participants)
    {
        var members = (participants ?? Enumerable.Empty<string>())
            .Select(p => (p ?? "").Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return members.Count == 0 ? ConversationAnnonces : "mp:" + string.Join("+", members).ToLowerInvariant();
    }

    public OperationResult SendMessage(List<string> destinataires, string texte)
    {
        var me = GetCurrentUser()?.Identifiant ?? "inconnu";
        var members = (destinataires ?? new List<string>())
            .Select(d => (d ?? "").Trim())
            .Where(d => d.Length > 0 && !d.Equals(me, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Chaque destinataire doit être un compte existant (ou aucun = canal Annonces).
        // Un membre archivé reste accepté dans une conversation qui existe déjà,
        // pour que les autres participants puissent continuer à y répondre.
        var inconnus = members.Where(d => FindUserByIdentifier(d) == null).ToList();
        if (inconnus.Count > 0)
        {
            var conv = ConversationIdFor(members.Append(me));
            var existe = _messages.Any(m => m.ConversationId == conv);
            if (!existe || inconnus.Count == members.Count)
                return OperationResult.Fail(L.F("Destinataire introuvable : « {0} ». Ce membre a peut-être été supprimé.", inconnus[0]));
        }
        if (string.IsNullOrWhiteSpace(texte))
            return OperationResult.Fail(L.T("Le message est vide."));
        members.Add(me);
        var message = new Message
        {
            Id = Guid.NewGuid().ToString("N"),
            ConversationId = members.Count <= 1 ? ConversationAnnonces : ConversationIdFor(members),
            Expediteur = me,
            Destinataires = members.Where(m => !m.Equals(me, StringComparison.OrdinalIgnoreCase)).ToList(),
            Texte = texte.Trim(),
            Date = DateTimeOffset.Now.ToString("O"),
            LuPar = new List<string> { me }
        };
        _messages.Add(message);
        SaveMessages();
        LogActivity(Models.ActivityTypes.MessageSend,
            members.Count <= 1 ? "Annonce : « " + Truncate(texte.Trim(), 60) + " »" : "Message à " + message.Destinataires.Count + " destinataire(s)");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Conversations visibles par l'utilisateur (ordonnées par dernier message).</summary>
    public IReadOnlyList<(string ConversationId, Message Dernier, int NonLus)> GetConversations(string identifiant)
    {
        var me = (identifiant ?? "").Trim();
        return _messages
            .Where(m => m.ConversationId == ConversationAnnonces
                || m.Expediteur.Equals(me, StringComparison.OrdinalIgnoreCase)
                || m.Destinataires.Any(d => d.Equals(me, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(m => m.ConversationId)
            .Select(g => (g.Key, Dernier: g.OrderByDescending(m => m.Date).First(),
                NonLus: g.Count(m => !m.Supprime && !m.LuPar.Any(l => l.Equals(me, StringComparison.OrdinalIgnoreCase)))))
            .OrderByDescending(x => x.Dernier.Date)
            .ToList();
    }

    public IReadOnlyList<Message> GetMessages(string conversationId)
        => _messages.Where(m => m.ConversationId == conversationId).OrderBy(m => m.Date).ToList();

    public int UnreadCount(string identifiant)
    {
        var me = (identifiant ?? "").Trim();
        return _messages.Count(m =>
            !m.Supprime
            && (m.ConversationId == ConversationAnnonces
                || m.Expediteur.Equals(me, StringComparison.OrdinalIgnoreCase)
                || m.Destinataires.Any(d => d.Equals(me, StringComparison.OrdinalIgnoreCase)))
            && !m.LuPar.Any(l => l.Equals(me, StringComparison.OrdinalIgnoreCase)));
    }

    public void MarkConversationRead(string conversationId, string identifiant)
    {
        var me = (identifiant ?? "").Trim();
        if (me.Length == 0) return;
        var changed = false;
        foreach (var m in _messages.Where(m => m.ConversationId == conversationId))
        {
            if (!m.LuPar.Any(l => l.Equals(me, StringComparison.OrdinalIgnoreCase)))
            {
                m.LuPar.Add(me);
                changed = true;
            }
        }
        if (changed) { SaveMessages(); DataChanged?.Invoke(); }
    }

    /// <summary>Supprime un message (expéditeur ou administrateur). Le contenu est masqué
    /// mais un petit journal reste visible dans la conversation.</summary>
    public OperationResult DeleteMessage(string messageId)
    {
        var idx = _messages.FindIndex(m => m.Id == messageId);
        if (idx < 0) return OperationResult.Fail(L.T("Message introuvable."));
        var target = _messages[idx];
        if (target.Supprime) return OperationResult.Fail(L.T("Message déjà supprimé."));
        var me = GetCurrentUser();
        var own = me != null && target.Expediteur.Equals(me.Identifiant, StringComparison.OrdinalIgnoreCase);
        if (!own && !IsAdmin)
            return OperationResult.Fail(L.T("Seul l'expéditeur ou un administrateur peut supprimer ce message."));
        var author = me?.Identifiant ?? "inconnu";
        target.Supprime = true;
        target.SupprimePar = author;
        target.SupprimeLe = DateTimeOffset.Now.ToString("O");
        if (!target.LuPar.Any(l => l.Equals(author, StringComparison.OrdinalIgnoreCase)))
            target.LuPar.Add(author);
        SaveMessages();
        LogActivity(Models.ActivityTypes.MessageDelete,
            "Message de « " + target.Expediteur + " » supprimé par « " + author + " » : « " + Truncate(target.Texte, 80) + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Délai pendant lequel l'expéditeur peut modifier son message.</summary>
    public static readonly TimeSpan MessageEditWindow = TimeSpan.FromMinutes(5);

    /// <summary>Indique si le message peut encore être modifié (expéditeur, 5 min, non supprimé).</summary>
    public bool CanEditMessage(string messageId)
    {
        var m = _messages.FirstOrDefault(x => x.Id == messageId);
        if (m == null || m.Supprime) return false;
        var me = GetCurrentUser()?.Identifiant;
        if (string.IsNullOrEmpty(me) || !m.Expediteur.Equals(me, StringComparison.OrdinalIgnoreCase)) return false;
        if (!DateTimeOffset.TryParse(m.Date, out var sent)) return false;
        return DateTimeOffset.Now - sent <= MessageEditWindow;
    }

    /// <summary>Modifie un message déjà envoyé (expéditeur uniquement, dans les 5 minutes).</summary>
    public OperationResult UpdateMessage(string messageId, string nouveauTexte)
    {
        var idx = _messages.FindIndex(m => m.Id == messageId);
        if (idx < 0) return OperationResult.Fail(L.T("Message introuvable."));
        var target = _messages[idx];
        if (target.Supprime) return OperationResult.Fail(L.T("Message supprimé."));
        var me = GetCurrentUser()?.Identifiant ?? "";
        if (string.IsNullOrEmpty(me) || !target.Expediteur.Equals(me, StringComparison.OrdinalIgnoreCase))
            return OperationResult.Fail(L.T("Seul l'expéditeur peut modifier ce message."));
        if (!DateTimeOffset.TryParse(target.Date, out var sent))
            return OperationResult.Fail(L.T("Modification impossible."));
        if (DateTimeOffset.Now - sent > MessageEditWindow)
            return OperationResult.Fail(L.T("Le délai de modification (5 min) est dépassé."));
        if (string.IsNullOrWhiteSpace(nouveauTexte))
            return OperationResult.Fail(L.T("Le message est vide."));
        target.Texte = nouveauTexte.Trim();
        target.DateModification = DateTimeOffset.Now.ToString("O");
        if (!target.LuPar.Any(l => l.Equals(me, StringComparison.OrdinalIgnoreCase)))
            target.LuPar.Add(me);
        SaveMessages();
        LogActivity(Models.ActivityTypes.MessageUpdate, "Message modifié : « " + Truncate(target.Texte, 80) + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

    // ---------- Tâches et suivi ----------

    public IReadOnlyList<Tache> Taches => _taches;

    public Tache? FindTache(string id) => _taches.FirstOrDefault(t => t.Id == id);

    /// <summary>Tâches visibles : assignées à l'utilisateur, créées par lui, ou toutes (manager).</summary>
    public IReadOnlyList<Tache> GetTachesForUser(string identifiant, bool all)
    {
        var me = (identifiant ?? "").Trim();
        var query = _taches.AsEnumerable();
        if (!all)
            query = query.Where(t => t.Assignes.Any(a => a.Equals(me, StringComparison.OrdinalIgnoreCase))
                || t.CreatedBy.Equals(me, StringComparison.OrdinalIgnoreCase));
        return query.OrderBy(t => t.Statut == Models.TacheStatuts.Terminee ? 1 : 0)
            .ThenBy(t => string.IsNullOrWhiteSpace(t.Echeance) ? "9999" : t.Echeance)
            .ThenByDescending(t => t.DateCreation)
            .ToList();
    }

    public OperationResult AddTache(Tache draft)
    {
        draft.Titre = (draft.Titre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Titre))
            return OperationResult.Fail(L.T("Le titre de la tâche est obligatoire."));
        draft.Description = (draft.Description ?? "").Trim();
        draft.Assignes = (draft.Assignes ?? new List<string>())
            .Select(a => (a ?? "").Trim()).Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var a in draft.Assignes)
        {
            if (FindUserByIdentifier(a) == null)
                return OperationResult.Fail(L.F("Assigné introuvable : « {0} ».", a));
        }
        if (!string.IsNullOrWhiteSpace(draft.Echeance) && !TryParseYmd(draft.Echeance, out _))
            return OperationResult.Fail(L.T("L'échéance doit être au format JJ/MM/AAAA."));
        draft.Id = Guid.NewGuid().ToString("N");
        draft.Statut = Models.TacheStatuts.AFaire;
        draft.DateCreation = DateTimeOffset.Now.ToString("O");
        draft.TermineLe = null;
        draft.CreatedBy = GetCurrentUser()?.Identifiant ?? "inconnu";
        _taches.Add(draft);
        SaveTaches();
        LogActivity(Models.ActivityTypes.TacheCreate, "Création de la tâche « " + draft.Titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult UpdateTache(string id, Tache draft)
    {
        var existing = FindTache(id);
        if (existing == null) return OperationResult.Fail(L.T("Tâche introuvable."));
        draft.Titre = (draft.Titre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Titre))
            return OperationResult.Fail(L.T("Le titre de la tâche est obligatoire."));
        if (!string.IsNullOrWhiteSpace(draft.Echeance) && !TryParseYmd(draft.Echeance, out _))
            return OperationResult.Fail(L.T("L'échéance doit être au format JJ/MM/AAAA."));
        draft.Assignes = (draft.Assignes ?? new List<string>())
            .Select(a => (a ?? "").Trim()).Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var a in draft.Assignes)
        {
            // Un membre archivé déjà assigné reste accepté : sinon la tâche ne pourrait plus être modifiée.
            if (FindUserByIdentifier(a) == null && !(existing.Assignes ?? new List<string>()).Contains(a, StringComparer.OrdinalIgnoreCase))
                return OperationResult.Fail(L.F("Assigné introuvable : « {0} ».", a));
        }
        existing.Titre = draft.Titre;
        existing.Description = (draft.Description ?? "").Trim();
        existing.Assignes = draft.Assignes;
        existing.Priorite = draft.Priorite;
        existing.Echeance = draft.Echeance ?? "";
        SaveTaches();
        LogActivity(Models.ActivityTypes.TacheUpdate, "Modification de la tâche « " + existing.Titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Fait avancer le suivi : À faire → En cours → Terminée.</summary>
    public OperationResult SetTacheStatut(string id, string statut)
    {
        var existing = FindTache(id);
        if (existing == null) return OperationResult.Fail(L.T("Tâche introuvable."));
        if (statut != Models.TacheStatuts.AFaire && statut != Models.TacheStatuts.EnCours && statut != Models.TacheStatuts.Terminee)
            return OperationResult.Fail(L.T("Statut inconnu."));
        existing.Statut = statut;
        existing.TermineLe = statut == Models.TacheStatuts.Terminee ? DateTimeOffset.Now.ToString("O") : null;
        SaveTaches();
        LogActivity(Models.ActivityTypes.TacheUpdate,
            "Tâche « " + existing.Titre + " » → " + Models.TacheStatuts.Label(statut));
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult DeleteTache(string id)
    {
        var idx = _taches.FindIndex(t => t.Id == id);
        if (idx < 0) return OperationResult.Fail(L.T("Tâche introuvable."));
        var titre = _taches[idx].Titre;
        _taches.RemoveAt(idx);
        SaveTaches();
        LogActivity(Models.ActivityTypes.TacheDelete, "Suppression de la tâche « " + titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public static bool TryParseYmd(string? value, out DateTime date)
    {
        date = DateTime.Today;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var s = value.Trim();
        // Stockage yyyy-MM-dd ou saisie JJ/MM/AAAA.
        return DateTime.TryParseExact(s, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out date)
            || DateTime.TryParseExact(s, new[] { "dd/MM/yyyy", "d/M/yyyy" },
                System.Globalization.CultureInfo.GetCultureInfo("fr-FR"),
                System.Globalization.DateTimeStyles.None, out date);
    }

    /// <summary>Convertit une saisie JJ/MM/AAAA en yyyy-MM-dd ("" si vide/invalide).</summary>
    public static string ToYmd(string? value)
        => TryParseYmd(value, out var d) ? d.ToString("yyyy-MM-dd") : "";

    /// <summary>Normalise une naissance vers yyyy-MM-dd (accepte aussi JJ/MM/AAAA).</summary>
    private static string NormalizeYmd(string? value)
        => TryParseYmd(value, out var d) ? d.ToString("yyyy-MM-dd") : "";

    // ---------- Anniversaires (utilisateurs ↔ calendrier) ----------

    /// <summary>Événements d'anniversaire liés à un utilisateur (assigné ou nommé).</summary>
    public IReadOnlyList<CalendarEvent> GetBirthdayEventsForUser(string identifiant, string nomComplet)
    {
        var id = (identifiant ?? "").Trim();
        var nom = (nomComplet ?? "").Trim();
        return _events
            .Where(e => e.Type == Models.CalendarEventTypes.Anniversaire
                && ((e.PersonnesConcernees ?? new List<string>()).Any(p => p.Equals(id, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(e.PersonneConcernee) && e.PersonneConcernee.Equals(id, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(nom) && (e.Titre ?? "").Contains(nom, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(e => e.Date)
            .ToList();
    }

    /// <summary>Crée l'événement d'anniversaire annuel d'un utilisateur (une seule fois).</summary>
    public OperationResult EnsureBirthdayEvent(string userId)
    {
        var user = FindUser(userId);
        if (user == null) return OperationResult.Fail(L.T("Utilisateur introuvable."));
        if (!TryParseYmd(user.DateNaissance, out _))
            return OperationResult.Fail(L.T("Renseignez d'abord la date de naissance (JJ/MM/AAAA)."));
        if (GetBirthdayEventsForUser(user.Identifiant, user.NomComplet).Count > 0)
            return OperationResult.Fail(L.T("Un événement d'anniversaire existe déjà pour ce membre."));
        return AddEvent(new CalendarEvent
        {
            Type = Models.CalendarEventTypes.Anniversaire,
            Titre = "Anniversaire de " + user.NomComplet,
            Date = user.DateNaissance,
            TouteLaJournee = true,
            Repetition = Models.CalendarEventTypes.RepetitionAnnuelle,
            PersonnesConcernees = new List<string> { user.Identifiant }
        });
    }

    /// <summary>
    /// Synchronise automatiquement un utilisateur vers le calendrier :
    /// anniversaire (DateNaissance) + chaque champ personnalisé de type Date.
    /// Création si absent, mise à jour (titre/date/personne) si le membre change.
    /// Retourne true si un événement a été créé ou mis à jour.
    /// </summary>
    public bool SyncUserDateEvents(UserAccount user)
    {
        if (user == null) return false;
        var changed = false;
        var ident = (user.Identifiant ?? "").Trim();
        var nomComplet = user.NomComplet?.Trim() ?? "";

        // 1) Anniversaire issu de la fiche membre (pas pour un membre décédé).
        if (!user.EstDecede && TryParseYmd(user.DateNaissance, out _))
        {
            var wantDate = ToYmd(user.DateNaissance);
            var wantTitle = "Anniversaire de " + nomComplet;
            var existing = GetBirthdayEventsForUser(ident, nomComplet);
            if (existing.Count == 0)
            {
                _events.Add(new CalendarEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = Models.CalendarEventTypes.Anniversaire,
                    Titre = wantTitle,
                    Date = wantDate,
                    TouteLaJournee = true,
                    Repetition = Models.CalendarEventTypes.RepetitionAnnuelle,
                    PersonnesConcernees = string.IsNullOrWhiteSpace(ident) ? new List<string>() : new List<string> { ident },
                    DateCreation = DateTimeOffset.Now.ToString("O"),
                    CreatedBy = "auto"
                });
                changed = true;
            }
            else
            {
                foreach (var e in existing)
                {
                    var needSave = false;
                    if (e.Titre != wantTitle) { e.Titre = wantTitle; needSave = true; }
                    if (e.Date != wantDate) { e.Date = wantDate; needSave = true; }
                    if (e.Repetition != Models.CalendarEventTypes.RepetitionAnnuelle) { e.Repetition = Models.CalendarEventTypes.RepetitionAnnuelle; needSave = true; }
                    if (!string.IsNullOrWhiteSpace(ident) && !(e.PersonnesConcernees ?? new List<string>()).Any(p => p.Equals(ident, StringComparison.OrdinalIgnoreCase)))
                    {
                        e.PersonnesConcernees ??= new List<string>();
                        e.PersonnesConcernees.Add(ident);
                        needSave = true;
                    }
                    if (needSave) changed = true;
                }
            }
        }

        // 2) Autres dates de la fiche : chaque champ personnalisé de type Date
        // (ex. date d'adhésion, échéance, rappel personnel) devient un événement.
        try
        {
            var dateFields = _fields.Where(f => f != null && f.Actif && f.Type == Models.FieldType.Date).ToList();
            foreach (var f in dateFields)
            {
                if (f == null || string.IsNullOrWhiteSpace(f.Id)) continue;
                if (user.Champs == null || !user.Champs.TryGetValue(f.Id, out var val)) continue;
                var raw = (val?.Texte ?? "").Trim();
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (!TryParseYmd(raw, out _)) continue;
                var ymd = ToYmd(raw);
                if (string.IsNullOrWhiteSpace(ymd)) continue;
                var title = (f.Label ?? "Date").Trim() + " de " + nomComplet;
                if (string.IsNullOrWhiteSpace(title)) continue;
                var exists = _events.Any(e =>
                    (e.Titre ?? "").Equals(title, StringComparison.OrdinalIgnoreCase)
                    && ((e.PersonnesConcernees ?? new List<string>()).Any(p => p.Equals(ident, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(nomComplet) && (e.Titre ?? "").Contains(nomComplet, StringComparison.OrdinalIgnoreCase))));
                if (!exists)
                {
                    _events.Add(new CalendarEvent
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Type = Models.CalendarEventTypes.Evenement,
                        Titre = title,
                        Date = ymd,
                        TouteLaJournee = true,
                        Repetition = Models.CalendarEventTypes.RepetitionAucune,
                        PersonnesConcernees = string.IsNullOrWhiteSpace(ident) ? new List<string>() : new List<string> { ident },
                        Notes = "[auto] " + (f.Label ?? ""),
                        DateCreation = DateTimeOffset.Now.ToString("O"),
                        CreatedBy = "auto"
                    });
                    changed = true;
                }
            }
        }
        catch { }

        if (changed)
        {
            _events.Sort((a, b) => SortKey(a).CompareTo(SortKey(b)));
            SaveEvents();
        }
        return changed;
    }

    /// <summary>Synchronise tous les utilisateurs vers le calendrier (appel au démarrage + après import).</summary>
    public int SyncAllUserDateEvents()
    {
        var count = 0;
        try
        {
            foreach (var u in _users.ToList())
            {
                try { if (SyncUserDateEventsSilent(u)) count++; }
                catch { }
            }
            if (count > 0)
            {
                _events.Sort((a, b) => SortKey(a).CompareTo(SortKey(b)));
                SaveEvents();
                DataChanged?.Invoke();
            }
        }
        catch { }
        return count;
    }

    private bool SyncUserDateEventsSilent(UserAccount user)
    {
        if (user == null) return false;
        var before = _events.Count;
        var snapshot = _events.Count;
        // Réutilise la logique sans sauvegarder à chaque membre (sauvegarde groupée par l'appelant).
        var ident = (user.Identifiant ?? "").Trim();
        var nomComplet = user.NomComplet?.Trim() ?? "";
        var changed = false;

        if (!user.EstDecede && TryParseYmd(user.DateNaissance, out _))
        {
            var wantDate = ToYmd(user.DateNaissance);
            var wantTitle = "Anniversaire de " + nomComplet;
            var existing = _events
                .Where(e => e.Type == Models.CalendarEventTypes.Anniversaire
                    && ((e.PersonnesConcernees ?? new List<string>()).Any(p => p.Equals(ident, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(nomComplet) && (e.Titre ?? "").Contains(nomComplet, StringComparison.OrdinalIgnoreCase))))
                .ToList();
            if (existing.Count == 0)
            {
                _events.Add(new CalendarEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = Models.CalendarEventTypes.Anniversaire,
                    Titre = wantTitle,
                    Date = wantDate,
                    TouteLaJournee = true,
                    Repetition = Models.CalendarEventTypes.RepetitionAnnuelle,
                    PersonnesConcernees = string.IsNullOrWhiteSpace(ident) ? new List<string>() : new List<string> { ident },
                    DateCreation = DateTimeOffset.Now.ToString("O"),
                    CreatedBy = "auto"
                });
                changed = true;
            }
            else
            {
                foreach (var e in existing)
                {
                    if (e.Titre != wantTitle) { e.Titre = wantTitle; changed = true; }
                    if (e.Date != wantDate) { e.Date = wantDate; changed = true; }
                    if (e.Repetition != Models.CalendarEventTypes.RepetitionAnnuelle) { e.Repetition = Models.CalendarEventTypes.RepetitionAnnuelle; changed = true; }
                    if (!string.IsNullOrWhiteSpace(ident) && !(e.PersonnesConcernees ?? new List<string>()).Any(p => p.Equals(ident, StringComparison.OrdinalIgnoreCase)))
                    {
                        e.PersonnesConcernees ??= new List<string>();
                        e.PersonnesConcernees.Add(ident);
                        changed = true;
                    }
                }
            }
        }

        try
        {
            var dateFields = _fields.Where(f => f != null && f.Actif && f.Type == Models.FieldType.Date).ToList();
            foreach (var f in dateFields)
            {
                if (user.Champs == null || !user.Champs.TryGetValue(f.Id, out var val)) continue;
                var raw = (val?.Texte ?? "").Trim();
                if (string.IsNullOrWhiteSpace(raw) || !TryParseYmd(raw, out _)) continue;
                var ymd = ToYmd(raw);
                var title = (f.Label ?? "Date").Trim() + " de " + nomComplet;
                var exists = _events.Any(e =>
                    (e.Titre ?? "").Equals(title, StringComparison.OrdinalIgnoreCase)
                    && ((e.PersonnesConcernees ?? new List<string>()).Any(p => p.Equals(ident, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(nomComplet) && (e.Titre ?? "").Contains(nomComplet, StringComparison.OrdinalIgnoreCase))));
                if (!exists)
                {
                    _events.Add(new CalendarEvent
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Type = Models.CalendarEventTypes.Evenement,
                        Titre = title,
                        Date = ymd,
                        TouteLaJournee = true,
                        Repetition = Models.CalendarEventTypes.RepetitionAucune,
                        PersonnesConcernees = string.IsNullOrWhiteSpace(ident) ? new List<string>() : new List<string> { ident },
                        Notes = "[auto] " + (f.Label ?? ""),
                        DateCreation = DateTimeOffset.Now.ToString("O"),
                        CreatedBy = "auto"
                    });
                    changed = true;
                }
            }
        }
        catch { }
        return changed || _events.Count != snapshot;
    }

    // ---------- Finances (cotisations, trésorerie — montants en Ariary) ----------

    public IReadOnlyList<Cotisation> Cotisations => _cotisations;

    public IReadOnlyList<TransactionTresorerie> Tresorerie => _tresorerie;

    public static string FormatMontant(double montant)
        => montant.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " Ar";

    /// <summary>
    /// Enregistre une cotisation. Si <paramref name="ajouterTresorerie"/> et que la
    /// cotisation est payée, l'entrée correspondante est aussi ajoutée en trésorerie.
    /// </summary>
    public OperationResult AddCotisation(Cotisation draft, bool ajouterTresorerie = false)
    {
        draft.Motif = (draft.Motif ?? "").Trim();
        if (FindUserByIdentifier(draft.MembreIdentifiant) == null)
            return OperationResult.Fail(L.F("Membre introuvable : « {0} ».", draft.MembreIdentifiant));
        if (draft.Montant <= 0)
            return OperationResult.Fail(L.T("Le montant doit être supérieur à 0."));
        if (string.IsNullOrWhiteSpace(draft.Periode)) draft.Periode = DateTime.Today.Year.ToString();
        else draft.Periode = draft.Periode.Trim();
        if (string.IsNullOrWhiteSpace(draft.Date)) draft.Date = DateTime.Today.ToString("yyyy-MM-dd");
        else if (!TryParseYmd(draft.Date, out _))
            return OperationResult.Fail(L.T("La date doit être au format JJ/MM/AAAA."));
        else draft.Date = ToYmd(draft.Date);
        if (draft.Statut != CotisationStatuts.EnAttente) draft.Statut = CotisationStatuts.Payee;
        draft.MembreIdentifiant = draft.MembreIdentifiant.Trim();
        // Un paiement règle d'abord l'impayé correspondant (même membre, même période,
        // même motif ou motif laissé vide) au lieu de laisser l'impayé ouvert à côté.
        if (draft.Statut == CotisationStatuts.Payee)
        {
            var ident = draft.MembreIdentifiant;
            var periode = draft.Periode;
            var motif = draft.Motif;
            var attente = _cotisations.FirstOrDefault(c => c.Statut == CotisationStatuts.EnAttente
                && string.Equals((c.MembreIdentifiant ?? "").Trim(), ident, StringComparison.OrdinalIgnoreCase)
                && c.Periode == periode
                && (motif.Length == 0 || string.Equals((c.Motif ?? "").Trim(), motif, StringComparison.OrdinalIgnoreCase)));
            if (attente != null)
            {
                attente.Montant = draft.Montant;
                if (motif.Length > 0) attente.Motif = motif;
                return EncaisserCotisation(attente.Id, draft.Date, ajouterTresorerie);
            }
        }
        draft.Id = Guid.NewGuid().ToString("N");
        draft.CreatedBy = GetCurrentUser()?.Identifiant ?? "inconnu";
        _cotisations.Add(draft);
        SaveCotisations();
        if (ajouterTresorerie && draft.Statut == CotisationStatuts.Payee) AddEntreeCotisation(draft);
        LogActivity(Models.ActivityTypes.FinanceCotisation,
            "Cotisation " + FormatMontant(draft.Montant) + " de " + draft.MembreIdentifiant + " (" + draft.Periode + ")");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>
    /// Configure des impayés : une cotisation « en attente » pour chaque membre choisi.
    /// Un membre qui a déjà une cotisation (payée ou non) de même motif sur la même
    /// période est ignoré, pour ne jamais créer de doublon.
    /// </summary>
    public (OperationResult Result, int Ajoutes, int Ignores) AddCotisationsEnAttente(
        IEnumerable<string> identifiants, double montant, string? motif, string? periode)
    {
        if (montant <= 0) return (OperationResult.Fail(L.T("Le montant doit être supérieur à 0.")), 0, 0);
        var ids = (identifiants ?? Enumerable.Empty<string>())
            .Select(i => (i ?? "").Trim()).Where(i => i.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) return (OperationResult.Fail(L.T("Choisissez au moins un membre.")), 0, 0);
        motif = string.IsNullOrWhiteSpace(motif) ? "Cotisation" : motif.Trim();
        periode = string.IsNullOrWhiteSpace(periode) ? DateTime.Today.Year.ToString() : periode.Trim();
        var auteur = GetCurrentUser()?.Identifiant ?? "inconnu";
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        int ajoutes = 0, ignores = 0;
        foreach (var id in ids)
        {
            var user = FindUserByIdentifier(id);
            if (user == null) { ignores++; continue; }
            var existe = _cotisations.Any(c =>
                string.Equals((c.MembreIdentifiant ?? "").Trim(), user.Identifiant, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.Periode, periode, StringComparison.OrdinalIgnoreCase)
                && string.Equals((c.Motif ?? "").Trim(), motif, StringComparison.OrdinalIgnoreCase));
            if (existe) { ignores++; continue; }
            _cotisations.Add(new Cotisation
            {
                Id = Guid.NewGuid().ToString("N"), MembreIdentifiant = user.Identifiant, Montant = montant,
                Motif = motif, Periode = periode, Date = today, Statut = CotisationStatuts.EnAttente, CreatedBy = auteur
            });
            ajoutes++;
        }
        if (ajoutes > 0)
        {
            SaveCotisations();
            LogActivity(Models.ActivityTypes.FinanceCotisation,
                ajoutes + " impayé(s) de " + FormatMontant(montant) + " configuré(s) : " + motif + " (" + periode + ")");
            DataChanged?.Invoke();
        }
        return (OperationResult.Ok(), ajoutes, ignores);
    }

    /// <summary>Encaisse une cotisation en attente (date de paiement, entrée en trésorerie en option).</summary>
    public OperationResult EncaisserCotisation(string id, string? date, bool ajouterTresorerie)
    {
        var cot = _cotisations.FirstOrDefault(c => c.Id == id);
        if (cot == null) return OperationResult.Fail(L.T("Cotisation introuvable."));
        if (cot.Statut == CotisationStatuts.Payee) return OperationResult.Fail(L.T("Cette cotisation est déjà payée."));
        if (string.IsNullOrWhiteSpace(date)) date = DateTime.Today.ToString("yyyy-MM-dd");
        else if (!TryParseYmd(date, out _)) return OperationResult.Fail(L.T("La date doit être au format JJ/MM/AAAA."));
        cot.Date = ToYmd(date);
        cot.Statut = CotisationStatuts.Payee;
        SaveCotisations();
        if (ajouterTresorerie) AddEntreeCotisation(cot);
        LogActivity(Models.ActivityTypes.FinanceCotisation,
            "Cotisation encaissée " + FormatMontant(cot.Montant) + " de " + cot.MembreIdentifiant + " (" + cot.Periode + ")");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Modifie le montant, le motif, la période, la date ou le statut d'une cotisation.</summary>
    public OperationResult UpdateCotisation(Cotisation changes)
    {
        var cot = _cotisations.FirstOrDefault(c => c.Id == changes.Id);
        if (cot == null) return OperationResult.Fail(L.T("Cotisation introuvable."));
        if (changes.Montant <= 0) return OperationResult.Fail(L.T("Le montant doit être supérieur à 0."));
        var date = changes.Date;
        if (string.IsNullOrWhiteSpace(date)) date = cot.Date;
        else if (!TryParseYmd(date, out _)) return OperationResult.Fail(L.T("La date doit être au format JJ/MM/AAAA."));
        cot.Montant = changes.Montant;
        cot.Motif = (changes.Motif ?? "").Trim();
        cot.Periode = string.IsNullOrWhiteSpace(changes.Periode) ? cot.Periode : changes.Periode.Trim();
        cot.Date = ToYmd(date);
        cot.Statut = changes.Statut == CotisationStatuts.EnAttente ? CotisationStatuts.EnAttente : CotisationStatuts.Payee;
        SaveCotisations();
        LogActivity(Models.ActivityTypes.FinanceCotisation,
            "Modification d'une cotisation de " + cot.MembreIdentifiant + " (" + cot.Periode + ")");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    private void AddEntreeCotisation(Cotisation cot)
    {
        var nom = FindUserByIdentifier(cot.MembreIdentifiant)?.NomComplet ?? cot.MembreIdentifiant;
        _tresorerie.Add(new TransactionTresorerie
        {
            Id = Guid.NewGuid().ToString("N"), Type = TresorerieTypes.Entree, Montant = cot.Montant,
            Motif = (string.IsNullOrWhiteSpace(cot.Motif) ? "Cotisation" : cot.Motif) + " – " + nom + " (" + cot.Periode + ")",
            Date = cot.Date, CreatedBy = GetCurrentUser()?.Identifiant ?? "inconnu"
        });
        SaveTresorerie();
    }

    /// <summary>Cotisations en attente de paiement sur la période (toutes périodes si null).</summary>
    public IReadOnlyList<Cotisation> CotisationsEnAttente(string? periode = null)
        => _cotisations
            .Where(c => c.Statut == CotisationStatuts.EnAttente && (periode == null || c.Periode == periode))
            .ToList();

    public OperationResult DeleteCotisation(string id)
    {
        var idx = _cotisations.FindIndex(c => c.Id == id);
        if (idx < 0) return OperationResult.Fail(L.T("Cotisation introuvable."));
        _cotisations.RemoveAt(idx);
        SaveCotisations();
        LogActivity(Models.ActivityTypes.FinanceCotisation, "Suppression d'une cotisation");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public double TotalCotisations(string? periode = null)
        => _cotisations
            .Where(c => c.Statut == CotisationStatuts.Payee && (periode == null || c.Periode == periode))
            .Sum(c => c.Montant);

    /// <summary>
    /// Membres ayant au moins une cotisation « en attente » sur la période (année en
    /// cours par défaut). Les impayés sont configurés par le trésorier, membre par
    /// membre : un membre sans cotisation attendue n'est jamais compté comme impayé.
    /// </summary>
    public IReadOnlyList<UserAccount> ImpayesCotisations(string? periode = null)
    {
        periode ??= DateTime.Today.Year.ToString();
        var enAttente = new HashSet<string>(_cotisations
            .Where(c => c.Periode == periode && c.Statut == CotisationStatuts.EnAttente)
            .Select(c => (c.MembreIdentifiant ?? "").Trim().ToLowerInvariant()));
        if (enAttente.Count == 0) return Array.Empty<UserAccount>();
        return _users
            .Where(u => !u.EstDecede && enAttente.Contains((u.Identifiant ?? "").Trim().ToLowerInvariant()))
            .OrderBy(u => u.Nom).ThenBy(u => u.Prenom)
            .ToList();
    }

    public OperationResult AddTransaction(TransactionTresorerie draft)
    {
        draft.Motif = (draft.Motif ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Motif))
            return OperationResult.Fail(L.T("Le motif est obligatoire."));
        if (draft.Montant <= 0)
            return OperationResult.Fail(L.T("Le montant doit être supérieur à 0."));
        if (draft.Type != TresorerieTypes.Sortie) draft.Type = TresorerieTypes.Entree;
        if (string.IsNullOrWhiteSpace(draft.Date)) draft.Date = DateTime.Today.ToString("yyyy-MM-dd");
        else if (!TryParseYmd(draft.Date, out _))
            return OperationResult.Fail(L.T("La date doit être au format JJ/MM/AAAA."));
        else draft.Date = ToYmd(draft.Date);
        draft.Id = Guid.NewGuid().ToString("N");
        draft.CreatedBy = GetCurrentUser()?.Identifiant ?? "inconnu";
        _tresorerie.Add(draft);
        SaveTresorerie();
        LogActivity(Models.ActivityTypes.FinanceTresorerie,
            (draft.Type == TresorerieTypes.Sortie ? "Sortie " : "Entrée ") + FormatMontant(draft.Montant) + " : " + draft.Motif);
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult DeleteTransaction(string id)
    {
        var idx = _tresorerie.FindIndex(t => t.Id == id);
        if (idx < 0) return OperationResult.Fail(L.T("Mouvement introuvable."));
        _tresorerie.RemoveAt(idx);
        SaveTresorerie();
        LogActivity(Models.ActivityTypes.FinanceTresorerie, "Suppression d'un mouvement de trésorerie");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public double SoldeTresorerie()
        => _tresorerie.Where(t => t.Type != TresorerieTypes.Sortie).Sum(t => t.Montant)
            - _tresorerie.Where(t => t.Type == TresorerieTypes.Sortie).Sum(t => t.Montant);

    // ---------- Matériel et prêts ----------

    public IReadOnlyList<Materiel> Materiels => _materiels;

    public IReadOnlyList<PretMateriel> Prets => _prets;

    public Materiel? FindMateriel(string id) => _materiels.FirstOrDefault(m => m.Id == id);

    /// <summary>Quantité disponible = total − prêts actifs.</summary>
    public int DisponibleMateriel(string materielId)
    {
        var mat = FindMateriel(materielId);
        if (mat == null) return 0;
        var enPret = _prets
            .Where(p => p.MaterielId == materielId && string.IsNullOrWhiteSpace(p.DateRetour))
            .Sum(p => p.Quantite);
        return Math.Max(0, mat.QuantiteTotale - enPret);
    }

    public IReadOnlyList<PretMateriel> PretsActifs(string materielId)
        => _prets.Where(p => p.MaterielId == materielId && string.IsNullOrWhiteSpace(p.DateRetour))
            .OrderByDescending(p => p.DatePret).ToList();

    public OperationResult AddMateriel(Materiel draft)
    {
        draft.Nom = (draft.Nom ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Nom))
            return OperationResult.Fail(L.T("Le nom du matériel est obligatoire."));
        draft.Description = (draft.Description ?? "").Trim();
        draft.Categorie = (draft.Categorie ?? "").Trim();
        draft.QuantiteTotale = Math.Max(1, draft.QuantiteTotale);
        if (!ListesChoix.Contient(ListesChoix.EtatsMateriel, draft.Etat, _settings.Listes)) draft.Etat = MaterielEtats.Bon;
        draft.Responsable = (draft.Responsable ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(draft.Responsable) && FindUserByIdentifier(draft.Responsable) == null)
            return OperationResult.Fail(L.F("Responsable introuvable : « {0} ».", draft.Responsable));
        draft.Id = Guid.NewGuid().ToString("N");
        _materiels.Add(draft);
        SaveMateriels();
        LogActivity(Models.ActivityTypes.MaterielChange, "Ajout du matériel « " + draft.Nom + " » (×" + draft.QuantiteTotale + ")");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult UpdateMateriel(string id, Materiel draft)
    {
        var existing = FindMateriel(id);
        if (existing == null) return OperationResult.Fail(L.T("Matériel introuvable."));
        draft.Nom = (draft.Nom ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Nom))
            return OperationResult.Fail(L.T("Le nom du matériel est obligatoire."));
        var nouvelleQuantite = Math.Max(1, draft.QuantiteTotale);
        var enPret = _prets
            .Where(p => p.MaterielId == id && string.IsNullOrWhiteSpace(p.DateRetour))
            .Sum(p => p.Quantite);
        if (nouvelleQuantite < enPret)
            return OperationResult.Fail(L.F("Quantité inférieure aux prêts actifs ({0}).", enPret));
        existing.Nom = draft.Nom;
        existing.Description = (draft.Description ?? "").Trim();
        existing.Categorie = (draft.Categorie ?? "").Trim();
        existing.QuantiteTotale = nouvelleQuantite;
        if (ListesChoix.Contient(ListesChoix.EtatsMateriel, draft.Etat, _settings.Listes)) existing.Etat = draft.Etat;
        existing.Responsable = (draft.Responsable ?? "").Trim();
        SaveMateriels();
        LogActivity(Models.ActivityTypes.MaterielChange, "Modification du matériel « " + existing.Nom + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult DeleteMateriel(string id)
    {
        var existing = FindMateriel(id);
        if (existing == null) return OperationResult.Fail(L.T("Matériel introuvable."));
        if (_prets.Any(p => p.MaterielId == id && string.IsNullOrWhiteSpace(p.DateRetour)))
            return OperationResult.Fail(L.T("Des prêts actifs existent : retournez-les d'abord."));
        _materiels.Remove(existing);
        _prets.RemoveAll(p => p.MaterielId == id);
        SaveMateriels();
        SavePrets();
        LogActivity(Models.ActivityTypes.MaterielChange, "Suppression du matériel « " + existing.Nom + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult AddPret(string materielId, string membreIdentifiant, int quantite)
    {
        var mat = FindMateriel(materielId);
        if (mat == null) return OperationResult.Fail(L.T("Matériel introuvable."));
        if (FindUserByIdentifier(membreIdentifiant) == null)
            return OperationResult.Fail(L.F("Membre introuvable : « {0} ».", membreIdentifiant));
        if (quantite < 1) return OperationResult.Fail(L.T("La quantité doit être d'au moins 1."));
        if (quantite > DisponibleMateriel(materielId))
            return OperationResult.Fail(L.F("Stock insuffisant : {0} disponible(s).", DisponibleMateriel(materielId)));
        _prets.Add(new PretMateriel
        {
            Id = Guid.NewGuid().ToString("N"),
            MaterielId = materielId,
            MembreIdentifiant = membreIdentifiant.Trim(),
            Quantite = quantite,
            DatePret = DateTime.Today.ToString("yyyy-MM-dd"),
            DateRetour = ""
        });
        SavePrets();
        LogActivity(Models.ActivityTypes.PretChange,
            "Prêt de « " + mat.Nom + " » (×" + quantite + ") à " + membreIdentifiant);
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult ReturnPret(string pretId)
    {
        var pret = _prets.FirstOrDefault(p => p.Id == pretId);
        if (pret == null) return OperationResult.Fail(L.T("Prêt introuvable."));
        if (!string.IsNullOrWhiteSpace(pret.DateRetour))
            return OperationResult.Fail(L.T("Ce prêt est déjà retourné."));
        pret.DateRetour = DateTime.Today.ToString("yyyy-MM-dd");
        SavePrets();
        var mat = FindMateriel(pret.MaterielId);
        LogActivity(Models.ActivityTypes.PretChange,
            "Retour de « " + (mat?.Nom ?? "?") + " » (×" + pret.Quantite + ") par " + pret.MembreIdentifiant);
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult DeletePret(string pretId)
    {
        var idx = _prets.FindIndex(p => p.Id == pretId);
        if (idx < 0) return OperationResult.Fail(L.T("Prêt introuvable."));
        _prets.RemoveAt(idx);
        SavePrets();
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    // ---------- Réunions et présence ----------

    public IReadOnlyList<Reunion> Reunions => _reunions;

    public Reunion? FindReunion(string id) => _reunions.FirstOrDefault(r => r.Id == id);

    public OperationResult AddReunion(Reunion draft)
    {
        draft.Titre = (draft.Titre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Titre))
            return OperationResult.Fail(L.T("Le titre de la réunion est obligatoire."));
        if (string.IsNullOrWhiteSpace(draft.Date) || !TryParseYmd(draft.Date, out _))
            return OperationResult.Fail(L.T("La date doit être au format JJ/MM/AAAA."));
        draft.Date = ToYmd(draft.Date);
        draft.Lieu = (draft.Lieu ?? "").Trim();
        draft.CompteRendu ??= "";
        draft.Participants = (draft.Participants ?? new List<string>())
            .Select(p => (p ?? "").Trim()).Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var p in draft.Participants)
        {
            if (FindUserByIdentifier(p) == null)
                return OperationResult.Fail(L.F("Participant introuvable : « {0} ».", p));
        }
        draft.Presences ??= new Dictionary<string, string>();
        draft.Id = Guid.NewGuid().ToString("N");
        draft.DateCreation = DateTimeOffset.Now.ToString("O");
        draft.CreatedBy = GetCurrentUser()?.Identifiant ?? "inconnu";
        _reunions.Add(draft);
        _reunions.Sort((a, b) => string.Compare(a.Date, b.Date, StringComparison.Ordinal));
        SaveReunions();
        LogActivity(Models.ActivityTypes.ReunionChange, "Création de la réunion « " + draft.Titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult UpdateReunion(string id, Reunion draft)
    {
        var existing = FindReunion(id);
        if (existing == null) return OperationResult.Fail(L.T("Réunion introuvable."));
        draft.Titre = (draft.Titre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Titre))
            return OperationResult.Fail(L.T("Le titre de la réunion est obligatoire."));
        if (string.IsNullOrWhiteSpace(draft.Date) || !TryParseYmd(draft.Date, out _))
            return OperationResult.Fail(L.T("La date doit être au format JJ/MM/AAAA."));
        var participants = (draft.Participants ?? new List<string>())
            .Select(p => (p ?? "").Trim()).Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var p in participants)
        {
            // Un membre archivé déjà invité reste accepté : sinon la réunion ne pourrait plus être modifiée.
            if (FindUserByIdentifier(p) == null && !(existing.Participants ?? new List<string>()).Contains(p, StringComparer.OrdinalIgnoreCase))
                return OperationResult.Fail(L.F("Participant introuvable : « {0} ».", p));
        }
        existing.Titre = draft.Titre;
        existing.Date = ToYmd(draft.Date);
        existing.HeureDebut = draft.HeureDebut;
        existing.HeureFin = draft.HeureFin;
        existing.Lieu = (draft.Lieu ?? "").Trim();
        existing.Participants = participants;
        foreach (var k in existing.Presences.Keys.Where(k => !participants.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            existing.Presences.Remove(k);
        existing.CompteRendu = draft.CompteRendu ?? "";
        _reunions.Sort((a, b) => string.Compare(a.Date, b.Date, StringComparison.Ordinal));
        SaveReunions();
        LogActivity(Models.ActivityTypes.ReunionChange, "Modification de la réunion « " + existing.Titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult DeleteReunion(string id)
    {
        var idx = _reunions.FindIndex(r => r.Id == id);
        if (idx < 0) return OperationResult.Fail(L.T("Réunion introuvable."));
        var titre = _reunions[idx].Titre;
        _reunions.RemoveAt(idx);
        SaveReunions();
        LogActivity(Models.ActivityTypes.ReunionChange, "Suppression de la réunion « " + titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Pointe la présence (ajoute le membre aux participants si besoin).</summary>
    public OperationResult MarkPresence(string reunionId, string membreIdentifiant, string statut)
    {
        var reunion = FindReunion(reunionId);
        if (reunion == null) return OperationResult.Fail(L.T("Réunion introuvable."));
        if (FindUserByIdentifier(membreIdentifiant) == null)
            return OperationResult.Fail(L.F("Membre introuvable : « {0} ».", membreIdentifiant));
        if (statut is not (PresenceStatuts.Present or PresenceStatuts.Absent or PresenceStatuts.Excuse))
            return OperationResult.Fail(L.T("Statut de présence inconnu."));
        var membre = membreIdentifiant.Trim();
        if (!reunion.Participants.Contains(membre, StringComparer.OrdinalIgnoreCase))
            reunion.Participants.Add(membre);
        reunion.Presences[membre] = statut;
        SaveReunions();
        LogActivity(Models.ActivityTypes.PresenceMark,
            "Présence « " + reunion.Titre + " » : " + membre + " → " + PresenceStatuts.Label(statut));
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public (int Presents, int Absents, int Excuses, int NonPointes, int Total) ReunionStats(string reunionId)
    {
        var reunion = FindReunion(reunionId);
        if (reunion == null) return (0, 0, 0, 0, 0);
        var presents = 0; var absents = 0; var excuses = 0; var nonPointes = 0;
        foreach (var p in reunion.Participants)
        {
            if (!reunion.Presences.TryGetValue(p, out var s)) nonPointes++;
            else if (s == PresenceStatuts.Absent) absents++;
            else if (s == PresenceStatuts.Excuse) excuses++;
            else presents++;
        }
        return (presents, absents, excuses, nonPointes, reunion.Participants.Count);
    }

    // ---------- Rappels intelligents (calculés, sans stockage) ----------

    /// <summary>Rappels calculés : tâches, anniversaires, événements, cotisations.</summary>
    public IReadOnlyList<Rappel> GetRappels()
    {
        var rappels = new List<Rappel>();
        var today = DateTime.Today;

        foreach (var t in _taches.Where(t => t.Statut != Models.TacheStatuts.Terminee))
        {
            if (TryParseYmd(t.Echeance, out var ech))
            {
                var jours = (ech.Date - today).Days;
                if (jours < 0)
                    rappels.Add(new Rappel
                    {
                        Id = "tache:retard:" + t.Id, Categorie = L.T("Tâches"),
                        Titre = L.F("En retard : {0}", t.Titre),
                        Detail = L.F("Échéance dépassée depuis {0} j ({1})", -jours, FormatDateCourte(t.Echeance)),
                        Priorite = Models.CalendarEventTypes.PrioriteUrgente, PageCible = "taches"
                    });
                else if (jours <= 3)
                    rappels.Add(new Rappel
                    {
                        Id = "tache:echeance:" + t.Id, Categorie = L.T("Tâches"),
                        Titre = L.F("Échéance proche : {0}", t.Titre),
                        Detail = L.F("J-{0} ({1})", jours, FormatDateCourte(t.Echeance)),
                        Priorite = Models.CalendarEventTypes.PrioriteImportante, PageCible = "taches"
                    });
            }
        }

        foreach (var u in _users.Where(u => u.Actif && !u.EstDecede && TryParseYmd(u.DateNaissance, out _)))
        {
            TryParseYmd(u.DateNaissance, out var naiss);
            // AddYears ramène le 29 février au 28 les années non bissextiles (pas d'exception).
            var prochain = naiss.Date.AddYears(today.Year - naiss.Year);
            if (prochain < today) prochain = naiss.Date.AddYears(today.Year + 1 - naiss.Year);
            var jours = (prochain - today).Days;
            if (jours <= 30)
                rappels.Add(new Rappel
                {
                    Id = "anniversaire:" + u.Id, Categorie = L.T("Anniversaires"),
                    Titre = L.F("Anniversaire de {0}", u.NomComplet),
                    Detail = jours == 0 ? L.F("Aujourd'hui ({0})", FormatDateCourte(u.DateNaissance)) : L.F("Dans {0} j ({1})", jours, prochain.ToString("dd/MM/yyyy")),
                    Priorite = jours <= 7 ? Models.CalendarEventTypes.PrioriteImportante : Models.CalendarEventTypes.PrioriteNormale,
                    PageCible = "users"
                });
        }

        foreach (var e in _events)
        {
            if (!TryParseYmd(e.Date, out var d)) continue;
            var jours = (d.Date - today).Days;
            if (jours >= 0 && jours <= 7)
                rappels.Add(new Rappel
                {
                    Id = "evenement:" + e.Id, Categorie = L.T("Calendrier"),
                    Titre = e.Titre,
                    Detail = jours == 0 ? L.F("Aujourd'hui ({0})", FormatDateCourte(e.Date)) : L.F("Dans {0} j ({1})", jours, FormatDateCourte(e.Date)),
                    Priorite = Models.CalendarEventTypes.PrioriteNormale, PageCible = "calendar"
                });
        }

        var annee = today.Year.ToString();
        var impayes = ImpayesCotisations(annee);
        if (impayes.Count > 0)
            rappels.Add(new Rappel
            {
                Id = "cotisations:" + annee, Categorie = L.T("Cotisations"),
                Titre = L.F("{0} cotisation(s) impayée(s) en {1}", impayes.Count, annee),
                Detail = string.Join(", ", impayes.Take(3).Select(u => u.NomComplet)) + (impayes.Count > 3 ? ", …" : ""),
                Priorite = Models.CalendarEventTypes.PrioriteImportante, PageCible = "finances"
            });

        return rappels
            .OrderBy(r => r.Priorite == Models.CalendarEventTypes.PrioriteUrgente ? 0
                : r.Priorite == Models.CalendarEventTypes.PrioriteImportante ? 1 : 2)
            .ThenBy(r => r.Titre)
            .Take(50)
            .ToList();
    }

    // ---------- Situations particulières ----------

    public IReadOnlyList<Situation> Situations => _situations;

    public Situation? FindSituation(string id) => _situations.FirstOrDefault(s => s.Id == id);

    public IReadOnlyList<Situation> GetSituationsForMember(string identifiant)
        => _situations
            .Where(s => s.MembreIdentifiant.Equals(identifiant ?? "", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Statut == Models.SituationStatuts.Resolu ? 1 : 0)
            .ThenByDescending(s => s.DateCreation)
            .ToList();

    /// <param name="participationEnFinances">Enregistrer la participation comme sortie dans Finances (null : inchangé).</param>
    public OperationResult AddSituation(Situation draft, bool? participationEnFinances = null)
    {
        draft.Titre = (draft.Titre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Titre))
            return OperationResult.Fail(L.T("Le titre de la situation est obligatoire."));
        if (FindUserByIdentifier(draft.MembreIdentifiant) == null)
            return OperationResult.Fail(L.F("Membre introuvable : « {0} ».", draft.MembreIdentifiant));
        if (!ListesChoix.Contient(ListesChoix.TypesSituation, draft.Type, _settings.Listes))
            draft.Type = Models.SituationTypes.Visite;
        if (NormaliserFamangiana(draft) is { } erreur) return OperationResult.Fail(erreur);
        draft.Description = (draft.Description ?? "").Trim();
        draft.MembreIdentifiant = draft.MembreIdentifiant.Trim();
        draft.Id = Guid.NewGuid().ToString("N");
        draft.Statut = Models.SituationStatuts.Ouvert;
        draft.DateCreation = DateTimeOffset.Now.ToString("O");
        draft.DateResolution = null;
        draft.CreatedBy = GetCurrentUser()?.Identifiant ?? "inconnu";
        draft.EvenementId = null;
        draft.TransactionId = null;
        draft.EnfantIdentifiant = null;
        _situations.Add(draft);
        SyncVisite(draft);
        SyncParticipation(draft, participationEnFinances);
        SaveSituations();
        LogActivity(Models.ActivityTypes.SituationChange, "Suivi ouvert : « " + draft.Titre + " » (" + draft.MembreIdentifiant + ")");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult UpdateSituation(string id, Situation draft, bool? participationEnFinances = null)
    {
        var existing = FindSituation(id);
        if (existing == null) return OperationResult.Fail(L.T("Situation introuvable."));
        draft.Titre = (draft.Titre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Titre))
            return OperationResult.Fail(L.T("Le titre de la situation est obligatoire."));
        if (FindUserByIdentifier(draft.MembreIdentifiant) == null)
            return OperationResult.Fail(L.F("Membre introuvable : « {0} ».", draft.MembreIdentifiant));
        if (NormaliserFamangiana(draft) is { } erreur) return OperationResult.Fail(erreur);
        existing.Titre = draft.Titre;
        existing.Description = (draft.Description ?? "").Trim();
        existing.MembreIdentifiant = draft.MembreIdentifiant.Trim();
        if (ListesChoix.Contient(ListesChoix.TypesSituation, draft.Type, _settings.Listes))
            existing.Type = draft.Type;
        existing.Priorite = draft.Priorite;
        CopierFamangiana(draft, existing);
        SyncVisite(existing);
        SyncParticipation(existing, participationEnFinances);
        SaveSituations();
        LogActivity(Models.ActivityTypes.SituationChange, "Modification du suivi « " + existing.Titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Fait avancer le suivi : Ouvert → En cours → Résolu.</summary>
    public OperationResult SetSituationStatut(string id, string statut)
    {
        var existing = FindSituation(id);
        if (existing == null) return OperationResult.Fail(L.T("Situation introuvable."));
        if (statut is not (Models.SituationStatuts.Ouvert or Models.SituationStatuts.EnCours or Models.SituationStatuts.Resolu))
            return OperationResult.Fail(L.T("Statut inconnu."));
        existing.Statut = statut;
        existing.DateResolution = statut == Models.SituationStatuts.Resolu ? DateTimeOffset.Now.ToString("O") : null;
        SaveSituations();
        LogActivity(Models.ActivityTypes.SituationChange,
            "Suivi « " + existing.Titre + " » → " + Models.SituationStatuts.Label(statut));
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult DeleteSituation(string id)
    {
        var idx = _situations.FindIndex(s => s.Id == id);
        if (idx < 0) return OperationResult.Fail(L.T("Situation introuvable."));
        var titre = _situations[idx].Titre;
        // La visite prévue disparaît du calendrier ; la participation reste dans Finances.
        var sansVisite = _situations[idx];
        sansVisite.DateVisite = "";
        SyncVisite(sansVisite);
        _situations.RemoveAt(idx);
        SaveSituations();
        LogActivity(Models.ActivityTypes.SituationChange, "Suppression du suivi « " + titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    // ---------- Carte de membre numérique (calculée, sans stockage) ----------

    /// <summary>Code membre stable dérivé de l'identifiant technique.</summary>
    public static string CodeMembre(UserAccount user)
    {
        var id = (user?.Id ?? "").Trim();
        var code = id.Length >= 6 ? id[..6] : id.PadRight(6, '0');
        return "GU-" + code.ToUpperInvariant();
    }

    // ---------- Votes et décisions internes ----------

    public IReadOnlyList<Vote> Votes => _votes;

    public Vote? FindVote(string id) => _votes.FirstOrDefault(v => v.Id == id);

    public OperationResult AddVote(Vote draft)
    {
        draft.Titre = (draft.Titre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Titre))
            return OperationResult.Fail(L.T("Le titre du vote est obligatoire."));
        draft.Description = (draft.Description ?? "").Trim();
        draft.Options = (draft.Options ?? new List<string>())
            .Select(o => (o ?? "").Trim()).Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (draft.Options.Count < 2)
            return OperationResult.Fail(L.T("Un vote doit proposer au moins 2 options."));
        if (!string.IsNullOrWhiteSpace(draft.DateCloture) && !TryParseYmd(draft.DateCloture, out _))
            return OperationResult.Fail(L.T("La date de clôture doit être au format JJ/MM/AAAA."));
        draft.DateCloture = string.IsNullOrWhiteSpace(draft.DateCloture) ? "" : ToYmd(draft.DateCloture);
        draft.Id = Guid.NewGuid().ToString("N");
        draft.Statut = Models.VoteStatuts.Ouvert;
        draft.Bulletins = new List<Models.Bulletin>();
        draft.DateCreation = DateTimeOffset.Now.ToString("O");
        draft.CreatedBy = GetCurrentUser()?.Identifiant ?? "inconnu";
        _votes.Add(draft);
        SaveVotes();
        LogActivity(Models.ActivityTypes.VoteChange, "Ouverture du vote « " + draft.Titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult UpdateVote(string id, Vote draft)
    {
        var existing = FindVote(id);
        if (existing == null) return OperationResult.Fail(L.T("Vote introuvable."));
        if (existing.Statut != Models.VoteStatuts.Ouvert)
            return OperationResult.Fail(L.T("Un vote clos ne peut plus être modifié (résultats figés)."));
        draft.Titre = (draft.Titre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Titre))
            return OperationResult.Fail(L.T("Le titre du vote est obligatoire."));
        var options = (draft.Options ?? new List<string>())
            .Select(o => (o ?? "").Trim()).Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (options.Count < 2)
            return OperationResult.Fail(L.T("Un vote doit proposer au moins 2 options."));
        if (!string.IsNullOrWhiteSpace(draft.DateCloture) && !TryParseYmd(draft.DateCloture, out _))
            return OperationResult.Fail(L.T("La date de clôture doit être au format JJ/MM/AAAA."));
        existing.Titre = draft.Titre;
        existing.Description = (draft.Description ?? "").Trim();
        // Même nombre d'options : une option corrigée garde ses bulletins (renommage par position).
        if (options.Count == existing.Options.Count)
        {
            for (var i = 0; i < options.Count; i++)
            {
                var ancienne = existing.Options[i];
                if (ancienne.Equals(options[i], StringComparison.Ordinal)) continue;
                if (existing.Options.Contains(options[i], StringComparer.OrdinalIgnoreCase)) continue; // simple réordonnancement
                foreach (var b in existing.Bulletins.Where(b => b.Option.Equals(ancienne, StringComparison.OrdinalIgnoreCase)))
                    b.Option = options[i];
            }
        }
        existing.Options = options;
        existing.DateCloture = string.IsNullOrWhiteSpace(draft.DateCloture) ? "" : ToYmd(draft.DateCloture);
        // Retirer les bulletins devenus hors options.
        existing.Bulletins.RemoveAll(b => !options.Contains(b.Option, StringComparer.OrdinalIgnoreCase));
        SaveVotes();
        LogActivity(Models.ActivityTypes.VoteChange, "Modification du vote « " + existing.Titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Exprime ou modifie le vote d'un membre (un seul bulletin par membre).</summary>
    public OperationResult CastVote(string voteId, string votantIdentifiant, string option)
    {
        var existing = FindVote(voteId);
        if (existing == null) return OperationResult.Fail(L.T("Vote introuvable."));
        if (existing.Statut != Models.VoteStatuts.Ouvert)
            return OperationResult.Fail(L.T("Ce vote est clos."));
        if (TryParseYmd(existing.DateCloture, out var cloture) && cloture.Date < DateTime.Today)
            return OperationResult.Fail(L.T("La date de clôture est dépassée."));
        votantIdentifiant = (votantIdentifiant ?? "").Trim();
        option = (option ?? "").Trim();
        if (FindUserByIdentifier(votantIdentifiant) == null)
            return OperationResult.Fail(L.F("Votant introuvable : « {0} ».", votantIdentifiant));
        if (!existing.Options.Contains(option, StringComparer.OrdinalIgnoreCase))
            return OperationResult.Fail(L.T("Option inconnue pour ce vote."));
        var bulletin = existing.Bulletins.FirstOrDefault(b =>
            b.VotantIdentifiant.Equals(votantIdentifiant, StringComparison.OrdinalIgnoreCase));
        if (bulletin == null)
        {
            existing.Bulletins.Add(new Models.Bulletin
            {
                VotantIdentifiant = votantIdentifiant,
                Option = option,
                Date = DateTimeOffset.Now.ToString("O")
            });
        }
        else
        {
            bulletin.Option = option;
            bulletin.Date = DateTimeOffset.Now.ToString("O");
        }
        SaveVotes();
        LogActivity(Models.ActivityTypes.VoteCast, "Vote exprimé sur « " + existing.Titre + " » par " + votantIdentifiant);
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult SetVoteStatut(string id, string statut)
    {
        var existing = FindVote(id);
        if (existing == null) return OperationResult.Fail(L.T("Vote introuvable."));
        if (statut is not (Models.VoteStatuts.Ouvert or Models.VoteStatuts.Clos))
            return OperationResult.Fail(L.T("Statut inconnu."));
        existing.Statut = statut;
        SaveVotes();
        LogActivity(Models.ActivityTypes.VoteChange,
            "Vote « " + existing.Titre + " » → " + Models.VoteStatuts.Label(statut));
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult DeleteVote(string id)
    {
        var idx = _votes.FindIndex(v => v.Id == id);
        if (idx < 0) return OperationResult.Fail(L.T("Vote introuvable."));
        var titre = _votes[idx].Titre;
        _votes.RemoveAt(idx);
        SaveVotes();
        LogActivity(Models.ActivityTypes.VoteChange, "Suppression du vote « " + titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Résultats : bulletins par option (ordre décroissant).</summary>
    public IReadOnlyList<(string Option, int Voix, double Pourcent)> VoteResults(string voteId)
    {
        var vote = FindVote(voteId);
        if (vote == null) return new List<(string, int, double)>();
        var total = Math.Max(1, vote.Bulletins.Count);
        return vote.Options
            .Select(o => (Option: o,
                Voix: vote.Bulletins.Count(b => b.Option.Equals(o, StringComparison.OrdinalIgnoreCase)),
                Pourcent: 0.0))
            .Select(x => (x.Option, x.Voix, Math.Round(x.Voix * 100.0 / total, 1)))
            .OrderByDescending(x => x.Voix)
            .ToList();
    }

    // ---------- Journal multimédia (photos, vidéos, audios — 100 % local) ----------

    public IReadOnlyList<MediaItem> JournalMedia => _journalMedia;

    public MediaItem? FindMedia(string id) => _journalMedia.FirstOrDefault(m => m.Id == id);

    public OperationResult AddMedia(MediaItem draft)
    {
        draft.Titre = (draft.Titre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Titre))
            return OperationResult.Fail(L.T("Le titre est obligatoire."));
        if (draft.Type is not (Models.MediaTypes.Video or Models.MediaTypes.Audio)) draft.Type = Models.MediaTypes.Photo;
        if (string.IsNullOrWhiteSpace(draft.Data))
            return OperationResult.Fail(L.T("Le contenu multimédia est manquant."));
        if (draft.Taille <= 0)
            return OperationResult.Fail(L.T("Le contenu multimédia est manquant."));
        draft.Description = (draft.Description ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Date)) draft.Date = DateTime.Today.ToString("yyyy-MM-dd");
        else if (!TryParseYmd(draft.Date, out _))
            return OperationResult.Fail(L.T("La date doit être au format JJ/MM/AAAA."));
        else draft.Date = ToYmd(draft.Date);
        draft.Id = Guid.NewGuid().ToString("N");
        draft.DateCreation = DateTimeOffset.Now.ToString("O");
        draft.CreatedBy = GetCurrentUser()?.Identifiant ?? "inconnu";
        _journalMedia.Add(draft);
        SaveJournalMedia();
        LogActivity(Models.ActivityTypes.MediaChange,
            "Ajout au journal (" + Models.MediaTypes.Label(draft.Type) + ") : « " + draft.Titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult UpdateMedia(string id, MediaItem draft)
    {
        var existing = FindMedia(id);
        if (existing == null) return OperationResult.Fail(L.T("Élément introuvable."));
        draft.Titre = (draft.Titre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(draft.Titre))
            return OperationResult.Fail(L.T("Le titre est obligatoire."));
        existing.Titre = draft.Titre;
        existing.Description = (draft.Description ?? "").Trim();
        SaveJournalMedia();
        LogActivity(Models.ActivityTypes.MediaChange, "Modification au journal : « " + existing.Titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    public OperationResult DeleteMedia(string id)
    {
        var idx = _journalMedia.FindIndex(m => m.Id == id);
        if (idx < 0) return OperationResult.Fail(L.T("Élément introuvable."));
        var titre = _journalMedia[idx].Titre;
        _journalMedia.RemoveAt(idx);
        SaveJournalMedia();
        LogActivity(Models.ActivityTypes.MediaChange, "Suppression au journal : « " + titre + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    // ---------- Réinitialisation / démo ----------

    /// <summary>
    /// Données initiales : annuaire vierge, sans champ personnalisé ni journal
    /// pré-rempli. Le compte applicatif « admin » et le compte technique « dba »
    /// sont les seuls comptes créés pour permettre l'administration initiale.
    /// </summary>
    public void SeedDemoData()
    {
        var now = DateTimeOffset.Now;

        _users.Clear();
        _users.Add(new UserAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            Prenom = "admin",
            Nom = "ADMIN",
            Identifiant = "admin",
            Email = "",
            Telephone = "",
            Role = Roles.Administrateur,
            Actif = true,
            MotDePasse = PasswordCipher.Encrypt("admin123"),
            DateCreation = now.ToString("O"),
            Notes = "Administrateur initial."
        });
        _users.Add(new UserAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            Prenom = "dba",
            Nom = "DBA",
            Identifiant = "dba",
            Email = "",
            Telephone = "",
            Role = Roles.BaseAdmin,
            Actif = true,
            MotDePasse = PasswordCipher.Encrypt("admindba"),
            DateCreation = now.ToString("O"),
            Notes = "Gestionnaire de la base de données."
        });

        _fields.Clear();
        _fields.Add(SystemField(FieldDefinition.FIELD_IDENTIFIANT, "Identifiant", false));

        _activity.Clear();

        _archive.Clear();

        _events.Clear();

        _documents.Clear();

        _folders.Clear();

        _community = new CommunityStory
        {
            Titre = "Histoire de notre communauté",
            SousTitre = "",
            Chapitres = new List<CommunityChapter>()
        };

        _permissions = DefaultPermissions();

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

        _sync = new SyncSnapshot { DeviceId = DeviceId };

        _session = null;

        SaveUsers();
        SaveFields();
        SaveActivity();
        SaveArchive();
        SaveEvents();
        SaveDocuments();
        SaveFolders();
        SaveCommunity();
        SavePermissions();
        SaveMessages();
        SaveTaches();
        SaveCotisations();
        SaveTresorerie();
        SaveMateriels();
        SavePrets();
        SaveReunions();
        SaveSituations();
        SaveVotes();
        SaveJournalMedia();
        SaveSyncSnapshot();
        SaveSettings();
        SaveSession();
    }

    public void ResetAll()
    {
        if (OnlineMode) ResetAllOnline();
        else SeedDemoData();
    }

    /// <summary>Réinitialise l'application puis génère un jeu de données de démonstration réaliste (captures du guide).</summary>
    public void ResetWithDemoData()
    {
        ResetAll();

        var today = DateTime.Today;
        string Ymd(DateTime d) => d.ToString("yyyy-MM-dd");
        static string TextData(string text)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            return "data:text/plain;base64," + Convert.ToBase64String(bytes);
        }
        static string SvgData(string txt, int w, int h, string bg1, string bg2)
        {
            var doc = string.Format(
                "<svg xmlns='http://www.w3.org/2000/svg' width='{0}' height='{1}'>" +
                "<defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='1'>" +
                "<stop offset='0' stop-color='{2}'/><stop offset='1' stop-color='{3}'/>" +
                "</linearGradient></defs>" +
                "<rect width='{0}' height='{1}' fill='url(#g)'/>" +
                "<circle cx='{4}' cy='{5}' r='{6}' fill='#ffffff22'/>" +
                "<text x='{7}' y='{8}' font-size='{9}' font-family='Arial, sans-serif' font-weight='bold' fill='#fff'>{10}</text>" +
                "</svg>",
                w, h, bg1, bg2,
                (int)(w * 0.78), (int)(h * 0.22), (int)(w * 0.2),
                (int)(w * 0.05), (int)(h * 0.6), Math.Max(18, (int)(h * 0.13)),
                System.Net.WebUtility.HtmlEncode(txt));
            var bytes = System.Text.Encoding.UTF8.GetBytes(doc);
            return "data:image/svg+xml;base64," + Convert.ToBase64String(bytes);
        }

        if (_settings != null)
        {
            _settings.Logo = SvgData("GU", 256, 256, "#4f46e5", "#7c3aed");
            SaveSettings();
        }

        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void DemoUser(string prenom, string nom, string role, string tel, string email, string matricule)
        {
            var res = AddUser(new UserAccount
            {
                Prenom = prenom,
                Nom = nom,
                Role = role,
                Actif = true,
                Telephone = tel,
                Email = email,
                Matricule = matricule,
                DateNaissance = today.AddYears(-(20 + _users.Count % 24)).AddDays(-(3 + _users.Count % 20)).ToString("yyyy-MM-dd"),
                Notes = role == Roles.Gestionnaire ? "Référent du groupe." : ""
            });
            if (res.Success && res.UserId != null)
            {
                var acc = _users.FirstOrDefault(u => u.Id == res.UserId);
                if (acc != null) ids[acc.Identifiant] = acc.Id;
            }
        }

        DemoUser("Tiana", "RAKOTO", Roles.Gestionnaire, "+261 34 12 345 67", "tiana.rakoto@exemple.org", "M-2026-001");
        DemoUser("Andry", "RASOA", Roles.Utilisateur, "+261 32 45 678 90", "andry.rasoa@exemple.org", "M-2026-002");
        DemoUser("Mialy", "VOAHANGY", Roles.Utilisateur, "", "mialy.voahangy@exemple.org", "M-2026-003");
        DemoUser("Haja", "RAZAFY", Roles.Gestionnaire, "+261 33 10 203 04", "haja.razafy@exemple.org", "M-2026-014");
        DemoUser("Noro", "RAVELO", Roles.Utilisateur, "", "noro.ravelo@exemple.org", "M-2026-005");
        DemoUser("Lova", "RANDRIAMAMPIANINA", Roles.Utilisateur, "", "lova.randria@exemple.org", "M-2026-006");
        DemoUser("Tovo", "ANDRIANJAFY", Roles.Utilisateur, "+261 34 98 765 43", "", "M-2026-007");
        DemoUser("Soa", "RABEMANANJARA", Roles.Utilisateur, "", "soa.rabema@exemple.org", "M-2026-008");
        DemoUser("Mirado", "RANARIVELO", Roles.Utilisateur, "", "mirado.rana@exemple.org", "M-2026-009");
        DemoUser("Fy", "SOLOFO", Roles.Utilisateur, "", "fy.solofo@exemple.org", "M-2026-010");

        AddEvent(new CalendarEvent
        {
            Type = CalendarEventTypes.Reunion,
            Titre = "Réunion mensuelle de la communauté",
            Date = Ymd(today),
            HeureDebut = "18:00",
            HeureFin = "20:00",
            Lieu = "Salle polyvalente",
            Description = "Ordre du jour : bilan du mois, préparation de la kermesse et nouvelles cotisations.",
            Statut = CalendarEventTypes.StatutAFaire,
            CreatedBy = "admin"
        });
        AddEvent(new CalendarEvent
        {
            Type = CalendarEventTypes.Anniversaire,
            Titre = "Anniversaire de Tiana",
            Date = Ymd(today.AddDays(6)),
            TouteLaJournee = true,
            PersonnesConcernees = new List<string> { "tiana.rakoto" }
        });
        AddEvent(new CalendarEvent
        {
            Type = CalendarEventTypes.Evenement,
            Titre = "Kermesse de fin d'année",
            Date = Ymd(today.AddDays(20)),
            HeureDebut = "09:00",
            HeureFin = "17:00",
            Lieu = "Terrain communal",
            Description = "Jeux, stands et tombola. Le bénéfice financera les activités de l'année prochaine.",
            Statut = CalendarEventTypes.StatutAFaire,
            CreatedBy = "admin"
        });
        AddEvent(new CalendarEvent
        {
            Type = CalendarEventTypes.Echeance,
            Titre = "Règlement cotisation trimestrielle",
            Date = Ymd(today.AddDays(9)),
            Priorite = CalendarEventTypes.PrioriteImportante,
            CreatedBy = "admin"
        });

        AddCotisation(new Cotisation { MembreIdentifiant = "tiana.rakoto", Montant = 50000, Motif = "Cotisation annuelle", Periode = "2026", Date = Ymd(today.AddMonths(-3)), Statut = CotisationStatuts.Payee, CreatedBy = "admin" });
        AddCotisation(new Cotisation { MembreIdentifiant = "haja.razafy", Montant = 20000, Motif = "Cotisation semestrielle", Periode = "2026 / 1", Date = Ymd(today.AddMonths(-2)), Statut = CotisationStatuts.Payee, CreatedBy = "admin" });
        AddCotisation(new Cotisation { MembreIdentifiant = "tovo.andrianjafy", Montant = 10000, Motif = "Cotisation trimestrielle", Periode = "2026 / 1", Date = Ymd(today.AddMonths(-1)), Statut = CotisationStatuts.Payee, CreatedBy = "admin" });
        AddCotisation(new Cotisation { MembreIdentifiant = "andry.rasoa", Montant = 10000, Motif = "Cotisation trimestrielle", Periode = "2026 / 1", Date = Ymd(today.AddMonths(-1)), Statut = CotisationStatuts.EnAttente, CreatedBy = "admin" });

        AddTransaction(new TransactionTresorerie { Type = TresorerieTypes.Entree, Montant = 250000, Motif = "Recettes kermesse 2025", Date = Ymd(today.AddMonths(-2)), CreatedBy = "admin" });
        AddTransaction(new TransactionTresorerie { Type = TresorerieTypes.Entree, Montant = 80000, Motif = "Cotisations — Janvier 2026", Date = Ymd(today.AddMonths(-1)), CreatedBy = "admin" });
        AddTransaction(new TransactionTresorerie { Type = TresorerieTypes.Sortie, Montant = 60000, Motif = "Achat de matériel (sonorisation)", Date = Ymd(today.AddDays(-14)), CreatedBy = "admin" });
        AddTransaction(new TransactionTresorerie { Type = TresorerieTypes.Sortie, Montant = 35000, Motif = "Goûter de la réunion", Date = Ymd(today.AddDays(-5)), CreatedBy = "admin" });
        AddTransaction(new TransactionTresorerie { Type = TresorerieTypes.Entree, Montant = 12000, Motif = "Cotisation — Tovo", Date = Ymd(today.AddDays(-3)), CreatedBy = "admin" });

        AddTache(new Tache
        {
            Titre = "Préparer la salle pour la réunion",
            Description = "Chaises, tables, vérification du vidéoprojecteur et de la sonorisation.",
            Assignes = new List<string> { "tiana.rakoto", "haja.razafy" },
            Statut = TacheStatuts.EnCours,
            Priorite = CalendarEventTypes.PrioriteImportante,
            Echeance = Ymd(today.AddDays(2)),
            CreatedBy = "admin"
        });
        AddTache(new Tache
        {
            Titre = "Commander les tee-shirts du groupe",
            Description = "42 tee-shirts, tailles et couleurs à confirmer auprès des membres.",
            Assignes = new List<string> { "mialy.voahangy" },
            Statut = TacheStatuts.Terminee,
            Priorite = CalendarEventTypes.PrioriteNormale,
            Echeance = Ymd(today.AddDays(-2)),
            CreatedBy = "admin"
        });
        AddTache(new Tache
        {
            Titre = "Relancer les cotisations en attente",
            Description = "Contacter Andry pour sa cotisation trimestrielle.",
            Assignes = new List<string> { "admin" },
            Statut = TacheStatuts.AFaire,
            Priorite = CalendarEventTypes.PrioriteUrgente,
            Echeance = Ymd(today.AddDays(1)),
            CreatedBy = "admin"
        });

        AddMateriel(new Materiel { Nom = "Vidéoprojecteur Epson EB-X06", Categorie = "Équipement", QuantiteTotale = 1, Etat = MaterielEtats.Bon, Responsable = "haja.razafy" });
        AddMateriel(new Materiel { Nom = "Tente de mobilisation (8 places)", Categorie = "Équipement", QuantiteTotale = 4, Etat = MaterielEtats.Bon, Responsable = "tovo.andrianjafy" });
        AddMateriel(new Materiel { Nom = "Kit de sonorisation", Categorie = "Équipement", QuantiteTotale = 1, Etat = MaterielEtats.Moyen, Responsable = "andry.rasoa" });
        AddMateriel(new Materiel { Nom = "Lot de tables pliantes", Categorie = "Mobilier", QuantiteTotale = 12, Etat = MaterielEtats.Bon, Responsable = "" });
        var projo = _materiels.FirstOrDefault(m => m.Nom.StartsWith("Vidéoprojecteur"));
        if (projo != null)
            AddPret(projo.Id, "andry.rasoa", 1);

        AddReunion(new Reunion
        {
            Titre = "Préparation de la kermesse",
            Date = Ymd(today.AddDays(7)),
            HeureDebut = "16:00",
            HeureFin = "18:30",
            Lieu = "Maison des jeunes",
            Participants = new List<string> { "tiana.rakoto", "haja.razafy", "mialy.voahangy", "noro.ravelo" },
            Presences = new Dictionary<string, string>
            {
                ["tiana.rakoto"] = PresenceStatuts.Present,
                ["haja.razafy"] = PresenceStatuts.Present,
                ["mialy.voahangy"] = PresenceStatuts.Excuse,
                ["noro.ravelo"] = PresenceStatuts.Absent
            },
            CompteRendu = "Repartition des stands validée. La sonorisation sera testée la veille.",
            CreatedBy = "admin"
        });

        AddSituation(new Situation
        {
            MembreIdentifiant = "noro.ravelo",
            Type = SituationTypes.Accompagnement,
            Titre = "Accompagnement démarches administratives",
            Description = "Aide pour le renouvellement des documents d'identité. Rendez-vous pris au bureau.",
            Statut = SituationStatuts.EnCours,
            Priorite = CalendarEventTypes.PrioriteImportante,
            CreatedBy = "admin"
        });
        AddSituation(new Situation
        {
            MembreIdentifiant = "soa.rabemananjara",
            Type = SituationTypes.EvenementImportant,
            Titre = "Mariage de Soa",
            Description = "Mobilisation du groupe pour l'organisation de la cérémonie.",
            Statut = SituationStatuts.Ouvert,
            Priorite = CalendarEventTypes.PrioriteNormale,
            CreatedBy = "admin"
        });

        AddVote(new Vote
        {
            Titre = "Date de la prochaine sortie de groupe",
            Description = "Proposez votre date préférée pour la sortie annuelle.",
            Options = new List<string> { "Samedi 20 juillet", "Dimanche 21 juillet", "Samedi 27 juillet" },
            Statut = VoteStatuts.Ouvert,
            DateCloture = Ymd(today.AddDays(14)),
            CreatedBy = "admin"
        });
        AddVote(new Vote
        {
            Titre = "Couleur des tee-shirts 2026",
            Description = "Choisissez la couleur des tee-shirts de l'équipe.",
            Options = new List<string> { "Indigo", "Vert forêt", "Gris anthracite" },
            Statut = VoteStatuts.Clos,
            DateCloture = Ymd(today.AddDays(-6)),
            CreatedBy = "admin"
        });
        var voteOuvert = _votes.FirstOrDefault(v => v.Statut == VoteStatuts.Ouvert);
        if (voteOuvert != null && voteOuvert.Options.Count > 0)
        {
            CastVote(voteOuvert.Id, "tiana.rakoto", voteOuvert.Options[0]);
            CastVote(voteOuvert.Id, "haja.razafy", voteOuvert.Options[1]);
            CastVote(voteOuvert.Id, "mialy.voahangy", voteOuvert.Options[voteOuvert.Options.Count - 1]);
        }

        AddFolder(new DocumentFolder { Nom = "Comptes rendus", Couleur = "#4f46e5", CreatedBy = "admin" });
        AddFolder(new DocumentFolder { Nom = "Kermesse 2026", Couleur = "#7c3aed", CreatedBy = "admin" });
        var folderCR = _folders.FirstOrDefault(f => f.Nom == "Comptes rendus");
        var folderKermesse = _folders.FirstOrDefault(f => f.Nom == "Kermesse 2026");
        AddDocument(new DocumentItem
        {
            Titre = "Compte rendu — Réunion de janvier",
            Description = "Bilan de la réunion mensuelle de janvier.",
            FolderId = folderCR?.Id,
            Categorie = DocumentCategories.Travail,
            FileName = "cr-janvier-2026.txt",
            Mime = "text/plain",
            Taille = 412,
            Data = TextData("Compte rendu de la réunion de janvier 2026..."),
            CreatedBy = "admin"
        });
        AddDocument(new DocumentItem
        {
            Titre = "Devis sonorisation",
            Description = "Devis du kit de sonorisation pour la kermesse.",
            FolderId = folderKermesse?.Id,
            Categorie = DocumentCategories.Factures,
            FileName = "devis-sonorisation.txt",
            Mime = "text/plain",
            Taille = 289,
            Data = TextData("Montant : 600 000 MGA - Prestataire : Sonor Pro"),
            Favori = true,
            CreatedBy = "admin"
        });
        AddDocument(new DocumentItem
        {
            Titre = "Règlement intérieur",
            Description = "Document de référence du groupe.",
            Categorie = DocumentCategories.Contrats,
            FileName = "reglement-interieur.txt",
            Mime = "text/plain",
            Taille = 905,
            Data = TextData("Règlement intérieur de la communauté..."),
            CreatedBy = "admin"
        });

        SaveCommunityStory("Communauté d'Ambatondrazaka", "Ensemble depuis 2018");
        AddCommunityChapter("Les débuts", "Notre communauté est née d'un petit groupe de voisins décidés à se rendre les services les uns aux autres.", Ymd(today.AddYears(-3)), null);
        AddCommunityChapter("La première kermesse", "Organisée en 2024, elle a rassemblé plus de 200 personnes et financé les premières activités.", Ymd(today.AddYears(-1)), null);
        AddCommunityChapter("Aujourd'hui", "Une équipe de 12 membres actifs et de nombreux projets : préparation de la kermesse 2026 !", Ymd(today.AddMonths(-1)), null);

        SendMessage(new List<string>(), "Bienvenue ! Ce canal centralise les annonces importantes du groupe. N'hésitez pas à l'utiliser pour partager les informations essentielles.");
        SendMessage(new List<string>(), "Rappel : réunion mensuelle ce soir à 18h dans la salle polyvalente.");
        SendMessage(new List<string> { "tiana.rakoto" }, "Tiana, peux-tu confirmer la disponibilité de la salle pour la kermesse ?");

        AddMedia(new MediaItem
        {
            Titre = "Mobilisation — Kermesse 2025",
            Description = "Toute l'équipe présente au terrain communal.",
            Type = MediaTypes.Photo,
            Mime = "image/svg+xml",
            Taille = 0,
            Data = SvgData("Kermesse 2025", 800, 450, "#4f46e5", "#7c3aed"),
            Date = Ymd(today.AddMonths(-2)),
            CreatedBy = "admin"
        });
        AddMedia(new MediaItem
        {
            Titre = "After-work de l'équipe",
            Type = MediaTypes.Photo,
            Mime = "image/svg+xml",
            Taille = 0,
            Data = SvgData("After-work", 800, 450, "#0e7490", "#155e75"),
            Date = Ymd(today.AddDays(-8)),
            CreatedBy = "admin"
        });
        AddMedia(new MediaItem
        {
            Titre = "Enregistrement — Réunion de janvier",
            Description = "Extrait audio de la réunion.",
            Type = MediaTypes.Audio,
            Mime = "audio/mpeg",
            Taille = 2457600,
            Date = Ymd(today.AddDays(-15)),
            CreatedBy = "admin"
        });

        var dba = _users.FirstOrDefault(u => u.Identifiant.Equals("dba", StringComparison.OrdinalIgnoreCase));
        if (dba != null)
            _session = new SessionInfo { UserId = dba.Id, ExpiryUtcTicks = DateTimeOffset.UtcNow.AddHours(8).UtcTicks };
        SaveSession();
    }
}
