using GestionUtilisateurs.Shared.Services;

namespace GestionUtilisateurs.Shared.Models;

/// <summary>Dossier de la gestion documentaire (hiérarchie simple via ParentId).</summary>
public class DocumentFolder
{
    public string Id { get; set; } = "";
    /// <summary>Dossier parent ; null ou vide = racine.</summary>
    public string? ParentId { get; set; }
    public string Nom { get; set; } = "";
    public string Couleur { get; set; } = DocumentCategories.CouleurDefaut;
    public string DateCreation { get; set; } = "";
    public string CreatedBy { get; set; } = "";
}

/// <summary>Document stocké localement (fichier en data URL, 100 % hors ligne).</summary>
public class DocumentItem
{
    public string Id { get; set; } = "";
    public string Titre { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Dossier parent ; null ou vide = racine.</summary>
    public string? FolderId { get; set; }
    public string Categorie { get; set; } = DocumentCategories.Autre;
    public List<string> Tags { get; set; } = new();
    public bool Favori { get; set; }
    public string FileName { get; set; } = "";
    public string Mime { get; set; } = "application/octet-stream";
    /// <summary>Taille du fichier d'origine en octets.</summary>
    public long Taille { get; set; }
    /// <summary>Contenu du fichier en data URL (base64), stocké en local.</summary>
    public string Data { get; set; } = "";
    /// <summary>Identifiant de l'utilisateur associé (optionnel).</summary>
    public string? UtilisateurId { get; set; }
    public string DateCreation { get; set; } = "";
    public string DateModification { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    /// <summary>Identifiant du dernier utilisateur ayant modifié le document (métadonnées, déplacement…).</summary>
    public string ModifiePar { get; set; } = "";
    /// <summary>Document placé dans la corbeille (restaurable).</summary>
    public bool Supprime { get; set; }
    public string? DateSuppression { get; set; }
}

/// <summary>Catégories de documents (libellé, couleur, icône).</summary>
public static class DocumentCategories
{
    public const string Personnel = "personnel";
    public const string Travail = "travail";
    public const string Factures = "factures";
    public const string Contrats = "contrats";
    public const string Photos = "photos";
    public const string Scans = "scans";
    public const string Autre = "autre";

    public const string CouleurDefaut = "#4f46e5";

    public static readonly string[] Toutes = { Personnel, Travail, Factures, Contrats, Photos, Scans, Autre };

    public static string LabelOrigine(string categorie) => categorie switch
    {
        Personnel => L.T("Personnel"),
        Travail => L.T("Travail"),
        Factures => L.T("Factures"),
        Contrats => L.T("Contrats"),
        Photos => L.T("Photos"),
        Scans => L.T("Scans"),
        _ => L.T("Autre")
    };

    public static string IconOrigine(string categorie) => categorie switch
    {
        Personnel => "👤",
        Travail => "💼",
        Factures => "🧾",
        Contrats => "📝",
        Photos => "🖼️",
        Scans => "📷",
        _ => "📄"
    };

    public static string Color(string categorie) => categorie switch
    {
        Personnel => "#0891b2",
        Travail => "#4f46e5",
        Factures => "#d97706",
        Contrats => "#7c3aed",
        Photos => "#059669",
        Scans => "#dc2626",
        _ => "#64748b"
    };

    /// <summary>Devine une catégorie depuis un type MIME.</summary>
    public static string FromMime(string? mime) => (mime ?? "").ToLowerInvariant() switch
    {
        var m when m.StartsWith("image/") => Photos,
        "application/pdf" => Travail,
        _ => Autre
    };
    public static string Label(string categorie) => ListesChoix.LibellePerso(ListesChoix.CategoriesDocument, categorie) ?? LabelOrigine(categorie);

    public static string Icon(string categorie) => ListesChoix.IconePerso(ListesChoix.CategoriesDocument, categorie) ?? (ListesChoix.EstAjoute(ListesChoix.CategoriesDocument, categorie) ? "📁" : IconOrigine(categorie));
}

/// <summary>Types de fichiers reconnus pour l'affichage et les filtres.</summary>
public static class DocumentKinds
{
    public static bool IsImage(string? mime) => (mime ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    public static bool IsPdf(string? mime) => string.Equals(mime, "application/pdf", StringComparison.OrdinalIgnoreCase);

    public static string KindOf(string? mime) => IsPdf(mime) ? "pdf" : IsImage(mime) ? "image" : "autre";

    public static string KindLabel(string kind) => kind switch
    {
        "pdf" => L.T("PDF"),
        "image" => L.T("Images"),
        _ => L.T("Autres")
    };
}
