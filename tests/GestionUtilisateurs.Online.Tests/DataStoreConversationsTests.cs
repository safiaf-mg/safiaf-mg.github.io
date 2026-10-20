using GestionUtilisateurs.Shared.Models;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Blazor.Tests;

/// <summary>Discussions de groupe, suppression d'une conversation, intentions par groupe et ajouts en double.</summary>
[Collection("Langue")]
public class DataStoreConversationsTests
{
    private readonly DataStore _store = new(new MemoryStorageProvider());

    public DataStoreConversationsTests()
    {
        _store.EnsureInitialized();
        Assert.True(_store.Login("admin", "admin123").Success);
        foreach (var (prenom, ident) in new[] { ("Lalao", "lalao"), ("Hery", "hery"), ("Tiana", "tiana") })
            Assert.True(_store.AddUser(new UserAccount
            {
                Prenom = prenom, Nom = "Rabe", Identifiant = ident,
                Role = Roles.Utilisateur, Actif = true, MotDePasse = "secret123"
            }).Success);
    }

    private void Connecter(string identifiant)
    {
        _store.Logout();
        Assert.True(_store.Login(identifiant, "secret123").Success);
    }

    private static string Demain => DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");

    [Fact]
    public void EcrireAuGroupe_UneSeuleDiscussionAuNomDuGroupe()
    {
        Assert.True(_store.AjouterGroupe(new Groupe { Nom = "Chorale", Responsables = new() { "lalao" }, Membres = new() { "hery" } }).Success);
        var g = _store.Groupes[0];
        Connecter("lalao");
        Assert.True(_store.EcrireAuGroupe(g.Id, "Répétition samedi").Success);
        Assert.True(_store.EcrireAuGroupe(g.Id, "N'oubliez pas les partitions").Success);

        Connecter("hery");
        var conv = Assert.Single(_store.GetConversations("hery"));
        Assert.Equal(DataStore.PrefixeGroupe + g.Id, conv.ConversationId);
        Assert.Equal("Chorale", _store.NomDiscussion(conv.ConversationId));
        Assert.Equal(2, conv.NonLus);
        Assert.True(_store.RepondreConversation(conv.ConversationId, "Je serai là").Success);
        Assert.Equal(3, _store.GetMessages(conv.ConversationId).Count);
    }

    [Fact]
    public void Messagerie_CreerUnGroupeNomme()
    {
        Connecter("lalao");
        Assert.False(_store.CreerDiscussionDeGroupe("", new[] { "hery", "tiana" }, "Bonjour").Success);
        Assert.False(_store.CreerDiscussionDeGroupe("Kermesse", new[] { "hery" }, "Bonjour").Success);
        Assert.True(_store.CreerDiscussionDeGroupe("Kermesse", new[] { "hery", "tiana" }, "Bonjour").Success);
        Assert.False(_store.CreerDiscussionDeGroupe("kermesse ", new[] { "hery", "tiana" }, "Encore").Success);
        var cid = _store.GetConversations("lalao")[0].ConversationId;
        Assert.True(DataStore.EstDiscussionDeGroupe(cid));
        Assert.Equal(3, _store.ParticipantsConversation(cid).Count);

        // Un message direct aux mêmes personnes reste une autre conversation.
        Assert.True(_store.SendMessage(new() { "hery", "tiana" }, "Hors groupe").Success);
        Assert.Equal(2, _store.GetConversations("lalao").Count);

        Connecter("tiana");
        Assert.True(_store.RepondreConversation(cid, "Merci").Success);
        Assert.Equal("Kermesse", _store.NomDiscussion(cid));
    }

    [Fact]
    public void SupprimerConversation_PourMoiPuisRetourAvecLesNouveauxMessages()
    {
        Connecter("lalao");
        Assert.True(_store.SendMessage(new() { "hery" }, "Premier").Success);
        var cid = _store.GetConversations("lalao")[0].ConversationId;

        Connecter("hery");
        Assert.True(_store.SupprimerConversation(cid).Success);
        Assert.Empty(_store.GetConversations("hery"));
        Assert.Equal(0, _store.UnreadCount("hery"));
        Assert.False(_store.SupprimerConversation(cid, pourTous: true).Success);

        Connecter("lalao");
        Assert.Single(_store.GetConversations("lalao"));
        Assert.True(_store.SendMessage(new() { "hery" }, "Second").Success);

        Connecter("hery");
        Assert.Single(_store.GetConversations("hery"));
        var m = Assert.Single(_store.GetMessages(cid));
        Assert.Equal("Second", m.Texte);
    }

    [Fact]
    public void SupprimerConversation_PourTousParUnAdministrateur()
    {
        Assert.True(_store.SendMessage(new() { "hery", "lalao" }, "Note").Success);
        var cid = _store.GetConversations(_store.GetCurrentUser()!.Identifiant)[0].ConversationId;
        Assert.True(_store.SupprimerConversation(cid, pourTous: true).Success);
        Assert.DoesNotContain(_store.Messages, x => x.ConversationId == cid);
    }

