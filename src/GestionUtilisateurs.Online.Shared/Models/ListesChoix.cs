using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Shared.Models;

/// <summary>Un choix d'une liste, tel que réglé par un gestionnaire (Paramètres › Listes de choix).</summary>
public class ElementListe
{
    /// <summary>Valeur enregistrée dans les données (ne change jamais).</summary>
    public string Cle { get; set; } = "";
    /// <summary>Libellé choisi ; vide = libellé d'origine (traduit).</summary>
    public string Libelle { get; set; } = "";
    /// <summary>Icône choisie ; vide = icône d'origine.</summary>
    public string Icone { get; set; } = "";
    /// <summary>Masqué des formulaires (les données existantes le gardent).</summary>
    public bool Masque { get; set; }
    /// <summary>Liens de famille ajoutés : le lien vu depuis l'autre fiche (vide = le même).</summary>
    public string Inverse { get; set; } = "";
    /// <summary>Libellé en anglais (« en ») et en malgache (« mg ») ; vide = libellé français.</summary>
    public Dictionary<string, string> Traductions { get; set; } = new();
    /// <summary>Choix d'origine supprimé de la liste (« Rétablir » le fait revenir).</summary>
    public bool Supprime { get; set; }

    /// <summary>Libellé réglé dans la langue affichée (null : aucun réglage).</summary>
    public string? LibelleAffiche()
    {
        var langue = L.LangueAffichee;
        if (langue != L.Fr && Traductions != null && Traductions.TryGetValue(langue, out var t) && !string.IsNullOrWhiteSpace(t))
            return t.Trim();
        return string.IsNullOrWhiteSpace(Libelle) ? null : Libelle.Trim();
    }
}

public class ListeConfig
{
    /// <summary>Choix dans l'ordre d'affichage (ceux d'origine et ceux ajoutés).</summary>
    public List<ElementListe> Elements { get; set; } = new();
}

/// <summary>Une liste réglable : ses choix d'origine et ce qu'on peut y changer.</summary>
public sealed record ListeDefinition(
    string Id,
    string Titre,
    string Module,
    bool AjoutPermis,
    Func<IReadOnlyList<(string Cle, string Libelle, string Icone)>> Origine,
    bool AvecInverse = false);

/// <summary>Un choix effectif : réglages appliqués aux choix d'origine.</summary>
public sealed record ChoixListe(string Cle, string Libelle, string Icone, bool Masque, bool DOrigine, string Inverse);

/// <summary>
/// Listes de choix de l'application (types, liens de famille, priorités…)
/// que les gestionnaires règlent eux-mêmes : renommer, changer l'icône,
/// masquer, réordonner, et pour la plupart ajouter ou supprimer des choix.
/// Les valeurs déjà enregistrées ne changent jamais.
/// </summary>
public static class ListesChoix
{
    public const string TypesSituation = "situations.types";
    public const string LiensDeFamille = "famille.liens";
    public const string TypesEvenement = "calendrier.types";
    public const string Priorites = "priorites";
    public const string CategoriesDocument = "documents.categories";
    public const string EtatsMateriel = "materiel.etats";
    public const string StatutsTache = "taches.statuts";
    public const string StatutsSituation = "situations.statuts";
    public const string StatutsCotisation = "cotisations.statuts";
    public const string TypesTresorerie = "tresorerie.types";
    public const string StatutsPresence = "presence.statuts";

    /// <summary>Réglages en cours (branché sur les paramètres de l'application au démarrage).</summary>
    public static Func<Dictionary<string, ListeConfig>?> Source { get; set; } = () => null;

    private static IReadOnlyList<(string, string, string)> Origine(IEnumerable<string> cles, Func<string, string> libelle, Func<string, string>? icone = null)
        => cles.Select(c => (c, libelle(c), icone?.Invoke(c) ?? "")).ToList();

    public static readonly ListeDefinition[] Definitions =
    {
        new(TypesSituation, "Types de famangiana et situations", "Famangiana et situations", true,
            () => Origine(SituationTypes.Tous, SituationTypes.LabelOrigine, SituationTypes.IconOrigine)),
        new(LiensDeFamille, "Liens de famille", "Utilisateurs", true,
            () => Origine(LiensFamille.Tous, LiensFamille.LibelleOrigine), AvecInverse: true),
        new(TypesEvenement, "Types d'événement", "Calendrier", true,
            () => Origine(CalendarEventTypes.Tous, CalendarEventTypes.LabelOrigine, CalendarEventTypes.IconOrigine)),
        new(Priorites, "Priorités", "Calendrier, tâches, situations", true,
            () => Origine(CalendarEventTypes.PrioritesOrigine, CalendarEventTypes.PrioriteLabelOrigine)),
        new(CategoriesDocument, "Catégories de documents", "Gestion documentaire", true,
            () => Origine(DocumentCategories.Toutes, DocumentCategories.LabelOrigine, DocumentCategories.IconOrigine)),
        new(EtatsMateriel, "États du matériel", "Matériel", true,
            () => Origine(MaterielEtats.Tous, MaterielEtats.LabelOrigine)),
        new(StatutsTache, "Statuts des tâches", "Tâches et suivi", false,
            () => Origine(TacheStatuts.Tous, TacheStatuts.LabelOrigine, TacheStatuts.IconOrigine)),
        new(StatutsSituation, "Statuts des suivis", "Famangiana et situations", false,
            () => Origine(SituationStatuts.Tous, SituationStatuts.LabelOrigine, SituationStatuts.IconOrigine)),
        new(StatutsCotisation, "Statuts des cotisations", "Finances", false,
            () => Origine(CotisationStatuts.Tous, CotisationStatuts.LabelOrigine)),
        new(TypesTresorerie, "Mouvements de trésorerie", "Finances", false,
            () => Origine(TresorerieTypes.Tous, TresorerieTypes.LabelOrigine, TresorerieTypes.IconOrigine)),
        new(StatutsPresence, "Statuts de présence", "Présence et réunions", false,
            () => Origine(PresenceStatuts.Tous, PresenceStatuts.LabelOrigine, PresenceStatuts.IconOrigine)),
    };

