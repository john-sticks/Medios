#!/usr/bin/env python3
"""Agente Keryx: genera un respaldo local y lo envía cifrado al NAS por HTTPS."""
import argparse,contextlib,datetime,gzip,hashlib,hmac,json,os,pathlib,shutil,ssl,subprocess,sys,tarfile,tempfile,urllib.request,urllib.error,fcntl

class SinRedireccion(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args,**kwargs):raise RuntimeError('El receptor intentó redirigir la conexión')

def enviar(config,path,data=None,content_type='application/json',headers=None):
    endpoint=config['receiver'].rstrip('/')+'/api/testbackup/'+path
    if not endpoint.startswith('https://'):raise RuntimeError('El receptor debe usar HTTPS')
    req=urllib.request.Request(endpoint,data=data,headers={'Authorization':'Bearer '+config['token'],'Content-Type':content_type,**(headers or {})})
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}),SinRedireccion(),urllib.request.HTTPSHandler(context=ssl.create_default_context()))
    with opener.open(req,timeout=120) as response:return json.loads(response.read() or b'{}')

def empaquetar(config,work):
    source=pathlib.Path(config.get('source','/opt/medios')).resolve()
    if source!=pathlib.Path('/opt/medios'):raise RuntimeError('Directorio de origen no permitido')
    if config.get('database')!='medios':raise RuntimeError('Base de origen no permitida')
    dump=work/'database.sql';start=datetime.datetime.now(datetime.timezone.utc)
    command=['mysqldump','--single-transaction','--quick','--no-tablespaces','--hex-blob','--skip-comments','--default-character-set=utf8mb4']
    options=subprocess.run(['mysqldump','--help'],capture_output=True,timeout=30).stdout
    if b'set-gtid-purged' in options:command.append('--set-gtid-purged=OFF')
    if b'column-statistics' in options:command.append('--column-statistics=0')
    with dump.open('wb') as out:
        result=subprocess.run(command+['medios'],stdout=out,stderr=subprocess.PIPE,timeout=1200)
    if result.returncode or dump.stat().st_size==0:raise RuntimeError('No se pudo generar el dump MySQL')
    manifest={'format':1,'database':'medios','startedUtc':start.isoformat(),'finishedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'includes':['database.sql','pdf','uploads']}
    (work/'manifest.json').write_text(json.dumps(manifest))
    archive=work/'backup.tar.gz'
    with tarfile.open(archive,'w:gz',compresslevel=6) as tar:
        tar.add(dump,arcname='database.sql',recursive=False);tar.add(work/'manifest.json',arcname='manifest.json',recursive=False)
        mounted=subprocess.run(['docker','inspect','medios','--format','{{json .Mounts}}'],capture_output=True,text=True,check=True,timeout=30)
        mounts={m['Destination']:pathlib.Path(m['Source']).resolve() for m in json.loads(mounted.stdout) if m.get('Source')}
        for name,destination in [('pdf','/app/wwwroot/pdf'),('uploads','/app/wwwroot/uploads')]:
            base=mounts.get(destination)
            if base is None or not base.is_dir() or not base.is_relative_to(source):raise RuntimeError('No se encontró el volumen persistente de Keryx: '+name)
            for current,dirs,files in os.walk(base,followlinks=False):
                dirs[:]=[d for d in dirs if not (pathlib.Path(current)/d).is_symlink()]
                for filename in files:
                    item=pathlib.Path(current)/filename
                    if item.is_symlink() or not item.is_file():continue
                    tar.add(item,arcname=name+'/'+item.relative_to(base).as_posix(),recursive=False)
    secret=work/'secret';secret.write_text(config['secret']);os.chmod(secret,0o600)
    encrypted=work/'ciphertext'
    subprocess.run(['openssl','enc',*(['-saltlen','8'] if b'-saltlen' in subprocess.run(['openssl','enc','-help'],capture_output=True).stderr else []),'-aes-256-cbc','-salt','-pbkdf2','-iter','200000','-md','sha256','-pass','file:'+str(secret),'-in',str(archive),'-out',str(encrypted)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.PIPE,timeout=1200)
    # OpenSSL 3.2 permite otro tamaño de sal; el receptor exige los 8 bytes históricos.
    with encrypted.open('rb') as src:
        if src.read(8)!=b'Salted__':raise RuntimeError('Formato de cifrado no reconocido')
    output=work/'respaldo.kbk';tag=hmac.new(hashlib.sha256((config['secret']+'|auth').encode()).digest(),digestmod=hashlib.sha256)
    with output.open('wb') as dst,encrypted.open('rb') as src:
        dst.write(b'KERYXBK1')
        while chunk:=src.read(1024*1024):tag.update(chunk);dst.write(chunk)
        dst.write(tag.digest())
    return output

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--config',default='/etc/keryx-backup/agent.json');parser.add_argument('--once',action='store_true');args=parser.parse_args()
    os.umask(0o077)
    if os.geteuid()!=0:raise RuntimeError('El agente debe ejecutarse con la cuenta operativa configurada para mysqldump')
    config_path=pathlib.Path(args.config)
    if config_path.stat().st_mode & 0o077:raise RuntimeError('La configuración del agente debe tener permisos 600')
    config=json.loads(config_path.read_text())
    root=pathlib.Path('/opt/medios/backups/agente');root.mkdir(parents=True,exist_ok=True)
    with (root/'agent.lock').open('w') as lock:
        try:fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
        except BlockingIOError:return
        plan=enviar(config,'plan');request=plan.get('requestId')
        if not request:return
        if not isinstance(request,str) or len(request)!=32 or any(c not in '0123456789abcdef' for c in request):raise RuntimeError('Solicitud inválida')
        try:
            with tempfile.TemporaryDirectory(prefix='job-',dir=root) as temp:
                backup=empaquetar(config,pathlib.Path(temp))
                if backup.stat().st_size>plan['maxBytes']:raise RuntimeError('El respaldo excede el límite del receptor')
                chunk_size=8*1024*1024;parts=(backup.stat().st_size+chunk_size-1)//chunk_size
                with backup.open('rb') as src:
                    for index in range(parts):
                        enviar(config,f'chunk/{request}/{index}',src.read(chunk_size),'application/octet-stream',{'X-Backup-Parts':str(parts)})
            print('Respaldo enviado y validado por el NAS.')
        except Exception:
            with contextlib.suppress(Exception):enviar(config,'error',b'{}')
            raise
if __name__=='__main__':
    try:main()
    except Exception as error:
        print('Agente Keryx: operación fallida ('+type(error).__name__+'). Revisar conectividad, mysqldump y configuración privada.',file=sys.stderr);sys.exit(1)
