using System.IO.Compression;
using GestionUtilisateurs.Shared.Models;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Blazor.Tests;

/// <summary>Âges et anniversaires, conversations renommées, export des membres, format de la carte.</summary>
[Collection("Langue")]
public class DataStoreAgesExportsTests
{
    private readonly MemoryStorageProvider _stockage = new();
    private readonly DataStore _store;

    public DataStoreAgesExportsTests()
    {
        _store = new DataStore(_stockage);
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

    // ---------- Âges ----------

    [Fact]
    public void Age_LeJourDeLAnniversaireCompte()
    {
        Assert.Equal(27, DataStore.Age("1998-12-01", new DateTime(2026, 10, 20)));
        Assert.Equal(27, DataStore.Age("1998-12-01", new DateTime(2026, 11, 30)));
        Assert.Equal(28, DataStore.Age("1998-12-01", new DateTime(2026, 12, 1)));
        Assert.Null(DataStore.Age("2030-01-01", new DateTime(2026, 10, 20)));
        Assert.Null(DataStore.Age("pas une date"));
    }

    [Fact]
    public void Age_Ne29Fevrier()
    {
        var naissance = new DateTime(2000, 2, 29);
        Assert.Equal(new DateTime(2027, 2, 28), DataStore.AnniversaireEn(naissance, 2027));
        Assert.Equal(new DateTime(2028, 2, 29), DataStore.AnniversaireEn(naissance, 2028));
        Assert.Equal(25, DataStore.Age("2000-02-29", new DateTime(2026, 2, 27)));
        Assert.Equal(26, DataStore.Age("2000-02-29", new DateTime(2026, 2, 28)));
        Assert.Equal(27, DataStore.Age("2000-02-29", new DateTime(2027, 2, 28)));
        Assert.Equal(new DateTime(2027, 2, 28), DataStore.ProchainAnniversaire(naissance, new DateTime(2026, 10, 20)));
    }

    [Fact]
    public void ProchaineOccurrence_SelonLaRepetition()
    {
        var jour = new DateTime(2026, 10, 20);
        CalendarEvent E(string date, string rep) => new() { Titre = "x", Date = date, Repetition = rep };

        // L'anniversaire de 1998 se range au 01/12/2026, où Ange a 28 ans.
        var anniv = DataStore.ProchaineOccurrence(E("1998-12-01", CalendarEventTypes.RepetitionAnnuelle), jour);
        Assert.Equal(new DateTime(2026, 12, 1), anniv);
        Assert.Equal(28, DataStore.Age("1998-12-01", anniv));
        Assert.Equal(new DateTime(2027, 3, 5), DataStore.ProchaineOccurrence(E("2020-03-05", CalendarEventTypes.RepetitionAnnuelle), jour));
        Assert.Equal(jour, DataStore.ProchaineOccurrence(E("2026-10-20", CalendarEventTypes.RepetitionAnnuelle), jour));
        // Un événement futur garde sa date.
        Assert.Equal(new DateTime(2027, 1, 10), DataStore.ProchaineOccurrence(E("2027-01-10", CalendarEventTypes.RepetitionAnnuelle), jour));
        // Hebdomadaire : même jour de la semaine (le 13/10/2026 est un mardi, comme le 20).
        Assert.Equal(jour, DataStore.ProchaineOccurrence(E("2026-10-13", CalendarEventTypes.RepetitionHebdomadaire), jour));
        Assert.Equal(new DateTime(2026, 10, 26), DataStore.ProchaineOccurrence(E("2026-10-12", CalendarEventTypes.RepetitionHebdomadaire), jour));
        // Mensuelle : un 31 saute les mois trop courts.
        Assert.Equal(new DateTime(2026, 10, 31), DataStore.ProchaineOccurrence(E("2026-01-31", CalendarEventTypes.RepetitionMensuelle), jour));
        Assert.Equal(new DateTime(2026, 11, 5), DataStore.ProchaineOccurrence(E("2026-01-05", CalendarEventTypes.RepetitionMensuelle), jour));
        // Sans répétition : la date de l'événement, même passée.
        Assert.Equal(new DateTime(2026, 1, 5), DataStore.ProchaineOccurrence(E("2026-01-05", CalendarEventTypes.RepetitionAucune), jour));
    }

    [Fact]
    public void Rappel_AnniversaireDonneLAgeFete()
    {
        var naissance = DateTime.Today.AddDays(5).AddYears(-30);
        var u = _store.FindUserByIdentifier("lalao")!;
        u.DateNaissance = naissance.ToString("yyyy-MM-dd");
        Assert.True(_store.UpdateUser(u.Id, u, false).Success);
        Assert.Equal(29, u.Age);

        var rappel = Assert.Single(_store.GetRappels(), r => r.Id == "anniversaire:" + u.Id);
        Assert.Contains("30", rappel.Detail);
    }

    // ---------- Conversations ----------

    [Fact]
    public void RenommerConversation_APlusieurs()
    {
        Connecter("lalao");
        Assert.True(_store.SendMessage(new() { "hery", "tiana" }, "Bonjour à vous deux").Success);
        var cid = _store.GetConversations("lalao")[0].ConversationId;
        Assert.False(DataStore.EstDiscussionDeGroupe(cid));
        Assert.Equal("", _store.NomDiscussion(cid));
        Assert.True(_store.PeutRenommerConversation(cid));

        Assert.False(_store.RenommerConversation(cid, new string('a', 81)).Success);
        Assert.True(_store.RenommerConversation(cid, "  Trio du dimanche ").Success);
        Assert.Equal("Trio du dimanche", _store.NomDiscussion(cid));

        // Les autres voient le nom, et les réponses le gardent.
        Connecter("hery");
        Assert.Equal("Trio du dimanche", _store.NomDiscussion(cid));
        Assert.True(_store.RepondreConversation(cid, "Merci").Success);
        Assert.Equal("Trio du dimanche", _store.NomDiscussion(cid));
        Assert.True(_store.RenommerConversation(cid, "Équipe accueil").Success);
        Assert.Equal("Équipe accueil", _store.NomDiscussion(cid));

        // Un nom vide la remet sans nom.
        Assert.True(_store.RenommerConversation(cid, "").Success);
        Assert.Equal("", _store.NomDiscussion(cid));
    }

    [Fact]
    public void RenommerConversation_RefuseeADeuxOuPourUnGroupe()
    {
        Connecter("lalao");
        Assert.True(_store.SendMessage(new() { "hery" }, "Salut").Success);
        var duo = _store.GetConversations("lalao")[0].ConversationId;
        Assert.False(_store.PeutRenommerConversation(duo));
        Assert.False(_store.RenommerConversation(duo, "Nous deux").Success);

        _store.Logout();
        Assert.True(_store.Login("admin", "admin123").Success);
        Assert.True(_store.AjouterGroupe(new Groupe { Nom = "Chorale", Responsables = new() { "lalao" }, Membres = new() { "hery", "tiana" } }).Success);
        Connecter("lalao");
        Assert.True(_store.EcrireAuGroupe(_store.Groupes[0].Id, "Répétition").Success);
        var chorale = DataStore.PrefixeGroupe + _store.Groupes[0].Id;
        Assert.False(_store.PeutRenommerConversation(chorale));
        Assert.False(_store.RenommerConversation(chorale, "Autre nom").Success);
        Assert.Equal("Chorale", _store.NomDiscussion(chorale));

        // Deux conversations ne portent pas le même nom.
        Assert.True(_store.SendMessage(new() { "hery", "tiana" }, "Bonjour").Success);
        var trio = _store.GetConversations("lalao").First(c => c.ConversationId.StartsWith("mp:")
            && _store.ParticipantsConversation(c.ConversationId).Count == 3).ConversationId;
        Assert.False(_store.RenommerConversation(trio, "chorale").Success);
    }

    // ---------- Export des membres ----------

    private static List<string> EnTetes(byte[] xlsx)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        var xml = new StreamReader((zip.GetEntry("xl/sharedStrings.xml") ?? zip.GetEntry("xl/worksheets/sheet1.xml"))!.Open()).ReadToEnd();
        return System.Text.RegularExpressions.Regex.Matches(xml, "<t[^>]*>([^<]*)</t>")
            .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
    }

