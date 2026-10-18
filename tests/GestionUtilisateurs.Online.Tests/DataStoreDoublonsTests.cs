using GestionUtilisateurs.Shared.Models;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Blazor.Tests;

public class DataStoreDoublonsTests
{
    private static DataStore NewAdminStore()
    {
        var store = new DataStore(new MemoryStorageProvider());
        store.EnsureInitialized();
        Assert.True(store.Login("admin", "admin123").Success);
        return store;
    }

    private static UserAccount Ajouter(DataStore store, string prenom, string nom, string email = "", string tel = "", string naissance = "")
    {
        var res = store.AddUser(new UserAccount
        {
            Prenom = prenom, Nom = nom, Role = Roles.Utilisateur, Actif = true,
            Email = email, Telephone = tel, DateNaissance = naissance
        });
        Assert.True(res.Success, res.Error);
        return store.FindUser(res.UserId!)!;
    }

    [Fact]
    public void Doublons_DetectesParNomEmailTelephone_EtEcartables()
    {
        var store = NewAdminStore();
        var a = Ajouter(store, "Jean", "Rakoto", "jean@exemple.mg", "+261 34 12 345 67");
        var b = Ajouter(store, "RAKOTO", "jéan", "", "0341234567");
        Ajouter(store, "Marie", "Rabe", "marie@exemple.mg");

        var possibles = store.DoublonsPossibles(new UserAccount { Prenom = "x", Nom = "y", Email = "JEAN@exemple.mg" });
        Assert.Contains(possibles, p => p.Fiche.Id == a.Id && p.Raison.Contains("e-mail"));

        var groupe = Assert.Single(store.GroupesDeDoublons());
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(i => i), groupe.Membres.Select(m => m.Id).OrderBy(i => i));

