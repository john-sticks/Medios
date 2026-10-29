using Medios.Entities;

namespace Medios.Services;

public static class SintesisContenido
{
    public static List<SintesisNota> NotasVisibles(Sintesis sintesis) => sintesis.NotasIncluidas
        .Where(sn => sn.NotaVersion != null && !SintesisWorkflowPolicy.EsDescartada(sn.NotaVersion)
            && (sintesis.EsFiltrada || !SintesisWorkflowPolicy.EsDescartada(sn.Nota?.VersionActual)))
        .OrderBy(sn => sn.Orden).ToList();
}
