using GestionUtilisateurs.Shared.Models;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Blazor.Tests;

/// <summary>Listes de choix réglables (Paramètres › Listes de choix).</summary>
[Collection("Langue")]
public class DataStoreListesTests : IDisposable
{
    private readonly DataStore _store;

    public DataStoreListesTests()
    {
        _store = new DataStore(new MemoryStorageProvider());
        _store.EnsureInitialized();
        Assert.True(_store.Login("admin", "admin123").Success);
        // Comme Root.razor : les libellés suivent les réglages de ce magasin.
        ListesChoix.Source = () => _store.Settings.Listes;
    }

    public void Dispose()
    {
        ListesChoix.Source = () => null;
        L.Definir(L.Fr);
    }

    private UserAccount Ajouter(string prenom, string nom)
    {
        var res = _store.AddUser(new UserAccount { Prenom = prenom, Nom = nom, Role = Roles.Utilisateur, Actif = true });
        Assert.True(res.Success, res.Error);
        return _store.FindUser(res.UserId!)!;
    }

    private ChoixListe Element(string liste, string libelle) => _store.Liste(liste).Single(e => e.Libelle == libelle);

    [Fact]
    public void ToutesLesListes_ProposentLeursChoixDOrigine()
    {
        foreach (var def in ListesChoix.Definitions)
        {
            var elements = _store.Liste(def.Id);
            Assert.NotEmpty(elements);
            Assert.All(elements, e => Assert.True(e.DOrigine && !e.Masque));
            Assert.Equal(elements.Count, elements.Select(e => e.Cle).Distinct().Count());
        }
        Assert.Equal(ListesChoix.Definitions.Length, ListesChoix.Definitions.Select(d => d.Id).Distinct().Count());
        Assert.Equal(SituationTypes.Tous, _store.Liste(ListesChoix.TypesSituation).Select(e => e.Cle));
    }

    [Fact]
    public void Ajouter_UnTypeDeSituation_UtilisableEtAffiche()
    {
        Assert.True(_store.AjouterElementListe(ListesChoix.TypesSituation, "Fiançailles", "💍").Success);
        var e = Element(ListesChoix.TypesSituation, "Fiançailles");
        Assert.Equal("perso-fiancailles", e.Cle);
        Assert.False(e.DOrigine);
        Assert.Equal("Fiançailles", SituationTypes.Label(e.Cle));
        Assert.Equal("💍", SituationTypes.Icon(e.Cle));

        // Les doublons de libellé sont refusés, la clé reste unique.
        Assert.False(_store.AjouterElementListe(ListesChoix.TypesSituation, "fiançailles").Success);
        Assert.False(_store.AjouterElementListe(ListesChoix.TypesSituation, "  ").Success);

        var m = Ajouter("Hery", "Rabe");
        Assert.True(_store.AddSituation(new Situation { MembreIdentifiant = m.Identifiant, Type = e.Cle, Titre = "Fiançailles de Hery" }).Success);
        Assert.Equal(e.Cle, _store.Situations.Single().Type);

        // Les réglages survivent à l'enregistrement des paramètres par la page Paramètres.
        var prev = _store.Settings;
        _store.SaveSettings(new AppSettings { AppName = prev.AppName, PasDoublons = prev.PasDoublons });
        Assert.Contains(_store.Liste(ListesChoix.TypesSituation), x => x.Cle == e.Cle);
    }

    [Fact]
    public void Renommer_UnChoixDOrigine_PuisRevenirALOriginal()
    {
        Assert.True(_store.ModifierElementListe(ListesChoix.Priorites, CalendarEventTypes.PrioriteUrgente, "Très urgent").Success);
        Assert.Equal("Très urgent", CalendarEventTypes.PrioriteLabel(CalendarEventTypes.PrioriteUrgente));
        Assert.Equal("Très urgent", Element(ListesChoix.Priorites, "Très urgent").Libelle);

        // Libellé vide : retour au libellé d'origine (traduit).
        Assert.True(_store.ModifierElementListe(ListesChoix.Priorites, CalendarEventTypes.PrioriteUrgente, "").Success);
        Assert.Equal("Urgente", CalendarEventTypes.PrioriteLabel(CalendarEventTypes.PrioriteUrgente));

        // Les listes de statuts se renomment mais n'acceptent ni ajout ni masquage.
        Assert.True(_store.ModifierElementListe(ListesChoix.StatutsTache, TacheStatuts.Terminee, "Fini", "🏁").Success);
        Assert.Equal("Fini", TacheStatuts.Label(TacheStatuts.Terminee));
        Assert.Equal("🏁", TacheStatuts.Icon(TacheStatuts.Terminee));
        Assert.False(_store.AjouterElementListe(ListesChoix.StatutsTache, "Bloquée").Success);
        Assert.False(_store.MasquerElementListe(ListesChoix.StatutsTache, TacheStatuts.EnCours, true).Success);
    }

