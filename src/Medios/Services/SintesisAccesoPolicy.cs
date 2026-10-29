using System.Linq.Expressions;
using Medios.Entities;

namespace Medios.Services;

public static class SintesisAccesoPolicy
{
    // Los destinos pertenecen a la delegación; el nombre del operador no otorga acceso.
    public static Expression<Func<Sintesis, bool>> ParaDelegacion(int? id) => s => id.HasValue
        && (s.DelegacionId == id.Value || (s.DelegacionId == null && s.Estado == "Informada"
            && (s.CanalTodosRoles || s.CanalTodasDelegaciones || s.DelegacionesDestino.Any(d => d.DelegacionId == id.Value))));

    public static bool PuedeDescargar(Sintesis sintesis, int? delegacionId) =>
        ParaDelegacion(delegacionId).Compile()(sintesis);

    public static bool PuedeVerDetalle(Sintesis sintesis, bool esDelegacion) =>
        !esDelegacion || sintesis.DelegacionId.HasValue;
}
