import copy
import json
from pathlib import Path
import unittest

from lock_reference import LOCKS, add_lock_references


class LockReferenceTests(unittest.TestCase):
    def test_refresh_preserves_unrelated_entries_and_is_idempotent(self):
        existing = {'id': 4844, 'script': 'draw(2);'}
        entries = [existing, {'id': 5594, 'script': 'stale'}]
        add_lock_references(entries, {})
        first = copy.deepcopy(entries)
        add_lock_references(entries, {})
        self.assertEqual(entries, first)
        self.assertIn(existing, entries)
        self.assertEqual(len(entries), len(LOCKS) + 1)

    def test_shipped_templates_match_generator_and_preserve_caveats(self):
        path = Path(__file__).resolve().parents[2] / 'Tools/WolfX/Content/effect_reference.json'
        shipped = {entry['id']: entry for entry in json.loads(path.read_text(encoding='utf-8'))['cards']}
        generated = []
        add_lock_references(generated, {})
        for entry in generated:
            self.assertEqual(shipped[entry['id']], entry)
            self.assertFalse(entry['sourceOk'])
            self.assertEqual(entry['complete'], entry['id'] == 5594)
            self.assertEqual(bool(entry['notes']), not entry['complete'])
        self.assertEqual(shipped[5594]['script'], 'lock(spell);')

    def test_catalog_supplies_original_text(self):
        entries = []
        add_lock_references(entries, {5594: {'description': 'Original text', 'kind': 'Effect'}})
        entry = next(entry for entry in entries if entry['id'] == 5594)
        self.assertEqual(entry['text'], 'Original text')


if __name__ == '__main__':
    unittest.main()
