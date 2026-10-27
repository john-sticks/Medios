(() => {
    const form = document.getElementById('formFiltrada');
    if (!form) return;
    const filas = Array.from(form.querySelectorAll('.nota-filtrable'));
    const filtros = Array.from(document.querySelectorAll('.filtro-notas'));
    const verSeleccionadas = document.getElementById('verSeleccionadas');
    const crear = document.getElementById('crearFiltrada');
    const aviso = document.getElementById('sinNotasVisibles');
    let soloSeleccionadas = false;

    function checkbox(fila) { return fila.querySelector('input[name="seleccionIds"]'); }
    function cantidad() { return filas.filter(fila => checkbox(fila).checked).length; }

    function actualizarVista() {
        filas.forEach(fila => {
            const valores = [fila.dataset.delegacion, fila.dataset.categoria, fila.dataset.partido];
            const coincide = filtros.every((filtro, i) => !filtro.value || filtro.value === valores[i]);
            fila.hidden = !coincide || (soloSeleccionadas && !checkbox(fila).checked);
        });
        verSeleccionadas.textContent = soloSeleccionadas ? 'Ver todas' : 'Ver seleccionadas';
        verSeleccionadas.setAttribute('aria-pressed', String(soloSeleccionadas));
        document.getElementById('cuentaSeleccion').textContent = cantidad() + ' nota(s) seleccionada(s)';
        crear.disabled = cantidad() === 0 || form.dataset.enviando === 'true';
        aviso.hidden = filas.some(fila => !fila.hidden);
        aviso.textContent = soloSeleccionadas ? 'No hay noticias seleccionadas.' : 'No hay noticias que coincidan con los filtros.';
    }

    filtros.forEach(filtro => filtro.addEventListener('change', () => {
        soloSeleccionadas = false;
        actualizarVista();
    }));
    document.getElementById('seleccionarVisibles').addEventListener('click', () => {
        filas.filter(fila => !fila.hidden).forEach(fila => checkbox(fila).checked = true);
        actualizarVista();
    });
    document.getElementById('limpiarSeleccion').addEventListener('click', () => {
        filas.forEach(fila => checkbox(fila).checked = false);
        actualizarVista();
    });
    verSeleccionadas.addEventListener('click', () => {
        filtros.forEach(filtro => filtro.value = '');
        soloSeleccionadas = !soloSeleccionadas;
        actualizarVista();
    });
    form.addEventListener('change', actualizarVista);
    form.addEventListener('submit', event => {
        if (cantidad() === 0 || form.dataset.enviando === 'true') { event.preventDefault(); return; }
        form.dataset.enviando = 'true';
        crear.disabled = true;
        crear.textContent = 'Creando...';
    });
    actualizarVista();
})();
