import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import types
import unittest
from unittest.mock import patch

# fcntl se usa en Linux para el bloqueo operativo; las pruebas de transporte
# también pueden ejecutarse en el equipo Windows de desarrollo.
if os.name == "nt":
    sys.modules.setdefault("fcntl", types.ModuleType("fcntl"))
spec = importlib.util.spec_from_file_location("backup_agent", Path(__file__).resolve().parents[1] / "scripts/keryx-backup-agent.py")
agent = importlib.util.module_from_spec(spec)
spec.loader.exec_module(agent)


class TransportChecks(unittest.TestCase):
    config = {"receiver": "https://example.invalid", "token": "token-ficticio-prueba"}

    def reply(self, status="200", body=b'{"requestId":null}', code=0, payload=None):
        def run(args, **kwargs):
            self.assertEqual(args[:2], ["curl", "--disable"])
            self.assertNotIn(self.config["token"], " ".join(args))
            self.assertIn("Authorization: Bearer " + self.config["token"], kwargs["input"])
            self.assertEqual(args[args.index("--proto")+1], "=https")
            self.assertEqual(args[args.index("--noproxy")+1], "*")
            self.assertNotIn("--insecure", args)
            self.assertNotIn("--location", args)
            if payload is not None:
                archivo = Path(args[args.index("--data-binary")+1][1:])
                self.assertEqual(archivo.read_bytes(), payload)
                if os.name != "nt": self.assertEqual(archivo.stat().st_mode & 0o777, 0o600)
                self.assertEqual(args[args.index("--request")+1], "POST")
                self.assertIn("X-Backup-Parts: 2", kwargs["input"])
            Path(args[args.index("--output")+1]).write_bytes(body)
            return subprocess.CompletedProcess(args, code, status, "dato privado que no debe registrarse")
        return run

    def test_plan_and_private_credentials(self):
        with patch.object(agent.subprocess, "run", side_effect=self.reply()):
            self.assertEqual(agent.enviar(self.config, "plan"), {"requestId": None})

    def test_binary_upload_is_not_reencoded(self):
        binary = bytes(range(256)) * 100
        with patch.object(agent.subprocess, "run", side_effect=self.reply(body=b'{"part":0}', payload=binary)):
            self.assertEqual(agent.enviar(self.config, "chunk/solicitud/0", binary, "application/octet-stream", {"X-Backup-Parts": "2"}), {"part": 0})

    def test_http_errors_are_safe_and_identifiable(self):
        for code in ["301", "403", "404", "413", "500"]:
            with self.subTest(code=code), patch.object(agent.subprocess, "run", side_effect=self.reply(status=code, body=b"RESPUESTA PRIVADA")):
                with self.assertRaises(agent.ErrorReceptor) as error: agent.enviar(self.config, "plan")
                self.assertIn("HTTP " + code, str(error.exception))
                self.assertNotIn("PRIVADA", str(error.exception))
                self.assertNotIn(self.config["token"], str(error.exception))

    def test_tls_failure_is_not_ignored(self):
        with patch.object(agent.subprocess, "run", side_effect=self.reply(code=60)):
            with self.assertRaisesRegex(agent.ErrorReceptor, "curl 60"): agent.enviar(self.config, "plan")

    def test_http_and_header_injection_are_rejected(self):
        for config in [{"receiver": "http://example.invalid", "token": "fake"}, {**self.config, "token": "fake\r\nX-Test: value"}]:
            with patch.object(agent.subprocess, "run") as run:
                with self.assertRaises(agent.ErrorReceptor): agent.enviar(config, "plan")
                run.assert_not_called()

    def test_timeout_does_not_expose_process_input(self):
        with patch.object(agent.subprocess, "run", side_effect=subprocess.TimeoutExpired("curl", 135)):
            with self.assertRaises(agent.ErrorReceptor) as error: agent.enviar(self.config, "plan")
            self.assertNotIn(self.config["token"], str(error.exception))


if __name__ == "__main__": unittest.main()
