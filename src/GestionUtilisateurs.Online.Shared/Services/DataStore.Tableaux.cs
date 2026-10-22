using System.Globalization;
using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Tableaux personnalisés (Paramètres › Tableaux) : les gestionnaires créent des
/// tableaux de rubriques (colonnes et lignes libres). Un tableau lié à un champ
/// de montant (participation des famangiana, cotisations, trésorerie) en fait une
/// liste de choix : choisir une ligne remplit le montant.
/// </summary>
public partial class DataStore
{
    private const int NomTableauMax = 80;
    private const int NomColonneMax = 60;
    private const int CelluleMax = 200;
    private const int ColonnesMax = 20;
    private const int LignesMax = 500;

    public IReadOnlyList<TableauPerso> Tableaux => _settings.Tableaux ??= new List<TableauPerso>();

    public TableauPerso? FindTableau(string? id)
        => string.IsNullOrEmpty(id) ? null : Tableaux.FirstOrDefault(t => t.Id == id);

    /// <summary>Tableau lié à ce champ (null : saisie libre seulement).</summary>
    public TableauPerso? TableauDuChamp(string champ)
        => Tableaux.FirstOrDefault(t => t.Champs.Contains(champ) && t.ColonneMontant != null);

    /// <summary>Lignes proposées pour ce champ, dans l'ordre du tableau.</summary>
    public IReadOnlyList<RubriqueTableau> Rubriques(string champ)
        => TableauDuChamp(champ) is { } t ? RubriquesDe(t) : Array.Empty<RubriqueTableau>();

    public static IReadOnlyList<RubriqueTableau> RubriquesDe(TableauPerso t)
    {
        var montant = t.ColonneMontant;
        var libelle = t.ColonneLibelle;
        var autres = t.Colonnes.Where(c => c != montant && c != libelle).ToList();
        var resultat = new List<RubriqueTableau>();
        foreach (var l in t.Lignes)
        {
            var m = montant == null ? null : TableauPerso.LireMontant(l.Valeur(montant.Id));
            var nom = libelle == null ? "" : l.Valeur(libelle.Id).Trim();
            if (nom.Length == 0 && m == null) continue;
            if (nom.Length == 0) nom = FormatMontant(m!.Value);
            var details = string.Join(" · ", autres.Select(c => l.Valeur(c.Id).Trim()).Where(v => v.Length > 0));
            resultat.Add(new RubriqueTableau(l.Id, nom, m, details));
        }
        return resultat;
    }

    private OperationResult? RefusTableau()
        => IsManager && Can(Permissions.SettingsManage) ? null : OperationResult.Fail(L.T("Action réservée aux gestionnaires."));

