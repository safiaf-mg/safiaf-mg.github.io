// Pont JS pour GestionUtilisateurs (MAUI Blazor Hybrid)
window.guLangue = {
    get: function () { try { return localStorage.getItem('gu2:langue'); } catch (e) { return null; } },
    set: function (code) {
        try { localStorage.setItem('gu2:langue', code); } catch (e) { }
        document.documentElement.lang = code === 'mg' ? 'mg' : (code === 'en' ? 'en' : 'fr');
    },
    // Changement de langue : le rechargement qui suit garde un écran de chargement court.
    recharger: function (code) {
        window.guLangue.set(code);
        try { sessionStorage.setItem('gu2:chargement-court', '1'); } catch (e) { }
    }
};

// Nom et logo de la communauté pour l'écran de connexion (gardés depuis la
// dernière visite, puis mis à jour par le serveur).
window.guMarque = {
    lire: function () {
        try { return { nom: localStorage.getItem('gu_app_name') || '', logo: localStorage.getItem('gu_logo') || '', slogan: localStorage.getItem('gu_slogan') || '' }; }
        catch (e) { return { nom: '', logo: '', slogan: '' }; }
    },
    ecrire: function (nom, logo, slogan) {
        try {
            if (nom) localStorage.setItem('gu_app_name', nom);
            if (logo) localStorage.setItem('gu_logo', logo); else localStorage.removeItem('gu_logo');
            if (typeof slogan === 'string') window.guMarque.slogan(slogan);
        } catch (e) { }
    },
    slogan: function (slogan) {
        try {
            if (slogan) localStorage.setItem('gu_slogan', slogan); else localStorage.removeItem('gu_slogan');
        } catch (e) { }
    }
};

window.guTheme = {
    apply: function (theme, color, appName, logoDataUrl, slogan) {
        var root = document.documentElement;
        if (theme === 'dark') root.setAttribute('data-theme', 'dark');
        else root.removeAttribute('data-theme');
        root.style.setProperty('--accent', color || '#4f46e5');
        if (appName) document.title = appName;
        var meta = document.querySelector('meta[name="theme-color"]');
        if (meta) meta.setAttribute('content', color || '#4f46e5');
        var icon = document.querySelector('link[rel="icon"]');
        if (icon && !icon.dataset.defaut) icon.dataset.defaut = icon.getAttribute('href');
        if (logoDataUrl) {
            if (icon) icon.href = logoDataUrl;
            try { localStorage.setItem('gu_logo', logoDataUrl); } catch (e) { }
            window.guSplash.setLogo(logoDataUrl);
        } else {
            // Logo retiré dans Apparence : on revient au logo livré avec le site,
            // y compris pour l'écran de chargement des prochaines ouvertures.
            if (icon) icon.href = icon.dataset.defaut;
            try { localStorage.removeItem('gu_logo'); } catch (e) { }
        }
        if (appName) {
            try { localStorage.setItem('gu_app_name', appName); } catch (e) { }
            window.guSplash.setName(appName);
        }
        // Slogan gardé pour la page de connexion des prochaines ouvertures.
        if (typeof slogan === 'string') window.guMarque.slogan(slogan.trim());
    }
};

