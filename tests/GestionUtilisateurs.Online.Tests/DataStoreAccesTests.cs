using GestionUtilisateurs.Shared.Models;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Blazor.Tests;

public class DataStoreAccesTests
{
    private static DataStore NewAdminStore()
    {
        var store = new DataStore(new MemoryStorageProvider());
        store.EnsureInitialized();
        Assert.True(store.Login("admin", "admin123").Success);
        return store;
    }

    private static void Retirer(DataStore store, string role, params string[] permissions)
    {
        var sets = store.RolePermissions.Select(s => new RolePermissionSet
        {
            Role = s.Role,
            Allowed = new Dictionary<string, bool>(s.Allowed)
        }).ToList();
        foreach (var p in permissions) sets.First(s => s.Role == role).Allowed[p] = false;
        Assert.True(store.SaveRolePermissions(sets).Success);
    }

    [Fact]
    public void Membre_SansGestionDocumentaire_NeVoitPasLaPage()
    {
        var store = NewAdminStore();
        Retirer(store, Roles.Utilisateur, Permissions.DocumentsUse, Permissions.JournalMediaUse);
        Assert.True(store.CommencerApercu(Roles.Utilisateur).Success);

        Assert.False(store.PeutOuvrir("documents"));
        Assert.False(store.PeutOuvrir("journal"));
        Assert.True(store.PeutOuvrir("calendar"));
        Assert.True(store.PeutOuvrir("votes"));
        Assert.True(store.PeutOuvrir("guide"));
    }

    [Fact]
    public void Membre_ParDefaut_NiAdministrationNiListeDesMembres()
    {
        var store = NewAdminStore();
        Assert.True(store.CommencerApercu(Roles.Utilisateur).Success);

        foreach (var page in new[] { "activity", "archive", "fields", "comptes", "roles", "data", "capacite", "finances", "materiel", "reunions", "situations" })
            Assert.False(store.PeutOuvrir(page), page);
        Assert.False(store.VoitListeMembres);
    }

    [Fact]
    public void Gestionnaire_ParDefaut_ActiviteEtListe_MaisPasRolesNiArchives()
    {
        var store = NewAdminStore();
        Assert.True(store.CommencerApercu(Roles.Gestionnaire).Success);

        Assert.True(store.PeutOuvrir("activity"));
        Assert.True(store.VoitListeMembres);
        Assert.False(store.PeutOuvrir("roles"));
        Assert.False(store.PeutOuvrir("archive"));
        Assert.False(store.PeutOuvrir("capacite"));
    }

    [Fact]
    public void Administrateur_OuvreTout()
    {
        var store = NewAdminStore();
        foreach (var page in new[] { "documents", "activity", "archive", "fields", "comptes", "roles", "data", "capacite" })
            Assert.True(store.PeutOuvrir(page), page);
    }
}
