using GestionUtilisateurs.Shared.Models;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Blazor.Tests;

public class DataStoreFamangianaTests
{
    private static DataStore NewAdminStore()
    {
        var store = new DataStore(new MemoryStorageProvider());
        store.EnsureInitialized();
        Assert.True(store.Login("admin", "admin123").Success);
        return store;
    }

    private static UserAccount Ajouter(DataStore store, string prenom, string nom, string naissance = "")
    {
        var res = store.AddUser(new UserAccount { Prenom = prenom, Nom = nom, Role = Roles.Utilisateur, Actif = true, DateNaissance = naissance });
        Assert.True(res.Success, res.Error);
        return store.FindUser(res.UserId!)!;
    }

    [Fact]
    public void Types_AnciensGardes_NouveauxAcceptes()
    {
        Assert.Contains(SituationTypes.Visite, SituationTypes.Tous);
        Assert.Contains(SituationTypes.Accompagnement, SituationTypes.Tous);
        Assert.Contains(SituationTypes.EvenementImportant, SituationTypes.Tous);
        Assert.Contains(SituationTypes.Autre, SituationTypes.Tous);
        Assert.Equal(SituationTypes.Tous.Length, SituationTypes.Tous.Distinct().Count());

        var store = NewAdminStore();
        var m = Ajouter(store, "Hery", "Rabe");
        Assert.True(store.AddSituation(new Situation { MembreIdentifiant = m.Identifiant, Type = SituationTypes.Maladie, Titre = "Hospitalisé" }).Success);
        Assert.True(store.AddSituation(new Situation { MembreIdentifiant = m.Identifiant, Type = "inconnu", Titre = "X" }).Success);
        Assert.Equal(SituationTypes.Maladie, store.Situations.Single(s => s.Titre == "Hospitalisé").Type);
        Assert.Equal(SituationTypes.Visite, store.Situations.Single(s => s.Titre == "X").Type);
    }

    [Fact]
    public void Visite_CreeMetAJourEtRetireLEvenement_EtParticipationDansFinances()
    {
        var store = NewAdminStore();
        var m = Ajouter(store, "Hery", "Rabe");
        var v = Ajouter(store, "Lalao", "Rasoa");
        var draft = new Situation
        {
            MembreIdentifiant = m.Identifiant, Type = SituationTypes.Deces, Titre = "Décès de sa mère",
            PersonneConcernee = "sa mère", DateVisite = "20/10/2026", HeureVisite = "15:00:00", LieuVisite = "Ambohimanarina",
            Visiteurs = new List<string> { v.Identifiant, "personne-inconnue" }, Participation = 20000
        };
        Assert.True(store.AddSituation(draft, participationEnFinances: true).Success);
        var s = store.Situations.Single();
        Assert.Equal("2026-10-20", s.DateVisite);
        Assert.Equal(new[] { v.Identifiant }, s.Visiteurs);

        var evt = store.FindEvent(s.EvenementId!)!;
        Assert.Equal("Famangiana : Décès de sa mère", evt.Titre);
        Assert.Equal("2026-10-20", evt.Date);
        Assert.Equal("15:00", evt.HeureDebut);
        Assert.Contains(v.Identifiant, evt.PersonnesConcernees);
        Assert.True(store.ParticipationEnFinances(s));

        // Modification : l'événement et la sortie suivent, sans doublon.
        draft = new Situation
        {
            MembreIdentifiant = m.Identifiant, Type = SituationTypes.Deces, Titre = "Décès de sa mère",
            DateVisite = "2026-10-21", Visiteurs = s.Visiteurs.ToList(), Participation = 30000
        };
        Assert.True(store.UpdateSituation(s.Id, draft, participationEnFinances: true).Success);
        Assert.Equal("2026-10-21", store.FindEvent(s.EvenementId!)!.Date);
        Assert.Single(store.Events, e => e.Titre.StartsWith("Famangiana"));
        Assert.Equal(30000, store.Tresorerie.Single(t => t.Id == s.TransactionId).Montant);
        Assert.Single(store.Tresorerie);

        // Sans participation dans Finances ni date : la sortie et l'événement disparaissent.
        draft.DateVisite = "";
        Assert.True(store.UpdateSituation(s.Id, draft, participationEnFinances: false).Success);
        Assert.Null(s.EvenementId);
        Assert.DoesNotContain(store.Events, e => e.Titre.StartsWith("Famangiana"));
        Assert.Empty(store.Tresorerie);

        Assert.False(store.UpdateSituation(s.Id, new Situation { MembreIdentifiant = m.Identifiant, Titre = "x", DateVisite = "pas une date" }).Success);
    }

    [Fact]
    public void Deces_DesactiveLaFiche_SansAnniversaire_EtSAnnule()
    {
        var store = NewAdminStore();
        var m = Ajouter(store, "Koto", "Rabe", "1950-03-01");
        Assert.NotEmpty(store.GetBirthdayEventsForUser(m.Identifiant, m.NomComplet));

        Assert.False(store.MarquerDeces(m.Id, DateTime.Today.AddDays(2).ToString("yyyy-MM-dd")).Success);
        Assert.True(store.MarquerDeces(m.Id, "18/10/2026").Success);
        Assert.True(m.EstDecede);
        Assert.False(m.Actif);
        Assert.Equal("Décédé(e)", m.Statut);
        Assert.Empty(store.GetBirthdayEventsForUser(m.Identifiant, m.NomComplet));
        store.SyncAllUserDateEvents();
        Assert.Empty(store.GetBirthdayEventsForUser(m.Identifiant, m.NomComplet));

        Assert.False(store.MarquerDeces(store.GetCurrentUser()!.Id, "2026-10-18").Success);

        Assert.True(store.AnnulerDeces(m.Id).Success);
        Assert.False(m.EstDecede);
        Assert.True(m.Actif);
        Assert.NotEmpty(store.GetBirthdayEventsForUser(m.Identifiant, m.NomComplet));
    }

    [Fact]
    public void Naissance_CreeLaFicheDuBebe_ReliéeAuxParents()
    {
        var store = NewAdminStore();
        var pere = Ajouter(store, "Hery", "Rabe");
        var mere = Ajouter(store, "Lalao", "Rasoa");
        Assert.True(store.AjouterLienFamille(pere.Id, mere.Identifiant, LiensFamille.Conjoint).Success);
        Assert.True(store.AddSituation(new Situation { MembreIdentifiant = pere.Identifiant, Type = SituationTypes.Naissance, Titre = "Naissance" }).Success);
        var s = store.Situations.Single();

        Assert.False(store.CreerFicheNaissance(s.Id, "", "", "2026-10-18", true).Success);
        var res = store.CreerFicheNaissance(s.Id, "Mamy", "", DateTime.Today.ToString("yyyy-MM-dd"), true);
        Assert.True(res.Success, res.Error);
        var bebe = store.FindUser(res.UserId!)!;
        Assert.Equal("RABE", bebe.Nom);
        Assert.Equal(bebe.Identifiant, s.EnfantIdentifiant);
        Assert.Equal(new[] { mere.Id, pere.Id }.OrderBy(x => x), store.Famille(bebe).Parents.Select(p => p.Id).OrderBy(x => x));
        Assert.Contains("(parent)", store.ResponsableAffiche(bebe).Nom);
        Assert.False(store.CreerFicheNaissance(s.Id, "Encore", "", "2026-10-18", true).Success);
    }
}
