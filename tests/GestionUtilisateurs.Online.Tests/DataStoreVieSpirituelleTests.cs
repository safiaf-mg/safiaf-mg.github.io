using GestionUtilisateurs.Shared.Models;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Blazor.Tests;

/// <summary>Groupes et ministères, planning des services, intentions de prière.</summary>
[Collection("Langue")]
public class DataStoreVieSpirituelleTests
{
    private readonly MemoryStorageProvider _stockage = new();
    private DataStore _store;

    public DataStoreVieSpirituelleTests()
    {
        _store = new DataStore(_stockage);
        _store.EnsureInitialized();
        Assert.True(_store.Login("admin", "admin123").Success);
        Ajouter("Lalao", "Rasoa", "lalao");
        Ajouter("Hery", "Rabe", "hery");
    }

    private void Ajouter(string prenom, string nom, string identifiant)
    {
        var res = _store.AddUser(new UserAccount
        {
            Prenom = prenom, Nom = nom, Identifiant = identifiant,
            Role = Roles.Utilisateur, Actif = true, MotDePasse = "secret123"
        });
        Assert.True(res.Success, res.Error);
    }

    private void ConnecterMembre(string identifiant)
    {
        _store.Logout();
        Assert.True(_store.Login(identifiant, "secret123").Success);
    }

    private static string Dimanche(int semaines = 1)
        => DateTime.Today.AddDays(7 * semaines).ToString("yyyy-MM-dd");

    [Fact]
    public void Permissions_ParDefaut()
    {
        foreach (var p in new[] { Permissions.GroupesManage, Permissions.ServicesManage, Permissions.PrieresUse, Permissions.PrieresManage })
            Assert.True(_store.Can(p), p);
        Assert.True(_store.PeutOuvrir("groupes"));
        Assert.True(_store.PeutOuvrir("services"));
        Assert.True(_store.PeutOuvrir("prieres"));

        ConnecterMembre("lalao");
        Assert.True(_store.Can(Permissions.PrieresUse));
        Assert.False(_store.Can(Permissions.PrieresManage));
        Assert.False(_store.Can(Permissions.GroupesManage));
        Assert.False(_store.Can(Permissions.ServicesManage));
        Assert.True(_store.PeutOuvrir("groupes"));
        Assert.True(_store.PeutOuvrir("services"));
        Assert.True(_store.PeutOuvrir("prieres"));
    }

    [Fact]
    public void Groupe_CreerModifierSupprimer()
    {
        var res = _store.AjouterGroupe(new Groupe
        {
            Nom = " Chorale ", Rencontre = "Samedi 15 h",
            Responsables = new() { "lalao" }, Membres = new() { "hery", "LALAO" }
        });
        Assert.True(res.Success, res.Error);
        var g = Assert.Single(_store.Groupes);
        Assert.Equal("Chorale", g.Nom);
        // Le responsable n'est pas répété parmi les membres.
        Assert.Equal(new[] { "hery" }, g.Membres);
        Assert.Equal(new[] { "lalao", "hery" }, DataStore.ParticipantsGroupe(g));
        Assert.Single(_store.GroupesDe("hery"));

        Assert.False(_store.AjouterGroupe(new Groupe { Nom = "chorale" }).Success);
        Assert.False(_store.AjouterGroupe(new Groupe { Nom = "Jeunesse", Membres = new() { "inconnu" } }).Success);
        Assert.False(_store.AjouterGroupe(new Groupe { Nom = "  " }).Success);

        Assert.True(_store.ModifierGroupe(g.Id, new Groupe { Nom = "Chorale Fifaliana", Membres = new() { "hery" } }).Success);
        Assert.Equal("Chorale Fifaliana", _store.FindGroupe(g.Id)!.Nom);
        Assert.Empty(_store.GroupesDe("lalao"));

        Assert.True(_store.SupprimerGroupe(g.Id).Success);
        Assert.Empty(_store.Groupes);
    }

