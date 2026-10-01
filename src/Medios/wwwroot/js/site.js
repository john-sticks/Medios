(function () {
    var estabaOffline = false;

    function mostrarAvisoOffline() {
        if (document.getElementById('aviso-offline')) return;
        estabaOffline = true;
        var aviso = document.createElement('div');
        aviso.id = 'aviso-offline';
        aviso.style.cssText =
            'position:fixed;bottom:20px;left:50%;transform:translateX(-50%);' +
            'background:#c0392b;color:#fff;padding:12px 24px;border-radius:6px;' +
            'font-size:15px;z-index:99999;box-shadow:0 2px 8px rgba(0,0,0,0.3);';
        aviso.innerHTML = '<i class="fa fa-wifi" style="margin-right:8px;"></i>Sin conexión a internet';
        document.body.appendChild(aviso);
    }

    function ocultarAvisoOffline() {
        var aviso = document.getElementById('aviso-offline');
        if (!aviso) return;
        aviso.remove();
        if (estabaOffline) {
            estabaOffline = false;
            if (typeof toastr !== 'undefined') {
                toastr.success('Conexión restaurada', '', { timeOut: 0, closeButton: true });
            }
        }
    }

    // Verificacion real contra el propio servidor (evita falsos positivos de navigator.onLine)
    function verificar() {
        var xhr = new XMLHttpRequest();
        xhr.open('GET', '/favicon.ico?_=' + Date.now(), true);
        xhr.timeout = 4000;
        xhr.onload = function () { ocultarAvisoOffline(); };
        xhr.onerror = function () { mostrarAvisoOffline(); };
        xhr.ontimeout = function () { mostrarAvisoOffline(); };
        xhr.send();
    }

    // Evento offline: feedback inmediato
    window.addEventListener('offline', mostrarAvisoOffline);

    // Evento online: verifica contra el servidor antes de informar restauracion
    window.addEventListener('online', function () {
        setTimeout(verificar, 500);
    });

    // Polling cada 8s como respaldo
    setInterval(verificar, 8000);
})();

// Geocodifica una localidad (Nominatim) dentro de la Provincia de Buenos Aires.
// Llama cb(lat, lng) si la encuentra. Usado por los formularios de nota para ubicar
// en el mapa la localidad seleccionada.
window.geocodarLocalidad = function (loc, partido, cb) {
    if (!loc) return;
    var partes = [loc];
    if (partido && partido.indexOf('—') < 0) partes.push(partido);
    partes.push('Provincia de Buenos Aires, Argentina');
    var q = partes.join(', ');
    if (typeof $ === 'undefined') return;
    $.getJSON('https://nominatim.openstreetmap.org/search',
        { q: q, format: 'json', limit: 1, 'accept-language': 'es' }, function (data) {
            if (data && data.length > 0) cb(parseFloat(data[0].lat), parseFloat(data[0].lon));
        });
};
