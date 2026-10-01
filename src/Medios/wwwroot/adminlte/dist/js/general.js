
function unescapeString(cadena) {
    return cadena.replace("$%$", "'").replace("$#$", "\"");
}

function irA(url) {
    window.location.href = url;
}

function slideIn(idTabla) {
    $("#" + idTabla).slideDown("slow");
}

function slideOut(idTabla) {
    $("#" + idTabla).slideUp("slow");
}

function pestanaDown(pestana) {
    slideOut('DivAlta');
    slideIn(pestana);
}

function pestanaUp(pestana) {
    slideOut(pestana);
    slideIn('DivAlta');
}

function getElement(name) {
    if (document.all) {
        return document.all(name);
    }
    return document.getElementById(name);
}

function mostrarError(mje, id) {
    getElement(id).innerHTML = mje;
}

function getAbsoluteElementPosition(element) {
    if (typeof element == "string") element = getElement(element)
    if (!element) return { top: 0, left: 0 };
    var y = 0; var x = 0;
    while (element.offsetParent) {
        x += element.offsetLeft;
        y += element.offsetTop;
        element = element.offsetParent;
    }
    return { top: y, left: x };
}

function posicionarSelectConIndice(idSelect, identificador) {
    var select = getElement(idSelect);
    var cant = select.length; var i = 0;
    while (i < cant) { if (select.options[i].value == identificador) { return i; } i++; }
}

function posicionarSelectConText(idSelect, identificador) {
    var select = getElement(idSelect);
    var cant = select.length; var i = 0;
    while (i < cant) {
        select.options[i].selected = (select.options[i].text == identificador);

        i++;
    }
}

function posicionarSelectConValue(idSelect, identificador) {
    var select = getElement(idSelect);
    var cant = select.length; var i = 0;
    while (i < cant) {
        select.options[i].selected = (select.options[i].value == identificador);
        i++;
    }
}

function selectFindIndexByText(idSelect, identificador) {
    var select = getElement(idSelect);
    var cant = select.length; var i = 0;
    while (i < cant) {
        if (select.options[i].text == identificador) break;
        i++;
    }
    return i;
}

function verificarSelect(campo, valor) {
    if (valor == -1) {
        return " - Por favor, elija un " + campo + ".<br />";
    }
    return "";
}

function verificarCheckList(campo, control) {
    if (isset(control)) {
        checklist = control.getElementsByTagName("input");
        valor = false;
        for (i = 0; i < checklist.length; i++) {
            valor = valor || checklist[i].checked;
        }
        if (!valor) {
            return " - Por favor, elija un " + campo + ".<br />";
        }
    }
    return "";
}

function verificarNombreUsuario(valor) {
    msj = "";
    msj += verificarNomUser("Nombre de Usuario", valor, true);
    if (valor.length < 6 || valor.length > 15) msj += " - El Nombre de Usuario debe contener entre 6 y 15 caracteres <br /> ";
    return msj;
}

function verificarPass(valor) {
    msj = "";
    msj += verificarNomUser("Contraseña", valor, true);
    if (valor.length < 6 || valor.length > 15) msj += " - La contraseña debe contener entre 6 y 15 caracteres <br /> ";
    return msj;
}

function validarSelect(valor) {
    if (valor == -1) {
        return false;
    }
    return true;
}

function verificarRangoEntero(campo, valor, minimo, maximo) {
    if (validarEntero(valor)) {
        valor = parseInt(valor);
        if (!((valor > minimo) && (valor < maximo))) {
            return " - El campo " + campo + " no esta dentro del rango permitido.\n";
        }
    }
    else {
        return " - El campo " + campo + " debe ser numerico.\n";
    }
    return "";
}

function verificarFecha(campo, valor, obligatorio) {
    if (obligatorio) {
        if (esVacio(valor)) {
            return " - El campo " + campo + " es obligatorio.<br />";
        }
    }

    if (valor) {
        borrar = valor;
        if ((valor.substr(2, 1) == "/") && (valor.substr(5, 1) == "/")) {
            for (i = 0; i < 10; i++) {
                if (((valor.substr(i, 1) < "0") || (valor.substr(i, 1) > "9")) && (i != 2) && (i != 5)) {
                    borrar = '';
                    break;
                }
            }
            if (borrar) {
                a = valor.substr(6, 4);
                m = valor.substr(3, 2);
                d = valor.substr(0, 2);
                if ((a < 1900) || (a > 2050) || (m < 1) || (m > 12) || (d < 1) || (d > 31))
                    borrar = '';
                else {
                    if ((a % 4 != 0) && (m == 2) && (d > 28))
                        borrar = ''; // Año no viciesto y es febrero y el dia es mayor a 28
                    else {
                        if ((((m == 4) || (m == 6) || (m == 9) || (m == 11)) && (d > 30)) || ((m == 2) && (d > 29)))
                            borrar = '';
                    }  // else
                } // fin else
            } // if (error)
        } // if ((valor.substr(2,1) == "/") && (valor.substr(5,1) == "/"))			    			
        else
            borrar = '';
        if (borrar == '')
            return " - El campo " + campo + " esta mal formado.<br />";
    } // if (valor)   
    return "";
}

