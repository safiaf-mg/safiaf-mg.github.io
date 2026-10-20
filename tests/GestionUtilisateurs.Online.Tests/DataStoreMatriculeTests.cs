using GestionUtilisateurs.Shared.Models;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Blazor.Tests;

public class DataStoreMatriculeTests
{
    private static DataStore NewStore()
    {
        var store = new DataStore(new MemoryStorageProvider());
        store.EnsureInitialized();
        return store;
    }

    private static void LoginAdmin(DataStore store)
    {
        Assert.True(store.Login("admin", "admin123").Success);
    }

    [Fact]
    public void Matricule_AjoutEtUnicite()
    {
        var store = NewStore();
        LoginAdmin(store);
        Assert.True(store.AddUser(new UserAccount
        {
            Prenom = "Jean", Nom = "PAUL", Identifiant = "jpaul",
            Role = Roles.Utilisateur, Actif = true, MotDePasse = "secret123",
            Matricule = "M-0001"
        }).Success);

        var doublon = store.AddUser(new UserAccount
        {
            Prenom = "Marie", Nom = "DURAND", Identifiant = "mdurand",
            Role = Roles.Utilisateur, Actif = true, MotDePasse = "secret123",
            Matricule = "m-0001"
        });
        Assert.False(doublon.Success);
        Assert.Contains("matricule", doublon.Error.ToLowerInvariant());
    }

    [Fact]
    public void Matricule_ValidationEtModification()
    {
        var store = NewStore();
        LoginAdmin(store);
        Assert.True(store.AddUser(new UserAccount
        {
            Prenom = "A", Nom = "B", Identifiant = "ab",
            Role = Roles.Utilisateur, Actif = true, MotDePasse = "secret123",
            Matricule = "M-1"
        }).Success);
        Assert.True(store.AddUser(new UserAccount
        {
            Prenom = "C", Nom = "D", Identifiant = "cd",
            Role = Roles.Utilisateur, Actif = true, MotDePasse = "secret123"
        }).Success);

        // Validation directe : doublon détecté avec la clé « matricule ».
        var draft = new UserAccount
        {
            Prenom = "C", Nom = "D", Identifiant = "cd",
            Role = Roles.Utilisateur, Actif = true, Matricule = "M-1"
        };
        var errors = store.ValidateUser(draft, store.FindUserByIdentifier("cd")!.Id, false);
        Assert.True(errors.ContainsKey("matricule"));

        // Modification vers un matricule libre : OK.
        var target = store.FindUserByIdentifier("cd")!;
        var update = new UserAccount
        {
            Id = target.Id, Prenom = target.Prenom, Nom = target.Nom, Identifiant = target.Identifiant,
            Email = target.Email, Telephone = target.Telephone, Role = target.Role, Actif = target.Actif,
            Notes = target.Notes, Champs = target.Champs, Photo = target.Photo, Matricule = "M-2"
        };
        Assert.True(store.UpdateUser(target.Id, update, false).Success);
        Assert.Equal("M-2", store.FindUserByIdentifier("cd")!.Matricule);

        // Modification vers un matricule pris : refusé.
        update.Matricule = "M-1";
        Assert.False(store.UpdateUser(target.Id, update, false).Success);
    }

    [Fact]
    public void Matricule_ExcelEtSynchro()
    {
        var store = NewStore();
        LoginAdmin(store);
        Assert.True(store.AddUser(new UserAccount
        {
            Prenom = "E", Nom = "F", Identifiant = "ef",
            Role = Roles.Utilisateur, Actif = true, MotDePasse = "secret123",
            Matricule = "M-9"
        }).Success);

        // Excel : colonne Matricule exportée puis réimportée.
        var bytes = store.ExportUsersExcel();
        var fresh = NewStore();
        var parsed = fresh.ParseExcelImport(bytes);
        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal("M-9", Assert.Single(parsed.Users!, u => u.Identifiant == "ef").Matricule);

        // Synchro : le matricule voyage avec la fiche.
        var other = NewStore();
        var json = store.BuildSyncPackageJson(new SyncOptions(), "A");
        var (package, error) = other.ParseSyncPackage(json);
        Assert.Null(error);
        var preview = other.PreviewSyncImport(package!);
        var resolutions = preview.Conflicts
            .Select(c => new SyncResolution { Collection = c.Collection, Key = c.Key, UseIncoming = true })
            .ToList();
        Assert.True(other.ApplySyncImport(package!, resolutions).Success);
        Assert.Equal("M-9", other.FindUserByIdentifier("ef")!.Matricule);
    }