    public static ListeDefinition? Definition(string id) => Definitions.FirstOrDefault(d => d.Id == id);

    /// <summary>Choix effectifs d'une liste, dans l'ordre réglé (masqués compris).</summary>
    public static IReadOnlyList<ChoixListe> Elements(string id, Dictionary<string, ListeConfig>? config = null)
    {
        var def = Definition(id);
        if (def == null) return Array.Empty<ChoixListe>();
        var origine = def.Origine();
        var parCle = origine.ToDictionary(o => o.Cle, o => o);
        var resultat = new List<ChoixListe>();
        var vus = new HashSet<string>(StringComparer.Ordinal);
        config ??= Source();
        if (config != null && config.TryGetValue(id, out var liste))
        {
            foreach (var e in liste.Elements ?? new List<ElementListe>())
            {
                if (string.IsNullOrWhiteSpace(e.Cle) || !vus.Add(e.Cle)) continue;
                var dOrigine = parCle.TryGetValue(e.Cle, out var o);
                // Supprimé, ou choix d'origine retiré de l'application (sans libellé à lui) : ignoré.
                if (e.Supprime || (!dOrigine && string.IsNullOrWhiteSpace(e.Libelle))) continue;
                var libelle = e.LibelleAffiche() ?? (dOrigine ? o.Libelle : e.Cle);
                var icone = !string.IsNullOrWhiteSpace(e.Icone) ? e.Icone.Trim() : dOrigine ? o.Icone : "";
                resultat.Add(new ChoixListe(e.Cle, libelle, icone, e.Masque, dOrigine, dOrigine ? "" : e.Inverse ?? ""));
            }
        }
        // Un choix d'origine absent des réglages (ajouté par une mise à jour) apparaît à la fin.
        foreach (var o in origine.Where(o => !vus.Contains(o.Cle)))
            resultat.Add(new ChoixListe(o.Cle, o.Libelle, o.Icone, false, true, ""));
        return resultat;
    }

    /// <summary>Choix d'origine supprimés d'une liste (pour les remettre).</summary>
    public static IReadOnlyList<ChoixListe> Supprimes(string id, Dictionary<string, ListeConfig>? config = null)
    {
        var def = Definition(id);
        config ??= Source();
        if (def == null || config == null || !config.TryGetValue(id, out var liste)) return Array.Empty<ChoixListe>();
        var supprimes = (liste.Elements ?? new List<ElementListe>()).Where(e => e.Supprime).Select(e => e.Cle).ToHashSet();
        return def.Origine().Where(o => supprimes.Contains(o.Cle))
            .Select(o => new ChoixListe(o.Cle, o.Libelle, o.Icone, true, true, "")).ToList();
    }

    /// <summary>Choix proposés dans un formulaire : les visibles, plus la valeur actuelle même masquée.</summary>
    public static IReadOnlyList<ChoixListe> Choix(string id, string? courant = null)
        => Elements(id).Where(e => !e.Masque || e.Cle == courant).ToList();

    /// <summary>La valeur existe dans la liste (choix d'origine ou ajouté).</summary>
    public static bool Contient(string id, string? cle, Dictionary<string, ListeConfig>? config = null)
        => !string.IsNullOrEmpty(cle) && Elements(id, config).Any(e => e.Cle == cle);

    private static ElementListe? Regle(string id, string cle)
        => Source() is { } config && config.TryGetValue(id, out var liste)
            ? liste.Elements?.FirstOrDefault(e => e.Cle == cle)
            : null;

    /// <summary>Libellé réglé par un gestionnaire (null : libellé d'origine).</summary>
    public static string? LibellePerso(string id, string? cle)
        => cle != null && Regle(id, cle) is { } e ? e.LibelleAffiche() : null;

    /// <summary>Icône réglée par un gestionnaire (null : icône d'origine).</summary>
    public static string? IconePerso(string id, string? cle)
        => cle != null && Regle(id, cle) is { } e && !string.IsNullOrWhiteSpace(e.Icone) ? e.Icone.Trim() : null;

    /// <summary>Vrai si la clé est un choix ajouté par un gestionnaire.</summary>
    public static bool EstAjoute(string id, string? cle)
        => cle != null && Regle(id, cle) != null && Definition(id)?.Origine().All(o => o.Cle != cle) == true;

    /// <summary>Lien inverse d'un lien de famille ajouté (vide : le même lien).</summary>
    public static string? InversePerso(string? cle)
        => EstAjoute(LiensDeFamille, cle) ? (Regle(LiensDeFamille, cle!)!.Inverse is { Length: > 0 } inv ? inv : cle) : null;
}
