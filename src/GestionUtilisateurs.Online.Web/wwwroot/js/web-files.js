// Fichiers côté navigateur pour la version en ligne :
// sélection (galerie, appareil photo, documents), compression des photos,
// téléchargement et ouverture. Remplace les services natifs Android.
window.guWebFiles = (function () {
    // Les photos sont réduites avant l'envoi pour économiser le stockage
    // Supabase (1 Go gratuit) et le transfert mensuel (5 Go gratuits).
    var MAX_SIDE = 1600;
    var JPEG_QUALITY = 0.8;

    function readAsDataUrl(file) {
        return new Promise(function (resolve, reject) {
            var reader = new FileReader();
            reader.onload = function () { resolve(reader.result); };
            reader.onerror = function () { reject(reader.error); };
            reader.readAsDataURL(file);
        });
    }

    function loadImage(dataUrl) {
        return new Promise(function (resolve, reject) {
            var img = new Image();
            img.onload = function () { resolve(img); };
            img.onerror = reject;
            img.src = dataUrl;
        });
    }

    // Image décodée : <img> d'abord, puis createImageBitmap (certains formats ou
    // fichiers sans type passent mieux par là).
    async function decodeImage(file, dataUrl) {
        try {
            var img = await loadImage(dataUrl);
            if (img.naturalWidth && img.naturalHeight) return { source: img, w: img.naturalWidth, h: img.naturalHeight };
        } catch (e) { }
        if (window.createImageBitmap) {
            try {
                var bmp = await createImageBitmap(file);
                if (bmp.width && bmp.height) return { source: bmp, w: bmp.width, h: bmp.height };
            } catch (e) { }
        }
        return null;
    }

    var IMAGE_EXT = /\.(jpe?g|png|webp|gif|bmp|heic|heif|avif|tiff?)$/i;

    // Toute image (sauf SVG), y compris un fichier sans type dont le nom est celui d'une image.
    function compressible(file) {
        var type = file.type || '';
        if (/^image\/svg/i.test(type)) return false;
        if (/^image\//i.test(type)) return true;
        return !type && IMAGE_EXT.test(file.name || '');
    }

    function sizeOf(dataUrl) {
        return Math.round((dataUrl.length - dataUrl.indexOf(',') - 1) * 3 / 4);
    }

    // Réduit une image (côté le plus long ≤ 1600 px) et la ré-encode en JPEG.
    // Si le résultat dépasse encore la taille maximale, l'image est réduite
    // davantage. Garde l'original s'il est déjà plus léger que la version compressée.
    async function compressImage(file, dataUrl, maxBytes) {
        try {
            var img = await decodeImage(file, dataUrl);
            if (!img) return null;
            var essais = [[MAX_SIDE, JPEG_QUALITY], [1280, 0.7], [1024, 0.6], [800, 0.5]];
            var out = null, scale = 1;
            for (var i = 0; i < essais.length; i++) {
                scale = Math.min(1, essais[i][0] / Math.max(img.w, img.h));
                var canvas = document.createElement('canvas');
                canvas.width = Math.round(img.w * scale);
                canvas.height = Math.round(img.h * scale);
                var ctx = canvas.getContext('2d');
                ctx.fillStyle = '#ffffff';
                ctx.fillRect(0, 0, canvas.width, canvas.height);
                ctx.drawImage(img.source, 0, 0, canvas.width, canvas.height);
                out = canvas.toDataURL('image/jpeg', essais[i][1]);
                if (!maxBytes || sizeOf(out) <= maxBytes) break;
            }
            var outSize = sizeOf(out);
            if (outSize >= file.size && scale === 1 && (!maxBytes || file.size <= maxBytes)) return null;
            var name = (file.name || 'photo').replace(/\.[^.]+$/, '') + '.jpg';
            return { name: name, type: 'image/jpeg', size: outSize, dataUrl: out };
        } catch (e) {
            return null;
        }
    }

    function pickFile(accept, capture) {
        return new Promise(function (resolve) {
            var input = document.createElement('input');
            input.type = 'file';
            if (accept) input.accept = accept;
            if (capture) input.setAttribute('capture', capture);
            input.style.display = 'none';
            var done = false;
            function finish(file) {
                if (done) return;
                done = true;
                input.remove();
                resolve(file);
            }
            input.addEventListener('change', function () {
                finish(input.files && input.files.length ? input.files[0] : null);
            });
            // Annulation de la boîte de dialogue (navigateurs récents).
            input.addEventListener('cancel', function () { finish(null); });
            document.body.appendChild(input);
            input.click();
        });
    }

    function formatSize(bytes) {
        if (bytes < 1024) return bytes + ' o';
        if (bytes < 1024 * 1024) return Math.round(bytes / 1024) + ' Ko';
        return (Math.round(bytes / (1024 * 1024) * 10) / 10).toString().replace('.', ',') + ' Mo';
    }

    // Renvoie { name, type, size, dataUrl } ou { error } ou null (annulé).
    async function pick(accept, capture, maxBytes, label, compress) {
        var file = await pickFile(accept, capture);
        if (!file) return null;
        if (!file.size) return { error: 'Le fichier est vide.' };
        var dataUrl = await readAsDataUrl(file);
        var result = { name: file.name, type: file.type || '', size: file.size, dataUrl: dataUrl };
        // Une image animée (GIF) n'est réduite que si elle dépasse la taille maximale.
        var gif = /gif/i.test(file.type || '') || /\.gif$/i.test(file.name || '');
        if (compress && compressible(file) && (!gif || (maxBytes && file.size > maxBytes))) {
            var small = await compressImage(file, dataUrl, maxBytes);
            if (small) result = small;
        }
        if (maxBytes && result.size > maxBytes) {
            return { error: (label || 'Fichier') + ' refusé (' + formatSize(result.size) + ') : taille maximale de ' + formatSize(maxBytes) + '.' };
        }
        return result;
    }

    function toBlob(base64, mime) {
        var bin = atob(base64);
        var bytes = new Uint8Array(bin.length);
        for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        return new Blob([bytes], { type: mime || 'application/octet-stream' });
    }

    function save(fileName, base64, mime) {
        var url = URL.createObjectURL(toBlob(base64, mime));
        var a = document.createElement('a');
        a.href = url;
        a.download = fileName || 'fichier';
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
        return true;
    }

    function open(fileName, base64, mime) {
        var url = URL.createObjectURL(toBlob(base64, mime));
        var win = window.open(url, '_blank', 'noopener');
        if (!win) return save(fileName, base64, mime);
        setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
        return true;
    }

    async function share(files, title) {
        try {
            var list = files.map(function (f) { return new File([toBlob(f.base64, f.mime)], f.name, { type: f.mime }); });
            if (navigator.canShare && navigator.canShare({ files: list })) {
                await navigator.share({ files: list, title: title });
                return true;
            }
        } catch (e) {
            if (e && e.name === 'AbortError') return true;
        }
        files.forEach(function (f) { save(f.name, f.base64, f.mime); });
        return true;
    }

    return { pick: pick, save: save, open: open, share: share };
})();

// Impression en PDF (carte de membre) : @page à la taille exacte de la carte,
// marges à 0, pour éviter les en-têtes et pieds de page du navigateur.
window.guWebPrint = {
    printHtml: function (pagesHtml, fileName, pageWidthPt, pageHeightPt) {
        try {
            var w = window.open('', '_blank', 'width=900,height=700');
            if (!w) return false;
            var hasSize = pageWidthPt > 0 && pageHeightPt > 0;
            var w2 = hasSize ? (+pageWidthPt).toFixed(2) + 'pt' : '';
            var h2 = hasSize ? (+pageHeightPt).toFixed(2) + 'pt' : '';
            var pageRule = hasSize
                ? '@page{size:' + w2 + ' ' + h2 + ';margin:0}'
                : '@page{size:auto;margin:0}';
            // Chaque page a exactement la taille de la feuille : sans hauteur fixe, le
            // moteur d'impression arrondit et fait déborder chaque face sur une page de plus.
            var css = '<style>' + pageRule +
                'html,body{margin:0 !important;padding:0 !important;background:#fff}' +
                'body{font-family:Segoe UI,system-ui,sans-serif;-webkit-print-color-adjust:exact;print-color-adjust:exact}' +
                '.print-page{margin:0;padding:0;overflow:hidden;break-inside:avoid;page-break-after:always;break-after:page' +
                (hasSize ? ';width:' + w2 + ';height:calc(' + h2 + ' - 0.5pt)' : '') + '}' +
                '.print-page:last-child{page-break-after:auto;break-after:auto}' +
                'img{max-width:100%}</style>';
            var body = '';
            for (var i = 0; i < pagesHtml.length; i++) body += '<div class="print-page">' + pagesHtml[i] + '</div>';
            var title = String(fileName || 'document').replace(/[<>&"]/g, '');
            w.document.write('<!DOCTYPE html><html lang="fr"><head><meta charset="utf-8"><title>' +
                title + '</title>' + css + '</head><body>' + body + '</body></html>');
            w.document.close();
            w.focus();
            setTimeout(function () { try { w.print(); } catch (e) { } }, 600);
            return true;
        } catch (e) { return false; }
    }
};
