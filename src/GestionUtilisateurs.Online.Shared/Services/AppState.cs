namespace GestionUtilisateurs.Shared.Services;

/// <summary>Navigation interne (pas de routeur) et notifications toast.</summary>
public class AppState
{
    private string _currentPage = "dashboard";
    private readonly List<string> _history = new();

    public string CurrentPage => _currentPage;

    /// <summary>Indique si une page précédente (ou un écran interne à fermer) existe :
    /// affiche le bouton retour et active le geste « retour » du système.</summary>
    public bool HasHistory => _history.Count > 0 || _retourSousVue != null;

    // Écran interne d'une page (fiche d'un membre, formulaire, module du guide,
    // conversation…) : le retour le ferme avant de quitter la page.
    private object? _sousVueProprietaire;
    private Action? _retourSousVue;

    /// <summary>Déclare (ou retire, avec null) l'écran interne ouvert par une page :
    /// le prochain retour appellera <paramref name="retour"/> au lieu de changer de page.</summary>
    public void SetSousVue(object proprietaire, Action? retour)
    {
        if (retour == null)
        {
            if (!ReferenceEquals(_sousVueProprietaire, proprietaire) || _retourSousVue == null) return;
            _sousVueProprietaire = null;
            _retourSousVue = null;
        }
        else
        {
            if (ReferenceEquals(_sousVueProprietaire, proprietaire) && Equals(_retourSousVue, retour)) return;
            _sousVueProprietaire = proprietaire;
            _retourSousVue = retour;
        }
        SousVueChanged?.Invoke();
    }

    private void OublierSousVue()
    {
        _sousVueProprietaire = null;
        _retourSousVue = null;
    }

    public event Action? PageChanged;

    /// <summary>Un écran interne s'ouvre ou se ferme (le bouton retour peut apparaître ou disparaître).</summary>
    public event Action? SousVueChanged;
    public event Action<string, string>? Toast;
    public event Action<string, string, string?, Func<Task>?>? ToastAction;
    public event Action? SearchChanged;
    public event Action? TourRequested;

    /// <summary>Demande le lancement du mini-guide de démarrage (depuis le menu, les paramètres…).</summary>
    public void RequestTour() => TourRequested?.Invoke();

    public void Navigate(string page)
    {
        if (_currentPage == page) return;
        OublierSousVue();
        // Si la page est déjà dans la pile, on dépile jusqu'à elle au lieu
        // d'empiler un doublon : le retour reste page par page, sans détour,
        // et la sortie de l'app ne demande jamais une longue série de gestes.
        var existing = _history.FindLastIndex(p => p.Equals(page, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
            _history.RemoveRange(existing, _history.Count - existing);
        else
            _history.Add(_currentPage);
        _currentPage = page;
        PageChanged?.Invoke();
    }

    /// <summary>Revient à la page précédente (geste de balayage vers la gauche ou la droite).
    /// Depuis la carte de membre, renvoie vers la fiche de l'utilisateur.</summary>
    public void GoBack()
    {
        if (_retourSousVue != null)
        {
            var retour = _retourSousVue;
            OublierSousVue();
            retour();
            SousVueChanged?.Invoke();
            return;
        }
        if (_history.Count == 0) return;
        if (_currentPage == "carte")
        {
            if (!string.IsNullOrWhiteSpace(_carteReturnIdent) && string.IsNullOrEmpty(_pendingUserId))
                _pendingUserIdent = _carteReturnIdent;
            _carteReturnIdent = null;
            _pendingCarteIdent = null;
        }
        _currentPage = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        PageChanged?.Invoke();
    }

    /// <summary>Efface l'historique de navigation (connexion/déconnexion).</summary>
    public void ResetNavigation()
    {
        _history.Clear();
        OublierSousVue();
        _currentPage = "dashboard";
        PageChanged?.Invoke();
    }

    /// <summary>Démarre sur la page d'accueil de la session sans empiler l'écran de connexion.</summary>
    public void GoToInitial(string page)
    {
        _history.Clear();
        OublierSousVue();
        _currentPage = page;
        PageChanged?.Invoke();
    }

    private string? _pendingUserId;

    /// <summary>Ouvre directement la fiche d'un utilisateur (depuis le tableau de bord).</summary>
    public void NavigateToUser(string id)
    {
        _pendingUserId = id;
        Navigate("users");
    }

    public string? PendingUserId => _pendingUserId;

    public void ConsumePendingUser() => _pendingUserId = null;

    private string? _pendingUserIdent;

    /// <summary>Ouvre la fiche d'un utilisateur à partir de son identifiant (retour depuis la carte).</summary>
    public string? PendingUserIdent => _pendingUserIdent;

    public void ConsumePendingUserIdent() => _pendingUserIdent = null;

    private string? _pendingCarteIdent;

    /// <summary>Ouvre la carte de membre d'un utilisateur donné (depuis sa fiche).</summary>
    public void NavigateToCarte(string identifiant)
    {
        _carteReturnIdent = identifiant;
        _pendingCarteIdent = identifiant;
        Navigate("carte");
    }

    public string? PendingCarteIdent => _pendingCarteIdent;

    public void ConsumePendingCarte() => _pendingCarteIdent = null;

    private string? _carteReturnIdent;

    private string _globalSearch = "";
    public string GlobalSearch => _globalSearch;

    public void SetGlobalSearch(string query)
    {
        _globalSearch = query;
        SearchChanged?.Invoke();
    }

    private string? _usersStatusFilter;
    private string? _usersRoleFilter;

    public string? UsersStatusFilter => _usersStatusFilter;
    public string? UsersRoleFilter => _usersRoleFilter;

    /// <summary>Navigue vers la liste des utilisateurs avec des filtres (statut et/ou rôle).</summary>
    public void NavigateWithUsersFilter(string? status, string? role)
    {
        _usersStatusFilter = status;
        _usersRoleFilter = role;
        Navigate("users");
    }

    /// <summary>Consomme puis efface les filtres transmis par le tableau de bord.</summary>
    public void ConsumeUsersFilter()
    {
        _usersStatusFilter = null;
        _usersRoleFilter = null;
    }

    public void Notify(string message, string type)
        => Toast?.Invoke(message, type);

    public void NotifySuccess(string message) => Toast?.Invoke(message, "success");
    public void NotifyError(string message) => Toast?.Invoke(message, "error");
    public void NotifyWarning(string message) => Toast?.Invoke(message, "warning");
    public void NotifyInfo(string message) => Toast?.Invoke(message, "info");

    /// <summary>Notification avec une action cliquable (ex : ouvrir le dossier local).</summary>
    public void NotifyAction(string message, string type, string actionLabel, Func<Task>? action)
    {
        if (action == null) { Notify(message, type); return; }
        ToastAction?.Invoke(message, type, actionLabel, action);
    }
}