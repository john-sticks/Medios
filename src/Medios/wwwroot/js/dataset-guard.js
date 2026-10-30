(function () {
    var meta = document.querySelector('meta[name="keryx-dataset"]');
    if (!meta) return;
    var marker = meta.content;
    function applies(method, url) {
        try { return !/^(GET|HEAD|OPTIONS)$/i.test(method || 'GET') && new URL(url || location.href, location.href).origin === location.origin; }
        catch (_) { return false; }
    }
    function bind(form) {
        if (!applies(form.method, form.action)) return;
        var field = form.querySelector('input[name="__KeryxDataset"]');
        if (!field) { field = document.createElement('input'); field.type = 'hidden'; field.name = '__KeryxDataset'; form.appendChild(field); }
        field.value = marker;
    }
    document.addEventListener('DOMContentLoaded', function () { document.querySelectorAll('form').forEach(bind); });
    document.addEventListener('submit', function (event) { if (event.target.tagName === 'FORM') bind(event.target); }, true);
    var originalFetch = window.fetch;
    window.fetch = function (input, options) {
        var request = input instanceof Request ? input : null;
        var method = (options && options.method) || (request && request.method) || 'GET';
        if (applies(method, request ? request.url : input)) {
            options = Object.assign({}, options);
            options.headers = new Headers(options.headers || (request && request.headers));
            options.headers.set('X-Keryx-Dataset', marker);
        }
        return originalFetch.call(this, input, options);
    };
    var open = XMLHttpRequest.prototype.open;
    var send = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.open = function (method, url) { this.keryxDatasetRequired = applies(method, url); return open.apply(this, arguments); };
    XMLHttpRequest.prototype.send = function () { if (this.keryxDatasetRequired) this.setRequestHeader('X-Keryx-Dataset', marker); return send.apply(this, arguments); };
})();