    [Fact]
    public void Groupe_ReserveALaGestion_EtRencontreAvecLesMembres()
    {
        Assert.True(_store.AjouterGroupe(new Groupe { Nom = "Jeunesse", Responsables = new() { "lalao" }, Membres = new() { "hery" } }).Success);
        var g = _store.Groupes[0];

        Assert.True(_store.NouvelleReunionGroupe(g.Id, Dimanche()).Success);
        var r = Assert.Single(_store.ReunionsDuGroupe(g.Id));
        Assert.Equal(g.Id, r.GroupeId);
        Assert.Contains("hery", r.Participants);
        Assert.Contains("lalao", r.Participants);

        ConnecterMembre("hery");
        Assert.False(_store.AjouterGroupe(new Groupe { Nom = "Autre" }).Success);
        Assert.False(_store.SupprimerGroupe(g.Id).Success);
        Assert.Single(_store.GroupesDe("hery"));
    }

    [Fact]
    public void Service_SeulLaPersonnePrevueRepond()
    {
        var res = _store.AjouterService(new ServiceCulte
        {
            Titre = "Culte du dimanche", Date = Dimanche(), Heure = "09:00",
            Affectations = new()
            {
                new AffectationService { Role = RolesService.Predication, Identifiant = "lalao" },
                new AffectationService { Role = RolesService.Lecture, Identifiant = "hery" },
                new AffectationService { Role = RolesService.Chants }
            }
        });
        Assert.True(res.Success, res.Error);
        var s = Assert.Single(_store.Services);
        Assert.Equal(3, s.Affectations.Count);
        Assert.All(s.Affectations, a => Assert.Equal(AffectationStatuts.Prevu, a.Statut));
        var predication = s.Affectations.First(a => a.Role == RolesService.Predication);
        var lecture = s.Affectations.First(a => a.Role == RolesService.Lecture);

        ConnecterMembre("hery");
        Assert.Single(_store.MesServicesAVenir());
        Assert.False(_store.RepondreAffectation(s.Id, predication.Id, true).Success);
        Assert.True(_store.RepondreAffectation(s.Id, lecture.Id, false, "En voyage").Success);
        Assert.Equal(AffectationStatuts.Remplacement, lecture.Statut);
        Assert.Equal("En voyage", lecture.Remarque);
        Assert.Equal(1, _store.RemplacementsDemandes());
        Assert.False(_store.AjouterService(new ServiceCulte { Titre = "X", Date = Dimanche() }).Success);

        ConnecterMembre("lalao");
        Assert.True(_store.RepondreAffectation(s.Id, predication.Id, true).Success);
        Assert.Equal(AffectationStatuts.Confirme, predication.Statut);
    }

    [Fact]
    public void Service_ChangerLaPersonne_RemetAPrevu_EtCopieSemaineSuivante()
    {
        Assert.True(_store.AjouterService(new ServiceCulte
        {
            Titre = "Culte", Date = Dimanche(),
            Affectations = new() { new AffectationService { Role = RolesService.Lecture, Identifiant = "hery" } }
        }).Success);
        var s = _store.Services[0];
        var a = s.Affectations[0];
        Assert.True(_store.RepondreAffectation(s.Id, a.Id, false).Success);

        var draft = new ServiceCulte
        {
            Titre = s.Titre, Date = s.Date,
            Affectations = new() { new AffectationService { Id = a.Id, Role = a.Role, Identifiant = "lalao" } }
        };
        Assert.True(_store.ModifierService(s.Id, draft).Success);
        Assert.Equal(AffectationStatuts.Prevu, _store.FindService(s.Id)!.Affectations[0].Statut);
        Assert.Equal(0, _store.RemplacementsDemandes());

        Assert.True(_store.DupliquerService(s.Id).Success);
        Assert.Equal(2, _store.Services.Count);
        var copie = _store.Services[1];
        Assert.Equal(Dimanche(2), copie.Date);
        Assert.Equal("lalao", copie.Affectations[0].Identifiant);
        Assert.NotEqual(s.Affectations[0].Id, copie.Affectations[0].Id);

        Assert.False(_store.AjouterService(new ServiceCulte
        {
            Titre = "Culte", Date = Dimanche(),
            Affectations = new() { new AffectationService { Role = "tache-inconnue", Identifiant = "hery" } }
        }).Success);
    }

