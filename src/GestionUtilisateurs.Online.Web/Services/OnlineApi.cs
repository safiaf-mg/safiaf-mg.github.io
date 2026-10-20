using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using GestionUtilisateurs.Shared.Models;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Online.Web.Services;

/// <summary>Adresse du projet Supabase et clé publique « anon » (wwwroot/appsettings.json).</summary>
public sealed class SupabaseOptions
{
    public string Url { get; set; } = "";
    public string AnonKey { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(AnonKey);
}

public sealed class OnlineAuthUser
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class OnlineResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public bool HasSession { get; set; }
}

/// <summary>Profil d'un compte en ligne (table public.profiles).</summary>
public sealed class OnlineProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("prenom")] public string Prenom { get; set; } = "";
    [JsonPropertyName("nom")] public string Nom { get; set; } = "";
    [JsonPropertyName("telephone")] public string Telephone { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("statut")] public string Statut { get; set; } = "";
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("validated_at")] public DateTimeOffset? ValidatedAt { get; set; }
    [JsonPropertyName("last_seen_at")] public DateTimeOffset? LastSeenAt { get; set; }
    /// <summary>Mot de passe oublié : date de la demande envoyée depuis l'écran de connexion.</summary>
    [JsonPropertyName("mdp_demande_at")] public DateTimeOffset? MdpDemandeAt { get; set; }
    /// <summary>Mot de passe donné par un administrateur : le membre doit le changer.</summary>
    [JsonPropertyName("mdp_provisoire")] public bool MdpProvisoire { get; set; }

    public string NomComplet => (Nom.ToUpperInvariant() + " " + Prenom).Trim();
    /// <summary>Compte créé sans adresse e-mail : l'adresse technique n'est jamais affichée.</summary>
    public bool EstSansEmail => SansEmail.EstTechnique(Email);
    public string EmailAffiche => EstSansEmail ? L.T("Sans e-mail") : Email;
    public bool IsActive => Statut == OnlineStatuts.Actif;
}

public static class OnlineStatuts
{
    public const string EnAttente = "en_attente";
    public const string Actif = "actif";
    public const string Refuse = "refuse";
    public const string Desactive = "desactive";

    public static string Label(string statut) => statut switch
    {
        EnAttente => L.T("En attente"),
        Actif => L.T("Actif"),
        Refuse => L.T("Refusé"),
        Desactive => L.T("Désactivé"),
        _ => statut
    };
}

public sealed class OnlineRowsResult
{
    public List<OnlineRow> Rows { get; set; } = new();
    public List<string> Removed { get; set; } = new();
    public string? Error { get; set; }
}

public sealed class OnlineRow
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public long Version { get; set; }
}

public sealed class OnlineSaveResult
{
    public string Status { get; set; } = "";
    public long Version { get; set; }
    public string? Value { get; set; }
    public string? Message { get; set; }
}

public sealed class RpcResult<T>
{
    public T? Data { get; init; }
    public string? Error { get; init; }
    public bool Ok => Error == null;
}

/// <summary>Appels vers Supabase via wwwroot/js/online.js.</summary>
public class OnlineApi
{
    private sealed class RawRpc
    {
        public string? Data { get; set; }
        public string? Error { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IJSRuntime _js;
    private readonly SupabaseOptions _options;
    private bool _initialized;

    public OnlineApi(IJSRuntime js, SupabaseOptions options)
    {
        _js = js;
        _options = options;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<bool> InitAsync()
    {
        if (_initialized) return true;
        if (!_options.IsConfigured) return false;
        _initialized = await _js.InvokeAsync<bool>("guOnline.init", _options.Url, _options.AnonKey);
        return _initialized;
    }

    public Task<OnlineAuthUser?> GetSessionAsync() => _js.InvokeAsync<OnlineAuthUser?>("guOnline.getSession").AsTask();

    public Task<OnlineResult> SignInAsync(string email, string password)
        => _js.InvokeAsync<OnlineResult>("guOnline.signIn", email.Trim(), password).AsTask();

    public Task<OnlineResult> SignUpAsync(string email, string password, string prenom, string nom, string telephone)
        => _js.InvokeAsync<OnlineResult>("guOnline.signUp", email.Trim(), password, prenom.Trim(), nom.Trim(), telephone.Trim()).AsTask();

    /// <summary>Connexion avec Google : la page part vers Google puis revient sur le site.</summary>
    public Task<OnlineResult> SignInWithGoogleAsync() => _js.InvokeAsync<OnlineResult>("guOnline.signInGoogle").AsTask();

    /// <summary>Le fournisseur Google est-il activé dans Supabase ?</summary>
    public async Task<bool> GoogleActifAsync()
    {
        try { return await _js.InvokeAsync<bool>("guOnline.googleActif"); }
        catch (JSException) { return false; }
    }

    /// <summary>Erreur rapportée au retour de Google (connexion annulée…), une seule fois.</summary>
    public async Task<string?> RedirectErrorAsync()
    {
        try { return await _js.InvokeAsync<string?>("guOnline.redirectError"); }
        catch (JSException) { return null; }
    }

    public Task SignOutAsync() => _js.InvokeVoidAsync("guOnline.signOut").AsTask();

    public Task<OnlineResult> UpdatePasswordAsync(string password)
        => _js.InvokeAsync<OnlineResult>("guOnline.updatePassword", password).AsTask();

    public async Task<RpcResult<T>> RpcAsync<T>(string name, object? args = null)
    {
        try
        {
            var raw = await _js.InvokeAsync<RawRpc>("guOnline.rpc", name, args ?? new { });
            if (raw.Error != null) return new RpcResult<T> { Error = Friendly(raw.Error) };
            var data = string.IsNullOrEmpty(raw.Data) ? default : JsonSerializer.Deserialize<T>(raw.Data, Json);
            return new RpcResult<T> { Data = data };
        }
        catch (JSException ex)
        {
            return new RpcResult<T> { Error = Friendly(ex.Message) };
        }
    }

    public Task<OnlineResult> RemoveFilesAsync(IEnumerable<string> names)
        => _js.InvokeAsync<OnlineResult>("guOnline.removeFiles", names.ToArray()).AsTask();

    public Task OpenCacheAsync(string userId) => _js.InvokeAsync<bool>("guOnline.openCache", userId).AsTask();

    public Task ClearCacheAsync() => _js.InvokeVoidAsync("guOnline.clearCache").AsTask();

    public async Task<OnlineRowsResult> LoadAsync(bool all)
    {
        try { return await _js.InvokeAsync<OnlineRowsResult>("guOnline.load", all); }
        catch (JSException ex) { return new OnlineRowsResult { Error = Friendly(ex.Message) }; }
    }

    public async Task<OnlineSaveResult> SaveAsync(string key, string json, long expectedVersion)
    {
        try { return await _js.InvokeAsync<OnlineSaveResult>("guOnline.save", key, json, expectedVersion); }
        catch (JSException ex) { return new OnlineSaveResult { Status = "error", Message = Friendly(ex.Message) }; }
    }

    private static string Friendly(string message)
    {
        if (message.Contains("Failed to fetch", StringComparison.OrdinalIgnoreCase)
            || message.Contains("NetworkError", StringComparison.OrdinalIgnoreCase))
            return L.T("Connexion au serveur impossible. Vérifiez votre connexion Internet.");
        return message;
    }
}