// Écran de chargement : logo configuré affiché pendant ~5 s au démarrage.
window.guSplash = {
    _start: Date.now(),
    _ready: false,
    _minMs: 5000,
    init: function () {
        try {
            if (sessionStorage.getItem('gu2:chargement-court')) {
                sessionStorage.removeItem('gu2:chargement-court');
                window.guSplash._minMs = 0;
            }
        } catch (e) { }
        try {
            var cached = localStorage.getItem('gu_logo');
            if (cached) window.guSplash.setLogo(cached);
            var name = localStorage.getItem('gu_app_name');
            if (name) window.guSplash.setName(name);
        } catch (e) { }
        window.guSplash._tick();
        // Sécurité : ne jamais bloquer plus de 9 s même si le reste échoue.
        setTimeout(function () { window.guSplash.hide(); }, 9000);
    },
    _tick: function () {
        var left = window.guSplash._minMs - (Date.now() - window.guSplash._start);
        if (left <= 0 && window.guSplash._ready) { window.guSplash.hide(); return; }
        setTimeout(window.guSplash._tick, left > 0 ? Math.min(left, 200) : 200);
    },
    setLogo: function (url) {
        if (!url) return;
        var img = document.getElementById('splash-logo');
        var brand = document.getElementById('splash-brand');
        if (img) { img.src = url; img.style.display = 'block'; }
        if (brand) brand.style.display = 'none';
    },
    setName: function (name) {
        if (!name) return;
        var t = document.querySelector('#app-splash .splash-title');
        if (t) t.textContent = name;
    },
    ready: function () {
        window.guSplash._ready = true;
        if (Date.now() - window.guSplash._start >= window.guSplash._minMs) window.guSplash.hide();
    },
    hide: function () {
        var s = document.getElementById('app-splash');
        if (!s || s.classList.contains('splash-hide')) return;
        s.classList.add('splash-hide');
        setTimeout(function () { if (s.parentNode) s.parentNode.removeChild(s); }, 400);
    }
};
window.guSplash.init();

window.guNav = {
    _ref: null,
    _bound: false,
    // Retour par balayage vers la droite, partout dans l'application :
    // fling horizontal franc, hors champs de saisie, listes à défilement
    // horizontal, lecteurs média et fenêtres ouvertes (modales, menus, visionneuse).
    enable: function (dotnetRef) {
        window.guNav._ref = dotnetRef;
        if (window.guNav._bound) return;
        window.guNav._bound = true;
        var startX = 0, startY = 0, startT = 0, tracking = false;
        function blocked(target) {
            if (document.documentElement.classList.contains('modal-open')) return true;
            if (document.querySelector('.modal-overlay, .photo-overlay, .more-sheet, .ob-overlay')) return true;
            if (!target || !target.closest) return false;
            return !!target.closest('input, textarea, select, [contenteditable], ' +
                '.chips-row, .more-sheet-grid, .mpicker-list, video, audio, .community-reader');
        }
        document.addEventListener('touchstart', function (e) {
            if (e.touches.length !== 1) { tracking = false; return; }
            var t = e.touches[0];
            if (blocked(t.target)) { tracking = false; return; }
            startX = t.clientX;
            startY = t.clientY;
            startT = Date.now();
            tracking = true;
        }, { passive: true });
        document.addEventListener('touchend', function (e) {
            if (!tracking || !window.guNav._ref) return;
            tracking = false;
            var t = e.changedTouches[0];
            var dx = t.clientX - startX, dy = t.clientY - startY;
            var dt = Date.now() - startT;
            if (dt < 600 && dx > 90 && Math.abs(dy) < 70 && Math.abs(dx) > 2 * Math.abs(dy)) {
                try { window.guNav._ref.invokeMethodAsync('OnSwipeBack'); } catch (_) { }
            }
        }, { passive: true });
        document.addEventListener('touchcancel', function () { tracking = false; }, { passive: true });
    },
    disable: function () {
        window.guNav._ref = null;
    }
};