    [Fact]
    public void CarteMembre_CodeSecours()
    {
        var store = NewStore();
        var admin = store.FindUserByIdentifier("admin")!;
        Assert.StartsWith("GU-", DataStore.CodeMembre(admin));
    }

    [Fact]
    public void Matricule_SaisieGuideeParLeFormat()
    {
        var parties = DataStore.DecouperFormatMatricule("M-{NNNN}");
        Assert.Equal(2, parties.Count);
        Assert.Equal("M-", parties[0].Fixe);
        Assert.Equal(4, parties[1].Longueur);

        // Pas plus de 4 chiffres, et rien d'autre que des chiffres.
        Assert.Equal("1234", DataStore.NettoyerSaisieMatricule(parties[1], "12a345"));
        Assert.Equal("M-0007", DataStore.ComposerMatricule(parties, new[] { "7" }));
        Assert.Equal("", DataStore.ComposerMatricule(parties, new[] { "" }));
        Assert.Equal(new[] { "0042" }, DataStore.SaisiesMatricule(parties, "M-0042"));
        Assert.Null(DataStore.SaisiesMatricule(parties, "ANCIEN-12"));

        var multi = DataStore.DecouperFormatMatricule("CJ{AA}-{INI}{NNN}");
        Assert.Equal(new[] { "CJ", "", "-", "", "" }, multi.Select(p => p.Fixe));
        Assert.Equal("CJ26-RA007", DataStore.ComposerMatricule(multi, new[] { "26", "ra", "7" }));

        Assert.Null(DataStore.VerifierMatriculeFormat("M-{NNNN}", "M-0042"));
        Assert.NotNull(DataStore.VerifierMatriculeFormat("M-{NNNN}", "M-42"));
        Assert.NotNull(DataStore.VerifierMatriculeFormat("{AAAA}/{MM}-{NN}", "2026/13-01"));
        Assert.Null(DataStore.VerifierMatriculeFormat("", "libre"));
    }

    [Fact]
    public void Matricule_HorsFormatRefuseSaufAncienInchange()
    {
        var store = NewStore();
        LoginAdmin(store);
        Assert.True(store.AddUser(new UserAccount
        {
            Prenom = "A", Nom = "B", Identifiant = "ab",
            Role = Roles.Utilisateur, Actif = true, MotDePasse = "secret123",
            Matricule = "ANCIEN-1"
        }).Success);
        var fiche = store.FindUserByIdentifier("ab")!;
        var s = store.Settings;
        s.MatriculeFormat = "M-{NNNN}";
        store.SaveSettings(s);

        var draft = new UserAccount { Prenom = "C", Nom = "D", Role = Roles.Utilisateur, Matricule = "X-12" };
        Assert.True(store.ValidateUser(draft, null, true).ContainsKey("matricule"));
        draft.Matricule = "M-0012";
        Assert.False(store.ValidateUser(draft, null, true).ContainsKey("matricule"));

        // L'ancien matricule d'une fiche reste accepté tant qu'on ne le change pas.
        var edit = new UserAccount { Prenom = "A", Nom = "B", Role = Roles.Utilisateur, Matricule = "ANCIEN-1" };
        Assert.False(store.ValidateUser(edit, fiche.Id, false).ContainsKey("matricule"));
        edit.Matricule = "ANCIEN-2";
        Assert.True(store.ValidateUser(edit, fiche.Id, false).ContainsKey("matricule"));
    }

    [Fact]
    public void Identifiant_NestPasUnChampPersonnalise()
    {
        var store = NewStore();
        Assert.Contains(store.Fields, f => f.Id == FieldDefinition.FIELD_IDENTIFIANT);
        Assert.DoesNotContain(store.ChampsPersonnalises, f => f.Id == FieldDefinition.FIELD_IDENTIFIANT);
    }
}
