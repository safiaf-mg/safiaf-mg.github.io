using System.Globalization;
using System.Text;
using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Ajouts en double : chaque menu refuse un élément identique à un élément existant
/// (même titre, même date…), et le calendrier propose de retirer ses doublons.
/// </summary>
public partial class DataStore
{
    /// <summary>Texte comparé sans majuscules, accents ni espaces en trop : « Réunion  du bureau » = « reunion du bureau ».</summary>
    public static string CleDoublon(string? texte)
    {
        var s = (texte ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(s.Length);
        var espace = false;
        foreach (var c in s)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c)) { espace = true; continue; }
            if (espace && sb.Length > 0) sb.Append(' ');
            espace = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string DateAffichee(string? ymd)
        => TryParseYmd(ymd ?? "", out var d) ? d.ToString("dd/MM/yyyy") : ymd ?? "";

    // ---------- Calendrier ----------

    /// <summary>Clé d'un événement : titre, jour (jour et mois seulement s'il revient chaque année) et heure de début.</summary>
    private static string CleEvenement(CalendarEvent e)
    {
        var date = (e.Date ?? "").Trim();
        var jour = e.Repetition == CalendarEventTypes.RepetitionAnnuelle && date.Length >= 10 ? date[5..10] : date;
        var heure = e.TouteLaJournee ? "" : (e.HeureDebut ?? "").Trim();
        return CleDoublon(e.Titre) + "|" + jour + "|" + heure;
    }

    private OperationResult? EvenementEnDouble(CalendarEvent draft, string? sauf = null)
    {
        var cle = CleEvenement(draft);
        return _events.Any(e => e.Id != sauf && CleEvenement(e) == cle)
            ? OperationResult.Fail(L.F("L'événement « {0} » existe déjà le {1}.", draft.Titre, DateAffichee(draft.Date)))
            : null;
    }

    /// <summary>Événements en double : pour chaque groupe, le plus ancien est gardé, les autres sont listés.</summary>
    public IReadOnlyList<CalendarEvent> DoublonsCalendrier()
        => _events.GroupBy(CleEvenement)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.OrderBy(e => e.DateCreation ?? "", StringComparer.Ordinal).Skip(1))
            .ToList();

    /// <summary>Retire les doublons du calendrier (garde le premier enregistré de chaque groupe).</summary>
    public OperationResult SupprimerDoublonsCalendrier()
    {
        if (!Can(Permissions.CalendarUse)) return OperationResult.Fail(L.T("Action réservée à la gestion du calendrier."));
        var aRetirer = DoublonsCalendrier().Select(e => e.Id).ToHashSet();
        if (aRetirer.Count == 0) return OperationResult.Ok();
        _events.RemoveAll(e => aRetirer.Contains(e.Id));
        SaveEvents();
        LogActivity(ActivityTypes.CalendarDelete, "Suppression de " + aRetirer.Count + " événement(s) en double");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    // ---------- Autres menus ----------

    private OperationResult? TacheEnDouble(Tache draft, string? sauf = null)
        => _taches.Any(t => t.Id != sauf && t.Statut != TacheStatuts.Terminee
                            && CleDoublon(t.Titre) == CleDoublon(draft.Titre)
                            && (t.Echeance ?? "") == (draft.Echeance ?? ""))
            ? OperationResult.Fail(L.F("La tâche « {0} » existe déjà.", draft.Titre))
            : null;

    private OperationResult? ReunionEnDouble(Reunion draft, string? sauf = null)
        => _reunions.Any(r => r.Id != sauf && r.Date == draft.Date
                              && CleDoublon(r.Titre) == CleDoublon(draft.Titre)
                              && (r.HeureDebut ?? "") == (draft.HeureDebut ?? ""))
            ? OperationResult.Fail(L.F("La réunion « {0} » existe déjà le {1}.", draft.Titre, DateAffichee(draft.Date)))
            : null;

    private OperationResult? MaterielEnDouble(Materiel draft, string? sauf = null)
        => _materiels.Any(m => m.Id != sauf && CleDoublon(m.Nom) == CleDoublon(draft.Nom))
            ? OperationResult.Fail(L.F("Le matériel « {0} » existe déjà : modifiez plutôt sa quantité.", draft.Nom))
            : null;

    private OperationResult? VoteEnDouble(Vote draft, string? sauf = null)
        => _votes.Any(v => v.Id != sauf && v.Statut == VoteStatuts.Ouvert && CleDoublon(v.Titre) == CleDoublon(draft.Titre))
            ? OperationResult.Fail(L.F("Le vote « {0} » est déjà ouvert.", draft.Titre))
            : null;

    private OperationResult? ServiceEnDouble(string titre, string date, string heure, string? sauf = null)
        => _services.Any(s => s.Id != sauf && s.Date == date && (s.Heure ?? "") == heure && CleDoublon(s.Titre) == CleDoublon(titre))
            ? OperationResult.Fail(L.F("Le service « {0} » existe déjà le {1}.", titre, DateAffichee(date)))
            : null;
}