// Retour du système (geste « retour » d'Android, retour prédictif, bouton
// Précédent du navigateur). L'application n'a pas d'adresses par page : on garde
// une seule entrée d'historique « de garde » tant qu'il y a quelque chose à
// fermer ou une page précédente. Le retour ferme d'abord la fenêtre ouverte,
// sinon revient à la page précédente ; sans rien de tout cela, il quitte l'app.
window.guRetour = {
    _ref: null,
    _bound: false,
    _garde: false,
    _ignorer: 0,
    _pagePrecedente: false,
    _minuteur: null,
    _ouvert: function () {
        var o = document.querySelectorAll('.modal-overlay, .more-overlay, .photo-overlay');
        return o.length ? o[o.length - 1] : null;
    },
    _sync: function () {
        var r = window.guRetour;
        if (!r._ref) return;
        var besoin = r._pagePrecedente || !!r._ouvert();
        try {
            if (besoin && !r._garde) {
                history.pushState({ guRetour: 1 }, '');
                r._garde = true;
            } else if (!besoin && r._garde) {
                r._garde = false;
                r._ignorer++;
                history.back();
            }
        } catch (_) { }
    },
    _plusTard: function () {
        var r = window.guRetour;
        clearTimeout(r._minuteur);
        r._minuteur = setTimeout(r._sync, 60);
    },
    enable: function (dotnetRef) {
        var r = window.guRetour;
        r._ref = dotnetRef;
        if (!r._bound) {
            r._bound = true;
            window.addEventListener('popstate', function () {
                if (r._ignorer > 0) { r._ignorer--; return; }
                if (!r._garde || !r._ref) return;
                r._garde = false;
                var o = r._ouvert();
                if (o) o.click();
                else if (r._pagePrecedente) { try { r._ref.invokeMethodAsync('OnSwipeBack'); } catch (_) { } }
                r._plusTard();
            });
            new MutationObserver(r._plusTard).observe(document.body, { childList: true, subtree: true });
        }
        r._plusTard();
    },
    pagePrecedente: function (oui) {
        window.guRetour._pagePrecedente = !!oui;
        window.guRetour._plusTard();
    },
    disable: function () {
        window.guRetour._ref = null;
    }
};

// Navigation de l'histoire de la communauté par balayage horizontal
// (gauche → chapitre suivant, droite → chapitre précédent), uniquement
// depuis la zone du lecteur (.community-reader). guNav ignore cette zone.
window.guCommunity = {
    _ref: null,
    _bound: false,
    enable: function (dotnetRef) {
        window.guCommunity._ref = dotnetRef;
        if (window.guCommunity._bound) return;
        window.guCommunity._bound = true;
        var startX = 0, startY = 0, startT = 0, tracking = false, onPhoto = false;
        document.addEventListener('touchstart', function (e) {
            if (e.touches.length !== 1) { tracking = false; return; }
            var t = e.target && e.target.closest ? e.target.closest('.community-reader, .community-card, .community-dots') : null;
            if (!t) { tracking = false; return; }
            // Sur les photos d'un chapitre, le balayage change de photo plutôt que de chapitre.
            onPhoto = !!e.target.closest('.community-gallery');
            var p = e.touches[0];
            startX = p.clientX;
            startY = p.clientY;
            startT = Date.now();
            tracking = true;
        }, { passive: true });
        document.addEventListener('touchend', function (e) {
            if (!tracking || !window.guCommunity._ref) return;
            tracking = false;
            var t = e.changedTouches[0];
            var dx = t.clientX - startX, dy = t.clientY - startY;
            var dt = Date.now() - startT;
            if (dt < 700 && Math.abs(dx) > 70 && Math.abs(dx) > 1.5 * Math.abs(dy)) {
                try {
                    window.guCommunity._ref.invokeMethodAsync(onPhoto ? 'OnSwipePhoto' : 'OnSwipeChapter', dx < 0 ? 1 : -1);
                } catch (_) { }
            }
        }, { passive: true });
        document.addEventListener('touchcancel', function () { tracking = false; }, { passive: true });
    },
    disable: function () {
        window.guCommunity._ref = null;
    }
};

window.guDownload = {
    text: function (filename, content, mime) {
        var blob = new Blob([content], { type: mime || 'application/octet-stream' });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = filename || 'fichier.txt';
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 4000);
    },
    dataUrl: function (filename, dataUrl) {
        var a = document.createElement('a');
        a.href = dataUrl;
        a.download = filename || 'fichier';
        document.body.appendChild(a);
        a.click();
        a.remove();
    }
};

window.guInfo = {
    localBytes: function () {
        try {
            var total = 0;
            for (var i = 0; i < localStorage.length; i++) {
                var k = localStorage.key(i);
                total += k.length + (localStorage.getItem(k) || '').length;
            }
            return total;
        } catch (e) { return 0; }
    }
};

