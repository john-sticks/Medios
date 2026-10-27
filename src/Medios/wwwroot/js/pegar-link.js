// Ocultar la ayuda cuando el navegador no expone lectura del portapapeles (por ejemplo, HTTP).
if (!navigator.clipboard || !navigator.clipboard.readText) {
    document.querySelectorAll('[data-pegar-link]').forEach(function (boton) {
        boton.style.display = 'none';
    });
}

document.addEventListener('click', async function (event) {
    const boton = event.target.closest('[data-pegar-link]');
    if (!boton) return;
    event.preventDefault();
    const campo = document.getElementById(boton.dataset.pegarLink);
    if (!campo || boton.disabled) return;

    if (!navigator.clipboard || !navigator.clipboard.readText) {
        toastr.warning('Para usar Pegar, abrí el sistema mediante HTTPS. También podés pegar con Ctrl+V en Link.');
        return;
    }

    boton.disabled = true;
    try {
        const texto = (await navigator.clipboard.readText()).trim();
        if (!texto) {
            toastr.info('El portapapeles está vacío. Copiá una URL e intentá nuevamente.');
            return;
        }
        campo.value = texto;
        campo.dispatchEvent(new Event('input', { bubbles: true }));
        campo.dispatchEvent(new Event('change', { bubbles: true }));
        campo.focus();
    } catch {
        toastr.warning('El navegador no permitió leer el portapapeles. Permití el acceso o pegá con Ctrl+V en Link.');
    } finally {
        boton.disabled = false;
    }
});
