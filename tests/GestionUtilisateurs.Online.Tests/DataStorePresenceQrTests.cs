using GestionUtilisateurs.Shared.Models;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Blazor.Tests;

public class DataStorePresenceQrTests
{
    private static DataStore NewStore()
    {
        var store = new DataStore(new MemoryStorageProvider());
        store.EnsureInitialized();
        Assert.True(store.Login("admin", "admin123").Success);
        Assert.True(store.AddUser(new UserAccount
        {
            Prenom = "Jean", Nom = "RAKOTO", Identifiant = "jrakoto",
            Role = Roles.Utilisateur, Actif = true, MotDePasse = "secret123",
            Matricule = "M-0007"
        }).Success);
        Assert.True(store.AddUser(new UserAccount
        {
            Prenom = "Marie", Nom = "RABE", Identifiant = "mrabe",
            Role = Roles.Utilisateur, Actif = true, MotDePasse = "secret123"
        }).Success);
        Assert.True(store.AddReunion(new Reunion
        {
            Titre = "Culte", Date = "2026-10-25", Participants = new List<string> { "mrabe" }
        }).Success);
        return store;
    }

    [Fact]
    public void QrDeLaCarte_RetrouveLeMembreParSonId()
    {
        var store = NewStore();
        var jean = store.FindUserByIdentifier("jrakoto")!;
        var qr = store.VCardMembre(jean);
        Assert.Contains("UID:" + jean.Id, qr);
        Assert.Same(jean, store.TrouverMembreParQr(qr));

        // L'identifiant change : l'Id porté par la carte imprimée reste valable.
        jean.Identifiant = "jean.rakoto";
        Assert.Same(jean, store.TrouverMembreParQr(qr));
    }

    [Fact]
    public void AncienneCarteSansUid_ReconnueParMatriculeTelephoneOuNom()
    {
        var store = NewStore();
        var jean = store.FindUserByIdentifier("jrakoto")!;
        var marie = store.FindUserByIdentifier("mrabe")!;
        marie.Telephone = "034 12 345 67";

        Assert.Same(jean, store.TrouverMembreParQr(
            "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:RAKOTO Jean\r\nNOTE:Membre M-0007 — Utilisateur\r\nEND:VCARD\r\n"));
        Assert.Same(marie, store.TrouverMembreParQr(
            "BEGIN:VCARD\nVERSION:3.0\nFN:Autre nom\nTEL;TYPE=CELL:+261 34 12 345 67\nNOTE:Utilisateur\nEND:VCARD"));
        Assert.Same(marie, store.TrouverMembreParQr(
            "BEGIN:VCARD\nVERSION:3.0\nFN:rabe marie\nNOTE:Utilisateur\nEND:VCARD"));

        // Texte simple : identifiant ou matricule.
        Assert.Same(jean, store.TrouverMembreParQr("M-0007"));
        Assert.Same(marie, store.TrouverMembreParQr("mrabe"));

        Assert.Null(store.TrouverMembreParQr("https://exemple.org"));
        Assert.Null(store.TrouverMembreParQr("BEGIN:VCARD\nFN:Inconnu\nEND:VCARD"));
        Assert.Null(store.TrouverMembreParQr(""));
    }

    [Fact]
    public void Pointage_MarquePresentEtAjouteLeParticipant()
    {
        var store = NewStore();
        var reunion = Assert.Single(store.Reunions);
        var jean = store.FindUserByIdentifier("jrakoto")!;

        var res = store.PointerPresenceParQr(reunion.Id, store.VCardMembre(jean));
        Assert.True(res.Succes, res.Message);
        Assert.False(res.DejaPointe);
        Assert.Same(jean, res.Membre);
        Assert.Contains("jrakoto", store.FindReunion(reunion.Id)!.Participants);
        Assert.Equal(PresenceStatuts.Present, store.FindReunion(reunion.Id)!.Presences["jrakoto"]);

        var encore = store.PointerPresenceParQr(reunion.Id, store.VCardMembre(jean));
        Assert.True(encore.Succes);
        Assert.True(encore.DejaPointe);

        var marie = store.FindUserByIdentifier("mrabe")!;
        Assert.True(store.MarkPresence(reunion.Id, "mrabe", PresenceStatuts.Absent).Success);
        Assert.False(store.PointerPresenceParQr(reunion.Id, store.VCardMembre(marie)).DejaPointe);
        Assert.Equal((2, 0, 0, 0, 2), store.ReunionStats(reunion.Id));

        var inconnu = store.PointerPresenceParQr(reunion.Id, "n'importe quoi");
        Assert.False(inconnu.Succes);
        Assert.Null(inconnu.Membre);
    }
}