window.guSession = {
    _ref: null,
    _bound: false,
    _last: 0,
    enable: function (dotnetRef) {
        window.guSession._ref = dotnetRef;
        if (!window.guSession._bound) {
            window.guSession._bound = true;
            var notify = function () {
                var now = Date.now();
                // Limite les appels C# : max 1 fois toutes les 20 secondes.
                if (now - window.guSession._last < 20000) return;
                window.guSession._last = now;
                if (window.guSession._ref) {
                    try { window.guSession._ref.invokeMethodAsync('OnUserActivity'); } catch (e) { }
                }
            };
            ['pointerdown', 'keydown', 'touchstart', 'wheel'].forEach(function (evt) {
                document.addEventListener(evt, notify, { passive: true });
            });
        }
    },
    disable: function () {
        window.guSession._ref = null;
    }
};

window.guModal = {
    _n: 0,
    lock: function () {
        window.guModal._n++;
        document.documentElement.classList.add('modal-open');
        document.body.classList.add('modal-open');
    },
    unlock: function () {
        window.guModal._n = Math.max(0, window.guModal._n - 1);
        if (window.guModal._n === 0) {
            document.documentElement.classList.remove('modal-open');
            document.body.classList.remove('modal-open');
        }
    }
};

window.guUi = {
    clearInput: function (id) {
        var el = document.getElementById(id);
        if (el) el.value = '';
    },
    scrollToId: function (id) {
        var el = document.getElementById(id);
        if (!el) return;
        try {
            el.scrollIntoView({ behavior: 'smooth', block: 'start' });
        } catch (_) {
            el.scrollIntoView(true);
        }
    },
    scrollTop: function () {
        try { window.scrollTo({ top: 0, left: 0, behavior: 'instant' }); }
        catch (_) { try { window.scrollTo(0, 0); } catch (_) { } }
        try { document.documentElement.scrollTop = 0; } catch (_) { }
        try { document.body.scrollTop = 0; } catch (_) { }
        try {
            var sc = document.querySelector('.app-content');
            if (sc) {
                try { sc.scrollTo({ top: 0, left: 0, behavior: 'instant' }); }
                catch (_) { sc.scrollTop = 0; }
            }
        } catch (_) { }
    }
};

// ── Signature électronique : pad de dessin hors ligne (doigt / souris / stylet) ──
// Export PNG fond blanc, prêt pour UserAccount.Signature et la carte de membre.
window.guSignature = {
    _pads: {},
    init: function (id) {
        var canvas = document.getElementById(id);
        if (!canvas) return false;
        this.destroy(id);
        var dpr = window.devicePixelRatio || 1;
        var cssW = canvas.clientWidth || (canvas.parentElement && canvas.parentElement.clientWidth) || 520;
        var cssH = 180;
        canvas.width = Math.floor(cssW * dpr);
        canvas.height = Math.floor(cssH * dpr);
        canvas.style.height = cssH + 'px';
        var ctx = canvas.getContext('2d');
        ctx.scale(dpr, dpr);
        ctx.lineWidth = 2.6;
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';
        ctx.strokeStyle = '#111827';
        var st = { canvas: canvas, ctx: ctx, drawing: false, hasDrawn: false, lastX: 0, lastY: 0 };
        function pos(e) {
            var r = canvas.getBoundingClientRect();
            return { x: e.clientX - r.left, y: e.clientY - r.top };
        }
        function down(e) {
            e.preventDefault();
            var p = pos(e);
            st.drawing = true;
            st.lastX = p.x; st.lastY = p.y;
            try { canvas.setPointerCapture(e.pointerId); } catch (_) { }
        }
        function move(e) {
            if (!st.drawing) return;
            e.preventDefault();
            var p = pos(e);
            ctx.beginPath();
            ctx.moveTo(st.lastX, st.lastY);
            ctx.lineTo(p.x, p.y);
            ctx.stroke();
            st.lastX = p.x; st.lastY = p.y;
            st.hasDrawn = true;
        }
        function up(e) {
            if (!st.drawing) return;
            st.drawing = false;
            try { canvas.releasePointerCapture(e.pointerId); } catch (_) { }
        }
        canvas.addEventListener('pointerdown', down);
        canvas.addEventListener('pointermove', move);
        canvas.addEventListener('pointerup', up);
        canvas.addEventListener('pointercancel', up);
        canvas.addEventListener('pointerleave', up);
        st.handlers = { down: down, move: move, up: up };
        this._pads[id] = st;
        return true;
    },
    clear: function (id) {
        var st = this._pads[id];
        if (!st) {
            var c = document.getElementById(id);
            if (c) { c.getContext('2d').clearRect(0, 0, c.width, c.height); }
            return true;
        }
        st.ctx.save();
        st.ctx.setTransform(1, 0, 0, 1, 0, 0);
        st.ctx.clearRect(0, 0, st.canvas.width, st.canvas.height);
        st.ctx.restore();
        var dpr = window.devicePixelRatio || 1;
        st.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        st.hasDrawn = false;
        return true;
    },
    isEmpty: function (id) {
        var st = this._pads[id];
        return st ? !st.hasDrawn : true;
    },
    toDataUrl: function (id) {
        var st = this._pads[id];
        var src = st ? st.canvas : document.getElementById(id);
        if (!src) return null;
        if (st && !st.hasDrawn) return null;
        var out = document.createElement('canvas');
        out.width = src.width;
        out.height = src.height;
        var octx = out.getContext('2d');
        octx.fillStyle = '#ffffff';
        octx.fillRect(0, 0, out.width, out.height);
        octx.drawImage(src, 0, 0);
        return out.toDataURL('image/png');
    },
    destroy: function (id) {
        var st = this._pads[id];
        if (!st) return;
        try {
            st.canvas.removeEventListener('pointerdown', st.handlers.down);
            st.canvas.removeEventListener('pointermove', st.handlers.move);
            st.canvas.removeEventListener('pointerup', st.handlers.up);
            st.canvas.removeEventListener('pointercancel', st.handlers.up);
            st.canvas.removeEventListener('pointerleave', st.handlers.up);
        } catch (_) { }
        delete this._pads[id];
    }
};

