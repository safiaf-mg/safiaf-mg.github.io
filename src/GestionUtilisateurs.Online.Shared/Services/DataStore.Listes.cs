using System.Globalization;
using System.Text;
using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Listes de choix réglables (Paramètres › Listes de choix) : les gestionnaires
/// renomment, changent l'icône, masquent, réordonnent, ajoutent et suppriment
/// des choix. Les valeurs enregistrées dans les données ne changent jamais :
/// un choix d'origine se masque, un choix ajouté encore utilisé aussi.
/// </summary>
public partial class DataStore
{
    private const int LibelleListeMax = 80;

    /// <summary>Choix effectifs d'une liste, dans l'ordre réglé (masqués compris).</summary>
    public IReadOnlyList<ChoixListe> Liste(string id) => ListesChoix.Elements(id, _settings.Listes);

    /// <summary>Nombre de fiches qui utilisent ce choix.</summary>
    public int UtilisationsElement(string id, string cle) => id switch
    {
        ListesChoix.TypesSituation => _situations.Count(s => s.Type == cle),
        ListesChoix.LiensDeFamille => _users.Sum(u => (u.Famille ?? new List<LienFamille>()).Count(l => l.Type == cle)),
        ListesChoix.TypesEvenement => _events.Count(e => e.Type == cle),
        ListesChoix.Priorites => _events.Count(e => e.Priorite == cle) + _taches.Count(t => t.Priorite == cle) + _situations.Count(s => s.Priorite == cle),
        ListesChoix.CategoriesDocument => _documents.Count(d => d.Categorie == cle),
        ListesChoix.EtatsMateriel => _materiels.Count(m => m.Etat == cle),
        ListesChoix.StatutsTache => _taches.Count(t => t.Statut == cle),
        ListesChoix.StatutsSituation => _situations.Count(s => s.Statut == cle),
        ListesChoix.StatutsCotisation => _cotisations.Count(c => c.Statut == cle),
        ListesChoix.TypesTresorerie => _tresorerie.Count(t => t.Type == cle),
        _ => 0
    };

    private OperationResult? RefusListe(string id, out ListeDefinition def)
    {
        def = ListesChoix.Definition(id)!;
        if (!IsManager) return OperationResult.Fail(L.T("Action réservée aux gestionnaires."));
        if (def == null) return OperationResult.Fail(L.T("Liste inconnue."));
        return null;
    }

