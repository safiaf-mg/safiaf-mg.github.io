using GestionUtilisateurs.Shared.Models;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Blazor.Tests;

public class DataStoreSansEmailTests
{
    private const string Technique = "m-6f1c2d3e-0000-4000-8000-000000000001@sans-email.invalid";

    private static DataStore NewAdminStore()
    {
        var store = new DataStore(new MemoryStorageProvider());
        store.EnsureInitialized();
        Assert.True(store.Login("admin", "admin123").Success);
        return store;
    }

    [Theory]
    [InlineData(Technique, true)]
    [InlineData(" M-1@SANS-EMAIL.INVALID ", true)]
    [InlineData("fara@gmail.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AdresseTechnique_Reconnue(string? email, bool attendu)
    {
        Assert.Equal(attendu, SansEmail.EstTechnique(email));
        Assert.Equal(attendu ? "" : (email ?? "").Trim(), SansEmail.Reelle(email));
    }

    [Fact]
    public void ValidationSansEmail_FicheSansAdresseTechnique()
    {
        var store = NewAdminStore();
        var res = store.UpsertOnlineMember("6f1c2d3e-0000-4000-8000-000000000001", "Fara", "Rasoa", Technique, "034 12 345 67", Roles.Utilisateur);

        Assert.True(res.Success, res.Error);
        var fiche = store.FindUser(res.UserId!)!;
        Assert.Equal("", fiche.Email);
        Assert.Equal("034 12 345 67", fiche.Telephone);
        Assert.False(string.IsNullOrWhiteSpace(fiche.Identifiant));
    }

    [Fact]
    public void ValidationSansEmail_RelieeAUneFiche_GardeSonEmailVide()
    {
        var store = NewAdminStore();
        var ajout = store.AddUser(new UserAccount { Prenom = "Fara", Nom = "RASOA", Role = Roles.Utilisateur, Actif = true, Telephone = "0341234567" });
        Assert.True(ajout.Success, ajout.Error);

        var proches = store.FichesPourInscription("Fara", "Rasoa", Technique, "034 12 345 67");
        var proche = Assert.Single(proches);
        Assert.DoesNotContain("e-mail", proche.Raison);

        var res = store.UpsertOnlineMember("6f1c2d3e-0000-4000-8000-000000000001", "Fara", "Rasoa", Technique, "", Roles.Utilisateur, ajout.UserId);
        Assert.True(res.Success, res.Error);
        Assert.Equal("", store.FindUser(res.UserId!)!.Email);
    }
}
