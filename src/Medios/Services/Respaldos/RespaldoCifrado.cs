using System.Security.Cryptography;
using System.Text;

namespace Medios.Services.Respaldos;

public static class RespaldoCifrado
{
    public static async Task VerificarAsync(string archivo,string clave,CancellationToken ct=default)
    {
        await using var stream=File.OpenRead(archivo);
        if(stream.Length<72)throw new InvalidOperationException("Respaldo incompleto.");
        var magic=new byte[8];await stream.ReadExactlyAsync(magic,ct);
        if(!magic.AsSpan().SequenceEqual("KERYXBK1"u8))throw new InvalidOperationException("Formato de respaldo inválido.");
        using var hmac=new HMACSHA256(SHA256.HashData(Encoding.UTF8.GetBytes(clave+"|auth")));
        var buffer=new byte[81920];long rest=stream.Length-40;
        while(rest>0){var n=await stream.ReadAsync(buffer.AsMemory(0,(int)Math.Min(rest,buffer.Length)),ct);if(n==0)throw new EndOfStreamException();hmac.TransformBlock(buffer,0,n,null,0);rest-=n;}
        hmac.TransformFinalBlock([],0,0);var tag=new byte[32];await stream.ReadExactlyAsync(tag,ct);
        if(!CryptographicOperations.FixedTimeEquals(tag,hmac.Hash!))throw new InvalidOperationException("El respaldo no supera la verificación de integridad.");
    }
    public static async Task DescifrarAsync(string archivo,string destino,string clave,CancellationToken ct)
    {
        await VerificarAsync(archivo,clave,ct);
        await using var input=File.OpenRead(archivo);input.Position=8;
        var header=new byte[16];await input.ReadExactlyAsync(header,ct);
        if(!header.AsSpan(0,8).SequenceEqual("Salted__"u8))throw new InvalidOperationException("Cifrado incompatible.");
        var derived=Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(clave),header.AsSpan(8),200000,HashAlgorithmName.SHA256,48);
        using var aes=Aes.Create();aes.Key=derived[..32];aes.IV=derived[32..];aes.Mode=CipherMode.CBC;aes.Padding=PaddingMode.PKCS7;
        await using var bounded=new Limitado(input,input.Length-56);
        await using var crypto=new CryptoStream(bounded,aes.CreateDecryptor(),CryptoStreamMode.Read);
        await using var output=new FileStream(destino,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        RespaldoStore.Privado(destino);await crypto.CopyToAsync(output,ct);
    }
    public static async Task CifrarAsync(string origen,string destino,string clave,CancellationToken ct=default)
    {
        var salt=RandomNumberGenerator.GetBytes(8);
        var key=Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(clave),salt,200000,HashAlgorithmName.SHA256,48);
        var temp=destino+".partial";
        try
        {
            await using(var output=new FileStream(temp,FileMode.Create))
            {
                RespaldoStore.Privado(temp);await output.WriteAsync("KERYXBK1"u8.ToArray(),ct);await output.WriteAsync("Salted__"u8.ToArray(),ct);await output.WriteAsync(salt,ct);
                using var aes=Aes.Create();aes.Key=key[..32];aes.IV=key[32..];
                await using var crypto=new CryptoStream(output,aes.CreateEncryptor(),CryptoStreamMode.Write,true);
                await using var input=File.OpenRead(origen);await input.CopyToAsync(crypto,ct);await crypto.FlushFinalBlockAsync(ct);
            }
            using var hmac=new HMACSHA256(SHA256.HashData(Encoding.UTF8.GetBytes(clave+"|auth")));
            await using(var input=File.OpenRead(temp))
            {
                input.Position=8;var buffer=new byte[81920];int n;
                while((n=await input.ReadAsync(buffer,ct))>0)hmac.TransformBlock(buffer,0,n,null,0);
                hmac.TransformFinalBlock([],0,0);
            }
            await using(var output=new FileStream(temp,FileMode.Append))await output.WriteAsync(hmac.Hash!,ct);
            File.Move(temp,destino,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    sealed class Limitado(Stream inner,long remaining):Stream
    {
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] b,int o,int c){var n=inner.Read(b,o,(int)Math.Min(c,remaining));remaining-=n;return n;}
        public override async ValueTask<int> ReadAsync(Memory<byte> b,CancellationToken ct=default){var n=await inner.ReadAsync(b[..(int)Math.Min(b.Length,remaining)],ct);remaining-=n;return n;}
        public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }
}
public class CopiaUso
{
    public SemaphoreSlim Operacion { get; } = new(1, 1);
    readonly object gate=new();int active;bool blocked;
    public bool Entrar(){lock(gate){if(blocked)return false;active++;return true;}}
    public void Salir(){lock(gate)active--;}
    public void Bloquear(){lock(gate)blocked=true;}
    public void Liberar(){lock(gate)blocked=false;}
    public int Activas{get{lock(gate)return active;}}
}