function verificarHora(campo, valor, obligatorio) {
    if (obligatorio) {
        if (esVacio(valor)) {
            return " - El campo " + campo + " es obligatorio.\n";
        }
    }

    var expr = /^(0[1-9]|1\d|2[0-3]):([0-5]\d)$/
    if ((!expr.test(valor)) && (!esVacio(valor))) {
        return " - El campo " + campo + " esta mal formado.\n";
    }
    return "";
}

//////////////////////////////////////////////////////////////////////////////////////

function verificarCadena(campo, cadena, obligatorio) {
    if (obligatorio) {
        if (esVacio(cadena)) {
            return " - El campo " + campo + " es obligatorio.<br />";
        }
    }
    return "";
}

function verificarEntero(campo, valor, obligatorio) {
    if (obligatorio) {
        if (esVacio(valor)) {
            return " - El campo " + campo + " es obligatorio.<br />";
        }
    }

    var expr = /^(?:\+|-)?\d+$/
    if ((!expr.test(valor)) && (!esVacio(valor))) {
        return " - El campo " + campo + " esta mal formado.<br />";
    }
    return "";
}

/*acepta entero o reales separados por '.' */
function verificarFlotante(campo, valor, obligatorio) {
    if (obligatorio) {
        if (esVacio(valor)) {
            return " - El campo " + campo + " es obligatorio.\n";
        }
    }
    var expr = /(^(?:\+|-)?\d+\.\d*$|^(?:\+|-)?\d+$)/
    if ((!expr.test(valor)) && (!esVacio(valor))) {
        return " - El campo " + campo + " debe ser un numero real.\n";
    }
    return "";
}

function verificarImagen(campo, cadena, obligatorio) {
    if (obligatorio) {
        if (esVacio(cadena)) {
            return " - El campo " + campo + " es obligatorio.<br />";
        }
    }
    if ((!validarImagen(cadena)) && (!esVacio(cadena))) {
        return " - El campo " + campo + " esta mal formado.<br />";
    }
    return "";
}

function GetFileExtension(Filename) {
    var I = Filename.lastIndexOf(".");
    return (I > -1) ? Filename.substring(I + 1, Filename.length).toLowerCase() : "";

}
function validarImagen(Filename) {
    Ext = GetFileExtension(Filename);
    if (Ext != "jpeg" && Ext != "jpg" && Ext != "png" && Ext != "gif") { return false; }
    return true;
}

function verificarMail(campo, valor, obligatorio) {
    if (obligatorio) {
        if (esVacio(valor)) {
            return " - El campo " + campo + " es obligatorio.\n";
        }
    }

    var expr = /\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*/;
    if ((!expr.test(valor)) && (!esVacio(valor))) {
        return " - El campo " + campo + " esta mal formado.\n";
    }
    return "";
}

function verificarTelefono(campo, valor, obligatorio) {
    if (obligatorio) {
        if (esVacio(valor)) {
            return " - El campo " + campo + " es obligatorio.<br />";
        }
    }
    //var expr = /^(\()?(\d{1,5})(\)|-)?(\d{3,4})(-)?(\d{4}|\d{4})$/
    var expr = /^[\d|\-|(|)| |*]*$/
    if ((!expr.test(valor)) && (!esVacio(valor))) {
        return " - El campo " + campo + " esta mal formado.<br />";
    }
    return "";
}

function verificarUrl(campo, valor, obligatorio) {
    if (obligatorio) {
        if (esVacio(valor)) {
            return " - El campo " + campo + " es obligatorio.<br />";
        }
    }

    var expr = /http(s)?:\/\/([\w-]+\.)+[\w-]+(\/[\w- ./?%&=]*)?/;
    if ((!expr.test(valor)) && (!esVacio(valor))) {
        return " - El campo " + campo + " esta mal formado.<br />";
    }
    return "";
}

function verificarApeyNom(campo, cadena, obligatorio) {
    if (obligatorio) {
        if (esVacio(cadena)) {
            return " - El campo " + campo + " es obligatorio.\n";
        }
    }
    if (!validarApeyNom(cadena)) {

        return " - El campo " + campo + " no permite numeros ni caracteres especiales.\n";
    }

    return "";
}

function verificarNomUser(campo, cadena, obligatorio) {
    if (obligatorio) {
        if (esVacio(cadena)) {
            return " - El campo " + campo + " es obligatorio.<br />";
        }
    }
    if (!validarNomUser(cadena)) {

        return " - El campo " + campo + " no permite espacios ni caracteres especiales.<br />";
    }

    return "";
}

