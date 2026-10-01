var wasSubmitted = false;
var prm = Sys.WebForms.PageRequestManager.getInstance();
prm.add_initializeRequest(InitializeRequest);
prm.add_endRequest(EndRequest);
var postBackElement;
var intervalCheckCookieDW;
var isDownloading = false; // Nueva variable para rastrear la descarga

function InitializeRequest(sender, args) {
    if (!isDownloading) { // Mostrar el modal solo si no estamos descargando
        $('#TBlock').modal({
            backdrop: 'static',
            keyboard: false
        });
        intervalCheckCookieDW = setInterval(checkCookieDW, 1000);
    }
}

function EndRequest(sender, args) {
    wasSubmitted = false;
    if (!isDownloading) { // Ocultar el modal solo si no estamos descargando
        $("#TBlock").modal('hide');
    }
    if (args.get_error() != undefined) {
        var errorMessage;
        if (args.get_response().get_statusCode() == '200') {
            errorMessage = args.get_error().message;
        } else {
            errorMessage = 'Error: Error no especificado. ';
        }
        args.set_errorHandled(true);
        var n = errorMessage.split(":");
        var msjFinal = "";
        for (var i = 1; i < n.length; i++) {
            msjFinal = msjFinal.concat(n[i]);
        }
        $("#textoError").html(msjFinal.replace(/\$\$/g, "<br/ >"));
        $('#TError').modal();
    }
    clearInterval(intervalCheckCookieDW);
    isDownloading = false; // Resetear la bandera de descarga al finalizar la petición
}