// Clavier à l'écran : garde toujours visible le champ en cours de saisie
// (scrollIntoView au focus puis à chaque frappe). Avec windowSoftInputMode
// adjustResize, la page est réduite et le champ reste au-dessus du clavier.
window.guScroll = {
    _init: false,
    _view: function () {
        return {
            vh: window.innerHeight || document.documentElement.clientHeight,
            kb: 0
        };
    },
    ensure: function (el) {
        if (!el || typeof el.scrollIntoView !== 'function') return;
        var v = window.guScroll._view();
        var r = el.getBoundingClientRect();
        if (r.top < 0 || r.bottom > v.vh - 8) {
            el.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    },
    init: function () {
        if (window.guScroll._init) return;
        window.guScroll._init = true;
        document.addEventListener('focusin', function (e) {
            var t = e.target;
            if (!t) return;
            var tag = t.tagName;
            if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || t.isContentEditable) {
                var self = t;
                setTimeout(function () { window.guScroll.ensure(self); }, 150);
            }
        }, true);
        document.addEventListener('input', function (e) {
            var t = e.target;
            if (!t) return;
            var tag = t.tagName;
            if (tag === 'INPUT' || tag === 'TEXTAREA' || t.isContentEditable) {
                window.guScroll.ensure(t);
            }
        }, true);
    }
};
if (window.guScroll) { window.guScroll.init(); }

