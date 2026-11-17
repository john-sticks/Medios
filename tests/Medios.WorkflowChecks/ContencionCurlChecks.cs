using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Medios.Services.Respaldos;

internal static class ContencionCurlChecks
{
    public static async Task<int> RunAsync()
    {
        if (OperatingSystem.IsWindows()) return 0;
        var root = Directory.CreateTempSubdirectory("keryx-curl-checks-");
        var anterior = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var curl = Path.Combine(root.FullName, "curl");
            await File.WriteAllTextAsync(curl, """
                #!/usr/bin/env python3
                import json, sys, time
                args = sys.argv[1:]
                assert '--noproxy' in args and args[args.index('--noproxy')+1] == '*'
                assert '--proto' in args and args[args.index('--proto')+1] == '=https'
                assert '--insecure' not in args and '-k' not in args and '--location' not in args
                token = 'A' * 64
                assert token not in ' '.join(args)
                config = sys.stdin.read()
                assert r'\u0022' not in config
                lines = [line.split(' = ', 1) for line in config.splitlines()]
                headers = [json.loads(value) for key, value in lines if key == 'header']
                assert 'Authorization: Bearer ' + token in headers
                if 'respaldo-previo' in args[-1]:
                    body = json.loads(next(json.loads(value) for key, value in lines if key == 'data'))
                    assert body == {'origenRespaldo': 'b'*32}
                    print('{"requestId":"' + 'b'*32 + '"}')
                elif 'solicitud=cancelar' in args[-1]:
                    time.sleep(30)
                else:
                    print('{"activa":true,"conocido":true}')
                print('200', end='')
                """ + "\n");
            File.SetUnixFileMode(curl, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable("PATH", root.FullName + Path.PathSeparator + anterior);
            using var client = new HttpClient(new ContencionCurlHandler());
            const string endpoint = "https://medios.crmai.net.ar/api/testbackup/";
            using var get = Request(HttpMethod.Get, endpoint + "contencion");
            using var response = await client.SendAsync(get);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (response.StatusCode != HttpStatusCode.OK || !json.RootElement.GetProperty("activa").GetBoolean())
                throw new InvalidOperationException("El transporte no devolvió el estado esperado.");

            using var post = Request(HttpMethod.Post, endpoint + "respaldo-previo");
            post.Content = new StringContent("{\"origenRespaldo\":\"" + new string('b',32) + "\"}");
            using var backup = await client.SendAsync(post);
            backup.EnsureSuccessStatusCode();

            using var invalid = Request(HttpMethod.Get, "https://otro.example/api/testbackup/contencion");
            try { await client.SendAsync(invalid); throw new InvalidOperationException("Se permitió otro destino."); }
            catch (HttpRequestException) { }

            using var ct = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            using var cancel = Request(HttpMethod.Get, endpoint + "contencion?solicitud=cancelar");
            try { await client.SendAsync(cancel, ct.Token); throw new InvalidOperationException("No se canceló curl."); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            return 4;
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", anterior);
            root.Delete(true);
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method,url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",new string('A',64));
        return request;
    }
}
