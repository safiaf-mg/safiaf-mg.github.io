using Microsoft.JSInterop;
using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>Pont JS : thème, téléchargement de fichiers, stockage fichiers (IndexedDB).</summary>
public class JavascriptBridge
{
    private readonly IJSRuntime _js;

    public JavascriptBridge(IJSRuntime js) => _js = js;

    public async Task ApplySettingsAsync(AppSettings settings)
    {
        try
        {
            await _js.InvokeVoidAsync("guTheme.apply", settings.Theme, settings.Color, settings.AppName, settings.Logo, settings.CarteSlogan ?? "");
        }
        catch { /* hors navigateur (tests) : sans effet */ }
    }

    /// <summary>Signale que l'interface est prête : masque l'écran de chargement au terme des ~5 s.</summary>
    public Task NotifySplashReadyAsync() => InvokeAsync("guSplash.ready");

    public Task EnableSwipeBackAsync<T>(DotNetObjectReference<T> reference) where T : class
        => InvokeAsync("guNav.enable", reference);
    public Task DisableSwipeBackAsync()
        => InvokeAsync("guNav.disable");

    /// <summary>Geste « retour » du système (Android, navigateur) : voir guRetour dans app.js.</summary>
    public Task EnableSystemBackAsync<T>(DotNetObjectReference<T> reference) where T : class
        => InvokeAsync("guRetour.enable", reference);
    public Task SetHasPreviousPageAsync(bool hasPrevious)
        => InvokeAsync("guRetour.pagePrecedente", hasPrevious);
    public Task DisableSystemBackAsync()
        => InvokeAsync("guRetour.disable");

    /// <summary>Balayage horizontal dans l'histoire de la communauté (lecteur de chapitres).</summary>
    public Task EnableCommunitySwipeAsync<T>(DotNetObjectReference<T> reference) where T : class
        => InvokeAsync("guCommunity.enable", reference);
    public Task DisableCommunitySwipeAsync()
        => InvokeAsync("guCommunity.disable");

    public Task EnableActivityTrackingAsync<T>(DotNetObjectReference<T> reference) where T : class
        => InvokeAsync("guSession.enable", reference);

    public Task DisableActivityTrackingAsync()
        => InvokeAsync("guSession.disable");

    public Task LockBodyScrollAsync()
        => InvokeAsync("guModal.lock");

    public Task UnlockBodyScrollAsync()
        => InvokeAsync("guModal.unlock");

    /// <summary>Lance le mini-guide de démarrage (tour interactif Intro.js).</summary>
    public Task StartTourAsync<T>(DotNetObjectReference<T> reference, string stepsJson, string onStepChange, string onFinished) where T : class
        => InvokeAsync("guTour.start", reference, stepsJson, onStepChange, onFinished);

    public Task StopTourAsync()
        => InvokeAsync("guTour.stop");

    /// <summary>Fait défiler la page jusqu'à un identifiant d'élément (sommaire du guide).</summary>
    public Task ScrollToIdAsync(string id)
        => InvokeAsync("guUi.scrollToId", id);

    /// <summary>Replace l'interface en haut de page à chaque changement de menu.</summary>
    public Task ScrollToTopAsync()
        => InvokeAsync("guUi.scrollTop");

    public Task DownloadTextAsync(string filename, string content, string mime)
        => InvokeAsync("guDownload.text", filename, content, mime);

    public Task DownloadDataUrlAsync(string filename, string dataUrl)
        => InvokeAsync("guDownload.dataUrl", filename, dataUrl);

    public Task IdbPutAsync(string key, string base64, string mime, string fileName, long size)
        => InvokeAsync("guFile.put", key, base64, mime, fileName, size);

    public Task<string?> IdbGetDataUrlAsync(string key)
        => _js.InvokeAsync<string?>("guFile.getDataUrl", key).AsTask();

    public Task IdbDeleteAsync(string key)
        => InvokeAsync("guFile.remove", key);

    public Task IdbDeleteManyAsync(string[] keys)
        => InvokeAsync("guFile.removeMany", keys);

    public Task<long> IdbSizeAsync()
    {
        try { return _js.InvokeAsync<long>("guFile.size").AsTask(); }
        catch { return Task.FromResult(0L); }
    }

    public async Task<long> LocalStorageBytesAsync()
    {
        try { return await _js.InvokeAsync<long>("guInfo.localBytes"); }
        catch { return 0; }
    }

    public Task ClearFileInputAsync(string id)
        => InvokeAsync("guUi.clearInput", id);

    /// <summary>Signature électronique : initialise le pad de dessin (souris + tactile).</summary>
    public Task SignatureInitAsync(string canvasId)
        => InvokeAsync("guSignature.init", canvasId);

    public Task SignatureClearAsync(string canvasId)
        => InvokeAsync("guSignature.clear", canvasId);

    public Task SignatureDestroyAsync(string canvasId)
        => InvokeAsync("guSignature.destroy", canvasId);

    public async Task<bool> SignatureIsEmptyAsync(string canvasId)
    {
        try { return await _js.InvokeAsync<bool>("guSignature.isEmpty", canvasId); }
        catch { return true; }
    }

    public async Task<string?> SignatureGetDataUrlAsync(string canvasId)
    {
        try { return await _js.InvokeAsync<string?>("guSignature.toDataUrl", canvasId); }
        catch { return null; }
    }

    /// <summary>Scanner de QR code (feuille de présence) : « ok », « refuse », « non-supporte » ou « erreur ».</summary>
    public async Task<string> QrScanStartAsync<T>(string videoId, DotNetObjectReference<T> reference) where T : class
    {
        try { return await _js.InvokeAsync<string>("guQrScan.start", videoId, reference); }
        catch { return "erreur"; }
    }

    public Task QrScanStopAsync()
        => InvokeAsync("guQrScan.stop");

    private Task InvokeAsync(string ident, params object?[] args)
    {
        try { return _js.InvokeVoidAsync(ident, args).AsTask(); }
        catch { return Task.CompletedTask; }
    }
}
