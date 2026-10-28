import unittest
from datetime import datetime, timezone

from prune_images import plan

NOW = datetime(2026, 10, 28, tzinfo=timezone.utc)
OLD = "2026-09-01T00:00:00Z"
RECENT = "2026-10-27T00:00:00Z"


def version(digest, tags=(), created=OLD):
    return {"id": digest, "name": digest, "created_at": created, "metadata": {"container": {"tags": list(tags)}}}


class PlanTests(unittest.TestCase):
    def deleted(self, versions, references):
        _, delete = plan(versions, lambda digest: references.get(digest, []), NOW, days=14)
        return sorted(v["name"] for v in delete)

    def test_a_release_keeps_its_platform_manifests_and_signatures(self):
        versions = [
            version("sha256:index", ["1.0.0", "abc123"]),
            version("sha256:amd64"),
            version("sha256:arm64"),
            version("sha256:sig", ["sha256-index.sig"]),
            version("sha256:att", ["sha256-index.att"]),
        ]
        self.assertEqual(self.deleted(versions, {"sha256:index": ["sha256:amd64", "sha256:arm64"]}), [])

    def test_an_old_commit_build_goes_with_everything_attached_to_it(self):
        versions = [
            version("sha256:index", ["def456"]),
            version("sha256:amd64"),
            version("sha256:sig", ["sha256-index.sig"]),
        ]
        references = {"sha256:index": ["sha256:amd64"]}
        self.assertEqual(self.deleted(versions, references), ["sha256:amd64", "sha256:index", "sha256:sig"])

    def test_recent_builds_and_their_children_are_kept(self):
        versions = [version("sha256:index", ["def456"], RECENT), version("sha256:amd64")]
        self.assertEqual(self.deleted(versions, {"sha256:index": ["sha256:amd64"]}), [])

    def test_orphaned_untagged_manifests_are_deleted(self):
        self.assertEqual(self.deleted([version("sha256:orphan")], {}), ["sha256:orphan"])


if __name__ == "__main__":
    unittest.main()
