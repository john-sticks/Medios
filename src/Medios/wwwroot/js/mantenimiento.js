(function () {
    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('form').forEach(function (form) {
            if ((form.method || '').toLowerCase() !== 'post' || /\/login/i.test(form.action)) return;
            form.querySelectorAll('button[type="submit"], input[type="submit"], button:not([type])').forEach(function (button) {
                button.disabled = true;
                button.title = 'El sistema está en mantenimiento; solo se permiten consultas.';
            });
        });
    });
})();