    /// <summary>Crée ou remplace un tableau (le brouillon vient de l'éditeur).</summary>
    public OperationResult EnregistrerTableau(TableauPerso brouillon)
    {
        if (RefusTableau() is { } refus) return refus;
        var t = brouillon.Copie();
        t.Nom = (t.Nom ?? "").Trim();
        t.Description = (t.Description ?? "").Trim();
        if (t.Nom.Length == 0) return OperationResult.Fail(L.T("Le nom du tableau est obligatoire."));
        if (t.Nom.Length > NomTableauMax) return OperationResult.Fail(L.F("Le nom ne doit pas dépasser {0} caractères.", NomTableauMax));
        var existant = FindTableau(t.Id);
        if (Tableaux.Any(x => x != existant && string.Equals(x.Nom.Trim(), t.Nom, StringComparison.CurrentCultureIgnoreCase)))
            return OperationResult.Fail(L.F("Un tableau « {0} » existe déjà.", t.Nom));

        // Colonnes : un nom chacune, sans doublon.
        if (t.Colonnes.Count == 0) return OperationResult.Fail(L.T("Ajoutez au moins une colonne."));
        if (t.Colonnes.Count > ColonnesMax) return OperationResult.Fail(L.F("Un tableau ne peut pas avoir plus de {0} colonnes.", ColonnesMax));
        foreach (var c in t.Colonnes)
        {
            c.Nom = (c.Nom ?? "").Trim();
            if (c.Nom.Length == 0) return OperationResult.Fail(L.T("Chaque colonne doit avoir un nom."));
            if (c.Nom.Length > NomColonneMax) return OperationResult.Fail(L.F("Le nom d'une colonne ne doit pas dépasser {0} caractères.", NomColonneMax));
            if (string.IsNullOrWhiteSpace(c.Id)) c.Id = Guid.NewGuid().ToString("N")[..8];
        }
        var doublon = t.Colonnes.GroupBy(c => c.Nom, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (doublon != null) return OperationResult.Fail(L.F("La colonne « {0} » existe déjà dans ce tableau.", doublon.Key));
        if (t.Colonnes.Select(c => c.Id).Distinct().Count() != t.Colonnes.Count)
            return OperationResult.Fail(L.T("Colonnes en double."));

        // Lignes : les cellules des colonnes supprimées partent, les lignes vides aussi.
        var ids = t.Colonnes.Select(c => c.Id).ToHashSet();
        var lignes = new List<LigneTableau>();
        foreach (var l in t.Lignes)
        {
            var cellules = new Dictionary<string, string>();
            foreach (var c in t.Colonnes)
            {
                var v = l.Valeur(c.Id).Trim();
                if (v.Length == 0) continue;
                if (v.Length > CelluleMax) return OperationResult.Fail(L.F("Une case ne doit pas dépasser {0} caractères.", CelluleMax));
                if (c.Montant)
                {
                    if (TableauPerso.LireMontant(v) is not { } m || m < 0)
                        return OperationResult.Fail(L.F("« {0} » n'est pas un montant valable (colonne « {1} »).", v, c.Nom));
                    v = m.ToString(CultureInfo.InvariantCulture);
                }
                cellules[c.Id] = v;
            }
            if (cellules.Count == 0) continue;
            lignes.Add(new LigneTableau { Id = string.IsNullOrWhiteSpace(l.Id) ? Guid.NewGuid().ToString("N")[..8] : l.Id, Cellules = cellules });
        }
        if (lignes.Count > LignesMax) return OperationResult.Fail(L.F("Un tableau ne peut pas avoir plus de {0} lignes.", LignesMax));
        t.Lignes = lignes;

        // Champs liés : connus, et seulement avec une colonne de montants.
        t.Champs = t.Champs.Where(c => ChampsLiables.Definition(c) != null).Distinct().ToList();
        if (t.Champs.Count > 0 && t.ColonneMontant == null)
            return OperationResult.Fail(L.T("Pour lier ce tableau à un champ, cochez « Montant » sur une colonne."));

        var tableaux = _settings.Tableaux ??= new List<TableauPerso>();
        // Un champ ne suit qu'un tableau : le lier ici le retire des autres.
        foreach (var autre in tableaux.Where(x => x != existant))
            autre.Champs.RemoveAll(t.Champs.Contains);
        if (existant == null)
        {
            if (string.IsNullOrWhiteSpace(t.Id) || tableaux.Any(x => x.Id == t.Id)) t.Id = Guid.NewGuid().ToString("N");
            tableaux.Add(t);
        }
        else tableaux[tableaux.IndexOf(existant)] = t;
        SaveSettings();
        LogActivity(ActivityTypes.Update, (existant == null ? "Tableau créé : « " : "Tableau modifié : « ") + t.Nom + " »");
        DataChanged?.Invoke();
        brouillon.Id = t.Id;
        return OperationResult.Ok();
    }

    public OperationResult SupprimerTableau(string id)
    {
        if (RefusTableau() is { } refus) return refus;
        var t = FindTableau(id);
        if (t == null) return OperationResult.Fail(L.T("Tableau introuvable."));
        _settings.Tableaux.Remove(t);
        SaveSettings();
        LogActivity(ActivityTypes.Update, "Tableau supprimé : « " + t.Nom + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Déplace un tableau dans la liste (sens -1 : monter).</summary>
    public OperationResult DeplacerTableau(string id, int sens)
    {
        if (RefusTableau() is { } refus) return refus;
        var liste = _settings.Tableaux;
        var i = liste.FindIndex(t => t.Id == id);
        var j = i + Math.Sign(sens);
        if (i < 0 || j < 0 || j >= liste.Count) return OperationResult.Ok();
        (liste[i], liste[j]) = (liste[j], liste[i]);
        SaveSettings();
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>
    /// Modèle prêt à remplir : « Barème des famangiana », une ligne par type de
    /// famangiana (sauf « Autre »), lié à la participation remise.
    /// </summary>
    public TableauPerso ModeleBaremeFamangiana()
    {
        var rubrique = new ColonneTableau { Id = "rubrique", Nom = L.T("Rubrique") };
        var montant = new ColonneTableau { Id = "montant", Nom = L.T("Montant (Ar)"), Montant = true };
        var t = new TableauPerso
        {
            Nom = L.T("Barème des famangiana"),
            Description = L.T("Participation remise selon le type de famangiana."),
            Colonnes = { rubrique, montant },
            Champs = { ChampsLiables.Participation }
        };
        foreach (var e in ListesChoix.Choix(ListesChoix.TypesSituation)
                     .Where(e => e.Cle is not (SituationTypes.Visite or SituationTypes.Accompagnement or SituationTypes.EvenementImportant or SituationTypes.Autre)))
            t.Lignes.Add(new LigneTableau { Id = Guid.NewGuid().ToString("N")[..8], Cellules = { [rubrique.Id] = e.Libelle } });
        return t;
    }

    /// <summary>Ligne dont la rubrique porte ce nom (accents et majuscules ignorés).</summary>
    public RubriqueTableau? RubriqueNommee(string champ, string? nom)
    {
        var cle = CleDoublon(nom ?? "");
        return cle.Length == 0 ? null : Rubriques(champ).FirstOrDefault(r => CleDoublon(r.Libelle) == cle);
    }
}