    [Fact]
    public void ExportMembres_ChampsPersonnalisesEtFiche()
    {
        Assert.True(_store.AddField(new FieldDefinition { Label = "Paroisse", Type = FieldType.Texte }).Success);
        Assert.True(_store.AddField(new FieldDefinition { Label = "Baptisé", Type = FieldType.Booleen }).Success);
        var paroisse = _store.ChampsPersonnalises.First(f => f.Label == "Paroisse");
        var baptise = _store.ChampsPersonnalises.First(f => f.Label == "Baptisé");
        var u = _store.FindUserByIdentifier("hery")!;
        u.DateNaissance = "2015-06-01";
        u.ResponsableLegal = "Rabe Naina";
        u.ResponsableTelephone = "+261340000000";
        u.Champs[paroisse.Id] = new FieldValue { Texte = "Ambohimanarina" };
        u.Champs[baptise.Id] = new FieldValue { Booleen = true };
        Assert.True(_store.UpdateUser(u.Id, u, false).Success);
        Assert.True(_store.AjouterGroupe(new Groupe { Nom = "Chorale", Membres = new() { "hery" } }).Success);

        var bytes = _store.ExportUsersExcel();
        var textes = EnTetes(bytes);
        foreach (var attendu in new[] { "Paroisse", "Baptisé", "Âge", "Date de décès", "Responsable légal", "Famille", "Groupes et ministères",
                     "Ambohimanarina", "Oui", "Rabe Naina", "Chorale", "01/06/2015", u.Age!.Value.ToString() })
            Assert.Contains(attendu, textes);

        // Réimport : les champs personnalisés et le responsable légal reviennent.
        var parsed = _store.ParseExcelImport(bytes);
        Assert.True(parsed.Success, parsed.Error);
        var relu = parsed.Users!.First(x => x.Identifiant == "hery");
        Assert.Equal("Ambohimanarina", relu.GetChampTexte(paroisse.Id));
        Assert.True(relu.GetChamp(baptise.Id)!.Booleen);
        Assert.Equal("Rabe Naina", relu.ResponsableLegal);
        Assert.Equal("+261340000000", relu.ResponsableTelephone);
        Assert.Equal("2015-06-01", relu.DateNaissance);
    }

    // ---------- Carte de membre ----------

    [Fact]
    public void Carte_FormatBadgeParDefaut()
    {
        Assert.True(new AppSettings().CarteEnBadge);
        Assert.Equal((85.6, 54.0), new AppSettings().CarteTailleMm);
        // Anciens réglages restés à 85 × 55 : passent au badge standard.
        Assert.True(new AppSettings { CarteLargeurMm = 85, CarteHauteurMm = 55 }.CarteEnBadge);
        // Une taille déjà personnalisée est gardée.
        Assert.Equal((90.0, 60.0), new AppSettings { CarteLargeurMm = 90, CarteHauteurMm = 60 }.CarteTailleMm);
        Assert.Equal((85.0, 55.0), new AppSettings { CarteFormat = AppSettings.CarteFormatPerso, CarteLargeurMm = 85, CarteHauteurMm = 55 }.CarteTailleMm);
        Assert.True(new AppSettings { CarteFormat = AppSettings.CarteFormatBadge, CarteLargeurMm = 90, CarteHauteurMm = 60 }.CarteEnBadge);
    }
}