// ── Guide interactif (Intro.js, chargé hors ligne dans lib/introjs) ──
// Le tour est piloté depuis Blazor (guTour.start). Les étapes sont fournies
// en JSON ; à la fin (terminé ou passé), Blazor marque le guide comme vu.
window.guTour = {
    _intro: null,
    _doneFired: false,
    start: function (dotnetRef, stepsJson, onStepChange, onFinished) {
        if (!window.introJs) { window.guTour._done(dotnetRef, onFinished); return; }
        window.guTour.stop();
        window.guTour._doneFired = false;
        var steps;
        try { steps = JSON.parse(stepsJson); } catch (_) { window.guTour._done(dotnetRef, onFinished); return; }
        if (!steps || !steps.length) { window.guTour._done(dotnetRef, onFinished); return; }
        // Sur téléphone le bouton Paramètres est en bas à droite : popup calé
        // à droite (flèche dessus). Sur tablette (rail à gauche) : calé à gauche.
        // Barre du bas : popup au-dessus sur téléphone, à droite du rail sur tablette.
        // Sur petit écran, les positions latérales (left/right/alignées) débordent :
        // on les rabat sur 'bottom' et on laisse le repositionnement auto corriger.
        try {
            var wide = window.innerWidth >= 720;
            steps.forEach(function (s) {
                if (!s) return;
                if (!s.element) { s.position = 'bottom'; return; }
                if (s.element === '[aria-label="Paramètres"]')
                    s.position = wide ? 'top-left-aligned' : 'bottom';
                else if (s.element === '.bottom-nav')
                    s.position = wide ? 'right' : 'top';
                else if (!wide && s.position && s.position !== 'bottom' && s.position !== 'top')
                    s.position = 'bottom';
            });
        } catch (_) { }
        try {
            var inst = window.introJs();
            inst.setOptions({
                steps: steps,
                prevLabel: '← Précédent',
                nextLabel: 'Suivant →',
                doneLabel: 'Terminer ✓',
                skipLabel: 'Passer',
                stepNumbersOfLabel: 'sur',
                exitOnOverlayClick: false,
                // Repositionnement automatique : la bulle bascule du côté visible
                // au lieu de déborder de l'écran (téléphones étroits, boutons de bord).
                autoPosition: true,
                showProgress: true,
                showBullets: true,
                showStepNumbers: true,
                disableInteraction: false
            });
            if (onStepChange) {
                inst.onchange(function () {
                    try { dotnetRef.invokeMethodAsync(onStepChange); } catch (_) { }
                });
            }
            inst.oncomplete(function () { window.guTour._done(dotnetRef, onFinished); });
            inst.onexit(function () { window.guTour._done(dotnetRef, onFinished); });
            window.guTour._intro = inst;
            setTimeout(function () { try { inst.start(); } catch (_) { window.guTour._done(dotnetRef, onFinished); } }, 350);
        } catch (_) {
            window.guTour._done(dotnetRef, onFinished);
        }
    },
    stop: function () {
        // Vide _intro AVANT exit() : intro.js redéclenche onexit de façon
        // synchrone, ce qui re-appellerait stop()/_done() en boucle
        // (débordement de pile + overlay jamais nettoyé).
        var inst = window.guTour._intro;
        window.guTour._intro = null;
        if (inst) {
            try { inst.exit(true); } catch (_) { }
        }
    },
    _done: function (dotnetRef, onFinished) {
        // oncomplete PUIS onexit se déclenchent tous deux : un seul appel .NET.
        if (window.guTour._doneFired) return;
        window.guTour._doneFired = true;
        window.guTour.stop();
        if (dotnetRef && onFinished) {
            try { dotnetRef.invokeMethodAsync(onFinished); } catch (_) { }
        }
    }
};

/* ---------- Mini-guide illustré : focus carte ---------- */
window.guOnboarding = {
    // Même seuil que la mise en page ordinateur (css/online.css).
    isDesktop: function () {
        try { return window.matchMedia('(min-width: 1024px)').matches; } catch (_) { return false; }
    },
    focus: function (id) {
        try {
            var el = document.getElementById(id);
            if (el && el.focus) el.focus({ preventScroll: true });
        } catch (_) { }
    }
};

/* ---------- guCoach : visite fléchée (spotlight sur l'élément visé) ---------- */
/* Soulève la barre parente (.app-topbar / .bottom-nav) au-dessus du voile
   puis entoure la cible d'un anneau indigo. Retourne le rectangle de la
   cible pour positionner la bulle + sa flèche côté Blazor. */
