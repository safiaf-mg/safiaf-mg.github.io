using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Traduction de l'interface. Le texte français sert de clé : <c>L.T("Utilisateurs")</c>
/// renvoie « Users » en anglais et « Mpikambana » en malgache. Un texte absent des
/// fichiers I18n/&lt;langue&gt;/*.json reste en français, rien ne casse.
/// La langue est un choix personnel de chaque membre (comme le thème).
/// </summary>
public static class L
{
    public const string Fr = "fr";
    public const string En = "en";
    public const string Mg = "mg";

    /// <summary>Clé du navigateur qui retient la dernière langue choisie (avant connexion).</summary>
    public const string CleNavigateur = "gu2:langue";

    public static readonly IReadOnlyList<(string Code, string Nom)> Langues = new[]
    {
        (Fr, "Français"),
        (En, "English"),
        (Mg, "Malagasy")
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Dicos = Charger();
    private static readonly Dictionary<string, Dictionary<string, string>> AvecNom = ChargerAvecNom();

    /// <summary>
    /// Nom de l'application (Paramètres › Apparence), ou null tant qu'il n'est pas choisi.
    /// Quand il est connu, les textes qui parlent de « la communauté » le citent à la place
    /// (fichiers I18n/nom/&lt;langue&gt;.json, avec {app} et {de_app} pour « de X » / « d'X »).
    /// </summary>
    public static Func<string?>? NomApplication { get; set; }

    public static string Langue { get; private set; } = Fr;

    /// <summary>Langue changée : l'application se recharge pour tout réafficher.</summary>
    public static event Action? Changed;

    public static string Normaliser(string? code) => code switch
    {
        En => En,
        Mg => Mg,
        _ => Fr
    };

    public static void Definir(string? code)
    {
        var c = Normaliser(code);
        if (c == Langue) return;
        Langue = c;
        Changed?.Invoke();
    }

    /// <summary>Traduit un texte français (le texte lui-même si aucune traduction n'existe).</summary>
    public static string T(string? fr)
    {
        if (string.IsNullOrEmpty(fr) || _enFrancais) return fr ?? "";
        if (AvecNom.TryGetValue(Langue, out var variantes) && variantes.TryGetValue(fr, out var modele))
        {
            var nom = NomApplication?.Invoke()?.Trim();
            if (!string.IsNullOrEmpty(nom)) return AvecLeNom(modele, nom);
        }
        if (Langue == Fr) return fr;
        return Dicos.TryGetValue(Langue, out var d) && d.TryGetValue(fr, out var t) && !string.IsNullOrEmpty(t) ? t : fr;
    }

    [ThreadStatic] private static bool _enFrancais;

    /// <summary>Langue des textes calculés en ce moment (français pendant <see cref="EnFrancais"/>).</summary>
    public static string LangueAffichee => _enFrancais ? Fr : Langue;

    /// <summary>Calcule un texte en français quelle que soit la langue (historique enregistré).</summary>
    public static string EnFrancais(Func<string> texte)
    {
        var avant = _enFrancais;
        _enFrancais = true;
        try { return texte(); }
        finally { _enFrancais = avant; }
    }

    /// <summary>Traduit un modèle avec des valeurs : <c>L.F("{0} membre(s)", n)</c>.</summary>
    public static string F(string fr, params object?[] valeurs)
    {
        try { return string.Format(CultureInfo.CurrentCulture, T(fr), valeurs); }
        catch (FormatException) { return string.Format(CultureInfo.CurrentCulture, fr, valeurs); }
    }

    /// <summary>Vrai si une traduction existe (utile aux tests et au suivi).</summary>
    public static bool EstTraduit(string langue, string fr)
        => Dicos.TryGetValue(langue, out var d) && d.ContainsKey(fr);

    public static IReadOnlyDictionary<string, string> Dictionnaire(string langue)
        => Dicos.TryGetValue(langue, out var d) ? d : new Dictionary<string, string>();

    private static readonly string[] MoisFr =
        { "janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre" };
    private static readonly string[] JoursFr =
        { "dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi" };

    /// <summary>Nom du mois (1 à 12) en minuscules dans la langue choisie.</summary>
    public static string Mois(int mois) => T(MoisFr[Math.Clamp(mois, 1, 12) - 1]);

    /// <summary>Nom du jour en minuscules dans la langue choisie.</summary>
    public static string Jour(DayOfWeek jour) => T(JoursFr[(int)jour]);

    /// <summary>« mars 2026 » / « March 2026 » / « Martsa 2026 ».</summary>
    public static string MoisAnnee(DateTime d) => Mois(d.Month) + " " + d.Year.ToString(CultureInfo.InvariantCulture);

    /// <summary>« lundi 5 mars 2026 » dans la langue choisie.</summary>
    public static string DateLongue(DateTime d) => Langue == En
        ? Majuscule(Jour(d.DayOfWeek)) + " " + d.Day + " " + Majuscule(Mois(d.Month)) + " " + d.Year
        : Jour(d.DayOfWeek) + " " + d.Day + " " + Mois(d.Month) + " " + d.Year;

    /// <summary>Remplace {app} par le nom et {de_app} par « de Nom » ou « d'Nom » (élision devant voyelle ou h).</summary>
    public static string AvecLeNom(string modele, string nom)
        => modele.Replace("{de_app}", (Elision(nom) ? "d'" : "de ") + nom).Replace("{app}", nom);

    /// <summary>Initiales d'un nom (« Avotra Ambohimanarina » → « AA », « Fanantenana » → « FA ») ; « GU » sans nom.</summary>
    public static string Initiales(string? nom)
    {
        var mots = (nom ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(m => char.IsLetter(m[0])).ToList();
        if (mots.Count == 0) return "GU";
        var ini = mots.Count == 1 ? mots[0][..Math.Min(2, mots[0].Length)] : $"{mots[0][0]}{mots[^1][0]}";
        return ini.ToUpperInvariant();
    }

    /// <summary>« d' » plutôt que « de » devant une voyelle ou un h.</summary>
    public static bool Elision(string nom)
        => nom.Length > 0 && "aeiouyhàâäéèêëîïôöùûüAEIOUYHÀÂÄÉÈÊËÎÏÔÖÙÛÜ".Contains(nom[0]);

    /// <summary>Textes « avec le nom de l'application » d'une langue (tests et suivi).</summary>
    public static IReadOnlyDictionary<string, string> VariantesAvecNom(string langue)
        => AvecNom.TryGetValue(langue, out var d) ? d : new Dictionary<string, string>();

    public static string Majuscule(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

    private static Dictionary<string, Dictionary<string, string>> Charger()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var asm = typeof(L).Assembly;
        foreach (var nom in asm.GetManifestResourceNames().OrderBy(n => n, StringComparer.Ordinal))
        {
            var chemin = nom.Replace('\\', '/');
            if (!chemin.StartsWith("i18n/", StringComparison.Ordinal) || !chemin.EndsWith(".json", StringComparison.Ordinal)) continue;
            var parts = chemin.Split('/');
            if (parts.Length < 3 || parts[1] == "nom") continue;
            var langue = parts[1];
            try
            {
                using var flux = asm.GetManifestResourceStream(nom);
                if (flux == null) continue;
                var dico = JsonSerializer.Deserialize<Dictionary<string, string>>(flux);
                if (dico == null) continue;
                if (!result.TryGetValue(langue, out var cible))
                    result[langue] = cible = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (k, v) in dico)
                    if (!string.IsNullOrEmpty(v)) cible[k] = v;
            }
            catch (JsonException)
            {
                // Fichier de traduction mal formé : ignoré, les textes restent en français.
            }
        }
        return result;
    }

    /// <summary>I18n/nom/&lt;langue&gt;.json : texte français → même texte citant le nom de l'application.</summary>
    private static Dictionary<string, Dictionary<string, string>> ChargerAvecNom()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var asm = typeof(L).Assembly;
        foreach (var nom in asm.GetManifestResourceNames())
        {
            var chemin = nom.Replace('\\', '/');
            if (!chemin.StartsWith("i18n/nom/", StringComparison.Ordinal) || !chemin.EndsWith(".json", StringComparison.Ordinal)) continue;
            var langue = Path.GetFileNameWithoutExtension(chemin);
            try
            {
                using var flux = asm.GetManifestResourceStream(nom);
                var dico = flux == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(flux);
                if (dico != null) result[langue] = new Dictionary<string, string>(dico.Where(kv => !string.IsNullOrEmpty(kv.Value)), StringComparer.Ordinal);
            }
            catch (JsonException) { }
        }
        return result;
    }
}
