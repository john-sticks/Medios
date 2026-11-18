import hashlib,importlib.util,json,os,sys,tempfile,types,unittest
from pathlib import Path
from unittest.mock import patch
if os.name=='nt':sys.modules.setdefault('fcntl',types.ModuleType('fcntl'))
spec=importlib.util.spec_from_file_location('incremental_agent',Path(__file__).resolve().parents[1]/'scripts/keryx-backup-agent.py')
agent=importlib.util.module_from_spec(spec);spec.loader.exec_module(agent)

class IncrementalChecks(unittest.TestCase):
    def test_content_defined_chunks_survive_insertion(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'database.sql'
            original=b''.join(hashlib.sha256(i.to_bytes(4,'little')).digest() for i in range(100000))
            p.write_bytes(original);base=list(agent.bloques(p,True))
            p.write_bytes(original[:300000]+b'fila nueva\n'+original[300000:]);actual=list(agent.bloques(p,True))
            known={hashlib.sha256(b).hexdigest() for b in base}
            changed=sum(len(b) for b in actual if hashlib.sha256(b).hexdigest() not in known)
            self.assertEqual(b''.join(actual),p.read_bytes())
            self.assertLess(changed,len(original)//2)
            self.assertTrue(all(0<len(b)<=agent.MAX_BLOQUE for b in actual))

    def test_only_new_blocks_are_uploaded_and_deletions_are_authoritative(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d);sql=p/'db';keep=p/'keep';new=p/'new'
            sql.write_bytes(b'SQL actualizado');keep.write_bytes(b'archivo conservado');new.write_bytes(b'archivo nuevo')
            known=hashlib.sha256(keep.read_bytes()).hexdigest();calls=[]
            def enviar(config,url,data=None,*args):
                calls.append((url,data))
                if url.startswith('incremental/base/'):
                    return {'baseId':'a'*64,'hashes':[known],'maxObjectBytes':agent.MAX_BLOQUE}
                return {'accepted':True}
            files={'database.sql':sql,'manifest.json':sql,'pdf/conservado.pdf':keep,'uploads/nuevo.png':new}
            with patch.object(agent,'preparar_archivos',return_value=files),patch.object(agent,'enviar',side_effect=enviar),patch.object(agent,'esperar_incremental',return_value={'status':'confirmed'}) as confirmed:
                agent.transferir_incremental({},p,'b'*32,{'maxBytes':2147483648})
            uploads=[data for url,data in calls if '/object/' in url]
            self.assertEqual(len(uploads),2) # SQL y manifiesto iguales se deduplican.
            self.assertNotIn(keep.read_bytes(),uploads)
            manifest=json.loads(next(data for url,data in calls if url.endswith('/finalizar')))
            self.assertEqual({f['path'] for f in manifest['files']},set(files))
            self.assertNotIn('uploads/eliminado.png',{f['path'] for f in manifest['files']})
            confirmed.assert_called_once()

    def test_processing_resumes_confirmation_without_resending_data(self):
        with patch.object(agent,'preparar_archivos') as snapshot,patch.object(agent,'esperar_incremental',return_value={'status':'confirmed'}) as wait:
            agent.transferir_incremental({},None,'b'*32,{'processing':True})
            snapshot.assert_not_called();wait.assert_called_once()

    def test_bad_base_is_rejected_without_upload(self):
        with patch.object(agent,'enviar',return_value={'baseId':'../otro','hashes':[]}),patch.object(agent,'preparar_archivos') as snapshot:
            with self.assertRaises(agent.ErrorReceptor):agent.transferir_incremental({},None,'b'*32,{})
            snapshot.assert_not_called()

    def test_validation_failure_is_not_confirmed(self):
        with patch.object(agent,'enviar',return_value={'status':'error'}):
            with self.assertRaises(agent.ErrorReceptor):agent.esperar_incremental({},'b'*32)

if __name__=='__main__':unittest.main()
