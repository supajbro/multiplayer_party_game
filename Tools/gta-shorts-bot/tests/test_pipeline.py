import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch, MagicMock

from shorts.core import Settings, Store, digest
from shorts.rights import Rights, import_file, check_source
from shorts.pipeline import select
from shorts import youtube, discovery


def rights(**overrides):
    data = dict(source_id="local:test", creator="Test", basis="owned",
                evidence="Test fixture created locally", commercial_use=True, editing=True,
                youtube_distribution=True, third_party_audio_cleared=True, game_content_cleared=True,
                access_authorized=True, attribution="Test recording")
    data.update(overrides)
    return Rights(**data)


class RightsTests(unittest.TestCase):
    def test_each_required_permission_is_enforced(self):
        for name in ("commercial_use", "editing", "youtube_distribution", "third_party_audio_cleared",
                     "game_content_cleared", "access_authorized"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                rights(**{name: False}).check()

    def test_expired_permission_is_rejected(self):
        with self.assertRaises(ValueError):
            rights(expires="2020-01-01T00:00:00+00:00").check()

    def test_import_deduplicates_and_detects_tampering(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            cfg = Settings(data_dir=folder)
            store = Store(cfg)
            source = folder / "recording.mp4"
            source.write_bytes(b"fixture, not actual video")
            import_file(cfg, store, source, rights())
            with self.assertRaises(ValueError):
                import_file(cfg, store, source, rights(source_id="local:copy"))
            row = store.db.execute("SELECT * FROM sources").fetchone()
            check_source(row)
            Path(row["path"]).write_bytes(b"changed")
            with self.assertRaises(ValueError):
                check_source(row)
            store.db.close()


class SelectionTests(unittest.TestCase):
    def test_distinct_sources_and_strongest_payoff_last(self):
        candidates = [{"source_id": str(i), "moment": {"score": 10-i,
                      "fingerprint": [h]*3}} for i, h in enumerate(
                          ("0000000000000000", "ffffffffffffffff", "aaaaaaaaaaaaaaaa"))]
        result = select(candidates, [])
        self.assertEqual(result[-1]["moment"]["score"], 10)
        self.assertEqual(select(candidates, [["0000000000000000"]*3]), [])

    def test_does_not_select_same_moment_from_different_source(self):
        candidates = [{"source_id": str(i), "moment": {"score": 10,
                      "fingerprint": ["0000000000000000"]*3}} for i in range(4)]
        self.assertEqual(select(candidates, []), [])


class UploadTests(unittest.TestCase):
    def _rendered_job(self, folder):
        cfg = Settings(data_dir=folder, upload_enabled=True, expected_channel_id="fixture")
        store = Store(cfg)
        output = folder / "rendered.mp4"
        output.write_bytes(b"mocked media")
        manifest = {"source_ids": [], "output": str(output), "output_sha256": digest(output),
                    "script": {"title": "Test title", "description": "Test description"}, "credits": []}
        store.db.execute("INSERT INTO productions VALUES('p','2020-01-01','rendered',?,NULL,NULL,'now')",
                         (json.dumps(manifest),))
        store.db.commit()
        return cfg, store

    def test_upload_private_and_confirmed_id_is_idempotent(self):
        with tempfile.TemporaryDirectory() as tmp:
            cfg, store = self._rendered_job(Path(tmp))
            client = MagicMock()
            client.videos().insert().next_chunk.return_value = (None, {"id": "remote-id"})
            with patch("shorts.youtube.service", return_value=client), \
                 patch("shorts.youtube.channel_id", return_value="fixture"), \
                 patch("shorts.youtube.media.quality_check"), \
                 patch("googleapiclient.http.MediaFileUpload"):
                self.assertEqual(youtube.upload(cfg, store, "p"), "remote-id")
                body = client.videos().insert.call_args.kwargs["body"]
                self.assertEqual(body["status"]["privacyStatus"], "private")
                client.reset_mock()
                self.assertEqual(youtube.upload(cfg, store, "p"), "remote-id")
                client.videos.assert_not_called()
            store.db.close()

    def test_remote_failure_is_durable_and_blocks_retry(self):
        with tempfile.TemporaryDirectory() as tmp:
            cfg, store = self._rendered_job(Path(tmp))
            client = MagicMock()
            client.videos().insert().next_chunk.side_effect = ConnectionError("lost response")
            with patch("shorts.youtube.service", return_value=client), \
                 patch("shorts.youtube.channel_id", return_value="fixture"), \
                 patch("shorts.youtube.media.quality_check"), \
                 patch("googleapiclient.http.MediaFileUpload"):
                with self.assertRaises(RuntimeError):
                    youtube.upload(cfg, store, "p")
                self.assertEqual(store.db.execute("SELECT status FROM productions").fetchone()[0], "upload_uncertain")
                with self.assertRaises(ValueError):
                    youtube.upload(cfg, store, "p")
            store.db.close()

    def test_wrong_channel_cannot_start_remote_insert(self):
        with tempfile.TemporaryDirectory() as tmp:
            cfg, store = self._rendered_job(Path(tmp))
            client = MagicMock()
            with patch("shorts.youtube.service", return_value=client), \
                 patch("shorts.youtube.channel_id", return_value="wrong-channel"), \
                 patch("shorts.youtube.media.quality_check"):
                with self.assertRaisesRegex(ValueError, "does not match"):
                    youtube.upload(cfg, store, "p")
                client.videos.assert_not_called()
                self.assertEqual(store.db.execute("SELECT status FROM productions").fetchone()[0], "rendered")
            store.db.close()

    def test_uncertain_job_cannot_start_another_upload(self):
        with tempfile.TemporaryDirectory() as tmp:
            cfg = Settings(data_dir=Path(tmp), upload_enabled=True, expected_channel_id="fixture")
            store = Store(cfg)
            store.db.execute("INSERT INTO productions VALUES('p','2020-01-01','upload_uncertain','{}',NULL,NULL,'now')")
            store.db.commit()
            with patch("shorts.youtube.service") as service:
                with self.assertRaisesRegex(ValueError, "Uncertain uploads"):
                    youtube.upload(cfg, store, "p")
                service.assert_not_called()
            store.db.close()

    def test_publication_requires_explicit_testing_flags(self):
        with self.assertRaises(ValueError):
            Settings(auto_publish=True)


class TwitchAuthTests(unittest.TestCase):
    def test_expired_user_token_refreshes_and_persists_rotation(self):
        with tempfile.TemporaryDirectory() as tmp:
            cfg = Settings(twitch_token=Path(tmp) / "token.json")
            invalid = MagicMock(status_code=401, ok=False)
            valid = MagicMock(ok=True)
            valid.json.return_value = {"client_id": "client", "user_id": "editor", "scopes": ["editor:manage:clips"]}
            refreshed = MagicMock(ok=True)
            refreshed.json.return_value = {"access_token": "new-token", "refresh_token": "new-refresh"}
            with patch.dict(os.environ, {"TWITCH_CLIENT_ID": "client", "TWITCH_EDITOR_ID": "editor",
                                          "TWITCH_USER_TOKEN": "old", "TWITCH_REFRESH_TOKEN": "refresh"}), \
                 patch("shorts.discovery.requests.get", side_effect=[invalid, valid]), \
                 patch("shorts.discovery.requests.post", return_value=refreshed):
                headers = discovery.twitch_headers(cfg, download=True)
                self.assertEqual(headers["Authorization"], "Bearer new-token")
                self.assertEqual(json.loads(cfg.twitch_token.read_text())["refresh_token"], "new-refresh")

    def test_wrong_editor_is_rejected_before_download(self):
        with tempfile.TemporaryDirectory() as tmp:
            cfg = Settings(twitch_token=Path(tmp) / "token.json")
            valid = MagicMock(ok=True, status_code=200)
            valid.json.return_value = {"client_id": "client", "user_id": "someone-else", "scopes": ["editor:manage:clips"]}
            with patch.dict(os.environ, {"TWITCH_CLIENT_ID": "client", "TWITCH_EDITOR_ID": "editor", "TWITCH_USER_TOKEN": "token"}), \
                 patch("shorts.discovery.requests.get", return_value=valid):
                with self.assertRaisesRegex(ValueError, "does not match"):
                    discovery.twitch_headers(cfg, download=True)


if __name__ == "__main__":
    unittest.main()
