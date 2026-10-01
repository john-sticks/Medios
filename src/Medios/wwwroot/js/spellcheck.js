/* Corrector ortográfico + reglas de estilo/puntuación (cliente, sin enviar datos a terceros).
   - Ortografía: Typo.js + diccionario Hunspell español local. Subraya las palabras
     no reconocidas dentro del campo y permite Cambiar / Omitir / Agregar al diccionario.
   - Estilo/puntuación: reglas básicas (espacios, signos ¿¡, etc.) con corrección automática.
   Cubre los campos Título y Texto de las notas en todos los formularios. */
(function () {
    var typo = null, cargando = false, cola = [];
    var revisores = [];                                   // re-revisar todos los campos
    var ignoradas = {};                                  // omitidas en esta sesión
    var custom = {};                                     // diccionario propio (persistente)
    try { custom = JSON.parse(localStorage.getItem('spell_custom') || '{}'); } catch (e) { custom = {}; }

    function cargarDiccionario(cb) {
        if (typo) { cb(typo); return; }
        cola.push(cb);
        if (cargando) return;
        cargando = true;
        Promise.all([
            fetch('/lib/spell/es.aff.txt').then(function (r) { return r.text(); }),
            fetch('/lib/spell/es.dic.txt').then(function (r) { return r.text(); })
        ]).then(function (res) {
            typo = new Typo('es', res[0], res[1]);
            cola.forEach(function (f) { f(typo); });
            cola = [];
        }).catch(function () { cargando = false; });
    }

    var RE_PAL = /[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]+/g;

    function esAceptada(w) {
        var k = w.toLowerCase();
        return ignoradas[k] || custom[k];
    }
    function esCorrecta(t, w) {
        if (esAceptada(w)) return true;
        if (t.check(w)) return true;
        var lower = w.toLowerCase();
        if (t.check(lower)) return true;
        var cap = lower.charAt(0).toUpperCase() + lower.slice(1);
        return t.check(cap);
    }

    function detectar(t, texto) {
        var set = {}, lista = [], vistos = {}, m;
        RE_PAL.lastIndex = 0;
        while ((m = RE_PAL.exec(texto)) !== null) {
            var w = m[0];
            if (w.length < 3) continue;
            if (w === w.toUpperCase() && w.length <= 4) continue; // siglas
            var key = w.toLowerCase();
            if (set[key] !== undefined) continue;
            var ok = esCorrecta(t, w);
            set[key] = !ok;
            if (!ok && !vistos[key]) { vistos[key] = true; lista.push(w); }
        }
        return { set: set, lista: lista };
    }

    // ── Reglas de estilo / puntuación (auto-corregibles) ──────────────────
    var REGLAS = [
        { id: 'esp2',     re: / {2,}/g,                                          fix: ' ',   msg: 'Espacios de más' },
        { id: 'espAntes', re: / +([,.;:!?…])/g,                                  fix: '$1',  msg: 'Espacio antes de un signo de puntuación' },
        { id: 'faltaEsp', re: /([,;:])(?=[A-Za-zÁÉÍÓÚÜÑáéíóúüñ¿¡])/g,           fix: '$1 ', msg: 'Falta un espacio después de la coma/punto y coma' },
        { id: 'espParen', re: /\(\s+|\s+\)/g,                                    fix: function (s) { return s.trim() === '(' ? '(' : ')'; }, msg: 'Espacio sobrante junto a paréntesis' }
    ];

    function analizarGramatica(texto) {
        var obs = [];
        REGLAS.forEach(function (r) {
            r.re.lastIndex = 0;
            var c = (texto.match(r.re) || []).length;
            if (c > 0) obs.push({ regla: r, cantidad: c });
        });
        // Signos de apertura: más cierres que aperturas
        var preg = (texto.match(/\?/g) || []).length, apreg = (texto.match(/¿/g) || []).length;
        var exc = (texto.match(/!/g) || []).length, aexc = (texto.match(/¡/g) || []).length;
        if (preg > apreg) obs.push({ aviso: 'Faltan signos de apertura «¿» en preguntas' });
        if (exc > aexc) obs.push({ aviso: 'Faltan signos de apertura «¡» en exclamaciones' });
        return obs;
    }

    function escHtml(s) { return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'); }

    function pintarHtml(texto, set) {
        return escHtml(texto).replace(RE_PAL, function (w) {
            return set[w.toLowerCase()] ? '<mark>' + w + '</mark>' : w;
        }) + '\n';
    }

    function reemplazar(campo, palabra, nueva) {
        var re = new RegExp('(^|[^A-Za-zÁÉÍÓÚÜÑáéíóúüñ])(' +
            palabra.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') +
            ')(?![A-Za-zÁÉÍÓÚÜÑáéíóúüñ])', 'g');
        campo.value = campo.value.replace(re, function (m, pre) { return pre + nueva; });
        campo.dispatchEvent(new Event('input', { bubbles: true }));
    }

    var PROPS = ['fontFamily', 'fontSize', 'fontWeight', 'fontStyle', 'letterSpacing',
        'lineHeight', 'textTransform', 'wordSpacing', 'textAlign', 'paddingTop', 'paddingRight',
        'paddingBottom', 'paddingLeft', 'borderTopWidth', 'borderRightWidth',
        'borderBottomWidth', 'borderLeftWidth', 'boxSizing'];
    function sincronizarEstilo(campo, backdrop) {
        var cs = getComputedStyle(campo);
        PROPS.forEach(function (p) { backdrop.style[p] = cs[p]; });
        backdrop.style.borderStyle = 'solid';
        backdrop.style.borderColor = 'transparent';
        // Compensar el ancho del scrollbar vertical del textarea (angosta el área de texto)
        var sb = campo.offsetWidth - campo.clientWidth
               - parseFloat(cs.borderLeftWidth || 0) - parseFloat(cs.borderRightWidth || 0);
        if (sb > 0) backdrop.style.paddingRight = (parseFloat(cs.paddingRight || 0) + sb) + 'px';
    }

    function revisarTodos() { revisores.forEach(function (f) { f(); }); }

    function init(campo) {
        if (campo.dataset.spellInit) return;
        campo.dataset.spellInit = '1';
        // Overlay de subrayado solo en textarea (en inputs de 1 línea el texto va centrado
        // verticalmente y el overlay se desalinea → para el Título se usa el chip subrayado).
        var esTextarea = campo.tagName.toLowerCase() === 'textarea';

        var backdrop = null;
        var panel = document.createElement('div');
        panel.style.cssText = 'margin-top:4px; line-height:1.9;';

        if (esTextarea) {
            var wrap = document.createElement('div');
            wrap.style.position = 'relative';
            campo.parentNode.insertBefore(wrap, campo);

            backdrop = document.createElement('div');
            backdrop.className = 'spell-backdrop';
            backdrop.style.cssText = 'position:absolute; top:0; left:0; right:0; bottom:0; ' +
                'overflow:hidden; pointer-events:none; color:transparent; ' +
                'white-space:pre-wrap; word-wrap:break-word; margin:0;';
            wrap.appendChild(backdrop);
            wrap.appendChild(campo);
            campo.style.position = 'relative';
            campo.style.background = 'transparent';
            wrap.insertAdjacentElement('afterend', panel);
            campo.addEventListener('scroll', function () {
                backdrop.scrollTop = campo.scrollTop; backdrop.scrollLeft = campo.scrollLeft;
            });
        } else {
            campo.insertAdjacentElement('afterend', panel);
        }
        function sincronizarScroll() {
            if (backdrop) { backdrop.scrollTop = campo.scrollTop; backdrop.scrollLeft = campo.scrollLeft; }
        }

        function renderPanel(t, lista, obs) {
            panel.innerHTML = '';
            // Ortografía
            if (lista.length === 0) {
                panel.innerHTML = '<small style="color:#00a65a;"><i class="fa fa-check-circle"></i> Ortografía: sin observaciones</small> ';
            } else {
                var lbl = document.createElement('small');
                lbl.style.color = '#dd4b39';
                lbl.innerHTML = '<i class="fa fa-exclamation-triangle"></i> ' + lista.length + ' palabra(s) a revisar: ';
                panel.appendChild(lbl);
                lista.slice(0, 30).forEach(function (w) {
                    var chip = document.createElement('span');
                    chip.textContent = w;
                    chip.title = 'Clic para opciones';
                    chip.style.cssText = 'cursor:pointer; background:#fdecea; color:#c0392b; ' +
                        'border:1px solid #f5b7b1; border-radius:3px; padding:0 6px; margin:2px; ' +
                        'display:inline-block; font-size:12px; text-decoration:underline wavy #c0392b;';
                    chip.addEventListener('click', function () { popup(t, campo, w, chip); });
                    panel.appendChild(chip);
                });
                // "Sin Faltas": valida el documento marcando todas las observadas como correctas
                var ok = document.createElement('a');
                ok.href = '#';
                ok.innerHTML = '<i class="fa fa-check-circle"></i> Sin Faltas';
                ok.title = 'Aceptar todas estas palabras como correctas';
                ok.style.cssText = 'margin-left:8px; color:#00a65a; text-decoration:none; font-size:12px; white-space:nowrap;';
                ok.addEventListener('click', function (e) {
                    e.preventDefault();
                    lista.forEach(function (w) { ignoradas[w.toLowerCase()] = true; });
                    revisarTodos();
                });
                panel.appendChild(ok);
            }
            // Estilo / puntuación
            if (obs && obs.length > 0) {
                var div = document.createElement('div');
                div.style.cssText = 'margin-top:4px;';
                var t2 = document.createElement('small');
                t2.style.color = '#f39c12';
                t2.innerHTML = '<i class="fa fa-pencil"></i> Estilo/puntuación: ';
                div.appendChild(t2);
                obs.forEach(function (o) {
                    var chip = document.createElement('span');
                    chip.style.cssText = 'background:#fcf3cf; color:#9a7d0a; border:1px solid #f7dc6f; ' +
                        'border-radius:3px; padding:1px 6px; margin:2px; display:inline-block; font-size:12px;';
                    if (o.regla) {
                        chip.innerHTML = escHtml(o.regla.msg) + ' (' + o.cantidad + ') ';
                        var fix = document.createElement('a');
                        fix.href = '#'; fix.textContent = 'Corregir';
                        fix.style.cssText = 'margin-left:4px; color:#3c8dbc; text-decoration:underline;';
                        fix.addEventListener('click', function (e) {
                            e.preventDefault();
                            campo.value = campo.value.replace(o.regla.re, o.regla.fix);
                            campo.dispatchEvent(new Event('input', { bubbles: true }));
                        });
                        chip.appendChild(fix);
                    } else {
                        chip.textContent = o.aviso;
                    }
                    div.appendChild(chip);
                });
                panel.appendChild(div);
            }
        }

        var timer = null, ultimoTexto = null;
        function revisar() {
            cargarDiccionario(function (t) {
                ultimoTexto = campo.value;
                var texto = campo.value;
                if (!texto.trim()) { if (backdrop) backdrop.innerHTML = ''; panel.innerHTML = ''; return; }
                var d = detectar(t, texto);
                if (backdrop) {
                    sincronizarEstilo(campo, backdrop);
                    backdrop.innerHTML = pintarHtml(texto, d.set);
                    sincronizarScroll();
                }
                renderPanel(t, d.lista, analizarGramatica(texto));
            });
        }
        campo._spellRevisar = revisar;
        revisores.push(revisar);

        campo.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(revisar, 600); });
        setInterval(function () { if (campo.value !== ultimoTexto) { clearTimeout(timer); revisar(); } }, 800);
        if (campo.value && campo.value.trim()) setTimeout(revisar, 300);
    }

    // Popup de opciones por palabra: sugerencias + Omitir / Agregar / Cerrar
    function popup(t, campo, palabra, chip) {
        var existente = document.querySelector('.spell-pop');
        if (existente) existente.remove();

        var sug = (t.suggest(palabra) || []).slice(0, 5);
        var pop = document.createElement('div');
        pop.className = 'spell-pop';
        pop.style.cssText = 'display:block; background:#fff; border:1px solid #ccc; border-radius:4px; ' +
            'padding:6px 10px; margin:4px 0; box-shadow:0 1px 5px rgba(0,0,0,.2); font-size:12px;';

        var fila1 = document.createElement('div');
        if (sug.length === 0) {
            fila1.innerHTML = '<span class="text-muted">Sin sugerencias.</span>';
        } else {
            fila1.innerHTML = '<strong style="margin-right:4px;">Cambiar por:</strong>';
            sug.forEach(function (s) {
                var a = document.createElement('a');
                a.textContent = s; a.href = '#';
                a.style.cssText = 'margin:0 5px; color:#3c8dbc; text-decoration:underline;';
                a.addEventListener('click', function (e) { e.preventDefault(); pop.remove(); reemplazar(campo, palabra, s); });
                fila1.appendChild(a);
            });
        }
        pop.appendChild(fila1);

        var fila2 = document.createElement('div');
        fila2.style.cssText = 'margin-top:5px; border-top:1px solid #eee; padding-top:5px;';
        function boton(txt, icono, color, fn) {
            var b = document.createElement('a');
            b.href = '#'; b.innerHTML = '<i class="fa ' + icono + '"></i> ' + txt;
            b.style.cssText = 'margin-right:12px; color:' + color + '; text-decoration:none;';
            b.addEventListener('click', function (e) { e.preventDefault(); fn(); });
            fila2.appendChild(b);
        }
        boton('Omitir', 'fa-eye-slash', '#777', function () {
            ignoradas[palabra.toLowerCase()] = true; pop.remove(); revisarTodos();
        });
        boton('Agregar al diccionario', 'fa-plus-circle', '#00a65a', function () {
            custom[palabra.toLowerCase()] = true;
            try { localStorage.setItem('spell_custom', JSON.stringify(custom)); } catch (e) {}
            pop.remove(); revisarTodos();
        });
        boton('Cerrar', 'fa-times', '#dd4b39', function () { pop.remove(); });
        pop.appendChild(fila2);

        chip.insertAdjacentElement('afterend', pop);
    }

    window.activarCorrectorOrtografico = function (scope) {
        (scope || document).querySelectorAll(
            'input[name="titulo"], textarea[name="texto"], textarea[name="sintesis"]'
        ).forEach(init);
    };

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', function () { window.activarCorrectorOrtografico(); });
    else
        window.activarCorrectorOrtografico();
})();