    [Fact]
    public void Intention_ConfieeAPlusieursGroupes_LueParLeursMembresSeulement()
    {
        Assert.True(_store.AjouterGroupe(new Groupe { Nom = "Chorale", Membres = new() { "lalao", "hery" } }).Success);
        Assert.True(_store.AjouterGroupe(new Groupe { Nom = "Jeunesse", Membres = new() { "lalao" } }).Success);
        var ids = _store.Groupes.Select(g => g.Id).ToList();

        Connecter("lalao");
        Assert.Equal(2, _store.GroupesDe("lalao").Count);
        Assert.True(_store.PartagerIntention("Pour notre concert", false, ids).Success);
        Assert.False(_store.PartagerIntention("pour notre  concert", false).Success);
        var p = Assert.Single(_store.Prieres);
        Assert.Equal(new[] { "Chorale", "Jeunesse" }, _store.GroupesIntention(p).OrderBy(x => x));

        Connecter("hery");
        Assert.Single(_store.Prieres);
        Assert.False(_store.PartagerIntention("Autre", false, new[] { ids.First(id => _store.FindGroupe(id)!.Nom == "Jeunesse") }).Success);

        Connecter("tiana");
        Assert.Empty(_store.Prieres);
        Assert.False(_store.BasculerJePrie(p.Id).Success);
    }

    [Fact]
    public void Doublons_RefusesALAjout()
    {
        var evt = new CalendarEvent { Titre = "Culte de Noël", Date = Demain, HeureDebut = "09:00" };
        Assert.True(_store.AddEvent(evt).Success);
        Assert.False(_store.AddEvent(new CalendarEvent { Titre = " culte de noel ", Date = Demain, HeureDebut = "09:00" }).Success);
        Assert.True(_store.AddEvent(new CalendarEvent { Titre = "Culte de Noël", Date = Demain, HeureDebut = "18:00" }).Success);

        Assert.True(_store.AddTache(new Tache { Titre = "Nettoyer la salle" }).Success);
        Assert.False(_store.AddTache(new Tache { Titre = "Nettoyer  la salle" }).Success);

        Assert.True(_store.AddMateriel(new Materiel { Nom = "Micro", QuantiteTotale = 2 }).Success);
        Assert.False(_store.AddMateriel(new Materiel { Nom = "micro", QuantiteTotale = 1 }).Success);

        Assert.True(_store.AddReunion(new Reunion { Titre = "Bureau", Date = Demain }).Success);
        Assert.False(_store.AddReunion(new Reunion { Titre = "Bureau", Date = Demain }).Success);

        Assert.True(_store.AddTransaction(new TransactionTresorerie { Motif = "Offrande", Montant = 1000, Date = Demain }).Success);
        Assert.False(_store.AddTransaction(new TransactionTresorerie { Motif = "Offrande", Montant = 1000, Date = Demain }).Success);
        Assert.True(_store.AddTransaction(new TransactionTresorerie { Motif = "Offrande", Montant = 2000, Date = Demain }).Success);

        Assert.True(_store.AddVote(new Vote { Titre = "Date de la kermesse", Options = new() { "Mai", "Juin" } }).Success);
        Assert.False(_store.AddVote(new Vote { Titre = "Date de la kermesse", Options = new() { "Mai", "Juin" } }).Success);

        Assert.True(_store.AjouterService(new ServiceCulte { Titre = "Culte", Date = Demain, Heure = "09:00" }).Success);
        Assert.False(_store.AjouterService(new ServiceCulte { Titre = "Culte", Date = Demain, Heure = "09:00" }).Success);
    }

    [Fact]
    public void Calendrier_SupprimerLesDoublonsExistants()
    {
        // Doublons venus d'avant la vérification (ancienne version, import).
        var stockage = new MemoryStorageProvider();
        var store = new DataStore(stockage);
        store.EnsureInitialized();
        stockage.Set("gu2:events", "[" + string.Join(",", new[]
        {
            ("e0", "Réunion", "2026-01-01"), ("d1", "réunion", "2026-02-01"), ("d2", "Réunion ", "2026-03-01"), ("x", "Autre", "2026-01-01")
        }.Select(e => $$"""{"id":"{{e.Item1}}","titre":"{{e.Item2}}","date":"{{Demain}}","dateCreation":"{{e.Item3}}T00:00:00Z"}""")) + "]");
        store = new DataStore(stockage);
        store.EnsureInitialized();
        Assert.True(store.Login("admin", "admin123").Success);

        Assert.Equal(new[] { "d1", "d2" }, store.DoublonsCalendrier().Select(e => e.Id).OrderBy(x => x));
        Assert.True(store.SupprimerDoublonsCalendrier().Success);
        Assert.Equal(new[] { "e0", "x" }, store.Events.Select(e => e.Id).OrderBy(x => x));
        Assert.Empty(store.DoublonsCalendrier());
    }

    [Fact]
    public void IdentifiantRenomme_SuitDansLesGroupesEtLesIntentions()
    {
        Assert.True(_store.AddUser(new UserAccount
        {
            Prenom = "Fara", Nom = "Rabe", Identifiant = "f.rabe",
            Role = Roles.Utilisateur, Actif = true, MotDePasse = "secret123"
        }).Success);
        Assert.True(_store.AjouterGroupe(new Groupe { Nom = "Mpiandry", Responsables = new() { "f.rabe" }, Membres = new() { "hery" } }).Success);
        Assert.True(_store.AppliquerIdentifiantsParPrenom().Success);
        Assert.Equal(new[] { "fara" }, _store.Groupes[0].Responsables);
    }
}