    [Fact]
    public void Priere_PartagerPrierExaucer()
    {
        ConnecterMembre("lalao");
        Assert.False(_store.PartagerIntention("   ", false).Success);
        Assert.False(_store.PartagerIntention(new string('a', 2001), false).Success);
        Assert.True(_store.PartagerIntention("Pour la santé de ma mère", true).Success);
        var p = Assert.Single(_store.Prieres);
        Assert.Equal("lalao", p.Auteur);
        Assert.Equal(L.T("Anonyme"), _store.AuteurIntention(p));

        ConnecterMembre("hery");
        Assert.True(_store.BasculerJePrie(p.Id).Success);
        Assert.Equal(new[] { "hery" }, p.Prieurs);
        // Un autre membre ne peut ni la marquer exaucée ni la supprimer.
        Assert.False(_store.MarquerExaucee(p.Id, true).Success);
        Assert.False(_store.SupprimerIntention(p.Id).Success);
        Assert.True(_store.BasculerJePrie(p.Id).Success);
        Assert.Empty(p.Prieurs);

        ConnecterMembre("lalao");
        Assert.True(_store.MarquerExaucee(p.Id, true, "Elle est guérie").Success);
        Assert.True(p.Exaucee);
        Assert.Equal("Elle est guérie", p.Temoignage);
        Assert.True(_store.MarquerExaucee(p.Id, false).Success);
        Assert.False(p.Exaucee);
        Assert.Equal("", p.Temoignage);
        Assert.True(_store.SupprimerIntention(p.Id).Success);
        Assert.Empty(_store.Prieres);
    }

    [Fact]
    public void Priere_Privees_CacheesSansLeDroit()
    {
        // Une intention privée déposée par le serveur (fonction deposer_intention_privee).
        _stockage.Set("gu2:prieres_privees",
            "[{\"id\":\"p1\",\"texte\":\"Confidentiel\",\"auteurNom\":\"Lalao Rasoa\",\"date\":\"2026-10-20T08:00:00+03:00\"}]");
        _store = new DataStore(_stockage);
        _store.EnsureInitialized();
        _store.Logout();
        Assert.True(_store.Login("admin", "admin123").Success);

        var privee = Assert.Single(_store.PrieresPrivees);
        Assert.Equal("Confidentiel", privee.Texte);
        Assert.Equal("Lalao Rasoa", _store.AuteurIntention(privee));
        Assert.True(_store.NoterSuiviPriere("p1", "Visite prévue").Success);
        Assert.Equal("Visite prévue", _store.PrieresPrivees[0].Suivi);

        ConnecterMembre("hery");
        Assert.Empty(_store.PrieresPrivees);
        Assert.False(_store.NoterSuiviPriere("p1", "x").Success);
        Assert.False(_store.MarquerExaucee("p1", true).Success);
        Assert.False(_store.SupprimerIntention("p1").Success);
    }

    [Fact]
    public void FusionDeDoublons_ReporteGroupesServicesEtPrieres()
    {
        Assert.True(_store.AjouterGroupe(new Groupe { Nom = "Chorale", Membres = new() { "hery" } }).Success);
        Assert.True(_store.AjouterService(new ServiceCulte
        {
            Titre = "Culte", Date = Dimanche(),
            Affectations = new() { new AffectationService { Role = RolesService.Lecture, Identifiant = "hery" } }
        }).Success);
        var hery = _store.FindUserByIdentifier("hery")!;
        var lalao = _store.FindUserByIdentifier("lalao")!;

        Assert.True(_store.FusionnerUtilisateurs(lalao.Id, hery.Id).Success);

        Assert.Equal(new[] { "lalao" }, _store.Groupes[0].Membres);
        Assert.Equal("lalao", _store.Services[0].Affectations[0].Identifiant);
    }
}
