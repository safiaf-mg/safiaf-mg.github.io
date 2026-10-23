using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Blazor.Tests;

/// <summary>
/// Le geste « retour » ferme d'abord l'écran interne d'une page (fiche d'un
/// membre, formulaire…) avant de revenir à la page précédente.
/// </summary>
public class AppStateRetourTests
{
    [Fact]
    public void Retour_FermeLaSousVueAvantDeChangerDePage()
    {
        var state = new AppState();
        state.Navigate("users");
        var page = new object();
        var fermee = 0;
        state.SetSousVue(page, () => fermee++);

        state.GoBack();

        Assert.Equal(1, fermee);
        Assert.Equal("users", state.CurrentPage);
        Assert.True(state.HasHistory);

        state.GoBack();

        Assert.Equal(1, fermee);
        Assert.Equal("dashboard", state.CurrentPage);
        Assert.False(state.HasHistory);
    }

    [Fact]
    public void SousVue_SurLEcranDeDepart_ActiveLeRetour()
    {
        var state = new AppState();
        var page = new object();
        Assert.False(state.HasHistory);

        state.SetSousVue(page, () => { });
        Assert.True(state.HasHistory);

        state.SetSousVue(page, null);
        Assert.False(state.HasHistory);
    }

    [Fact]
    public void ChangerDePage_OublieLaSousVue_EtLAnciennePageNeRetireQueLaSienne()
    {
        var state = new AppState();
        var ancienne = new object();
        var nouvelle = new object();
        var fermee = false;
        state.SetSousVue(ancienne, () => fermee = true);

        state.Navigate("guide");
        state.SetSousVue(nouvelle, () => { });
        state.SetSousVue(ancienne, null);

        Assert.True(state.HasHistory);
        state.GoBack();
        Assert.False(fermee);
        Assert.Equal("guide", state.CurrentPage);
    }
}
