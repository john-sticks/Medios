document.addEventListener('DOMContentLoaded', function () {
    var form = document.getElementById('formFinalizadas');
    if (!form) return;
    var checks = Array.from(document.querySelectorAll('.finalizada-check:not(:disabled)'));
    var todas = document.getElementById('seleccionarFinalizadas');
    var boton = document.getElementById('consolidarSeleccionadas');
    var contador = document.getElementById('contadorFinalizadas');

    function visibles() {
        return checks.filter(function (check) { return check.closest('tr').style.display !== 'none'; });
    }

    function actualizar() {
        var seleccionadas = checks.filter(function (check) { return check.checked; }).length;
        var filas = visibles();
        var marcadas = filas.filter(function (check) { return check.checked; }).length;
        contador.textContent = seleccionadas + (seleccionadas === 1 ? ' síntesis seleccionada' : ' síntesis seleccionadas');
        boton.disabled = seleccionadas === 0;
        todas.checked = filas.length > 0 && marcadas === filas.length;
        todas.indeterminate = marcadas > 0 && marcadas < filas.length;
    }

    checks.forEach(function (check) { check.addEventListener('change', actualizar); });
    todas.addEventListener('change', function () {
        visibles().forEach(function (check) { check.checked = todas.checked; });
        actualizar();
    });
    form.addEventListener('submit', function (event) {
        if (!checks.some(function (check) { return check.checked; })) {
            event.preventDefault();
            actualizar();
        }
    });
    // Conservar los checks al cambiar de pestaña o volver con el navegador.
    document.addEventListener('finalizadas:visibilidad', actualizar);
    window.addEventListener('pageshow', actualizar);
    actualizar();
});
