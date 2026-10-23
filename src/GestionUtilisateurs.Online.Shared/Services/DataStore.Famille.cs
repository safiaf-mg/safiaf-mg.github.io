using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Liens de famille entre membres : chaque lien est enregistré des deux côtés
/// (X parent de Y ⇔ Y enfant de X, X beau-père de Y ⇔ Y gendre de X…).
/// Le reste de la famille (grands-parents, oncles, cousins, beaux-parents…)
/// est déduit des liens enregistrés.
/// </summary>
public partial class DataStore
{
    /// <summary>Une ligne de la section Famille : un type de lien et les membres concernés.</summary>
    public sealed record GroupeFamille(string Type, IReadOnlyList<UserAccount> Membres)
    {
        public string Titre => LiensFamille.Titre(Type);
    }

    /// <summary>La famille d'un membre, groupée dans l'ordre de <see cref="LiensFamille.Tous"/>.</summary>
    public sealed record FamilleVue(IReadOnlyList<GroupeFamille> Groupes)
    {
        public bool EstVide => Groupes.All(g => g.Membres.Count == 0);
        public IReadOnlyList<UserAccount> De(string type)
            => Groupes.FirstOrDefault(g => g.Type == type)?.Membres ?? Array.Empty<UserAccount>();
        public IReadOnlyList<UserAccount> Parents => De(LiensFamille.Parent);
        public IReadOnlyList<UserAccount> Enfants => De(LiensFamille.Enfant);
        public IReadOnlyList<UserAccount> Conjoints => De(LiensFamille.Conjoint);
        public IReadOnlyList<UserAccount> FreresSoeurs => De(LiensFamille.Fratrie);
        public IReadOnlyList<UserAccount> GrandsParents => De(LiensFamille.GrandParent);
        public IReadOnlyList<UserAccount> PetitsEnfants => De(LiensFamille.PetitEnfant);
    }

    private List<UserAccount> Lies(UserAccount u, string type)
        => (u.Famille ?? new List<LienFamille>())
            .Where(l => l.Type == type)
            .Select(l => FindUserByIdentifier(l.Identifiant))
            .OfType<UserAccount>()
            .Where(x => x.Id != u.Id)
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.NomComplet, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Frères et sœurs déclarés, ou enfants d'un même parent.</summary>
    private List<UserAccount> FratrieDe(UserAccount u)
        => Lies(u, LiensFamille.Fratrie)
            .Concat(Lies(u, LiensFamille.Parent).SelectMany(p => Lies(p, LiensFamille.Enfant)))
            .Where(x => x.Id != u.Id)
            .DistinctBy(x => x.Id)
            .ToList();

