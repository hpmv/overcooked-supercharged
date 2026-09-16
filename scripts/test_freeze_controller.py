import hashlib
import tempfile
import unittest
from pathlib import Path

import freeze_controller as freeze


class CaptureTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.source = self.root / 'controller'
        self.source.mkdir()
        (self.source / freeze.PROJECT).write_bytes(b'<Project />\n')
        (self.source / 'Program.cs').write_bytes(b'// original bytes\r\n')

    def test_captured_build_sources_survive_live_edits(self):
        sources = freeze.capture(self.source)
        (self.source / 'Program.cs').write_bytes(b'// changed live source\n')
        rows, tree = freeze.write_capture(self.root / 'candidate', sources)
        self.assertEqual((self.root / 'candidate/source/controller/Program.cs').read_bytes(), b'// original bytes\r\n')
        self.assertNotEqual(freeze.capture(self.source), sources)
        expected = ''.join(row['path'] + '\0' + hashlib.sha256(sources[row['path']]).hexdigest() + '\n' for row in rows)
        self.assertEqual(tree, hashlib.sha256(expected.encode()).hexdigest())

    def test_excludes_generated_and_noncompiled_sources(self):
        for folder in ('bin', 'obj', 'protocol-tests'):
            (self.source / folder).mkdir()
            (self.source / folder / 'Program.cs').write_text('uncompiled')
        self.assertEqual(set(freeze.capture(self.source)), {'controller/' + freeze.PROJECT, 'controller/Program.cs'})

    def test_new_nested_compiled_source_fails_explicitly(self):
        (self.source / 'feature').mkdir()
        (self.source / 'feature/Foo.cs').write_text('new compiled code')
        with self.assertRaisesRegex(ValueError, 'Unaccounted nested'):
            freeze.capture(self.source)

    def test_source_snapshot_cannot_be_overwritten(self):
        sources = freeze.capture(self.source)
        freeze.write_capture(self.root / 'candidate', sources)
        with self.assertRaises(FileExistsError):
            freeze.write_capture(self.root / 'candidate', sources)

    def test_missing_project_is_an_error(self):
        (self.source / freeze.PROJECT).unlink()
        with self.assertRaises(FileNotFoundError):
            freeze.capture(self.source)

    def test_freeze_rejects_non_artifact_destination_before_write(self):
        with self.assertRaisesRegex(ValueError, 'workspace artifacts'):
            freeze.freeze(self.root / 'outside-artifacts', 'test')
        self.assertFalse((self.root / 'outside-artifacts').exists())


if __name__ == '__main__':
    unittest.main()
