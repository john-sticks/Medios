using Medios.Entities;

namespace Medios.Services;

public static class SintesisWorkflowPolicy
{
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
