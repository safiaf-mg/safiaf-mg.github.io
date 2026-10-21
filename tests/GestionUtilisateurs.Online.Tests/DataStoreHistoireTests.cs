using GestionUtilisateurs.Shared.Models;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Blazor.Tests;

[Collection("Langue")]
public class DataStoreHistoireTests
{
    private static DataStore NewAdminStore()
    {
        var store = new DataStore(new MemoryStorageProvider());
        store.EnsureInitialized();
        Assert.True(store.Login("admin", "admin123").Success);
        return store;
    }

    private static CommunityPhoto Photo(string legende = "") => new() { Image = "data:image/png;base64,AAAA", Legende = legende };

    [Fact]
    public void DateSouple_AnneeMoisOuJour()
    {
        try
        {
            L.Definir(L.Fr);
            Assert.Equal("annee", DataStore.PrecisionDateHistoire("1993"));
            Assert.Equal("mois", DataStore.PrecisionDateHistoire("03/2001"));
            Assert.Equal("jour", DataStore.PrecisionDateHistoire("15/08/2018"));

            Assert.Equal("1993", DataStore.FormatDateHistoire("1993"));
            Assert.Equal("Mars 2001", DataStore.FormatDateHistoire("03/2001"));
            Assert.Equal("15/08/2018", DataStore.FormatDateHistoire("15/08/2018"));
            Assert.Equal("", DataStore.FormatDateHistoire(""));

            Assert.Equal("2001", DataStore.AnneeHistoire("03/2001"));
            Assert.Equal("2018", DataStore.AnneeHistoire("15/08/2018"));
            Assert.Equal("", DataStore.AnneeHistoire(""));

            L.Definir(L.En);
            Assert.Equal("March 2001", DataStore.FormatDateHistoire("03/2001"));
        }
        finally { L.Definir(L.Fr); }
    }

    [Fact]
    public void Chapitre_PlusieursPhotosAvecLegende()
    {
        var store = NewAdminStore();
        var photos = Enumerable.Range(0, DataStore.MaxPhotosChapitre + 2).Select(i => Photo(" Photo " + i + " ")).ToList();
        photos.Insert(1, new CommunityPhoto { Image = "" });
        Assert.True(store.AddCommunityChapter("La fondation", "Début", "1993", photos).Success);

        var c = store.Community.Chapitres.Single(x => x.Titre == "La fondation");
        Assert.Equal("1993", c.Date);
        var lues = DataStore.PhotosChapitre(c);
        Assert.Equal(DataStore.MaxPhotosChapitre, lues.Count);
        Assert.Equal("Photo 0", lues[0].Legende);
        Assert.Equal("Photo 1", lues[1].Legende);

        Assert.True(store.UpdateCommunityChapter(c.Id, "La fondation", "Début", "03/1993", new[] { Photo("Seule") }).Success);
        c = store.Community.Chapitres.Single(x => x.Id == c.Id);
        Assert.Equal("03/1993", c.Date);
        Assert.Equal("Seule", Assert.Single(DataStore.PhotosChapitre(c)).Legende);
    }

    [Fact]
    public void AncienneImageUnique_LueCommePremierePhoto()
    {
        var store = NewAdminStore();
        Assert.True(store.AddCommunityChapter("Avant", "", "").Success);
        var c = store.Community.Chapitres.Single(x => x.Titre == "Avant");
        c.Image = "data:image/png;base64,BBBB";
        c.Photos.Clear();

        var photo = Assert.Single(DataStore.PhotosChapitre(c));
        Assert.Equal(c.Image, photo.Image);

        // En enregistrant le chapitre, l'ancienne image passe dans la liste des photos.
        Assert.True(store.UpdateCommunityChapter(c.Id, "Avant", "", "", DataStore.PhotosChapitre(c)).Success);
        Assert.Null(c.Image);
        Assert.Equal("data:image/png;base64,BBBB", Assert.Single(c.Photos).Image);
    }
}
