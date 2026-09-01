#!/usr/bin/env bash
# Builds the Lambda deployment package Terraform consumes.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet tool restore
rm -rf dist && mkdir -p dist
dotnet lambda package \
  --project-location src/PowerexScraper \
  --configuration Release \
  --function-architecture arm64 \
  --output-package "$(pwd)/dist/lambda.zip"

# the config file must ship inside the zip — fail loudly if it doesn't
# (captured to a variable rather than piped directly into grep -q: under
# `pipefail`, grep -q exits as soon as it matches, which can SIGPIPE unzip
# before it finishes writing the rest of the listing and fail the pipeline
# even though the match was found)
listing="$(unzip -l dist/lambda.zip)"
grep -q "endpoints.json" <<<"$listing" || { echo "ERROR: endpoints.json missing from package"; exit 1; }
grep -q "PowerexScraper.dll" <<<"$listing" || { echo "ERROR: PowerexScraper.dll missing from package"; exit 1; }
echo "OK: dist/lambda.zip"
