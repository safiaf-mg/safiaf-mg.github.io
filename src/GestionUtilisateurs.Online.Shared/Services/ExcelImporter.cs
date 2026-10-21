using System.IO.Compression;
using System.Xml;
using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Lit un classeur .xlsx (Open XML) exporté par l'application et reconstruit une
/// liste d'utilisateurs. Les colonnes personnalisées sont rapprochées des champs
/// par libellé.
/// </summary>
public static class ExcelImporter
{
    private static readonly string[] StandardHeaders =
    {
        "nom", "prenom", "identifiant", "matricule", "email", "telephone", "role",
        "statut", "date de naissance", "date creation", "derniere connexion", "notes"
    };

    public static ImportResult Parse(byte[] xlsx, IReadOnlyList<FieldDefinition> fields)
    {
        try
        {
            using var ms = new MemoryStream(xlsx);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

            var sharedStrings = ReadSharedStrings(zip);
            var sheetEntry = ResolveFirstWorksheet(zip);
            if (sheetEntry == null)
                return new ImportResult { Success = false, Error = L.T("Le classeur ne contient pas de feuille de calcul.") };

            var sheetRows = ReadRows(sheetEntry, sharedStrings);
            if (sheetRows.Count < 1)
                return new ImportResult { Success = false, Error = L.T("Le classeur est vide.") };

            var header = sheetRows[0];
            var colMap = new Dictionary<int, string>();
            for (var c = 0; c < header.Length; c++)
            {
                var h = NormalizeHeader(header[c]);
                if (!string.IsNullOrWhiteSpace(h)) colMap[c] = h;
            }
            if (!colMap.Values.Any(v => StandardHeaders.Contains(v)))
                return new ImportResult { Success = false, Error = L.T("Le fichier ne ressemble pas à un export utilisateur (en-têtes introuvables).") };

            // En-têtes et libellés comparés sous la même forme : un champ « Baptisé » ou
            // « Lieu d'origine » est retrouvé malgré ses accents et apostrophes.
            var fieldByLabel = new Dictionary<string, FieldDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in fields)
                if (!string.IsNullOrWhiteSpace(f.Label)) fieldByLabel[NormalizeHeader(f.Label)] = f;

            var users = new List<UserAccount>();
            for (var r = 1; r < sheetRows.Count; r++)
            {
                var row = sheetRows[r];
                if (row.All(string.IsNullOrWhiteSpace)) continue;

                var u = new UserAccount { Id = Guid.NewGuid().ToString("N") };
                var seen = new HashSet<string>();
                for (var c = 0; c < row.Length; c++)
                {
                    if (!colMap.TryGetValue(c, out var key) || string.IsNullOrWhiteSpace(row[c])) continue;
                    if (!seen.Add(key)) continue;

                    switch (key)
                    {
                        case "nom": u.Nom = row[c].Trim(); break;
                        case "prenom": u.Prenom = row[c].Trim(); break;
                        case "identifiant": u.Identifiant = row[c].Trim(); break;
                        case "matricule": u.Matricule = row[c].Trim(); break;
                        case "email": u.Email = row[c].Trim(); break;
                        case "telephone": u.Telephone = row[c].Trim(); break;
                        case "role": u.Role = NormalizeRole(row[c]); break;
                        case "statut": u.Actif = row[c].Trim().StartsWith("Actif", StringComparison.OrdinalIgnoreCase); break;
                        case "date de naissance": u.DateNaissance = ParseYmd(row[c]); break;
                        case "date creation": u.DateCreation = ParseDate(row[c]); break;
                        case "derniere connexion": u.DerniereConnexion = string.IsNullOrWhiteSpace(row[c]) ? null : row[c]; break;
                        case "notes": u.Notes = row[c]; break;
                        case "date de deces": u.DateDeces = ParseYmd(row[c]); break;
                        case "responsable legal": u.ResponsableLegal = row[c].Trim(); break;
                        case "telephone du responsable": u.ResponsableTelephone = row[c].Trim(); break;
                        default:
                            if (fieldByLabel.TryGetValue(key, out var f))
                                u.Champs[f.Id] = FieldValueFrom(f, row[c]);
                            break;
                    }
                }

                if (string.IsNullOrWhiteSpace(u.Prenom) || string.IsNullOrWhiteSpace(u.Nom))
                    return new ImportResult { Success = false, Error = L.F("Ligne {0}: prénom et nom sont obligatoires.", r + 1) };
                if (string.IsNullOrWhiteSpace(u.Identifiant))
                    u.Identifiant = DataStore.GenerateIdentifiant(u.Prenom, u.Nom);
                if (string.IsNullOrWhiteSpace(u.MotDePasse))
                    u.MotDePasse = DataStore.DEFAULT_PASSWORD;
                if (string.IsNullOrWhiteSpace(u.DateCreation))
                    u.DateCreation = DateTimeOffset.Now.ToString("O");

                users.Add(u);
            }

            if (users.Count == 0)
                return new ImportResult { Success = false, Error = L.T("Le fichier ne contient aucun utilisateur.") };

            return new ImportResult { Success = true, Users = users };
        }
        catch (InvalidDataException ex)
        {
            return new ImportResult { Success = false, Error = L.F("Le fichier n'est pas un classeur Excel valide : {0}", ex.Message) };
        }
        catch (Exception ex)
        {
            return new ImportResult { Success = false, Error = L.F("Erreur de lecture du fichier : {0}", ex.Message) };
        }
    }

    /// <summary>Localise la première feuille de calcul du classeur selon workbook.xml et ses relations.</summary>
    private static ZipArchiveEntry? ResolveFirstWorksheet(ZipArchive zip)
    {
        const string relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var workbookEntry = zip.GetEntry("xl/workbook.xml");
        if (workbookEntry == null)
            return zip.GetEntry("xl/worksheets/sheet1.xml");

        string? rId = null;
        using (var reader = XmlReader.Create(workbookEntry.Open(), new XmlReaderSettings { IgnoreComments = true }))
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet")
                {
                    rId = reader.GetAttribute("id", relNs);
                    if (!string.IsNullOrWhiteSpace(rId)) break;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(rId))
        {
            var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (relsEntry != null)
            {
                using var relsReader = XmlReader.Create(relsEntry.Open(), new XmlReaderSettings { IgnoreComments = true });
                while (relsReader.Read())
                {
                    if (relsReader.NodeType == XmlNodeType.Element && relsReader.LocalName == "Relationship" &&
                        relsReader.GetAttribute("Id") == rId)
                    {
                        var target = relsReader.GetAttribute("Target") ?? "";
                        if (!target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                            target = "xl/" + target.TrimStart('/');
                        var entry = zip.GetEntry(target);
                        if (entry != null) return entry;
                        break;
                    }
                }
            }
        }
        return zip.GetEntry("xl/worksheets/sheet1.xml");
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var result = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return result;

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreComments = true });
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
            {
                result.Add(reader.ReadElementContentAsString());
            }
        }
        return result;
    }

    private static List<string[]> ReadRows(ZipArchiveEntry entry, List<string> shared)
    {
        var rows = new List<string[]>();
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreComments = true });

        string[]? current = null;
        int maxCol = 0;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row")
            {
                maxCol = 0;
                current = new string[0];
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "c" && current != null)
            {
                var type = reader.GetAttribute("t");
                var cellRef = reader.GetAttribute("r");
                var colIdx = ColIndex(cellRef);
                maxCol = Math.Max(maxCol, colIdx + 1);
                if (maxCol > current.Length) Array.Resize(ref current, maxCol);
                current[colIdx] = ReadCellValue(reader, type, shared);
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "row" && current != null)
            {
                if (current.Any(v => !string.IsNullOrWhiteSpace(v)))
                    rows.Add(current);
                current = null;
            }
        }
        return rows;
    }

    private static string ReadCellValue(XmlReader reader, string? type, List<string> shared)
    {
        // Lit le contenu <v> ou <t> de la cellule (partagé ou inlineStr) puis la ferme proprement.
        // ReadElementContentAsString positionne le lecteur sur la balise fermante </c> : on l'examine
        // avant tout Read() supplémentaire, sinon la boucle avalerait le reste du document.
        if (reader.IsEmptyElement) return "";
        string value = "";
        reader.Read();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "c")
                break;
            if (reader.NodeType == XmlNodeType.Element && (reader.LocalName == "v" || reader.LocalName == "t"))
            {
                value = reader.ReadElementContentAsString();
                continue;
            }
            reader.Read();
        }
        if (type == "s" && int.TryParse(value, out var idx) && idx >= 0 && idx < shared.Count)
            return shared[idx];
        return value;
    }

    private static int ColIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return 0;
        var letters = "";
        foreach (var ch in cellRef)
            if (char.IsLetter(ch)) letters += ch;
        var idx = 0;
        foreach (var ch in letters) idx = idx * 26 + (char.ToUpper(ch) - 'A' + 1);
        return Math.Max(0, idx - 1);
    }

    private static string NormalizeHeader(string? h)
        => (h ?? "").Trim().ToLowerInvariant()
            .Replace("é", "e").Replace("è", "e").Replace("ê", "e").Replace("ë", "e")
            .Replace("à", "a").Replace("â", "a").Replace("î", "i").Replace("ï", "i")
            .Replace("ô", "o").Replace("ö", "o").Replace("û", "u").Replace("ù", "u")
            .Replace("ç", "c").Replace("'", "").Replace("-", " ").Replace("_", " ");

    private static string ParseYmd(string raw)
    {
        var s = raw.Trim();
        if (string.IsNullOrWhiteSpace(s)) return "";
        // Naissance affichée JJ/MM/AAAA, stockée yyyy-MM-dd.
        if (DateTime.TryParseExact(s, new[] { "dd/MM/yyyy", "d/M/yyyy" },
                System.Globalization.CultureInfo.GetCultureInfo("fr-FR"),
                System.Globalization.DateTimeStyles.None, out var fr))
            return fr.ToString("yyyy-MM-dd");
        if (DateTime.TryParseExact(s, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var ymd))
            return ymd.ToString("yyyy-MM-dd");
        return "";
    }

    private static string ParseDate(string raw)
    {
        var s = raw.Trim();
        if (string.IsNullOrWhiteSpace(s)) return DateTimeOffset.Now.ToString("O");
        // Format d'affichage de l'application : JJ/MM/AAAA (ex. 05/09/2026).
        if (DateTime.TryParseExact(s, new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm" },
                System.Globalization.CultureInfo.GetCultureInfo("fr-FR"),
                System.Globalization.DateTimeStyles.None, out var fr))
            return fr.ToString("O");
        if (DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out _))
            return s;
        if (DateTime.TryParse(s, System.Globalization.CultureInfo.CurrentCulture, out _))
            return s;
        return DateTimeOffset.Now.ToString("O");
    }

    private static string NormalizeRole(string? role) => role switch
    {
        Roles.BaseAdmin or "Administrateur de la base" or "dba" => Roles.BaseAdmin,
        "Administrateur" or "administrateur" or "admin" => "Administrateur",
        "Gestionnaire" or "gestionnaire" or "manager" => "Gestionnaire",
        _ => "Utilisateur"
    };

    private static FieldValue FieldValueFrom(FieldDefinition f, string raw)
    {
        var v = new FieldValue();
        switch (f.Type)
        {
            case FieldType.Nombre:
                if (double.TryParse(raw.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var n)) v.Nombre = n;
                else v.Texte = raw;
                break;
            case FieldType.Booleen:
                v.Booleen = raw.StartsWith("Oui", StringComparison.OrdinalIgnoreCase) ||
                            raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1";
                break;
            default:
                v.Texte = raw;
                break;
        }
        return v;
    }
}
