using Medios.Entities;
using Medios.Infrastructure.Data;
using Medios.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;

namespace Medios.Services.Respaldos;

public class AccesoEmergenciaService(RespaldoStore store,MediosDbContextFactory factory)
{
    readonly PasswordHasher<string> hasher=new();
    public async Task<List<AutorizacionUsuario>> UsuariosAsync()
    {
        var state=await store.LeerAsync();if(!state.CopiaLista || state.Restaurando)return [];
        using var db=factory.CreateCopy();
        var users=await db.AutorizacionesUsuario.AsNoTracking().OrderByDescending(a=>a.FechaSolicitud).ThenByDescending(a=>a.Id).ToListAsync();
        return users.GroupBy(a=>a.Usuario,StringComparer.OrdinalIgnoreCase).Select(g=>g.First())
            .Where(AutorizacionLocalPolicy.PermiteAcceso).OrderBy(a=>a.Usuario).ToList();
    }
    public async Task<AutorizacionUsuario?> UsuarioAsync(string user)
    {
        using var db=factory.CreateCopy();
        var result=await db.AutorizacionesUsuario.AsNoTracking().Where(a=>a.Usuario==user)
            .OrderByDescending(a=>a.FechaSolicitud).ThenByDescending(a=>a.Id).FirstOrDefaultAsync();
        if(result!=null)result.Rol=result.Rol?.Trim().ToUpperInvariant();
        return result;
    }
    public async Task<string> PrepararAsync(string usuario,string actor)
    {
        var state=await store.LeerAsync();if(!state.CopiaLista || state.Restaurando)throw new InvalidOperationException("Restaurá primero una copia productiva.");
        var user=await UsuarioAsync(usuario);
        if(!AutorizacionLocalPolicy.PermiteAcceso(user))throw new InvalidOperationException("El usuario no está aprobado y activo en la copia.");
        var password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        await store.Cambiar(s=>{
            s.Accesos.RemoveAll(a=>a.Usuario.Equals(user!.Usuario,StringComparison.OrdinalIgnoreCase));
            s.Accesos.Add(new(){Usuario=user!.Usuario,Hash=hasher.HashPassword(user.Usuario,password),HastaUtc=DateTimeOffset.UtcNow.AddDays(30)});
            RespaldoStore.Evento(s,actor,"Credencial de emergencia preparada: "+user.Usuario);return true;});
        return password;
    }
    public async Task<AutorizacionUsuario?> ValidarAsync(string usuario,string password)
    {
        if(string.IsNullOrWhiteSpace(usuario) || usuario.Length>100 || password.Length>256)return null;
        var ok=await store.Cambiar(s=>{
            if(!s.ContingenciaActiva || !s.CopiaLista || s.Restaurando)return false;
            var a=s.Accesos.SingleOrDefault(a=>a.Usuario.Equals(usuario,StringComparison.OrdinalIgnoreCase));
            if(a==null || a.HastaUtc<=DateTimeOffset.UtcNow || a.BloqueadoHastaUtc>DateTimeOffset.UtcNow)return false;
            if(hasher.VerifyHashedPassword(a.Usuario,a.Hash,password)==PasswordVerificationResult.Failed)
            {a.Intentos++;if(a.Intentos>=5){a.BloqueadoHastaUtc=DateTimeOffset.UtcNow.AddMinutes(15);a.Intentos=0;}return false;}
            a.Intentos=0;return true;});
        if(!ok)return null;
        var user=await UsuarioAsync(usuario);
        return AutorizacionLocalPolicy.PermiteAcceso(user) && (user!.Rol!="DELEGACION" || user.DelegacionId.HasValue) ? user : null;
    }
    public async Task<bool> SesionValidaAsync(ClaimsPrincipal user,RespaldoEstado state)
    {
        if(!state.ContingenciaActiva || !state.CopiaLista || state.Restaurando || user.FindFirst("BackupEmergencyEpoch")?.Value!=state.EpocaContingencia)return false;
        var login=user.FindFirst("UsuarioReal")?.Value??"";
        var access=state.Accesos.SingleOrDefault(a=>a.Usuario.Equals(login,StringComparison.OrdinalIgnoreCase));
        if(access==null || access.HastaUtc<=DateTimeOffset.UtcNow || user.FindFirst("BackupAccessRevision")?.Value!=access.Revision)return false;
        return AutorizacionLocalPolicy.SesionVigente(user,await UsuarioAsync(login));
    }
    public async Task CambiarClaveAsync(string usuario,string password)
    {
        if(password.Length is <14 or >128)throw new InvalidOperationException("La clave debe tener entre 14 y 128 caracteres.");
        await store.Cambiar(s=>{
            var a=s.Accesos.Single(x=>x.Usuario.Equals(usuario,StringComparison.OrdinalIgnoreCase));
            a.Hash=hasher.HashPassword(a.Usuario,password);a.CambiarClave=false;a.Revision=Guid.NewGuid().ToString("N");
            RespaldoStore.Evento(s,usuario,"Cambio de clave de emergencia");return true;});
    }
}
