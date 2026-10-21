using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Âges et anniversaires : un seul calcul pour la fiche du membre et le calendrier,
/// pour qu'ils affichent toujours le même âge pour le même jour.
/// </summary>
public partial class DataStore
{
    /// <summary>Âge atteint le jour donné (aujourd'hui par défaut) ; null si la date est invalide ou future.</summary>
    public static int? Age(string? naissance, DateTime? jour = null)
    {
        if (!TryParseYmd(naissance ?? "", out var n)) return null;
        var j = (jour ?? DateTime.Today).Date;
        var age = j.Year - n.Year;
        if (j < AnniversaireEn(n, j.Year)) age--;
        return age < 0 ? null : age;
    }

    /// <summary>Jour de l'anniversaire une année donnée (né un 29 février : le 28 les années non bissextiles).</summary>
    public static DateTime AnniversaireEn(DateTime naissance, int annee)
        => naissance.Month == 2 && naissance.Day == 29 && !DateTime.IsLeapYear(annee)
            ? new DateTime(annee, 2, 28)
            : new DateTime(annee, naissance.Month, naissance.Day);

    /// <summary>Prochain anniversaire à partir du jour donné (ce jour compris).</summary>
    public static DateTime ProchainAnniversaire(DateTime naissance, DateTime depuis)
    {
        var d = AnniversaireEn(naissance, depuis.Year);
        return d >= depuis.Date ? d : AnniversaireEn(naissance, depuis.Year + 1);
    }

    /// <summary>
    /// Date à laquelle la liste du calendrier range l'événement : la prochaine occurrence
    /// (aujourd'hui compris) pour un événement répété, sa date sinon. Un anniversaire
    /// apparaît donc à sa prochaine date, avec l'âge fêté ce jour-là.
    /// </summary>
    public static DateTime? ProchaineOccurrence(CalendarEvent e, DateTime depuis)
    {
        if (!TryParseYmd(e.Date ?? "", out var d)) return null;
        var jour = depuis.Date;
        switch (e.Repetition ?? CalendarEventTypes.RepetitionAucune)
        {
            case CalendarEventTypes.RepetitionAnnuelle:
                return ProchainAnniversaire(d, d > jour ? d : jour);
            case CalendarEventTypes.RepetitionQuotidienne:
                return d > jour ? d : jour;
            case CalendarEventTypes.RepetitionHebdomadaire:
                return d >= jour ? d : jour.AddDays((7 - (jour - d).Days % 7) % 7);
            case CalendarEventTypes.RepetitionMensuelle:
                if (d >= jour) return d;
                for (var m = new DateTime(jour.Year, jour.Month, 1); ; m = m.AddMonths(1))
                    if (d.Day <= DateTime.DaysInMonth(m.Year, m.Month) && new DateTime(m.Year, m.Month, d.Day) >= jour)
                        return new DateTime(m.Year, m.Month, d.Day);
            default:
                return d;
        }
    }
}
