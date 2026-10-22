using GestionUtilisateurs.Shared.Models;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Blazor.Tests;

/// <summary>Tableaux personnalisés (Paramètres › Tableaux) et champs de montant liés.</summary>
[Collection("Langue")]
public class DataStoreTableauxTests : IDisposable
{
    private readonly DataStore _store;

    public DataStoreTableauxTests()
    {
        _store = new DataStore(new MemoryStorageProvider());
        _store.EnsureInitialized();
        Assert.True(_store.Login("admin", "admin123").Success);
        ListesChoix.Source = () => _store.Settings.Listes;
    }

    public void Dispose()
    {
        ListesChoix.Source = () => null;
        L.Definir(L.Fr);
    }

    private static TableauPerso Bareme(params (string Rubrique, string Montant)[] lignes)
    {
        var t = new TableauPerso
        {
            Nom = "Barème",
            Colonnes =
            {
                new ColonneTableau { Id = "r", Nom = "Rubrique" },
                new ColonneTableau { Id = "m", Nom = "Montant", Montant = true },
                new ColonneTableau { Id = "n", Nom = "Note" }
            },
            Champs = { ChampsLiables.Participation }
        };
        foreach (var (r, m) in lignes)
            t.Lignes.Add(new LigneTableau { Cellules = { ["r"] = r, ["m"] = m } });
        return t;
    }

    [Fact]
    public void Enregistrer_NormaliseLesMontants_EtProposeLesLignes()
    {
        var t = Bareme(("Naissance", "20 000"), ("Décès", "50000 Ar"), ("", ""), ("Maladie", ""));
        t.Lignes[0].Cellules["n"] = "par famille";
        Assert.True(_store.EnregistrerTableau(t).Success);

        var enregistre = _store.FindTableau(t.Id)!;
        Assert.Equal(3, enregistre.Lignes.Count); // la ligne vide est retirée
        Assert.Equal("20000", enregistre.Lignes[0].Valeur("m"));

        var rubriques = _store.Rubriques(ChampsLiables.Participation);
        Assert.Equal(new[] { "Naissance", "Décès", "Maladie" }, rubriques.Select(r => r.Libelle));
        Assert.Equal(20000, rubriques[0].Montant);
        Assert.Null(rubriques[2].Montant);
        Assert.Equal("par famille", rubriques[0].Details);
        Assert.Empty(_store.Rubriques(ChampsLiables.Cotisation));
        Assert.Equal("Décès", _store.RubriqueNommee(ChampsLiables.Participation, "deces")!.Libelle);

        // Le brouillon de l'éditeur n'est pas le tableau enregistré.
        t.Nom = "Autre nom";
        Assert.Equal("Barème", _store.FindTableau(t.Id)!.Nom);
    }

    [Fact]
    public void Enregistrer_RefuseLesErreurs()
    {
        Assert.False(_store.EnregistrerTableau(Bareme(("Naissance", "vingt mille"))).Success);
        Assert.False(_store.EnregistrerTableau(Bareme(("Naissance", "-5"))).Success);

        var sansNom = Bareme();
        sansNom.Nom = " ";
        Assert.False(_store.EnregistrerTableau(sansNom).Success);

        var colonneVide = Bareme();
        colonneVide.Colonnes[2].Nom = "";
        Assert.False(_store.EnregistrerTableau(colonneVide).Success);

        var doublon = Bareme();
        doublon.Colonnes[2].Nom = "rubrique";
        Assert.False(_store.EnregistrerTableau(doublon).Success);

        // Lié à un champ de montant sans colonne de montants : refusé ; non lié : accepté.
        var sansMontant = Bareme();
        sansMontant.Colonnes[1].Montant = false;
        Assert.False(_store.EnregistrerTableau(sansMontant).Success);
        sansMontant.Champs.Clear();
        Assert.True(_store.EnregistrerTableau(sansMontant).Success);

        // Deux tableaux ne portent pas le même nom.
        Assert.False(_store.EnregistrerTableau(Bareme()).Success);
    }