window.guCoach = {
    spot: function (selector) {
        try { this.unspot(); } catch (_) { }
        if (!selector) return null;
        var el = null;
        try { el = document.querySelector(selector); } catch (_) { return null; }
        if (!el) return null;
        // Mesure instantanée : pas de défilement animé (sinon le rectangle
        // est mesuré avant la fin du scroll et la bulle finit hors écran,
        // boutons inatteignables après « Suivant »).
        try {
            if (el.scrollIntoView) el.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
        } catch (_) {
            try { el.scrollIntoView({ block: 'nearest', inline: 'nearest' }); } catch (_) { }
        }
        try { el.classList.add('ob-spot'); } catch (_) { }
        var barRight = 0;
        try {
            var bar = el.closest ? el.closest('.app-topbar, .bottom-nav, .web-sidebar') : null;
            if (bar) {
                bar.classList.add('ob-spot-root');
                // Menu latéral : la bulle se place à droite de tout le menu, pas sur lui.
                if (bar.classList.contains('web-sidebar')) barRight = bar.getBoundingClientRect().right;
            }
        } catch (_) { }
        var r = el.getBoundingClientRect();
        var vw = window.innerWidth || document.documentElement.clientWidth || 0;
        var vh = window.innerHeight || document.documentElement.clientHeight || 0;
        // Cible invisible, masquée ou hors écran : repli carte centrée
        // (toujours cliquable) au lieu d'une bulle mal placée.
        if (!r || r.width <= 0 || r.height <= 0) { try { this.unspot(); } catch (_) { } return null; }
        if (vw <= 0 || vh <= 0) { try { this.unspot(); } catch (_) { } return null; }
        if (r.bottom < 0 || r.top > vh || r.right < 0 || r.left > vw) { try { this.unspot(); } catch (_) { } return null; }
        return { x: r.left, y: r.top, w: r.width, h: r.height, vw: vw, vh: vh, br: barRight };
    },
    unspot: function () {
        try {
            var s = document.querySelectorAll('.ob-spot');
            for (var i = 0; i < s.length; i++) s[i].classList.remove('ob-spot');
            var b = document.querySelectorAll('.ob-spot-root');
            for (var j = 0; j < b.length; j++) b[j].classList.remove('ob-spot-root');
        } catch (_) { }
    }
};

/* ---------- guDragReorder : réorganisation par glisser-déposer ---------- */
/* Pointer Events (souris + tactile + stylet) : fonctionne sur téléphone et
   tablette comme sur bureau. Le geste démarre uniquement sur la poignée
   (.reorder-handle, touch-action:none) pour ne pas bloquer le défilement.
   Stabilité : pendant le geste, le DOM n'est JAMAIS réordonné (seul
   l'élément glissé est translaté + un indicateur de dépôt est affiché).
   L'ordre final est calculé depuis la géométrie figée au démarrage et
   appliqué par Blazor au dépôt : aucun conflit avec les re-rendus. */
