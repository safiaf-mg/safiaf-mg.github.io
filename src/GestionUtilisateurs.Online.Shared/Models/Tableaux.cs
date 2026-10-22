using System.Globalization;
using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Shared.Models;

/// <summary>Colonne d'un tableau personnalisé.</summary>
public class ColonneTableau
{
    public string Id { get; set; } = "";
    public string Nom { get; set; } = "";
    /// <summary>Colonne de montants (en Ariary) : c'est elle qui remplit les champs liés.</summary>
    public bool Montant { get; set; }
}

/// <summary>Ligne (rubrique) d'un tableau personnalisé : une valeur par colonne.</summary>
public class LigneTableau
{
    public string Id { get; set; } = "";
    /// <summary>Valeurs par identifiant de colonne (montants au format invariant, ex. « 20000 »).</summary>
    public Dictionary<string, string> Cellules { get; set; } = new();

    public string Valeur(string colonneId) => Cellules != null && Cellules.TryGetValue(colonneId, out var v) ? v ?? "" : "";
}

/// <summary>
/// Tableau créé par un gestionnaire (Paramètres › Tableaux) : des colonnes et des
/// lignes libres, par exemple un barème « Rubrique / Montant ». Lié à un champ de
/// montant, ses lignes deviennent une liste de choix dans le formulaire.
/// </summary>
public class TableauPerso
{
    public string Id { get; set; } = "";
    public string Nom { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ColonneTableau> Colonnes { get; set; } = new();
    public List<LigneTableau> Lignes { get; set; } = new();
    /// <summary>Champs liés (voir <see cref="ChampsLiables"/>) : leurs formulaires proposent les lignes.</summary>
    public List<string> Champs { get; set; } = new();

    /// <summary>Colonne qui donne le montant (la première colonne de montants).</summary>
    public ColonneTableau? ColonneMontant => Colonnes.FirstOrDefault(c => c.Montant);

    /// <summary>Colonne qui nomme la rubrique (la première qui n'est pas un montant).</summary>
    public ColonneTableau? ColonneLibelle => Colonnes.FirstOrDefault(c => !c.Montant);

    public TableauPerso Copie() => new()
    {
        Id = Id,
        Nom = Nom,
        Description = Description,
        Colonnes = Colonnes.Select(c => new ColonneTableau { Id = c.Id, Nom = c.Nom, Montant = c.Montant }).ToList(),
        Lignes = Lignes.Select(l => new LigneTableau { Id = l.Id, Cellules = new Dictionary<string, string>(l.Cellules ?? new()) }).ToList(),
        Champs = Champs.ToList()
    };

    public static double? LireMontant(string? texte)
    {
        var v = (texte ?? "").Trim().Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace(",", ".");
        if (v.EndsWith("ar", StringComparison.OrdinalIgnoreCase)) v = v[..^2];
        return v.Length > 0 && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}

/// <summary>Une ligne d'un tableau, telle que proposée dans un formulaire.</summary>
public sealed record RubriqueTableau(string LigneId, string Libelle, double? Montant, string Details)
{
    /// <summary>Texte de l'option : « Naissance — 20 000 Ar ».</summary>
    public string Texte => Libelle + (Montant is { } m ? " — " + DataStore.FormatMontant(m) : "") + (Details.Length > 0 ? " (" + Details + ")" : "");
}

/// <summary>Un champ de montant qu'un tableau peut remplir.</summary>
public sealed record ChampLiable(string Id, string Titre, string Module);

/// <summary>Champs de montant que l'on peut lier à un tableau (un tableau par champ).</summary>
public static class ChampsLiables
{
    public const string Participation = "famangiana.participation";
    public const string Cotisation = "finances.cotisation";
    public const string Tresorerie = "finances.tresorerie";

    public static readonly ChampLiable[] Tous =
    {
        new(Participation, "Participation remise", "Famangiana et situations"),
        new(Cotisation, "Montant des cotisations", "Finances"),
        new(Tresorerie, "Montant des mouvements de trésorerie", "Finances"),
    };

    public static ChampLiable? Definition(string id) => Tous.FirstOrDefault(c => c.Id == id);
}