function verificarAlfanumerico(campo, cadena, obligatorio) {
    if (obligatorio) {
        if (esVacio(cadena)) {
            return " - El campo " + campo + " es obligatorio.<br />";
        }
    }
    if (!validarAlfanumerico(cadena)) {

        return " - El campo " + campo + " esta mal formado.<br />";
    }

    return "";
}

function validarNomUser(valor) {
    var alfanum = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789Ññ";
    for (i = 0; i < valor.length; i++) {
        if (alfanum.indexOf(valor.charAt(i)) == -1) {
            return false;
        }
    }
    return true;
}

function validarApeyNom(valor) {
    var alfa = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz ";
    alfa += String.fromCharCode(193, 201, 205, 209, 211, 218, 225, 233, 237, 241, 243, 250);
    for (i = 0; i < valor.length; i++) {
        if (alfa.indexOf(valor.charAt(i)) == -1) {
            return false;
        }
    }
    return true;
}

function validarAlfanumerico(valor) {
    var alfa = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.;,()- ";
    alfa += String.fromCharCode(193, 201, 205, 209, 211, 218, 225, 233, 237, 241, 243, 250);
    for (i = 0; i < valor.length; i++) {
        if (alfa.indexOf(valor.charAt(i)) == -1) {
            return false;
        }
    }
    return true;
}

function validarEntero(valor) {
    var valido = true;
    for (i = 0; i < valor.length; i++) {
        if (!(((valor.charAt(i) >= "0") && (valor.charAt(i) <= "9")))) {
            valido = false;
            break;
        }
    }
    return valido;
}

function validarFlotante(valor) {
    var valido = true;
    //permito numeros, caracter '.'
    for (i = 0; i < valor.length; i++) {
        if (!(((valor.charAt(i) >= "0") && (valor.charAt(i) <= "9")) || (valor.charAt(i) == "."))) {
            valido = false;
            break;
        }
    }
    return valido;
}

function esVacio(cadena) {
    var valido = false;
    var mistr;
    mistr = "";
    for (i = 0; i < cadena.length; i++) {
        if (cadena.charAt(i) != " ") {
            mistr = mistr + cadena.charAt(i);
        }
    }

    if (cadena.length == 0 || mistr.length == 0) {
        valido = true;
    }
    return valido;
}

function isset(variable_name) {
    try {
        if (typeof(eval(variable_name)) != 'undefined') {
            if (eval(variable_name) != null) {
                return true;
            }
        }
    } catch (e) { }
    return false;
}

function unescape(texto) {
    texto = texto.split("$#$").join("\"");
    texto = texto.split("$%$").join("'");
    return texto;
}

function clickButton(e, buttonid) {
    var evt = e ? e : window.event;
    var bt = document.getElementById(buttonid);

    if (bt) {
        if (evt.keyCode == 13) {
            bt.click();
            return false;
        }
    }
}

function focusInput(e, inputid) {
    var evt = e ? e : window.event;
    var bt = document.getElementById(inputid);

    if (bt) {
        if (evt.keyCode == 13) {
            bt.focus();
            return false;
        }
    }
}

function focusSelect2(e, inputid) {
    var evt = e ? e : window.event;
    var bt = document.getElementById(inputid.replace("#", ""));

    if (bt) {
        if (evt.keyCode == 13) {
            $(inputid).select2("focus2");
            return false;
        }
    }
}