window.guDragReorder = {
    _active: null,
    enable: function (listId, dotnetRef, onDropMethod) {
        var list = document.getElementById(listId);
        if (!list) return false;
        this.disable(listId);
        var self = this;
        function collect() {
            return Array.prototype.slice.call(list.querySelectorAll('[data-rkey]'));
        }
        function clearMark() {
            try {
                var m = list.querySelectorAll('.drop-target');
                for (var i = 0; i < m.length; i++) m[i].classList.remove('drop-target');
            } catch (_) { }
        }
        function onDown(e) {
            if (self._active) return;
            var handle = e.target && e.target.closest ? e.target.closest('.reorder-handle') : null;
            if (!handle || !list.contains(handle)) return;
            if (e.pointerType === 'mouse' && e.button !== undefined && e.button !== 0) return;
            var item = handle.closest('[data-rkey]');
            if (!item) return;
            try { e.preventDefault(); } catch (_) { }
            var nodes = collect();
            var rects = [];
            for (var i = 0; i < nodes.length; i++) {
                var r = nodes[i].getBoundingClientRect();
                rects.push({
                    key: nodes[i].getAttribute('data-rkey'),
                    el: nodes[i],
                    mid: r.top + r.height / 2
                });
            }
            var selfRect = rects.filter(function (x) { return x.el === item; })[0];
            if (!selfRect) return;
            self._active = {
                listId: listId,
                list: list,
                ref: dotnetRef,
                method: onDropMethod,
                item: item,
                handle: handle,
                pid: e.pointerId,
                startY: e.clientY,
                startScroll: window.scrollY || window.pageYOffset || 0,
                center0: selfRect.mid,
                rects: rects,
                target: -1,
                moved: false,
                done: false
            };
            item.classList.add('dragging');
            list.classList.add('reordering');
            try { handle.setPointerCapture(e.pointerId); } catch (_) { }
        }
        function onMove(e) {
            var st = self._active;
            if (!st || st.done || st.listId !== listId) return;
            if (st.pid !== undefined && e.pointerId !== undefined && e.pointerId !== st.pid) return;
            var dy = e.clientY - st.startY;
            if (!st.moved && Math.abs(dy) < 5) return;
            st.moved = true;
            try { e.preventDefault(); } catch (_) { }
            st.item.style.transform = 'translateY(' + dy + 'px)';
            // Défilement auto près des bords (listes longues sur téléphone).
            try {
                if (e.clientY < 90) window.scrollBy(0, -12);
                else if (e.clientY > window.innerHeight - 90) window.scrollBy(0, 12);
            } catch (_) { }
            var scrollDelta = (window.scrollY || window.pageYOffset || 0) - st.startScroll;
            var center = st.center0 + dy + scrollDelta;
            // Nombre d'AUTRES éléments dont le milieu est au-dessus du centre :
            // c'est l'index final dans la liste complète (0 = premier).
            var t = 0;
            for (var i = 0; i < st.rects.length; i++) {
                if (st.rects[i].el === st.item) continue;
                if (st.rects[i].mid + scrollDelta < center) t++;
            }
            if (t !== st.target) {
                st.target = t;
                clearMark();
                var without = st.rects.filter(function (x) { return x.el !== st.item; });
                var mark = without[Math.min(t, without.length - 1)];
                if (mark && mark.el !== st.item) {
                    try { mark.el.classList.add('drop-target'); } catch (_) { }
                }
            }
        }
        function cleanup(st) {
            try { st.item.classList.remove('dragging'); } catch (_) { }
            try { st.list.classList.remove('reordering'); } catch (_) { }
            try { st.item.style.transform = ''; } catch (_) { }
            clearMark();
        }
        function finish(commit) {
            var st = self._active;
            if (!st || st.done || st.listId !== listId) return;
            st.done = true;
            self._active = null;
            // Ordre final depuis la géométrie figée (insensible à l'état du DOM).
            var without = [];
            for (var i = 0; i < st.rects.length; i++) {
                if (st.rects[i].el !== st.item) without.push(st.rects[i].key);
            }
            var selfKey = st.item.getAttribute('data-rkey');
            var at = Math.max(0, Math.min(st.target < 0 ? without.length : st.target, without.length));
            var keys = without.slice(0, at).concat([selfKey], without.slice(at));
            cleanup(st);
            if (!commit || !st.moved) return;
            try {
                if (st.ref && st.method) st.ref.invokeMethodAsync(st.method, keys);
            } catch (_) { }
        }
        function onUp() { finish(true); }
        function onCancel() { finish(false); }
        function onLostCapture(e) {
            var st = self._active;
            if (!st || st.done || st.listId !== listId) return;
            if (e.pointerId !== undefined && st.pid !== undefined && e.pointerId !== st.pid) return;
            finish(false);
        }
        list.addEventListener('pointerdown', onDown);
        list.addEventListener('pointermove', onMove, { passive: false });
        list.addEventListener('pointerup', onUp);
        list.addEventListener('pointercancel', onCancel);
        list.addEventListener('lostpointercapture', onLostCapture);
        list._guReorder = { down: onDown, move: onMove, up: onUp, cancel: onCancel, lost: onLostCapture };
        return true;
    },
    disable: function (listId) {
        var list = document.getElementById(listId);
        if (!list || !list._guReorder) return;
        try {
            list.removeEventListener('pointerdown', list._guReorder.down);
            list.removeEventListener('pointermove', list._guReorder.move);
            list.removeEventListener('pointerup', list._guReorder.up);
            list.removeEventListener('pointercancel', list._guReorder.cancel);
            list.removeEventListener('lostpointercapture', list._guReorder.lost);
        } catch (_) { }
        list._guReorder = null;
    }
};