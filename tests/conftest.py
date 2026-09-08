import json
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).parent.parent / "src"))

FIXTURES = Path(__file__).parent / "fixtures"


@pytest.fixture
def load_fixture_bytes():
    def _load(name: str) -> bytes:
        return (FIXTURES / name).read_bytes()
    return _load


class MemoryStore:
    def __init__(self):
        self.objects: dict[str, bytes] = {}

    def put(self, key: str, content: bytes, content_type: str) -> None:
        self.objects[key] = content


@pytest.fixture
def memory_store():
    return MemoryStore()