function HtmlDecode(s) {
    var out = "";
    if (s == null) return;

    var l = s.length;
    for (var i = 0; i < l; i++) {
        var ch = s.charAt(i);

        if (ch == '&') {
            var semicolonIndex = s.indexOf(';', i + 1);

            if (semicolonIndex > 0) {
                var entity = s.substring(i + 1, semicolonIndex);
                if (entity.length > 1 && entity.charAt(0) == '#') {
                    if (entity.charAt(1) == 'x' || entity.charAt(1) == 'X')
                        ch = String.fromCharCode(eval('0' + entity.substring(1)));
                    else
                        ch = String.fromCharCode(eval(entity.substring(1)));
                }
                else {
                    switch (entity) {
                        case 'quot': ch = String.fromCharCode(0x0022); break;
                        case 'amp': ch = String.fromCharCode(0x0026); break;
                        case 'lt': ch = String.fromCharCode(0x003c); break;
                        case 'gt': ch = String.fromCharCode(0x003e); break;
                        case 'nbsp': ch = String.fromCharCode(0x00a0); break;
                        case 'iexcl': ch = String.fromCharCode(0x00a1); break;
                        case 'cent': ch = String.fromCharCode(0x00a2); break;
                        case 'pound': ch = String.fromCharCode(0x00a3); break;
                        case 'curren': ch = String.fromCharCode(0x00a4); break;
                        case 'yen': ch = String.fromCharCode(0x00a5); break;
                        case 'brvbar': ch = String.fromCharCode(0x00a6); break;
                        case 'sect': ch = String.fromCharCode(0x00a7); break;
                        case 'uml': ch = String.fromCharCode(0x00a8); break;
                        case 'copy': ch = String.fromCharCode(0x00a9); break;
                        case 'ordf': ch = String.fromCharCode(0x00aa); break;
                        case 'laquo': ch = String.fromCharCode(0x00ab); break;
                        case 'not': ch = String.fromCharCode(0x00ac); break;
                        case 'shy': ch = String.fromCharCode(0x00ad); break;
                        case 'reg': ch = String.fromCharCode(0x00ae); break;
                        case 'macr': ch = String.fromCharCode(0x00af); break;
                        case 'deg': ch = String.fromCharCode(0x00b0); break;
                        case 'plusmn': ch = String.fromCharCode(0x00b1); break;
                        case 'sup2': ch = String.fromCharCode(0x00b2); break;
                        case 'sup3': ch = String.fromCharCode(0x00b3); break;
                        case 'acute': ch = String.fromCharCode(0x00b4); break;
                        case 'micro': ch = String.fromCharCode(0x00b5); break;
                        case 'para': ch = String.fromCharCode(0x00b6); break;
                        case 'middot': ch = String.fromCharCode(0x00b7); break;
                        case 'cedil': ch = String.fromCharCode(0x00b8); break;
                        case 'sup1': ch = String.fromCharCode(0x00b9); break;
                        case 'ordm': ch = String.fromCharCode(0x00ba); break;
                        case 'raquo': ch = String.fromCharCode(0x00bb); break;
                        case 'frac14': ch = String.fromCharCode(0x00bc); break;
                        case 'frac12': ch = String.fromCharCode(0x00bd); break;
                        case 'frac34': ch = String.fromCharCode(0x00be); break;
                        case 'iquest': ch = String.fromCharCode(0x00bf); break;
                        case 'Agrave': ch = String.fromCharCode(0x00c0); break;
                        case 'Aacute': ch = String.fromCharCode(0x00c1); break;
                        case 'Acirc': ch = String.fromCharCode(0x00c2); break;
                        case 'Atilde': ch = String.fromCharCode(0x00c3); break;
                        case 'Auml': ch = String.fromCharCode(0x00c4); break;
                        case 'Aring': ch = String.fromCharCode(0x00c5); break;
                        case 'AElig': ch = String.fromCharCode(0x00c6); break;
                        case 'Ccedil': ch = String.fromCharCode(0x00c7); break;
                        case 'Egrave': ch = String.fromCharCode(0x00c8); break;
                        case 'Eacute': ch = String.fromCharCode(0x00c9); break;
                        case 'Ecirc': ch = String.fromCharCode(0x00ca); break;
                        case 'Euml': ch = String.fromCharCode(0x00cb); break;
                        case 'Igrave': ch = String.fromCharCode(0x00cc); break;
                        case 'Iacute': ch = String.fromCharCode(0x00cd); break;
                        case 'Icirc': ch = String.fromCharCode(0x00ce); break;
                        case 'Iuml': ch = String.fromCharCode(0x00cf); break;
                        case 'ETH': ch = String.fromCharCode(0x00d0); break;
                        case 'Ntilde': ch = String.fromCharCode(0x00d1); break;
                        case 'Ograve': ch = String.fromCharCode(0x00d2); break;
                        case 'Oacute': ch = String.fromCharCode(0x00d3); break;
                        case 'Ocirc': ch = String.fromCharCode(0x00d4); break;
                        case 'Otilde': ch = String.fromCharCode(0x00d5); break;
                        case 'Ouml': ch = String.fromCharCode(0x00d6); break;
                        case 'times': ch = String.fromCharCode(0x00d7); break;
                        case 'Oslash': ch = String.fromCharCode(0x00d8); break;
                        case 'Ugrave': ch = String.fromCharCode(0x00d9); break;
                        case 'Uacute': ch = String.fromCharCode(0x00da); break;
                        case 'Ucirc': ch = String.fromCharCode(0x00db); break;
                        case 'Uuml': ch = String.fromCharCode(0x00dc); break;
                        case 'Yacute': ch = String.fromCharCode(0x00dd); break;
                        case 'THORN': ch = String.fromCharCode(0x00de); break;
                        case 'szlig': ch = String.fromCharCode(0x00df); break;
                        case 'agrave': ch = String.fromCharCode(0x00e0); break;
                        case 'aacute': ch = String.fromCharCode(0x00e1); break;
                        case 'acirc': ch = String.fromCharCode(0x00e2); break;
                        case 'atilde': ch = String.fromCharCode(0x00e3); break;
                        case 'auml': ch = String.fromCharCode(0x00e4); break;
                        case 'aring': ch = String.fromCharCode(0x00e5); break;
                        case 'aelig': ch = String.fromCharCode(0x00e6); break;
                        case 'ccedil': ch = String.fromCharCode(0x00e7); break;
                        case 'egrave': ch = String.fromCharCode(0x00e8); break;
                        case 'eacute': ch = String.fromCharCode(0x00e9); break;
                        case 'ecirc': ch = String.fromCharCode(0x00ea); break;
                        case 'euml': ch = String.fromCharCode(0x00eb); break;
                        case 'igrave': ch = String.fromCharCode(0x00ec); break;
                        case 'iacute': ch = String.fromCharCode(0x00ed); break;
                        case 'icirc': ch = String.fromCharCode(0x00ee); break;
                        case 'iuml': ch = String.fromCharCode(0x00ef); break;
                        case 'eth': ch = String.fromCharCode(0x00f0); break;
                        case 'ntilde': ch = String.fromCharCode(0x00f1); break;
                        case 'ograve': ch = String.fromCharCode(0x00f2); break;
                        case 'oacute': ch = String.fromCharCode(0x00f3); break;
                        case 'ocirc': ch = String.fromCharCode(0x00f4); break;
                        case 'otilde': ch = String.fromCharCode(0x00f5); break;
                        case 'ouml': ch = String.fromCharCode(0x00f6); break;
                        case 'divide': ch = String.fromCharCode(0x00f7); break;
                        case 'oslash': ch = String.fromCharCode(0x00f8); break;
                        case 'ugrave': ch = String.fromCharCode(0x00f9); break;
                        case 'uacute': ch = String.fromCharCode(0x00fa); break;
                        case 'ucirc': ch = String.fromCharCode(0x00fb); break;
                        case 'uuml': ch = String.fromCharCode(0x00fc); break;
                        case 'yacute': ch = String.fromCharCode(0x00fd); break;
                        case 'thorn': ch = String.fromCharCode(0x00fe); break;
                        case 'yuml': ch = String.fromCharCode(0x00ff); break;
                        case 'OElig': ch = String.fromCharCode(0x0152); break;
                        case 'oelig': ch = String.fromCharCode(0x0153); break;
                        case 'Scaron': ch = String.fromCharCode(0x0160); break;
                        case 'scaron': ch = String.fromCharCode(0x0161); break;
                        case 'Yuml': ch = String.fromCharCode(0x0178); break;
                        case 'fnof': ch = String.fromCharCode(0x0192); break;
                        case 'circ': ch = String.fromCharCode(0x02c6); break;
                        case 'tilde': ch = String.fromCharCode(0x02dc); break;
                        case 'Alpha': ch = String.fromCharCode(0x0391); break;
                        case 'Beta': ch = String.fromCharCode(0x0392); break;
                        case 'Gamma': ch = String.fromCharCode(0x0393); break;
                        case 'Delta': ch = String.fromCharCode(0x0394); break;
                        case 'Epsilon': ch = String.fromCharCode(0x0395); break;
                        case 'Zeta': ch = String.fromCharCode(0x0396); break;
                        case 'Eta': ch = String.fromCharCode(0x0397); break;
                        case 'Theta': ch = String.fromCharCode(0x0398); break;
                        case 'Iota': ch = String.fromCharCode(0x0399); break;
                        case 'Kappa': ch = String.fromCharCode(0x039a); break;
                        case 'Lambda': ch = String.fromCharCode(0x039b); break;
                        case 'Mu': ch = String.fromCharCode(0x039c); break;
                        case 'Nu': ch = String.fromCharCode(0x039d); break;
                        case 'Xi': ch = String.fromCharCode(0x039e); break;
                        case 'Omicron': ch = String.fromCharCode(0x039f); break;
                        case 'Pi': ch = String.fromCharCode(0x03a0); break;
                        case ' Rho ': ch = String.fromCharCode(0x03a1); break;
                        case 'Sigma': ch = String.fromCharCode(0x03a3); break;
                        case 'Tau': ch = String.fromCharCode(0x03a4); break;
                        case 'Upsilon': ch = String.fromCharCode(0x03a5); break;
                        case 'Phi': ch = String.fromCharCode(0x03a6); break;
                        case 'Chi': ch = String.fromCharCode(0x03a7); break;
                        case 'Psi': ch = String.fromCharCode(0x03a8); break;
                        case 'Omega': ch = String.fromCharCode(0x03a9); break;
                        case 'alpha': ch = String.fromCharCode(0x03b1); break;
                        case 'beta': ch = String.fromCharCode(0x03b2); break;
                        case 'gamma': ch = String.fromCharCode(0x03b3); break;
                        case 'delta': ch = String.fromCharCode(0x03b4); break;
                        case 'epsilon': ch = String.fromCharCode(0x03b5); break;
                        case 'zeta': ch = String.fromCharCode(0x03b6); break;
                        case 'eta': ch = String.fromCharCode(0x03b7); break;
                        case 'theta': ch = String.fromCharCode(0x03b8); break;
                        case 'iota': ch = String.fromCharCode(0x03b9); break;
                        case 'kappa': ch = String.fromCharCode(0x03ba); break;
                        case 'lambda': ch = String.fromCharCode(0x03bb); break;
                        case 'mu': ch = String.fromCharCode(0x03bc); break;
                        case 'nu': ch = String.fromCharCode(0x03bd); break;
                        case 'xi': ch = String.fromCharCode(0x03be); break;
                        case 'omicron': ch = String.fromCharCode(0x03bf); break;
                        case 'pi': ch = String.fromCharCode(0x03c0); break;
                        case 'rho': ch = String.fromCharCode(0x03c1); break;
                        case 'sigmaf': ch = String.fromCharCode(0x03c2); break;
                        case 'sigma': ch = String.fromCharCode(0x03c3); break;
                        case 'tau': ch = String.fromCharCode(0x03c4); break;
                        case 'upsilon': ch = String.fromCharCode(0x03c5); break;
                        case 'phi': ch = String.fromCharCode(0x03c6); break;
                        case 'chi': ch = String.fromCharCode(0x03c7); break;
                        case 'psi': ch = String.fromCharCode(0x03c8); break;
                        case 'omega': ch = String.fromCharCode(0x03c9); break;
                        case 'thetasym': ch = String.fromCharCode(0x03d1); break;
                        case 'upsih': ch = String.fromCharCode(0x03d2); break;
                        case 'piv': ch = String.fromCharCode(0x03d6); break;
                        case 'ensp': ch = String.fromCharCode(0x2002); break;
                        case 'emsp': ch = String.fromCharCode(0x2003); break;
                        case 'thinsp': ch = String.fromCharCode(0x2009); break;
                        case 'zwnj': ch = String.fromCharCode(0x200c); break;
                        case 'zwj': ch = String.fromCharCode(0x200d); break;
                        case 'lrm': ch = String.fromCharCode(0x200e); break;
                        case 'rlm': ch = String.fromCharCode(0x200f); break;
                        case 'ndash': ch = String.fromCharCode(0x2013); break;
                        case 'mdash': ch = String.fromCharCode(0x2014); break;
                        case 'lsquo': ch = String.fromCharCode(0x2018); break;
                        case 'rsquo': ch = String.fromCharCode(0x2019); break;
                        case 'sbquo': ch = String.fromCharCode(0x201a); break;
                        case 'ldquo': ch = String.fromCharCode(0x201c); break;
                        case 'rdquo': ch = String.fromCharCode(0x201d); break;
                        case 'bdquo': ch = String.fromCharCode(0x201e); break;
                        case 'dagger': ch = String.fromCharCode(0x2020); break;
                        case 'Dagger': ch = String.fromCharCode(0x2021); break;
                        case 'bull': ch = String.fromCharCode(0x2022); break;
                        case 'hellip': ch = String.fromCharCode(0x2026); break;
                        case 'permil': ch = String.fromCharCode(0x2030); break;
                        case 'prime': ch = String.fromCharCode(0x2032); break;
                        case 'Prime': ch = String.fromCharCode(0x2033); break;
                        case 'lsaquo': ch = String.fromCharCode(0x2039); break;
                        case 'rsaquo': ch = String.fromCharCode(0x203a); break;
                        case 'oline': ch = String.fromCharCode(0x203e); break;
                        case 'frasl': ch = String.fromCharCode(0x2044); break;
                        case 'euro': ch = String.fromCharCode(0x20ac); break;
                        case 'image': ch = String.fromCharCode(0x2111); break;
                        case 'weierp': ch = String.fromCharCode(0x2118); break;
                        case 'real': ch = String.fromCharCode(0x211c); break;
                        case 'trade': ch = String.fromCharCode(0x2122); break;
                        case 'alefsym': ch = String.fromCharCode(0x2135); break;
                        case 'larr': ch = String.fromCharCode(0x2190); break;
                        case 'uarr': ch = String.fromCharCode(0x2191); break;
                        case 'rarr': ch = String.fromCharCode(0x2192); break;
                        case 'darr': ch = String.fromCharCode(0x2193); break;
                        case 'harr': ch = String.fromCharCode(0x2194); break;
                        case 'crarr': ch = String.fromCharCode(0x21b5); break;
                        case 'lArr': ch = String.fromCharCode(0x21d0); break;
                        case 'uArr': ch = String.fromCharCode(0x21d1); break;
                        case 'rArr': ch = String.fromCharCode(0x21d2); break;
                        case 'dArr': ch = String.fromCharCode(0x21d3); break;
                        case 'hArr': ch = String.fromCharCode(0x21d4); break;
                        case 'forall': ch = String.fromCharCode(0x2200); break;
                        case 'part': ch = String.fromCharCode(0x2202); break;
                        case 'exist': ch = String.fromCharCode(0x2203); break;
                        case 'empty': ch = String.fromCharCode(0x2205); break;
                        case 'nabla': ch = String.fromCharCode(0x2207); break;
                        case 'isin': ch = String.fromCharCode(0x2208); break;
                        case 'notin': ch = String.fromCharCode(0x2209); break;
                        case 'ni': ch = String.fromCharCode(0x220b); break;
                        case 'prod': ch = String.fromCharCode(0x220f); break;
                        case 'sum': ch = String.fromCharCode(0x2211); break;
                        case 'minus': ch = String.fromCharCode(0x2212); break;
                        case 'lowast': ch = String.fromCharCode(0x2217); break;
                        case 'radic': ch = String.fromCharCode(0x221a); break;
                        case 'prop': ch = String.fromCharCode(0x221d); break;
                        case 'infin': ch = String.fromCharCode(0x221e); break;
                        case 'ang': ch = String.fromCharCode(0x2220); break;
                        case 'and': ch = String.fromCharCode(0x2227); break;
                        case 'or': ch = String.fromCharCode(0x2228); break;
                        case 'cap': ch = String.fromCharCode(0x2229); break;
                        case 'cup': ch = String.fromCharCode(0x222a); break;
                        case 'int': ch = String.fromCharCode(0x222b); break;
                        case 'there4': ch = String.fromCharCode(0x2234); break;
                        case 'sim': ch = String.fromCharCode(0x223c); break;
                        case 'cong': ch = String.fromCharCode(0x2245); break;
                        case 'asymp': ch = String.fromCharCode(0x2248); break;
                        case 'ne': ch = String.fromCharCode(0x2260); break;
                        case 'equiv': ch = String.fromCharCode(0x2261); break;
                        case 'le': ch = String.fromCharCode(0x2264); break;
                        case 'ge': ch = String.fromCharCode(0x2265); break;
                        case 'sub': ch = String.fromCharCode(0x2282); break;
                        case 'sup': ch = String.fromCharCode(0x2283); break;
                        case 'nsub': ch = String.fromCharCode(0x2284); break;
                        case 'sube': ch = String.fromCharCode(0x2286); break;
                        case 'supe': ch = String.fromCharCode(0x2287); break;
                        case 'oplus': ch = String.fromCharCode(0x2295); break;
                        case 'otimes': ch = String.fromCharCode(0x2297); break;
                        case 'perp': ch = String.fromCharCode(0x22a5); break;
                        case 'sdot': ch = String.fromCharCode(0x22c5); break;
                        case 'lceil': ch = String.fromCharCode(0x2308); break;
                        case 'rceil': ch = String.fromCharCode(0x2309); break;
                        case 'lfloor': ch = String.fromCharCode(0x230a); break;
                        case 'rfloor': ch = String.fromCharCode(0x230b); break;
                        case 'lang': ch = String.fromCharCode(0x2329); break;
                        case 'rang': ch = String.fromCharCode(0x232a); break;
                        case 'loz': ch = String.fromCharCode(0x25ca); break;
                        case 'spades': ch = String.fromCharCode(0x2660); break;
                        case 'clubs': ch = String.fromCharCode(0x2663); break;
                        case 'hearts': ch = String.fromCharCode(0x2665); break;
                        case 'diams': ch = String.fromCharCode(0x2666); break;
                        default: ch = ''; break;
                    }
                }
                i = semicolonIndex;
            }
        }

        out += ch;
    }

    return out;

}