    [Fact]
    public void Masquer_Deplacer_Supprimer()
    {
        var liste = ListesChoix.EtatsMateriel;
        Assert.True(_store.AjouterElementListe(liste, "À réparer").Success);
        var cle = Element(liste, "À réparer").Cle;

        // Un choix masqué n'est plus proposé, sauf pour la fiche qui l'a déjà.
        Assert.True(_store.MasquerElementListe(liste, MaterielEtats.Moyen, true).Success);
        Assert.DoesNotContain(ListesChoix.Choix(liste), c => c.Cle == MaterielEtats.Moyen);
        Assert.Contains(ListesChoix.Choix(liste, MaterielEtats.Moyen), c => c.Cle == MaterielEtats.Moyen);
        Assert.True(ListesChoix.Contient(liste, MaterielEtats.Moyen));

        // Monter le choix ajouté en tête.
        for (var i = 0; i < 5; i++) Assert.True(_store.DeplacerElementListe(liste, cle, -1).Success);
        Assert.Equal(cle, _store.Liste(liste)[0].Cle);

        // Un choix utilisé ne se supprime pas.
        Assert.True(_store.AddMateriel(new Materiel { Nom = "Groupe électrogène", QuantiteTotale = 1, Etat = cle }).Success);
        Assert.Equal(cle, _store.Materiels.Single(m => m.Nom == "Groupe électrogène").Etat);
        Assert.False(_store.SupprimerElementListe(liste, cle).Success);
        var mat = _store.Materiels.Single(m => m.Nom == "Groupe électrogène");
        Assert.True(_store.DeleteMateriel(mat.Id).Success);
        Assert.True(_store.SupprimerElementListe(liste, cle).Success);
        Assert.DoesNotContain(_store.Liste(liste), c => c.Cle == cle);

        // Rétablir : ordre et visibilité d'origine.
        Assert.True(_store.ReinitialiserListe(liste).Success);
        Assert.Equal(MaterielEtats.Tous, _store.Liste(liste).Select(e => e.Cle));
        Assert.All(_store.Liste(liste), e => Assert.False(e.Masque));
    }

    [Fact]
    public void Masquer_TousLesChoix_EstRefuse()
    {
        var liste = ListesChoix.EtatsMateriel;
        Assert.True(_store.MasquerElementListe(liste, MaterielEtats.Bon, true).Success);
        Assert.True(_store.MasquerElementListe(liste, MaterielEtats.Moyen, true).Success);
        Assert.False(_store.MasquerElementListe(liste, MaterielEtats.HorsService, true).Success);
    }

    [Fact]
    public void LienDeFamille_Ajoute_AvecSonInverse()
    {
        Assert.True(_store.AjouterElementListe(ListesChoix.LiensDeFamille, "Mpiray tam-po amin'ny raibe").Success);
        var a = Element(ListesChoix.LiensDeFamille, "Mpiray tam-po amin'ny raibe").Cle;
        Assert.True(_store.AjouterElementListe(ListesChoix.LiensDeFamille, "Zafin'ny rahalahy", inverse: a).Success);
        var b = Element(ListesChoix.LiensDeFamille, "Zafin'ny rahalahy").Cle;
        // Le lien inverse est réciproque.
        Assert.Equal(b, LiensFamille.Inverse(a));
        Assert.Equal(a, LiensFamille.Inverse(b));

        var hery = Ajouter("Hery", "Rabe");
        var solo = Ajouter("Solo", "Rabe");
        Assert.True(_store.AjouterLienFamille(hery.Id, solo.Identifiant, a).Success);
        Assert.Equal(a, _store.LienEntre(hery, solo.Identifiant));
        Assert.Equal(b, _store.LienEntre(solo, hery.Identifiant));
        Assert.Contains(_store.Famille(hery).Groupes, g => g.Type == a && g.Membres.Any(x => x.Id == solo.Id));
        Assert.Equal("Mpiray tam-po amin'ny raibe", LiensFamille.Titre(a));

        // Un lien sans inverse choisi est le même des deux côtés.
        Assert.True(_store.AjouterElementListe(ListesChoix.LiensDeFamille, "Namana akaiky").Success);
        var c = Element(ListesChoix.LiensDeFamille, "Namana akaiky").Cle;
        Assert.Equal(c, LiensFamille.Inverse(c));
        // Les liens d'origine gardent leur inverse.
        Assert.Equal(LiensFamille.Filleul, LiensFamille.Inverse(LiensFamille.ParrainMarraine));
        Assert.False(_store.AjouterLienFamille(hery.Id, solo.Identifiant, "lien-inexistant").Success);
    }

    [Fact]
    public void Reglages_ReservesAuxGestionnaires()
    {
        Assert.True(_store.AddUser(new UserAccount
        {
            Prenom = "Lalao", Nom = "Rasoa", Identifiant = "lalao",
            Role = Roles.Utilisateur, Actif = true, MotDePasse = "secret123"
        }).Success);
        _store.Logout();
        Assert.True(_store.Login("lalao", "secret123").Success);
        Assert.False(_store.AjouterElementListe(ListesChoix.TypesSituation, "Test").Success);
        Assert.False(_store.ModifierElementListe(ListesChoix.Priorites, CalendarEventTypes.PrioriteUrgente, "X").Success);
    }

