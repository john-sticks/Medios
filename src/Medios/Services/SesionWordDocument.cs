using Medios.Entities;

namespace Medios.Services;

public static class SesionWordDocument
{
    // Exportación de consulta: no crea una consolidada, no requiere PDF ni cambia estados.
    public static byte[] Crear(SesionPrensa sesion, string webRoot)
    {
        if (sesion.Estado != "Finalizada")
            throw new InvalidOperationException("Solo se pueden exportar a Word las síntesis finalizadas.");

        var notas = sesion.Notas.Where(n => SintesisWorkflowPolicy.PuedeConsolidarNota(sesion, n))
            .OrderBy(n => n.Id).ToList();
        if (notas.Count == 0)
            throw new InvalidOperationException("La síntesis no tiene notas aptas para exportar.");

        var documento = new Sintesis
        {
            Fecha = sesion.Fecha,
            Tipo = sesion.Turno,
            DelegacionId = sesion.DelegacionId,
            Delegacion = sesion.Delegacion,
            OperadorGenera = sesion.UsuarioCarga
        };
        var incluidas = notas.Select((nota, orden) => new SintesisNota
        {
            NotaPrensaId = nota.Id,
            Nota = nota,
            NotaVersionId = nota.VersionActualId!.Value,
            NotaVersion = nota.VersionActual!,
            Orden = orden
        }).ToList();
        return SintesisWordDocument.Crear(documento, incluidas, webRoot);
    }
}