function descargar(uri) {
    descargar(null, uri);
}

function descargar(args, uri) {
    if (args != null && args.length != 0) {
        var iframe = document.createElement("iframe");
        var query = "";
        if (args.indexOf("|") != -1) {
            var arguments = args.split("|");
            for (var i = 0; i < arguments.length; i++) {
                query += "arg" + i + "=" + arguments[i];
                if (i + 1 < arguments.length) {
                    query += "&";
                }
            }
            iframe.src = uri + "?" + query;
        }
        else {
            iframe.src = uri;
        }
        iframe.style.display = "none";
        document.body.appendChild(iframe);
    }
}

function descargar2(src) {
    $("body").append('<iframe id="idiframe" src="' + src + '" scrolling="no" frameborder="0" style="border:6px solid #a9a9a9;background-color:#ffffff;display:none;">');
}

var fileDownloadCheckTimer;
function blockForDownload() {
    showBlockDwld();
    fileDownloadCheckTimer = window.setInterval(function () {
        var cookieValue = $.cookie('fileDownloadToken');
        if (cookieValue === undefined)
            setTimeout(function () { finishDownload(); }, 5000);
    }, 1000);
}

function finishDownload() {
    window.clearInterval(fileDownloadCheckTimer);
    $.removeCookie('fileDownloadToken');
    $("#TBlock").modal('hide');
}

