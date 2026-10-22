// Pont entre l'application Blazor et Supabase (comptes, données partagées,
// fichiers). Les données sont gardées en cache sur l'appareil (IndexedDB)
// et seules les collections modifiées sont téléchargées, pour rester dans
// le transfert gratuit de Supabase (5 Go par mois).
window.guOnline = (function () {
    var client = null;
    var BUCKET = 'fichiers';
    // Au-delà de cette taille, un contenu « data:… » est envoyé dans le
    // stockage de fichiers et remplacé par une référence « gufile: ».
    var INLINE_MAX = 2048;

    function err(e) {
        if (!e) return 'Erreur inconnue.';
        return e.message || e.error_description || String(e);
    }

    // ---------- Cache local (IndexedDB) ----------

    var dbPromise = null;
    function db() {
        if (dbPromise) return dbPromise;
        dbPromise = new Promise(function (resolve, reject) {
            var req = indexedDB.open('gu_online', 1);
            req.onupgradeneeded = function () {
                var d = req.result;
                if (!d.objectStoreNames.contains('data')) d.createObjectStore('data');
                if (!d.objectStoreNames.contains('files')) d.createObjectStore('files');
                if (!d.objectStoreNames.contains('meta')) d.createObjectStore('meta');
            };
            req.onsuccess = function () { resolve(req.result); };
            req.onerror = function () { reject(req.error); };
        });
        return dbPromise;
    }

    function tx(store, mode, fn) {
        return db().then(function (d) {
            return new Promise(function (resolve, reject) {
                var t = d.transaction(store, mode);
                var s = t.objectStore(store);
                var result = fn(s);
                t.oncomplete = function () { resolve(result && 'result' in result ? result.result : result); };
                t.onerror = function () { reject(t.error); };
                t.onabort = function () { reject(t.error); };
            });
        });
    }

    function cacheGet(store, key) { return tx(store, 'readonly', function (s) { return s.get(key); }); }
    function cachePut(store, key, value) { return tx(store, 'readwrite', function (s) { s.put(value, key); }); }
    function cacheDelete(store, key) { return tx(store, 'readwrite', function (s) { s.delete(key); }); }
    function cacheClear(store) { return tx(store, 'readwrite', function (s) { s.clear(); }); }
    function cacheAll(store) {
        return db().then(function (d) {
            return new Promise(function (resolve, reject) {
                var out = {};
                var req = d.transaction(store, 'readonly').objectStore(store).openCursor();
                req.onsuccess = function () {
                    var c = req.result;
                    if (c) { out[c.key] = c.value; c.continue(); } else resolve(out);
                };
                req.onerror = function () { reject(req.error); };
            });
        });
    }

    // ---------- Fichiers : data URL <-> référence « gufile:<sha256>:<mime> » ----------

    async function sha256(text) {
        var buf = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
        return Array.from(new Uint8Array(buf)).map(function (b) { return b.toString(16).padStart(2, '0'); }).join('');
    }

    function walk(node, visit) {
        if (Array.isArray(node)) {
            for (var i = 0; i < node.length; i++) {
                if (typeof node[i] === 'string') { var r = visit(node[i]); if (r !== undefined) node[i] = r; }
                else if (node[i] && typeof node[i] === 'object') walk(node[i], visit);
            }
        } else if (node && typeof node === 'object') {
            for (var k in node) {
                if (!Object.prototype.hasOwnProperty.call(node, k)) continue;
                var v = node[k];
                if (typeof v === 'string') { var r2 = visit(v); if (r2 !== undefined) node[k] = r2; }
                else if (v && typeof v === 'object') walk(v, visit);
            }
        }
    }

    function dataUrlParts(s) {
        var m = /^data:([^;,]*)(;[^,]*)?;base64,/.exec(s);
        return m ? { mime: m[1] || 'application/octet-stream', offset: m[0].length } : null;
    }

    function dataUrlToBlob(s, parts) {
        var bin = atob(s.substring(parts.offset));
        var bytes = new Uint8Array(bin.length);
        for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        return new Blob([bytes], { type: parts.mime });
    }

    function blobToDataUrl(blob, mime) {
        return new Promise(function (resolve, reject) {
            var reader = new FileReader();
            reader.onload = function () { resolve(reader.result); };
            reader.onerror = function () { reject(reader.error); };
            reader.readAsDataURL(new Blob([blob], { type: mime }));
        });
    }

    // Envoie les fichiers intégrés et renvoie le JSON allégé.
    async function dehydrate(json) {
        var root = JSON.parse(json);
        var found = [];
        walk(root, function (s) {
            if (s.length > INLINE_MAX && s.charCodeAt(0) === 100 /* d */ && s.startsWith('data:')) {
                var parts = dataUrlParts(s);
                if (parts) found.push({ s: s, parts: parts });
            }
        });
        if (!found.length) return json;
        var refs = new Map();
        for (var i = 0; i < found.length; i++) {
            var f = found[i];
            if (refs.has(f.s)) continue;
            var hash = await sha256(f.s);
            var known = await cacheGet('files', hash);
            if (!known) {
                var up = await client.storage.from(BUCKET).upload('f/' + hash, dataUrlToBlob(f.s, f.parts), {
                    contentType: f.parts.mime, upsert: false, cacheControl: '31536000'
                });
                if (up.error && !/exist|duplicate/i.test(err(up.error))) throw new Error('Envoi du fichier impossible : ' + err(up.error));
                await cachePut('files', hash, f.s);
            }
            refs.set(f.s, 'gufile:' + hash + ':' + f.parts.mime);
        }
        walk(root, function (s) { return refs.has(s) ? refs.get(s) : undefined; });
        return JSON.stringify(root);
    }

    var REF = /^gufile:([0-9a-f]{64}):(.+)$/;

    // Remplace les références par le contenu des fichiers (téléchargés une
    // seule fois par appareil puis lus depuis le cache).
    async function hydrate(json) {
        if (json.indexOf('gufile:') < 0) return json;
        var root = JSON.parse(json);
        var wanted = new Map();
        walk(root, function (s) {
            var m = REF.exec(s);
            if (m) wanted.set(m[1], m[2]);
        });
        var contents = new Map();
        var missing = [];
        for (var entry of wanted) {
            var cached = await cacheGet('files', entry[0]);
            if (cached) contents.set(entry[0], cached); else missing.push(entry);
        }
        // Téléchargements en parallèle par petits groupes.
        for (var i = 0; i < missing.length; i += 6) {
            await Promise.all(missing.slice(i, i + 6).map(async function (e) {
                try {
                    var dl = await client.storage.from(BUCKET).download('f/' + e[0]);
                    if (dl.error) return;
                    var url = await blobToDataUrl(dl.data, e[1]);
                    await cachePut('files', e[0], url);
                    contents.set(e[0], url);
                } catch (_) { /* fichier indisponible : la référence est conservée */ }
            }));
        }
        walk(root, function (s) {
            var m = REF.exec(s);
            return m && contents.has(m[1]) ? contents.get(m[1]) : undefined;
        });
        return JSON.stringify(root);
    }

    // ---------- API ----------

    // ---------- Retour de Google (connexion OAuth, flux PKCE) ----------

    var supaUrl = '', supaKey = '', redirectErr = null;

    async function finishRedirect() {
        var q = new URLSearchParams(location.search);
        var code = q.get('code');
        var e = q.get('error_description') || q.get('error');
        if (!code && !e) return;
        if (code) {
            try {
                var r = await client.auth.exchangeCodeForSession(code);
                if (r.error) redirectErr = err(r.error);
            } catch (x) {
                redirectErr = err(x);
            }
        } else {
            redirectErr = (q.get('error') === 'access_denied' ? 'access_denied: ' : '') + e;
        }
        ['code', 'error', 'error_code', 'error_description', 'state'].forEach(function (k) { q.delete(k); });
        var rest = q.toString();
        history.replaceState(history.state, '', location.pathname + (rest ? '?' + rest : '') + location.hash);
    }

    return {
        init: async function (url, anonKey) {
            if (!url || !anonKey || !window.supabase) return false;
            supaUrl = url.replace(/\/+$/, '');
            supaKey = anonKey;
            client = window.supabase.createClient(url, anonKey, {
                auth: { persistSession: true, autoRefreshToken: true, detectSessionInUrl: false, flowType: 'pkce', storageKey: 'gu-online-auth' }
            });
            await finishRedirect();
            return true;
        },

        // Erreur rapportée au retour de Google, rendue une seule fois.
        redirectError: function () {
            var e = redirectErr;
            redirectErr = null;
            return e;
        },

        // Le fournisseur Google est-il activé dans Supabase (Authentication > Providers) ?
        googleActif: async function () {
            try {
                var r = await fetch(supaUrl + '/auth/v1/settings', { headers: { apikey: supaKey } });
                if (!r.ok) return false;
                var j = await r.json();
                return !!(j.external && j.external.google);
            } catch (_) {
                return false;
            }
        },

        // Part vers Google ; au retour, init() termine la connexion (code dans l'adresse).
        signInGoogle: async function () {
            var r = await client.auth.signInWithOAuth({
                provider: 'google',
                options: { redirectTo: location.origin + location.pathname, queryParams: { prompt: 'select_account' } }
            });
            return r.error ? { ok: false, error: err(r.error) } : { ok: true };
        },

        getSession: async function () {
            var r = await client.auth.getSession();
            var s = r.data && r.data.session;
            return s ? { id: s.user.id, email: s.user.email || '' } : null;
        },

        // « login » : e-mail, ou identifiant (sans @). L'identifiant est traduit en
        // e-mail par le serveur seulement si le mot de passe est juste (script 0004).
        signIn: async function (login, password) {
            var email = login;
            if (login.indexOf('@') < 0) {
                var q = await client.rpc('login_email', { p_identifiant: login, p_password: password });
                if (q.error) {
                    var m = err(q.error);
                    if (/trop_de_tentatives/.test(m)) return { ok: false, error: 'Trop d\'essais avec cet identifiant. Réessayez dans 15 minutes, ou connectez-vous avec votre e-mail.' };
                    if (/login_email|function|schema cache/i.test(m)) return { ok: false, error: 'La connexion par identifiant n\'est pas encore activée : utilisez votre e-mail.' };
                    return { ok: false, error: m };
                }
                if (!q.data) return { ok: false, error: 'Identifiant ou mot de passe incorrect.' };
                email = q.data;
            }
            var r = await client.auth.signInWithPassword({ email: email, password: password });
            if (r.error) return { ok: false, error: /invalid login/i.test(err(r.error)) ? (email === login ? 'E-mail ou mot de passe incorrect.' : 'Identifiant ou mot de passe incorrect.') : err(r.error) };
            return { ok: true };
        },

        signUp: async function (email, password, prenom, nom, telephone) {
            var r = await client.auth.signUp({
                email: email,
                password: password,
                options: { data: { prenom: prenom, nom: nom, telephone: telephone } }
            });
            if (r.error) {
                var m = err(r.error);
                if (/already registered|already exists/i.test(m)) m = 'Un compte existe déjà avec cet e-mail.';
                return { ok: false, error: m };
            }
            return { ok: true, hasSession: !!(r.data && r.data.session) };
        },

        signOut: async function () {
            try { await client.auth.signOut(); } catch (_) { }
        },

        updatePassword: async function (password) {
            var r = await client.auth.updateUser({ password: password });
            return r.error ? { ok: false, error: err(r.error) } : { ok: true };
        },

        // Appel d'une fonction SQL ; renvoie { data: "<json>" } ou { error }.
        rpc: async function (name, args) {
            var r = await client.rpc(name, args || {});
            if (r.error) return { error: err(r.error) };
            return { data: JSON.stringify(r.data === undefined ? null : r.data) };
        },

        // Nom, logo et slogan de la communauté pour l'écran de connexion (sans compte).
        marque: async function () {
            var r = await client.rpc('public_branding');
            if (r.error || !r.data) return { error: r.error ? err(r.error) : 'vide' };
            var nom = r.data.nom || '', logo = r.data.logo || '', slogan = r.data.slogan;
            var m = REF.exec(logo);
            if (m) {
                logo = '';
                try {
                    var dl = await client.storage.from(BUCKET).download('f/' + m[1]);
                    if (!dl.error) logo = await blobToDataUrl(dl.data, m[2]);
                } catch (_) { }
            }
            // slogan absent (script 0007 pas encore lancé) : on garde celui de l'appareil.
            return { nom: nom, logo: logo.startsWith('data:image/') ? logo : '', slogan: typeof slogan === 'string' ? slogan : null };
        },

        removeFiles: async function (names) {
            var r = await client.storage.from(BUCKET).remove(names);
            return r.error ? { ok: false, error: err(r.error) } : { ok: true };
        },

        // Le cache appartient au compte connecté : il est vidé si un autre
        // compte se connecte sur le même navigateur.
        openCache: async function (userId) {
            var owner = await cacheGet('meta', 'owner');
            if (owner !== userId) {
                await cacheClear('data');
                await cacheClear('files');
                await cachePut('meta', 'owner', userId);
            }
            return true;
        },

        clearCache: async function () {
            await cacheClear('data');
            await cacheClear('files');
            await cacheDelete('meta', 'owner');
        },

        // Charge les collections : cache + celles modifiées sur le serveur.
        // all=true : renvoie tout ; sinon seulement les changements.
        load: async function (all) {
            var cached = await cacheAll('data');
            var known = {};
            for (var k in cached) known[k] = cached[k].version;
            var r = await client.rpc('load_app_data', { p_known: known });
            if (r.error) return { error: err(r.error) };
            var readable = new Set(r.data.readable || []);
            var changed = {};
            for (var i = 0; i < r.data.rows.length; i++) {
                var row = r.data.rows[i];
                var entry = { value: JSON.stringify(row.value), version: row.version };
                await cachePut('data', row.key, entry);
                cached[row.key] = entry;
                changed[row.key] = true;
            }
            var removed = [];
            for (var key in cached) {
                if (!readable.has(key)) { removed.push(key); await cacheDelete('data', key); delete cached[key]; }
            }
            var rows = [];
            for (var key2 in cached) {
                if (!all && !changed[key2]) continue;
                rows.push({ key: key2, value: await hydrate(cached[key2].value), version: cached[key2].version });
            }
            return { rows: rows, removed: removed };
        },

        // Enregistre une collection (avec ses fichiers) si la version n'a pas changé.
        save: async function (key, json, expectedVersion) {
            try {
                var light = await dehydrate(json);
                var r = await client.rpc('save_app_data', { p_key: key, p_value: JSON.parse(light), p_expected_version: expectedVersion });
                if (r.error) return { status: 'error', message: err(r.error) };
                var res = r.data;
                if (res.status === 'ok') {
                    await cachePut('data', key, { value: light, version: res.version });
                    return { status: 'ok', version: res.version };
                }
                if (res.status === 'conflict' && res.value !== undefined) {
                    var server = JSON.stringify(res.value);
                    await cachePut('data', key, { value: server, version: res.version });
                    return { status: 'conflict', version: res.version, value: await hydrate(server) };
                }
                return { status: res.status, version: res.version || 0, message: res.message || '' };
            } catch (e) {
                return { status: 'error', message: err(e) };
            }
        }
    };
})();
