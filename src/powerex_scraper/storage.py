"""Object stores: S3 for the Lambda, local filesystem for the CLI."""
from __future__ import annotations

from pathlib import Path
from typing import Protocol


class Store(Protocol):
    def put(self, key: str, content: bytes, content_type: str) -> None: ...


class FileStore:
    def __init__(self, root: str | Path):
        self.root = Path(root)

    def put(self, key: str, content: bytes, content_type: str) -> None:
        path = self.root.joinpath(*key.split("/"))
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)


class S3Store:
    def __init__(self, bucket: str, s3_client=None):
        if s3_client is None:  # boto3 ships in the Lambda runtime; import lazily
            import boto3
            s3_client = boto3.client("s3")
        self.bucket = bucket
        self.s3 = s3_client

    def put(self, key: str, content: bytes, content_type: str) -> None:
        self.s3.put_object(Bucket=self.bucket, Key=key,
                           Body=content, ContentType=content_type)