function showBlockDwld() {
    $.cookie('fileDownloadToken', guid());
    $('#TBlock').modal({
        backdrop: 'static',
        keyboard: false
    })
}

function formatDate(date) {
    //var date = eval("new " + sdate.slice(1, -1));
    var d = date.getDate();
    var m = date.getMonth() + 1;
    var a = date.getFullYear();
    if (d.toString().length == 1)
        salida = '0' + d.toString() + "/";
    else
        salida = d.toString() + "/";
    if (m.toString().length == 1)
        salida += '0' + m.toString() + "/";
    else
        salida += m.toString() + "/";
    return salida + a.toString();
}
function formatDateTime(date) {
    //var date = eval("new " + sdate.slice(1, -1));
    var salida = formatDate(date) + " ";
    var h = date.getHours();
    var m = date.getMinutes();
    var s = date.getSeconds();
    if (h.toString().length == 1)
        salida += '0' + h.toString() + ":";
    else
        salida += h.toString() + ":";
    if (m.toString().length == 1)
        salida += '0' + m.toString() + ":";
    else
        salida += m.toString() + ":";
    if (s.toString().length == 1)
        return salida + '0' + s.toString();
    else
        return salida + s.toString();
}
function formatTime(date) {
    //var date = eval("new " + sdate.slice(1, -1));
    var salida = "";
    var h = date.getHours();
    var m = date.getMinutes();
    var s = date.getSeconds();
    if (h.toString().length == 1)
        salida = '0' + h.toString() + ":";
    else
        salida = h.toString() + ":";
    if (m.toString().length == 1)
        salida += '0' + m.toString() + ":";
    else
        salida += m.toString() + ":";
    if (s.toString().length == 1)
        return salida + '0' + s.toString();
    else
        return salida + s.toString();
}