    public FamilleVue Famille(UserAccount u)
    {
        IEnumerable<UserAccount> De(IEnumerable<UserAccount> source, string type)
            => source.SelectMany(x => Lies(x, type));

        var parents = Lies(u, LiensFamille.Parent);
        var enfants = Lies(u, LiensFamille.Enfant);
        var conjoints = Lies(u, LiensFamille.Conjoint);
        var fratrie = FratrieDe(u).Concat(Lies(u, LiensFamille.DemiFratrie)).DistinctBy(x => x.Id).ToList();
        var grandsParents = De(parents, LiensFamille.Parent).Concat(Lies(u, LiensFamille.GrandParent)).ToList();
        var petitsEnfants = De(enfants, LiensFamille.Enfant).Concat(Lies(u, LiensFamille.PetitEnfant)).ToList();
        var oncles = parents.SelectMany(FratrieDe).Concat(Lies(u, LiensFamille.OncleTante)).ToList();

        // Liens déduits des liens enregistrés (les liens enregistrés viennent en plus, voir plus bas).
        var deduits = new Dictionary<string, IEnumerable<UserAccount>>
        {
            [LiensFamille.Fratrie] = FratrieDe(u),
            [LiensFamille.GrandParent] = De(parents, LiensFamille.Parent),
            [LiensFamille.PetitEnfant] = De(enfants, LiensFamille.Enfant),
            [LiensFamille.ArriereGrandParent] = De(grandsParents, LiensFamille.Parent),
            [LiensFamille.ArrierePetitEnfant] = De(petitsEnfants, LiensFamille.Enfant),
            [LiensFamille.OncleTante] = parents.SelectMany(FratrieDe),
            [LiensFamille.NeveuNiece] = De(fratrie, LiensFamille.Enfant),
            [LiensFamille.Cousin] = De(oncles, LiensFamille.Enfant),
            [LiensFamille.BeauParent] = De(conjoints, LiensFamille.Parent),
            [LiensFamille.GendreBru] = De(enfants, LiensFamille.Conjoint),
            [LiensFamille.BeauFrereSoeur] = conjoints.SelectMany(FratrieDe).Concat(De(fratrie, LiensFamille.Conjoint)),
            [LiensFamille.ConjointDuParent] = De(parents, LiensFamille.Conjoint),
            [LiensFamille.EnfantDuConjoint] = De(conjoints, LiensFamille.Enfant),
        };

        // Un membre relié explicitement n'apparaît que sous son lien enregistré ;
        // un lien déduit n'apparaît qu'une fois, sous le premier type qui le trouve.
        var explicites = (u.Famille ?? new List<LienFamille>())
            .Select(l => FindUserByIdentifier(l.Identifiant)?.Id).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var vus = new HashSet<string>(StringComparer.Ordinal) { u.Id };
        var groupes = new List<GroupeFamille>();
        // Les déductions suivent toujours l'ordre d'origine ; l'affichage suit l'ordre réglé.
        var ordre = ListesChoix.Elements(ListesChoix.LiensDeFamille, _settings.Listes).Select(e => e.Cle).ToList();
        foreach (var type in LiensFamille.Tous.Concat(ordre.Except(LiensFamille.Tous)))
        {
            var membres = Lies(u, type)
                .Concat((deduits.GetValueOrDefault(type) ?? Enumerable.Empty<UserAccount>()).Where(x => !explicites.Contains(x.Id)))
                .Where(x => x.Id != u.Id && (explicites.Contains(x.Id) ? LienEntre(u, x.Identifiant) == type : !vus.Contains(x.Id)))
                .DistinctBy(x => x.Id)
                .OrderBy(x => x.NomComplet, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            foreach (var m in membres) vus.Add(m.Id);
            groupes.Add(new GroupeFamille(type, membres));
        }
        return new FamilleVue(groupes.OrderBy(g => ordre.IndexOf(g.Type) is var i and >= 0 ? i : int.MaxValue).ToList());
    }

    /// <summary>Lien explicite entre deux fiches (null si aucun).</summary>
    public string? LienEntre(UserAccount u, string autreIdentifiant)
        => (u.Famille ?? new List<LienFamille>())
            .FirstOrDefault(l => l.Identifiant.Equals(autreIdentifiant, StringComparison.OrdinalIgnoreCase))?.Type;

    /// <summary>Déclare que <paramref name="autreIdentifiant"/> est le <paramref name="type"/> du membre <paramref name="userId"/>.</summary>
    public OperationResult AjouterLienFamille(string userId, string autreIdentifiant, string type)
    {
        if (!Can(Permissions.UsersManage)) return OperationResult.Fail(L.T("Action réservée à la gestion des utilisateurs."));
        if (!ListesChoix.Contient(ListesChoix.LiensDeFamille, type, _settings.Listes)) return OperationResult.Fail(L.T("Type de lien inconnu."));
        var moi = _users.FirstOrDefault(u => u.Id == userId);
        var autre = FindUserByIdentifier(autreIdentifiant ?? "");
        if (moi == null || autre == null) return OperationResult.Fail(L.T("Membre introuvable."));
        if (moi.Id == autre.Id) return OperationResult.Fail(L.T("Un membre ne peut pas être lié à lui-même."));
        if (LienEntre(moi, autre.Identifiant) is { } existant)
            return OperationResult.Fail(L.F("{0} est déjà lié(e) à cette fiche ({1}).", autre.NomComplet, L.T(LiensFamille.Libelle(existant)).ToLowerInvariant()));
        if (LiensFamille.EstAscendant(type) || LiensFamille.EstAscendant(LiensFamille.Inverse(type)))
        {
            // Pas de boucle : on ne peut pas être son propre ancêtre.
            var parent = LiensFamille.EstAscendant(type) ? autre : moi;
            var enfant = LiensFamille.EstAscendant(type) ? moi : autre;
            if (EstAncetre(enfant, parent))
                return OperationResult.Fail(L.F("{0} est déjà un ascendant de {1}.", enfant.NomComplet, parent.NomComplet));
        }

        moi.Famille ??= new List<LienFamille>();
        autre.Famille ??= new List<LienFamille>();
        moi.Famille.Add(new LienFamille { Identifiant = autre.Identifiant, Type = type });
        autre.Famille.RemoveAll(l => l.Identifiant.Equals(moi.Identifiant, StringComparison.OrdinalIgnoreCase));
        autre.Famille.Add(new LienFamille { Identifiant = moi.Identifiant, Type = LiensFamille.Inverse(type) });
        SaveUsers();
        LogActivity(ActivityTypes.Update, "Lien de famille : " + autre.Identifiant + " (" + L.EnFrancais(() => LiensFamille.Libelle(type)).ToLowerInvariant() + ") de " + moi.Identifiant);
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Retire le lien entre deux membres, des deux côtés.</summary>
    public OperationResult RetirerLienFamille(string userId, string autreIdentifiant)
    {
        if (!Can(Permissions.UsersManage)) return OperationResult.Fail(L.T("Action réservée à la gestion des utilisateurs."));
        var moi = _users.FirstOrDefault(u => u.Id == userId);
        if (moi == null) return OperationResult.Fail(L.T("Membre introuvable."));
        var n = (moi.Famille ?? new List<LienFamille>()).RemoveAll(l => l.Identifiant.Equals(autreIdentifiant, StringComparison.OrdinalIgnoreCase));
        var autre = FindUserByIdentifier(autreIdentifiant);
        if (autre != null)
            n += (autre.Famille ?? new List<LienFamille>()).RemoveAll(l => l.Identifiant.Equals(moi.Identifiant, StringComparison.OrdinalIgnoreCase));
        if (n == 0) return OperationResult.Fail(L.T("Aucun lien à retirer."));
        SaveUsers();
        LogActivity(ActivityTypes.Update, "Lien de famille retiré entre " + moi.Identifiant + " et " + autreIdentifiant);
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Vrai si <paramref name="a"/> est parent, grand-parent… de <paramref name="b"/>.</summary>
    private bool EstAncetre(UserAccount a, UserAccount b)
    {
        var vus = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<UserAccount> Ascendants(UserAccount x)
            => LiensFamille.Tous.Where(LiensFamille.EstAscendant).SelectMany(t => Lies(x, t));
        var file = new Queue<UserAccount>(Ascendants(b));
        while (file.Count > 0)
        {
            var p = file.Dequeue();
            if (p.Id == a.Id) return true;
            if (!vus.Add(p.Id)) continue;
            foreach (var gp in Ascendants(p)) file.Enqueue(gp);
        }
        return false;
    }

    /// <summary>Responsable légal affiché : celui saisi, sinon un parent, sinon un tuteur lié à la fiche.
    /// Sans téléphone sur la fiche du parent ou du tuteur, celui saisi sur la fiche du mineur est repris.</summary>
    public (string Nom, string Telephone) ResponsableAffiche(UserAccount u)
    {
        var telSaisi = (u.ResponsableTelephone ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(u.ResponsableLegal)) return (u.ResponsableLegal.Trim(), telSaisi);
        var parent = Lies(u, LiensFamille.Parent).FirstOrDefault(p => !p.EstMineur);
        if (parent != null) return (L.F("{0} (parent)", parent.NomComplet), Premier(parent.Telephone, telSaisi));
        var tuteur = Lies(u, LiensFamille.Tuteur).FirstOrDefault(p => !p.EstMineur);
        return tuteur == null ? ("", telSaisi) : (L.F("{0} (tuteur)", tuteur.NomComplet), Premier(tuteur.Telephone, telSaisi));
    }

    private static string Premier(string? a, string b) => string.IsNullOrWhiteSpace(a) ? b : a.Trim();

    /// <summary>Téléphone du premier parent (ou tuteur) majeur relié à la fiche qui en a un.</summary>
    private string TelephoneResponsableRelie(UserAccount u)
        => Lies(u, LiensFamille.Parent).Concat(Lies(u, LiensFamille.Tuteur))
            .Where(p => !p.EstMineur).Select(p => (p.Telephone ?? "").Trim()).FirstOrDefault(t => t.Length > 0) ?? "";

    /// <summary>Vrai si un parent ou un tuteur majeur est relié à la fiche.</summary>
    private bool AResponsableRelie(UserAccount u)
        => Lies(u, LiensFamille.Parent).Concat(Lies(u, LiensFamille.Tuteur)).Any(p => !p.EstMineur);
}
