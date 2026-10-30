(() => {
    const lista = document.getElementById('contactosGrupo');
    if (!lista) return;
    let siguiente = Number(lista.dataset.proximo || 0);
    const actualizar = () => {
        document.getElementById('cantidadContactos').textContent = lista.rows.length;
    };
    document.getElementById('agregarContacto').addEventListener('click', () => {
        const fila = document.getElementById('plantillaContacto').content.cloneNode(true);
        const indice = siguiente++;
        fila.querySelector('input[type="hidden"]').value = indice;
        fila.querySelectorAll('[data-campo]').forEach(input => {
            input.name = `Contactos[${indice}].${input.dataset.campo}`;
        });
        lista.appendChild(fila);
        actualizar();
        lista.lastElementChild.querySelector('[data-campo="Nombre"]').focus();
    });
    lista.addEventListener('click', event => {
        const boton = event.target.closest('.quitar-contacto');
        if (!boton) return;
        boton.closest('tr').remove();
        actualizar();
    });
})();