function PadLeft(cad, pad) {
    var str = "" + cad
    return pad.substring(0, pad.length - str.length) + str
}

function OpenWindow(uri, width, height) {
    var myWindow = window.open(uri, "MsgWindow", "width=" + width + ", height=" + height + ", resizable=yes,scrollbars=yes");
    myWindow.focus();
}

function dateDiff(date1, date2) {

    var one_day = 1000 * 60 * 60 * 24;

    // Convert both dates to milliseconds
    var date1_ms = date1.getTime();
    var date2_ms = date2.getTime();

    // Calculate the difference in milliseconds
    var difference_ms = date2_ms - date1_ms;

    // Convert back to days and return
    return Math.round(difference_ms / one_day);
}

function parseParams(queryString) {
    var parametros = {}, queries, temp, i, l;

    // Split into key/value pairs
    queries = queryString.split("&");

    // Convert the array of strings into an object
    for (i = 0, l = queries.length; i < l; i++) {
        temp = queries[i].split('=');
        parametros[temp[0]] = temp[1];
    }
    return "{parametros:" + JSON.stringify(parametros) + "}";
}

function guid() {
    return s4() + s4() + '-' + s4() + '-' + s4() + '-' +
      s4() + '-' + s4() + s4() + s4();
}

function s4() {
    return Math.floor((1 + Math.random()) * 0x10000)
      .toString(16)
      .substring(1);
}