    [Fact]
    public void SupprimerUnChoixDOrigine_PuisLeRemettre()
    {
        var liste = ListesChoix.TypesSituation;
        Assert.True(_store.SupprimerElementListe(liste, SituationTypes.Accident).Success);
        Assert.DoesNotContain(_store.Liste(liste), e => e.Cle == SituationTypes.Accident);
        Assert.False(ListesChoix.Contient(liste, SituationTypes.Accident));
        Assert.Equal(new[] { SituationTypes.Accident }, ListesChoix.Supprimes(liste).Select(e => e.Cle));
        // Le choix ne revient pas de lui-même après une autre modification.
        Assert.True(_store.DeplacerElementListe(liste, SituationTypes.Visite, 1).Success);
        Assert.DoesNotContain(_store.Liste(liste), e => e.Cle == SituationTypes.Accident);

        // Un choix d'origine utilisé ne se supprime pas.
        var m = Ajouter("Hery", "Rabe");
        Assert.True(_store.AddSituation(new Situation { MembreIdentifiant = m.Identifiant, Type = SituationTypes.Maladie, Titre = "Malade" }).Success);
        Assert.False(_store.SupprimerElementListe(liste, SituationTypes.Maladie).Success);
        // Les listes de statuts ne perdent aucun choix.
        Assert.False(_store.SupprimerElementListe(ListesChoix.StatutsTache, TacheStatuts.EnCours).Success);

        Assert.True(_store.RemettreElementListe(liste, SituationTypes.Accident).Success);
        Assert.Contains(_store.Liste(liste), e => e.Cle == SituationTypes.Accident);
        Assert.Empty(ListesChoix.Supprimes(liste));

        Assert.True(_store.SupprimerElementListe(liste, SituationTypes.Bapteme).Success);
        Assert.True(_store.ReinitialiserListe(liste).Success);
        Assert.Equal(SituationTypes.Tous, _store.Liste(liste).Select(e => e.Cle));
    }

    [Fact]
    public void Traductions_ParLangue_SinonTexteFrancais()
    {
        var liste = ListesChoix.TypesSituation;
        Assert.True(_store.AjouterElementListe(liste, "Fiançailles", "💍", traductions: new() { [L.En] = "Engagement", [L.Mg] = "" }).Success);
        var cle = _store.Liste(liste).Single(e => e.Libelle == "Fiançailles").Cle;
        Assert.Equal("Fiançailles", SituationTypes.Label(cle));

        L.Definir(L.En);
        Assert.Equal("Engagement", SituationTypes.Label(cle));
        Assert.Contains(ListesChoix.Choix(liste), e => e.Libelle == "Engagement");
        // L'historique reste en français.
        Assert.Equal("Fiançailles", L.EnFrancais(() => SituationTypes.Label(cle)));
        L.Definir(L.Mg);
        Assert.Equal("Fiançailles", SituationTypes.Label(cle));

        // Choix d'origine : la traduction donnée remplace celle de l'application, pour cette langue seulement.
        Assert.True(_store.ModifierElementListe(liste, SituationTypes.Accident, "", traductions: new() { [L.Mg] = "Loza (voina)" }).Success);
        Assert.Equal("Loza (voina)", SituationTypes.Label(SituationTypes.Accident));
        L.Definir(L.Fr);
        Assert.Equal("Accident ou sinistre", SituationTypes.Label(SituationTypes.Accident));
        var reglage = _store.ReglageElement(liste, SituationTypes.Accident);
        Assert.Equal("Accident ou sinistre", reglage.Francais);
        Assert.Equal("Loza (voina)", reglage.Traductions[L.Mg]);

        // Saisi en anglais, le libellé français reste la référence (pas de doublon, pas d'écrasement).
        L.Definir(L.En);
        Assert.True(_store.ModifierElementListe(liste, cle, "Fiançailles", "💍", traductions: new() { [L.En] = "Engagement party" }).Success);
        L.Definir(L.Fr);
        Assert.Equal("Fiançailles", SituationTypes.Label(cle));
    }

    [Fact]
    public void ChoixRetireDeLApplication_IgnoreDansLesReglages()
    {
        // Réglages enregistrés avant le retrait de « famadihana » (supprimé ou seulement déplacé).
        _store.Settings.Listes[ListesChoix.TypesSituation] = new ListeConfig
        {
            Elements = new() { new() { Cle = "famadihana", Supprime = true }, new() { Cle = SituationTypes.Deces } }
        };
        Assert.DoesNotContain(_store.Liste(ListesChoix.TypesSituation), e => e.Cle == "famadihana");
        Assert.Empty(ListesChoix.Supprimes(ListesChoix.TypesSituation));
        _store.Settings.Listes[ListesChoix.TypesSituation] = new ListeConfig { Elements = new() { new() { Cle = "famadihana" } } };
        Assert.DoesNotContain(_store.Liste(ListesChoix.TypesSituation), e => e.Cle == "famadihana");
        Assert.Equal(SituationTypes.Tous, _store.Liste(ListesChoix.TypesSituation).Select(e => e.Cle));
        Assert.DoesNotContain("famadihana", SituationTypes.Tous);
    }
}