    [Fact]
    public void UnChampSuitUnSeulTableau()
    {
        var a = Bareme(("A", "1000"));
        Assert.True(_store.EnregistrerTableau(a).Success);
        var b = Bareme(("B", "2000"));
        b.Nom = "Barème 2";
        Assert.True(_store.EnregistrerTableau(b).Success);

        Assert.Empty(_store.FindTableau(a.Id)!.Champs);
        Assert.Equal("B", _store.Rubriques(ChampsLiables.Participation).Single().Libelle);

        Assert.True(_store.SupprimerTableau(b.Id).Success);
        Assert.Empty(_store.Rubriques(ChampsLiables.Participation));
        Assert.Single(_store.Tableaux);
    }

    [Fact]
    public void ColonneSupprimee_SesCasesPartent()
    {
        var t = Bareme(("Naissance", "20000"));
        t.Lignes[0].Cellules["n"] = "note";
        Assert.True(_store.EnregistrerTableau(t).Success);
        var copie = _store.FindTableau(t.Id)!.Copie();
        copie.Colonnes.RemoveAt(2);
        Assert.True(_store.EnregistrerTableau(copie).Success);
        Assert.False(_store.FindTableau(t.Id)!.Lignes[0].Cellules.ContainsKey("n"));
    }

    [Fact]
    public void ModeleFamangiana_UneLigneParType_LieALaParticipation()
    {
        var modele = _store.ModeleBaremeFamangiana();
        Assert.Contains(ChampsLiables.Participation, modele.Champs);
        var noms = modele.Lignes.Select(l => l.Valeur(modele.ColonneLibelle!.Id)).ToList();
        Assert.Contains(SituationTypes.Label(SituationTypes.Naissance), noms);
        Assert.Contains(SituationTypes.Label(SituationTypes.Deces), noms);
        Assert.DoesNotContain(SituationTypes.Label(SituationTypes.Autre), noms);

        // Sans montant, les lignes sont gardées (un nom suffit) ; on les remplit ensuite.
        Assert.True(_store.EnregistrerTableau(modele).Success);
        Assert.Equal(noms.Count, _store.Rubriques(ChampsLiables.Participation).Count);
    }

    [Fact]
    public void Participation_GardeLaRubrique_EtLaMetDansLeMotif()
    {
        var m = _store.AddUser(new UserAccount { Prenom = "Hery", Nom = "Rabe", Role = Roles.Utilisateur, Actif = true });
        var membre = _store.FindUser(m.UserId!)!;
        Assert.True(_store.AddSituation(new Situation
        {
            MembreIdentifiant = membre.Identifiant, Type = SituationTypes.Naissance, Titre = "Naissance de Fitia",
            Participation = 20000, ParticipationRubrique = " Naissance "
        }, participationEnFinances: true).Success);
        var s = _store.Situations.Single();
        Assert.Equal("Naissance", s.ParticipationRubrique);
        var tx = _store.Tresorerie.Single(t => t.Id == s.TransactionId);
        Assert.EndsWith("· Naissance", tx.Motif);

        Assert.True(_store.UpdateSituation(s.Id, new Situation
        {
            MembreIdentifiant = membre.Identifiant, Type = SituationTypes.Naissance, Titre = "Naissance de Fitia",
            Participation = 15000
        }).Success);
        Assert.Equal("", _store.Situations.Single().ParticipationRubrique);
    }

    [Fact]
    public void Tableaux_GardesQuandLesParametresSontEnregistres_EtReservesAuxGestionnaires()
    {
        Assert.True(_store.EnregistrerTableau(Bareme(("A", "1000"))).Success);
        _store.SaveSettings(new AppSettings { AppName = "Test" });
        Assert.Single(_store.Tableaux);

        Assert.True(_store.AddUser(new UserAccount
        {
            Prenom = "Lalao", Nom = "Rasoa", Identifiant = "lalao",
            Role = Roles.Utilisateur, Actif = true, MotDePasse = "secret123"
        }).Success);
        _store.Logout();
        Assert.True(_store.Login("lalao", "secret123").Success);
        var autre = Bareme(("B", "1"));
        autre.Nom = "Autre";
        Assert.False(_store.EnregistrerTableau(autre).Success);
        Assert.False(_store.SupprimerTableau(_store.Tableaux[0].Id).Success);
        // Mais tout le monde voit les lignes dans les formulaires.
        Assert.Single(_store.Rubriques(ChampsLiables.Participation));
    }
}
