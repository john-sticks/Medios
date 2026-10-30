using Medios.Entities;

namespace Medios.Services;

public static class SintesisWorkflowPolicy
{
    public static bool PuedeFinalizar(SesionPrensa sesion) =>
        sesion.Estado == "Remitida" && sesion.Notas.Count > 0
        && sesion.Notas.All(n => n.VersionActual?.EstadoRevision is "Aprobada" or "Agregada" or "Finalizada"
            || EsDescartada(n.VersionActual));

    public static bool EsDescartada(NotaVersion? version) =>
        version != null && (version.EstadoRevision == "Descartada"
            || (version.EstadoRevision == "Sin Remitir"
                && (!string.IsNullOrWhiteSpace(version.MotivoDescarte) || version.TipoEvento == "Descarte")));

    public static bool TieneNotasConsolidables(SesionPrensa sesion) =>
        sesion.Notas.Any(n => PuedeConsolidarNota(sesion, n));

    public static bool PuedeConsolidarNota(SesionPrensa sesion, NotaPrensa nota)
    {
        if (sesion.Estado != "Finalizada") return false;
        var version = nota.VersionActual;
        if (version == null || !string.IsNullOrWhiteSpace(version.MotivoDescarte)) return false;
        if (version.EstadoRevision is "Aprobada" or "Finalizada" or "Agregada") return true;

        // Compatibilidad con finalizadas propias anteriores a la corrección de publicación.
        return sesion.Delegacion?.Nombre == "División Medios"
            && version.EstadoRevision is "Borrador" or "Remitida";
    }
}