function notificar(tipo, titulo, mensaje) {
    toastr.options = {
        "closeButton": true,
        "debug": false,
        "progressBar": false,
        "preventDuplicates": false,
        "positionClass": "toast-top-right",
        "onclick": null,
        "showDuration": "400",
        "hideDuration": "1000",
        "timeOut": "7000",
        "extendedTimeOut": "1000",
        "showEasing": "swing",
        "hideEasing": "linear",
        "showMethod": "fadeIn",
        "hideMethod": "fadeOut"
    }
    var ntipo = "";
    switch (tipo) {
        case 1: ntipo = "info"; break;
        case 2: ntipo = "success"; break;
        case 3: ntipo = "warning"; break;
        case 4: ntipo = "error"; break;
        default: ntipo = "info";
    }
    toastr[ntipo](mensaje, titulo);

}

function getCookie(cname) {
    var name = cname + "=";
    var decodedCookie = decodeURIComponent(document.cookie);
    var ca = decodedCookie.split(';');
    for (var i = 0; i < ca.length; i++) {
        var c = ca[i];
        while (c.charAt(0) == ' ') {
            c = c.substring(1);
        }
        if (c.indexOf(name) == 0) {
            return c.substring(name.length, c.length);
        }
    }
    return "";
}
function checkCookieDW() {
    var username = getCookie("fileDownloadToken");
    if (username != "") {
        wasSubmitted = false;
        $("#TBlock").modal('hide');
    }

}