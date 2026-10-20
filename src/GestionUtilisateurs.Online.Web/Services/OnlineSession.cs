using GestionUtilisateurs.Shared.Models;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Online.Web.Services;

public enum OnlineSessionState
{
    Starting,
    NotConfigured,
    SignedOut,
    Loading,
    Pending,
    Refused,
    Disabled,
    NoMemberRecord,
    Ready,
    Error
}

/// <summary>Résumé pour les administrateurs : demandes de compte et alertes de capacité.</summary>
public sealed class AdminStatus
{
    public int OpenAlerts { get; set; }
    public string Level { get; set; } = "ok";
    public int PendingAccounts { get; set; }
    /// <summary>Demandes « mot de passe oublié » en attente d'un mot de passe provisoire.</summary>
    public int PasswordRequests { get; set; }
}

/// <summary>
/// Cycle de vie de la connexion en ligne : compte Supabase, profil validé,
/// chargement des données partagées, puis ouverture de la session DataStore.
/// </summary>
public sealed class OnlineSession : IDisposable
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AdminStatusEvery = TimeSpan.FromMinutes(5);

    private readonly OnlineApi _api;
    private readonly OnlineStorageProvider _storage;
    private readonly DataStore _store;
    private readonly AppState _state;
    private System.Threading.Timer? _timer;
    private DateTime _lastAdminStatus = DateTime.MinValue;
    private DateTime _lastNetworkError = DateTime.MinValue;
    private bool _storeInitialized;
    private bool _signingOut;
    private bool _refreshing;

    public OnlineSession(OnlineApi api, OnlineStorageProvider storage, DataStore store, AppState state)
    {
        _api = api;
        _storage = storage;
        _store = store;
        _state = state;
        _storage.ConflictResolved += OnConflict;
        _storage.Merged += OnMerged;
        _storage.SaveRejected += OnRejected;
        _storage.SaveFailed += OnSaveFailed;
        _store.SessionChanged += OnStoreSessionChanged;
    }

    public OnlineSessionState State { get; private set; } = OnlineSessionState.Starting;
    public OnlineProfile? Profile { get; private set; }
    public string? Error { get; private set; }
    /// <summary>Message à afficher sur l'écran de connexion (retour de Google en erreur).</summary>
    public string? AuthMessage { get; set; }
    public AdminStatus Admin { get; private set; } = new();

    /// <summary>Changement d'état (connexion, chargement, alertes…).</summary>
    public event Action? Changed;

    public bool IsAdmin => Profile != null && Roles.IsAdmin(Profile.Role) && State == OnlineSessionState.Ready;

    public async Task InitializeAsync()
    {
        if (!_api.IsConfigured || !await _api.InitAsync())
        {
            SetState(OnlineSessionState.NotConfigured);
            return;
        }
        var retour = await _api.RedirectErrorAsync();
        if (!string.IsNullOrWhiteSpace(retour))
            AuthMessage = retour.Contains("access_denied", StringComparison.OrdinalIgnoreCase)
                ? L.T("Connexion avec Google annulée.")
                : L.F("La connexion avec Google a échoué : {0}", retour);
        var user = await _api.GetSessionAsync();
        if (user == null) SetState(OnlineSessionState.SignedOut);
        else await EnterAsync(user);
    }

    /// <summary>« Continuer avec Google » : la page part vers Google, puis revient connectée.</summary>
    public async Task<string?> SignInWithGoogleAsync()
    {
        var res = await _api.SignInWithGoogleAsync();
        return res.Ok ? null : L.F("La connexion avec Google a échoué : {0}", res.Error ?? "");
    }

    public Task<bool> GoogleActifAsync() => _api.GoogleActifAsync();

    public async Task<string?> SignInAsync(string email, string password)
    {
        var res = await _api.SignInAsync(email, password);
        if (!res.Ok) return L.T(res.Error ?? "Connexion impossible.");
        var user = await _api.GetSessionAsync();
        if (user == null) return L.T("Connexion impossible.");
        await EnterAsync(user);
        return null;
    }

    /// <summary>Inscription : le compte reste en attente jusqu'à la validation par un administrateur.</summary>
    public async Task<string?> SignUpAsync(string email, string password, string prenom, string nom, string telephone)
    {
        var res = await _api.SignUpAsync(email, password, prenom, nom, telephone);
        if (!res.Ok) return res.Error ?? L.T("Inscription impossible.");
        if (!res.HasSession)
            return L.T("Inscription enregistrée, mais la confirmation par e-mail est activée dans Supabase. Désactivez-la (voir le guide de mise en ligne) pour que la validation se fasse par l'administrateur.");
        var user = await _api.GetSessionAsync();
        if (user != null) await EnterAsync(user);
        return null;
    }

    /// <summary>
    /// Inscription sans adresse e-mail (script SQL 0005) : le serveur crée le compte
    /// avec une adresse technique, puis le membre est connecté. Après validation,
    /// il se connecte avec l'identifiant de sa fiche.
    /// </summary>
    public async Task<string?> SignUpSansEmailAsync(string prenom, string nom, string telephone, string password)
    {
        var res = await _api.RpcAsync<string>("inscription_sans_email",
            new { p_prenom = prenom.Trim(), p_nom = nom.Trim(), p_telephone = telephone.Trim(), p_password = password });
        if (!res.Ok || string.IsNullOrEmpty(res.Data))
        {
            var m = res.Error ?? "";
            if (m.Contains("trop_de_demandes", StringComparison.Ordinal))
                return L.T("Trop d'inscriptions sans e-mail en ce moment. Réessayez dans une heure.");
            if (m.Contains("inscription_sans_email", StringComparison.Ordinal) || m.Contains("schema cache", StringComparison.OrdinalIgnoreCase))
                return L.T("L'inscription sans e-mail n'est pas encore activée. Demandez à l'administrateur, ou inscrivez-vous avec une adresse e-mail.");
            return string.IsNullOrEmpty(m) ? L.T("Inscription impossible.") : L.T(m);
        }
        var login = await _api.SignInAsync(res.Data, password);
        if (!login.Ok)
            return L.T("Votre demande est enregistrée. Après la validation par l'administrateur, connectez-vous avec l'identifiant qu'il vous donnera.");
        var user = await _api.GetSessionAsync();
        if (user != null) await EnterAsync(user);
        return null;
    }

    /// <summary>Le membre en attente complète ou corrige son prénom, son nom et son téléphone.</summary>
    public async Task<string?> CompleteProfileAsync(string prenom, string nom, string telephone)
    {
        var res = await _api.RpcAsync<object>("complete_my_profile",
            new { p_prenom = prenom.Trim(), p_nom = nom.Trim(), p_telephone = telephone.Trim() });
        if (!res.Ok)
            return res.Error!.Contains("complete_my_profile", StringComparison.Ordinal)
                ? L.T("Cette fonction n'est pas encore activée. Donnez vos informations directement à l'administrateur.")
                : L.T(res.Error);
        if (Profile != null)
        {
            Profile.Prenom = prenom.Trim();
            Profile.Nom = nom.Trim();
            Profile.Telephone = telephone.Trim();
            Changed?.Invoke();
        }
        return null;
    }

    /// <summary>Vérifie à nouveau le statut du compte (bouton « Actualiser » de l'écran d'attente).</summary>
    public async Task RecheckAsync()
    {
        var user = await _api.GetSessionAsync();
        if (user == null) SetState(OnlineSessionState.SignedOut);
        else await EnterAsync(user);
    }

    public async Task SignOutAsync()
    {
        if (_signingOut) return;
        _signingOut = true;
        try
        {
            StopTimer();
            if (_store.IsAuthenticated) _store.Logout();
            await _storage.FlushAsync();
            await _api.SignOutAsync();
            _storage.Clear();
            Profile = null;
            Admin = new AdminStatus();
            _state.ResetNavigation();
            SetState(OnlineSessionState.SignedOut);
        }
        finally
        {
            _signingOut = false;
        }
    }

    /// <summary>Le membre a besoin de choisir son propre mot de passe (provisoire donné par un administrateur).</summary>
    public bool MustChangePassword => State == OnlineSessionState.Ready && Profile?.MdpProvisoire == true;

    public async Task<string?> ChangePasswordAsync(string password)
    {
        var res = await _api.UpdatePasswordAsync(password);
        if (!res.Ok) return res.Error ?? L.T("Modification impossible.");
        // Le mot de passe n'est plus provisoire (sans effet si le script SQL 0003 n'est pas installé).
        await _api.RpcAsync<object>("password_changed");
        if (Profile != null && Profile.MdpProvisoire)
        {
            Profile.MdpProvisoire = false;
            Changed?.Invoke();
        }
        return null;
    }

    /// <summary>
    /// « Mot de passe oublié » depuis l'écran de connexion : la demande est
    /// transmise aux administrateurs, qui donnent un mot de passe provisoire.
    /// Le membre saisit son e-mail ou son identifiant (script SQL 0005).
    /// </summary>
    public async Task<string?> RequestPasswordHelpAsync(string emailOuIdentifiant)
    {
        var res = await _api.RpcAsync<object>("request_password_help", new { p_email = emailOuIdentifiant.Trim() });
        return res.Ok ? null : L.T("La demande n'a pas pu être envoyée. Contactez directement un administrateur de la communauté.");
    }

    /// <summary>Met à jour le résumé administrateur (badges et bandeau de saturation).</summary>
    public async Task RefreshAdminStatusAsync()
    {
        if (State != OnlineSessionState.Ready || Profile == null || !Roles.IsAdmin(Profile.Role) && Profile.Role != Roles.Gestionnaire)
            return;
        var res = await _api.RpcAsync<AdminStatusDto>("capacity_status");
        _lastAdminStatus = DateTime.UtcNow;
        if (res.Data == null) return;
        Admin = new AdminStatus
        {
            OpenAlerts = res.Data.open_alerts,
            Level = res.Data.level ?? "ok",
            PendingAccounts = res.Data.pending_accounts,
            PasswordRequests = res.Data.password_requests
        };
        Changed?.Invoke();
    }

    private sealed class AdminStatusDto
    {
        public int open_alerts { get; set; }
        public string? level { get; set; }
        public int pending_accounts { get; set; }
        public int password_requests { get; set; }
    }

    private async Task EnterAsync(OnlineAuthUser user)
    {
        SetState(OnlineSessionState.Loading);
        var profile = await _api.RpcAsync<OnlineProfile>("my_profile");
        if (!profile.Ok || profile.Data == null)
        {
            Error = profile.Error ?? L.T("Profil introuvable. Le schéma Supabase est-il installé ?");
            SetState(OnlineSessionState.Error);
            return;
        }
        Profile = profile.Data;
        switch (Profile.Statut)
        {
            case OnlineStatuts.EnAttente: SetState(OnlineSessionState.Pending); return;
            case OnlineStatuts.Refuse: SetState(OnlineSessionState.Refused); return;
            case OnlineStatuts.Desactive: SetState(OnlineSessionState.Disabled); return;
        }

        await _api.OpenCacheAsync(user.Id);
        var loadError = await _storage.LoadAllAsync();
        if (loadError != null)
        {
            Error = loadError;
            SetState(OnlineSessionState.Error);
            return;
        }

        // Les écritures automatiques du chargement (normalisations) ne
        // doivent pas afficher d'erreur à un membre sans droit d'écriture.
        _storage.Quiet = true;
        try
        {
            if (!_storeInitialized)
            {
                _store.EnsureInitialized();
                _storeInitialized = true;
            }
            else
            {
                _store.Reload();
            }

            var record = _store.Users.FirstOrDefault(u => u.Id == user.Id);
            if (record == null && Roles.IsAdmin(Profile.Role))
            {
                // Premier administrateur : sa fiche membre est créée automatiquement.
                _store.UpsertOnlineMember(user.Id, Profile.Prenom, Profile.Nom, Profile.Email, Profile.Telephone, Profile.Role);
                record = _store.Users.FirstOrDefault(u => u.Id == user.Id);
            }
            if (record == null)
            {
                SetState(OnlineSessionState.NoMemberRecord);
                return;
            }
            // Le rôle du serveur fait foi (droits appliqués côté Supabase).
            record.Role = Profile.Role;

            var started = _store.StartOnlineSession(user.Id);
            if (!started.Success)
            {
                Error = started.Error;
                SetState(OnlineSessionState.Error);
                return;
            }
            await _storage.FlushAsync();
        }
        finally
        {
            _storage.Quiet = false;
        }

        SetState(OnlineSessionState.Ready);
        StartTimer();
        await RefreshAdminStatusAsync();
    }

    private void StartTimer()
    {
        StopTimer();
        _timer = new System.Threading.Timer(_ => _ = TickAsync(), null, RefreshEvery, RefreshEvery);
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Récupère régulièrement les modifications faites par les autres membres.</summary>
    private async Task TickAsync()
    {
        if (State != OnlineSessionState.Ready || _refreshing) return;
        _refreshing = true;
        try
        {
            if (await _storage.RefreshAsync()) ReloadStore();
            if (DateTime.UtcNow - _lastAdminStatus > AdminStatusEvery) await RefreshAdminStatusAsync();
        }
        catch
        {
            // Réseau indisponible : nouvel essai au prochain passage.
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ReloadStore()
    {
        _storage.Quiet = true;
        try { _store.Reload(); }
        finally { _storage.Quiet = false; }
        Changed?.Invoke();
    }

    private void OnConflict(string key)
    {
        ReloadStore();
        _state.NotifyWarning(L.T("Un autre membre vient de modifier ces données. Elles ont été mises à jour : vérifiez et refaites votre dernière modification si besoin."));
    }

    private void OnMerged(string key) => ReloadStore();

    private void OnRejected(string key, string message)
    {
        ReloadStore();
        if (!string.IsNullOrEmpty(message)) _state.NotifyError(message);
    }

    private void OnSaveFailed(string message)
    {
        if (DateTime.UtcNow - _lastNetworkError < TimeSpan.FromMinutes(1)) return;
        _lastNetworkError = DateTime.UtcNow;
        _state.NotifyWarning(L.F("{0} Nouvel essai automatique dans quelques secondes.", message));
    }

    private void OnStoreSessionChanged()
    {
        // Déconnexion depuis le menu ou après inactivité : on ferme aussi la session Supabase.
        if (State == OnlineSessionState.Ready && !_signingOut && !_store.IsAuthenticated)
            _ = SignOutAsync();
    }

    private void SetState(OnlineSessionState state)
    {
        State = state;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        StopTimer();
        _storage.ConflictResolved -= OnConflict;
        _storage.Merged -= OnMerged;
        _storage.SaveRejected -= OnRejected;
        _storage.SaveFailed -= OnSaveFailed;
        _store.SessionChanged -= OnStoreSessionChanged;
    }
}