        Assert.True(store.MarquerPasDoublon(new[] { a.Id, b.Id }).Success);
        Assert.Empty(store.GroupesDeDoublons());
    }

    [Fact]
    public void Fusion_ReporteLesDonnees_EtSupprimeLaFiche()
    {
        var store = NewAdminStore();
        var garder = Ajouter(store, "Jean", "Rakoto", "jean@exemple.mg");
        var retirer = Ajouter(store, "Jean", "Rakoto", "", "+261 34 12 345 67", "1990-05-04");
        Assert.True(store.AddCotisationsEnAttente(new[] { retirer.Identifiant }, 1000, "Annuelle", "2026").Result.Success);
        Assert.True(store.AddTache(new Tache { Titre = "Préparer", Assignes = new List<string> { retirer.Identifiant } }).Success);

        var res = store.FusionnerUtilisateurs(garder.Id, retirer.Id);
        Assert.True(res.Success, res.Error);

        Assert.Null(store.FindUser(retirer.Id));
        var fiche = store.FindUser(garder.Id)!;
        Assert.Equal("+261 34 12 345 67", fiche.Telephone);
        Assert.Equal("1990-05-04", fiche.DateNaissance);
        Assert.Contains(store.ImpayesCotisations("2026"), u => u.Id == garder.Id);
        Assert.Contains(store.Taches, t => t.Assignes.Contains(garder.Identifiant));
        Assert.Empty(store.GroupesDeDoublons());
    }

    [Fact]
    public void Fusion_GardeLeCompteEnLigne()
    {
        var store = NewAdminStore();
        var fiche = Ajouter(store, "Hery", "Rabe", "hery@exemple.mg");
        var compteId = Guid.NewGuid().ToString("D");
        var compte = Ajouter(store, "Hery", "Rabe");
        compte.Id = compteId; // fiche créée à la validation d'un compte en ligne

        var ficheId = fiche.Id;
        Assert.True(store.FusionnerUtilisateurs(ficheId, compteId).Success);
        var gardee = store.FindUser(compteId);
        Assert.NotNull(gardee);
        Assert.Equal("hery@exemple.mg", gardee!.Email);
        Assert.Null(store.FindUser(ficheId));
        Assert.Single(store.Users, u => u.Email == "hery@exemple.mg");
    }

    [Fact]
    public void Mineur_DemandeLeResponsableLegal()
    {
        var store = NewAdminStore();
        var naissance = DateTime.Today.AddYears(-12).ToString("yyyy-MM-dd");
        var draft = new UserAccount { Prenom = "Tiana", Nom = "Rabe", Role = Roles.Utilisateur, Actif = true, DateNaissance = naissance };
        Assert.True(draft.EstMineur);
        Assert.Equal(12, draft.Age);
        Assert.Contains("responsable", store.ValidateUser(draft, null, true, false).Keys);

        draft.ResponsableLegal = "Rabe Hery (père)";
        Assert.DoesNotContain("responsable", store.ValidateUser(draft, null, true, false).Keys);

        var adulte = new UserAccount { DateNaissance = DateTime.Today.AddYears(-18).ToString("yyyy-MM-dd") };
        Assert.False(adulte.EstMineur);
    }

    [Fact]
    public void Matricule_FormatRegle_AttribueAutomatiquement()
    {
        Assert.Null(DataStore.ValiderFormatMatricule(""));
        Assert.Null(DataStore.ValiderFormatMatricule("M-{NNNN}"));
        Assert.NotNull(DataStore.ValiderFormatMatricule("M-{AAAA}"));
        Assert.NotNull(DataStore.ValiderFormatMatricule("M-{NNN}-{XX}"));
        Assert.Equal("CJ26-007", DataStore.ConstruireMatricule("CJ{AA}-{NNN}", 7, new DateTime(2026, 3, 1)));

        var store = NewAdminStore();
        var sans = Ajouter(store, "Avant", "Format");
        Assert.Equal("", sans.Matricule);

        var s = store.Settings;
        s.MatriculeFormat = "M-{NNNN}";
        store.SaveSettings(s);

        var a = Ajouter(store, "Aina", "Rabe");
        var b = Ajouter(store, "Bako", "Rabe");
        Assert.Equal("M-0001", a.Matricule);
        Assert.Equal("M-0002", b.Matricule);

        var (res, n) = store.AttribuerMatriculesManquants();
        Assert.True(res.Success, res.Error);
        Assert.True(n >= 1);
        Assert.All(store.Users, u => Assert.False(string.IsNullOrWhiteSpace(u.Matricule)));
        Assert.Equal(store.Users.Count, store.Users.Select(u => u.Matricule).Distinct().Count());
    }

    [Fact]
    public void Rappels_NeLevePasDException_Pour29Fevrier()
    {
        var store = NewAdminStore();
        Ajouter(store, "Bissextile", "Rabe", naissance: "2004-02-29");
        var rappels = store.GetRappels();
        Assert.NotNull(rappels);
    }

    [Fact]
    public void Famille_LiensDansLesDeuxSens_EtDeductions()
    {
        var store = NewAdminStore();
        var grandMere = Ajouter(store, "Voahangy", "Rabe");
        var pere = Ajouter(store, "Hery", "Rabe", tel: "+261 34 11 222 33");
        var mere = Ajouter(store, "Lalao", "Rasoa");
        var a = Ajouter(store, "Tiana", "Rabe", naissance: DateTime.Today.AddYears(-10).ToString("yyyy-MM-dd"));
        var b = Ajouter(store, "Fara", "Rabe");

        Assert.True(store.AjouterLienFamille(a.Id, pere.Identifiant, LiensFamille.Parent).Success);
        Assert.True(store.AjouterLienFamille(pere.Id, mere.Identifiant, LiensFamille.Conjoint).Success);
        Assert.True(store.AjouterLienFamille(pere.Id, b.Identifiant, LiensFamille.Enfant).Success);
        Assert.True(store.AjouterLienFamille(pere.Id, grandMere.Identifiant, LiensFamille.Parent).Success);

        var fa = store.Famille(a);
        Assert.Equal(pere.Id, Assert.Single(fa.Parents).Id);
        Assert.Equal(b.Id, Assert.Single(fa.FreresSoeurs).Id);       // même parent
        Assert.Equal(grandMere.Id, Assert.Single(fa.GrandsParents).Id);

        var fp = store.Famille(pere);
        Assert.Equal(2, fp.Enfants.Count);
        Assert.Equal(mere.Id, Assert.Single(fp.Conjoints).Id);
        Assert.Equal(pere.Id, Assert.Single(store.Famille(mere).Conjoints).Id);
        Assert.Equal(2, store.Famille(grandMere).PetitsEnfants.Count);

        // Pas de boucle ni de doublon.
        Assert.False(store.AjouterLienFamille(pere.Id, a.Identifiant, LiensFamille.Parent).Success);
        Assert.False(store.AjouterLienFamille(grandMere.Id, a.Identifiant, LiensFamille.Parent).Success);
        Assert.False(store.AjouterLienFamille(a.Id, a.Identifiant, LiensFamille.Fratrie).Success);

        // Un mineur relié à un parent n'a plus besoin du responsable saisi à la main.
        Assert.Equal(pere.NomComplet + " (parent)", store.ResponsableAffiche(a).Nom);
        Assert.Equal("+261 34 11 222 33", store.ResponsableAffiche(a).Telephone);
        Assert.DoesNotContain("responsable", store.ValidateUser(a, a.Id, false, false).Keys);

        Assert.True(store.RetirerLienFamille(a.Id, pere.Identifiant).Success);
        Assert.Empty(store.Famille(a).Parents);
        Assert.Single(store.Famille(pere).Enfants);
    }

    [Fact]
    public void Famille_TousLesLiens_InversesEtDeductions()
    {
        var store = NewAdminStore();
        var pere = Ajouter(store, "Hery", "Rabe");
        var mere = Ajouter(store, "Lalao", "Rasoa");
        var fille = Ajouter(store, "Fara", "Rabe");
        var gendre = Ajouter(store, "Naina", "Rakoto");
        var oncle = Ajouter(store, "Solo", "Rabe");
        var cousin = Ajouter(store, "Mamy", "Rabe");
        var parrain = Ajouter(store, "Jean", "Randria");

        // Chaque type a un inverse, et l'inverse de l'inverse revient au type.
        foreach (var t in LiensFamille.Tous)
        {
            Assert.Contains(LiensFamille.Inverse(t), LiensFamille.Tous);
            Assert.Equal(t, LiensFamille.Inverse(LiensFamille.Inverse(t)));
        }
        Assert.Equal(LiensFamille.Tous.Length, LiensFamille.Groupes.SelectMany(g => g.Types).Distinct().Count());

        Assert.True(store.AjouterLienFamille(fille.Id, pere.Identifiant, LiensFamille.Parent).Success);
        Assert.True(store.AjouterLienFamille(pere.Id, mere.Identifiant, LiensFamille.Conjoint).Success);
        Assert.True(store.AjouterLienFamille(pere.Id, gendre.Identifiant, LiensFamille.GendreBru).Success);
        Assert.True(store.AjouterLienFamille(oncle.Id, pere.Identifiant, LiensFamille.Fratrie).Success);
        Assert.True(store.AjouterLienFamille(cousin.Id, oncle.Identifiant, LiensFamille.Parent).Success);
        Assert.True(store.AjouterLienFamille(fille.Id, parrain.Identifiant, LiensFamille.ParrainMarraine).Success);

        // Enregistré des deux côtés : le gendre voit son beau-père, le parrain sa filleule.
        Assert.Equal(pere.Id, Assert.Single(store.Famille(gendre).De(LiensFamille.BeauParent)).Id);
        Assert.Equal(fille.Id, Assert.Single(store.Famille(parrain).De(LiensFamille.Filleul)).Id);

        var ff = store.Famille(fille);
        Assert.Equal(oncle.Id, Assert.Single(ff.De(LiensFamille.OncleTante)).Id);       // frère du père
        Assert.Equal(cousin.Id, Assert.Single(ff.De(LiensFamille.Cousin)).Id);          // enfant de l'oncle
        Assert.Equal(mere.Id, Assert.Single(ff.De(LiensFamille.ConjointDuParent)).Id);  // non déclarée comme mère
        Assert.Equal(fille.Id, Assert.Single(store.Famille(oncle).De(LiensFamille.NeveuNiece)).Id);
        Assert.Equal(pere.Id, Assert.Single(store.Famille(cousin).De(LiensFamille.OncleTante)).Id);
        Assert.Equal(fille.Id, Assert.Single(store.Famille(mere).De(LiensFamille.EnfantDuConjoint)).Id);
        Assert.Equal(oncle.Id, Assert.Single(store.Famille(mere).De(LiensFamille.BeauFrereSoeur)).Id);

        // Un membre n'apparaît qu'une fois sur la fiche.
        var tous = ff.Groupes.SelectMany(g => g.Membres).Select(m => m.Id).ToList();
        Assert.Equal(tous.Count, tous.Distinct().Count());

        // Pas de boucle : un petit-enfant ne peut pas devenir grand-parent.
        Assert.True(store.AjouterLienFamille(oncle.Id, mere.Identifiant, LiensFamille.Autre).Success);
        var bebe = Ajouter(store, "Koto", "Rabe");
        Assert.True(store.AjouterLienFamille(bebe.Id, pere.Identifiant, LiensFamille.GrandParent).Success);
        Assert.False(store.AjouterLienFamille(bebe.Id, pere.Identifiant, LiensFamille.PetitEnfant).Success);
        Assert.False(store.AjouterLienFamille(pere.Id, fille.Identifiant, LiensFamille.ArriereGrandParent).Success);
        Assert.Equal(bebe.Id, Assert.Single(store.Famille(pere).PetitsEnfants).Id);
        Assert.False(store.AjouterLienFamille(fille.Id, pere.Identifiant, "inconnu").Success);
    }

    [Fact]
    public void Famille_TuteurRelie_SuffitPourUnMineur()
    {
        var store = NewAdminStore();
        var tuteur = Ajouter(store, "Hery", "Rabe", tel: "+261 34 11 222 33");
        var enfant = Ajouter(store, "Tiana", "Rabe", naissance: DateTime.Today.AddYears(-10).ToString("yyyy-MM-dd"));
        Assert.True(store.AjouterLienFamille(tuteur.Id, enfant.Identifiant, LiensFamille.Pupille).Success);
        Assert.Equal(tuteur.NomComplet + " (tuteur)", store.ResponsableAffiche(enfant).Nom);
        Assert.DoesNotContain("responsable", store.ValidateUser(enfant, enfant.Id, false, false).Keys);
    }

    [Fact]
    public void HistoireDeLaCommunaute_PorteLeNomDeLApplication()
    {
        var store = NewAdminStore();
        Assert.Equal("Histoire de la communauté", store.LibelleHistoire());
        var s = store.Settings;
        s.AppName = "Fikambanana Fanantenana";
        store.SaveSettings(s);
        Assert.Equal("Histoire de Fikambanana Fanantenana", store.LibelleHistoire());
        s.AppName = "Avotra";
        store.SaveSettings(s);
        Assert.Equal("Histoire d'Avotra", store.LibelleHistoire());
    }
}
