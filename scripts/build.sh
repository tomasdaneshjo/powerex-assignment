#!/usr/bin/env bash
# Builds dist/lambda.zip, the package Terraform deploys.
set -euo pipefail
cd "$(dirname "$0")/.."

rm -rf dist && mkdir -p dist/build
python3 -m pip install --quiet --target dist/build -r requirements.txt \
  --platform manylinux2014_aarch64 --implementation cp \
  --python-version 3.13 --only-binary=:all:

cp -R src/powerex_scraper dist/build/
(cd dist/build && zip -qr ../lambda.zip . -x '*__pycache__*')
rm -rf dist/build

echo "OK: dist/lambda.zip ($(du -h dist/lambda.zip | cut -f1))"
