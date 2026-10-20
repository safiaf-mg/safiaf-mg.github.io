using System.Globalization;
using System.Text;
using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Doublons de fiches membres : détection (même nom, e-mail, téléphone ou
/// matricule), signalement avant création et fusion de deux fiches.
/// </summary>
public partial class DataStore
{
    /// <summary>Groupe de fiches qui semblent décrire la même personne.</summary>
    public sealed record DoublonGroupe(string Raison, IReadOnlyList<UserAccount> Membres);

    /// <summary>Fiche reliée à un compte en ligne (identifiant Supabase au format UUID avec tirets).</summary>
    public static bool EstCompteEnLigne(UserAccount? u)
        => u != null && Guid.TryParseExact(u.Id ?? "", "D", out _);

    /// <summary>Nom comparable : sans accents ni casse, mots triés (« Jean RAKOTO » = « RAKOTO Jean »).</summary>
    public static string CleNom(string? prenom, string? nom)
    {
        var mots = (Replier(prenom) + " " + Replier(nom))
            .Split(new[] { ' ', '-', '\'', '’', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(m => m, StringComparer.Ordinal);
        return string.Join(" ", mots);
    }

    private static string Replier(string? texte)
    {
        var d = (texte ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Téléphone comparable : les 9 derniers chiffres (+261 34… = 034…).</summary>
    private static string CleTelephone(string? tel)
    {
        var chiffres = new string((tel ?? "").Where(char.IsDigit).ToArray());
        return chiffres.Length < 7 ? "" : chiffres.Length > 9 ? chiffres[^9..] : chiffres;
    }

    private static IEnumerable<(string Raison, string Cle)> ClesDoublon(UserAccount u)
    {
        var nom = CleNom(u.Prenom, u.Nom);
        if (nom.Length > 0) yield return ("même nom", "n:" + nom);
        if (!string.IsNullOrWhiteSpace(u.Email)) yield return ("même e-mail", "e:" + u.Email.Trim().ToLowerInvariant());
        var tel = CleTelephone(u.Telephone);
        if (tel.Length > 0) yield return ("même téléphone", "t:" + tel);
        if (!string.IsNullOrWhiteSpace(u.Matricule)) yield return ("même matricule", "m:" + u.Matricule.Trim().ToLowerInvariant());
    }

    /// <summary>Clés qui identifient sûrement une personne (e-mail, téléphone, matricule), sans le nom.</summary>
    private static IEnumerable<string> ClesContact(UserAccount u)
        => ClesDoublon(u).Where(k => !k.Cle.StartsWith("n:", StringComparison.Ordinal)).Select(k => k.Cle);

    /// <summary>Fiches existantes qui ressemblent à <paramref name="draft"/> (avant création ou modification).</summary>
    public IReadOnlyList<(UserAccount Fiche, string Raison)> DoublonsPossibles(UserAccount draft, string? existingId = null)
    {
        var cles = ClesDoublon(draft).ToList();
        var result = new List<(UserAccount, string)>();
        if (cles.Count == 0) return result;
        foreach (var u in _users)
        {
            if (u.Id == existingId) continue;
            var siennes = ClesDoublon(u).Select(k => k.Cle).ToHashSet();
            var raisons = cles.Where(k => siennes.Contains(k.Cle)).Select(k => L.T(k.Raison)).ToList();
            if (raisons.Count > 0) result.Add((u, string.Join(", ", raisons)));
        }
        return result;
    }

    /// <summary>
    /// Groupes de fiches qui partagent un nom, un e-mail, un téléphone ou un
    /// matricule. Les paires marquées « pas un doublon » sont écartées.
    /// </summary>
    public IReadOnlyList<DoublonGroupe> GroupesDeDoublons()
    {
        var parCle = new Dictionary<string, List<UserAccount>>();
        var raisonDe = new Dictionary<string, string>();
        foreach (var u in _users)
            foreach (var (raison, cle) in ClesDoublon(u))
            {
                if (!parCle.TryGetValue(cle, out var l)) parCle[cle] = l = new List<UserAccount>();
                l.Add(u);
                raisonDe[cle] = raison;
            }

        // Union des fiches liées par au moins une clé commune (hors paires écartées).
        var parent = _users.ToDictionary(u => u.Id, u => u.Id);
        string Racine(string id) { while (parent[id] != id) id = parent[id] = parent[parent[id]]; return id; }
        var raisons = new Dictionary<string, HashSet<string>>();
        foreach (var (cle, fiches) in parCle)
        {
            if (fiches.Count < 2) continue;
            for (var i = 0; i < fiches.Count; i++)
                for (var j = i + 1; j < fiches.Count; j++)
                {
                    var a = fiches[i]; var b = fiches[j];
                    if (SontEcartes(a, b)) continue;
                    var ra = Racine(a.Id); var rb = Racine(b.Id);
                    if (ra != rb) parent[ra] = rb;
                    var r = Racine(b.Id);
                    if (!raisons.TryGetValue(r, out var set)) raisons[r] = set = new HashSet<string>();
                    set.Add(raisonDe[cle]);
                }
        }
        var groupes = _users.GroupBy(u => Racine(u.Id)).Where(g => g.Count() > 1).ToList();
        return groupes
            .Select(g =>
            {
                var r = Racine(g.Key);
                var raison = raisons.Where(kv => Racine(kv.Key) == r).SelectMany(kv => kv.Value).Distinct();
                return new DoublonGroupe(string.Join(", ", raison.Select(L.T)),
                    g.OrderByDescending(EstCompteEnLigne).ThenBy(u => u.DateCreation).ToList());
            })
            .OrderBy(g => g.Membres[0].NomComplet)
            .ToList();
    }

    private bool SontEcartes(UserAccount a, UserAccount b)
        => (_settings.PasDoublons ?? new List<string>()).Contains(ClePaire(a.Id, b.Id));

    private static string ClePaire(string a, string b)
        => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;

    /// <summary>Indique que deux fiches désignent bien deux personnes différentes (homonymes).</summary>
    public OperationResult MarquerPasDoublon(IEnumerable<string> userIds)
    {
        if (!Can(Permissions.UsersManage)) return OperationResult.Fail(L.T("Action réservée à la gestion des utilisateurs."));
        var ids = userIds.Distinct().ToList();
        _settings.PasDoublons ??= new List<string>();
        for (var i = 0; i < ids.Count; i++)
            for (var j = i + 1; j < ids.Count; j++)
            {
                var k = ClePaire(ids[i], ids[j]);
                if (!_settings.PasDoublons.Contains(k)) _settings.PasDoublons.Add(k);
            }
        SaveSettings();
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>
    /// Fusionne la fiche <paramref name="retirerId"/> dans <paramref name="garderId"/> :
    /// les champs vides de la fiche gardée sont complétés, toutes les données qui
    /// citaient la fiche retirée (cotisations, prêts, tâches, messages, réunions,
    /// votes, documents, événements, situations) passent sur la fiche gardée, puis
    /// la fiche retirée est supprimée. Le compte en ligne éventuel est conservé.
    /// </summary>
    public OperationResult FusionnerUtilisateurs(string garderId, string retirerId)
    {
        if (!Can(Permissions.UsersManage)) return OperationResult.Fail(L.T("Action réservée à la gestion des utilisateurs."));
        if (garderId == retirerId) return OperationResult.Fail(L.T("Choisissez deux fiches différentes."));
        var garder = _users.FirstOrDefault(u => u.Id == garderId);
        var retirer = _users.FirstOrDefault(u => u.Id == retirerId);
        if (garder == null || retirer == null) return OperationResult.Fail(L.T("Fiche introuvable."));
        if (EstCompteEnLigne(garder) && EstCompteEnLigne(retirer))
            return OperationResult.Fail(L.T("Les deux fiches ont chacune un compte en ligne : désactivez plutôt le compte en trop depuis sa fiche."));
        if (retirer.Role == Roles.BaseAdmin || garder.Role == Roles.BaseAdmin)
            return OperationResult.Fail(L.T("Le compte de l'administrateur de la base ne peut pas être fusionné."));

        var ancien = retirer.Identifiant;
        var nouveau = garder.Identifiant;

        // 1) Compléter la fiche gardée.
        if (string.IsNullOrWhiteSpace(garder.Email)) garder.Email = retirer.Email;
        if (string.IsNullOrWhiteSpace(garder.Telephone)) garder.Telephone = retirer.Telephone;
        if (string.IsNullOrWhiteSpace(garder.DateNaissance)) garder.DateNaissance = retirer.DateNaissance;
        if (string.IsNullOrWhiteSpace(garder.Matricule)) garder.Matricule = retirer.Matricule;
        if (string.IsNullOrWhiteSpace(garder.Photo)) garder.Photo = retirer.Photo;
        if (string.IsNullOrWhiteSpace(garder.Signature)) garder.Signature = retirer.Signature;
        if (!string.IsNullOrWhiteSpace(retirer.Notes) && !(garder.Notes ?? "").Contains(retirer.Notes.Trim()))
            garder.Notes = string.IsNullOrWhiteSpace(garder.Notes) ? retirer.Notes : garder.Notes + "\n" + retirer.Notes;
        foreach (var (k, v) in retirer.Champs ?? new Dictionary<string, FieldValue>())
            if (!garder.Champs.ContainsKey(k)) garder.Champs[k] = v;
        if (string.CompareOrdinal(retirer.DateCreation ?? "", garder.DateCreation ?? "") < 0 && !string.IsNullOrEmpty(retirer.DateCreation))
            garder.DateCreation = retirer.DateCreation;
        if (string.CompareOrdinal(retirer.DerniereConnexion ?? "", garder.DerniereConnexion ?? "") > 0)
            garder.DerniereConnexion = retirer.DerniereConnexion;
        // Le compte en ligne suit la personne : la fiche gardée prend son identifiant,
        // son rôle et son état, pour que la connexion continue de fonctionner.
        if (EstCompteEnLigne(retirer))
        {
            garder.Id = retirer.Id;
            garder.Role = retirer.Role;
            garder.Actif = retirer.Actif;
            if (_session != null && _session.UserId == retirer.Id) { _session.UserId = garder.Id; SaveSession(); }
        }

        // 2) Les anniversaires automatiques de la fiche retirée seront recréés pour la fiche gardée.
        var anniv = GetBirthdayEventsForUser(ancien, retirer.NomComplet).Where(e => e.CreatedBy == "auto").ToList();
        var evtChange = anniv.Count > 0;
        foreach (var e in anniv) _events.Remove(e);

        // 3) Reporter les références vers la fiche gardée.
        bool Meme(string? s) => string.Equals((s ?? "").Trim(), ancien, StringComparison.OrdinalIgnoreCase);
        bool Remplacer(List<string>? liste)
        {
            if (liste == null || !liste.Any(Meme)) return false;
            var copie = liste.Select(x => Meme(x) ? nouveau : x).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            liste.Clear(); liste.AddRange(copie);
            return true;
        }

        var cotChange = false;
        foreach (var c in _cotisations.Where(c => Meme(c.MembreIdentifiant))) { c.MembreIdentifiant = nouveau; cotChange = true; }
        var pretChange = false;
        foreach (var p in _prets.Where(p => Meme(p.MembreIdentifiant))) { p.MembreIdentifiant = nouveau; pretChange = true; }
        var sitChange = false;
        foreach (var s in _situations.Where(s => Meme(s.MembreIdentifiant))) { s.MembreIdentifiant = nouveau; sitChange = true; }
        foreach (var s in _situations)
        {
            sitChange |= Remplacer(s.Visiteurs);
            if (Meme(s.EnfantIdentifiant)) { s.EnfantIdentifiant = nouveau; sitChange = true; }
        }
        var tacheChange = false;
        foreach (var t in _taches) tacheChange |= Remplacer(t.Assignes);
        var msgChange = false;
        foreach (var m in _messages)
        {
            var touche = false;
            if (Meme(m.Expediteur)) { m.Expediteur = nouveau; touche = true; }
            touche |= Remplacer(m.Destinataires);
            touche |= Remplacer(m.LuPar);
            touche |= Remplacer(m.MasquePour);
            if (touche && EstConversationParParticipants(m.ConversationId))
                m.ConversationId = ConversationIdFor(m.Destinataires.Append(m.Expediteur));
            msgChange |= touche;
        }
        var reuChange = false;
        foreach (var r in _reunions)
        {
            reuChange |= Remplacer(r.Participants);
            var cle = r.Presences.Keys.FirstOrDefault(Meme);
            if (cle != null)
            {
                var v = r.Presences[cle];
                r.Presences.Remove(cle);
                r.Presences.TryAdd(nouveau, v);
                reuChange = true;
            }
        }
        var voteChange = false;
        foreach (var v in _votes)
        {
            if (!v.Bulletins.Any(b => Meme(b.VotantIdentifiant))) continue;
            // Un seul bulletin par personne : on garde celui de la fiche gardée s'il existe.
            var dejaVote = v.Bulletins.Any(b => string.Equals(b.VotantIdentifiant, nouveau, StringComparison.OrdinalIgnoreCase));
            if (dejaVote) v.Bulletins.RemoveAll(b => Meme(b.VotantIdentifiant));
            else foreach (var b in v.Bulletins.Where(b => Meme(b.VotantIdentifiant))) b.VotantIdentifiant = nouveau;
            voteChange = true;
        }
        var docChange = false;
        foreach (var d in _documents.Where(d => Meme(d.UtilisateurId))) { d.UtilisateurId = nouveau; docChange = true; }
        foreach (var e in _events)
        {
            evtChange |= Remplacer(e.PersonnesConcernees);
            if (Meme(e.PersonneConcernee)) { e.PersonneConcernee = nouveau; evtChange = true; }
        }

        // Vie spirituelle : groupes, services et intentions de prière.
        var groupeChange = false;
        foreach (var g in _groupes)
        {
            groupeChange |= Remplacer(g.Membres);
            groupeChange |= Remplacer(g.Responsables);
        }
        var serviceChange = false;
        foreach (var a in _services.SelectMany(s => s.Affectations).Where(a => Meme(a.Identifiant)))
        {
            a.Identifiant = nouveau;
            serviceChange = true;
        }
        var priereChange = false;
        foreach (var p in _prieres)
        {
            if (Meme(p.Auteur)) { p.Auteur = nouveau; priereChange = true; }
            priereChange |= Remplacer(p.Prieurs);
        }

        // Famille : les liens de la fiche retirée passent sur la fiche gardée.
        garder.Famille ??= new List<LienFamille>();
        foreach (var l in retirer.Famille ?? new List<LienFamille>())
            if (!garder.Famille.Any(x => x.Identifiant.Equals(l.Identifiant, StringComparison.OrdinalIgnoreCase)))
                garder.Famille.Add(new LienFamille { Identifiant = l.Identifiant, Type = l.Type });
        foreach (var u in _users)
        {
            if (u.Famille == null || u.Famille.Count == 0) continue;
            foreach (var l in u.Famille.Where(l => Meme(l.Identifiant))) l.Identifiant = nouveau;
            var propres = u.Famille
                .Where(l => !l.Identifiant.Equals(u.Identifiant, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(l => l.Identifiant.ToLowerInvariant()).ToList();
            u.Famille.Clear(); u.Famille.AddRange(propres);
        }

        // 4) Retirer la fiche en double et enregistrer.
        _users.Remove(retirer);
        SaveUsers();
        if (SyncUserDateEvents(garder)) evtChange = true;
        if (cotChange) SaveCotisations();
        if (pretChange) SavePrets();
        if (sitChange) SaveSituations();
        if (tacheChange) SaveTaches();
        if (msgChange) SaveMessages();
        if (reuChange) SaveReunions();
        if (voteChange) SaveVotes();
        if (groupeChange) SaveGroupes();
        if (serviceChange) SaveServices();
        if (priereChange) SavePrieres();
        if (docChange) SaveDocuments();
        if (evtChange) SaveEvents();
        LogActivity(ActivityTypes.Update, "Fusion de la fiche " + ancien + " dans " + nouveau);
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }
}