    /// <summary>Les réglages de la liste, complétés par les choix d'origine manquants.</summary>
    private ListeConfig ConfigListe(string id)
    {
        _settings.Listes ??= new Dictionary<string, ListeConfig>();
        var effectifs = ListesChoix.Elements(id, _settings.Listes);
        if (!_settings.Listes.TryGetValue(id, out var config) || config == null)
            _settings.Listes[id] = config = new ListeConfig();
        var regles = (config.Elements ?? new List<ElementListe>())
            .Where(e => !string.IsNullOrWhiteSpace(e.Cle))
            .GroupBy(e => e.Cle, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        config.Elements = effectifs.Select(e => regles.TryGetValue(e.Cle, out var r) ? r : new ElementListe { Cle = e.Cle })
            // Les choix d'origine supprimés restent notés, pour ne pas revenir d'eux-mêmes.
            .Concat(regles.Values.Where(r => r.Supprime && effectifs.All(e => e.Cle != r.Cle)))
            .ToList();
        return config;
    }

    /// <summary>La liste avec ses libellés français (pour comparer et enregistrer).</summary>
    private IReadOnlyList<ChoixListe> ListeFr(string id)
    {
        IReadOnlyList<ChoixListe> liste = Array.Empty<ChoixListe>();
        L.EnFrancais(() => { liste = Liste(id); return ""; });
        return liste;
    }

    /// <summary>Libellé d'origine en français (vide pour un choix ajouté).</summary>
    public static string LibelleOrigineFr(string id, string cle)
        => L.EnFrancais(() => ListesChoix.Definition(id)?.Origine().FirstOrDefault(o => o.Cle == cle).Libelle ?? "");

    /// <summary>Ce qui est réglé pour un choix : libellé français, traductions (« en », « mg ») et icône.</summary>
    public (string Francais, Dictionary<string, string> Traductions, string Icone) ReglageElement(string id, string cle)
    {
        var regle = _settings.Listes != null && _settings.Listes.TryGetValue(id, out var l)
            ? l.Elements?.FirstOrDefault(e => e.Cle == cle) : null;
        var origine = LibelleOrigineFr(id, cle);
        var francais = !string.IsNullOrWhiteSpace(regle?.Libelle) ? regle!.Libelle.Trim() : origine.Length > 0 ? origine : cle;
        var icone = Liste(id).FirstOrDefault(e => e.Cle == cle)?.Icone ?? "";
        return (francais, new Dictionary<string, string>(regle?.Traductions ?? new Dictionary<string, string>()), icone);
    }

    private static Dictionary<string, string> NettoyerTraductions(Dictionary<string, string>? traductions)
        => (traductions ?? new Dictionary<string, string>())
            .Where(t => t.Key is L.En or L.Mg && !string.IsNullOrWhiteSpace(t.Value))
            .ToDictionary(t => t.Key, t => t.Value.Trim());

    private void ListeModifiee(ListeDefinition def, string detail)
    {
        SaveSettings();
        LogActivity(ActivityTypes.Update, "Listes de choix, " + def.Titre + " : " + detail);
        DataChanged?.Invoke();
    }

    private string? VerifierLibelle(string id, string libelle, string? cleExclue)
    {
        if (libelle.Length == 0) return L.T("Le libellé est obligatoire.");
        if (libelle.Length > LibelleListeMax) return L.F("Le libellé ne doit pas dépasser {0} caractères.", LibelleListeMax);
        if (ListeFr(id).Any(e => e.Cle != cleExclue && e.Libelle.Equals(libelle, StringComparison.CurrentCultureIgnoreCase)))
            return L.F("Le choix « {0} » existe déjà dans cette liste.", libelle);
        return null;
    }

    private string? VerifierInverse(string id, string inverse, string? cle)
    {
        if (inverse.Length == 0 || inverse == cle) return null;
        return Liste(id).Any(e => e.Cle == inverse) ? null : L.T("Le lien inverse choisi n'existe pas.");
    }

    /// <summary>Clé ascii stable tirée du libellé (« perso-belle-soeur »).</summary>
    private string NouvelleCle(string id, string libelle)
    {
        var sb = new StringBuilder();
        foreach (var c in libelle.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
            else if (c is >= 'A' and <= 'Z') sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var racine = "perso-" + (sb.ToString().Trim('-') is { Length: > 0 } s ? s[..Math.Min(s.Length, 40)].Trim('-') : Guid.NewGuid().ToString("N")[..8]);
        var existantes = Liste(id).Select(e => e.Cle).ToHashSet(StringComparer.Ordinal);
        var cle = racine;
        for (var n = 2; existantes.Contains(cle); n++) cle = racine + "-" + n;
        return cle;
    }

    /// <summary>Ajoute un choix à une liste (les listes de statuts n'en acceptent pas).</summary>
    public OperationResult AjouterElementListe(string id, string libelle, string icone = "", string inverse = "", Dictionary<string, string>? traductions = null)
    {
        if (RefusListe(id, out var def) is { } refus) return refus;
        if (!def.AjoutPermis) return OperationResult.Fail(L.T("On ne peut pas ajouter de choix à cette liste."));
        libelle = (libelle ?? "").Trim();
        icone = (icone ?? "").Trim();
        inverse = def.AvecInverse ? (inverse ?? "").Trim() : "";
        if (VerifierLibelle(id, libelle, null) is { } erreur) return OperationResult.Fail(erreur);
        if (icone.Length > 16) return OperationResult.Fail(L.T("L'icône doit être un emoji ou un symbole court."));
        if (VerifierInverse(id, inverse, null) is { } erreurInverse) return OperationResult.Fail(erreurInverse);
        var trad = NettoyerTraductions(traductions);
        if (trad.Values.Any(v => v.Length > LibelleListeMax)) return OperationResult.Fail(L.F("Le libellé ne doit pas dépasser {0} caractères.", LibelleListeMax));

        var cle = NouvelleCle(id, libelle);
        var config = ConfigListe(id);
        config.Elements.Add(new ElementListe { Cle = cle, Libelle = libelle, Icone = icone, Inverse = inverse, Traductions = trad });
        LierInverse(id, config, cle, inverse);
        ListeModifiee(def, "ajout de « " + libelle + " »");
        return OperationResult.Ok();
    }

    /// <summary>
    /// Renomme un choix (en français, et en anglais et malgache si on le souhaite), change son
    /// icône et, pour un lien de famille ajouté, son lien inverse. Pour un choix d'origine, un
    /// libellé ou une icône vide rétablit l'original. <paramref name="traductions"/> null : inchangées.
    /// </summary>
    public OperationResult ModifierElementListe(string id, string cle, string libelle, string icone = "", string? inverse = null, Dictionary<string, string>? traductions = null)
    {
        if (RefusListe(id, out var def) is { } refus) return refus;
        var actuel = ListeFr(id).FirstOrDefault(e => e.Cle == cle);
        if (actuel == null) return OperationResult.Fail(L.T("Choix introuvable."));
        var origine = L.EnFrancais(() => def.Origine().FirstOrDefault(o => o.Cle == cle).Libelle ?? "");
        var origineIcone = def.Origine().FirstOrDefault(o => o.Cle == cle).Icone ?? "";
        libelle = (libelle ?? "").Trim();
        icone = (icone ?? "").Trim();
        if (actuel.DOrigine && libelle.Length == 0) libelle = origine;
        if (VerifierLibelle(id, libelle, cle) is { } erreur) return OperationResult.Fail(erreur);
        if (icone.Length > 16) return OperationResult.Fail(L.T("L'icône doit être un emoji ou un symbole court."));
        var trad = traductions == null ? null : NettoyerTraductions(traductions);
        if (trad?.Values.Any(v => v.Length > LibelleListeMax) == true) return OperationResult.Fail(L.F("Le libellé ne doit pas dépasser {0} caractères.", LibelleListeMax));
        if (!actuel.DOrigine && def.AvecInverse && inverse != null)
        {
            inverse = inverse.Trim();
            if (VerifierInverse(id, inverse, cle) is { } erreurInverse) return OperationResult.Fail(erreurInverse);
        }

        var config = ConfigListe(id);
        var e = config.Elements.First(x => x.Cle == cle);
        // Un libellé ou une icône identique à l'original reste « d'origine » (et donc traduit).
        e.Libelle = actuel.DOrigine && libelle == origine ? "" : libelle;
        e.Icone = actuel.DOrigine && icone == origineIcone ? "" : icone;
        if (trad != null) e.Traductions = trad;
        if (!actuel.DOrigine && def.AvecInverse && inverse != null && inverse != e.Inverse)
        {
            DelierInverse(config, cle);
            e.Inverse = inverse == cle ? "" : inverse;
            LierInverse(id, config, cle, e.Inverse);
        }
        ListeModifiee(def, "modification de « " + libelle + " »");
        return OperationResult.Ok();
    }

    /// <summary>Lien inverse réciproque entre deux liens de famille ajoutés (A ↔ B).</summary>
    private static void LierInverse(string id, ListeConfig config, string cle, string inverse)
    {
        if (inverse.Length == 0 || inverse == cle) return;
        var autre = config.Elements.FirstOrDefault(x => x.Cle == inverse);
        if (autre == null || ListesChoix.Definition(id)!.Origine().Any(o => o.Cle == inverse)) return;
        // L'ancien partenaire de l'autre lien redevient « le même lien ».
        foreach (var x in config.Elements.Where(x => x.Cle == autre.Inverse && x.Inverse == autre.Cle)) x.Inverse = "";
        autre.Inverse = cle;
    }

    /// <summary>Retire les liens inverses qui pointent vers <paramref name="cle"/>.</summary>
    private static void DelierInverse(ListeConfig config, string cle)
    {
        foreach (var x in config.Elements.Where(x => x.Inverse == cle)) x.Inverse = "";
    }

    /// <summary>Masque un choix des formulaires (les fiches qui l'utilisent le gardent).</summary>
    public OperationResult MasquerElementListe(string id, string cle, bool masque)
    {
        if (RefusListe(id, out var def) is { } refus) return refus;
        if (!def.AjoutPermis) return OperationResult.Fail(L.T("Les choix de cette liste ne peuvent pas être masqués."));
        var actuel = Liste(id).FirstOrDefault(e => e.Cle == cle);
        if (actuel == null) return OperationResult.Fail(L.T("Choix introuvable."));
        if (masque && Liste(id).Count(e => !e.Masque && e.Cle != cle) == 0)
            return OperationResult.Fail(L.T("Gardez au moins un choix visible dans la liste."));
        var config = ConfigListe(id);
        config.Elements.First(x => x.Cle == cle).Masque = masque;
        var nom = ListeFr(id).First(x => x.Cle == cle).Libelle;
        ListeModifiee(def, masque ? "« " + nom + " » masqué" : "« " + nom + " » de nouveau proposé");
        return OperationResult.Ok();
    }

    /// <summary>Monte (sens négatif) ou descend (sens positif) un choix dans la liste.</summary>
    public OperationResult DeplacerElementListe(string id, string cle, int sens)
    {
        if (RefusListe(id, out var def) is { } refus) return refus;
        var config = ConfigListe(id);
        var i = config.Elements.FindIndex(x => x.Cle == cle);
        if (i < 0) return OperationResult.Fail(L.T("Choix introuvable."));
        var j = Math.Clamp(i + Math.Sign(sens), 0, config.Elements.Count - 1);
        if (i == j) return OperationResult.Ok();
        (config.Elements[i], config.Elements[j]) = (config.Elements[j], config.Elements[i]);
        ListeModifiee(def, "nouvel ordre");
        return OperationResult.Ok();
    }

    /// <summary>
    /// Supprime un choix qui n'est plus utilisé. Un choix d'origine supprimé peut être remis
    /// (« Remettre » ou « Rétablir les choix d'origine »).
    /// </summary>
    public OperationResult SupprimerElementListe(string id, string cle)
    {
        if (RefusListe(id, out var def) is { } refus) return refus;
        if (!def.AjoutPermis) return OperationResult.Fail(L.T("On ne peut pas retirer de choix de cette liste."));
        var actuel = Liste(id).FirstOrDefault(e => e.Cle == cle);
        if (actuel == null) return OperationResult.Fail(L.T("Choix introuvable."));
        var n = UtilisationsElement(id, cle);
        if (n > 0) return OperationResult.Fail(L.F("« {0} » est utilisé par {1} fiche(s) : masquez-le plutôt.", actuel.Libelle, n));
        if (Liste(id).Count(e => !e.Masque && e.Cle != cle) == 0)
            return OperationResult.Fail(L.T("Gardez au moins un choix visible dans la liste."));
        var config = ConfigListe(id);
        var nom = ListeFr(id).First(x => x.Cle == cle).Libelle;
        if (actuel.DOrigine) config.Elements.First(x => x.Cle == cle).Supprime = true;
        else config.Elements.RemoveAll(x => x.Cle == cle);
        DelierInverse(config, cle);
        ListeModifiee(def, "suppression de « " + nom + " »");
        return OperationResult.Ok();
    }

    /// <summary>Remet dans la liste un choix d'origine supprimé.</summary>
    public OperationResult RemettreElementListe(string id, string cle)
    {
        if (RefusListe(id, out var def) is { } refus) return refus;
        var config = ConfigListe(id);
        var e = config.Elements.FirstOrDefault(x => x.Cle == cle && x.Supprime);
        if (e == null) return OperationResult.Fail(L.T("Choix introuvable."));
        e.Supprime = false;
        e.Masque = false;
        ListeModifiee(def, "« " + LibelleOrigineFr(id, cle) + " » remis");
        return OperationResult.Ok();
    }

    /// <summary>
    /// Rétablit les choix d'origine (libellés, icônes, ordre, tous visibles).
    /// Les choix ajoutés restent, à la fin de la liste.
    /// </summary>
    public OperationResult ReinitialiserListe(string id)
    {
        if (RefusListe(id, out var def) is { } refus) return refus;
        var ajoutes = ConfigListe(id).Elements.Where(e => def.Origine().All(o => o.Cle != e.Cle)).ToList();
        _settings.Listes[id] = new ListeConfig
        {
            Elements = def.Origine().Select(o => new ElementListe { Cle = o.Cle }).Concat(ajoutes).ToList()
        };
        ListeModifiee(def, "choix d'origine rétablis");
        return OperationResult.Ok();
    }
}
