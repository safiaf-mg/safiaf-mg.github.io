// Scanner de QR code (feuille de présence) : caméra arrière du téléphone.
// BarcodeDetector quand le navigateur le propose (Chrome Android), sinon jsQR
// (iPhone, anciens navigateurs), chargé seulement à la première ouverture.
window.guQrScan = {
    _gen: 0,
    _stream: null,
    _timer: 0,
    _ref: null,
    _dernier: '',
    _dernierA: 0,
    _chargement: null,

    start: async function (videoId, dotnetRef) {
        this.stop();
        var gen = this._gen;
        this._ref = dotnetRef;
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) return 'non-supporte';
        var stream;
        try {
            stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false });
        } catch (e) {
            return e && (e.name === 'NotAllowedError' || e.name === 'SecurityError') ? 'refuse' : 'erreur';
        }
        // Fenêtre fermée pendant la demande d'autorisation : on rend la caméra.
        if (gen !== this._gen) { this._couper(stream); return 'arrete'; }
        this._stream = stream;
        var video = document.getElementById(videoId);
        if (!video) { this.stop(); return 'erreur'; }
        video.setAttribute('playsinline', '');
        video.muted = true;
        video.srcObject = stream;
        try { await video.play(); } catch (e) { }
        var lire = await this._lecteur();
        if (gen !== this._gen) return 'arrete';
        if (!lire) { this.stop(); return 'non-supporte'; }
        var self = this, occupe = false;
        this._timer = setInterval(async function () {
            if (occupe || gen !== self._gen || video.readyState < 2) return;
            occupe = true;
            try {
                var texte = await lire(video);
                if (texte && gen === self._gen) self._signaler(texte);
            } catch (e) { }
            occupe = false;
        }, 250);
        return 'ok';
    },

    stop: function () {
        this._gen++;
        if (this._timer) { clearInterval(this._timer); this._timer = 0; }
        if (this._stream) { this._couper(this._stream); this._stream = null; }
        this._ref = null;
        this._dernier = '';
    },

    _couper: function (stream) {
        stream.getTracks().forEach(function (t) { try { t.stop(); } catch (e) { } });
    },

    // Le même QR code tenu devant la caméra n'est signalé qu'une fois
    // (de nouveau seulement après 3 s sans le voir).
    _signaler: function (texte) {
        var maintenant = Date.now();
        var deja = texte === this._dernier && maintenant - this._dernierA < 3000;
        this._dernierA = maintenant;
        if (deja) return;
        this._dernier = texte;
        try { if (navigator.vibrate) navigator.vibrate(80); } catch (e) { }
        if (this._ref) this._ref.invokeMethodAsync('OnQrLu', texte).catch(function () { });
    },

    _lecteur: async function () {
        if ('BarcodeDetector' in window) {
            try {
                var formats = await window.BarcodeDetector.getSupportedFormats();
                if (formats.indexOf('qr_code') >= 0) {
                    var detecteur = new window.BarcodeDetector({ formats: ['qr_code'] });
                    return async function (video) {
                        var codes = await detecteur.detect(video);
                        return codes.length ? codes[0].rawValue : null;
                    };
                }
            } catch (e) { }
        }
        await this._chargerJsQr();
        if (!window.jsQR) return null;
        var canvas = document.createElement('canvas');
        var ctx = canvas.getContext('2d', { willReadFrequently: true });
        return async function (video) {
            var w = video.videoWidth, h = video.videoHeight;
            if (!w || !h) return null;
            var k = Math.min(1, 800 / Math.max(w, h));
            canvas.width = Math.round(w * k);
            canvas.height = Math.round(h * k);
            ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
            var image = ctx.getImageData(0, 0, canvas.width, canvas.height);
            var code = window.jsQR(image.data, image.width, image.height, { inversionAttempts: 'dontInvert' });
            return code ? code.data : null;
        };
    },

    _chargerJsQr: function () {
        if (window.jsQR) return Promise.resolve();
        if (this._chargement) return this._chargement;
        var self = this;
        this._chargement = new Promise(function (resolve) {
            var s = document.createElement('script');
            s.src = 'lib/jsqr/jsQR.min.js';
            s.onload = function () { resolve(); };
            s.onerror = function () { self._chargement = null; resolve(); };
            document.head.appendChild(s);
        });
        return this._chargement;
    }
};
